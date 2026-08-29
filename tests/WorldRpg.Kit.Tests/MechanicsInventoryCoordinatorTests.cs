using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using Xunit;
using EngineEquipmentSlotId = Rusty.Engine.Mechanics.EquipmentSlotId;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Kit.Tests;

public sealed class MechanicsInventoryCoordinatorTests
{
    [Fact]
    public void Grant_and_consume_use_the_managed_inventory_world()
    {
        EntityId owner = new(7);
        InventoryWorld world = CreateWorld(owner);
        ItemDefinition gold = Fungible("gold", maximumQuantity: 100);
        MechanicsInventoryCoordinator inventory = new(
            world,
            owner,
            new Dictionary<InventoryItemId, ItemDefinition>
            {
                [new InventoryItemId("gold")] = gold,
            });

        InventoryMutationReceipt granted = inventory.Grant(
            new InventoryGrant("reward", "actor:9", new InventoryItemId("gold"), 3));
        InventoryMutationReceipt consumed = inventory.Consume(
            new InventoryConsume("consume", "actor:9", new InventoryItemId("gold"), 2));

        Assert.Equal(InventoryMutationKind.Grant, granted.Kind);
        Assert.Equal(3UL, granted.AfterQuantity);
        Assert.Equal(InventoryMutationKind.Consume, consumed.Kind);
        Assert.Equal(1UL, consumed.AfterQuantity);
        Assert.True(inventory.Read().Stacks.Single() is { Quantity: 1 } stack
            && stack.Definition == gold.Id);
    }

    [Fact]
    public void Invalid_or_unknown_inventory_requests_stop_before_managed_mutation()
    {
        EntityId owner = new(7);
        InventoryWorld world = CreateWorld(owner);
        MechanicsInventoryCoordinator inventory = new(
            world,
            owner,
            new Dictionary<InventoryItemId, ItemDefinition>());

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Consume(
            new InventoryConsume("consume", "test", new InventoryItemId("gold"), 0)));
        Assert.Throws<InvalidOperationException>(() => inventory.Grant(
            new InventoryGrant("reward", "test", new InventoryItemId("gold"), 1)));
        Assert.Empty(inventory.Read().Stacks);
    }

    [Fact]
    public void Materialize_equip_unequip_and_swap_use_managed_item_and_equipment_state()
    {
        EntityId owner = new(7);
        InventoryWorld world = CreateWorld(owner);
        ItemDefinition sword = UniqueEquipment("sword");
        ItemDefinition dagger = UniqueEquipment("dagger");
        EquipmentSlotDefinition hand = new(
            EngineEquipmentSlotId.Parse("hand"),
            [ItemClassificationId.Parse("blade")]);
        MechanicsEquipmentCoordinator equipment = new(
            world,
            owner,
            new Dictionary<InventoryItemId, ItemDefinition>
            {
                [new InventoryItemId("sword")] = sword,
                [new InventoryItemId("dagger")] = dagger,
            },
            new Dictionary<KitEquipmentSlotId, EquipmentSlotDefinition>
            {
                [new KitEquipmentSlotId("hand")] = hand,
            });

        KitUniqueInventoryItem swordItem = equipment.Materialize(
            new UniqueItemMaterialization("runtime-sword", 44, new InventoryItemId("sword")));
        KitUniqueInventoryItem daggerItem = equipment.Materialize(
            new UniqueItemMaterialization("runtime-dagger", 45, new InventoryItemId("dagger")));

        EquipmentMutationReceipt equipped = equipment.Equip(
            swordItem,
            [new KitEquipmentSlotId("hand")],
            new EquipmentChange("equip", "test"));
        EquipmentRead equippedView = equipment.Read();

        Assert.Equal(EquipmentMutationKind.Equip, equipped.Kind);
        Assert.True(equippedView.TryGet(new KitEquipmentSlotId("hand"), out KitUniqueInventoryItem equippedItem));
        Assert.Equal(swordItem, equippedItem);
        Assert.Contains(
            world.Read(owner).UniqueItems,
            item => item.Entity == new EntityId(45) && item.Definition == dagger.Id);

        EquipmentMutationReceipt unequipped = equipment.Unequip(
            swordItem,
            new EquipmentChange("unequip", "test"));
        Assert.Equal(EquipmentMutationKind.Unequip, unequipped.Kind);

        equipment.Equip(swordItem, [new KitEquipmentSlotId("hand")], new EquipmentChange("equip", "test"));
        EquipmentMutationReceipt swapped = equipment.Swap(
            swordItem,
            daggerItem,
            [new KitEquipmentSlotId("hand")],
            new EquipmentChange("swap", "test"));

        Assert.Equal(EquipmentMutationKind.Swap, swapped.Kind);
        Assert.Equal(swordItem.EntityId, swapped.ReplacedItem!.Value.Value);
        Assert.True(equipment.Read().TryGet(new KitEquipmentSlotId("hand"), out KitUniqueInventoryItem swappedItem));
        Assert.Equal(daggerItem, swappedItem);
    }

    private static InventoryWorld CreateWorld(EntityId owner)
    {
        InventoryWorld world = new();
        world.RegisterInventory(new InventoryState(owner));
        world.RegisterEquipment(new EquipmentState(owner));
        return world;
    }

    private static ItemDefinition Fungible(string id, ulong maximumQuantity) =>
        new(ItemDefinitionId.Parse(id), ItemKind.Fungible, maximumQuantity);

    private static ItemDefinition UniqueEquipment(string id) =>
        new(
            ItemDefinitionId.Parse(id),
            ItemKind.Unique,
            maximumQuantity: 1,
            classifications: [ItemClassificationId.Parse("blade")],
            equipment: new ItemEquipmentPolicy(requiredSlots: 1));
}
