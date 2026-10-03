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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mixed_quest_placement_retires_only_emptied_source_piles(bool retainOtherContents)
    {
        using EntityDirectory entities = new();
        InventoryStore store = new();
        EntityId player = entities.Create(new(DurableIdentityKind.Actor, 1), new("player"));
        store.RegisterInventory(new InventoryState(player));
        entities.Store.Add(player, new InventoryComponent(store, player));
        ItemDefinition apples = new(ItemDefinitionId.Parse("apple"), ItemKind.Fungible, 20);
        MechanicsInventoryContainerCoordinator containers = new(store, entities,
            new Dictionary<InventoryItemId, ItemDefinition> { [new("apple")] = apples });
        var stack = InventoryStackId.Parse("player.apples");
        new InventoryComponent(store, player).Grant(apples, stack, 5);
        DaggerfallItemInstances instances = new();
        instances.RegisterStack(DaggerfallItemOwner.Player, stack, Metadata(DaggerfallItemOwner.Player));
        DurableIdentityAllocator identities = new(DurableIdentityKind.Container, 100);
        DaggerfallGroundContainers ground = new(containers, instances, player, identities,
            Profile(DaggerfallWorldProfileKind.Exterior, "charing-exterior"));
        var old = ground.Drop(new(new("apple"), 3, stack), default, store.Revision);
        var dropped = containers.Read(old.Owner).Stacks.Single().Id;
        if (retainOtherContents)
        {
            var other = InventoryStackId.Parse("other.apples");
            containers.Seed(old.Owner, [new(new("apple"), 1, Stack: other)]);
            instances.RegisterStack(DaggerfallItemOwner.Ground(old.Id), other, Metadata(DaggerfallItemOwner.Ground(old.Id)));
        }
        var binding = DaggerfallQuestResourceBinding.Stack(new("ground", old.Id), dropped.Value) with
        { Stacks = [new(new("ground", old.Id), dropped.Value), new(new("player", 0), stack.Value)] };
        ground.PlaceQuestItem(binding, new(4, 0, 8), [DaggerfallItemOwner.Ground(old.Id), DaggerfallItemOwner.Player], target =>
        {
            var destination = entities.Resolve(new(DurableIdentityKind.Container, checked((ulong)target.Id)));
            foreach (var source in new[] { (old.Owner, DaggerfallItemOwner.Ground(old.Id), dropped), (player, DaggerfallItemOwner.Player, stack) })
            {
                ulong quantity = containers.Read(source.Item1).Stacks.Single(value => value.Id == source.Item3).Quantity;
                containers.Transfer(source.Item1, destination, new(new("apple"), quantity, source.Item3));
                instances.TransferStack(source.Item2, target, source.Item3, source.Item3, true);
            }
        });
        Assert.Equal(retainOtherContents, ground.TryGet(old.Id, out _));
        Assert.Equal(retainOtherContents, store.TryGetInventory(old.Owner, out _));
        Assert.Equal(retainOtherContents, entities.TryResolve(new(DurableIdentityKind.Container, checked((ulong)old.Id)), out _));
        Assert.Equal(retainOtherContents ? DurableIdentityClassification.Live : DurableIdentityClassification.Removed,
            identities.Classify(new(DurableIdentityKind.Container, checked((ulong)old.Id))));
        Assert.Equal(5UL, containers.Read(ground.Persisted.Single(value => value.Id != old.Id).Owner).Stacks.Aggregate(0UL, (sum, value) => sum + value.Quantity));
        if (retainOtherContents) Assert.Equal(1UL, ground.Read(old.Id)!.Stacks.Single().Quantity);
    }

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
        List<DaggerfallStackChange> changes = [];
        instances.StackChanged += changes.Add;
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
        Assert.Equal(new(DaggerfallItemOwner.Ground(pile.Id), droppedStack, DaggerfallItemOwner.Player, destination, SourceRetired: false), changes.Last());

        _ = ground.Take(pile.Id, new(new InventoryItemId("apple"), 1, droppedStack, destination), store.Revision);
        Assert.Equal(new(DaggerfallItemOwner.Ground(pile.Id), droppedStack, DaggerfallItemOwner.Player, destination), changes.Last());
        Assert.Empty(ground.All);
        Assert.Empty(ground.Persisted);
        Assert.Null(ground.Read(pile.Id));
        Assert.False(store.TryGetInventory(pile.Owner, out _));
        Assert.Equal(DurableIdentityClassification.Removed,
            identities.Classify(new DurableIdentityReference(DurableIdentityKind.Container, (ulong)pile.Id)));
    }

    private static DaggerfallWorldProfileKey Profile(DaggerfallWorldProfileKind kind, string id) =>
        new DaggerfallWorldProfileKey(new DaggerfallSiteId(1, 2), kind, id).Validate();

    private static DaggerfallItemInstanceMetadata Metadata(DaggerfallItemOwner owner) =>
        new DaggerfallItemInstanceMetadata("apple", "none", 0, 0, 0, true, false, null, null, null, owner).Validate();
}
