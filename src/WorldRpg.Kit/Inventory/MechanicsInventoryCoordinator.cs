using Rusty.Engine;

namespace WorldRpg.Kit.Inventory;

public readonly record struct InventoryItemId(string Value);
public readonly record struct EquipmentSlotId(string Value);
public readonly record struct UniqueInventoryItem(ulong EntityId, InventoryItemId Definition);
/// <summary>A product-owned safe entity lease for a unique item already admitted by Engine.</summary>
public sealed record EquipmentItemLease(UniqueInventoryItem Item, MechanicsEntity Entity);
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

/// <summary>One Engine-observed equipment view, joined to its contained unique-item definitions.</summary>
public sealed class EquipmentRead(IReadOnlyList<EquipmentAssignment> assignments, MechanicsComponentRevision revision, ulong relationshipStateRevision)
{
    public IReadOnlyList<EquipmentAssignment> Assignments { get; } = Array.AsReadOnly(assignments.ToArray());
    public MechanicsComponentRevision Revision { get; } = revision;
    public ulong RelationshipStateRevision { get; } = relationshipStateRevision;
    public bool TryGet(EquipmentSlotId slot, out UniqueInventoryItem item)
    {
        foreach (EquipmentAssignment assignment in Assignments)
            if (assignment.Slot == slot) { item = assignment.Item; return true; }
        item = default;
        return false;
    }
}

/// <summary>Thin revision-guarded product coordinator over Engine-authoritative inventory state.</summary>
public sealed class MechanicsInventoryCoordinator(IMechanicsService mechanics, MechanicsEntity owner)
{
    private readonly IMechanicsService _mechanics = mechanics ?? throw new ArgumentNullException(nameof(mechanics));
    private readonly MechanicsEntity _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public MechanicsInventoryViewLeaseReceipt Read() => _mechanics.ReadInventoryView(_owner);

    public MechanicsInventoryMutationLeaseReceipt Grant(InventoryGrant grant)
    {
        grant.Validate();
        MechanicsInventoryViewLeaseReceipt current = Read();
        return _mechanics.GrantInventory(new MechanicsInventoryMutationRequest(
            _owner,
            grant.Operation,
            MechanicsActiveEffectProvenanceKind.Request,
            0,
            string.Empty,
            0,
            string.Empty,
            0,
            string.Empty,
            0,
            0,
            string.Empty,
            grant.Operation,
            grant.SourceInstance,
            grant.Item.Value,
            grant.Quantity,
            MechanicsRevisionGuard.Exact,
            current.InventoryRevision));
    }

    public MechanicsInventoryMutationLeaseReceipt Consume(InventoryConsume consume)
    {
        consume.Validate();
        MechanicsInventoryViewLeaseReceipt current = Read();
        return _mechanics.ConsumeInventory(new MechanicsInventoryMutationRequest(
            _owner,
            consume.Operation,
            MechanicsActiveEffectProvenanceKind.Request,
            0,
            string.Empty,
            0,
            string.Empty,
            0,
            string.Empty,
            0,
            0,
            string.Empty,
            consume.Operation,
            consume.SourceInstance,
            consume.Item.Value,
            consume.Quantity,
            MechanicsRevisionGuard.Exact,
            current.InventoryRevision));
    }
}

/// <summary>
/// Thin typed coordination over Engine-authoritative unique-item containment and equipment.
/// It stores only disposable leases for items it materializes; inventory and equipment facts are
/// always read from Mechanics immediately before a guarded mutation.
/// </summary>
public sealed class MechanicsEquipmentCoordinator : IDisposable
{
    private readonly IMechanicsService _mechanics;
    private readonly MechanicsCatalog _catalog;
    private readonly MechanicsEntity _owner;
    private readonly Dictionary<ulong, MechanicsEntity> _items = [];
    private readonly List<OwnedUniqueItem> _materialized = [];
    private bool _disposed;

    public MechanicsEquipmentCoordinator(IMechanicsService mechanics, MechanicsCatalog catalog, MechanicsEntity owner, IEnumerable<EquipmentItemLease>? existingItems = null)
    {
        _mechanics = mechanics ?? throw new ArgumentNullException(nameof(mechanics));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        if (existingItems is not null)
            foreach (EquipmentItemLease item in existingItems)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(item.Item.Definition.Value);
                if (!_items.TryAdd(item.Item.EntityId, item.Entity)) throw new ArgumentException($"Duplicate unique item entity id {item.Item.EntityId}.", nameof(existingItems));
            }
    }

    public EquipmentRead Read()
    {
        ThrowIfDisposed();
        MechanicsInventoryViewLeaseReceipt inventory = _mechanics.ReadInventoryView(_owner);
        MechanicsEquipmentAssignmentComponentLeaseReceipt equipment = _mechanics.ReadEquipmentAssignmentComponent(_owner);
        Dictionary<ulong, InventoryItemId> contained = [];
        foreach (MechanicsInventoryViewUniqueItemRow item in inventory.UniqueItems.Span)
            contained.Add(item.EntityId, new InventoryItemId(item.Definition));
        List<EquipmentAssignment> assignments = [];
        foreach (MechanicsEquipmentAssignmentComponentRow assignment in equipment.Entries.Span)
        {
            if (!contained.TryGetValue(assignment.ItemEntityId, out InventoryItemId definition))
                throw new InvalidOperationException($"Engine equipment assignment '{assignment.Slot}' refers to item {assignment.ItemEntityId} outside its owner's inventory.");
            assignments.Add(new(new EquipmentSlotId(assignment.Slot), new UniqueInventoryItem(assignment.ItemEntityId, definition)));
        }
        MechanicsComponentReadMetadata metadata = equipment.Metadata;
        return new EquipmentRead(assignments, new MechanicsComponentRevision(metadata.EntityId, metadata.Revision, metadata.Component, metadata.Present), inventory.RelationshipStateRevision);
    }

    public UniqueInventoryItem Materialize(UniqueItemMaterialization item)
    {
        ThrowIfDisposed();
        item.Validate();
        MechanicsInventoryViewLeaseReceipt current = _mechanics.ReadInventoryView(_owner);
        MechanicsEntity entity = _mechanics.BindEntity(new MechanicsEntityBindRequest(_catalog, item.EntityId, item.Identity));
        try
        {
            MechanicsUniqueItemMaterializationLeaseReceipt receipt = _mechanics.MaterializeUniqueItem(new MechanicsUniqueItemMaterializationRequest(entity, _owner, item.Definition.Value, current.RelationshipStateRevision));
            _materialized.Add(new OwnedUniqueItem(receipt.ItemEntityId, entity));
            _items.Add(receipt.ItemEntityId, entity);
            return new UniqueInventoryItem(receipt.ItemEntityId, new InventoryItemId(receipt.ItemDefinition));
        }
        catch
        {
            entity.Dispose();
            throw;
        }
    }

    public MechanicsEquipmentMutationLeaseReceipt Equip(UniqueInventoryItem item, IReadOnlyList<EquipmentSlotId> slots, EquipmentChange change)
    {
        change.Validate();
        if (slots.Count == 0) throw new ArgumentException("At least one equipment slot is required.", nameof(slots));
        EquipmentRead current = Read();
        return _mechanics.EquipEquipment(new MechanicsEquipmentEquipRequest(
            _owner, RequireLease(item), change.Operation, MechanicsActiveEffectProvenanceKind.Request,
            0, string.Empty, 0, string.Empty, 0, string.Empty, 0, 0, string.Empty,
            change.Operation, change.SourceInstance, slots.Select(slot => new MechanicsText(slot.Value)).ToArray(),
            MechanicsRevisionGuard.Exact, current.Revision, current.RelationshipStateRevision));
    }

    public MechanicsEquipmentMutationLeaseReceipt Unequip(UniqueInventoryItem item, EquipmentChange change)
    {
        change.Validate();
        EquipmentRead current = Read();
        return _mechanics.UnequipEquipment(new MechanicsEquipmentUnequipRequest(
            _owner, RequireLease(item), change.Operation, MechanicsActiveEffectProvenanceKind.Request,
            0, string.Empty, 0, string.Empty, 0, string.Empty, 0, 0, string.Empty,
            change.Operation, change.SourceInstance, MechanicsRevisionGuard.Exact, current.Revision, current.RelationshipStateRevision));
    }

    public MechanicsEquipmentMutationLeaseReceipt Swap(UniqueInventoryItem outgoing, UniqueInventoryItem incoming, IReadOnlyList<EquipmentSlotId> incomingSlots, EquipmentChange change)
    {
        change.Validate();
        if (incomingSlots.Count == 0) throw new ArgumentException("At least one equipment slot is required.", nameof(incomingSlots));
        EquipmentRead current = Read();
        return _mechanics.SwapEquipment(new MechanicsEquipmentSwapRequest(
            _owner, RequireLease(outgoing), RequireLease(incoming), change.Operation, MechanicsActiveEffectProvenanceKind.Request,
            0, string.Empty, 0, string.Empty, 0, string.Empty, 0, 0, string.Empty,
            change.Operation, change.SourceInstance, incomingSlots.Select(slot => new MechanicsText(slot.Value)).ToArray(),
            MechanicsRevisionGuard.Exact, current.Revision, current.RelationshipStateRevision));
    }

    public void Dispose()
    {
        if (_disposed) return;
        List<Exception>? failures = null;
        for (int index = _materialized.Count - 1; index >= 0; index--)
        {
            OwnedUniqueItem item = _materialized[index];
            try
            {
                EquipmentRead equipment = Read();
                if (equipment.Assignments.Any(assignment => assignment.Item.EntityId == item.EntityId))
                    Unequip(item.AsInventoryItem(), new EquipmentChange("kit.dispose.unequip", $"item:{item.EntityId}"));
                ulong relationshipRevision = _mechanics.ReadInventoryView(_owner).RelationshipStateRevision;
                _mechanics.DestroyUniqueItem(new MechanicsUniqueItemDestroyRequest(
                    item.Entity, "kit.dispose.destroy", MechanicsActiveEffectProvenanceKind.Request,
                    0, string.Empty, 0, string.Empty, 0, string.Empty, 0, 0, string.Empty,
                    "kit.dispose.destroy", $"item:{item.EntityId}", relationshipRevision));
                item.Entity.Dispose();
                _items.Remove(item.EntityId);
                _materialized.RemoveAt(index);
            }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (_materialized.Count == 0) _disposed = true;
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private MechanicsEntity RequireLease(UniqueInventoryItem item)
    {
        return _items.TryGetValue(item.EntityId, out MechanicsEntity? entity)
            ? entity
            : throw new InvalidOperationException($"Unique item {item.EntityId} is not known to this coordinator.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MechanicsEquipmentCoordinator));
    }

    private sealed record OwnedUniqueItem(ulong EntityId, MechanicsEntity Entity)
    {
        internal UniqueInventoryItem AsInventoryItem() => new(EntityId, new InventoryItemId(string.Empty));
    }
}
