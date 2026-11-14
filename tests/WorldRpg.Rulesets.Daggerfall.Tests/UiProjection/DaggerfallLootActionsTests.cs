using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.World;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallLootActions"/> built without the session over a player's inventory and a ground pile: the
/// quests' item hearing and the property containers' stock and theft are plain inputs, recorded here.
/// </summary>
public sealed class DaggerfallLootActionsTests : IDisposable
{
    private static readonly InventoryStackId Gold = InventoryStackId.Parse("loot.actions.gold");
    private readonly EntityDirectory _entities = new();
    private readonly PresentationState _presentation = new("Ready", 5d);
    private readonly List<(string Definition, ulong Quantity)> _observed = [];
    private readonly MechanicsInventoryCoordinator _inventory;
    private readonly DaggerfallGroundContainers _ground;
    private readonly DaggerfallLootPresentation _loot;
    private readonly DaggerfallLootActions _actions;
    private readonly long _pile;
    private bool? _stock = false;

    public DaggerfallLootActionsTests()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        InventoryStore store = new();
        EntityId player = _entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("player"));
        store.RegisterInventory(new InventoryState(player));
        store.RegisterEquipment(new EquipmentState(player));
        InventoryComponent inventoryComponent = new(store, player);
        EquipmentComponent equipmentComponent = new(store, player);
        _entities.Store.Add(player, inventoryComponent);
        _entities.Store.Add(player, equipmentComponent);
        Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.Concat(definitions.TemplateItems.Values)
            .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
        Dictionary<KitEquipmentSlotId, EquipmentSlotDefinition> slots = definitions.EquipmentSlots.Values
            .ToDictionary(slot => new KitEquipmentSlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
        _inventory = new(inventoryComponent, _entities, items);
        MechanicsEquipmentCoordinator equipment = new(inventoryComponent, equipmentComponent, _entities, items, slots);
        DaggerfallItemInstances instances = new();
        _inventory.Grant(new InventoryGrant(new InventoryItemId("gold-piece"), Gold, 5));
        instances.RegisterDefaultStack(DaggerfallItemOwner.Player, _inventory.Read().Stacks.Single(stack => stack.Id == Gold),
            definitions.RequireItem(new DaggerfallItemId("gold-piece")));
        DaggerfallWorldProfileKey profile = new DaggerfallWorldProfileKey(new DaggerfallSiteId(1, 2), DaggerfallWorldProfileKind.Exterior, "loot-exterior").Validate();
        _ground = new(new MechanicsInventoryContainerCoordinator(store, _entities, items), instances, player,
            new DurableIdentityAllocator(DurableIdentityKind.Container, 100), profile);
        _pile = _ground.Drop(new(new InventoryItemId("gold-piece"), 4, Gold), new WorldPoint(4, 0, 8), store.Revision).Id;
        DaggerfallInventoryPresentation inventoryUi = new(new DaggerfallEquipmentMoves(_inventory, equipment, definitions), definitions, new Dictionary<string, string>());
        _loot = new(null!, inventoryUi, _ground);
        DaggerfallActorDefinition playerDefinition = definitions.RequireActor(new DaggerfallActorId("player"));
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(playerDefinition,
            DaggerfallPlayerVitals.Initial(playerDefinition.Stats, definitions.Catalogs.RequireCareer("class00")));
        DaggerfallTuning tuning = DaggerfallTuning.Defaults;
        _actions = new(_loot, null!, _ground, new FactBuffer<IProductFact>(), new DaggerfallEncumbrancePolicy(_inventory, stats), definitions,
            instances, _inventory, new PlayerControlState(null, 0f, 0f),
            new PlayerInputSystem(tuning.PlayerControl, DaggerfallInput.Controls, DaggerfallInput.Bindings, tuning.ControllerInput),
            _ => true, _ => _stock, (_, _, definition, quantity) => _observed.Add((definition, quantity)), _presentation);
    }

    [Fact]
    public void A_pile_opens_once_stocked_and_a_take_moves_its_row_and_tells_the_property_owner()
    {
        _stock = null;
        Assert.Equal(new DaggerfallActivationOutcome(false, "That container is no longer available."), _actions.OpenGround(_pile));
        _stock = false;
        Assert.True(_actions.OpenGround(_pile).Applied);

        LootPresentation shown = Assert.IsType<LootPresentation>(_loot.Read());
        InventoryItemPresentation row = Assert.Single(shown.Items);
        _actions.Take(new DaggerfallPlayerUiAction("loot-take", shown.Revision, row.Key, Container: shown.Container, Amount: 2));
        Assert.Equal(3UL, _inventory.Read().Stacks.Single(stack => stack.Id == Gold).Quantity);
        Assert.Equal([("gold-piece", 2UL)], _observed);
        Assert.Equal(_loot.Message, _presentation.LastOutcome);
    }

    [Fact]
    public void Take_all_names_the_window_it_was_shown_and_empties_it_once()
    {
        _actions.TakeAll(new DaggerfallPlayerUiAction("loot-take-all", "0", Container: "none"));
        Assert.Equal("Loot changed. Choose the item again.", _presentation.LastOutcome);

        _actions.OpenGround(_pile);
        LootPresentation shown = Assert.IsType<LootPresentation>(_loot.Read());
        _actions.TakeAll(new DaggerfallPlayerUiAction("loot-take-all", shown.Revision, Container: shown.Container));
        Assert.Equal(5UL, _inventory.Read().Stacks.Single(stack => stack.Id == Gold).Quantity);
        Assert.Equal(_loot.Message, _presentation.LastOutcome);
    }

    [Fact]
    public void A_quest_reward_opens_its_pile_or_asks_the_player_to_come_back()
    {
        Assert.True(_actions.PresentQuestReward(null));
        Assert.Equal("Quest completed.", _presentation.LastOutcome);
        Assert.False(_actions.PresentQuestReward(_pile + 1000));
        Assert.Equal("Return to the reward location to collect your quest reward.", _presentation.LastOutcome);
        Assert.True(_actions.PresentQuestReward(_pile));
        Assert.NotNull(_loot.Read());
    }

    public void Dispose() => _entities.Dispose();
}
