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

        containers.Transfer(source, destination, new InventoryContainerSelection(new InventoryItemId("sword"), 1, item.Value), store.Revision);

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
            new InventoryContainerSeed(new InventoryItemId("gold"), 11),
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
            new InventoryContainerSeed(new InventoryItemId("amber"), 1),
            new InventoryContainerSeed(new InventoryItemId("zinc"), 2),
        ]);
        containers.Seed(destination, [new InventoryContainerSeed(new InventoryItemId("zinc"), 9)]);
        InventoryView sourceBefore = containers.Read(source);
        InventoryView destinationBefore = containers.Read(destination);

        Assert.Throws<MechanicsException>(() => containers.TransferAll(source, destination));

        Assert.Equal(sourceBefore.Stacks, containers.Read(source).Stacks);
        Assert.Equal(destinationBefore.Stacks, containers.Read(destination).Stacks);
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

    private static ItemDefinition Fungible(string id, ulong maximumQuantity) =>
        new(ItemDefinitionId.Parse(id), ItemKind.Fungible, maximumQuantity);

    private static ItemDefinition Unique(string id) =>
        new(ItemDefinitionId.Parse(id), ItemKind.Unique, maximumQuantity: 1);
}
