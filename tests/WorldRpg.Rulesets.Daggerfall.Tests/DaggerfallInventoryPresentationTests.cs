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
        Assert.Equal(new UniqueItem(f.Entities.Resolve(new(DurableIdentityKind.Item, 1002)).Value, new("iron-dagger")), f.Ui.LastEquipmentChange?.Equipped);
        Assert.Equal("equip", equipped.EquipmentChange?.Cue);
        f.Ui.Move(new("inventory-move", equipped.Revision, f.Key(1002), 49));
        var unequipped = f.Ui.Read();
        Assert.Equal(49, Item(unequipped, f.Key(1002)).GridSlot);
        Assert.Null(unequipped.Slots.Single(slot => slot.Id == "right-hand").ItemKey);
        Assert.Null(f.Ui.LastEquipmentChange?.Equipped);
        Assert.Equal("unequip", unequipped.EquipmentChange?.Cue);
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
    public void Two_hand_transition_replaces_both_hands_atomically()
    {
        using Fixture f = new();
        static UniqueItem Typed(Fixture fixture, ulong durable, string definition) => new(fixture.Entities.Resolve(new(DurableIdentityKind.Item, durable)).Value, new(definition));
        UniqueItem dagger = Typed(f, 1002, "iron-dagger");
        UniqueItem sword = Typed(f, 1001, "iron-longsword");
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToSlot(dagger, new SlotId("left-hand")).Outcome);
        f.Equipment.Materialize(new(DurableIdentityKind.Item, 1004), new("iron-short-bow"));
        UniqueItem bow = Typed(f, 1004, "iron-short-bow");
        ulong before = f.Equipment.Read().RelationshipStateRevision;

        EquipmentMoveResult result = f.Moves.MoveToSlot(bow, new SlotId("right-hand"));

        Assert.Equal(EquipmentMoveOutcome.Applied, result.Outcome);
        Assert.Equal(new[] { dagger, sword }.OrderBy(item => item.EntityId), result.RemovedItems.OrderBy(item => item.EntityId));
        EquipmentRead after = f.Equipment.Read();
        // The Engine records each removed relationship inside one candidate; the one Daggerfall
        // notification below is the product-level operation boundary.
        Assert.True(after.RelationshipStateRevision > before);
        Assert.Equal(bow, after.Assignments.Single(assignment => assignment.Slot.Value == "right-hand").Item);
        Assert.Equal(bow, after.Assignments.Single(assignment => assignment.Slot.Value == "left-hand").Item);
        Assert.DoesNotContain(after.Assignments, assignment => assignment.Item == dagger || assignment.Item == sword);
    }

    [Fact]
    public void Two_hand_change_publishes_one_complete_notification_with_donor_hand_timing()
    {
        using Fixture f = new();
        static UniqueItem Typed(Fixture fixture, ulong durable, string definition) => new(fixture.Entities.Resolve(new(DurableIdentityKind.Item, durable)).Value, new(definition));
        List<DaggerfallEquipmentChange> changes = [];
        f.Moves.Changed += changes.Add;
        f.Equipment.Materialize(new(DurableIdentityKind.Item, 1101), new("template-120-iron"));
        f.Equipment.Materialize(new(DurableIdentityKind.Item, 1102), new("template-113-iron"));
        f.Equipment.Materialize(new(DurableIdentityKind.Item, 1103), new("template-129-iron"));
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToSlot(Typed(f, 1101, "template-120-iron"), new("right-hand")).Outcome);
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToSlot(Typed(f, 1102, "template-113-iron"), new("left-hand")).Outcome);
        changes.Clear();

        EquipmentMoveResult result = f.Moves.MoveToSlot(Typed(f, 1103, "template-129-iron"), new("right-hand"));

        DaggerfallEquipmentChange change = Assert.Single(changes);
        Assert.Equal(result.Change, change);
        Assert.Equal(new DaggerfallHandEquipTiming(5700, 4500), change.Timing);
        Assert.Equal(DaggerfallEquipmentCue.Equip, change.Cue);
        Assert.Equal(new[] { Typed(f, 1101, "template-120-iron"), Typed(f, 1102, "template-113-iron") }.OrderBy(item => item.EntityId), change.Removed.OrderBy(item => item.EntityId));
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
    public void Content_slot_meaning_rejects_a_weapon_from_an_amulet_slot()
    {
        using Fixture f = new();
        UniqueItem sword = new(f.Entities.Resolve(new(DurableIdentityKind.Item, 1001)).Value, new InventoryItemId("iron-longsword"));
        ulong before = f.Equipment.Read().RelationshipStateRevision;

        EquipmentMoveResult result = f.Moves.MoveToSlot(sword, new SlotId("amulet0"));

        Assert.Equal(EquipmentMoveOutcome.Incompatible, result.Outcome);
        Assert.Equal(before, f.Equipment.Read().RelationshipStateRevision);
        Assert.Equal(f.Key(1001), f.Ui.Read().Slots.Single(slot => slot.Id == "right-hand").ItemKey);
    }

    [Fact]
    public void Broken_item_is_rejected_before_an_equipment_mutation()
    {
        using Fixture f = new(enforceItemCondition: true);
        DaggerfallItemInstanceMetadata metadata = f.ItemInstances!.RequireUnique(1001);
        f.ItemInstances.ReplaceUnique(1001, metadata with { CurrentCondition = 0 });
        UniqueItem sword = new(f.Entities.Resolve(new(DurableIdentityKind.Item, 1001)).Value, new InventoryItemId("iron-longsword"));
        EquipmentRead before = f.Equipment.Read();

        EquipmentMoveResult result = f.Moves.MoveToSlot(sword, new SlotId("right-hand"));

        Assert.Equal(EquipmentMoveOutcome.Rejected, result.Outcome);
        Assert.Equal("That item is broken.", result.Detail);
        Assert.Equal(before.Assignments, f.Equipment.Read().Assignments);
        Assert.Equal(before.RelationshipStateRevision, f.Equipment.Read().RelationshipStateRevision);
    }

    [Fact]
    public void Predefined_class_restrictions_reject_live_equipment()
    {
        using Fixture f = new(predefinedCareer: "class00");
        UniqueItem sword = new(f.Entities.Resolve(new(DurableIdentityKind.Item, 1001)).Value, new InventoryItemId("iron-longsword"));
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToGrid(sword, 49).Outcome);

        EquipmentMoveResult result = f.Moves.MoveToSlot(sword, new SlotId("right-hand"));

        Assert.Equal(EquipmentMoveOutcome.Rejected, result.Outcome);
        Assert.Contains("long blade", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(f.Equipment.Read().Assignments, assignment => assignment.Item == sword);
    }

    [Fact]
    public void Moving_equipped_item_to_grid_publishes_one_complete_unequip_change()
    {
        using Fixture f = new();
        UniqueItem sword = new(f.Entities.Resolve(new(DurableIdentityKind.Item, 1001)).Value, new InventoryItemId("iron-longsword"));
        List<DaggerfallEquipmentChange> changes = [];
        f.Moves.Changed += changes.Add;

        EquipmentMoveResult result = f.Moves.MoveToGrid(sword, 49);

        DaggerfallEquipmentChange change = Assert.Single(changes);
        Assert.Equal(result.Change, change);
        Assert.Null(change.Equipped);
        Assert.Equal([sword], change.Removed);
        Assert.Equal(DaggerfallEquipmentCue.Unequip, change.Cue);
        Assert.Equal(DaggerfallHandEquipTiming.None, change.Timing);
        Assert.DoesNotContain(f.Equipment.Read().Assignments, assignment => assignment.Item == sword);
    }

    [Fact]
    public void Grid_transfer_publishes_one_complete_transfer_change()
    {
        using Fixture f = new();
        UniqueItem sword = new(f.Entities.Resolve(new(DurableIdentityKind.Item, 1001)).Value, new InventoryItemId("iron-longsword"));
        UniqueItem dagger = new(f.Entities.Resolve(new(DurableIdentityKind.Item, 1002)).Value, new InventoryItemId("iron-dagger"));
        int target = Item(f.Ui.Read(), f.Key(1002)).GridSlot!.Value;
        List<DaggerfallEquipmentChange> changes = [];
        f.Moves.Changed += changes.Add;

        EquipmentMoveResult result = f.Moves.MoveToGrid(sword, target);

        DaggerfallEquipmentChange change = Assert.Single(changes);
        Assert.Equal(result.Change, change);
        Assert.Equal(dagger, change.Equipped);
        Assert.Equal([sword], change.Removed);
        Assert.Equal(DaggerfallEquipmentCue.Transfer, change.Cue);
        Assert.Equal(dagger, f.Equipment.Read().Assignments.Single(assignment => assignment.Slot.Value == "right-hand").Item);
        Assert.Equal(target, f.Moves.GridPosition(DaggerfallEquipmentMoves.LayoutKey(sword)));
    }

    [Fact]
    public void Newly_forbidden_equipment_publishes_one_complete_unequip_change()
    {
        List<string> restrictions = [];
        using Fixture f = new(forbiddenEquipment: () => restrictions);
        UniqueItem sword = new(f.Entities.Resolve(new(DurableIdentityKind.Item, 1001)).Value, new InventoryItemId("iron-longsword"));
        List<DaggerfallEquipmentChange> changes = [];
        f.Moves.Changed += changes.Add;
        restrictions.Add("forbidden-weapon:long-blade");

        Assert.Equal(1, f.Moves.UnequipForbidden());

        DaggerfallEquipmentChange change = Assert.Single(changes);
        Assert.Null(change.Equipped);
        Assert.Equal([sword], change.Removed);
        Assert.Equal(DaggerfallEquipmentCue.Unequip, change.Cue);
        Assert.Equal(change, f.Ui.LastEquipmentChange);
        Assert.DoesNotContain(f.Equipment.Read().Assignments, assignment => assignment.Item == sword);
    }

    [Fact]
    public void One_hand_weapons_share_hands_without_displacing_the_other_hand()
    {
        using Fixture f = new();
        var before = f.Ui.Read();
        Assert.Equal(f.Key(1001), before.Slots.Single(slot => slot.Id == "right-hand").ItemKey);
        // ItemEquipTable permits one-hand weapons in each hand. Hand conflicts are slot occupancy;
        // the two-hand/shield rules determine the exceptional transitions.
        f.Ui.Move(new("inventory-move", before.Revision, f.Key(1002), TargetEquipment: "left-hand"));
        var moved = f.Ui.Read();
        Assert.Equal(f.Key(1002), moved.Slots.Single(slot => slot.Id == "left-hand").ItemKey);
        Assert.Equal(f.Key(1001), moved.Slots.Single(slot => slot.Id == "right-hand").ItemKey);
        Assert.Null(Item(moved, f.Key(1001)).GridSlot);
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
        // The bow overlaps both hand slots and displaces once; independent one-hand weapons
        // no longer conflict through the donor-inaccurate shared-hands group.
        IReadOnlyList<UniqueItem> conflicts = DaggerfallEquipmentPolicy.ConflictingEquipped(
            f.Definitions, equipped, daggerItem, dagger, [new SlotId("left-hand")]);
        Assert.Single(conflicts);
        Assert.Contains(bow, conflicts);
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
        internal readonly DaggerfallItemInstances? ItemInstances;
        internal Fixture(bool enforceItemCondition = false, Func<IReadOnlyList<string>>? forbiddenEquipment = null, string? predefinedCareer = null)
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
            var definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(directory!.FullName, "content/worldrpg/payloads/daggerfall.base.json")));
            var items = definitions.Items.Values.Concat(definitions.TemplateItems.Values).ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
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
            ItemInstances = enforceItemCondition ? new DaggerfallItemInstances() : null;
            Func<IReadOnlyList<string>>? restrictions = forbiddenEquipment ?? (predefinedCareer is null
                ? null
                : () => definitions.Catalogs.RequireCareer(predefinedCareer).ForbiddenEquipment);
            Moves = new(Inventory, Equipment, definitions, restrictions, ItemInstances);
            Definitions = definitions;
            foreach ((DaggerfallLoadoutEntry entry, int ordinal) in definitions.RequireActor(new("player")).Loadout.Select((entry, ordinal) => (entry, ordinal)))
            {
                if (entry.UniqueEntityId is ulong entity)
                {
                    var item = Equipment.Materialize(new(DurableIdentityKind.Item, entity), new(entry.ItemId.Value));
                    if (ItemInstances is not null) ItemInstances.RegisterDefaultUnique(entity, definitions.RequireItem(entry.ItemId), DaggerfallItemOwner.Player);
                    if (entry.EquipSlot is { } slot) Equipment.Equip(item, [new(slot.Value)]);
                }
                else Inventory.Grant(new(new(entry.ItemId.Value), InventoryStackId.Parse($"fixture.{ordinal}"), entry.Quantity));
            }
            Ui = new(Moves, definitions, new Dictionary<string, string>());
        }
        public void Dispose() => Entities.Dispose();
    }
}
