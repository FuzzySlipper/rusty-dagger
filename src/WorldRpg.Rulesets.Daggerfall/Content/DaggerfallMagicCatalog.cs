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
    IReadOnlyList<string> SourceRecords)
{
    /// <summary>Every enchantment that resolved to a published spell, in key order.</summary>
    internal IEnumerable<DaggerfallMagicEnchantmentDefinition> ResolvedLinks =>
        MagicItems.Values
            .SelectMany(item => item.Enchantments)
            .Where(enchantment => enchantment.SpellKey is not null)
            .OrderBy(enchantment => enchantment.Key, StringComparer.Ordinal);
}
