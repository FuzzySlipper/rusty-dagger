using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class MechanicsInventoryContainerCoordinatorTests
{
    [Fact]
    public void Seed_and_transfer_all_preserve_mixed_contents_with_one_publish_each()
    {
        InventoryWorld world = new();
        MechanicsInventoryContainerCoordinator containers = CreateCoordinator(world);
        EntityId chest = new(10);
        EntityId player = new(20);
        containers.RegisterOwner(chest);
        containers.RegisterOwner(player);

        InventoryContainerSeedReceipt seeded = containers.Seed(chest,
        [
            new InventoryContainerSeed(new InventoryItemId("gold"), 4),
            new InventoryContainerSeed(new InventoryItemId("sword"), UniqueIdentity: "chest:sword", UniqueEntityId: 40),
        ]);
        InventoryContainerTransferReceipt transfer = containers.TransferAll(chest, player);

        Assert.True(seeded.WorldRevisionAfter > seeded.WorldRevisionBefore);
        Assert.True(transfer.WorldRevisionAfter > transfer.WorldRevisionBefore);
        Assert.Empty(containers.Read(chest).Stacks);
        Assert.Empty(containers.Read(chest).UniqueItems);
        InventoryView playerContents = containers.Read(player);
        Assert.Equal(4UL, playerContents.Stacks.Single().Quantity);
        Assert.Equal(new EntityId(40), playerContents.UniqueItems.Single().Entity);
        Assert.Equal(chest, transfer.SourceBefore.Owner);
        Assert.Equal(player, transfer.DestinationBefore.Owner);
        Assert.Equal(1, transfer.SourceBefore.StackCount);
        Assert.Equal(1, transfer.SourceBefore.UniqueItemCount);
        Assert.Equal(0, transfer.SourceAfter.StackCount);
        Assert.Equal(0, transfer.SourceAfter.UniqueItemCount);
    }

    [Fact]
    public void Transfer_all_returns_stacks_and_unique_items_in_deterministic_order()
    {
        InventoryWorld world = new();
        MechanicsInventoryContainerCoordinator containers = CreateCoordinator(world);
        EntityId source = new(10);
        EntityId destination = new(20);
        containers.RegisterOwner(source);
        containers.RegisterOwner(destination);
        containers.Seed(source,
        [
            new InventoryContainerSeed(new InventoryItemId("zinc"), 1),
            new InventoryContainerSeed(new InventoryItemId("amber"), 2),
            new InventoryContainerSeed(new InventoryItemId("sword"), UniqueIdentity: "item:thirty", UniqueEntityId: 30),
            new InventoryContainerSeed(new InventoryItemId("sword"), UniqueIdentity: "item:ten", UniqueEntityId: 10),
        ]);

        InventoryContainerTransferReceipt transfer = containers.TransferAll(source, destination);

        Assert.Equal(["amber", "zinc"], transfer.Stacks.Select(item => item.Item.Value));
        Assert.Equal([10UL, 30UL], transfer.UniqueItems.Select(item => item.EntityId));
    }

    [Fact]
    public void Transfer_all_of_an_empty_owner_publishes_once_and_reports_empty_transfer()
    {
        InventoryWorld world = new();
        MechanicsInventoryContainerCoordinator containers = CreateCoordinator(world);
        EntityId source = new(10);
        EntityId destination = new(20);
        containers.RegisterOwner(source);
        containers.RegisterOwner(destination);
        ulong revisionBefore = world.Revision;

        InventoryContainerTransferReceipt transfer = containers.TransferAll(source, destination);

        Assert.Empty(transfer.Stacks);
        Assert.Empty(transfer.UniqueItems);
        Assert.Equal(revisionBefore, world.Revision);
        Assert.Equal(0, transfer.SourceAfter.StackCount);
        Assert.Equal(0, transfer.DestinationAfter.UniqueItemCount);
    }

    [Fact]
    public void Failed_transfer_candidate_leaves_both_owners_and_world_revision_unchanged()
    {
        InventoryWorld world = new();
        MechanicsInventoryContainerCoordinator containers = CreateCoordinator(world);
        EntityId source = new(10);
        EntityId destination = new(20);
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
        ulong revisionBefore = world.Revision;

        Assert.Throws<MechanicsException>(() => containers.TransferAll(source, destination));

        Assert.Equal(revisionBefore, world.Revision);
        Assert.Equal(sourceBefore.Stacks, containers.Read(source).Stacks);
        Assert.Equal(destinationBefore.Stacks, containers.Read(destination).Stacks);
    }

    [Fact]
    public void Unique_seed_identities_and_entities_are_materialized_exactly_once()
    {
        InventoryWorld world = new();
        MechanicsInventoryContainerCoordinator containers = CreateCoordinator(world);
        EntityId owner = new(10);
        containers.RegisterOwner(owner);

        Assert.Throws<InvalidOperationException>(() => containers.Seed(owner,
        [
            new InventoryContainerSeed(new InventoryItemId("sword"), UniqueIdentity: "same", UniqueEntityId: 40),
            new InventoryContainerSeed(new InventoryItemId("sword"), UniqueIdentity: "other", UniqueEntityId: 40),
        ]));
        Assert.Empty(containers.Read(owner).UniqueItems);

        containers.Seed(owner, [new InventoryContainerSeed(new InventoryItemId("sword"), UniqueIdentity: "same", UniqueEntityId: 40)]);
        Assert.Throws<InvalidOperationException>(() => containers.Seed(
            owner,
            [new InventoryContainerSeed(new InventoryItemId("sword"), UniqueIdentity: "same", UniqueEntityId: 41)]));
        Assert.Single(containers.Read(owner).UniqueItems);
    }

    [Fact]
    public void Container_operations_require_registered_nonzero_distinct_owners()
    {
        InventoryWorld world = new();
        MechanicsInventoryContainerCoordinator containers = CreateCoordinator(world);
        EntityId owner = new(10);

        Assert.Throws<ArgumentOutOfRangeException>(() => containers.RegisterOwner(default));
        Assert.Throws<InvalidOperationException>(() => containers.Read(owner));
        containers.RegisterOwner(owner);
        Assert.Throws<MechanicsException>(() => containers.RegisterOwner(owner));
        Assert.Throws<InvalidOperationException>(() => containers.TransferAll(owner, new EntityId(20)));
        Assert.Throws<ArgumentException>(() => containers.TransferAll(owner, owner));
    }

    [Fact]
    public void Constructor_requires_exact_product_to_managed_definition_mapping()
    {
        InventoryWorld world = new();
        ItemDefinition gold = Fungible("gold", maximumQuantity: 10);

        Assert.Throws<ArgumentException>(() => new MechanicsInventoryContainerCoordinator(
            world,
            new Dictionary<InventoryItemId, ItemDefinition>
            {
                [new InventoryItemId("coins")] = gold,
            }));
    }

    [Fact]
    public void Constructor_snapshots_definition_mapping_against_later_caller_mutation()
    {
        InventoryWorld world = new();
        ItemDefinition originalGold = Fungible("gold", maximumQuantity: 10);
        ItemDefinition originalSword = Unique("sword");
        var callerDefinitions = new Dictionary<InventoryItemId, ItemDefinition>
        {
            [new InventoryItemId("gold")] = originalGold,
            [new InventoryItemId("sword")] = originalSword,
        };
        MechanicsInventoryContainerCoordinator containers = new(world, callerDefinitions);
        EntityId source = new(10);
        EntityId destination = new(20);
        containers.RegisterOwner(source);
        containers.RegisterOwner(destination);

        callerDefinitions[new InventoryItemId("gold")] = Fungible("gold", maximumQuantity: 1);
        callerDefinitions.Remove(new InventoryItemId("sword"));
        callerDefinitions[new InventoryItemId("amber")] = Fungible("amber", maximumQuantity: 10);

        containers.Seed(source,
        [
            new InventoryContainerSeed(new InventoryItemId("gold"), 4),
            new InventoryContainerSeed(new InventoryItemId("sword"), UniqueIdentity: "source:sword", UniqueEntityId: 40),
        ]);
        InventoryContainerTransferReceipt transfer = containers.TransferAll(source, destination);

        Assert.Equal(4UL, containers.Read(destination).Stacks.Single().Quantity);
        Assert.Equal(new EntityId(40), containers.Read(destination).UniqueItems.Single().Entity);
        Assert.Equal("gold", transfer.Stacks.Single().Item.Value);
        Assert.Equal("sword", transfer.UniqueItems.Single().Item.Value);
        Assert.DoesNotContain(transfer.Stacks, item => item.Item.Value == "amber");
    }

    private static MechanicsInventoryContainerCoordinator CreateCoordinator(InventoryWorld world) =>
        new(world, new Dictionary<InventoryItemId, ItemDefinition>
        {
            [new InventoryItemId("amber")] = Fungible("amber", maximumQuantity: 10),
            [new InventoryItemId("gold")] = Fungible("gold", maximumQuantity: 10),
            [new InventoryItemId("zinc")] = Fungible("zinc", maximumQuantity: 10),
            [new InventoryItemId("sword")] = Unique("sword"),
        });

    private static ItemDefinition Fungible(string id, ulong maximumQuantity) =>
        new(ItemDefinitionId.Parse(id), ItemKind.Fungible, maximumQuantity);

    private static ItemDefinition Unique(string id) =>
        new(ItemDefinitionId.Parse(id), ItemKind.Unique, maximumQuantity: 1);
}
