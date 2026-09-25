using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

internal enum DaggerfallSpellTarget { CasterOnly, ByTouch, SingleTargetAtRange, AreaAroundCaster, AreaAtRange }

internal readonly record struct DaggerfallMagicCost(int Gold, int SpellPoints)
{
    internal DaggerfallMagicCost Validate()
    {
        if (Gold < 0 || SpellPoints < 0) throw new ArgumentOutOfRangeException(nameof(Gold));
        return this;
    }
}

internal sealed record DaggerfallItemEnchantmentQuote(int Capacity, int RequiredPoints, bool Eligible, string? Reason);

/// <summary>Pure classic spell and enchantment quotations over normalized, donor-derived coefficient rows.</summary>
internal static class DaggerfallMagicCostPolicy
{
    private const int CastingFloor = 5;

    /// <summary>
    /// FORM-11's ordinary effect-bundle quotation.  This is deliberately separate from
    /// <see cref="QuoteCasting"/>: the former sums gold components and applies the /400
    /// spell-point conversion per effect, while classic SPELLS.STD casting applies /100
    /// before the range shift.
    /// </summary>
    internal static DaggerfallMagicCost CalculateEffectCosts(DaggerfallMagicCatalogSet catalog,
        DaggerfallSpellEffectDefinition effect, IReadOnlyDictionary<string, int> schoolSkills)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(schoolSkills);

        DaggerfallMagicEffectCostDefinition row = catalog.RequireEffectCost(effect);
        int skill = RequireSkill(schoolSkills, row.School);
        int gold = CalculateRegularEffectGold(effect, row);
        int spellPoints = checked((int)(checked((long)gold * (110 - skill)) / 400));
        return new DaggerfallMagicCost(gold, spellPoints).Validate();
    }

    /// <summary>
    /// FORM-11's normal total-cost path.  An empty effect list is the donor's zero quote;
    /// the five-point floor is only applied to a non-empty bundle after its target multiplier.
    /// </summary>
    internal static DaggerfallMagicCost CalculateTotalEffectCosts(DaggerfallMagicCatalogSet catalog,
        IReadOnlyList<DaggerfallSpellEffectDefinition> effects, DaggerfallSpellTarget target,
        IReadOnlyDictionary<string, int> schoolSkills, bool minimumCastingCost = false)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(schoolSkills);
        if (effects.Count == 0) return new DaggerfallMagicCost(0, 0);

        long gold = 0;
        long spellPoints = 0;
        foreach (DaggerfallSpellEffectDefinition effect in effects)
        {
            DaggerfallMagicCost effectCost = CalculateEffectCosts(catalog, effect, schoolSkills);
            gold = checked(gold + effectCost.Gold);
            spellPoints = checked(spellPoints + effectCost.SpellPoints);
        }

        DaggerfallMagicCost targeted = ApplyTargetMultiplier(new(checked((int)gold), checked((int)spellPoints)), target);
        return new(targeted.Gold, minimumCastingCost ? CastingFloor : Math.Max(CastingFloor, targeted.SpellPoints));
    }

    /// <summary>Classic <c>CalculateCastingCost</c> over the SPELLS.STD coefficients.</summary>
    internal static DaggerfallMagicCost QuoteCasting(DaggerfallMagicCatalogSet catalog, DaggerfallSpellDefinition spell,
        IReadOnlyDictionary<string, int> schoolSkills, bool enchantingItem = false)
    {
        ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(spell); ArgumentNullException.ThrowIfNull(schoolSkills);
        DaggerfallSpellTarget target = TargetForRangeType(spell.RangeType);
        ValidateElement(spell.Element);
        long total = 0;
        foreach (DaggerfallSpellEffectDefinition effect in spell.Effects)
        {
            DaggerfallMagicEffectCostDefinition row = catalog.RequireEffectCost(effect);
            int skill = enchantingItem ? 50 : RequireSkill(schoolSkills, row.School);
            total = checked(total + checked((long)ClassicCostFromSettings(effect, row) * (110 - skill) / 100));
        }
        int points = ApplyTargetMultiplier(new DaggerfallMagicCost(0, checked((int)total)), target).SpellPoints;
        return new DaggerfallMagicCost(0, Math.Max(CastingFloor, points)).Validate();
    }

    /// <summary>Classic <c>GetSpellEnchantPtCost</c>, whose donor default uses the item-maker skill of fifty.</summary>
    internal static int SpellEnchantPoints(DaggerfallMagicCatalogSet catalog, DaggerfallSpellDefinition spell) =>
        checked(10 * QuoteCasting(catalog, spell, new Dictionary<string, int>(), enchantingItem: true).SpellPoints);

    /// <summary>FORM-11 target multiplier only. The casting floor belongs to the total-cost callers.</summary>
    internal static DaggerfallMagicCost ApplyTargetMultiplier(DaggerfallMagicCost cost, DaggerfallSpellTarget target)
    {
        cost.Validate();
        int multiplier = target switch
        {
            DaggerfallSpellTarget.CasterOnly or DaggerfallSpellTarget.ByTouch => 2,
            DaggerfallSpellTarget.SingleTargetAtRange => 3,
            DaggerfallSpellTarget.AreaAroundCaster => 4,
            DaggerfallSpellTarget.AreaAtRange => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        return new DaggerfallMagicCost(checked((int)(checked((long)cost.Gold * multiplier) / 2)),
            checked((int)(checked((long)cost.SpellPoints * multiplier) / 2))).Validate();
    }

    internal static int ItemEnchantmentPower(DaggerfallItemDefinition item, DaggerfallItemInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(item); ArgumentNullException.ThrowIfNull(metadata);
        if (item.Template is null) throw new ArgumentException($"Item '{item.Id.Value}' has no retained enchantment power.", nameof(item));
        int numerator = item.Weapon is not null ? MaterialMultiplier(metadata.Material, weapon: true)
            : item.Armor is not null || item.Shield is not null ? MaterialMultiplier(metadata.Material, weapon: false) : 4;
        long adjustment = checked((long)item.Template.EnchantmentPoints * (numerator - 4));
        return checked((int)(item.Template.EnchantmentPoints + FloorDivide(adjustment, 4)));
    }

    internal static DaggerfallItemEnchantmentQuote QuoteItemEnchantment(DaggerfallDefinitions definitions,
        DaggerfallItemDefinition item, DaggerfallItemInstanceMetadata metadata, DaggerfallMagicItemDefinition magic)
    {
        ArgumentNullException.ThrowIfNull(definitions); ArgumentNullException.ThrowIfNull(magic);
        if (magic.Type != 0)
            return new(0, 0, false, $"{magic.Key} is an artifact template, not an item-maker construction.");
        int capacity = ItemEnchantmentPower(item, metadata);
        long total = 0;
        foreach (DaggerfallMagicEnchantmentDefinition enchantment in magic.Enchantments)
        {
            int cost;
            if (enchantment.SpellKey is { } spellKey)
                cost = SpellEnchantPoints(definitions.Magic, definitions.Magic.Spells[spellKey]);
            else if (!TryGetNonSpellEnchantmentCost(enchantment, out cost))
                return new(capacity, 0, false, $"{magic.Key} has no retained item-maker cost for {enchantment.ParamMeaning} ({enchantment.Type}, {enchantment.Param}).");
            total = checked(total + cost);
        }
        if (total < 0)
            return new(capacity, 0, false, $"{magic.Key} would produce a negative construction payment.");
        int required = checked((int)total);
        return required <= capacity ? new(capacity, required, true, null) : new(capacity, required, false, "The item lacks enchantment capacity.");
    }

    internal static int WeaponEnchantmentMultiplierQuarter(string material) => MaterialMultiplier(material, weapon: true) - 4;
    internal static int ArmorEnchantmentMultiplierQuarter(string material) => MaterialMultiplier(material, weapon: false) - 4;

    private static int CalculateRegularEffectGold(DaggerfallSpellEffectDefinition value, DaggerfallMagicEffectCostDefinition row)
    {
        RequireSettingRanges(value);
        DaggerfallMagicEffectComponentCosts components = row.RegularComponents
            ?? throw new InvalidOperationException($"Effect ({row.Type}, {row.SubType}) has no retained regular component cost metadata.");

        long gold = 0;
        bool activeComponents = false;
        if (components.Duration is { } duration)
        {
            activeComponents = true;
            gold = checked(gold + duration.Calculate(value.DurationBase, value.DurationMod, value.DurationPerLevel));
        }
        if (components.Chance is { } chance)
        {
            activeComponents = true;
            gold = checked(gold + chance.Calculate(value.ChanceBase, value.ChanceMod, value.ChancePerLevel));
        }
        if (components.Magnitude is { } magnitude)
        {
            activeComponents = true;
            int magnitudeBase = checked((value.MagnitudeBaseHigh + value.MagnitudeBaseLow) / 2);
            int magnitudeIncrease = checked((value.MagnitudeLevelBase + value.MagnitudeLevelHigh) / 2);
            gold = checked(gold + magnitude.Calculate(magnitudeBase, magnitudeIncrease, value.MagnitudePerLevel));
        }

        // FormulaHelper deliberately assigns a synthetic component to effects such as Teleport
        // and MorphSelf. This preserves classic casting points while acknowledging the donor's
        // known zero-component gold-cost discrepancy.
        if (!activeComponents)
            gold = new DaggerfallMagicEffectComponentCost(160, 60, 100).Calculate(1, 1, 1);

        return checked((int)gold);
    }

    private static int ClassicCostFromSettings(DaggerfallSpellEffectDefinition value, DaggerfallMagicEffectCostDefinition row)
    {
        RequireSettingRanges(value);
        RequireDivisors(value, row.SettingsType);
        long averageBase = ((long)value.MagnitudeBaseHigh + value.MagnitudeBaseLow) / 2;
        long averageLevel = ((long)value.MagnitudeLevelHigh + value.MagnitudeLevelBase) / 2;
        return row.SettingsType switch
        {
            1 => checked((int)(row.Coefficient0 * (long)value.DurationBase + value.DurationMod / value.DurationPerLevel * (long)row.Coefficient1 + row.Coefficient2 * (long)value.ChanceBase + value.ChanceMod / value.ChancePerLevel * (long)row.Coefficient3)),
            2 => checked((int)(row.Coefficient0 * (long)value.DurationBase + value.DurationMod / value.DurationPerLevel * (long)row.Coefficient1 + averageBase * row.Coefficient2 + averageLevel / value.MagnitudePerLevel * (long)row.Coefficient3)),
            3 => checked((int)(row.Coefficient0 * (long)value.DurationBase + value.DurationMod / value.DurationPerLevel * (long)row.Coefficient1)),
            4 => checked((int)(row.Coefficient0 * (long)value.ChanceBase + value.ChanceMod / value.ChancePerLevel * (long)row.Coefficient1)),
            5 => checked((int)(averageBase * row.Coefficient0 + averageLevel / value.MagnitudePerLevel * (long)row.Coefficient1)),
            6 => checked((int)(row.Coefficient0 * (long)value.DurationBase + row.Coefficient1 * (long)value.DurationMod / value.DurationPerLevel + averageBase * row.Coefficient2 + row.Coefficient3 / value.MagnitudePerLevel * averageLevel)),
            7 => checked((int)(averageBase * row.Coefficient0
                + (long)row.Coefficient1 * (value.MagnitudeLevelBase + value.MagnitudeLevelHigh) / 2
                / value.MagnitudePerLevel * value.DurationBase / value.DurationMod)),
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        };
    }

    private static void RequireDivisors(DaggerfallSpellEffectDefinition value, int type)
    {
        if ((type is 1 or 2 or 3 or 6) && value.DurationPerLevel <= 0 || (type is 1 or 4) && value.ChancePerLevel <= 0
            || (type is 2 or 5 or 6 or 7) && value.MagnitudePerLevel <= 0 || type == 7 && value.DurationMod == 0)
            throw new ArgumentOutOfRangeException(nameof(value), "A retained cost setting has an invalid divisor.");
    }

    private static void RequireSettingRanges(DaggerfallSpellEffectDefinition value)
    {
        int[] values = [value.DurationBase, value.DurationMod, value.DurationPerLevel, value.ChanceBase, value.ChanceMod,
            value.ChancePerLevel, value.MagnitudeBaseLow, value.MagnitudeBaseHigh, value.MagnitudeLevelBase,
            value.MagnitudeLevelHigh, value.MagnitudePerLevel];
        if (values.Any(value => value is < 0 or > byte.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(value), "Classic spell settings are unsigned-byte values.");
    }

    private static int RequireSkill(IReadOnlyDictionary<string, int> values, string school) =>
        values.TryGetValue(school, out int skill) && skill is >= 0 and <= 100 ? skill : throw new ArgumentException($"A 0..100 '{school}' skill is required.", nameof(values));

    private static DaggerfallSpellTarget TargetForRangeType(int rangeType) => rangeType switch
    {
        0 => DaggerfallSpellTarget.CasterOnly,
        1 => DaggerfallSpellTarget.ByTouch,
        2 => DaggerfallSpellTarget.SingleTargetAtRange,
        3 => DaggerfallSpellTarget.AreaAroundCaster,
        4 => DaggerfallSpellTarget.AreaAtRange,
        _ => throw new ArgumentOutOfRangeException(nameof(rangeType), "A spell must retain a classic range type."),
    };

    private static void ValidateElement(int element)
    {
        if (element is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(element), "A spell must retain a classic element type.");
    }

    private static long FloorDivide(long value, long divisor) => value >= 0 ? value / divisor : -((checked(-value) + divisor - 1) / divisor);

    // Only these non-spell forms occur on retained regular MAGIC.DEF templates. Their values are
    // the item-maker settings from the donor effect classes, retained explicitly rather than
    // pretending every non-spell enchantment has a spell price.
    /// <summary>
    /// The retained item-maker cost of one non-spell enchantment payload. Two payloads belong to
    /// published magic items the item maker does not offer and stay here; every payload it does offer
    /// keeps its cost in the settings catalog, which is the donor's own table.
    /// </summary>
    internal static bool TryGetNonSpellEnchantmentCost(DaggerfallMagicEnchantmentDefinition value, out int cost)
    {
        ArgumentNullException.ThrowIfNull(value);
        if ((value.Type, value.Param) is (6, 1)) { cost = 1000; return true; }   // VampiricEffect.WhenStrikes
        if ((value.Type, value.Param) is (9, -1)) { cost = 1500; return true; }  // AbsorbsSpells
        return DaggerfallEnchantmentSettings.TryCost(value.Type, value.Param, out cost);
    }
    private static int MaterialMultiplier(string material, bool weapon) => material switch
    {
        "iron" => 3, "steel" or "leather" or "chain" => 4, "silver" or "adamantium" => 7,
        "elven" or "mithril" => 5, "dwarven" => 6, "ebony" => 8, "orcish" => 10, "daedric" => 12,
        _ => throw new ArgumentException($"{(weapon ? "Weapon" : "Armor")} material '{material}' has no classic enchantment multiplier.", nameof(material)),
    };
}
