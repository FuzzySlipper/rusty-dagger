using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The pack grid's save and restore at its owner, over a player's own pack with no session.</summary>
public sealed class DaggerfallPackLayoutTests
{
    [Fact]
    public void A_pack_rebuilt_from_its_save_keeps_the_cells_the_player_arranged_not_the_grant_order()
    {
        using Pack saved = new();
        // Uniques first, as a new game's kit is granted; a fresh read would put the stacks first.
        saved.Sword(1); saved.Sword(2); saved.Sword(3);
        saved.Stack("Gems", "pack.rubies", 3, 0); saved.Stack("Currency", "pack.gold", 500, 276);
        saved.Moves.ReconcileLayout();
        saved.Moves.ArrangeGrid(saved.Cells().Select(cell => cell.Key).Reverse());
        Assert.Equal(EquipmentMoveOutcome.Applied, saved.Moves.MoveStackToGrid(InventoryStackId.Parse("pack.gold"), 0).Outcome);
        Assert.Equal(EquipmentMoveOutcome.Applied, saved.Moves.MoveStackToGrid(InventoryStackId.Parse("pack.gold"), 30).Outcome);
        (int Cell, string Item)[] arranged = saved.Durable();
        Assert.NotEqual(0, arranged[0].Cell);

        DaggerfallInventorySave contents = saved.Contents();
        DaggerfallPackLayoutSave layout = saved.Moves.CaptureLayout();
        layout.Validate(contents);
        using Pack loaded = new();
        loaded.Apply(contents);
        loaded.Moves.RestoreLayout(layout);

        Assert.Equal(arranged, loaded.Durable());
    }

    [Fact]
    public void A_layout_the_saved_pack_disagrees_with_is_refused()
    {
        using Pack saved = new();
        saved.Sword(1); saved.Stack("Currency", "pack.gold", 500, 276);
        DaggerfallInventorySave contents = saved.Contents();
        DaggerfallPackLayoutSave layout = saved.Moves.CaptureLayout();
        ulong sword = contents.UniqueItems.Single().EntityId;

        Assert.Contains("no cell", Assert.Throws<ArgumentException>(() => (layout with { Places = layout.Places[..1] }).Validate(contents)).Message);
        Assert.Throws<ArgumentException>(() => (layout with { Places = [.. layout.Places, new(5, ItemEntityId: sword + 1)] }).Validate(contents));
        Assert.Throws<ArgumentException>(() => (layout with { Places = [new(0, StackId: "pack.gold"), new(0, ItemEntityId: sword)] }).Validate(contents));
        Assert.Throws<ArgumentException>(() => (layout with { Places = [new(0, StackId: "pack.gold"), new(DaggerfallEquipmentMoves.GridCapacity, ItemEntityId: sword)] }).Validate(contents));
        Assert.Throws<ArgumentException>(() => (layout with { Places = [new(0, StackId: "pack.gold", ItemEntityId: sword)] }).Validate(contents));
        Assert.Throws<ArgumentException>(() => layout.Validate(contents with { Equipment = [new("right-hand", sword)] }));
    }

    /// <summary>A player's pack and worn set over their own stores, with the grid's owner.</summary>
    private sealed class Pack : IDisposable
    {
        private static readonly DaggerfallDefinitions Definitions = TestPayload.Definitions;
        private readonly ActorsState actors = new();
        private readonly MechanicsInventoryCoordinator inventory;
        private readonly MechanicsEquipmentCoordinator equipment;
        private readonly DaggerfallItemInstances instances = new();
        private readonly DaggerfallUniqueItemAllocator allocator = new(DaggerfallUniqueItemAllocator.DefaultFirstEntityId);
        private readonly DaggerfallItemFactory factory = new(Definitions, RandomMinimum.Create());
        internal DaggerfallEquipmentMoves Moves { get; }

        internal Pack()
        {
            DaggerfallActorDefinition player = Definitions.RequireActor(new DaggerfallActorId("player"));
            PlayerActorState actor = actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId(player.Id.Value),
                TestSessions.HealthyMechanics(), "health", DaggerfallActorFactory.PlayerCapabilities);
            InventoryStore store = new();
            store.RegisterInventory(new InventoryState(actor.Actor.Entity, [new InventoryCapacityLimit(DaggerfallEncumbrancePolicy.ClassicWeightMetric, ulong.MaxValue)]));
            store.RegisterEquipment(new EquipmentState(actor.Actor.Entity));
            InventoryComponent pack = new(store, actor.Actor.Entity);
            EquipmentComponent worn = new(store, actor.Actor.Entity);
            actor.Actor.Add(pack);
            actor.Actor.Add(worn);
            Dictionary<InventoryItemId, ItemDefinition> items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values)
                .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerfallActorFactory.ToManagedItem);
            Dictionary<WorldRpg.Kit.Inventory.EquipmentSlotId, EquipmentSlotDefinition> slots = Definitions.EquipmentSlots.Values
                .ToDictionary(slot => new WorldRpg.Kit.Inventory.EquipmentSlotId(slot.Id.Value), DaggerfallActorFactory.ToManagedSlot);
            inventory = new(pack, actors.Entities, items);
            equipment = new(pack, worn, actors.Entities, items, slots);
            Moves = new(inventory, equipment, Definitions, itemInstances: instances);
        }

        internal void Sword(int sword) => factory.Materialize(factory.Create(new("Weapons", $"pack.sword.{sword}", DaggerfallItemOwner.Player,
            TemplateIndex: 113, Material: new[] { "iron", "steel", "silver", "elven" }[sword - 1])), inventory, instances, unique: allocator.AllocateReference());

        internal void Stack(string category, string id, ulong quantity, int template) => factory.Materialize(factory.Create(new(category, id,
            DaggerfallItemOwner.Player, Quantity: quantity, TemplateIndex: template)), inventory, instances, InventoryStackId.Parse(id));

        internal DaggerfallInventorySave Contents()
        {
            var (stacks, uniques) = DaggerfallInventorySaveBoundary.CaptureContents(inventory.Read(), DaggerfallItemOwner.Player, instances, actors.Entities);
            return new(stacks, uniques, []);
        }

        /// <summary>Grants a saved pack's contents back, as the session's load does.</summary>
        internal void Apply(DaggerfallInventorySave saved)
        {
            foreach (DaggerfallStackSave stack in saved.Stacks)
            {
                inventory.Grant(new InventoryGrant(new InventoryItemId(stack.ItemId), InventoryStackId.Parse(stack.StackId), stack.Quantity));
                instances.RegisterStack(DaggerfallItemOwner.Player, InventoryStackId.Parse(stack.StackId), DaggerfallItemInstanceMetadata.Restore(stack.ItemId, stack.Metadata));
            }
            foreach (DaggerfallUniqueSave item in saved.UniqueItems)
            {
                equipment.Materialize(new DurableIdentityReference(DurableIdentityKind.Item, item.EntityId), new InventoryItemId(item.ItemId),
                    DaggerfallEncumbrancePolicy.CapacityOverride(item.Metadata.WeightClassicUnits));
                instances.RegisterUnique(item.EntityId, DaggerfallItemInstanceMetadata.Restore(item.ItemId, item.Metadata));
            }
        }

        /// <summary>Each placed cell and its layout key, by cell.</summary>
        internal (int Cell, string Key)[] Cells()
        {
            Moves.ReconcileLayout();
            InventoryView read = inventory.Read();
            return [.. read.Stacks.Select(stack => DaggerfallEquipmentMoves.LayoutKey(stack.Id))
                .Concat(read.UniqueItems.Select(item => DaggerfallEquipmentMoves.LayoutKey(item.Entity.Value)))
                .Where(key => Moves.GridPosition(key) is not null)
                .Select(key => (Moves.GridPosition(key)!.Value, key)).OrderBy(cell => cell.Item1)];
        }

        /// <summary>Each placed cell and its item by what outlasts a load: a stack's id, a unique's durable identity.</summary>
        internal (int Cell, string Item)[] Durable()
        {
            Moves.ReconcileLayout();
            InventoryView read = inventory.Read();
            return [.. read.Stacks.Select(stack => (Key: DaggerfallEquipmentMoves.LayoutKey(stack.Id), Item: $"stack:{stack.Id.Value}"))
                .Concat(read.UniqueItems.Select(item => (Key: DaggerfallEquipmentMoves.LayoutKey(item.Entity.Value), Item: $"item:{actors.Entities.IdentityOf(item.Entity).Value}")))
                .Where(entry => Moves.GridPosition(entry.Key) is not null)
                .Select(entry => (Moves.GridPosition(entry.Key)!.Value, entry.Item)).OrderBy(cell => cell.Item1)];
        }

        public void Dispose() => actors.Dispose();
    }
}
