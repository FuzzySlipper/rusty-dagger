using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Durable, staged allocation for one classic level, before its permanent mutations are applied.</summary>
internal sealed record DaggerfallLevelUpSave(
    int Level,
    int BonusPool,
    int HealthGain,
    DaggerfallLevelUpAttributeSave[] Allocations)
{
    internal DaggerfallLevelUpSave Validate()
    {
        if (Level < 2 || BonusPool is < 4 or > 6 || HealthGain < 1)
            throw new ArgumentOutOfRangeException(nameof(Level), "Saved Daggerfall level-up values are outside the classic bounds.");
        ArgumentNullException.ThrowIfNull(Allocations);
        HashSet<string> attributes = new(StringComparer.Ordinal);
        int allocated = 0;
        foreach (DaggerfallLevelUpAttributeSave allocation in Allocations)
        {
            ArgumentNullException.ThrowIfNull(allocation);
            if (string.IsNullOrWhiteSpace(allocation.Attribute) || allocation.Points < 1 || !attributes.Add(allocation.Attribute))
                throw new ArgumentException("Saved Daggerfall level-up allocations must name distinct attributes with positive points.", nameof(Allocations));
            allocated = checked(allocated + allocation.Points);
        }
        if (allocated > BonusPool)
            throw new ArgumentException("Saved Daggerfall level-up allocations exceed their bonus pool.", nameof(Allocations));
        return this;
    }
}

internal sealed record DaggerfallLevelUpAttributeSave(string Attribute, int Points);

internal sealed record DaggerfallLevelUpAttributePresentation(string Id, string Label, long Permanent, long Live, int Pending, bool CanAllocate);
internal sealed record DaggerfallLevelUpPresentation(int Level, int BonusPool, int RemainingPoints, int HealthGain,
    bool CanCommit, DaggerfallLevelUpAttributePresentation[] Attributes);

/// <summary>
/// Daggerfall level-up policy over the existing skill-eligibility and Mechanics owners.  The
/// random results are resolved once when rest makes a level available, while attributes remain a
/// cancellable-in-practice draft until an explicit commit applies them.
/// </summary>
internal sealed class DaggerfallLevelUpState
{
    private const int MaximumAttribute = 100;
    private const int MinimumBonusPool = 4;
    private const int MaximumBonusPool = 6;
    private readonly ProgressionState _progression;
    private readonly DaggerfallSkillUseReactions _skills;
    private readonly StatsComponent _stats;
    private readonly DaggerfallStatId[] _attributes;
    private readonly Func<DaggerfallCareerDefinition> _career;
    private readonly IRandomService _random;
    private readonly DaggerfallRewardReactions _rewards;
    private DaggerfallLevelUpSave? _pending;

    internal DaggerfallLevelUpState(ProgressionState progression, DaggerfallSkillUseReactions skills, StatsComponent stats,
        DaggerfallDefinitions definitions, Func<DaggerfallCareerDefinition> career, IRandomService random, DaggerfallRewardReactions rewards)
    {
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _attributes = definitions?.Vocabulary.Attributes.ToArray() ?? throw new ArgumentNullException(nameof(definitions));
        _career = career ?? throw new ArgumentNullException(nameof(career));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
    }

    internal DaggerfallLevelUpSave? Pending => _pending;

    /// <summary>Resolves the donor's pool and health roll once after an ordinary rest skill check.</summary>
    internal bool BeginIfEligible()
    {
        if (_pending is not null || !_skills.PendingLevelUp) return false;
        int level = checked(_progression.Level + 1);
        int pool = Draw(level, "attribute-pool", MinimumBonusPool, MaximumBonusPool);
        int health = DaggerfallLevelUpHealthSource.RollGain(_random, _career(), Permanent(DaggerfallMechanicsIds.Endurance), level);
        _pending = new DaggerfallLevelUpSave(level, pool, health, []).Validate();
        return true;
    }

    internal void Allocate(string attribute)
    {
        DaggerfallLevelUpSave pending = RequirePending();
        DaggerfallStatId id = _attributes.SingleOrDefault(candidate => candidate.Value == attribute);
        if (id.Value is null) throw new ArgumentException($"'{attribute}' is not a Daggerfall attribute.", nameof(attribute));
        if (Remaining(pending) == 0) throw new ArgumentException("All level-up bonus points have already been allocated.", nameof(attribute));
        if (Permanent(id) + Points(pending, id.Value) >= MaximumAttribute)
            throw new ArgumentException($"{id.Value} is already at the maximum attribute value.", nameof(attribute));
        DaggerfallLevelUpAttributeSave[] allocations = [.. pending.Allocations.Where(item => item.Attribute != id.Value),
            new DaggerfallLevelUpAttributeSave(id.Value, checked(Points(pending, id.Value) + 1))];
        _pending = pending with { Allocations = allocations.OrderBy(item => item.Attribute, StringComparer.Ordinal).ToArray() };
    }

    internal void Commit()
    {
        DaggerfallLevelUpSave pending = RequirePending();
        if (pending.Level != _progression.Level + 1 || !_skills.PendingLevelUp)
            throw new ArgumentException("This level-up is no longer eligible to commit.");
        if (Remaining(pending) != 0 && !AllAttributesAtMaximum(pending))
            throw new ArgumentException("Allocate all available level-up bonus points before committing.");

        foreach (DaggerfallLevelUpAttributeSave allocation in pending.Allocations)
            DaggerfallStatModifiers.AdjustPermanent(_stats, new DaggerfallStatId(allocation.Attribute), allocation.Points);
        DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(_stats, _career());
        _rewards.CommitLevelUp(pending.Level, pending.HealthGain);
        _pending = null;
    }

    internal DaggerfallLevelUpSave? Capture() => _pending;

    internal void Restore(DaggerfallLevelUpSave? pending)
    {
        if (pending is null) { _pending = null; return; }
        pending = pending.Validate();
        if (pending.Level != _progression.Level + 1 || !_skills.PendingLevelUp)
            throw new ArgumentException("Saved Daggerfall level-up is not eligible against the restored progression state.", nameof(pending));
        foreach (DaggerfallLevelUpAttributeSave allocation in pending.Allocations)
        {
            if (!_attributes.Any(attribute => attribute.Value == allocation.Attribute))
                throw new ArgumentException($"Saved Daggerfall level-up names unknown attribute '{allocation.Attribute}'.", nameof(pending));
            if (Permanent(new DaggerfallStatId(allocation.Attribute)) + allocation.Points > MaximumAttribute)
                throw new ArgumentException($"Saved Daggerfall level-up exceeds the maximum for '{allocation.Attribute}'.", nameof(pending));
        }
        _pending = pending;
    }

    internal DaggerfallLevelUpPresentation? Read()
    {
        if (_pending is not { } pending) return null;
        return new(pending.Level, pending.BonusPool, Remaining(pending), pending.HealthGain,
            Remaining(pending) == 0 || AllAttributesAtMaximum(pending),
            _attributes.Select(id => new DaggerfallLevelUpAttributePresentation(id.Value, Label(id.Value), Permanent(id), Live(id),
                Points(pending, id.Value), Remaining(pending) > 0 && Permanent(id) + Points(pending, id.Value) < MaximumAttribute)).ToArray());
    }

    private DaggerfallLevelUpSave RequirePending() => _pending ?? throw new ArgumentException("No level-up allocation is pending.");
    private int Remaining(DaggerfallLevelUpSave pending) => checked(pending.BonusPool - pending.Allocations.Sum(item => item.Points));
    private bool AllAttributesAtMaximum(DaggerfallLevelUpSave pending) => _attributes.All(id => Permanent(id) + Points(pending, id.Value) >= MaximumAttribute);
    private int Points(DaggerfallLevelUpSave pending, string attribute) => pending.Allocations.SingleOrDefault(item => item.Attribute == attribute)?.Points ?? 0;
    private int Permanent(DaggerfallStatId attribute) => checked((int)_stats.GetStat(StatId.Parse(attribute.Value)).BaseValue);
    private long Live(DaggerfallStatId attribute) => _stats.GetStat(StatId.Parse(attribute.Value)).ValueInt64;

    private int Draw(int level, string roll, int minimum, int maximum)
    {
        int result = checked((int)_random.DrawKeyed(new KeyedRngRequest(
            CombatRandomKey.Seed, CombatRandomKey.PlayerScope, $"player.level-up.{level}.{roll}", minimum, maximum)).Value);
        if (result < minimum || result > maximum)
            throw new MechanicsException($"Daggerfall level-up {roll} for level {level} was outside [{minimum}, {maximum}].");
        return result;
    }

    private static string Label(string id) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('-', ' '));
}
