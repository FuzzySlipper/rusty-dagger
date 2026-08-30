namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>
/// Named, compiled Daggerfall formulas.  The donor catalogs are evidence for
/// these policies; they are not an evaluator or a runtime rules language.
/// Values which are part of a selected profile live in the tuning record so a
/// caller can identify and replace them without hunting through call sites.
/// </summary>
internal static class DaggerfallFormulaPolicy
{
    internal static DaggerfallFormulaTuning Classic { get; } = new();
    // The current live profile is intentionally named separately from the
    // classic skill-sum profile even while it shares the 500-XP tuning value.
    internal static DaggerfallFormulaTuning Experimental { get; } = Classic with { ExperiencePerLevel = 500 };

    internal static int DamageModifier(int strength, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return FloorDivide(strength - selected.AttributeBaseline, selected.DamageModifierDivisor);
    }

    internal static int ToHitModifier(int agility, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return FloorDivide(agility, selected.ToHitAttributeDivisor) - selected.ToHitBaseline;
    }

    internal static int HitPointsModifier(int endurance, DaggerfallFormulaTuning? tuning = null) =>
        FloorDivide(endurance - tuning.GetValueOrDefault(Classic).AttributeBaseline, tuning.GetValueOrDefault(Classic).AttributeDivisor);

    internal static int HealingRateModifier(int endurance, DaggerfallFormulaTuning? tuning = null) =>
        HitPointsModifier(endurance, tuning);

    internal static int MagicResist(int willpower, DaggerfallFormulaTuning? tuning = null) =>
        FloorDivide(willpower, tuning.GetValueOrDefault(Classic).AttributeDivisor);

    internal static int MaxEncumbrance(int strength, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return FloorDivide(checked(strength * selected.EncumbranceNumerator), selected.EncumbranceDenominator);
    }

    internal static int MaxBreath(int endurance, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return FloorDivide(endurance, selected.BreathDivisor);
    }

    internal static int MaxFatigue(int strength, int endurance, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return checked((strength + endurance) * selected.FatigueUnitsPerAttributePoint);
    }

    internal static int SpellPoints(int intelligence, int multiplierMilli, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return FloorDivide(checked(intelligence * multiplierMilli), selected.MilliScale);
    }

    internal static int HandToHandMinimumDamage(int skill, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return checked(FloorDivide(skill, selected.HandToHandMinimumDivisor) + 1);
    }

    internal static int HandToHandMaximumDamage(int skill, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return checked(FloorDivide(skill, selected.HandToHandMaximumDivisor) + 1);
    }

    internal static int HealthRecoveryRate(int endurance, int medical, int maximumHealth, bool rapidHealing, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        int careerBonus = rapidHealing ? selected.RapidHealingBonus : 0;
        int recovery = checked(
            HealingRateModifier(endurance, selected)
            + FloorDivide(checked((medical + selected.HealthRecoveryBase + careerBonus) * maximumHealth), selected.HealthRecoveryScale));
        return Math.Max(selected.MinimumRecoveryRate, recovery);
    }

    internal static int FatigueRecoveryRate(int maximumFatigue, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return Math.Max(selected.MinimumRecoveryRate, FloorDivide(maximumFatigue, selected.RecoveryDivisor));
    }

    internal static int SpellPointRecoveryRate(int maximumMagicka, bool noRegeneration, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        if (noRegeneration) return 0;
        return Math.Max(selected.MinimumRecoveryRate, FloorDivide(maximumMagicka, selected.RecoveryDivisor));
    }

    internal static int BackstabChance(int skill, bool targetFacingAway) => targetFacingAway ? Math.Max(0, skill) : 0;

    /// <summary>Classic skill-sum progression, kept separate from the live XP experiment.</summary>
    internal static int ClassicPlayerLevel(int currentLevelUpSkills, int startingLevelUpSkills, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return FloorDivide(checked(currentLevelUpSkills - startingLevelUpSkills + selected.LevelFormulaOffset), selected.LevelFormulaDivisor);
    }

    /// <summary>The selected live profile's 500-XP threshold count.</summary>
    internal static int ExperimentalXpLevel(int experience, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return Math.Max(0, FloorDivide(experience, selected.ExperiencePerLevel));
    }

    internal static int HitPointsPerLevelUp(int roll, int endurance, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return Math.Max(selected.MinimumRecoveryRate, checked(roll + HealingRateModifier(endurance, selected)));
    }

    internal static (int Minimum, int Maximum) HitPointsPerLevelRollBounds(int hitPointsPerLevel, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        if (hitPointsPerLevel < selected.MinimumHitPointsPerLevel) throw new ArgumentOutOfRangeException(nameof(hitPointsPerLevel));
        return (Math.Max(selected.MinimumHitPointsPerLevel, FloorDivide(hitPointsPerLevel, selected.HitPointsRollDivisor)), hitPointsPerLevel);
    }

    internal static int ReflexesSkillUseScaleMilli(int reflexes, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        return checked(selected.MilliScale - ((reflexes - selected.ReflexesBaseline) * selected.ReflexesPenaltyMilli));
    }

    internal static int SkillUsesForAdvancement(int skillValue, int skillMultiplier, int careerMultiplierCenti, int level, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        selected.Validate();
        if (skillValue < 0 || skillMultiplier < 0 || careerMultiplierCenti < 0 || level < 0 || level > selected.MaximumSkillLevel) throw new ArgumentOutOfRangeException();
        long powerMilli = selected.MilliScale;
        for (int current = 0; current < level; current++) powerMilli = checked(powerMilli * selected.SkillLevelPowerMilli / selected.MilliScale);
        long numerator = checked((long)skillValue * skillMultiplier * careerMultiplierCenti * powerMilli * selected.SkillUsesNumerator);
        return checked((int)(FloorDivide(numerator, selected.SkillUsesDenominator) + 1));
    }

    internal static int SkillAdvancementMultiplier(string skill)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skill);
        return SkillAdvancementMultipliers.TryGetValue(skill, out int value) ? value : throw new ArgumentException($"Unknown Daggerfall skill '{skill}'.", nameof(skill));
    }

    internal static int CalculateHitChance(int skill, int struckArmor, int attackerLuck, int targetLuck, int attackerAgility, int targetAgility, int targetDodging, DaggerfallFormulaTuning? tuning = null)
    {
        DaggerfallFormulaTuning selected = tuning.GetValueOrDefault(Classic);
        int chance = checked(skill + struckArmor + selected.HitChanceBase
            + TruncateDivide(attackerLuck - targetLuck, selected.HitChanceAttributeDivisor)
            + TruncateDivide(attackerAgility - targetAgility, selected.HitChanceAttributeDivisor)
            - FloorDivide(targetDodging, selected.HitChanceDodgingDivisor));
        return Math.Clamp(chance, selected.MinimumHitChance, selected.MaximumHitChance);
    }

    /// <summary>Classic material gate: a weapon must meet the target's minimum material.</summary>
    internal static bool CanHitMaterial(string? weaponMaterial, string? targetMinimumMaterial, IReadOnlyDictionary<string, int> weaponMaterialRanks)
    {
        if (targetMinimumMaterial is null) return true;
        if (weaponMaterial is null) return false;
        return weaponMaterialRanks.TryGetValue(weaponMaterial, out int weaponRank)
            && weaponMaterialRanks.TryGetValue(targetMinimumMaterial, out int targetRank)
            && weaponRank >= targetRank;
    }

    /// <summary>
    /// The classic weapon-material ladder is distinct from armor values:
    /// leather and chain are armor-only materials and cannot satisfy a weapon
    /// minimum-metal gate.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> ClassicWeaponMaterialRanks { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["iron"] = 0, ["steel"] = 1, ["silver"] = 2, ["elven"] = 3, ["dwarven"] = 4,
        ["mithril"] = 5, ["adamantium"] = 6, ["ebony"] = 7, ["orcish"] = 8, ["daedric"] = 9,
    };

    internal static int StruckBodyPart(int roll)
    {
        if ((uint)roll >= StruckBodyTable.Length) throw new ArgumentOutOfRangeException(nameof(roll));
        return StruckBodyTable[roll];
    }

    private static int FloorDivide(int value, int divisor) => value >= 0 ? value / divisor : -checked(((-value) + divisor - 1) / divisor);
    private static long FloorDivide(long value, long divisor) => value >= 0 ? value / divisor : -checked(((-value) + divisor - 1) / divisor);
    private static int TruncateDivide(int value, int divisor) => value / divisor;

    private static readonly int[] StruckBodyTable = [0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 6];

    internal static IReadOnlyDictionary<string, int> SkillAdvancementMultipliers { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["medical"] = 12, ["etiquette"] = 1, ["streetwise"] = 1, ["mercantile"] = 1, ["swimming"] = 1,
        ["backstabbing"] = 1, ["destruction"] = 1, ["illusion"] = 1, ["alteration"] = 1, ["mysticism"] = 1,
        ["archery"] = 1, ["lockpicking"] = 2, ["pickpocket"] = 2, ["stealth"] = 2, ["climbing"] = 2,
        ["restoration"] = 2, ["thaumaturgy"] = 2, ["short-blade"] = 2, ["long-blade"] = 2, ["hand-to-hand"] = 2,
        ["axe"] = 2, ["blunt-weapon"] = 2, ["dodging"] = 4, ["jumping"] = 5, ["critical-strike"] = 8,
        ["orcish"] = 15, ["harpy"] = 15, ["giantish"] = 15, ["dragonish"] = 15, ["nymph"] = 15,
        ["daedric"] = 15, ["spriggan"] = 15, ["centaurian"] = 15, ["impish"] = 15, ["running"] = 50,
    };
}

/// <summary>Named profile values used by <see cref="DaggerfallFormulaPolicy"/>.</summary>
internal sealed record DaggerfallFormulaTuning(
    int AttributeBaseline = 50,
    int AttributeDivisor = 10,
    int DamageModifierDivisor = 5,
    int ToHitAttributeDivisor = 10,
    int ToHitBaseline = 5,
    int EncumbranceNumerator = 3,
    int EncumbranceDenominator = 2,
    int BreathDivisor = 2,
    int FatigueUnitsPerAttributePoint = 64,
    int MilliScale = 1000,
    int HandToHandMinimumDivisor = 10,
    int HandToHandMaximumDivisor = 5,
    int RapidHealingBonus = 40,
    int HealthRecoveryBase = 60,
    int HealthRecoveryScale = 1000,
    int MinimumRecoveryRate = 1,
    int RecoveryDivisor = 8,
    int LevelFormulaOffset = 28,
    int LevelFormulaDivisor = 15,
    int ExperiencePerLevel = 500,
    int MinimumHitPointsPerLevel = 1,
    int HitPointsRollDivisor = 2,
    int ReflexesBaseline = 2,
    int ReflexesPenaltyMilli = 125,
    int SkillLevelPowerMilli = 1040,
    int SkillUsesNumerator = 2,
    int SkillUsesDenominator = 500000,
    int MaximumSkillLevel = 64,
    int HitChanceBase = -50,
    int HitChanceAttributeDivisor = 10,
    int HitChanceDodgingDivisor = 4,
    int MinimumHitChance = 3,
    int MaximumHitChance = 97)
{
    internal DaggerfallFormulaTuning Validate()
    {
        if (AttributeDivisor <= 0 || DamageModifierDivisor <= 0 || ToHitAttributeDivisor <= 0
            || EncumbranceDenominator <= 0 || BreathDivisor <= 0 || MilliScale <= 0
            || HandToHandMinimumDivisor <= 0 || HandToHandMaximumDivisor <= 0
            || HealthRecoveryScale <= 0 || RecoveryDivisor <= 0 || LevelFormulaDivisor <= 0
            || ExperiencePerLevel <= 0 || HitPointsRollDivisor <= 0 || SkillUsesDenominator <= 0
            || MaximumSkillLevel < 0 || HitChanceAttributeDivisor <= 0 || HitChanceDodgingDivisor <= 0
            || MinimumHitChance > MaximumHitChance)
            throw new ArgumentException("Daggerfall formula tuning contains an invalid divisor, bound, or level range.", nameof(DaggerfallFormulaTuning));
        return this;
    }
}

file static class NullableTuningExtensions
{
    internal static DaggerfallFormulaTuning GetValueOrDefault(this DaggerfallFormulaTuning? tuning, DaggerfallFormulaTuning fallback) => (tuning ?? fallback).Validate();
}
