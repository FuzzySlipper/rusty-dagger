using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.World;

namespace WorldRpg.Kit.Inventory;

public readonly record struct InventoryItemId(string Value);
public readonly record struct EquipmentSlotId(string Value);
/// <summary>A live runtime item reference. Save its directory identity, not this session-local number.</summary>
public readonly record struct UniqueInventoryItem(ulong EntityId, InventoryItemId Definition);

public sealed record InventoryGrant(InventoryItemId Item, ulong Quantity)
{
    public InventoryGrant Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Item.Value);
        ArgumentOutOfRangeException.ThrowIfZero(Quantity);
        return this;
    }
}

public sealed record InventoryConsume(InventoryItemId Item, ulong Quantity)
{
    public InventoryConsume Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Item.Value);
        ArgumentOutOfRangeException.ThrowIfZero(Quantity);
        return this;
    }
}

/// <summary>One fungible or durable unique item admitted as part of one atomic grant.</summary>
public sealed record InventoryAtomicGrant(
    InventoryItemId Item,
    ulong Quantity = 1,
    DurableIdentityReference? UniqueItem = null)
{
    public InventoryAtomicGrant Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Item.Value);
        ArgumentOutOfRangeException.ThrowIfZero(Quantity);
        if (UniqueItem is DurableIdentityReference identity)
        {
            identity.Validate();
            if (identity.Kind != DurableIdentityKind.Item || Quantity != 1)
                throw new ArgumentException("Unique atomic grants require one durable item identity and quantity one.", nameof(UniqueItem));
        }
        return this;
    }
}

public sealed record EquipmentAssignment(EquipmentSlotId Slot, UniqueInventoryItem Item);

/// <summary>A copied managed equipment view joined to its contained item definitions.</summary>
public sealed class EquipmentRead(IReadOnlyList<EquipmentAssignment> assignments, ulong revision, ulong relationshipStateRevision)
{
    public IReadOnlyList<EquipmentAssignment> Assignments { get; } = Array.AsReadOnly(assignments.ToArray());
    public ulong Revision { get; } = revision;
    public ulong RelationshipStateRevision { get; } = relationshipStateRevision;

    public bool TryGet(EquipmentSlotId slot, out UniqueInventoryItem item)
    {
        foreach (EquipmentAssignment assignment in Assignments)
        {
            if (assignment.Slot == slot) { item = assignment.Item; return true; }
        }
        item = default;
        return false;
    }
}

/// <summary>Typed product coordination over one live Engine inventory component.</summary>
public sealed class MechanicsInventoryCoordinator
{
    private readonly IReadOnlyDictionary<InventoryItemId, ItemDefinition> _items;

    public MechanicsInventoryCoordinator(InventoryComponent component, EntityDirectory entities,
        IReadOnlyDictionary<InventoryItemId, ItemDefinition> items)
    {
        Component = component ?? throw new ArgumentNullException(nameof(component));
        Entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public InventoryComponent Component { get; }
    public EntityDirectory Entities { get; }
    public InventoryView Read() => Component.View();

    public InventoryMutationReceipt Grant(InventoryGrant grant)
    {
        grant.Validate();
        return Component.Grant(RequireDefinition(grant.Item), grant.Quantity);
    }

    /// <summary>Publishes all requested grants together, or retains none of their newly materialized item entities.</summary>
    public void GrantAtomic(IEnumerable<InventoryAtomicGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        InventoryAtomicGrant[] values = grants.Select(grant => grant.Validate()).ToArray();
        if (values.Length == 0) throw new ArgumentException("At least one atomic grant is required.", nameof(grants));

        List<DurableIdentityReference> created = [];
        try
        {
            using InventoryEdit candidate = Component.Store.Prepare();
            foreach (InventoryAtomicGrant grant in values)
            {
                ItemDefinition definition = RequireDefinition(grant.Item);
                if (grant.UniqueItem is DurableIdentityReference identity)
                {
                    if (definition.Kind != ItemKind.Unique)
                        throw new InvalidOperationException($"Atomic unique grant '{grant.Item.Value}' requires a unique item definition.");
                    EntityId item = CreateItemEntity(identity, definition);
                    created.Add(identity);
                    candidate.MaterializeUnique(new ItemState(item, definition), Component.Owner);
                }
                else
                {
                    if (definition.Kind != ItemKind.Fungible)
                        throw new InvalidOperationException($"Atomic stack grant '{grant.Item.Value}' requires a fungible item definition.");
                    candidate.Grant(Component.Owner, definition, grant.Quantity);
                }
            }
            candidate.Publish();
        }
        catch
        {
            foreach (DurableIdentityReference identity in created) Entities.Destroy(identity);
            throw;
        }
    }

    public InventoryMutationReceipt Consume(InventoryConsume consume)
    {
        consume.Validate();
        return Component.Consume(RequireDefinition(consume.Item), consume.Quantity);
    }

    public DurableIdentityReference GetDurableItemId(EntityId item) => RequireItemIdentity(Entities.IdentityOf(item));

    private EntityId CreateItemEntity(DurableIdentityReference identity, ItemDefinition definition) =>
        Entities.Create(RequireItemIdentity(identity), new EntityTypeId(definition.Id.Value));

    private static DurableIdentityReference RequireItemIdentity(DurableIdentityReference identity)
    {
        identity.Validate();
        if (identity.Kind != DurableIdentityKind.Item)
            throw new ArgumentException("An inventory item requires a durable item identity.", nameof(identity));
        return identity;
    }

    private ItemDefinition RequireDefinition(InventoryItemId id) => _items.TryGetValue(id, out ItemDefinition? definition)
        ? definition : throw new InvalidOperationException($"Managed inventory does not define item '{id.Value}'.");
}

/// <summary>Typed coordination over live inventory and equipment components for one owner.</summary>
public sealed class MechanicsEquipmentCoordinator
{
    private readonly IReadOnlyDictionary<InventoryItemId, ItemDefinition> _items;
    private readonly IReadOnlyDictionary<EquipmentSlotId, EquipmentSlotDefinition> _slots;

    public MechanicsEquipmentCoordinator(InventoryComponent inventory, EquipmentComponent component,
        EntityDirectory entities, IReadOnlyDictionary<InventoryItemId, ItemDefinition> items,
        IReadOnlyDictionary<EquipmentSlotId, EquipmentSlotDefinition> slots)
    {
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        Component = component ?? throw new ArgumentNullException(nameof(component));
        Entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _slots = slots ?? throw new ArgumentNullException(nameof(slots));
        if (!ReferenceEquals(Inventory.Store, Component.Store) || Inventory.Owner != Component.Owner)
            throw new ArgumentException("Inventory and equipment components must belong to the same store owner.");
    }

    public InventoryComponent Inventory { get; }
    public EquipmentComponent Component { get; }
    public EntityDirectory Entities { get; }

    public EquipmentRead Read()
    {
        Dictionary<ulong, InventoryItemId> contained = Inventory.UniqueItems
            .ToDictionary(item => item.Entity.Value, item => new InventoryItemId(item.Definition.Value));
        List<EquipmentAssignment> assignments = [];
        foreach (Rusty.Engine.Mechanics.EquipmentAssignment assignment in Component.Assignments)
        {
            assignments.Add(new EquipmentAssignment(new EquipmentSlotId(assignment.Slot.Value),
                new UniqueInventoryItem(assignment.Item.Value, RequireContained(contained, assignment.Item))));
        }
        return new EquipmentRead(assignments, Component.Revision, Inventory.Store.Revision);
    }

    /// <summary>Creates one live Engine item from its durable item identity and admits it to this inventory.</summary>
    public UniqueInventoryItem Materialize(DurableIdentityReference itemId, InventoryItemId definitionId)
    {
        ItemDefinition definition = RequireDefinition(definitionId);
        if (definition.Kind != ItemKind.Unique)
            throw new InvalidOperationException($"Item '{definitionId.Value}' is not a unique item definition.");

        EntityId item = Entities.Create(RequireItemIdentity(itemId), new EntityTypeId(definition.Id.Value));
        try { Inventory.MaterializeUnique(new ItemState(item, definition)); }
        catch { Entities.Destroy(itemId); throw; }
        return new UniqueInventoryItem(item.Value, definitionId);
    }

    public EquipmentMutationReceipt Equip(UniqueInventoryItem item, IReadOnlyList<EquipmentSlotId> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0) throw new ArgumentException("At least one equipment slot is required.", nameof(slots));
        return Component.Equip(RequireEntity(item), slots.Select(RequireSlot));
    }

    public EquipmentMutationReceipt Unequip(UniqueInventoryItem item) => Component.Unequip(RequireEntity(item));

    public EquipmentMutationReceipt Swap(UniqueInventoryItem outgoing, UniqueInventoryItem incoming,
        IReadOnlyList<EquipmentSlotId> incomingSlots)
    {
        ArgumentNullException.ThrowIfNull(incomingSlots);
        if (incomingSlots.Count == 0) throw new ArgumentException("At least one equipment slot is required.", nameof(incomingSlots));
        return Component.Swap(RequireEntity(outgoing), RequireEntity(incoming), incomingSlots.Select(RequireSlot));
    }

    /// <summary>Moves an equipped item and explicitly selected replacements through one Engine candidate.</summary>
    public EquipmentMutationReceipt Reassign(UniqueInventoryItem item, IReadOnlyList<EquipmentSlotId> slots,
        IReadOnlyList<UniqueInventoryItem> replaced)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(replaced);
        if (slots.Count == 0) throw new ArgumentException("At least one equipment slot is required.", nameof(slots));
        using InventoryEdit candidate = Inventory.Store.Prepare();
        candidate.Unequip(Inventory.Owner, RequireEntity(item));
        foreach (UniqueInventoryItem outgoing in replaced) candidate.Unequip(Inventory.Owner, RequireEntity(outgoing));
        EquipmentMutationReceipt receipt = candidate.Equip(Inventory.Owner, RequireEntity(item), slots.Select(RequireSlot));
        candidate.Publish();
        return receipt;
    }

    public DurableIdentityReference GetDurableItemId(EntityId item) => RequireItemIdentity(Entities.IdentityOf(item));

    private EntityId RequireEntity(UniqueInventoryItem item)
    {
        EntityId entity = new(item.EntityId);
        if (!Inventory.Contains(entity) || !Inventory.Store.TryGetItem(entity, out ItemState? found)
            || found is null || !string.Equals(found.Definition.Id.Value, item.Definition.Value, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unique item {item.EntityId} is not contained by this inventory with definition '{item.Definition.Value}'.");
        return entity;
    }

    private ItemDefinition RequireDefinition(InventoryItemId id) => _items.TryGetValue(id, out ItemDefinition? definition)
        ? definition : throw new InvalidOperationException($"Managed inventory does not define item '{id.Value}'.");

    private EquipmentSlotDefinition RequireSlot(EquipmentSlotId id) => _slots.TryGetValue(id, out EquipmentSlotDefinition? slot)
        ? slot : throw new InvalidOperationException($"Managed equipment does not define slot '{id.Value}'.");

    private static InventoryItemId RequireContained(IReadOnlyDictionary<ulong, InventoryItemId> contained, EntityId item) =>
        contained.TryGetValue(item.Value, out InventoryItemId definition)
            ? definition : throw new InvalidOperationException($"Managed equipment assignment refers to item {item.Value} outside its owner's inventory.");

    private static DurableIdentityReference RequireItemIdentity(DurableIdentityReference identity)
    {
        identity.Validate();
        if (identity.Kind != DurableIdentityKind.Item)
            throw new ArgumentException("An inventory item requires a durable item identity.", nameof(identity));
        return identity;
    }
}
