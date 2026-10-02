using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using KitItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Captured creature meaning on actual inventory items, shared by trapping and soul consumers.</summary>
internal sealed class DaggerfallSoulGems(MechanicsInventoryCoordinator inventory, DaggerfallItemInstances instances,
    DaggerfallMagicCatalogSet magic, DaggerfallUniqueItemAllocator identities)
{
    internal static bool IsStar(DaggerfallItemInstanceMetadata item, DaggerfallMagicCatalogSet magic) =>
        item.Enchantment is { } key && magic.TryEnchantments(key, out var payloads)
            && payloads.Any(value => value.Type == 26 && value.Param == 9);
    internal static bool IsTrap(DaggerfallItemInstanceMetadata item, DaggerfallMagicCatalogSet magic) =>
        item.ItemId == "template-274" || IsStar(item, magic);

    internal bool Capture(int mobileId, bool starOnly = false)
    {
        if (mobileId is < 0 or > 42) throw new ArgumentOutOfRangeException(nameof(mobileId));
        var candidates = inventory.Read().UniqueItems.Select(item =>
            (Item: item, Id: inventory.GetDurableItemId(item.Entity), Metadata: instances.RequireUnique(inventory.GetDurableItemId(item.Entity).Value)))
            .Where(value => value.Metadata.CapturedSoulMobileId is null && IsTrap(value.Metadata, magic)
                && (!starOnly || IsStar(value.Metadata, magic)))
            .OrderByDescending(value => IsStar(value.Metadata, magic)).ThenBy(value => value.Id.Value).ToArray();
        if (candidates.Length == 0) return false;
        var chosen = candidates[0];
        instances.ReplaceUnique(chosen.Id.Value, chosen.Metadata with { CapturedSoulMobileId = mobileId });
        return true;
    }

    /// <summary>Ordinary filled gems are consumed first; the Star is emptied and remains reusable.</summary>
    internal bool Consume(int mobileId)
    {
        if (mobileId is < 0 or > 42) throw new ArgumentOutOfRangeException(nameof(mobileId));
        var candidates = inventory.Read().UniqueItems.Select(item =>
            (Item: item, Id: inventory.GetDurableItemId(item.Entity), Metadata: instances.RequireUnique(inventory.GetDurableItemId(item.Entity).Value)))
            .Where(value => value.Metadata.CapturedSoulMobileId == mobileId && IsTrap(value.Metadata, magic))
            .OrderBy(value => IsStar(value.Metadata, magic)).ThenBy(value => value.Id.Value).ToArray();
        if (candidates.Length == 0) return false;
        var chosen = candidates[0];
        if (IsStar(chosen.Metadata, magic)) instances.ReplaceUnique(chosen.Id.Value, chosen.Metadata with { CapturedSoulMobileId = null });
        else
        {
            inventory.Destroy(new KitItem(chosen.Item.Entity.Value, new InventoryItemId(chosen.Item.Definition.Value)));
            inventory.Entities.Destroy(chosen.Id);
            instances.RemoveUnique(chosen.Id.Value);
            identities.Remove(chosen.Id);
        }
        return true;
    }
}
