using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

/// <summary>
/// The ruleset facts that affect one enemy's read of the player. Engine geometry
/// is deliberately absent: the caller supplies the typed perception pair that
/// the Engine admitted for this update.
/// </summary>
internal readonly record struct DaggerfallEnemyPerceptionContext(
    long GameMinute,
    int StealthSkill,
    int Personality,
    string? LanguageSkill,
    int LanguageSkillValue,
    bool TargetMovingLessThanHalfSpeed,
    bool TargetInsideDungeonCastle,
    bool TargetInvisible,
    bool TargetBlending,
    bool TargetShade,
    bool EnemySeesThroughInvisibility,
    bool TargetPacified,
    bool EnemyHostile,
    bool TargetWeaponSheathed,
    int ComprehendLanguagesBonus = 0,
    double Noise = 1d,
    Func<int, int>? RollPercent = null,
    bool IsWithinClassicSpawnRange = true)
{
    internal static DaggerfallEnemyPerceptionContext Default(long gameMinute) => new(
        gameMinute,
        StealthSkill: 0,
        Personality: 0,
        LanguageSkill: null,
        LanguageSkillValue: 0,
        TargetMovingLessThanHalfSpeed: false,
        TargetInsideDungeonCastle: false,
        TargetInvisible: false,
        TargetBlending: false,
        TargetShade: false,
        EnemySeesThroughInvisibility: false,
        TargetPacified: false,
        EnemyHostile: true,
        TargetWeaponSheathed: true);

    internal DaggerfallEnemyPerceptionContext Validate()
    {
        if (StealthSkill is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(StealthSkill));
        if (Personality is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(Personality));
        if (LanguageSkillValue is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(LanguageSkillValue));
        if (ComprehendLanguagesBonus < 0) throw new ArgumentOutOfRangeException(nameof(ComprehendLanguagesBonus));
        if (!double.IsFinite(Noise) || Noise < 0d) throw new ArgumentOutOfRangeException(nameof(Noise));
        if (LanguageSkill is not null && string.IsNullOrWhiteSpace(LanguageSkill)) throw new ArgumentException("A language skill must be null or non-blank.", nameof(LanguageSkill));
        return this;
    }
}

/// <summary>Mobile-specific source modifiers retained by the perception owner.</summary>
internal sealed record DaggerfallEnemyPerceptionSource(
    double SightRadius = DaggerfallPerceptionQueryDefaults.SightRadius,
    double HearingRadius = DaggerfallPerceptionQueryDefaults.HearingRadius,
    double SightModifier = 0d,
    double HearingModifier = 0d,
    bool SeesThroughInvisibility = false)
{
    internal DaggerfallEnemyPerceptionSource Validate()
    {
        if (!double.IsFinite(SightRadius) || SightRadius <= 0d) throw new ArgumentOutOfRangeException(nameof(SightRadius));
        if (!double.IsFinite(HearingRadius) || HearingRadius < 0d) throw new ArgumentOutOfRangeException(nameof(HearingRadius));
        if (!double.IsFinite(SightModifier)) throw new ArgumentOutOfRangeException(nameof(SightModifier));
        if (!double.IsFinite(HearingModifier)) throw new ArgumentOutOfRangeException(nameof(HearingModifier));
        double effectiveSightRadius = SightRadius + SightModifier;
        double effectiveHearingRadius = HearingRadius + HearingModifier;
        if (!double.IsFinite(effectiveSightRadius) || effectiveSightRadius <= 0d)
            throw new ArgumentOutOfRangeException(nameof(SightModifier));
        if (!double.IsFinite(effectiveHearingRadius) || effectiveHearingRadius < 0d)
            throw new ArgumentOutOfRangeException(nameof(HearingModifier));
        return this;
    }
}

/// <summary>One enemy's retained classic senses state. It is cleared with the live site.</summary>
internal sealed class DaggerfallEnemyPerceptionMemory
{
    internal bool Detected { get; set; }
    internal bool HasEncounteredPlayer { get; set; }
    internal bool Pacified { get; set; }
    internal long? LastStealthCheckMinute { get; set; }
    internal long? LastDirectSightMinute { get; set; }

    internal void Clear()
    {
        Detected = false;
        HasEncounteredPlayer = false;
        Pacified = false;
        LastStealthCheckMinute = null;
        LastDirectSightMinute = null;
    }
}

internal readonly record struct DaggerfallPacificationAttempt(
    string Skill,
    int Chance,
    int Roll,
    bool Succeeded);

/// <summary>Ruleset output consumed by the existing pursuit coordinator and skill-use owner.</summary>
internal sealed record DaggerfallEnemyPerceptionDecision(
    bool InSight,
    bool InEarshot,
    bool StealthCheckAttempted,
    bool Detected,
    bool BlockedByIllusion,
    bool Pacified,
    DaggerfallPacificationAttempt? Pacification,
    IReadOnlyList<DaggerfallSkillUse> SkillUses)
{
    internal bool PursuitVisible => Detected && !Pacified;
}

/// <summary>
/// Daggerfall's senses decision over an Engine perception fact. The method keeps
/// the donor's ordering: direct sight, remembered hearing, then stealth, followed
/// by first-contact language pacification.
/// </summary>
internal static class DaggerfallPerceptionPolicy
{
    private const string StealthSkill = DaggerfallSkills.Stealth;

    internal static DaggerfallEnemyPerceptionDecision Evaluate(
        DaggerfallEnemyPerceptionMemory memory,
        PerceptionPair? pair,
        DaggerfallEnemyPerceptionSource source,
        DaggerfallEnemyPerceptionContext context,
        Func<int, int>? rollPercent = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(source);
        source.Validate();
        context.Validate();
        List<DaggerfallSkillUse> uses = [];

        if (context.TargetPacified)
            memory.Pacified = true;

        PerceptionPair observed = pair ?? default;
        bool hasPair = pair is not null;
        if (hasPair && (!double.IsFinite(observed.Distance) || observed.Distance < 0d))
            throw new ArgumentOutOfRangeException(nameof(pair), "A perception pair distance must be finite and non-negative.");
        bool inRange = hasPair && observed.Distance < source.SightRadius + source.SightModifier;
        bool physicalSight = inRange && observed.Kind == PerceptionPairKind.Visible;
        bool blockedByIllusion = false;
        if (!context.EnemySeesThroughInvisibility && !source.SeesThroughInvisibility)
        {
            if (context.TargetInvisible)
                blockedByIllusion = true;
            else if (context.TargetBlending)
                blockedByIllusion = !BreaksThrough(DaggerfallPerceptionQueryDefaults.BlendingBreakthroughChance, context, rollPercent);
            else if (context.TargetShade)
                blockedByIllusion = !BreaksThrough(DaggerfallPerceptionQueryDefaults.ShadeBreakthroughChance, context, rollPercent);
        }
        bool inSight = physicalSight && !blockedByIllusion;
        if (inSight)
            memory.LastDirectSightMinute = context.GameMinute;

        bool inEarshot = !blockedByIllusion
            && physicalSight == false
            && memory.Detected
            && hasPair
            && observed.Kind != PerceptionPairKind.Occluded
            && observed.Distance < source.HearingRadius + source.HearingModifier
            && context.Noise > 0d;

        bool stealthAttempted = false;
        bool stealthDetected = false;
        if (!inSight && !inEarshot && !blockedByIllusion
            && context.IsWithinClassicSpawnRange
            && hasPair
            && observed.Distance <= DaggerfallPerceptionQueryDefaults.StealthMaximumDistance
            && !(context.TargetInsideDungeonCastle && !context.EnemyHostile))
        {
            stealthDetected = StealthCheck(memory, context, observed.Distance, uses, rollPercent, out stealthAttempted);
        }

        bool detected = !blockedByIllusion && (inSight || inEarshot || stealthDetected);
        DaggerfallPacificationAttempt? pacification = null;
        if (detected && !memory.HasEncounteredPlayer)
        {
            memory.HasEncounteredPlayer = true;
            if (context.EnemyHostile && !memory.Pacified && context.LanguageSkill is { Length: > 0 } language)
            {
                pacification = TryPacification(language, context, rollPercent);
                if (pacification.Value.Succeeded)
                {
                    memory.Pacified = true;
                    uses.Add(new DaggerfallSkillUse(language, DaggerfallSkillUseReason.PacificationSucceeded, DaggerfallSkillUseOutcome.Succeeded));
                }
                else if (language is not DaggerfallSkills.Etiquette and not DaggerfallSkills.Streetwise)
                {
                    uses.Add(new DaggerfallSkillUse(language, DaggerfallSkillUseReason.PacificationFailed, DaggerfallSkillUseOutcome.Attempted));
                }
            }
        }

        memory.Detected = detected;
        return new(inSight, inEarshot, stealthAttempted, detected, blockedByIllusion, memory.Pacified, pacification, uses);
    }

    private static bool StealthCheck(
        DaggerfallEnemyPerceptionMemory memory,
        DaggerfallEnemyPerceptionContext context,
        double distance,
        List<DaggerfallSkillUse> uses,
        Func<int, int>? rollPercent,
        out bool attempted)
    {
        attempted = false;
        if (memory.LastStealthCheckMinute == context.GameMinute)
            return memory.Detected;

        // EnemySenses skips every other minute for a player moving at less than
        // half speed. A moving player already encountered by the enemy is always
        // detected in the donor path.
        if (context.TargetMovingLessThanHalfSpeed && (context.GameMinute & 1) == 1)
            return memory.Detected;
        // A moving player that has already been encountered stays detected in
        // the donor path. A slow player still gets the normal stealth roll on
        // even minutes; only odd minutes are skipped above. The live pursuit
        // seam does not let the same admitted minute's facing rejection replay
        // the direct sight that just ended.
        if (context.TargetMovingLessThanHalfSpeed
            && memory.HasEncounteredPlayer
            && memory.LastDirectSightMinute == context.GameMinute)
            return false;
        if (!context.TargetMovingLessThanHalfSpeed && memory.HasEncounteredPlayer)
            return true;

        memory.LastStealthCheckMinute = context.GameMinute;
        attempted = true;
        uses.Add(new DaggerfallSkillUse(StealthSkill, DaggerfallSkillUseReason.StealthCheck,
            DaggerfallSkillUseOutcome.Attempted, context.GameMinute));
        int chance = CalculateStealthChance(distance, context.StealthSkill);
        int roll = NormalizeRoll(rollPercent?.Invoke(100) ?? 0);
        // The donor's FailedRoll(chance) result is the detection result: chance
        // is the target's chance to remain hidden, without a final clamp.
        return roll >= chance;
    }

    private static DaggerfallPacificationAttempt TryPacification(
        string language,
        DaggerfallEnemyPerceptionContext context,
        Func<int, int>? rollPercent)
    {
        int roll = NormalizeRoll(rollPercent?.Invoke(200) ?? 0, 200);
        int chance = CalculateEnemyPacificationChance(language, context.LanguageSkillValue, context.Personality,
            context.TargetWeaponSheathed, context.ComprehendLanguagesBonus);
        bool succeeded = DaggerfallFormulaPolicy.CalculateEnemyPacification(language, context.LanguageSkillValue,
            context.Personality, context.TargetWeaponSheathed, context.ComprehendLanguagesBonus, roll);
        return new(language, chance, roll, succeeded);
    }

    private static bool BreaksThrough(
        int chance,
        DaggerfallEnemyPerceptionContext context,
        Func<int, int>? rollPercent)
    {
        int roll = NormalizeRoll(rollPercent?.Invoke(100) ?? 100);
        return roll < chance;
    }

    internal static int CalculateStealthChance(double distance, int targetStealthSkill)
        => DaggerfallFormulaPolicy.CalculateStealthChance(distance, targetStealthSkill);

    internal static int CalculateStealthChance(float distance, int targetStealthSkill)
        => DaggerfallFormulaPolicy.CalculateStealthChance(distance, targetStealthSkill);

    internal static int CalculateEnemyPacificationChance(
        string languageSkill,
        int languageSkillValue,
        int personality,
        bool weaponSheathed,
        int comprehendLanguagesBonus = 0)
        => DaggerfallFormulaPolicy.CalculateEnemyPacificationChance(languageSkill, languageSkillValue, personality,
            weaponSheathed, comprehendLanguagesBonus);

    private static int NormalizeRoll(int value, int exclusiveMaximum = 100)
    {
        if (exclusiveMaximum <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        return Math.Clamp(value, 0, exclusiveMaximum - 1);
    }
}

/// <summary>
/// Read-only Engine perception adapter. It leaves geometry and occlusion to
/// Engine, then projects Daggerfall's senses decision into the existing generic
/// pursuit receipt so pursuit does not grow a second AI loop.
/// </summary>
internal sealed class DaggerfallEnemyPerceptionService : IPerceptionService
{
    private readonly IPerceptionService _inner;
    private readonly Func<long, PerceptionReadoutLeaseReceipt, PerceptionReadoutLeaseReceipt> _filter;

    internal DaggerfallEnemyPerceptionService(
        IPerceptionService inner,
        Func<long, PerceptionReadoutLeaseReceipt, PerceptionReadoutLeaseReceipt> filter)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
    }

    public PerceptionReadoutLeaseReceipt QueryVisibility(PerceptionQueryRequest request)
    {
        PerceptionReadoutLeaseReceipt receipt = _inner.QueryVisibility(request);
        if (request.Observers.Length != 1 || request.Targets.Length != 1)
            return receipt;
        ulong observer = request.Observers.Span[0].Entity;
        if (observer > long.MaxValue)
            return receipt;
        return _filter((long)observer, receipt);
    }
}
