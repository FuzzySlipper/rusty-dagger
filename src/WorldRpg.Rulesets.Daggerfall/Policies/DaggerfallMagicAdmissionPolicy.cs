using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

[Flags]
internal enum DaggerfallMagicAllowedElements
{
    None = 0,
    Fire = 1,
    Cold = 2,
    Poison = 4,
    Shock = 8,
    Magic = 16,
}

internal enum DaggerfallMagicBundleElement
{
    None,
    Fire,
    Cold,
    Poison,
    Shock,
    Magic,
}

internal enum DaggerfallMagicResistanceElement
{
    None = -1,
    Fire,
    Frost,
    DiseaseOrPoison,
    Shock,
    Magic,
}

[Flags]
internal enum DaggerfallMagicEffectFlags
{
    None = 0,
    Paralysis = 1,
    Magic = 2,
    Poison = 4,
    Fire = 8,
    Frost = 16,
    Shock = 32,
    Disease = 64,
}

internal enum DaggerfallMagicTolerance
{
    Normal,
    Immune,
    Resistant,
    LowTolerance,
    CriticalWeakness,
}

[Flags]
internal enum DaggerfallMagicToleranceFlags
{
    Normal = 0,
    Immune = 1,
    Resistant = 2,
    LowTolerance = 4,
    CriticalWeakness = 8,
}

/// <summary>
/// Runtime facts the donor reads from one instantiated effect and its parent spell bundle.
/// A missing source or parent bundle is represented explicitly for the two overloads that define
/// fallback behavior; element/flag extraction requires an instantiated bundle.
/// </summary>
internal sealed record DaggerfallMagicEffectSource(
    bool HasParentBundle,
    bool IsParalysis,
    bool IsDisease,
    DaggerfallMagicAllowedElements AllowedElements,
    DaggerfallMagicBundleElement BundleElement)
{
    internal DaggerfallMagicEffectSource Validate()
    {
        const DaggerfallMagicAllowedElements all = DaggerfallMagicAllowedElements.Fire
            | DaggerfallMagicAllowedElements.Cold | DaggerfallMagicAllowedElements.Poison
            | DaggerfallMagicAllowedElements.Shock | DaggerfallMagicAllowedElements.Magic;
        if ((AllowedElements & ~all) != 0 || !Enum.IsDefined(BundleElement))
            throw new ArgumentOutOfRangeException(nameof(AllowedElements), "Magic effect element metadata contains unknown values.");
        return this;
    }
}

/// <summary>The career tolerances consulted for each classic saving-throw effect flag.</summary>
internal sealed record DaggerfallMagicCareerTolerances(
    DaggerfallMagicTolerance Paralysis,
    DaggerfallMagicTolerance Magic,
    DaggerfallMagicTolerance Poison,
    DaggerfallMagicTolerance Fire,
    DaggerfallMagicTolerance Frost,
    DaggerfallMagicTolerance Shock,
    DaggerfallMagicTolerance Disease)
{
    internal DaggerfallMagicCareerTolerances Validate()
    {
        foreach (DaggerfallMagicTolerance value in new[] { Paralysis, Magic, Poison, Fire, Frost, Shock, Disease })
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), "A career magic tolerance is unknown.");
        return this;
    }
}

/// <summary>Career-derived race flags, already resolved into the same effect vocabulary as a spell.</summary>
internal sealed record DaggerfallMagicRaceToleranceFlags(
    DaggerfallMagicEffectFlags Resistance,
    DaggerfallMagicEffectFlags Immunity,
    DaggerfallMagicEffectFlags LowTolerance,
    DaggerfallMagicEffectFlags CriticalWeakness)
{
    internal DaggerfallMagicRaceToleranceFlags Validate()
    {
        const DaggerfallMagicEffectFlags all = DaggerfallMagicEffectFlags.Paralysis
            | DaggerfallMagicEffectFlags.Magic | DaggerfallMagicEffectFlags.Poison
            | DaggerfallMagicEffectFlags.Fire | DaggerfallMagicEffectFlags.Frost
            | DaggerfallMagicEffectFlags.Shock | DaggerfallMagicEffectFlags.Disease;
        if (((Resistance | Immunity | LowTolerance | CriticalWeakness) & ~all) != 0)
            throw new ArgumentOutOfRangeException(nameof(Resistance), "Race magic resistance flags contain unknown values.");
        return this;
    }
}

/// <summary>The target's live defensive bonuses in donor percentage/modifier points.</summary>
internal sealed record DaggerfallMagicResistanceModifiers(
    int DiseaseOrPoison,
    int Fire,
    int Frost,
    int Shock,
    int Magic);

/// <summary>One active resistance spell channel. A matching channel consumes one donor percentile roll.</summary>
internal sealed record DaggerfallMagicActiveResistance(DaggerfallMagicResistanceElement Element, int Chance)
{
    internal DaggerfallMagicActiveResistance Validate()
    {
        if (!Enum.IsDefined(Element) || Element == DaggerfallMagicResistanceElement.None)
            throw new ArgumentOutOfRangeException(nameof(Element));
        ArgumentOutOfRangeException.ThrowIfNegative(Chance);
        return this;
    }
}

/// <summary>
/// Explicit target facts required by FORM-06. MagicResistance is live Willpower / 10; biography
/// modifiers are supplied only for the player because the donor applies them only to that actor.
/// </summary>
internal sealed record DaggerfallMagicTargetProfile(
    int LiveWillpower,
    DaggerfallMagicCareerTolerances CareerTolerances,
    DaggerfallMagicRaceToleranceFlags? PlayerRaceTolerances,
    int BiographyMagicResistance,
    int BiographyPoisonResistance,
    int BiographyDiseaseResistance,
    DaggerfallMagicResistanceModifiers ResistanceModifiers,
    DaggerfallMagicActiveResistance[] ActiveResistances)
{
    internal DaggerfallMagicTargetProfile Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(LiveWillpower);
        ArgumentNullException.ThrowIfNull(CareerTolerances);
        _ = CareerTolerances.Validate();
        _ = PlayerRaceTolerances?.Validate();
        ArgumentNullException.ThrowIfNull(ResistanceModifiers);
        ArgumentNullException.ThrowIfNull(ActiveResistances);
        HashSet<DaggerfallMagicResistanceElement> elements = [];
        foreach (DaggerfallMagicActiveResistance resistance in ActiveResistances)
        {
            ArgumentNullException.ThrowIfNull(resistance);
            _ = resistance.Validate();
            if (!elements.Add(resistance.Element))
                throw new ArgumentException("A target cannot have duplicate active resistance channels for one element.", nameof(ActiveResistances));
        }
        return this with { ActiveResistances = (DaggerfallMagicActiveResistance[])ActiveResistances.Clone() };
    }

    internal int? ActiveResistanceChance(DaggerfallMagicResistanceElement element) =>
        ActiveResistances.FirstOrDefault(resistance => resistance.Element == element)?.Chance;
}

/// <summary>
/// Source-backed Daggerfall magic formulas. The caller supplies typed live actor/effect facts and
/// the Engine-backed percentile draw; this policy does not create casters, targets, or effects.
/// </summary>
internal static class DaggerfallMagicAdmissionPolicy
{
    private static readonly IReadOnlyDictionary<DaggerfallMagicTolerance, DaggerfallMagicToleranceFlags> ToleranceFlags =
        new Dictionary<DaggerfallMagicTolerance, DaggerfallMagicToleranceFlags>
        {
            [DaggerfallMagicTolerance.Normal] = DaggerfallMagicToleranceFlags.Normal,
            [DaggerfallMagicTolerance.Immune] = DaggerfallMagicToleranceFlags.Immune,
            [DaggerfallMagicTolerance.Resistant] = DaggerfallMagicToleranceFlags.Resistant,
            [DaggerfallMagicTolerance.LowTolerance] = DaggerfallMagicToleranceFlags.LowTolerance,
            [DaggerfallMagicTolerance.CriticalWeakness] = DaggerfallMagicToleranceFlags.CriticalWeakness,
        };

    private static readonly IReadOnlyDictionary<DaggerfallMagicBundleElement, DaggerfallMagicEffectFlags> BundleEffectFlags =
        new Dictionary<DaggerfallMagicBundleElement, DaggerfallMagicEffectFlags>
        {
            [DaggerfallMagicBundleElement.Fire] = DaggerfallMagicEffectFlags.Fire,
            [DaggerfallMagicBundleElement.Cold] = DaggerfallMagicEffectFlags.Frost,
            [DaggerfallMagicBundleElement.Poison] = DaggerfallMagicEffectFlags.Poison,
            [DaggerfallMagicBundleElement.Shock] = DaggerfallMagicEffectFlags.Shock,
            [DaggerfallMagicBundleElement.Magic] = DaggerfallMagicEffectFlags.Magic,
        };

    /// <summary>FORM-06.CalculateCasterLevel: a missing caster has donor level one; a live caster keeps its level.</summary>
    internal static int CalculateCasterLevel(int? casterLevel) => casterLevel ?? 1;

    /// <summary>
    /// FORM-11.CalculateCastingCost projection. The established construction/cost owner performs
    /// the coefficient, school-skill, target multiplier and five-point-floor arithmetic.
    /// The donor overload defaults <paramref name="enchantingItem"/> to true.
    /// </summary>
    internal static int CalculateCastingCost(
        DaggerfallMagicCatalogSet catalog,
        DaggerfallSpellDefinition spell,
        IReadOnlyDictionary<string, int>? schoolSkills = null,
        bool enchantingItem = true)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(spell);
        IReadOnlyDictionary<string, int> skills = schoolSkills ?? EmptySkills;
        return DaggerfallMagicCostPolicy.QuoteCasting(catalog, spell, skills, enchantingItem).SpellPoints;
    }

    /// <summary>FORM-06.GetToleranceFlag maps one career tolerance to its OR-composable donor flag.</summary>
    internal static DaggerfallMagicToleranceFlags GetToleranceFlag(DaggerfallMagicTolerance tolerance) =>
        ToleranceFlags.TryGetValue(tolerance, out DaggerfallMagicToleranceFlags flags)
            ? flags
            : throw new ArgumentOutOfRangeException(nameof(tolerance));

    /// <summary>FORM-06.GetEffectFlags maps instantiated effect traits and parent-bundle element to donor effect flags.</summary>
    internal static DaggerfallMagicEffectFlags GetEffectFlags(DaggerfallMagicEffectSource effect)
    {
        DaggerfallMagicEffectSource source = RequireBundledEffect(effect);
        DaggerfallMagicEffectFlags flags = DaggerfallMagicEffectFlags.None;
        if (source.IsParalysis) flags |= DaggerfallMagicEffectFlags.Paralysis;
        if (source.IsDisease) flags |= DaggerfallMagicEffectFlags.Disease;
        if (BundleEffectFlags.TryGetValue(source.BundleElement, out DaggerfallMagicEffectFlags elementFlag))
            flags |= elementFlag;
        return flags;
    }

    /// <summary>FORM-06.GetElementType; disease and poison share the donor resistance channel.</summary>
    internal static DaggerfallMagicResistanceElement GetElementType(DaggerfallMagicEffectSource effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        DaggerfallMagicEffectSource source = effect.Validate();
        if (source.AllowedElements == DaggerfallMagicAllowedElements.Magic)
            return DaggerfallMagicResistanceElement.Magic;
        if (!source.HasParentBundle)
            throw new ArgumentException("Element extraction requires a parent bundle for a non-magic-only effect.", nameof(effect));
        return source.BundleElement switch
        {
            DaggerfallMagicBundleElement.Fire => DaggerfallMagicResistanceElement.Fire,
            DaggerfallMagicBundleElement.Cold => DaggerfallMagicResistanceElement.Frost,
            DaggerfallMagicBundleElement.Poison => DaggerfallMagicResistanceElement.DiseaseOrPoison,
            DaggerfallMagicBundleElement.Shock => DaggerfallMagicResistanceElement.Shock,
            DaggerfallMagicBundleElement.Magic => DaggerfallMagicResistanceElement.Magic,
            _ => DaggerfallMagicResistanceElement.None,
        };
    }

    /// <summary>FORM-06.GetResistanceModifier uses the donor's single best matching live resistance.</summary>
    internal static int GetResistanceModifier(
        DaggerfallMagicEffectFlags effectFlags,
        DaggerfallMagicResistanceModifiers targetResistance)
    {
        ValidateEffectFlags(effectFlags);
        ArgumentNullException.ThrowIfNull(targetResistance);
        if ((effectFlags & (DaggerfallMagicEffectFlags.Disease | DaggerfallMagicEffectFlags.Poison)) != 0)
            return targetResistance.DiseaseOrPoison;
        if ((effectFlags & DaggerfallMagicEffectFlags.Fire) != 0) return targetResistance.Fire;
        if ((effectFlags & DaggerfallMagicEffectFlags.Frost) != 0) return targetResistance.Frost;
        if ((effectFlags & DaggerfallMagicEffectFlags.Shock) != 0) return targetResistance.Shock;
        if ((effectFlags & DaggerfallMagicEffectFlags.Magic) != 0) return targetResistance.Magic;
        return 0;
    }

    /// <summary>
    /// FORM-06.SavingThrow direct-input overload. The callback is asked for an inclusive 1–100 roll
    /// only when its stage is reached: active resistance first, then the ordinary saving throw.
    /// </summary>
    internal static int SavingThrow(
        DaggerfallMagicResistanceElement elementType,
        DaggerfallMagicEffectFlags effectFlags,
        DaggerfallMagicTargetProfile target,
        int modifier,
        Func<int> rollPercentile)
    {
        if (!Enum.IsDefined(elementType)) throw new ArgumentOutOfRangeException(nameof(elementType));
        ValidateEffectFlags(effectFlags);
        DaggerfallMagicTargetProfile profile = (target ?? throw new ArgumentNullException(nameof(target))).Validate();
        ArgumentNullException.ThrowIfNull(rollPercentile);

        if (elementType != DaggerfallMagicResistanceElement.None
            && profile.ActiveResistanceChance(elementType) is int activeChance)
        {
            int resistanceRoll = RollPercentile(rollPercentile);
            if (resistanceRoll <= Math.Clamp(activeChance, 0, 100))
                return 0;
        }

        int savingThrow = 50;
        DaggerfallMagicRaceToleranceFlags? race = profile.PlayerRaceTolerances;
        bool raceImmune = false;
        bool raceCriticalWeakness = false;
        if (race is not null)
        {
            if (RaceMatches(elementType, race.Resistance, effectFlags)) savingThrow += 30;
            raceImmune = RaceMatches(elementType, race.Immunity, effectFlags);
            if (RaceMatches(elementType, race.LowTolerance, effectFlags)) savingThrow -= 25;
            raceCriticalWeakness = RaceMatches(elementType, race.CriticalWeakness, effectFlags);
        }

        DaggerfallMagicToleranceFlags tolerances = CareerToleranceFlags(profile.CareerTolerances, effectFlags);
        // The donor identifies the additive mixed-tolerance handling as a deviation from
        // classic: immunity wins; otherwise critical weakness permits full effect.
        if (raceImmune || (tolerances & DaggerfallMagicToleranceFlags.Immune) != 0) return 0;
        if (raceCriticalWeakness || (tolerances & DaggerfallMagicToleranceFlags.CriticalWeakness) != 0) return 100;
        if ((tolerances & DaggerfallMagicToleranceFlags.LowTolerance) != 0) savingThrow -= 25;
        if ((tolerances & DaggerfallMagicToleranceFlags.Resistant) != 0) savingThrow += 25;

        int biographyModifier = 0;
        if ((effectFlags & DaggerfallMagicEffectFlags.Magic) != 0)
            biographyModifier = checked(biographyModifier + profile.BiographyMagicResistance);
        if ((effectFlags & DaggerfallMagicEffectFlags.Poison) != 0)
            biographyModifier = checked(biographyModifier + profile.BiographyPoisonResistance);
        if ((effectFlags & DaggerfallMagicEffectFlags.Disease) != 0)
            biographyModifier = checked(biographyModifier + profile.BiographyDiseaseResistance);
        savingThrow = checked(savingThrow + biographyModifier + modifier);

        // The donor returns a complete resistance before adding MagicResist or consuming its save roll.
        if (savingThrow >= 100)
            return 0;

        savingThrow = Math.Clamp(checked(savingThrow + DaggerfallFormulaPolicy.MagicResist(profile.LiveWillpower)), 5, 95);
        int saveRoll = RollPercentile(rollPercentile);
        if (saveRoll > savingThrow)
            return 100;
        int amountPercent = savingThrow - 20 <= saveRoll
            ? 100 - 5 * (savingThrow - saveRoll)
            : 0;
        return Math.Clamp(amountPercent, 0, 100);
    }

    /// <summary>FORM-06.SavingThrow source-effect overload, including unbundled-effect fallback and modifier derivation.</summary>
    internal static int SavingThrow(
        DaggerfallMagicEffectSource? sourceEffect,
        DaggerfallMagicTargetProfile target,
        Func<int> rollPercentile)
    {
        if (sourceEffect is null || !sourceEffect.HasParentBundle)
            return 100;
        DaggerfallMagicEffectFlags flags = GetEffectFlags(sourceEffect);
        DaggerfallMagicResistanceElement element = GetElementType(sourceEffect);
        int modifier = GetResistanceModifier(flags, (target ?? throw new ArgumentNullException(nameof(target))).ResistanceModifiers);
        return SavingThrow(element, flags, target, modifier, rollPercentile);
    }

    /// <summary>FORM-06.ModifyEffectAmount uses the donor percentage as float and truncates toward zero.</summary>
    internal static int ModifyEffectAmount(
        DaggerfallMagicEffectSource? sourceEffect,
        DaggerfallMagicTargetProfile target,
        int amount,
        Func<int> rollPercentile)
    {
        if (sourceEffect is null || !sourceEffect.HasParentBundle)
            return amount;
        int amountPercent = SavingThrow(sourceEffect, target, rollPercentile);
        float percent = amountPercent / 100f;
        return (int)(amount * percent);
    }

    private static readonly IReadOnlyDictionary<string, int> EmptySkills = new Dictionary<string, int>(StringComparer.Ordinal);

    private static DaggerfallMagicEffectSource RequireBundledEffect(DaggerfallMagicEffectSource effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        DaggerfallMagicEffectSource source = effect.Validate();
        if (!source.HasParentBundle)
            throw new ArgumentException("Effect flag and element extraction require a parent bundle.", nameof(effect));
        return source;
    }

    private static DaggerfallMagicToleranceFlags CareerToleranceFlags(
        DaggerfallMagicCareerTolerances career,
        DaggerfallMagicEffectFlags effects)
    {
        DaggerfallMagicToleranceFlags result = DaggerfallMagicToleranceFlags.Normal;
        if ((effects & DaggerfallMagicEffectFlags.Paralysis) != 0) result |= GetToleranceFlag(career.Paralysis);
        if ((effects & DaggerfallMagicEffectFlags.Magic) != 0) result |= GetToleranceFlag(career.Magic);
        if ((effects & DaggerfallMagicEffectFlags.Poison) != 0) result |= GetToleranceFlag(career.Poison);
        if ((effects & DaggerfallMagicEffectFlags.Fire) != 0) result |= GetToleranceFlag(career.Fire);
        if ((effects & DaggerfallMagicEffectFlags.Frost) != 0) result |= GetToleranceFlag(career.Frost);
        if ((effects & DaggerfallMagicEffectFlags.Shock) != 0) result |= GetToleranceFlag(career.Shock);
        if ((effects & DaggerfallMagicEffectFlags.Disease) != 0) result |= GetToleranceFlag(career.Disease);
        return result;
    }

    private static bool RaceMatches(
        DaggerfallMagicResistanceElement element,
        DaggerfallMagicEffectFlags raceFlags,
        DaggerfallMagicEffectFlags effectFlags)
    {
        bool elementMatch = element switch
        {
            DaggerfallMagicResistanceElement.Fire => (raceFlags & DaggerfallMagicEffectFlags.Fire) != 0,
            DaggerfallMagicResistanceElement.Frost => (raceFlags & DaggerfallMagicEffectFlags.Frost) != 0,
            DaggerfallMagicResistanceElement.DiseaseOrPoison =>
                (raceFlags & effectFlags & (DaggerfallMagicEffectFlags.Disease | DaggerfallMagicEffectFlags.Poison)) != 0,
            DaggerfallMagicResistanceElement.Shock => (raceFlags & DaggerfallMagicEffectFlags.Shock) != 0,
            DaggerfallMagicResistanceElement.Magic => (raceFlags & DaggerfallMagicEffectFlags.Magic) != 0,
            _ => false,
        };
        bool paralysisMatch = (effectFlags & DaggerfallMagicEffectFlags.Paralysis) != 0
            && (raceFlags & DaggerfallMagicEffectFlags.Paralysis) != 0;
        return elementMatch || paralysisMatch;
    }

    private static void ValidateEffectFlags(DaggerfallMagicEffectFlags flags)
    {
        const DaggerfallMagicEffectFlags all = DaggerfallMagicEffectFlags.Paralysis
            | DaggerfallMagicEffectFlags.Magic | DaggerfallMagicEffectFlags.Poison
            | DaggerfallMagicEffectFlags.Fire | DaggerfallMagicEffectFlags.Frost
            | DaggerfallMagicEffectFlags.Shock | DaggerfallMagicEffectFlags.Disease;
        if ((flags & ~all) != 0)
            throw new ArgumentOutOfRangeException(nameof(flags));
    }

    private static int RollPercentile(Func<int> rollPercentile)
    {
        int roll = rollPercentile();
        if (roll is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(rollPercentile), "A Daggerfall percentile roll is inclusive from 1 through 100.");
        return roll;
    }
}
