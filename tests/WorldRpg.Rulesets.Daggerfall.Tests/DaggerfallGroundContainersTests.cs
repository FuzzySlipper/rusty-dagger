using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallGroundContainersTests
{
    [Fact]
    public void Drop_and_partial_take_are_engine_transfers_and_profile_scoped()
    {
        using EntityDirectory entities = new();
        InventoryStore store = new();
        EntityId player = entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("player"));
        store.RegisterInventory(new InventoryState(player));
        entities.Store.Add(player, new InventoryComponent(store, player));
        ItemDefinition apples = new(ItemDefinitionId.Parse("apple"), ItemKind.Fungible, 20);
        MechanicsInventoryContainerCoordinator containers = new(store, entities, new Dictionary<InventoryItemId, ItemDefinition>
        {
            [new InventoryItemId("apple")] = apples,
        });
        InventoryStackId playerStack = InventoryStackId.Parse("player.apples");
        new InventoryComponent(store, player).Grant(apples, playerStack, 5);
        DaggerfallItemInstances instances = new();
        instances.RegisterStack(DaggerfallItemOwner.Player, playerStack, Metadata(DaggerfallItemOwner.Player));
        DaggerfallWorldProfileKey exterior = Profile(DaggerfallWorldProfileKind.Exterior, "charing-exterior");
        DaggerfallWorldProfileKey interior = Profile(DaggerfallWorldProfileKind.Interior, "charing-interior");
        DurableIdentityAllocator identities = new(DurableIdentityKind.Container, 100);
        DaggerfallGroundContainers ground = new(containers, instances, player, identities, exterior);

        Assert.Throws<InvalidOperationException>(() => ground.Drop(new(new InventoryItemId("apple"), 3, playerStack),
            new WorldPoint(4, 0, 8), store.Revision - 1));
        Assert.Empty(ground.All);
        Assert.Equal(5UL, containers.Read(player).Stacks.Single().Quantity);
        DaggerfallGroundContainer pile = ground.Drop(new(new InventoryItemId("apple"), 3, playerStack), new WorldPoint(4, 0, 8), store.Revision);

        Assert.Equal(100, pile.Id);
        Assert.Equal(exterior, pile.Profile);
        Assert.Equal(2UL, containers.Read(player).Stacks.Single().Quantity);
        Assert.Equal(3UL, Assert.Single(ground.Read(pile.Id)!.Stacks).Quantity);
        InventoryStackId droppedStack = ground.Read(pile.Id)!.Stacks.Single().Id;
        Assert.Equal(DaggerfallItemOwner.Ground(pile.Id), instances.RequireStack(DaggerfallItemOwner.Ground(pile.Id), droppedStack).Owner);

        ground.SwitchProfile(interior);
        Assert.Empty(ground.All);
        Assert.Null(ground.Read(pile.Id));
        Assert.Throws<InvalidOperationException>(() => ground.Take(pile.Id,
            new(new InventoryItemId("apple"), 1, playerStack, InventoryStackId.Parse("return.apples")), store.Revision));
        Assert.Equal(2UL, containers.Read(player).Stacks.Single().Quantity);

        ground.SwitchProfile(exterior);
        InventoryStackId destination = ground.ResolveTakeDestination(pile.Id, droppedStack, InventoryStackId.Parse("return.apples"));
        _ = ground.Take(pile.Id, new(new InventoryItemId("apple"), 2, droppedStack, destination), store.Revision);

        Assert.Equal(4UL, containers.Read(player).Stacks.Single().Quantity);
        Assert.Equal(1UL, Assert.Single(ground.Read(pile.Id)!.Stacks).Quantity);
        Assert.Equal(DaggerfallItemOwner.Player, instances.RequireStack(DaggerfallItemOwner.Player, destination).Owner);

        _ = ground.Take(pile.Id, new(new InventoryItemId("apple"), 1, droppedStack, destination), store.Revision);
        Assert.Empty(ground.All);
        Assert.Empty(ground.Persisted);
        Assert.Null(ground.Read(pile.Id));
        Assert.Equal(DurableIdentityClassification.Removed,
            identities.Classify(new DurableIdentityReference(DurableIdentityKind.Container, (ulong)pile.Id)));
    }

    private static DaggerfallWorldProfileKey Profile(DaggerfallWorldProfileKind kind, string id) =>
        new DaggerfallWorldProfileKey(new DaggerfallSiteId(1, 2), kind, id).Validate();

    private static DaggerfallItemInstanceMetadata Metadata(DaggerfallItemOwner owner) =>
        new DaggerfallItemInstanceMetadata("apple", "none", 0, 0, 0, true, false, null, null, null, owner).Validate();
}
