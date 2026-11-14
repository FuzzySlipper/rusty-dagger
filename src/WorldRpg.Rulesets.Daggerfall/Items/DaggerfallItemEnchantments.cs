using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The enchantments an item instance carries, read from the magic catalog: the settings its maker chose when it was
/// made, else the payloads its catalog enchantment key owns.
/// </summary>
internal static class DaggerfallItemEnchantments
{
    /// <summary>The loaded payloads owned by one item key, including maker-authored settings.</summary>
    internal static bool TryEnchantments(this DaggerfallMagicCatalogSet magic, DaggerfallItemInstanceMetadata item, out IReadOnlyList<DaggerfallMagicEnchantmentDefinition> enchantments)
        => magic.TryEnchantments(item.Enchantment, item.MadeEnchantment, out enchantments);

    internal static bool TryEnchantments(this DaggerfallMagicCatalogSet magic, DaggerfallItemMetadataSave item, out IReadOnlyList<DaggerfallMagicEnchantmentDefinition> enchantments)
        => magic.TryEnchantments(item.Enchantment, item.MadeEnchantment, out enchantments);

    private static bool TryEnchantments(this DaggerfallMagicCatalogSet magic, string? key, DaggerfallMadeEnchantment? made, out IReadOnlyList<DaggerfallMagicEnchantmentDefinition> enchantments)
    {
        if (made is not null)
        {
            enchantments = made.Settings.Select((value, index) => DaggerfallEnchantmentSettings.ToEffect(magic.EnchantmentSettings[value.Key]) with { Key = $"{value.Key}.made.{index}" }).ToArray();
            return true;
        }
        if (key is not null) return magic.TryEnchantments(key, out enchantments);
        enchantments = [];
        return false;
    }
}
