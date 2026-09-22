using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Classic sources of a use count, kept separate so an operation cannot claim another operation's policy.</summary>
internal enum DaggerfallSkillUseReason
{
    WeaponHit,
    CriticalStrikeHit,
    DodgingEnemyAttack,
    BackstabbingOpportunity,
    ReleasedSpellEffect,
    Running,
    Swimming,
    Jumping,
    ClimbingCheck,
    Rappelling,
    MedicalRest,
    LockpickingAttempt,
    PickpocketAttempt,
    ShopliftingAttempt,
    MercantileTrade,
    StealthCheck,
    PacificationSucceeded,
    PacificationFailed,
    DialogueEtiquette,
    DialogueStreetwise,
    CourtEtiquette,
    CourtStreetwise,
}

internal enum DaggerfallSkillUseDomain { Combat, Magic, Physical, Social }
internal enum DaggerfallSkillUseOutcome { Accepted, Attempted, Succeeded }
internal enum DaggerfallSkillUseCadence { PerAcceptedOperation, PerGameMinute }

/// <summary>Typed classic attribution: fixed amount, admission outcome, and only the cadence required by its source.</summary>
internal sealed record DaggerfallSkillUsePolicy(
    DaggerfallSkillUseReason Reason,
    DaggerfallSkillUseDomain Domain,
    string? RequiredSkill,
    int Amount,
    DaggerfallSkillUseOutcome Outcome,
    DaggerfallSkillUseCadence Cadence);

/// <summary>One operation already admitted by its gameplay owner. A game minute is supplied only by minute-governed sources.</summary>
internal readonly record struct DaggerfallSkillUse(
    string Skill,
    DaggerfallSkillUseReason Reason,
    DaggerfallSkillUseOutcome Outcome,
    long? GameMinute = null)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Skill);
        if (!Enum.IsDefined(Reason)) throw new ArgumentOutOfRangeException(nameof(Reason));
        if (!Enum.IsDefined(Outcome)) throw new ArgumentOutOfRangeException(nameof(Outcome));
    }
}

/// <summary>Current Daggerfall-owned progression policy beside Kit's reusable counters and level state.</summary>
internal sealed class DaggerfallSkillUseReactions
{
    internal const int MaximumSkillUses = 20_000;

    private static readonly IReadOnlyDictionary<DaggerfallSkillUseReason, DaggerfallSkillUsePolicy> Policies =
        new Dictionary<DaggerfallSkillUseReason, DaggerfallSkillUsePolicy>
        {
            [DaggerfallSkillUseReason.WeaponHit] = new(DaggerfallSkillUseReason.WeaponHit, DaggerfallSkillUseDomain.Combat, null, 1, DaggerfallSkillUseOutcome.Succeeded, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.CriticalStrikeHit] = new(DaggerfallSkillUseReason.CriticalStrikeHit, DaggerfallSkillUseDomain.Combat, "critical-strike", 1, DaggerfallSkillUseOutcome.Succeeded, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.DodgingEnemyAttack] = new(DaggerfallSkillUseReason.DodgingEnemyAttack, DaggerfallSkillUseDomain.Combat, "dodging", 1, DaggerfallSkillUseOutcome.Attempted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.BackstabbingOpportunity] = new(DaggerfallSkillUseReason.BackstabbingOpportunity, DaggerfallSkillUseDomain.Combat, "backstabbing", 1, DaggerfallSkillUseOutcome.Accepted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.ReleasedSpellEffect] = new(DaggerfallSkillUseReason.ReleasedSpellEffect, DaggerfallSkillUseDomain.Magic, null, 1, DaggerfallSkillUseOutcome.Accepted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.Running] = new(DaggerfallSkillUseReason.Running, DaggerfallSkillUseDomain.Physical, "running", 1, DaggerfallSkillUseOutcome.Accepted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.Swimming] = new(DaggerfallSkillUseReason.Swimming, DaggerfallSkillUseDomain.Physical, "swimming", 1, DaggerfallSkillUseOutcome.Accepted, DaggerfallSkillUseCadence.PerGameMinute),
            [DaggerfallSkillUseReason.Jumping] = new(DaggerfallSkillUseReason.Jumping, DaggerfallSkillUseDomain.Physical, "jumping", 1, DaggerfallSkillUseOutcome.Accepted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.ClimbingCheck] = new(DaggerfallSkillUseReason.ClimbingCheck, DaggerfallSkillUseDomain.Physical, "climbing", 1, DaggerfallSkillUseOutcome.Attempted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.Rappelling] = new(DaggerfallSkillUseReason.Rappelling, DaggerfallSkillUseDomain.Physical, "climbing", 1, DaggerfallSkillUseOutcome.Accepted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.MedicalRest] = new(DaggerfallSkillUseReason.MedicalRest, DaggerfallSkillUseDomain.Physical, "medical", 1, DaggerfallSkillUseOutcome.Accepted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.LockpickingAttempt] = new(DaggerfallSkillUseReason.LockpickingAttempt, DaggerfallSkillUseDomain.Physical, "lockpicking", 1, DaggerfallSkillUseOutcome.Attempted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.PickpocketAttempt] = new(DaggerfallSkillUseReason.PickpocketAttempt, DaggerfallSkillUseDomain.Social, "pickpocket", 1, DaggerfallSkillUseOutcome.Attempted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.ShopliftingAttempt] = new(DaggerfallSkillUseReason.ShopliftingAttempt, DaggerfallSkillUseDomain.Social, "pickpocket", 1, DaggerfallSkillUseOutcome.Attempted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.MercantileTrade] = new(DaggerfallSkillUseReason.MercantileTrade, DaggerfallSkillUseDomain.Social, "mercantile", 1, DaggerfallSkillUseOutcome.Accepted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.StealthCheck] = new(DaggerfallSkillUseReason.StealthCheck, DaggerfallSkillUseDomain.Physical, "stealth", 1, DaggerfallSkillUseOutcome.Attempted, DaggerfallSkillUseCadence.PerGameMinute),
            [DaggerfallSkillUseReason.PacificationSucceeded] = new(DaggerfallSkillUseReason.PacificationSucceeded, DaggerfallSkillUseDomain.Social, null, 1, DaggerfallSkillUseOutcome.Succeeded, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.PacificationFailed] = new(DaggerfallSkillUseReason.PacificationFailed, DaggerfallSkillUseDomain.Social, null, 1, DaggerfallSkillUseOutcome.Attempted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.DialogueEtiquette] = new(DaggerfallSkillUseReason.DialogueEtiquette, DaggerfallSkillUseDomain.Social, "etiquette", 1, DaggerfallSkillUseOutcome.Accepted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.DialogueStreetwise] = new(DaggerfallSkillUseReason.DialogueStreetwise, DaggerfallSkillUseDomain.Social, "streetwise", 1, DaggerfallSkillUseOutcome.Accepted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.CourtEtiquette] = new(DaggerfallSkillUseReason.CourtEtiquette, DaggerfallSkillUseDomain.Social, "etiquette", 1, DaggerfallSkillUseOutcome.Accepted, DaggerfallSkillUseCadence.PerAcceptedOperation),
            [DaggerfallSkillUseReason.CourtStreetwise] = new(DaggerfallSkillUseReason.CourtStreetwise, DaggerfallSkillUseDomain.Social, "streetwise", 1, DaggerfallSkillUseOutcome.Accepted, DaggerfallSkillUseCadence.PerAcceptedOperation),
        };

    private readonly ProgressionState _progression;
    private readonly StatsComponent _stats;
    private readonly DaggerfallCareerDefinition _career;
    private readonly DaggerfallStatId[] _skills;
    private readonly Dictionary<DaggerfallSkillUseIntervalKey, long> _lastGameMinutes = [];
    private int _startingLevelUpSkillSum;

    internal DaggerfallSkillUseReactions(
        ProgressionState progression,
        StatsComponent stats,
        DaggerfallDefinitions definitions,
        DaggerfallActorDefinition player)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(player);
        _progression = progression;
        _stats = stats;
        _skills = definitions.Vocabulary.Skills.ToArray();
        _career = definitions.Catalogs.RequireCareer(player.Career
            ?? throw new InvalidOperationException("The Daggerfall player definition must name a career for classic progression."));
        EnsureKnownCareerSkills();
        foreach (DaggerfallStatId skill in _skills) _progression.TallySkillUse(skill.Value, 0, MaximumSkillUses);
        _startingLevelUpSkillSum = CurrentLevelUpSkillSum();
    }

    internal int StartingLevelUpSkillSum => _startingLevelUpSkillSum;
    internal static IReadOnlyCollection<DaggerfallSkillUsePolicy> AttributionPolicies => Policies.Values.ToArray();

    /// <summary>
    /// Records an operation which its gameplay owner already accepted. Ordinary operations rely on
    /// that owner's single admitted application; only minute-governed uses retain a clock gate.
    /// </summary>
    internal bool Record(DaggerfallSkillUse use)
    {
        use.Validate();
        if (!_skills.Any(skill => string.Equals(skill.Value, use.Skill, StringComparison.Ordinal)))
            throw new ArgumentException($"Daggerfall skill use names unknown skill '{use.Skill}'.", nameof(use));
        DaggerfallSkillUsePolicy policy = Policies[use.Reason];
        if (use.Outcome != policy.Outcome)
            throw new ArgumentException($"Daggerfall skill use '{use.Reason}' requires outcome '{policy.Outcome}'.", nameof(use));
        if (policy.RequiredSkill is { } required && !string.Equals(required, use.Skill, StringComparison.Ordinal))
            throw new ArgumentException($"Daggerfall skill use '{use.Reason}' requires skill '{required}'.", nameof(use));
        if (!SupportsVariableSkill(use))
            throw new ArgumentException($"Daggerfall skill use '{use.Reason}' does not support skill '{use.Skill}'.", nameof(use));

        if (policy.Cadence == DaggerfallSkillUseCadence.PerGameMinute)
        {
            if (use.GameMinute is not long minute)
                throw new ArgumentException($"Daggerfall skill use '{use.Reason}' requires its admitted game minute.", nameof(use));
            DaggerfallSkillUseIntervalKey key = new(use.Skill, use.Reason);
            if (_lastGameMinutes.TryGetValue(key, out long previous))
            {
                if (minute == previous) return false;
                if (minute < previous)
                    throw new ArgumentException("Daggerfall skill-use game time cannot move backwards.", nameof(use));
            }
            _lastGameMinutes[key] = minute;
        }
        else if (use.GameMinute is not null)
        {
            throw new ArgumentException($"Daggerfall skill use '{use.Reason}' is not minute-governed.", nameof(use));
        }

        _progression.TallySkillUse(use.Skill, policy.Amount, MaximumSkillUses);
        return true;
    }

    internal DaggerfallSkillProgressionSave Capture() => new(
        _skills.Select(skill => new DaggerfallSkillUseCounterSave(skill.Value, _progression.SkillUses.GetValueOrDefault(skill.Value))).ToArray(),
        _startingLevelUpSkillSum,
        _lastGameMinutes.Select(entry => new DaggerfallSkillUseIntervalSave(entry.Key.Skill, entry.Key.Reason, entry.Value)).ToArray());

    internal void Restore(DaggerfallSkillProgressionSave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate();
        HashSet<string> expected = _skills.Select(skill => skill.Value).ToHashSet(StringComparer.Ordinal);
        if (!saved.Counters.Select(counter => counter.Skill).ToHashSet(StringComparer.Ordinal).SetEquals(expected)
            || saved.Counters.Length != expected.Count)
            throw new ArgumentException("Saved Daggerfall skill counters do not match the selected ruleset vocabulary.", nameof(saved));

        Dictionary<DaggerfallSkillUseIntervalKey, long> restoredIntervals = [];
        foreach (DaggerfallSkillUseIntervalSave interval in saved.Intervals)
        {
            if (!expected.Contains(interval.Skill) || !Policies.TryGetValue(interval.Reason, out DaggerfallSkillUsePolicy? policy)
                || policy.Cadence != DaggerfallSkillUseCadence.PerGameMinute
                || !string.Equals(policy.RequiredSkill ?? interval.Skill, interval.Skill, StringComparison.Ordinal))
                throw new ArgumentException("Saved Daggerfall skill-use intervals do not match the selected ruleset policy.", nameof(saved));
            restoredIntervals.Add(new DaggerfallSkillUseIntervalKey(interval.Skill, interval.Reason), interval.GameMinute);
        }

        _progression.RestoreSkillUses(saved.Counters.Select(counter => new KeyValuePair<string, int>(counter.Skill, counter.Uses)));
        _lastGameMinutes.Clear();
        foreach ((DaggerfallSkillUseIntervalKey key, long minute) in restoredIntervals) _lastGameMinutes.Add(key, minute);
        _startingLevelUpSkillSum = saved.StartingLevelUpSkillSum;
    }

    private int CurrentLevelUpSkillSum()
    {
        int primary = _career.PrimarySkills.Sum(skill => Permanent(new DaggerfallStatId(skill)));
        int majors = _career.MajorSkills.Sum(skill => Permanent(new DaggerfallStatId(skill)))
            - _career.MajorSkills.Min(skill => Permanent(new DaggerfallStatId(skill)));
        int minor = _career.MinorSkills.Max(skill => Permanent(new DaggerfallStatId(skill)));
        return checked(primary + majors + minor);
    }

    private int Permanent(DaggerfallStatId skill) => checked((int)_stats.GetStat(StatId.Parse(skill.Value)).BaseValue);

    private static bool SupportsVariableSkill(DaggerfallSkillUse use) => use.Reason switch
    {
        DaggerfallSkillUseReason.WeaponHit => use.Skill is "short-blade" or "long-blade" or "hand-to-hand" or "axe" or "blunt-weapon" or "archery",
        DaggerfallSkillUseReason.ReleasedSpellEffect => use.Skill is "destruction" or "restoration" or "illusion" or "alteration" or "thaumaturgy" or "mysticism",
        DaggerfallSkillUseReason.PacificationSucceeded => IsLanguageSkill(use.Skill),
        DaggerfallSkillUseReason.PacificationFailed => use.Skill is "orcish" or "harpy" or "giantish" or "dragonish" or "nymph" or "daedric" or "spriggan" or "centaurian" or "impish",
        _ => true,
    };

    private static bool IsLanguageSkill(string skill) => skill is "etiquette" or "streetwise"
        or "orcish" or "harpy" or "giantish" or "dragonish" or "nymph" or "daedric" or "spriggan" or "centaurian" or "impish";

    private void EnsureKnownCareerSkills()
    {
        HashSet<string> known = _skills.Select(skill => skill.Value).ToHashSet(StringComparer.Ordinal);
        if (_career.PrimarySkills.Count == 0 || _career.MajorSkills.Count == 0 || _career.MinorSkills.Count == 0
            || _career.SkillReferences.Any(skill => !known.Contains(skill)))
            throw new InvalidOperationException($"Daggerfall career '{_career.Id}' has no complete skill progression mapping.");
    }

    private readonly record struct DaggerfallSkillUseIntervalKey(string Skill, DaggerfallSkillUseReason Reason);
}

/// <summary>Current-state data Daggerfall owns around Kit's generic per-skill counters.</summary>
internal sealed record DaggerfallSkillProgressionSave(
    DaggerfallSkillUseCounterSave[] Counters,
    int StartingLevelUpSkillSum,
    DaggerfallSkillUseIntervalSave[] Intervals)
{
    internal DaggerfallSkillProgressionSave Validate()
    {
        ArgumentNullException.ThrowIfNull(Counters);
        ArgumentNullException.ThrowIfNull(Intervals);
        if (StartingLevelUpSkillSum < 0)
            throw new ArgumentOutOfRangeException(nameof(StartingLevelUpSkillSum), "Saved Daggerfall skill progression has an invalid baseline.");
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (DaggerfallSkillUseCounterSave counter in Counters)
        {
            ArgumentNullException.ThrowIfNull(counter);
            if (string.IsNullOrWhiteSpace(counter.Skill) || counter.Uses is < 0 or > DaggerfallSkillUseReactions.MaximumSkillUses || !names.Add(counter.Skill))
                throw new ArgumentException("Saved Daggerfall skill counters must be distinct known non-negative values.");
        }
        HashSet<DaggerfallSkillUseIntervalKey> intervalKeys = [];
        foreach (DaggerfallSkillUseIntervalSave interval in Intervals)
        {
            ArgumentNullException.ThrowIfNull(interval);
            if (string.IsNullOrWhiteSpace(interval.Skill) || !Enum.IsDefined(interval.Reason)
                || !intervalKeys.Add(new DaggerfallSkillUseIntervalKey(interval.Skill, interval.Reason)))
                throw new ArgumentException("Saved Daggerfall skill-use intervals must be distinct values.");
        }
        return this;
    }

    private readonly record struct DaggerfallSkillUseIntervalKey(string Skill, DaggerfallSkillUseReason Reason);
}

internal sealed record DaggerfallSkillUseCounterSave(string Skill, int Uses);
internal sealed record DaggerfallSkillUseIntervalSave(string Skill, DaggerfallSkillUseReason Reason, long GameMinute);
