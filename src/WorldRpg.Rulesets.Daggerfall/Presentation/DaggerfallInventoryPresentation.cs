using System.Globalization;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed record InventoryItemPresentation(string Key, string Definition, string Label, string Quantity, int Weight, int Value,
    string Details, string? Icon, int? GridSlot, string[] EquippedSlots, string[] CompatibleSlots);
internal sealed record EquipmentSlotPresentation(string Id, string Label, string? ItemKey);
internal sealed record InventoryPresentation(string Revision, InventoryItemPresentation[] Items, EquipmentSlotPresentation[] Slots, string Message);

/// <summary>Daggerfall presentation and drop policy; quantities and equipment remain Engine facts.</summary>
internal sealed class DaggerfallInventoryPresentation(
    MechanicsInventoryCoordinator inventory,
    MechanicsEquipmentCoordinator equipment,
    DaggerfallDefinitions definitions,
    IReadOnlyDictionary<string, string> icons)
{
    internal const int GridCapacity = 50;
    private readonly InventoryGridLayout _layout = new(GridCapacity);
    private string _message = "Drag items between the grid and compatible equipment slots.";

    internal InventoryPresentation Read()
    {
        InventoryView current = inventory.Read();
        EquipmentRead equipped = equipment.Read();
        var items = current.UniqueItems.Select(item => (Key: UniqueKey(item.Entity.Value), Definition: item.Definition.Value, Quantity: 1UL,
                Slots: equipped.Assignments.Where(assignment => assignment.Item.EntityId == item.Entity.Value).Select(assignment => assignment.Slot.Value).ToArray()))
            .Concat(current.Stacks.Select(stack => (Key: StackKey(stack.Definition.Value), Definition: stack.Definition.Value, Quantity: stack.Quantity, Slots: Array.Empty<string>())))
            .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        _layout.Reconcile(items.Where(item => item.Slots.Length == 0).Select(item => item.Key));
        return new InventoryPresentation($"{current.WorldRevision}:{_layout.Revision}", items.Select(item =>
        {
            DaggerfallItemDefinition definition = definitions.Items[new DaggerfallItemId(item.Definition)];
            return new InventoryItemPresentation(item.Key, item.Definition, Label(item.Definition), item.Quantity.ToString(CultureInfo.InvariantCulture),
                definition.Weight, definition.Value, Details(definition), icons.GetValueOrDefault(item.Definition),
                item.Slots.Length == 0 ? _layout.Position(item.Key) : null, item.Slots,
                definitions.EquipmentSlots.Keys.Select(slot => slot.Value).Where(slot => Compatible(definition, slot)).ToArray());
        }).ToArray(), definitions.EquipmentSlots.Values.Select(slot => new EquipmentSlotPresentation(slot.Id.Value, Label(slot.Id.Value),
            equipped.TryGet(new SlotId(slot.Id.Value), out UniqueItem item) ? UniqueKey(item.EntityId) : null)).ToArray(), _message);
    }

    internal void Move(DaggerfallPlayerUiAction action)
    {
        InventoryPresentation before = Read();
        if (action.Revision != before.Revision) { _message = "Inventory changed. Choose the item again."; return; }
        InventoryItemPresentation? item = before.Items.SingleOrDefault(item => item.Key == action.Item);
        if (item is null) { _message = "That item is no longer in your inventory."; return; }
        try
        {
            if (action.TargetGrid is int target)
            {
                if (target < 0 || target >= GridCapacity) { _message = "Choose a valid inventory slot."; return; }
                if (item.EquippedSlots.Length == 0)
                {
                    _layout.Place(item.Key, target);
                }
                else
                {
                    string? occupant = _layout.At(target);
                    if (occupant is not null)
                    {
                        InventoryItemPresentation incoming = before.Items.Single(value => value.Key == occupant);
                        Assign(incoming, item.EquippedSlots[0], before);
                    }
                    else equipment.Unequip(Unique(item), new EquipmentChange("daggerfall.ui.unequip", item.Key));
                    Read();
                    _layout.Move(item.Key, target);
                }
            }
            else if (action.TargetEquipment is string slot)
            {
                int? previousGrid = item.GridSlot;
                string? replaced = Assign(item, slot, before);
                Read();
                if (replaced is not null && previousGrid is int targetGrid && _layout.Position(replaced) is not null)
                    _layout.Move(replaced, targetGrid);
            }
            else { _message = "Choose an inventory or equipment destination."; return; }
            _message = "Inventory updated.";
        }
        catch (Exception error) when (error is MechanicsException or ArgumentException or InvalidOperationException)
        {
            // Engine candidates reject atomically; layout changes only follow accepted mutations.
            _message = "Cannot place that item there. " + error.Message;
        }
    }

    private string? Assign(InventoryItemPresentation item, string slot, InventoryPresentation before)
    {
        DaggerfallItemDefinition definition = definitions.Items[new DaggerfallItemId(item.Definition)];
        if (!Compatible(definition, slot)) throw new ArgumentException("The item does not fit this equipment slot.");
        string[] slots = definition.Equipment!.RequiredSlots == 2 ? ["right-hand", "left-hand"] : [slot];
        InventoryItemPresentation[] outgoing = before.Items.Where(value => value.Key != item.Key && value.EquippedSlots.Length > 0
            && (value.EquippedSlots.Intersect(slots).Any()
                || definition.Equipment.ExclusiveGroup is string group && definitions.Items[new DaggerfallItemId(value.Definition)].Equipment?.ExclusiveGroup == group)).ToArray();
        if (outgoing.Length > 1) throw new ArgumentException("Unequip the conflicting items first.");
        SlotId[] target = slots.Select(value => new SlotId(value)).ToArray();
        EquipmentChange change = new("daggerfall.ui.equip", item.Key);
        if (item.EquippedSlots.Length > 0)
            equipment.Reassign(Unique(item), target, outgoing.Select(Unique).ToArray(), change);
        else if (outgoing.Length == 1) equipment.Swap(Unique(outgoing[0]), Unique(item), target, change);
        else equipment.Equip(Unique(item), target, change);
        return outgoing.FirstOrDefault()?.Key;
    }

    private bool Compatible(DaggerfallItemDefinition item, string slot)
    {
        if (item.IsFungible || item.Equipment is null || !definitions.EquipmentSlots.TryGetValue(new DaggerfallEquipmentSlotId(slot), out var target)) return false;
        // Empty classifications are unimplemented Daggerfall accessory slots, not universal item sockets.
        if (!target.AllowedClassifications.Intersect(item.Equipment.Classifications).Any()) return false;
        return item.Equipment.RequiredSlots == 1 || item.Equipment.RequiredSlots == 2 && slot == "right-hand";
    }
    private static UniqueItem Unique(InventoryItemPresentation item) => new(ulong.Parse(item.Key.AsSpan("unique:".Length), CultureInfo.InvariantCulture), new InventoryItemId(item.Definition));
    internal static string UniqueKey(ulong entity) => $"unique:{entity.ToString(CultureInfo.InvariantCulture)}";
    internal static string StackKey(string definition) => $"stack:{definition}";
    private static string Label(string id) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('-', ' '));
    private static string Details(DaggerfallItemDefinition item) => item.Weapon is { } weapon
        ? $"Damage {weapon.MinimumDamage}–{weapon.MaximumDamage}; {Label(weapon.Material)}; {Label(weapon.Skill)}"
        : item.Armor is { } armor ? $"Armor: {Label(armor.Material)}" : item.Shield is not null ? "Shield" : item.IsFungible ? "Stackable item" : "Equipment";
}
