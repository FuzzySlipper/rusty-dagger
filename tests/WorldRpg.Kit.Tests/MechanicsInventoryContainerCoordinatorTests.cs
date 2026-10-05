using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class MechanicsInventoryContainerCoordinatorTests
{
    [Fact]
    public void Register_owner_attaches_a_live_inventory_component_and_seeded_same_definition_items_have_runtime_ids()
    {
        EntityDirectory entities = new();
        InventoryStore store = new();
        MechanicsInventoryContainerCoordinator containers = CreateCoordinator(store, entities);
        EntityId owner = CreateOwner(entities, 10);
        containers.RegisterOwner(owner);

        containers.Seed(owner,
        [
            new InventoryContainerSeed(new InventoryItemId("sword"), UniqueItem: Item(40)),
            new InventoryContainerSeed(new InventoryItemId("sword"), UniqueItem: Item(41)),
        ]);

        InventoryComponent inventory = entities.Store.Get<InventoryComponent>(owner);
        Rusty.Engine.Mechanics.UniqueInventoryItem[] items = inventory.UniqueItems.OrderBy(item => item.Entity.Value).ToArray();
        Assert.Equal(2, items.Length);
        Assert.NotEqual(items[0].Entity, items[1].Entity);
        Assert.DoesNotContain(items, item => item.Entity.Value is 40 or 41);
        Assert.Equal(Item(40), containers.GetDurableItemId(items[0].Entity));
        Assert.Equal(Item(41), containers.GetDurableItemId(items[1].Entity));
    }

    [Fact]
    public void Transfer_updates_destination_live_facade_and_keeps_the_runtime_unique_item()
    {
        EntityDirectory entities = new();
        InventoryStore store = new();
        MechanicsInventoryContainerCoordinator containers = CreateCoordinator(store, entities);
        EntityId source = CreateOwner(entities, 10);
        EntityId destination = CreateOwner(entities, 20);
        containers.RegisterOwner(source);
        containers.RegisterOwner(destination);
        containers.Seed(source, [new InventoryContainerSeed(new InventoryItemId("sword"), UniqueItem: Item(40))]);
        EntityId item = Assert.Single(containers.Read(source).UniqueItems).Entity;

        containers.Transfer(source, destination, new InventoryContainerSelection(new InventoryItemId("sword"), 1, UniqueEntityId: item.Value));

        Assert.Empty(entities.Store.Get<InventoryComponent>(source).UniqueItems);
        Assert.Equal(item, Assert.Single(entities.Store.Get<InventoryComponent>(destination).UniqueItems).Entity);
        Assert.Equal(Item(40), containers.GetDurableItemId(item));
    }

    [Fact]
    public void Failed_seed_cleans_new_engine_items_and_leaves_canonical_inventory_unchanged()
    {
        EntityDirectory entities = new();
        InventoryStore store = new();
        MechanicsInventoryContainerCoordinator containers = CreateCoordinator(store, entities);
        EntityId owner = CreateOwner(entities, 10);
        containers.RegisterOwner(owner);

        Assert.Throws<MechanicsException>(() => containers.Seed(owner,
        [
            new InventoryContainerSeed(new InventoryItemId("sword"), UniqueItem: Item(40)),
            new InventoryContainerSeed(new InventoryItemId("gold"), 11, Stack: Stack("gold")),
        ]));

        Assert.Empty(containers.Read(owner).Stacks);
        Assert.Empty(containers.Read(owner).UniqueItems);
        Assert.False(entities.TryResolve(Item(40), out _));
    }

    [Fact]
    public void Failed_multi_stack_transfer_preserves_both_canonical_inventories()
    {
        EntityDirectory entities = new();
        InventoryStore store = new();
        MechanicsInventoryContainerCoordinator containers = CreateCoordinator(store, entities);
        EntityId source = CreateOwner(entities, 10);
        EntityId destination = CreateOwner(entities, 20);
        containers.RegisterOwner(source);
        containers.RegisterOwner(destination);
        containers.Seed(source,
        [
            new InventoryContainerSeed(new InventoryItemId("amber"), 1, Stack: Stack("amber-source")),
            new InventoryContainerSeed(new InventoryItemId("zinc"), 2, Stack: Stack("zinc-source")),
        ]);
        containers.Seed(destination, [new InventoryContainerSeed(new InventoryItemId("zinc"), 9, Stack: Stack("zinc-source"))]);
        InventoryView sourceBefore = containers.Read(source);
        InventoryView destinationBefore = containers.Read(destination);

        Assert.Throws<MechanicsException>(() => containers.TransferAll(source, destination));

        Assert.Equal(sourceBefore.Stacks, containers.Read(source).Stacks);
        Assert.Equal(destinationBefore.Stacks, containers.Read(destination).Stacks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unequipping_and_transferring_carried_items_publish_together_or_preserve_equipment_on_failure(bool reject)
    {
        EntityDirectory entities = new();
        InventoryStore store = new();
        var items = new Dictionary<InventoryItemId, ItemDefinition>
        {
            [new("sword")] = new(ItemDefinitionId.Parse("sword"), ItemKind.Unique, 1,
                classifications: [ItemClassificationId.Parse("blade")], equipment: new ItemEquipmentPolicy(1)),
            [new("zinc")] = Fungible("zinc", 10),
        };
        MechanicsInventoryContainerCoordinator containers = new(store, entities, items);
        var source = CreateOwner(entities, 10);
        var destination = CreateOwner(entities, 20);
        containers.RegisterOwner(source);
        containers.RegisterOwner(destination);
        store.RegisterEquipment(new EquipmentState(source));
        EquipmentComponent component = new(store, source);
        entities.Store.Add(source, component);
        MechanicsEquipmentCoordinator equipment = new(entities.Store.Get<InventoryComponent>(source), component, entities, items,
            new Dictionary<WorldRpg.Kit.Inventory.EquipmentSlotId, EquipmentSlotDefinition>
            {
                [new("hand")] = new(Rusty.Engine.Mechanics.EquipmentSlotId.Parse("hand"), [ItemClassificationId.Parse("blade")]),
            });
        containers.Seed(source, [new(new("sword"), UniqueItem: Item(40)), new(new("zinc"), 2, Stack: Stack("zinc"))]);
        var item = Assert.Single(containers.Read(source).UniqueItems).Entity;
        equipment.Equip(new(item.Value, new("sword")), [new("hand")]);
        if (reject) containers.Seed(destination, [new(new("zinc"), 9, Stack: Stack("zinc"))]);
        ulong revision = store.Revision;
        void Transfer() => containers.TransferAll(source, destination, prepareTransfer: edit => edit.Unequip(source, item));
        if (reject)
        {
            Assert.Throws<MechanicsException>(Transfer);
            Assert.Equal(revision, store.Revision);
            Assert.Equal(item.Value, Assert.Single(equipment.Read().Assignments).Item.EntityId);
            Assert.Equal(item, Assert.Single(containers.Read(source).UniqueItems).Entity);
            Assert.Empty(containers.Read(destination).UniqueItems);
            Assert.Equal(2UL, Assert.Single(containers.Read(source).Stacks).Quantity);
            Assert.Equal(9UL, Assert.Single(containers.Read(destination).Stacks).Quantity);
        }
        else
        {
            Transfer();
            Assert.Equal(revision + 1, store.Revision);
            Assert.Empty(equipment.Read().Assignments);
            Assert.Empty(containers.Read(source).UniqueItems);
            Assert.Empty(containers.Read(source).Stacks);
            Assert.Equal(item, Assert.Single(containers.Read(destination).UniqueItems).Entity);
            Assert.Equal(2UL, Assert.Single(containers.Read(destination).Stacks).Quantity);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Retained_seed_and_live_transfer_publish_together_or_leave_both_owners_unchanged(bool reject)
    {
        EntityDirectory entities = new();
        InventoryStore store = new();
        var containers = CreateCoordinator(store, entities);
        var source = CreateOwner(entities, 10);
        var destination = CreateOwner(entities, 20);
        containers.RegisterOwner(source);
        containers.RegisterOwner(destination);
        containers.Seed(source, [new(new("sword"), UniqueItem: Item(40))]);
        var live = Assert.Single(containers.Read(source).UniqueItems).Entity;
        void SeedAndTransfer() => containers.Seed(destination, [new(new("sword"), UniqueItem: Item(41))], edit =>
        {
            edit.TransferUnique(live, source, destination);
            if (reject) edit.Grant(destination, Fungible("gold", 10), Stack("overflow"), 11);
        });
        if (reject)
        {
            Assert.Throws<MechanicsException>(SeedAndTransfer);
            Assert.Equal(live, Assert.Single(containers.Read(source).UniqueItems).Entity);
            Assert.Empty(containers.Read(destination).UniqueItems);
            Assert.Empty(containers.Read(destination).Stacks);
            Assert.False(entities.TryResolve(Item(41), out _));
        }
        else
        {
            SeedAndTransfer();
            Assert.Empty(containers.Read(source).UniqueItems);
            Assert.Equal(2, containers.Read(destination).UniqueItems.Count);
            Assert.Contains(containers.Read(destination).UniqueItems, item => item.Entity == live);
            Assert.True(entities.TryResolve(Item(41), out _));
        }
    }

    [Fact]
    public void Register_owner_requires_an_existing_engine_entity()
    {
        EntityDirectory entities = new();
        MechanicsInventoryContainerCoordinator containers = CreateCoordinator(new InventoryStore(), entities);

        Assert.Throws<InvalidOperationException>(() => containers.RegisterOwner(new EntityId(99)));
    }

    private static MechanicsInventoryContainerCoordinator CreateCoordinator(InventoryStore store, EntityDirectory entities) =>
        new(store, entities, new Dictionary<InventoryItemId, ItemDefinition>
        {
            [new InventoryItemId("amber")] = Fungible("amber", 10),
            [new InventoryItemId("gold")] = Fungible("gold", 10),
            [new InventoryItemId("zinc")] = Fungible("zinc", 10),
            [new InventoryItemId("sword")] = Unique("sword"),
        });

    private static EntityId CreateOwner(EntityDirectory entities, ulong id) =>
        entities.Create(new DurableIdentityReference(DurableIdentityKind.Container, id), new EntityTypeId("container"));

    private static DurableIdentityReference Item(ulong id) => new(DurableIdentityKind.Item, id);

    private static InventoryStackId Stack(string id) => InventoryStackId.Parse(id);

    private static ItemDefinition Fungible(string id, ulong maximumQuantity) =>
        new(ItemDefinitionId.Parse(id), ItemKind.Fungible, maximumQuantity);

    private static ItemDefinition Unique(string id) =>
        new(ItemDefinitionId.Parse(id), ItemKind.Unique, maximumQuantity: 1);
}
