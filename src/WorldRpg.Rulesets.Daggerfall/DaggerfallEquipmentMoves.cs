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

/// <summary>The accepted item and every displaced item from one Engine equipment mutation.</summary>
internal sealed record EquipmentMoveResult(
    EquipmentMoveOutcome Outcome,
    string? Detail = null,
    UniqueItem? Equipped = null,
    IReadOnlyList<UniqueItem>? Removed = null,
    DaggerfallEquipmentChange? Change = null)
{
    internal IReadOnlyList<UniqueItem> RemovedItems { get; } = Removed ?? [];
}

/// <summary>Typed meaning of one accepted equipment mutation for UI, held effects, and attack readiness.</summary>
internal sealed record DaggerfallEquipmentChange(
    UniqueItem? Equipped,
    IReadOnlyList<UniqueItem> Removed,
    DaggerfallHandEquipTiming Timing,
    DaggerfallEquipmentCue Cue);

/// <summary>Delays accumulated when the inventory closes, matching the donor's per-hand timing rule.</summary>
internal sealed record DaggerfallHandEquipTiming(int RightHandMilliseconds, int LeftHandMilliseconds)
{
    internal static DaggerfallHandEquipTiming None { get; } = new(0, 0);
}

/// <summary>The presentation cue selected by an accepted equipment mutation.</summary>
internal enum DaggerfallEquipmentCue { Equip, Unequip, Transfer }

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
    Func<IReadOnlyList<string>>? forbiddenEquipment = null,
    DaggerfallItemInstances? itemInstances = null)
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
    internal event Action<DaggerfallEquipmentChange>? Changed;

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
            // This is one transfer requested from the grid, rather than two independently
            // published operations (equip the occupant, then remove the mover).
            EquipmentMoveResult seated = MoveToSlot(occupant, firstSlot, DaggerfallEquipmentCue.Transfer, publish: false);
            if (seated.Outcome != EquipmentMoveOutcome.Applied) return seated;
            ReconcileLayout();
            if (_layout.Position(key) is not null) _layout.Move(key, target);
            if (seated.Change is { } transfer) Changed?.Invoke(transfer);
            return seated;
        }

        // Seating the occupant displaces the mover through the same Swap/Reassign path, so only
        // unequip when the mover is still equipped.
        if (IsEquipped(item.EntityId))
        {
            EquipmentRead before = equipment.Read();
            try
            {
                equipment.Unequip(item);
            }
            catch (Exception error) when (error is MechanicsException or ArgumentException or InvalidOperationException)
            {
                return new(EquipmentMoveOutcome.Rejected, error.Message);
            }
            DaggerfallEquipmentChange change = new(null, [item], Timing(before, equipment.Read()), DaggerfallEquipmentCue.Unequip);
            ReconcileLayout();
            if (_layout.Position(key) is null)
            {
                // Arrangement is full, but the Engine mutation already applied: the item stays safe in
                // the authoritative read rather than failing the whole operation for a grid cell.
                Changed?.Invoke(change);
                return new(EquipmentMoveOutcome.Applied, Removed: [item], Change: change);
            }
            _layout.Move(key, target);
            Changed?.Invoke(change);
            return new(EquipmentMoveOutcome.Applied, Removed: [item], Change: change);
        }
        ReconcileLayout();
        return new(EquipmentMoveOutcome.Applied);
    }

    internal EquipmentMoveResult MoveToSlot(UniqueItem item, SlotId slot) =>
        MoveToSlot(item, slot, DaggerfallEquipmentCue.Equip, publish: true);

    private EquipmentMoveResult MoveToSlot(UniqueItem item, SlotId slot, DaggerfallEquipmentCue cue, bool publish)
    {
        if (!definitions.TryResolveItem(new DaggerfallItemId(item.Definition.Value), out DaggerfallItemDefinition definition))
            return new(EquipmentMoveOutcome.UnknownItem);
        if (!IsContained(item)) return new(EquipmentMoveOutcome.UnknownItem);
        if (forbiddenEquipment?.Invoke() is { } restrictions
            && DaggerfallCustomCareerPolicy.Forbids(definition, restrictions, out string restriction))
            return new(EquipmentMoveOutcome.Rejected, restriction);
        if (itemInstances is not null
            && itemInstances.RequireUnique(equipment.GetDurableItemId(new Rusty.Engine.Entities.EntityId(item.EntityId)).Value).CurrentCondition < 1)
            return new(EquipmentMoveOutcome.Rejected, "That item is broken.");
        if (!DaggerfallEquipmentPolicy.IsCompatible(definitions, definition, slot.Value))
            return new(EquipmentMoveOutcome.Incompatible, "The item does not fit this equipment slot.");
        IReadOnlyList<SlotId> slots = DaggerfallEquipmentPolicy.TargetSlots(definition, slot);
        EquipmentRead before = equipment.Read();
        IReadOnlyList<UniqueItem> conflicts =
            DaggerfallEquipmentPolicy.ConflictingEquipped(definitions, before, item, definition, slots);
        int? previousGrid = _layout.Position(LayoutKey(item));
        try
        {
            if (before.Assignments.Any(assignment => assignment.Item.EntityId == item.EntityId) || conflicts.Count > 1)
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
        DaggerfallEquipmentChange change = new(item, conflicts, Timing(before, equipment.Read()), cue);
        if (publish) Changed?.Invoke(change);
        return new(EquipmentMoveOutcome.Applied, Equipped: item, Removed: conflicts, Change: change);
    }

    /// <summary>Removes equipment made ineligible by a newly committed custom career.</summary>
    internal int UnequipForbidden()
    {
        IReadOnlyList<string> restrictions = forbiddenEquipment?.Invoke() ?? [];
        EquipmentRead before = equipment.Read();
        List<UniqueItem> removed = [];
        foreach (UniqueItem item in equipment.Read().Assignments.Select(assignment => assignment.Item).DistinctBy(item => item.EntityId).ToArray())
        {
            if (!definitions.TryResolveItem(new DaggerfallItemId(item.Definition.Value), out DaggerfallItemDefinition definition)
                || !DaggerfallCustomCareerPolicy.Forbids(definition, restrictions, out _)) continue;
            equipment.Unequip(item);
            removed.Add(item);
        }
        if (removed.Count == 0) return 0;
        ReconcileLayout();
        Changed?.Invoke(new(null, removed, Timing(before, equipment.Read()), DaggerfallEquipmentCue.Unequip));
        return removed.Count;
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

    private DaggerfallHandEquipTiming Timing(EquipmentRead before, EquipmentRead after) => new(
        HandTiming(before, after, "right-hand"),
        HandTiming(before, after, "left-hand"));

    private int HandTiming(EquipmentRead before, EquipmentRead after, string slot)
    {
        UniqueItem? earlier = before.TryGet(new SlotId(slot), out UniqueItem oldItem) ? oldItem : null;
        UniqueItem? current = after.TryGet(new SlotId(slot), out UniqueItem newItem) ? newItem : null;
        if (earlier == current) return 0;
        return Delay(earlier) + Delay(current);
    }

    private int Delay(UniqueItem? item)
    {
        if (item is not UniqueItem value
            || !definitions.TryResolveItem(new DaggerfallItemId(value.Definition.Value), out DaggerfallItemDefinition definition)
            || definition.Template is not { } template) return 0;
        int index = template.Groups.Contains("Weapons", StringComparer.Ordinal) ? template.Index - 113
            : template.Groups.Contains("Armor", StringComparer.Ordinal) ? template.Index - 102
            : -1;
        return index is >= 0 and < 18 ? EquipDelayMilliseconds[index] : 0;
    }

    // WeaponManager.EquipDelayTimes, indexed by the donor's retained weapon/armor group index.
    private static readonly int[] EquipDelayMilliseconds = [500, 700, 1200, 900, 900, 1800, 1600, 1700, 1700, 3000, 3400, 2000, 2200, 2000, 2200, 2000, 4000, 5000];
}
