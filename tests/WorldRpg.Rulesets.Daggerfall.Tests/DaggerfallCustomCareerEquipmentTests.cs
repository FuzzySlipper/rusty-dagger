using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCustomCareerEquipmentTests
{
    [Fact]
    public void Custom_forbidden_shield_rejects_a_factory_materialized_template_item()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallItemFactory factory = new(definitions, RandomMinimum());
        DaggerfallCreatedItem shield = factory.Create(new DaggerfallItemCreateRequest("Armor", "steel-buckler", DaggerfallItemOwner.Player,
            TemplateIndex: 109, Material: "steel", Race: "breton", Gender: "male"));
        using EntityDirectory entities = new();
        EntityId owner = entities.Create(new(DurableIdentityKind.Actor, 1), new EntityTypeId("test.player"));
        InventoryStore store = new();
        store.RegisterInventory(new InventoryState(owner));
        store.RegisterEquipment(new EquipmentState(owner));
        InventoryComponent inventoryComponent = new(store, owner);
        EquipmentComponent equipmentComponent = new(store, owner);
        entities.Store.Add(owner, inventoryComponent);
        entities.Store.Add(owner, equipmentComponent);
        var items = definitions.Items.Values.Concat(definitions.TemplateItems.Values).ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
        var slots = definitions.EquipmentSlots.Values.ToDictionary(slot => new SlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
        MechanicsInventoryCoordinator inventory = new(inventoryComponent, entities, items);
        MechanicsEquipmentCoordinator equipment = new(inventoryComponent, equipmentComponent, entities, items, slots);
        DaggerfallItemInstances instances = new();
        DurableIdentityReference identity = new(DurableIdentityKind.Item, 700);
        factory.Materialize(shield, inventory, instances, unique: identity);
        UniqueItem item = new(entities.Resolve(identity).Value, shield.Item);
        DaggerfallEquipmentMoves moves = new(inventory, equipment, definitions, () => ["forbidden-shield:buckler"]);

        EquipmentMoveResult result = moves.MoveToSlot(item, new SlotId("left-hand"));

        Assert.Equal(EquipmentMoveOutcome.Rejected, result.Outcome);
        Assert.Contains("buckler", result.Detail, StringComparison.Ordinal);
        Assert.Empty(equipment.Read().Assignments);
    }

    private static DaggerfallDefinitions LoadDefinitions() =>
        TestPayload.Definitions;

    private static IRandomService RandomMinimum() => DispatchProxy.Create<IRandomService, RandomMinimumProxy>();

    private class RandomMinimumProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }

}
