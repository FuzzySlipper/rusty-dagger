using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>
/// Compiled Daggerfall equipment-placement policy: which slots an item fits, how many slots it
/// occupies, and which equipped items conflict with placing it. Pure interpretation of the
/// admitted definitions; the caller owns the Engine mutation.
/// </summary>
internal static class DaggerfallEquipmentPolicy
{
    internal static bool IsCompatible(DaggerfallDefinitions definitions, DaggerfallItemDefinition item, string slot)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        if (item.IsFungible || item.Equipment is null
            || !definitions.EquipmentSlots.TryGetValue(new DaggerfallEquipmentSlotId(slot), out DaggerfallEquipmentSlotDefinition? target))
            return false;
        if (!target.AllowedClassifications.Intersect(item.Equipment.Classifications).Any()) return false;
        return item.Equipment.RequiredSlots == 1 || item.Equipment.RequiredSlots == 2 && slot == "right-hand";
    }

    /// <summary>The slots placing this item occupies: a two-handed item takes both hands.</summary>
    internal static IReadOnlyList<EquipmentSlotId> TargetSlots(DaggerfallItemDefinition item, EquipmentSlotId requested)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Equipment is { RequiredSlots: 2 }) return [new EquipmentSlotId("right-hand"), new EquipmentSlotId("left-hand")];
        return [requested];
    }

    /// <summary>
    /// Equipped items placing <paramref name="item"/> into <paramref name="slots"/> must displace:
    /// anything already in those slots, plus anything sharing the item's exclusive group. The item
    /// itself is never its own conflict. One entry per conflicting item: a two-handed occupant
    /// matches twice (slot overlap plus group) but displaces once.
    /// </summary>
    internal static IReadOnlyList<UniqueInventoryItem> ConflictingEquipped(
        DaggerfallDefinitions definitions,
        EquipmentRead equipped,
        UniqueInventoryItem item,
        DaggerfallItemDefinition definition,
        IReadOnlyList<EquipmentSlotId> slots)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(equipped);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(slots);
        return equipped.Assignments
            .Where(assignment => assignment.Item.EntityId != item.EntityId
                && (slots.Contains(assignment.Slot)
                    || definition.Equipment?.ExclusiveGroup is string group && group != "hands"
                        && definitions.TryResolveItem(new DaggerfallItemId(assignment.Item.Definition.Value), out DaggerfallItemDefinition other)
                        && other.Equipment?.ExclusiveGroup == group))
            .DistinctBy(assignment => assignment.Item.EntityId)
            .Select(assignment => assignment.Item)
            .ToArray();
    }
}
