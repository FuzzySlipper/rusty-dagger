using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace WorldRpg.Kit.Inventory;

public readonly record struct InventoryItemId(string Value);
public readonly record struct EquipmentSlotId(string Value);
public readonly record struct UniqueInventoryItem(ulong EntityId, InventoryItemId Definition);

public sealed record InventoryGrant(string Operation, string SourceInstance, InventoryItemId Item, ulong Quantity)
{
    public InventoryGrant Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceInstance);
        ArgumentException.ThrowIfNullOrWhiteSpace(Item.Value);
        ArgumentOutOfRangeException.ThrowIfZero(Quantity);
        return this;
    }
}

public sealed record InventoryConsume(string Operation, string SourceInstance, InventoryItemId Item, ulong Quantity)
{
    public InventoryConsume Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceInstance);
        ArgumentException.ThrowIfNullOrWhiteSpace(Item.Value);
        ArgumentOutOfRangeException.ThrowIfZero(Quantity);
        return this;
    }
}

public sealed record EquipmentChange(string Operation, string SourceInstance)
{
    public EquipmentChange Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceInstance);
        return this;
    }
}

public sealed record UniqueItemMaterialization(string Identity, ulong EntityId, InventoryItemId Definition)
{
    public UniqueItemMaterialization Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Identity);
        ArgumentOutOfRangeException.ThrowIfZero(EntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Definition.Value);
        return this;
    }
}

public sealed record EquipmentAssignment(EquipmentSlotId Slot, UniqueInventoryItem Item);

/// <summary>A copied managed equipment view joined to its contained item definitions.</summary>
public sealed class EquipmentRead(
    IReadOnlyList<EquipmentAssignment> assignments,
    ulong revision,
    ulong relationshipStateRevision)
{
    public IReadOnlyList<EquipmentAssignment> Assignments { get; } = Array.AsReadOnly(assignments.ToArray());
    public ulong Revision { get; } = revision;
    public ulong RelationshipStateRevision { get; } = relationshipStateRevision;

    public bool TryGet(EquipmentSlotId slot, out UniqueInventoryItem item)
    {
        foreach (EquipmentAssignment assignment in Assignments)
        {
            if (assignment.Slot == slot)
            {
                item = assignment.Item;
                return true;
            }
        }

        item = default;
        return false;
    }
}

/// <summary>
/// Thin product coordinator over the managed Engine inventory mechanism. Item
/// definitions remain owned by the ruleset; the Engine helper owns stack and
/// capacity invariants.
/// </summary>
public sealed class MechanicsInventoryCoordinator
{
    private readonly InventoryWorld _world;
    private readonly EntityId _owner;
    private readonly IReadOnlyDictionary<InventoryItemId, ItemDefinition> _items;

    public MechanicsInventoryCoordinator(
        InventoryWorld world,
        EntityId owner,
        IReadOnlyDictionary<InventoryItemId, ItemDefinition> items)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _owner = owner;
        _items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public InventoryView Read() => _world.Read(_owner);

    public InventoryMutationReceipt Grant(InventoryGrant grant)
    {
        grant.Validate();
        return _world.Grant(_owner, RequireDefinition(grant.Item), grant.Quantity);
    }

    public InventoryMutationReceipt Consume(InventoryConsume consume)
    {
        consume.Validate();
        return _world.Consume(_owner, RequireDefinition(consume.Item), consume.Quantity);
    }

    private ItemDefinition RequireDefinition(InventoryItemId id) => _items.TryGetValue(id, out ItemDefinition? definition)
        ? definition
        : throw new InvalidOperationException($"Managed inventory does not define item '{id.Value}'.");
}

/// <summary>
/// Thin typed coordination over managed unique-item containment and equipment.
/// It keeps only ruleset-facing identity conversion; InventoryWorld remains the
/// state owner and validates every relationship mutation atomically.
/// </summary>
public sealed class MechanicsEquipmentCoordinator : IDisposable
{
    private readonly InventoryWorld _world;
    private readonly EntityId _owner;
    private readonly IReadOnlyDictionary<InventoryItemId, ItemDefinition> _items;
    private readonly IReadOnlyDictionary<EquipmentSlotId, EquipmentSlotDefinition> _slots;
    private readonly Dictionary<ulong, InventoryItemId> _knownItems = [];
    private bool _disposed;

    public MechanicsEquipmentCoordinator(
        InventoryWorld world,
        EntityId owner,
        IReadOnlyDictionary<InventoryItemId, ItemDefinition> items,
        IReadOnlyDictionary<EquipmentSlotId, EquipmentSlotDefinition> slots)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _owner = owner;
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _slots = slots ?? throw new ArgumentNullException(nameof(slots));
    }

    public EquipmentRead Read()
    {
        ThrowIfDisposed();
        InventoryView inventory = _world.Read(_owner);
        if (!_world.TryGetEquipment(_owner, out EquipmentState? equipment) || equipment is null)
        {
            throw new InvalidOperationException($"Managed equipment is not registered for owner {_owner.Value}.");
        }

        Dictionary<ulong, InventoryItemId> contained = inventory.UniqueItems
            .ToDictionary(item => item.Entity.Value, item => new InventoryItemId(item.Definition.Value));
        List<EquipmentAssignment> assignments = [];
        foreach (Rusty.Engine.Mechanics.EquipmentAssignment assignment in equipment.Assignments)
        {
            assignments.Add(new EquipmentAssignment(
                new EquipmentSlotId(assignment.Slot.Value),
                new UniqueInventoryItem(assignment.Item.Value, RequireContained(contained, assignment.Item))));
        }

        return new EquipmentRead(assignments, equipment.Revision, inventory.WorldRevision);
    }

    public UniqueInventoryItem Materialize(UniqueItemMaterialization item)
    {
        ThrowIfDisposed();
        item.Validate();
        ItemDefinition definition = RequireDefinition(item.Definition);
        if (definition.Kind != ItemKind.Unique)
        {
            throw new InvalidOperationException($"Item '{item.Definition.Value}' is not a unique item definition.");
        }

        EntityId entity = new(item.EntityId);
        _world.MaterializeUnique(new ItemState(entity, definition), _owner);
        UniqueInventoryItem result = new(item.EntityId, item.Definition);
        _knownItems.Add(item.EntityId, item.Definition);
        return result;
    }

    public EquipmentMutationReceipt Equip(
        UniqueInventoryItem item,
        IReadOnlyList<EquipmentSlotId> slots,
        EquipmentChange change)
    {
        ThrowIfDisposed();
        change.Validate();
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0)
        {
            throw new ArgumentException("At least one equipment slot is required.", nameof(slots));
        }

        return EquipmentService.Equip(
            _world,
            _owner,
            RequireEntity(item),
            slots.Select(RequireSlot));
    }

    public EquipmentMutationReceipt Unequip(UniqueInventoryItem item, EquipmentChange change)
    {
        ThrowIfDisposed();
        change.Validate();
        return EquipmentService.Unequip(_world, _owner, RequireEntity(item));
    }

    public EquipmentMutationReceipt Swap(
        UniqueInventoryItem outgoing,
        UniqueInventoryItem incoming,
        IReadOnlyList<EquipmentSlotId> incomingSlots,
        EquipmentChange change)
    {
        ThrowIfDisposed();
        change.Validate();
        ArgumentNullException.ThrowIfNull(incomingSlots);
        if (incomingSlots.Count == 0)
        {
            throw new ArgumentException("At least one equipment slot is required.", nameof(incomingSlots));
        }

        return EquipmentService.Swap(
            _world,
            _owner,
            RequireEntity(outgoing),
            RequireEntity(incoming),
            incomingSlots.Select(RequireSlot));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _knownItems.Clear();
    }

    private EntityId RequireEntity(UniqueInventoryItem item)
    {
        if (!_knownItems.TryGetValue(item.EntityId, out InventoryItemId definition)
            || definition != item.Definition)
        {
            throw new InvalidOperationException($"Unique item {item.EntityId} is not known to this coordinator.");
        }

        return new EntityId(item.EntityId);
    }

    private ItemDefinition RequireDefinition(InventoryItemId id) => _items.TryGetValue(id, out ItemDefinition? definition)
        ? definition
        : throw new InvalidOperationException($"Managed inventory does not define item '{id.Value}'.");

    private EquipmentSlotDefinition RequireSlot(EquipmentSlotId id) => _slots.TryGetValue(id, out EquipmentSlotDefinition? slot)
        ? slot
        : throw new InvalidOperationException($"Managed equipment does not define slot '{id.Value}'.");

    private static InventoryItemId RequireContained(
        IReadOnlyDictionary<ulong, InventoryItemId> contained,
        EntityId item) => contained.TryGetValue(item.Value, out InventoryItemId definition)
        ? definition
        : throw new InvalidOperationException($"Managed equipment assignment refers to item {item.Value} outside its owner's inventory.");

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MechanicsEquipmentCoordinator));
    }
}
