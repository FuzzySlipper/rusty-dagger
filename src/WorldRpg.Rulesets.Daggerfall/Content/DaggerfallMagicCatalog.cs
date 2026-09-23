namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Stable Engine definition identity for one magic template over one selected base item.</summary>
internal static class DaggerfallMagicItemIds
{
    internal static string For(string baseItemId, string magicKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(magicKey);
        return $"{baseItemId}-magic-{magicKey.Replace('.', '-')}";
    }
}

/// <summary>One effect of a published spell, with the source's duration, chance and magnitude triples.</summary>
internal sealed record DaggerfallSpellEffectDefinition(
    string Key,
    int Type,
    int SubType,
    int DurationBase,
    int DurationMod,
    int DurationPerLevel,
    int ChanceBase,
    int ChanceMod,
    int ChancePerLevel,
    int MagnitudeBaseLow,
    int MagnitudeBaseHigh,
    int MagnitudeLevelBase,
    int MagnitudeLevelHigh,
    int MagnitudePerLevel);

/// <summary>One donor regular effect component's offset and setting coefficients.</summary>
internal sealed record DaggerfallMagicEffectComponentCost(int OffsetGold, int CostA, int CostB)
{
    internal int Calculate(int starting, int increase, int perLevel)
    {
        if (OffsetGold < 0 || CostA < 0 || CostB < 0 || starting < 0 || increase < 0 || perLevel <= 0)
            throw new ArgumentOutOfRangeException(nameof(perLevel), "A retained effect cost component requires non-negative settings and a positive level divisor.");
        return checked(OffsetGold + checked(CostA * starting) + checked(CostB * (increase / perLevel)));
    }
}

/// <summary>Donor EffectCosts rows for regular effect quotations, separate from classic SPELLS.STD coefficients.</summary>
internal sealed record DaggerfallMagicEffectComponentCosts(
    DaggerfallMagicEffectComponentCost? Duration,
    DaggerfallMagicEffectComponentCost? Chance,
    DaggerfallMagicEffectComponentCost? Magnitude);

/// <summary>Normalized classic coefficient row plus regular component metadata for one retained effect variant.</summary>
internal sealed record DaggerfallMagicEffectCostDefinition(
    int Type,
    int SubType,
    int SettingsType,
    string School,
    int Coefficient0,
    int Coefficient1,
    int Coefficient2,
    int Coefficient3,
    DaggerfallMagicEffectComponentCosts? RegularComponents = null);

internal static class DaggerfallMagicCostMetadata
{
    private static DaggerfallMagicEffectComponentCost D(int a, int b, int offset = 0) => new(offset, a, b);
    private static DaggerfallMagicEffectComponentCosts Duration(DaggerfallMagicEffectComponentCost value) => new(value, null, null);
    private static DaggerfallMagicEffectComponentCosts Chance(DaggerfallMagicEffectComponentCost value) => new(null, value, null);
    private static DaggerfallMagicEffectComponentCosts Magnitude(DaggerfallMagicEffectComponentCost value) => new(null, null, value);
    private static DaggerfallMagicEffectComponentCosts DurationChance(DaggerfallMagicEffectComponentCost duration, DaggerfallMagicEffectComponentCost chance) => new(duration, chance, null);
    private static DaggerfallMagicEffectComponentCosts DurationMagnitude(DaggerfallMagicEffectComponentCost duration, DaggerfallMagicEffectComponentCost magnitude) => new(duration, null, magnitude);

    /// <summary>
    /// The regular CalculateEffectCosts path uses these donor component rows. The payload's
    /// effectCosts rows intentionally retain the separate classic settings-type coefficients.
    /// </summary>
    internal static DaggerfallMagicEffectComponentCosts? For(int type, int subType) => (type, subType) switch
    {
        (0, -1) => DurationChance(D(28, 100), D(28, 100)),
        (1, 0) => DurationMagnitude(D(28, 8), D(40, 28)),
        (1, 1) => DurationMagnitude(D(20, 8), D(40, 28)),
        (1, 2) => DurationMagnitude(D(40, 8), D(40, 28)),
        (3, 0) or (3, 1) => Chance(D(8, 100)),
        (3, 2) => Chance(D(20, 140)),
        (4, 0) or (4, 2) => Magnitude(D(20, 28)),
        (5, -1) => Chance(D(80, 140)),
        (6, 0) or (6, 2) => Chance(D(120, 180)),
        (6, 1) => Chance(D(80, 140)),
        (7, 0) or (7, 1) or (7, 2) or (7, 3) or (7, 5) or (7, 6) => Magnitude(D(8, 100, 116)),
        (8, >= 0 and <= 3) => DurationChance(D(100, 100), D(8, 100)),
        (9, >= 0 and <= 7) => DurationMagnitude(D(28, 100), D(40, 120)),
        (10, 8) => Magnitude(D(20, 28)),
        (10, 9) => Magnitude(D(8, 28)),
        (11, 8) or (11, 9) => Magnitude(D(60, 100, 40)),
        (12, -1) => DurationChance(D(60, 68), D(40, 68)),
        (13, 0) => Duration(D(40, 120)),
        (14, -1) => Duration(D(60, 100)),
        (15, -1) => Duration(D(8, 40)),
        (16, -1) => Chance(D(28, 120, 120)),
        (17, -1) => Chance(D(20, 100)),
        (18, -1) => DurationMagnitude(D(100, 20), D(8, 8)),
        (19, -1) => DurationChance(D(20, 100), D(20, 100)),
        (20, -1) or (21, -1) => DurationChance(D(28, 140), D(28, 140)),
        (22, -1) => DurationChance(D(20, 100), D(20, 100)),
        (23, 0) or (24, 0) => Duration(D(20, 80)),
        (25, -1) => Duration(D(20, 100)),
        (27, -1) or (30, -1) or (31, -1) => Duration(D(20, 8)),
        (29, -1) or (43, -1) => new(null, null, null),
        (33, 0) => Chance(D(60, 100, 160)),
        (33, 1) or (33, 2) => Chance(D(80, 140, 60)),
        (34, -1) => DurationChance(D(20, 8), D(40, 60)),
        (35, -1) => DurationMagnitude(D(28, 8), D(80, 60)),
        (44, -1) => DurationChance(D(60, 68), D(40, 68)),
        _ => null,
    };
}

/// <summary>
/// A published classic spell. <see cref="Identity"/> is the source's own identity byte, which is not
/// unique in this corpus, so <see cref="IdentityShared"/> says when another spell claims it too.
/// </summary>
internal sealed record DaggerfallSpellDefinition(
    string Key,
    int Identity,
    bool IdentityShared,
    string Name,
    int Element,
    int RangeType,
    int Cost,
    int Icon,
    IReadOnlyList<DaggerfallSpellEffectDefinition> Effects);

/// <summary>One enchantment of a published magic item, with what its parameter names.</summary>
internal sealed record DaggerfallMagicEnchantmentDefinition(
    string Key,
    int Type,
    int Param,
    string ParamMeaning,
    string? SpellKey,
    bool SpellIdentityShared);

/// <summary>A published classic magic-item template and the spells its enchantments name.</summary>
internal sealed record DaggerfallMagicItemDefinition(
    string Key,
    long Offset,
    string Name,
    int Type,
    int Group,
    int GroupIndex,
    int Uses,
    int Value,
    int Material,
    IReadOnlyList<DaggerfallMagicEnchantmentDefinition> Enchantments);

/// <summary>A published record that carried no spell, kept so the catalog states what it did not carry.</summary>
internal sealed record DaggerfallMagicDisposition(long Offset, string Kind, string Reason);

/// <summary>
/// The published magical catalogs a spell or enchantment consumer resolves through. This is loaded from
/// the pack alone: no source file is read to resolve a spell, its effects or an item's enchantment links.
/// </summary>
internal sealed record DaggerfallMagicCatalogSet(
    IReadOnlyDictionary<string, DaggerfallSpellDefinition> Spells,
    IReadOnlyDictionary<string, DaggerfallMagicItemDefinition> MagicItems,
    IReadOnlyList<DaggerfallMagicDisposition> Dispositions,
    IReadOnlyList<string> SourceRecords,
    IReadOnlyDictionary<(int Type, int SubType), DaggerfallMagicEffectCostDefinition> EffectCosts)
{
    internal DaggerfallMagicEffectCostDefinition RequireEffectCost(DaggerfallSpellEffectDefinition effect) =>
        EffectCosts.TryGetValue((effect.Type, effect.SubType), out DaggerfallMagicEffectCostDefinition? cost)
            ? cost.RegularComponents is not null
                ? cost
                : cost with { RegularComponents = DaggerfallMagicCostMetadata.For(cost.Type, cost.SubType) }
            : throw new InvalidOperationException($"Published spell effect '{effect.Key}' has no normalized cost coefficients.");
    /// <summary>Every enchantment that resolved to a published spell, in key order.</summary>
    internal IEnumerable<DaggerfallMagicEnchantmentDefinition> ResolvedLinks =>
        MagicItems.Values
            .SelectMany(item => item.Enchantments)
            .Where(enchantment => enchantment.SpellKey is not null)
            .OrderBy(enchantment => enchantment.Key, StringComparer.Ordinal);
}
