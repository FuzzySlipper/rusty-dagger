using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallGroundLootPresentationTests
{
    [Fact]
    public void Dropped_stack_opens_as_semantic_loot_transfers_a_selected_partial_amount_and_captures_its_profile()
    {
        DaggerfallDefinitions definitions = ReadDefinitions();
        using EntityDirectory entities = new();
        InventoryStore store = new();
        EntityId player = entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("player"));
        store.RegisterInventory(new InventoryState(player));
        store.RegisterEquipment(new EquipmentState(player));
        InventoryComponent inventoryComponent = new(store, player);
        EquipmentComponent equipmentComponent = new(store, player);
        entities.Store.Add(player, inventoryComponent);
        entities.Store.Add(player, equipmentComponent);
        Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.Concat(definitions.TemplateItems.Values)
            .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
        Dictionary<KitEquipmentSlotId, EquipmentSlotDefinition> slots = definitions.EquipmentSlots.Values
            .ToDictionary(slot => new KitEquipmentSlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
        MechanicsInventoryCoordinator inventory = new(inventoryComponent, entities, items);
        MechanicsEquipmentCoordinator equipment = new(inventoryComponent, equipmentComponent, entities, items, slots);
        DaggerfallItemInstances instances = new();
        InventoryStackId gold = InventoryStackId.Parse("ground.presentation.gold");
        inventory.Grant(new InventoryGrant(new InventoryItemId("gold-piece"), gold, 5));
        instances.RegisterDefaultStack(DaggerfallItemOwner.Player, inventory.Read().Stacks.Single(stack => stack.Id == gold),
            definitions.RequireItem(new DaggerfallItemId("gold-piece")));
        DaggerfallWorldProfileKey profile = new DaggerfallWorldProfileKey(new DaggerfallSiteId(1, 2), DaggerfallWorldProfileKind.Exterior, "charing-exterior").Validate();
        DaggerfallGroundContainers ground = new(new MechanicsInventoryContainerCoordinator(store, entities, items), instances, player,
            new DurableIdentityAllocator(DurableIdentityKind.Container, 100), profile);
        DaggerfallGroundContainer pile = ground.Drop(new(new InventoryItemId("gold-piece"), 4, gold), new WorldPoint(4, 0, 8), store.Revision);
        DaggerfallInventoryPresentation inventoryPresentation = new(new DaggerfallEquipmentMoves(inventory, equipment, definitions), definitions, new Dictionary<string, string>());
        DaggerfallLootPresentation loot = new(null!, inventoryPresentation, ground);

        Assert.True(loot.OpenGround(pile.Id));
        LootPresentation opened = Assert.IsType<LootPresentation>(loot.Read());
        InventoryItemPresentation row = Assert.Single(opened.Items);
        PendingGroundLoot pending = Assert.IsType<PendingGroundLoot>(loot.PrepareGroundTake(
            new DaggerfallPlayerUiAction("loot-take", opened.Revision, row.Key, Container: opened.Container, Amount: 2)));
        _ = ground.Take(pending.Id, pending.Selection, pending.ExpectedWorldRevision);
        loot.CompleteGround(true);

        Assert.Equal(3UL, inventory.Read().Stacks.Single(stack => stack.Id == gold).Quantity);
        Assert.Equal(2UL, Assert.Single(ground.Read(pile.Id)!.Stacks).Quantity);
        DaggerfallStackSave[] stacks = ground.Read(pile.Id)!.Stacks.Select(stack => new DaggerfallStackSave(stack.Id.Value, stack.Definition.Value, stack.Quantity,
            instances.RequireStack(DaggerfallItemOwner.Ground(pile.Id), stack.Id).Capture())).ToArray();
        DaggerfallGroundContainerSave saved = new(DaggerfallWorldProfileKeySave.Capture(profile), pile.Id, pile.Position.X, pile.Position.Y, pile.Position.Z,
            new DaggerfallInventorySave(stacks, [], []));
        saved.Validate();
        Assert.Equal(profile, saved.Profile.Require());
    }

    private static DaggerfallDefinitions ReadDefinitions()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
        return TestPayload.Definitions;
    }
}
