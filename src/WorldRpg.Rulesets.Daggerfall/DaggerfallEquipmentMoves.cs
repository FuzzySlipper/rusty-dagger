using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What one typed equipment move decided. The caller phrases the message.</summary>
internal enum EquipmentMoveOutcome
{
    Applied,
    UnknownItem,
    Incompatible,
    ConflictsNeedClearing,
    InvalidDestination,
    Rejected,
}

internal sealed record EquipmentMoveResult(EquipmentMoveOutcome Outcome, string? Detail = null);

/// <summary>
/// Typed inventory/equipment move operations over live Engine state. The UI adapter translates one
/// semantic action into these calls; future AI/scripts call them directly with typed identities.
/// Grid arrangement lives here because only moves change it. Dagger eligibility and slot policy
/// comes from <see cref="DaggerfallEquipmentPolicy"/>; the Engine mutation itself goes through the
/// existing Kit coordinators, which stay atomic.
/// </summary>
internal sealed class DaggerfallEquipmentMoves(
    MechanicsInventoryCoordinator inventory,
    MechanicsEquipmentCoordinator equipment,
    DaggerfallDefinitions definitions,
    Func<IReadOnlyList<string>>? forbiddenEquipment = null)
{
    internal const int GridCapacity = 50;
    private readonly InventoryGridLayout _layout = new(GridCapacity);

    internal static string LayoutKey(ulong entity) =>
        $"unique:{entity.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    internal static string LayoutKey(UniqueItem item) => LayoutKey(item.EntityId);
    internal static string LayoutKey(InventoryStackId stack) => $"stack:{stack.Value}";

    internal InventoryView ReadInventory() => inventory.Read();
    internal EquipmentRead ReadEquipment() => equipment.Read();
    internal ulong LayoutRevision => _layout.Revision;
    internal int? GridPosition(string key) => _layout.Position(key);

    /// <summary>Arranges every unequipped item from the authoritative reads. Reads call this.</summary>
    internal void ReconcileLayout()
    {
        InventoryView current = inventory.Read();
        HashSet<ulong> equipped = equipment.Read().Assignments.Select(assignment => assignment.Item.EntityId).ToHashSet();
        _layout.Reconcile(current.Stacks.Select(stack => LayoutKey(stack.Id))
            .Concat(current.UniqueItems.Where(item => !equipped.Contains(item.Entity.Value)).Select(item => LayoutKey(new UniqueItem(item.Entity.Value, new InventoryItemId(item.Definition.Value))))));
    }

    internal EquipmentMoveResult MoveStackToGrid(InventoryStackId stack, int target)
    {
        if (target < 0 || target >= GridCapacity) return new(EquipmentMoveOutcome.InvalidDestination);
        if (!inventory.Read().Stacks.Any(entry => entry.Id == stack))
            return new(EquipmentMoveOutcome.UnknownItem);
        _layout.Place(LayoutKey(stack), target);
        return new(EquipmentMoveOutcome.Applied);
    }

    internal EquipmentMoveResult MoveToGrid(UniqueItem item, int target)
    {
        if (target < 0 || target >= GridCapacity) return new(EquipmentMoveOutcome.InvalidDestination);
        if (!IsContained(item)) return new(EquipmentMoveOutcome.UnknownItem);
        string key = LayoutKey(item);
        if (!IsEquipped(item.EntityId))
        {
            _layout.Place(key, target);
            return new(EquipmentMoveOutcome.Applied);
        }

        string? occupantKey = _layout.At(target);
        if (occupantKey is not null && occupantKey != key)
        {
            // The occupant takes the mover's equipment slot; a stack cannot, so it stays put and the
            // move is refused rather than crashing on an entity parse.
            if (!TryResolveOccupant(occupantKey, out UniqueItem occupant, out bool occupantIsStack))
                return new(EquipmentMoveOutcome.UnknownItem);
            if (occupantIsStack) return new(EquipmentMoveOutcome.Incompatible, "Stacked items cannot be equipped.");
            SlotId firstSlot = equipment.Read().Assignments.First(assignment => assignment.Item.EntityId == item.EntityId).Slot;
            EquipmentMoveResult seated = MoveToSlot(occupant, firstSlot);
            if (seated.Outcome != EquipmentMoveOutcome.Applied) return seated;
        }

        // Seating the occupant displaces the mover through the same Swap/Reassign path, so only
        // unequip when the mover is still equipped.
        if (IsEquipped(item.EntityId))
        {
            try
            {
                equipment.Unequip(item);
            }
            catch (Exception error) when (error is MechanicsException or ArgumentException or InvalidOperationException)
            {
                return new(EquipmentMoveOutcome.Rejected, error.Message);
            }
        }
        ReconcileLayout();
        if (_layout.Position(key) is null)
        {
            // Arrangement is full, but the Engine mutation already applied: the item stays safe in
            // the authoritative read rather than failing the whole operation for a grid cell.
            return new(EquipmentMoveOutcome.Applied);
        }
        _layout.Move(key, target);
        return new(EquipmentMoveOutcome.Applied);
    }

    internal EquipmentMoveResult MoveToSlot(UniqueItem item, SlotId slot)
    {
        if (!definitions.TryResolveItem(new DaggerfallItemId(item.Definition.Value), out DaggerfallItemDefinition definition))
            return new(EquipmentMoveOutcome.UnknownItem);
        if (!IsContained(item)) return new(EquipmentMoveOutcome.UnknownItem);
        if (forbiddenEquipment?.Invoke() is { } restrictions
            && DaggerfallCustomCareerPolicy.Forbids(definition, restrictions, out string restriction))
            return new(EquipmentMoveOutcome.Rejected, restriction);
        if (!DaggerfallEquipmentPolicy.IsCompatible(definitions, definition, slot.Value))
            return new(EquipmentMoveOutcome.Incompatible, "The item does not fit this equipment slot.");
        IReadOnlyList<SlotId> slots = DaggerfallEquipmentPolicy.TargetSlots(definition, slot);
        EquipmentRead before = equipment.Read();
        IReadOnlyList<UniqueItem> conflicts =
            DaggerfallEquipmentPolicy.ConflictingEquipped(definitions, before, item, definition, slots);
        if (conflicts.Count > 1) return new(EquipmentMoveOutcome.ConflictsNeedClearing, "Unequip the conflicting items first.");

        int? previousGrid = _layout.Position(LayoutKey(item));
        try
        {
            if (before.Assignments.Any(assignment => assignment.Item.EntityId == item.EntityId))
                equipment.Reassign(item, slots, conflicts);
            else if (conflicts.Count == 1) equipment.Swap(conflicts[0], item, slots);
            else equipment.Equip(item, slots);
        }
        catch (Exception error) when (error is MechanicsException or ArgumentException or InvalidOperationException)
        {
            // Engine candidates reject atomically; layout changes only follow accepted mutations.
            return new(EquipmentMoveOutcome.Rejected, error.Message);
        }

        // A displaced occupant takes the mover's old grid cell when it has arrangement.
        ReconcileLayout();
        if (conflicts.Count == 1 && previousGrid is int targetGrid
            && _layout.Position(LayoutKey(conflicts[0])) is not null)
            _layout.Move(LayoutKey(conflicts[0]), targetGrid);
        return new(EquipmentMoveOutcome.Applied);
    }

    /// <summary>Removes equipment made ineligible by a newly committed custom career.</summary>
    internal int UnequipForbidden()
    {
        IReadOnlyList<string> restrictions = forbiddenEquipment?.Invoke() ?? [];
        int removed = 0;
        foreach (UniqueItem item in equipment.Read().Assignments.Select(assignment => assignment.Item).DistinctBy(item => item.EntityId).ToArray())
        {
            if (!definitions.TryResolveItem(new DaggerfallItemId(item.Definition.Value), out DaggerfallItemDefinition definition)
                || !DaggerfallCustomCareerPolicy.Forbids(definition, restrictions, out _)) continue;
            equipment.Unequip(item);
            removed++;
        }
        if (removed != 0) ReconcileLayout();
        return removed;
    }

    private bool IsContained(UniqueItem item) =>
        inventory.Read().UniqueItems.Any(entry => entry.Entity.Value == item.EntityId && entry.Definition.Value == item.Definition.Value);

    private bool IsEquipped(ulong entity) =>
        equipment.Read().Assignments.Any(assignment => assignment.Item.EntityId == entity);

    private bool TryResolveOccupant(string key, out UniqueItem occupant, out bool isStack)
    {
        occupant = new UniqueItem(0, new InventoryItemId(string.Empty));
        isStack = false;
        InventoryView current = inventory.Read();
        if (key.StartsWith("unique:", StringComparison.Ordinal)
            && ulong.TryParse(key.AsSpan("unique:".Length), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out ulong entity))
        {
            if (!current.UniqueItems.Any(entry => entry.Entity.Value == entity)) return false;
            var found = current.UniqueItems.First(entry => entry.Entity.Value == entity);
            occupant = new UniqueItem(entity, new InventoryItemId(found.Definition.Value));
            return true;
        }
        if (key.StartsWith("stack:", StringComparison.Ordinal))
        {
            string id = key.Substring("stack:".Length);
            if (!current.Stacks.Any(entry => entry.Id.Value == id)) return false;
            isStack = true;
            return true;
        }
        return false;
    }
}
