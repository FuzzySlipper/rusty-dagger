using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallItemInstanceTests
{
    [Fact]
    public void Compatible_split_and_merge_preserve_one_metadata_instance_but_incompatible_merge_is_rejected_before_engine_mutation()
    {
        InventoryStore store = new();
        EntityDirectory entities = new();
        EntityId owner = Owner(entities, 10);
        store.RegisterInventory(new InventoryState(owner));
        InventoryComponent component = new(store, owner);
        ItemDefinition definition = Fungible("arrow");
        MechanicsInventoryCoordinator inventory = new(component, entities, new Dictionary<InventoryItemId, ItemDefinition>
        {
            [new InventoryItemId("arrow")] = definition,
        });
        InventoryStackId source = Stack("source");
        InventoryStackId split = Stack("split");
        InventoryStackId incompatible = Stack("incompatible");
        inventory.Grant(new InventoryGrant(new InventoryItemId("arrow"), source, 4));

        DaggerfallItemInstances instances = new();
        DaggerfallItemInstanceMetadata metadata = Metadata("arrow", DaggerfallItemOwner.Actor(10), material: "iron", variant: 2,
            condition: 6, maximumCondition: 9, identified: false, stolen: true, quest: "quest-17", symbol: "letter", enchantment: "fire");
        instances.RegisterStack(DaggerfallItemOwner.Actor(10), source, metadata);

        instances.SplitStack(DaggerfallItemOwner.Actor(10), inventory, source, split, 2);
        Assert.Equal(metadata, instances.RequireStack(DaggerfallItemOwner.Actor(10), split));

        instances.MergeStacks(DaggerfallItemOwner.Actor(10), inventory, split, source);
        Assert.Equal(4UL, Assert.Single(inventory.Read().Stacks).Quantity);

        inventory.Grant(new InventoryGrant(new InventoryItemId("arrow"), incompatible, 1));
        instances.RegisterStack(DaggerfallItemOwner.Actor(10), incompatible,
            Metadata("arrow", DaggerfallItemOwner.Actor(10), material: "steel", variant: 2,
                condition: 6, maximumCondition: 9, identified: false, stolen: true, quest: "quest-17", symbol: "letter", enchantment: "fire"));

        Assert.Throws<InvalidOperationException>(() => instances.MergeStacks(DaggerfallItemOwner.Actor(10), inventory, incompatible, source));
        Assert.Equal(2, inventory.Read().Stacks.Count);
        Assert.Equal(1UL, inventory.Read().Stacks.Single(stack => stack.Id == incompatible).Quantity);
    }

    [Fact]
    public void Transfer_and_restore_retain_every_metadata_field_and_stable_owner()
    {
        InventoryStore store = new();
        EntityDirectory entities = new();
        EntityId sourceOwner = Owner(entities, 10);
        EntityId destinationOwner = Owner(entities, 20);
        store.RegisterInventory(new InventoryState(sourceOwner));
        store.RegisterInventory(new InventoryState(destinationOwner));
        InventoryComponent sourceInventory = new(store, sourceOwner);
        InventoryComponent destinationInventory = new(store, destinationOwner);
        InventoryStackId source = Stack("source");
        InventoryStackId destination = Stack("destination");
        sourceInventory.Grant(Fungible("template-277"), source, 3);

        DaggerfallItemInstances instances = new();
        DaggerfallItemInstanceMetadata metadata = Metadata("template-277", DaggerfallItemOwner.Corpse(10), material: "none", variant: 3,
            condition: 7, maximumCondition: 11, identified: false, stolen: true, quest: "mq-9", symbol: "relic", enchantment: "soul-trap") with { BookId = 59 };
        instances.RegisterStack(DaggerfallItemOwner.Corpse(10), source, metadata);

        destinationInventory.Grant(Fungible("template-277"), destination, 1);
        instances.RegisterStack(DaggerfallItemOwner.Player, destination,
            Metadata("template-277", DaggerfallItemOwner.Player, material: "iron", variant: 3,
                condition: 7, maximumCondition: 11, identified: false, stolen: true, quest: "mq-9", symbol: "relic", enchantment: "soul-trap"));
        Assert.Throws<InvalidOperationException>(() => instances.EnsureTransferCompatible(
            DaggerfallItemOwner.Corpse(10), DaggerfallItemOwner.Player, source, destination));
        Assert.Equal(3UL, Assert.Single(sourceInventory.View().Stacks).Quantity);
        Assert.Equal(1UL, Assert.Single(destinationInventory.View().Stacks).Quantity);

        instances.ReplaceStack(DaggerfallItemOwner.Player, destination, metadata with { Owner = DaggerfallItemOwner.Player });
        instances.EnsureTransferCompatible(DaggerfallItemOwner.Corpse(10), DaggerfallItemOwner.Player, source, destination);

        sourceInventory.TransferFungible(destinationOwner, source, destination, 2);
        instances.TransferStack(DaggerfallItemOwner.Corpse(10), DaggerfallItemOwner.Player, source, destination, sourceWasExhausted: false);
        DaggerfallItemInstanceMetadata moved = instances.RequireStack(DaggerfallItemOwner.Player, destination);
        Assert.Equal(metadata with { Owner = DaggerfallItemOwner.Player }, moved);

        DaggerfallStackSave saved = new(destination.Value, "template-277", 3, moved.Capture());
        new DaggerfallInventorySave([saved], [], []).Validate();
        DaggerfallItemInstances restored = new();
        restored.RegisterStack(DaggerfallItemOwner.Player, InventoryStackId.Parse(saved.StackId),
            DaggerfallItemInstanceMetadata.Restore(saved.ItemId, saved.Metadata));
        Assert.Equal(moved, restored.RequireStack(DaggerfallItemOwner.Player, destination));
    }

    [Fact]
    public void Duplicate_unique_placement_is_rejected()
    {
        DaggerfallItemInstances instances = new();
        DaggerfallItemInstanceMetadata metadata = Metadata("iron-dagger", DaggerfallItemOwner.Player, material: "iron", variant: 0,
            condition: 1, maximumCondition: 1, identified: true, stolen: false, quest: null, symbol: null, enchantment: null);
        instances.RegisterUnique(77, metadata);

        Assert.Throws<InvalidOperationException>(() => instances.RegisterUnique(77, metadata));
    }

    private static EntityId Owner(EntityDirectory entities, ulong id) =>
        entities.Create(new WorldRpg.Kit.World.DurableIdentityReference(WorldRpg.Kit.World.DurableIdentityKind.Container, id), new EntityTypeId("container"));

    private static InventoryStackId Stack(string id) => InventoryStackId.Parse(id);
    private static ItemDefinition Fungible(string id) => new(ItemDefinitionId.Parse(id), ItemKind.Fungible, 50);
    private static DaggerfallItemInstanceMetadata Metadata(string item, DaggerfallItemOwner owner, string material, int variant,
        int condition, int maximumCondition, bool identified, bool stolen, string? quest, string? symbol, string? enchantment) =>
        new DaggerfallItemInstanceMetadata(item, material, variant, condition, maximumCondition, identified, stolen, quest, symbol, enchantment, owner).Validate();
}
