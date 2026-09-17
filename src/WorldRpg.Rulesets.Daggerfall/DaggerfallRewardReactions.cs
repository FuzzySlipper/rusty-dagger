using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Daggerfall-owned reward policy for defeated authored actors.</summary>
internal sealed class DaggerfallRewardReactions(ProgressionState progression, ActorMechanicsState playerMechanics, DaggerfallActorDefinition playerDefinition, IRandomService random, IReadOnlyDictionary<long, DaggerfallActorDefinition> actors)
{
    private readonly HashSet<long> _awarded = [];
    private readonly HashSet<long> _experienceAwarded = [];

    internal void React(ActorDiedFact fact, FactBuffer<IProductFact> facts)
    {
        if (fact.KillerId != DaggerfallActorIdentity.PlayerEntityId) return;
        if (_awarded.Contains(fact.ActorId) || !actors.TryGetValue(fact.ActorId, out DaggerfallActorDefinition? actor)) return;

        ProgressionAwardPlan? progressionPlan = PlanProgression(fact.ActorId, actor);
        if (progressionPlan is not null)
        {
            if (progressionPlan.HealthSources is not null)
                ApplyHealthSources(progressionPlan.HealthSources);
            progression.AdvanceTo(progressionPlan.NextExperience, progressionPlan.NextLevel);
            facts.Append(new ExperienceAwardedFact(fact.ActorId, actor.Rewards.ExperienceReward));
            _experienceAwarded.Add(fact.ActorId);
        }
        _awarded.Add(fact.ActorId);
    }

    /// <summary>Rebuilds the deterministic level-up source family before restored current tracks are applied.</summary>
    internal void RestoreProgression(int experience, int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(experience);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        progression.AdvanceTo(experience, level);
        if (level == 1) return;

        int endurance = playerMechanics.ReadStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt;
        Stat healthMaximum = playerMechanics.ReadStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value));
        List<StatSource> sources = [.. healthMaximum.Sources];
        for (int restoredLevel = 2; restoredLevel <= level; restoredLevel++)
        {
            int gain = DaggerfallLevelUpHealthSource.RollGain(random, playerDefinition, endurance, restoredLevel);
            StatSource source = DaggerfallLevelUpHealthSource.Create(playerMechanics.Entity, restoredLevel, gain);
            if (sources.All(existing => existing.Identity != source.Identity)) sources.Add(source);
        }
        ApplyHealthSources(sources);
    }

    private ProgressionAwardPlan? PlanProgression(long defeatedActorId, DaggerfallActorDefinition defeated)
    {
        if (defeated.Rewards.ExperienceReward <= 0 || _experienceAwarded.Contains(defeatedActorId)) return null;

        int nextExperience = checked(progression.Experience + defeated.Rewards.ExperienceReward);
        int curveLevel = checked(1 + DaggerfallFormulaPolicy.ExperimentalXpLevel(nextExperience, DaggerfallFormulaPolicy.Experimental));
        int nextLevel = Math.Max(progression.Level, curveLevel);
        if (nextLevel == progression.Level)
            return new ProgressionAwardPlan(nextExperience, nextLevel, null);

        int endurance = playerMechanics.ReadStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt;
        List<StatSource> expectedSources = [];
        for (int level = checked(progression.Level + 1); ; level++)
        {
            int gain = DaggerfallLevelUpHealthSource.RollGain(random, playerDefinition, endurance, level);
            expectedSources.Add(DaggerfallLevelUpHealthSource.Create(playerMechanics.Entity, level, gain));
            if (level == nextLevel) break;
        }

        Stat healthMaximum = playerMechanics.ReadStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value));
        List<StatSource> prospectiveSources = [.. healthMaximum.Sources];
        bool changed = false;
        foreach (StatSource expected in expectedSources)
        {
            StatSource? existing = prospectiveSources.SingleOrDefault(source => source.Identity == expected.Identity);
            if (existing is null)
            {
                prospectiveSources.Add(expected);
                changed = true;
                continue;
            }

            if (!DaggerfallLevelUpHealthSource.Matches(existing, expected))
                throw new MechanicsException($"Daggerfall level-up source {expected.Identity} already exists with different policy.");
        }

        if (changed)
        {
            Track health = playerMechanics.ReadTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
            long expectedGain = expectedSources
                .Where(source => !healthMaximum.Sources.Any(existing => existing.Identity == source.Identity))
                .Aggregate(0L, (total, source) => checked(total + checked((long)Math.Round(((StatContribution.Add)source.Contributions[0].Contribution).Amount, MidpointRounding.ToZero))));
            double expectedMaximum = healthMaximum.Value + expectedGain;
            double expectedCurrent = health.Current + expectedGain;
            Stat planned = healthMaximum.Copy();
            Track plannedHealth = new(
                planned,
                health.Current,
                health.Minimum,
                health.MaximumChangePolicy,
                health.Quantum,
                health.Rounding,
                health.IntegerRounding);
            planned.SetSources(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value), prospectiveSources);
            if (planned.Value != expectedMaximum || plannedHealth.Current != expectedCurrent)
            {
                throw new MechanicsException("Daggerfall level-up health gain was constrained before it could raise maximum and current equally.");
            }
        }
        return new ProgressionAwardPlan(nextExperience, nextLevel, changed ? prospectiveSources.ToArray() : null);
    }

    private void ApplyHealthSources(IReadOnlyList<StatSource> sources)
    {
        Stat healthMaximum = playerMechanics.ReadStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value));
        healthMaximum.SetSources(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value), sources);
    }

    private sealed record ProgressionAwardPlan(
        int NextExperience,
        int NextLevel,
        StatSource[]? HealthSources);
}

/// <summary>Daggerfall's durable, per-level health-max source policy.</summary>
internal static class DaggerfallLevelUpHealthSource
{
    private const string Definition = "daggerfall.player.level-up.health";
    private const string Group = "daggerfall.player.level-up.health";

    internal static StatSource Create(Rusty.Engine.Entities.EntityId player, int level, int gain)
    {
        if (level < 2) throw new ArgumentOutOfRangeException(nameof(level));
        if (gain < 1) throw new ArgumentOutOfRangeException(nameof(gain));
        return new StatSource(
            new IntrinsicSourceIdentity(player, SourceInstanceId.Parse($"daggerfall.player.level-up.{level}.health")),
            SourceDefinitionId.Parse(Definition),
            priority: 0,
            [new StatContributionDefinition(
                StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value),
                StackingGroupId.Parse(Group),
                MechanicsStackingPolicy.Sum,
                new StatContribution.Add(gain))]);
    }

    /// <summary>One keyed, ruleset-owned level-up gain shared by restore validation and source reconstruction.</summary>
    internal static int RollGain(IRandomService random, DaggerfallActorDefinition playerDefinition, int endurance, int level)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(playerDefinition);
        if (level < 2) throw new ArgumentOutOfRangeException(nameof(level));
        int hitPointsPerLevel = playerDefinition.HitPointsPerLevel
            ?? throw new InvalidOperationException("The Daggerfall player definition must provide hitPointsPerLevel.");
        (int minimum, int maximum) = DaggerfallFormulaPolicy.HitPointsPerLevelRollBounds(hitPointsPerLevel, DaggerfallFormulaPolicy.Experimental);
        int roll = checked((int)random.DrawKeyed(new KeyedRngRequest(
            CombatRandomKey.Seed,
            CombatRandomKey.PlayerScope,
            $"player.level-up.{level}.hp-roll",
            minimum,
            maximum)).Value);
        if (roll < minimum || roll > maximum)
            throw new MechanicsException($"Daggerfall level-up roll for level {level} was outside [{minimum}, {maximum}].");
        return DaggerfallFormulaPolicy.HitPointsPerLevelUp(roll, endurance, DaggerfallFormulaPolicy.Experimental);
    }

    internal static bool Matches(StatSource actual, StatSource expected) =>
        actual.Identity == expected.Identity
        && actual.Definition == expected.Definition
        && actual.Priority == expected.Priority
        && actual.Contributions.Count == 1
        && expected.Contributions.Count == 1
        && actual.Contributions[0] == expected.Contributions[0];
}

internal static class LootRandomKey
{
    internal const ulong Seed = 0;
    internal const string Scope = "dagger.combat.ai.v1";
    internal static string For(ulong generation, ulong update, long actor, string roll) => $"generation:{generation}:step:{update}:loot:{actor}:{roll}";
}

/// <summary>
/// Daggerfall's durable identity for generated unique loot. Allocation,
/// reservations, and tombstones are Kit mechanism; this type only names the
/// Daggerfall-owned entity id that the Engine handle is derived from.
/// </summary>
internal sealed class DaggerfallUniqueItemAllocator
{
    internal const ulong DefaultFirstEntityId = 1_000_000_000_000UL;
    private static readonly DurableIdentityKind LootKind = DurableIdentityKind.Item;
    private readonly DurableIdentityAllocator _identities;

    internal DaggerfallUniqueItemAllocator(
        ulong firstEntityId,
        IEnumerable<ulong>? reserved = null,
        IEnumerable<ulong>? removed = null) =>
        _identities = new DurableIdentityAllocator(LootKind, firstEntityId, reserved, removed);

    private DaggerfallUniqueItemAllocator(DurableIdentityAllocator identities) => _identities = identities;

    internal ulong NextEntityId => _identities.NextIdentity(LootKind);

    internal IReadOnlyCollection<ulong> ReservedEntityIds => _identities.ReservedIdentities(LootKind);

    internal IReadOnlyCollection<ulong> RemovedEntityIds => _identities.RemovedIdentities(LootKind);

    /// <summary>Recreates the session ledger from the identity state a save implies.</summary>
    internal static DaggerfallUniqueItemAllocator Restore(DurableIdentityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new DaggerfallUniqueItemAllocator(DurableIdentityAllocator.Restore(state));
    }

    /// <summary>Captures this session's allocator evidence for the save payload.</summary>
    internal DurableIdentityState CaptureState() => _identities.CaptureState();

    /// <summary>Issues one durable reference. The Engine handle is derived from it at the named edge.</summary>
    internal DurableIdentityReference AllocateReference() => _identities.Allocate(LootKind);

    /// <summary>Records that one generated identity no longer exists in this world.</summary>
    internal void Remove(DurableIdentityReference identity) => _identities.Remove(identity);
}
