using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallItemCastTriggers
{
    private IReadOnlyList<DaggerfallMagicEnchantmentDefinition> Enchantments(DaggerfallItemInstanceMetadata metadata) =>
        magic.TryEnchantments(metadata, out var values) ? values : [];

    /// <summary>The ItemMaker calls this when enchanting commits; an existing found item retains its original age.</summary>
    internal void Enchanted(ulong itemId)
    {
        var metadata = instances.RequireUnique(itemId);
        if (Enchantments(metadata).Any(value => value.Type == DaggerfallEnchantmentSettings.HealthLeechType))
            instances.ReplaceUnique(itemId, metadata with { HealthLeechLastUsedMinute = minuteIndex() });
    }

    private void ApplyLeechUse(long casterId, ulong itemId, IReadOnlyList<DaggerfallMagicEnchantmentDefinition> payloads, bool strike)
    {
        foreach (var payload in payloads.Where(value => value.Type == DaggerfallEnchantmentSettings.HealthLeechType))
        {
            instances.ReplaceUnique(itemId, instances.RequireUnique(itemId) with { HealthLeechLastUsedMinute = minuteIndex() });
            if (payload.Param == 0)
                (damageSource ?? throw new InvalidOperationException("Health leech requires the shared vitality owner."))(casterId, strike ? 8 : 16);
        }
    }

    private static bool MatchesEnemy(int parameter, DaggerfallEnemyGroup? enemy) => enemy is not null && parameter switch
    {
        0 => enemy == DaggerfallEnemyGroup.Undead,
        1 => enemy == DaggerfallEnemyGroup.Daedra,
        2 => enemy == DaggerfallEnemyGroup.Humanoid,
        3 => enemy == DaggerfallEnemyGroup.Animals,
        _ => false,
    };
}
