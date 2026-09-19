using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using Xunit;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Kit.Tests;

public sealed class MechanicsInventoryCoordinatorTests
{
    [Fact]
    public void Grant_and_consume_update_the_attached_live_inventory_component()
    {
        OwnerState owner = CreateOwner();
        ItemDefinition gold = Fungible("gold", 100);
        MechanicsInventoryCoordinator inventory = new(owner.Inventory, owner.Entities,
            new Dictionary<InventoryItemId, ItemDefinition> { [new InventoryItemId("gold")] = gold });

        inventory.Grant(new InventoryGrant(new InventoryItemId("gold"), 3));
        InventoryMutationReceipt consumed = inventory.Consume(new InventoryConsume(new InventoryItemId("gold"), 2));

        Assert.Same(owner.Inventory, owner.Entities.Store.Get<InventoryComponent>(owner.Entity));
        Assert.Equal(1UL, consumed.AfterQuantity);
        Assert.True(owner.Inventory.TryGetQuantity(gold.Id, out ulong quantity));
        Assert.Equal(1UL, quantity);
    }

    [Fact]
    public void Atomic_grant_leaves_canonical_inventory_empty_and_destroys_new_items_when_it_fails()
    {
        OwnerState owner = CreateOwner();
        ItemDefinition gold = Fungible("gold", 100);
        ItemDefinition sword = UniqueEquipment("sword", requiredSlots: 1);
        MechanicsInventoryCoordinator inventory = new(owner.Inventory, owner.Entities,
            new Dictionary<InventoryItemId, ItemDefinition>
            {
                [new InventoryItemId("gold")] = gold,
                [new InventoryItemId("sword")] = sword,
            });
        DurableIdentityReference swordId = new(DurableIdentityKind.Item, 44);

        Assert.Throws<InvalidOperationException>(() => inventory.GrantAtomic(
        [
            new InventoryAtomicGrant(new InventoryItemId("gold"), 3),
            new InventoryAtomicGrant(new InventoryItemId("sword"), UniqueItem: swordId),
            new InventoryAtomicGrant(new InventoryItemId("missing")),
        ]));

        Assert.Empty(owner.Inventory.Stacks);
        Assert.Empty(owner.Inventory.UniqueItems);
        Assert.False(owner.Entities.TryResolve(swordId, out _));

        inventory.GrantAtomic(
        [
            new InventoryAtomicGrant(new InventoryItemId("gold"), 3),
            new InventoryAtomicGrant(new InventoryItemId("sword"), UniqueItem: swordId),
        ]);
        inventory.GrantAtomic([new InventoryAtomicGrant(new InventoryItemId("gold"), 2)]);

        Assert.Equal(5UL, Assert.Single(owner.Inventory.Stacks).Quantity);
        EntityId runtimeSword = Assert.Single(owner.Inventory.UniqueItems).Entity;
        Assert.Equal(swordId, inventory.GetDurableItemId(runtimeSword));
    }

    [Fact]
    public void Invalid_typed_inventory_requests_leave_the_live_component_unchanged()
    {
        OwnerState owner = CreateOwner();
        MechanicsInventoryCoordinator inventory = new(owner.Inventory, owner.Entities,
            new Dictionary<InventoryItemId, ItemDefinition>());

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Consume(new InventoryConsume(new InventoryItemId("gold"), 0)));
        Assert.Throws<InvalidOperationException>(() => inventory.Grant(new InventoryGrant(new InventoryItemId("gold"), 1)));
        Assert.Empty(owner.Inventory.Stacks);
    }

    [Fact]
    public void Same_definition_items_keep_distinct_runtime_entities_and_equip_across_multiple_slots()
    {
        OwnerState owner = CreateOwner(withEquipment: true);
        ItemDefinition greatsword = UniqueEquipment("greatsword", requiredSlots: 2);
        var items = new Dictionary<InventoryItemId, ItemDefinition> { [new InventoryItemId("greatsword")] = greatsword };
        var slots = new Dictionary<KitEquipmentSlotId, EquipmentSlotDefinition>
        {
            [new KitEquipmentSlotId("left")] = Slot("left"),
            [new KitEquipmentSlotId("right")] = Slot("right"),
        };
        MechanicsEquipmentCoordinator equipment = new(owner.Inventory, owner.Equipment!, owner.Entities, items, slots);
        DurableIdentityReference firstId = new(DurableIdentityKind.Item, 44);
        DurableIdentityReference secondId = new(DurableIdentityKind.Item, 45);

        KitUniqueInventoryItem first = equipment.Materialize(firstId, new InventoryItemId("greatsword"));
        KitUniqueInventoryItem second = equipment.Materialize(secondId, new InventoryItemId("greatsword"));
        Assert.Throws<InvalidOperationException>(() => equipment.Equip(new(9999, new InventoryItemId("greatsword")),
            [new KitEquipmentSlotId("left"), new KitEquipmentSlotId("right")]));
        equipment.Equip(first, [new KitEquipmentSlotId("left"), new KitEquipmentSlotId("right")]);

        Assert.NotEqual(first.EntityId, second.EntityId);
        Assert.NotEqual(firstId.Value, first.EntityId);
        Assert.Equal([first.EntityId, first.EntityId], equipment.Read().Assignments.Select(value => value.Item.EntityId));
        equipment.Unequip(first);
        Assert.Empty(equipment.Read().Assignments);

        Assert.Throws<InvalidOperationException>(() => equipment.Equip(new(9999, new InventoryItemId("greatsword")),
            [new KitEquipmentSlotId("left"), new KitEquipmentSlotId("right")]));
        equipment.Equip(first, [new KitEquipmentSlotId("left"), new KitEquipmentSlotId("right")]);
        EquipmentMutationReceipt swapped = equipment.Swap(first, second, [new KitEquipmentSlotId("left"), new KitEquipmentSlotId("right")]);
        Assert.Equal(EquipmentMutationKind.Swap, swapped.Kind);
        Assert.Equal(new EntityId(first.EntityId), swapped.ReplacedItem);
        Assert.Equal([second.EntityId, second.EntityId], equipment.Read().Assignments.Select(value => value.Item.EntityId));
    }

    [Fact]
    public void Transfer_updates_the_destination_attached_inventory_facade()
    {
        OwnerState source = CreateOwner(withEquipment: true);
        OwnerState destination = CreateOwner(source.Entities, source.Store, actorId: 2, withEquipment: true);
        ItemDefinition sword = UniqueEquipment("sword", requiredSlots: 1);
        MechanicsEquipmentCoordinator equipment = new(source.Inventory, source.Equipment!, source.Entities,
            new Dictionary<InventoryItemId, ItemDefinition> { [new InventoryItemId("sword")] = sword },
            new Dictionary<KitEquipmentSlotId, EquipmentSlotDefinition> { [new KitEquipmentSlotId("hand")] = Slot("hand") });
        KitUniqueInventoryItem item = equipment.Materialize(new DurableIdentityReference(DurableIdentityKind.Item, 44), new InventoryItemId("sword"));

        source.Inventory.TransferUnique(new EntityId(item.EntityId), destination.Entity);

        Assert.Empty(source.Inventory.UniqueItems);
        Assert.Equal((ulong)item.EntityId, Assert.Single(destination.Inventory.UniqueItems).Entity.Value);
        Assert.Same(destination.Inventory, destination.Entities.Store.Get<InventoryComponent>(destination.Entity));

        MechanicsEquipmentCoordinator destinationEquipment = new(destination.Inventory, destination.Equipment!, destination.Entities,
            new Dictionary<InventoryItemId, ItemDefinition> { [new InventoryItemId("sword")] = sword },
            new Dictionary<KitEquipmentSlotId, EquipmentSlotDefinition> { [new KitEquipmentSlotId("hand")] = Slot("hand") });
        destinationEquipment.Equip(new KitUniqueInventoryItem(item.EntityId, new InventoryItemId("sword")), [new KitEquipmentSlotId("hand")]);
        Assert.Equal(item.EntityId, Assert.Single(destinationEquipment.Read().Assignments).Item.EntityId);
    }

    private static OwnerState CreateOwner(EntityDirectory? entities = null, InventoryStore? store = null, ulong actorId = 1,
        bool withEquipment = false)
    {
        EntityDirectory directory = entities ?? new EntityDirectory();
        InventoryStore inventoryStore = store ?? new InventoryStore();
        EntityId entity = directory.Create(new DurableIdentityReference(DurableIdentityKind.Actor, actorId), new EntityTypeId("actor"));
        inventoryStore.RegisterInventory(new InventoryState(entity));
        InventoryComponent inventory = new(inventoryStore, entity);
        directory.Store.Add(entity, inventory);
        EquipmentComponent? equipment = null;
        if (withEquipment)
        {
            inventoryStore.RegisterEquipment(new EquipmentState(entity));
            equipment = new EquipmentComponent(inventoryStore, entity);
            directory.Store.Add(entity, equipment);
        }
        return new OwnerState(directory, inventoryStore, entity, inventory, equipment);
    }

    private static ItemDefinition Fungible(string id, ulong maximumQuantity) =>
        new(ItemDefinitionId.Parse(id), ItemKind.Fungible, maximumQuantity);

    private static ItemDefinition UniqueEquipment(string id, int requiredSlots) =>
        new(ItemDefinitionId.Parse(id), ItemKind.Unique, maximumQuantity: 1,
            classifications: [ItemClassificationId.Parse("blade")], equipment: new ItemEquipmentPolicy(checked((ushort)requiredSlots)));

    private static EquipmentSlotDefinition Slot(string id) => new(Rusty.Engine.Mechanics.EquipmentSlotId.Parse(id), [ItemClassificationId.Parse("blade")]);

    private sealed record OwnerState(EntityDirectory Entities, InventoryStore Store, EntityId Entity,
        InventoryComponent Inventory, EquipmentComponent? Equipment);
}
