using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallInventoryPresentationTests
{
    [Fact]
    public void Grid_swap_equip_replacement_and_unequip_keep_engine_items_and_layout_coherent()
    {
        using Fixture f = new();
        var before = f.Ui.Read();
        int daggerSlot = Item(before, f.Key(1002)).GridSlot!.Value;
        int goldSlot = Item(before, "stack:gold-piece").GridSlot!.Value;
        f.Ui.Move(new("inventory-move", before.Revision, f.Key(1002), goldSlot));
        var moved = f.Ui.Read();
        Assert.Equal(goldSlot, Item(moved, f.Key(1002)).GridSlot);
        Assert.Equal(daggerSlot, Item(moved, "stack:gold-piece").GridSlot);
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
    public void Stale_and_incompatible_drops_preserve_contents_equipment_and_layout()
    {
        using Fixture f = new();
        var before = f.Ui.Read();
        f.Ui.Move(new("inventory-move", before.Revision, f.Key(1002), 49));
        var current = f.Ui.Read();
        f.Ui.Move(new("inventory-move", before.Revision, f.Key(1002), TargetEquipment: "right-hand"));
        Assert.Contains("changed", f.Ui.Read().Message);
        Assert.Equal(current.Revision, f.Ui.Read().Revision);
        f.Ui.Move(new("inventory-move", current.Revision, "stack:gold-piece", TargetEquipment: "head"));
        Assert.Contains("does not fit", f.Ui.Read().Message);
        Assert.Equal(current.Revision, f.Ui.Read().Revision);
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
        internal readonly MechanicsInventoryCoordinator Inventory;
        internal readonly MechanicsEquipmentCoordinator Equipment;
        internal readonly DaggerfallInventoryPresentation Ui;
        internal Fixture()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
            var definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(directory!.FullName, "content/worldrpg/payloads/daggerfall.base.json")));
            var items = definitions.Items.Values.ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerfallSession.ToManagedItem);
            var slots = definitions.EquipmentSlots.Values.ToDictionary(slot => new SlotId(slot.Id.Value), DaggerfallSession.ToManagedSlot);
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
            foreach (var entry in definitions.RequireActor(new("player")).Loadout)
            {
                if (entry.UniqueEntityId is ulong entity)
                {
                    var item = Equipment.Materialize(new(DurableIdentityKind.Item, entity), new(entry.ItemId.Value));
                    if (entry.EquipSlot is { } slot) Equipment.Equip(item, [new(slot.Value)]);
                }
                else Inventory.Grant(new(new(entry.ItemId.Value), entry.Quantity));
            }
            Ui = new(Inventory, Equipment, definitions, new Dictionary<string, string>());
        }
        public void Dispose() => Entities.Dispose();
    }
}
