using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using KitItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallStarCaptureOutcome { Captured, Occupied, Ineligible, Unavailable }
internal sealed record DaggerfallStarCaptureResult(DaggerfallStarCaptureOutcome Outcome, ulong? ItemId);

/// <summary>Captured creature meaning on actual inventory items, shared by trapping and soul consumers.</summary>
internal sealed class DaggerfallSoulGems(MechanicsInventoryCoordinator inventory, DaggerfallItemInstances instances,
    DaggerfallMagicCatalogSet magic, DaggerfallUniqueItemAllocator identities)
{
    internal static bool IsStar(DaggerfallItemInstanceMetadata item, DaggerfallMagicCatalogSet magic) =>
        magic.TryEnchantments(item, out var payloads)
            && payloads.Any(value => value.Type == 26 && value.Param == 9);
    internal static bool IsTrap(DaggerfallItemInstanceMetadata item, DaggerfallMagicCatalogSet magic) =>
        item.ItemId == "template-274" || IsStar(item, magic);

    internal bool Capture(int mobileId, bool starOnly = false)
    {
        if (mobileId is < 0 or > 42) throw new ArgumentOutOfRangeException(nameof(mobileId));
        var candidates = CurrentItems()
            .Where(value => value.Metadata.CapturedSoulMobileId is null && IsTrap(value.Metadata, magic)
                && (!starOnly || IsStar(value.Metadata, magic)))
            .OrderByDescending(value => IsStar(value.Metadata, magic)).ThenBy(value => value.Id.Value).ToArray();
        if (candidates.Length == 0) return false;
        var chosen = candidates[0];
        instances.ReplaceUnique(chosen.Id.Value, chosen.Metadata with { CapturedSoulMobileId = mobileId });
        return true;
    }

    internal DaggerfallStarCaptureResult CaptureStar(int? mobileId)
    {
        var stars = CurrentItems().Where(value => IsStar(value.Metadata, magic)).OrderBy(value => value.Id.Value).ToArray();
        if (stars.Length == 0) return new(DaggerfallStarCaptureOutcome.Unavailable, null);
        if (mobileId is null or < 0 or > 42) return new(DaggerfallStarCaptureOutcome.Ineligible, stars[0].Id.Value);
        var empty = stars.Where(value => value.Metadata.CapturedSoulMobileId is null).ToArray();
        if (empty.Length == 0) return new(DaggerfallStarCaptureOutcome.Occupied, stars[0].Id.Value);
        var chosen = empty[0];
        instances.ReplaceUnique(chosen.Id.Value, chosen.Metadata with { CapturedSoulMobileId = mobileId });
        return new(DaggerfallStarCaptureOutcome.Captured, chosen.Id.Value);
    }

    /// <summary>The Used payload releases the stored soul and keeps the same reusable artifact.</summary>
    internal int? ReleaseStar(ulong itemId)
    {
        var item = instances.RequireUnique(itemId);
        if (!IsStar(item, magic)) throw new ArgumentException("The used item is not Azura's Star.", nameof(itemId));
        if (item.CapturedSoulMobileId is not { } soul) return null;
        instances.ReplaceUnique(itemId, item with { CapturedSoulMobileId = null });
        return soul;
    }

    /// <summary>Ordinary filled gems are consumed first; the Star is emptied and remains reusable.</summary>
    internal bool HasSoul(int mobileId) => CurrentItems().Any(value => value.Metadata.CapturedSoulMobileId == mobileId && IsTrap(value.Metadata, magic));

    internal int[] AvailableSouls() => CurrentItems().Where(value => IsTrap(value.Metadata, magic) && value.Metadata.CapturedSoulMobileId is not null)
        .Select(value => value.Metadata.CapturedSoulMobileId!.Value).Distinct().Order().ToArray();

    internal bool Consume(int mobileId)
    {
        if (mobileId is < 0 or > 42) throw new ArgumentOutOfRangeException(nameof(mobileId));
        var candidates = CurrentItems()
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

    private IEnumerable<(Rusty.Engine.Mechanics.UniqueInventoryItem Item, DurableIdentityReference Id, DaggerfallItemInstanceMetadata Metadata)> CurrentItems() =>
        inventory.Read().UniqueItems.Select(item =>
        {
            var id = inventory.GetDurableItemId(item.Entity);
            return (item, id, instances.RequireUnique(id.Value));
        });
}
