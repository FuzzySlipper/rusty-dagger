using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallInventoryPresentationTests
{
    [Fact]
    public void Grid_swap_equip_replacement_and_unequip_keep_engine_items_and_layout_coherent()
    {
        using Fixture f = new();
        var before = f.Ui.Read();
        int daggerSlot = Item(before, f.Key(1002)).GridSlot!.Value;
        int goldSlot = Item(before, f.StackKey("gold-piece")).GridSlot!.Value;
        f.Ui.Move(new("inventory-move", before.Revision, f.Key(1002), goldSlot));
        var moved = f.Ui.Read();
        Assert.Equal(goldSlot, Item(moved, f.Key(1002)).GridSlot);
        Assert.Equal(daggerSlot, Item(moved, f.StackKey("gold-piece")).GridSlot);
        Assert.Equal(25UL, f.Inventory.Read().Stacks.Single().Quantity);

        f.Ui.Move(new("inventory-move", moved.Revision, f.Key(1002), TargetEquipment: "right-hand"));
        var equipped = f.Ui.Read();
        Assert.Equal(f.Key(1002), equipped.Slots.Single(slot => slot.Id == "right-hand").ItemKey);
        Assert.Null(Item(equipped, f.Key(1002)).GridSlot);
        Assert.Equal(goldSlot, Item(equipped, f.Key(1001)).GridSlot);
        f.Ui.Move(new("inventory-move", equipped.Revision, f.Key(1002), 49));
        var unequipped = f.Ui.Read();
        Assert.Equal(49, Item(unequipped, f.Key(1002)).GridSlot);
        Assert.Null(unequipped.Slots.Single(slot => slot.Id == "right-hand").ItemKey);
        Assert.Equal(3, f.Inventory.Read().UniqueItems.Count);
    }

    [Fact]
    public void Stale_revision_with_a_valid_selection_still_applies_while_missing_items_are_rejected()
    {
        using Fixture f = new();
        var before = f.Ui.Read();
        f.Ui.Move(new("inventory-move", before.Revision, f.Key(1002), 49));
        Assert.Equal(49, Item(f.Ui.Read(), f.Key(1002)).GridSlot);
        // The coordinators' containment checks decide staleness, not the revision: a move issued
        // from the older revision still applies while its selection is valid.
        f.Ui.Move(new("inventory-move", before.Revision, f.Key(1001), TargetEquipment: "right-hand"));
        Assert.Equal("Inventory updated.", f.Ui.Read().Message);
        Assert.Equal(f.Key(1001), f.Ui.Read().Slots.Single(slot => slot.Id == "right-hand").ItemKey);
        var current = f.Ui.Read();
        // A genuinely missing selection is rejected without touching contents, equipment, or layout.
        f.Ui.Move(new("inventory-move", before.Revision, "unique:999999", 10));
        Assert.Contains("no longer in your inventory", f.Ui.Read().Message);
        Assert.Equal(current.Revision, f.Ui.Read().Revision);
        f.Ui.Move(new("inventory-move", current.Revision, f.StackKey("gold-piece"), TargetEquipment: "head"));
        Assert.Contains("does not fit", f.Ui.Read().Message);
        Assert.Equal(f.Ui.Read().Revision, current.Revision);
        Assert.Equal(f.Key(1001), f.Ui.Read().Slots.Single(slot => slot.Id == "right-hand").ItemKey);
        Assert.Equal(49, Item(f.Ui.Read(), f.Key(1002)).GridSlot);
    }

    [Fact]
    public void Two_handed_assignment_uses_both_hands_and_reassignment_is_atomic()
    {
        using Fixture f = new();
        f.Equipment.Materialize(new(DurableIdentityKind.Item, 1004), new("iron-short-bow"));
        var before = f.Ui.Read();
        f.Ui.Move(new("inventory-move", before.Revision, f.Key(1004), TargetEquipment: "right-hand"));
        var bow = f.Ui.Read();
        Assert.Equal(new[] { "left-hand", "right-hand" }, Item(bow, f.Key(1004)).EquippedSlots.Order());
        f.Ui.Move(new("inventory-move", bow.Revision, f.Key(1002), TargetEquipment: "left-hand"));
        var dagger = f.Ui.Read();
        Assert.Empty(Item(dagger, f.Key(1004)).EquippedSlots);
        Assert.Equal(new[] { "left-hand" }, Item(dagger, f.Key(1002)).EquippedSlots);
        f.Ui.Move(new("inventory-move", dagger.Revision, f.Key(1002), TargetEquipment: "right-hand"));
        Assert.Equal(new[] { "right-hand" }, Item(f.Ui.Read(), f.Key(1002)).EquippedSlots);
    }

    [Fact]
    public void Typed_service_moves_equip_swap_and_two_hand_without_ui_dtos()
    {
        using Fixture f = new();
        var before = f.Ui.Read();
        static UniqueItem Typed(Fixture f, ulong durable, string definition) =>
            new(f.Entities.Resolve(new(DurableIdentityKind.Item, durable)).Value, new InventoryItemId(definition));
        UniqueItem dagger = Typed(f, 1002, Item(before, f.Key(1002)).Definition);

        // Equip through the service: the same operation the UI adapter calls.
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToSlot(dagger, new SlotId("left-hand")).Outcome);
        Assert.Equal(["left-hand"], Item(f.Ui.Read(), f.Key(1002)).EquippedSlots);

        // Swap onto the occupied hand displaces the sword without partial mutation.
        UniqueItem sword = Typed(f, 1001, Item(before, f.Key(1001)).Definition);
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToSlot(dagger, new SlotId("right-hand")).Outcome);
        var swapped = f.Ui.Read();
        Assert.Equal(f.Key(1002), swapped.Slots.Single(slot => slot.Id == "right-hand").ItemKey);
        Assert.Empty(Item(swapped, f.Key(1001)).EquippedSlots);

        // A two-handed bow takes both hands; the dagger returns to the grid.
        f.Equipment.Materialize(new(DurableIdentityKind.Item, 1004), new("iron-short-bow"));
        UniqueItem bow = Typed(f, 1004, "iron-short-bow");
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToSlot(bow, new SlotId("right-hand")).Outcome);
        Assert.Equal(["left-hand", "right-hand"], Item(f.Ui.Read(), f.Key(1004)).EquippedSlots.Order());
    }

    [Fact]
    public void Typed_service_rejects_unknown_items_and_bad_destinations_without_mutation()
    {
        using Fixture f = new();
        var before = f.Ui.Read();
        ulong storeBefore = f.Inventory.Read().StoreRevision;
        Assert.Equal(EquipmentMoveOutcome.UnknownItem,
            f.Moves.MoveToSlot(new UniqueItem(999999, new InventoryItemId("iron-longsword")), new SlotId("right-hand")).Outcome);
        Assert.Equal(EquipmentMoveOutcome.UnknownItem,
            f.Moves.MoveToGrid(new UniqueItem(999999, new InventoryItemId("iron-longsword")), 10).Outcome);
        Assert.Equal(EquipmentMoveOutcome.InvalidDestination,
            f.Moves.MoveStackToGrid(f.Inventory.Read().Stacks.First().Id, DaggerfallEquipmentMoves.GridCapacity).Outcome);
        Assert.Equal(storeBefore, f.Inventory.Read().StoreRevision);
        Assert.Equal(before.Revision, f.Ui.Read().Revision);
    }

    [Fact]
    public void Exclusive_group_membership_evicts_across_slots_through_the_adapter()
    {
        using Fixture f = new();
        var before = f.Ui.Read();
        Assert.Equal(f.Key(1001), before.Slots.Single(slot => slot.Id == "right-hand").ItemKey);
        // Every hand item shares the "hands" group, so taking the left hand with the dagger evicts
        // the sword from the right: the conflict is by group, not by slot overlap. Dropping the
        // group clause would leave both equipped and this test fails.
        f.Ui.Move(new("inventory-move", before.Revision, f.Key(1002), TargetEquipment: "left-hand"));
        var moved = f.Ui.Read();
        Assert.Equal(f.Key(1002), moved.Slots.Single(slot => slot.Id == "left-hand").ItemKey);
        Assert.Null(moved.Slots.Single(slot => slot.Id == "right-hand").ItemKey);
        Assert.NotNull(Item(moved, f.Key(1001)).GridSlot);
    }

    [Fact]
    public void Policy_reports_slot_and_group_conflicts_deduped_per_item()
    {
        using Fixture f = new();
        DaggerfallItemDefinition dagger = f.Definitions.Items[new DaggerfallItemId(Item(f.Ui.Read(), f.Key(1002)).Definition)];
        UniqueItem bow = new(900, new InventoryItemId("iron-short-bow"));
        UniqueItem sword = new(901, new InventoryItemId("iron-longsword"));
        UniqueItem daggerItem = new(902, new InventoryItemId(Item(f.Ui.Read(), f.Key(1002)).Definition));
        EquipmentRead equipped = new(
            [new(new SlotId("left-hand"), bow), new(new SlotId("right-hand"), bow), new(new SlotId("right-hand"), sword)],
            0, 0);
        // The bow matches twice (left-hand slot overlap plus the shared "hands" group on the
        // right) but displaces once; the sword matches by group. Three assignments, two items.
        IReadOnlyList<UniqueItem> conflicts = DaggerfallEquipmentPolicy.ConflictingEquipped(
            f.Definitions, equipped, daggerItem, dagger, [new SlotId("left-hand")]);
        Assert.Equal(2, conflicts.Count);
        Assert.Contains(bow, conflicts);
        Assert.Contains(sword, conflicts);
    }

    [Fact]
    public void Equipped_item_dropped_on_a_stack_cell_is_refused_without_exception()
    {
        using Fixture f = new();
        var before = f.Ui.Read();
        int goldSlot = Item(before, f.StackKey("gold-piece")).GridSlot!.Value;
        // The sword starts equipped; dropping it on the gold stack's cell refuses gracefully.
        // The previous implementation threw FormatException out of Move on this input.
        f.Ui.Move(new("inventory-move", before.Revision, f.Key(1001), goldSlot));
        Assert.Contains("Stacked items cannot be equipped", f.Ui.Read().Message);
        Assert.Equal(f.Key(1001), f.Ui.Read().Slots.Single(slot => slot.Id == "right-hand").ItemKey);
        Assert.Equal(goldSlot, Item(f.Ui.Read(), f.StackKey("gold-piece")).GridSlot);
    }

    [Fact]
    public void Failed_engine_reassignment_does_not_publish_the_temporary_unequip()
    {
        using Fixture f = new();
        var before = f.Equipment.Read();
        Assert.Throws<MechanicsException>(() => f.Equipment.Reassign(
            new(f.Entities.Resolve(new(DurableIdentityKind.Item, 1001)).Value, new("iron-longsword")), [new("head")], []));
        var after = f.Equipment.Read();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.RelationshipStateRevision, after.RelationshipStateRevision);
        Assert.Equal(before.Assignments, after.Assignments);
    }

    private static InventoryItemPresentation Item(InventoryPresentation value, string key) => value.Items.Single(item => item.Key == key);
    private sealed class Fixture : IDisposable
    {
        internal readonly EntityDirectory Entities = new();
        internal string Key(ulong id) => DaggerfallInventoryPresentation.UniqueKey(Entities.Resolve(new(DurableIdentityKind.Item, id)).Value);
        internal string StackKey(string definition) => DaggerfallInventoryPresentation.StackKey(
            Inventory.Read().Stacks.Single(stack => stack.Definition.Value == definition).Id);
        internal readonly MechanicsInventoryCoordinator Inventory;
        internal readonly MechanicsEquipmentCoordinator Equipment;
        internal readonly DaggerfallEquipmentMoves Moves;
        internal readonly DaggerfallInventoryPresentation Ui;
        internal readonly DaggerfallDefinitions Definitions;
        internal Fixture()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
            var definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(directory!.FullName, "content/worldrpg/payloads/daggerfall.base.json")));
            var items = definitions.Items.Values.ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            var slots = definitions.EquipmentSlots.Values.ToDictionary(slot => new SlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
            InventoryStore world = new();
            EntityId owner = Entities.Create(new(DurableIdentityKind.Actor, 1), new("test.player"));
            world.RegisterInventory(new InventoryState(owner));
            world.RegisterEquipment(new EquipmentState(owner));
            InventoryComponent inventory = new(world, owner);
            EquipmentComponent equipment = new(world, owner);
            Entities.Store.Add(owner, inventory);
            Entities.Store.Add(owner, equipment);
            Inventory = new(inventory, Entities, items);
            Equipment = new(inventory, equipment, Entities, items, slots);
            Moves = new(Inventory, Equipment, definitions);
            Definitions = definitions;
            foreach ((DaggerfallLoadoutEntry entry, int ordinal) in definitions.RequireActor(new("player")).Loadout.Select((entry, ordinal) => (entry, ordinal)))
            {
                if (entry.UniqueEntityId is ulong entity)
                {
                    var item = Equipment.Materialize(new(DurableIdentityKind.Item, entity), new(entry.ItemId.Value));
                    if (entry.EquipSlot is { } slot) Equipment.Equip(item, [new(slot.Value)]);
                }
                else Inventory.Grant(new(new(entry.ItemId.Value), InventoryStackId.Parse($"fixture.{ordinal}"), entry.Quantity));
            }
            Ui = new(Moves, definitions, new Dictionary<string, string>());
        }
        public void Dispose() => Entities.Dispose();
    }
}
