using WorldRpg.Kit.Targeting;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Loot and currency: corpse and container interaction, transfers at the modal boundary and corpse saves.</summary>
public sealed class LootSessionTests
{
    [Fact]
    public void Loot_ui_actions_open_without_transfer_then_take_one_at_the_admitted_boundary_and_close()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        AimActivationAt(session, 2000);
        CorpseContainer corpse = session.Corpses[2000];
        Assert.True(corpse.IsRegistered);
        session.State.Containers.Seed(corpse.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: InventoryStackId.Parse("test.loot.2807"))]);
        RegisterCorpseStack(session, definitions, 2000, "test.loot.2807");
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, 1d, PerceptionPairKind.Visible, 1d));
        ulong before = session.State.Inventory.Read().StoreRevision;
        void Ui(string json, ulong step)
        {
            ProductInputEvent action = Input(InputEventKind.DirectDigital) with
            {
                ValueKind = InputValueKind.ProductPayload,
                PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
                PayloadData = Encoding.UTF8.GetBytes(json),
            };
            session.Update(new ProductUpdate(OuterUpdate(step), [action]));
        }
        Ui("{\"action\":\"loot\"}", 2);
        LootPresentation opened = Assert.IsType<LootPresentation>(session.OpenLoot);
        Assert.Equal(before, session.State.Inventory.Read().StoreRevision);
        InventoryItemPresentation gold = opened.Items.Single(item => item.Key == DaggerfallInventoryPresentation.StackKey(InventoryStackId.Parse("test.loot.2807")));
        string take = System.Text.Json.JsonSerializer.Serialize(new { action = "loot-take", container = opened.Container, revision = opened.Revision, item = gold.Key });
        Ui(take, 3);
        Assert.Equal(ulong.Parse(gold.Quantity) - 1, ulong.Parse(session.OpenLoot!.Items.Single(item => item.Key == gold.Key).Quantity));
        ulong after = session.State.Inventory.Read().StoreRevision;
        Ui(take, 4);
        Assert.Equal(after, session.State.Inventory.Read().StoreRevision);
        Ui(System.Text.Json.JsonSerializer.Serialize(new { action = "loot-close", container = opened.Container }), 5);
        Assert.Null(session.OpenLoot);
    }

    [Fact]
    public void Corpse_loot_uses_engine_visibility_and_transfers_to_the_player_only_after_explicit_interaction()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.Concat(definitions.TemplateItems.Values).ToDictionary(
            item => new InventoryItemId(item.Id.Value),
            item => new ItemDefinition(ItemDefinitionId.Parse(item.Id.Value), item.IsFungible ? ItemKind.Fungible : ItemKind.Unique, item.MaximumQuantity));
        using ActorsState actors = ActorsWithNpc(2000, DefeatedMechanics(), new WorldPoint(0f, 0f, 1f));
        InventoryStore world = new();
        EntityId playerOwner = actors.Player.Actor.Entity;
        MechanicsInventoryContainerCoordinator containers = new(world, actors.Entities, items);
        containers.RegisterOwner(playerOwner);
        using SpatialMovementSystem movement = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        DaggerfallItemInstances itemInstances = new();
        DaggerfallCorpseLootModule loot = new(
            perception.Service, movement, containers, itemInstances, playerOwner, actors,
            new Dictionary<long, DaggerfallActorDefinition> { [2000] = definitions.RequireActor(new DaggerfallActorId("thief")) },
            definitions, RandomMinimum.Create(), new DaggerfallUniqueItemAllocator(1_000), new ProgressionState(), DaggerfallTuning.Defaults.LootInteraction,
            CharacterForLoot(definitions));
        // Corpse policy is independent of player XP credit.
        ActorDiedFact death = new(2000, 77, DaggerfallDamageCause.PhysicalAttack, 3, 3d, 2, 3);
        loot.Create(death);
        Assert.True(loot.Corpses[2000].IsInteractable);
        Assert.Empty(containers.Read(playerOwner).Stacks);
        var magicItem = Assert.Single(containers.Read(loot.Corpses[2000].Owner).UniqueItems,
            item => item.Definition.Value.Contains("-magic-", StringComparison.Ordinal));
        ulong magicIdentity = actors.Entities.IdentityOf(magicItem.Entity).Value;
        Assert.StartsWith("magic-item.", itemInstances.RequireUnique(magicIdentity).Enchantment);

        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Occluded, 0d));
        Assert.Null(loot.PrepareLoot(new PlayerControlState(new WorldPoint(0, 0, 0), 0, 0), ForwardLook()));
        Assert.True(loot.Corpses[2000].IsInteractable);

        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        world.RegisterEquipment(new EquipmentState(playerOwner));
        InventoryComponent inventory = actors.Player.Actor.Get<InventoryComponent>();
        EquipmentComponent equipmentComponent = new(world, playerOwner);
        actors.Player.Actor.Add(equipmentComponent);
        MechanicsEquipmentCoordinator equipment = new(inventory, equipmentComponent, actors.Entities, items,
            definitions.EquipmentSlots.Values.ToDictionary(slot => new WorldRpg.Kit.Inventory.EquipmentSlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot));
        DaggerfallEquipmentMoves inventoryMoves = new(new MechanicsInventoryCoordinator(inventory, actors.Entities, items), equipment, definitions);
        DaggerfallInventoryPresentation inventoryUi = new(inventoryMoves, definitions,
            new Dictionary<string, string>());
        DaggerfallLootPresentation panel = new(loot, inventoryUi);
        PlayerControlState player = new(new WorldPoint(0, 0, 0), 0, 0);
        // An ordinary generated corpse can hold mixed contents. Ensure the selected stack
        // has several units so this exercise distinguishes taking one from taking all.
        InventoryStackId testStack = InventoryStackId.Parse("test.loot.2882");
        containers.Seed(loot.Corpses[2000].Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: testStack)]);
        itemInstances.RegisterDefaultStack(DaggerfallItemOwner.Corpse(2000),
            containers.Read(loot.Corpses[2000].Owner).Stacks.Single(stack => stack.Id == testStack), definitions.Items[new DaggerfallItemId("gold-piece")]);
        ulong beforeOpen = world.Revision;
        Assert.Null(panel.Open(player, ForwardLook()));
        LootPresentation opened = Assert.IsType<LootPresentation>(panel.Read());
        Assert.Equal(beforeOpen, world.Revision);
        Assert.Empty(containers.Read(playerOwner).Stacks);
        InventoryItemPresentation gold = opened.Items.Single(item => item.Key == DaggerfallInventoryPresentation.StackKey(testStack));
        ulong goldBefore = ulong.Parse(gold.Quantity);
        DaggerfallPlayerUiAction take = new("loot-take", opened.Revision, gold.Key, Container: opened.Container);
        Assert.Null(panel.PrepareTake(take with { Container = "other" }, player, ForwardLook()));
        Assert.Equal(beforeOpen, world.Revision);
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Occluded, 0d));
        Assert.Null(panel.PrepareTake(take, player, ForwardLook()));
        Assert.Equal(beforeOpen, world.Revision);
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        PendingCorpseLoot pending = Assert.IsType<PendingCorpseLoot>(panel.PrepareTake(take, player, ForwardLook()));
        FactBuffer<IProductFact> facts = new();
        panel.Complete(loot.TryCommitLoot(pending, facts));
        Assert.Equal(1UL, containers.Read(playerOwner).Stacks.Single(stack => stack.Definition.Value == "gold-piece").Quantity);
        Assert.Equal(goldBefore - 1, containers.Read(loot.Corpses[2000].Owner).Stacks.Single(stack => stack.Id == testStack).Quantity);
        Assert.True(loot.Corpses[2000].IsInteractable);
        ulong afterTake = world.Revision;
        Assert.Null(panel.PrepareTake(take, player, ForwardLook()));
        Assert.Equal(afterTake, world.Revision);
        Assert.Contains("changed", panel.Message);
        while (panel.Read() is { Empty: false } current)
        {
            InventoryItemPresentation next = current.Items[0];
            PendingCorpseLoot one = Assert.IsType<PendingCorpseLoot>(panel.PrepareTake(
                new("loot-take", current.Revision, next.Key, Container: current.Container), player, ForwardLook()));
            panel.Complete(loot.TryCommitLoot(one, facts));
        }
        Assert.False(loot.Corpses[2000].IsInteractable);
        Assert.True(Assert.IsType<LootPresentation>(panel.Read()).Empty);
        Assert.Contains("until Exit", panel.Message);
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.All(delivered.OfType<LootAwardedFact>(), fact => Assert.Equal(1UL, fact.Quantity));
        Assert.Single(delivered.OfType<CorpseLootedFact>());
        Assert.Equal(2.25d, loot.LastEvidence?.Request.Observers.Span[0].MaximumDistance);
        Assert.Equal(.5d, loot.LastEvidence?.Request.Observers.Span[0].MinimumFacingCosine);
        panel.Close(opened.Container);
        Assert.Null(panel.Read());

    }

    [Fact]
    public void Empty_corpse_is_explicitly_searchable_once_without_an_engine_inventory_owner()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.Concat(definitions.TemplateItems.Values).ToDictionary(
            item => new InventoryItemId(item.Id.Value),
            item => new ItemDefinition(ItemDefinitionId.Parse(item.Id.Value), item.IsFungible ? ItemKind.Fungible : ItemKind.Unique, item.MaximumQuantity));
        using ActorsState actors = ActorsWithNpc(2000, DefeatedMechanics(), new WorldPoint(0f, 0f, 1f));
        InventoryStore world = new();
        EntityId playerOwner = actors.Player.Actor.Entity;
        MechanicsInventoryContainerCoordinator containers = new(world, actors.Entities, items);
        containers.RegisterOwner(playerOwner);
        using SpatialMovementSystem movement = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        DaggerfallCorpseLootModule loot = new(
            perception.Service, movement, containers, new DaggerfallItemInstances(), playerOwner, actors,
            new Dictionary<long, DaggerfallActorDefinition> { [2000] = definitions.RequireActor(new DaggerfallActorId("rat")) },
            definitions, RandomMinimum.Create(), new DaggerfallUniqueItemAllocator(1_000), new ProgressionState(), DaggerfallTuning.Defaults.LootInteraction,
            CharacterForLoot(definitions));
        loot.Create(new ActorDiedFact(2000, 77, DaggerfallDamageCause.PhysicalAttack, 3, 3d, 2, 3));
        Assert.True(loot.Corpses[2000].IsInteractable);
        Assert.False(loot.Corpses[2000].IsRegistered);
        Assert.Equal([playerOwner], world.InventoryOwners);

        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, .8d, PerceptionPairKind.Visible, 1d));
        PendingCorpseLoot pending = Assert.IsType<PendingCorpseLoot>(loot.PrepareLoot(new PlayerControlState(new WorldPoint(0, 0, 0), 0, 0), ForwardLook()));
        Assert.True(pending.IsEmpty);
        FactBuffer<IProductFact> facts = new();
        Assert.Equal(CorpseLootCommitResult.Committed, loot.TryCommitLoot(pending, facts));
        Assert.False(loot.Corpses[2000].IsInteractable);
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.Single(delivered.OfType<CorpseSearchedEmptyFact>());
        Assert.Null(loot.PrepareLoot(new PlayerControlState(new WorldPoint(0, 0, 0), 0, 0), ForwardLook()));
    }

    [Theory]
    [InlineData("encounter-monk", true)]
    [InlineData("encounter-city-watch-the-haltmeister", false)]
    public void Enemy_corpse_rolls_its_mobile_map_chance_and_only_a_keyed_enemy_rolls_potion_and_recipe(string actorId, bool keyed)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.Concat(definitions.TemplateItems.Values).ToDictionary(
            item => new InventoryItemId(item.Id.Value),
            item => new ItemDefinition(ItemDefinitionId.Parse(item.Id.Value), item.IsFungible ? ItemKind.Fungible : ItemKind.Unique, item.MaximumQuantity));
        using ActorsState actors = ActorsWithNpc(2000, DefeatedMechanics(), new WorldPoint(0f, 0f, 1f));
        InventoryStore world = new();
        MechanicsInventoryContainerCoordinator containers = new(world, actors.Entities, items);
        containers.RegisterOwner(actors.Player.Actor.Entity);
        using SpatialMovementSystem movement = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        DaggerfallActorDefinition actor = definitions.RequireActor(new DaggerfallActorId(actorId));
        KeyedRandomFake random = KeyedRandomFake.Create(0);
        DaggerfallItemInstances itemInstances = new();
        DaggerfallCorpseLootModule loot = new(
            PerceptionFake.Create().Service, movement, containers, itemInstances, actors.Player.Actor.Entity, actors,
            new Dictionary<long, DaggerfallActorDefinition> { [2000] = actor },
            definitions, random.Service, new DaggerfallUniqueItemAllocator(1_000), new ProgressionState(), DaggerfallTuning.Defaults.LootInteraction,
            CharacterForLoot(definitions));

        loot.Create(new ActorDiedFact(2000, 77, DaggerfallDamageCause.PhysicalAttack, 3, 3d, 2, 3));

        // The Monk (key T, MapChance 1) and the keyless City Watch (MapChance 0) both roll the map.
        Assert.Equal(keyed, actor.LootTableKey is not null);
        Assert.Equal(keyed ? 1 : 0, definitions.Mobiles.Mobiles[actor.MobileId!.Value].MapChance);
        string[] extras = random.Requests.Select(request => request.Key[(request.Key.LastIndexOf(':') + 1)..])
            .Where(roll => roll.StartsWith("corpse.loot.enemy.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(keyed
            ? ["corpse.loot.enemy.map", "corpse.loot.enemy.potion", "corpse.loot.enemy.potion.recipe", "corpse.loot.enemy.potion-recipe", "corpse.loot.enemy.potion-recipe.recipe"]
            : ["corpse.loot.enemy.map"], extras);
        if (!keyed)
        {
            Assert.False(loot.Corpses[2000].IsRegistered);
            return;
        }
        InventoryView contents = Assert.IsType<InventoryView>(loot.ReadContents(2000));
        Assert.Contains(contents.UniqueItems, item => item.Definition.Value == "template-287");
        Assert.Contains(contents.UniqueItems, item => item.Definition.Value == "template-278");
        InventoryStack potion = Assert.Single(contents.Stacks, stack => stack.Definition.Value == "template-83");
        Assert.Equal(221871, itemInstances.RequireStack(DaggerfallItemOwner.Corpse(2000), potion.Id).PotionRecipeKey);
    }

    private static StatsComponent DefeatedMechanics()
    {
        Stat maximum = new(100, quantum: 1, rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse("health-maximum"), maximum);
        stats.AddTrack(TrackId.Parse("health"), new Track(
            maximum,
            0,
            quantum: 1,
            rounding: MidpointRounding.ToZero,
            integerRounding: MidpointRounding.ToZero));
        return stats;
    }

    private static DaggerfallCharacterState CharacterForLoot(DaggerfallDefinitions definitions)
    {
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer(player.Career!);
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, career));
        return new DaggerfallCharacterState(definitions, stats, player);
    }
    [Fact]
    public void Host_adopts_the_loot_close_and_resumes_play_through_the_real_session()
    {
        // The defect this pins: closing loot asked the session for Playing, but the product
        // adopted the request without closesModal and refused Modal -> Playing, so gameplay
        // stayed held. Driving the session alone cannot show it; only the real handshake can.
        string root = TestData.RepositoryRoot;
        List<string> releases = [];
        ContentFake content = new(releases);
        DaggerfallSiteProfile inputs = ReadInputs(root);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service,
            random: QuestPlaceRandomMinimum.Create());
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new();

        static ProductInputEvent UiAction(string json) => Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes(json),
        };

        using (WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId("daggerfall.privateers-hold")))
        {
            product.Start();
            NewGameSessionTests.Commit(ruleset.RequireSession());
            product.Begin();
            DaggerfallSession session = ruleset.RequireSession();

            // One lootable corpse within reach, mirroring the session-level loot fixture.
            session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
            session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
            session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
            AimActivationAt(session, 2000);
            CorpseContainer corpse = session.Corpses[2000];
            session.State.Containers.Seed(corpse.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: InventoryStackId.Parse("test.loot.4691"))]);
            session.State.ItemInstances.RegisterStack(DaggerfallItemOwner.Corpse(2000), InventoryStackId.Parse("test.loot.4691"),
                new DaggerfallItemInstanceMetadata("gold-piece", "none", 0, 1, 1, true, false, null, null, null, DaggerfallItemOwner.Corpse(2000)).Validate());
            perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));

            // Opening loot through the product puts the product into the modal the session asked for.
            product.Update(new ProductUpdate(OuterUpdate(1), [UiAction("{\"action\":\"loot\"}")]));
            Assert.Equal(ProductMode.Modal, product.Mode);
            LootPresentation opened = Assert.IsType<LootPresentation>(session.OpenLoot);

            // Closing through the interaction's own token returns the product to ordinary play.
            // The update adopts twice — before and after the session runs — so the trailing entry
            // is the post-adopt no-op; what matters is that this update caused Modal->Playing.
            int stepsBeforeClose = spatial.StepCalls;
            int historyBeforeClose = product.ModeHistory.Count;
            product.Update(new ProductUpdate(OuterUpdate(2), [UiAction(JsonSerializer.Serialize(new { action = "loot-close", container = opened.Container }))]));
            Assert.Equal(ProductMode.Playing, product.Mode);
            ProductModeChange close = product.ModeHistory.Skip(historyBeforeClose).First(change => change.Changed);
            Assert.Equal(ProductMode.Modal, close.From);
            Assert.Equal(ProductMode.Playing, close.To);
            Assert.Null(session.OpenLoot);

            // World advancement and input resume: a held key steps the world with intent again.
            product.Update(new ProductUpdate(OuterUpdate(3), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
            Assert.True(spatial.StepCalls > stepsBeforeClose, "ordinary play admits world time again");
            Assert.NotEqual(Vector2.Zero, spatial.StepRequests[^1].Command.PlanarIntent);
            product.Shutdown();
        }
    }

    [Fact]
    public void Loot_takes_apply_deliver_and_publish_inside_the_modal_update()
    {
        // The deferred defect this pins: a take prepared its transfer and facts, but the facts
        // waited for a playing step while the published presentation still showed the item.
        // A take must now move the item, deliver its facts, and republish within the same modal
        // update, with the panel still open and the world still held.
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        AimActivationAt(session, 2000);
        CorpseContainer corpse = session.Corpses[2000];
        Assert.True(corpse.IsRegistered);
        session.State.Containers.Seed(corpse.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: InventoryStackId.Parse("test.loot.4740"))]);
        RegisterCorpseStack(session, definitions, 2000, "test.loot.4740");
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));

        static ulong PlayerGold(DaggerfallSession session) => session.State.Inventory.Read().Stacks
            .Where(stack => stack.Definition.Value == "gold-piece").Select(stack => stack.Quantity).Aggregate(0UL, (a, b) => a + b);
        static ulong CorpseGold(LootPresentation loot) => loot.Items.Where(item => item.Definition == "gold-piece")
            .Select(item => ulong.Parse(item.Quantity, CultureInfo.InvariantCulture)).Aggregate(0UL, (a, b) => a + b);
        void Ui(string json, ulong step)
        {
            ProductInputEvent action = Input(InputEventKind.DirectDigital) with
            {
                ValueKind = InputValueKind.ProductPayload,
                PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
                PayloadData = Encoding.UTF8.GetBytes(json),
            };
            session.Update(new ProductUpdate(OuterUpdate(step), [action]));
        }

        Ui("{\"action\":\"loot\"}", 2);
        Assert.Equal(ProductMode.Modal, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Modal);
        LootPresentation opened = Assert.IsType<LootPresentation>(session.OpenLoot);
        ulong playerBefore = PlayerGold(session);
        ulong corpseBefore = CorpseGold(opened);
        Assert.True(corpseBefore > 0, "the corpse should hold loot to take");
        int stepsBefore = spatial.StepCalls;

        // One take moves one unit, delivers its facts, and republishes: the panel stays open,
        // the world takes no step, and the outcome line carries the delivered fact.
        string take = JsonSerializer.Serialize(new
        {
            action = "loot-take",
            container = opened.Container,
            revision = opened.Revision,
            item = DaggerfallInventoryPresentation.StackKey(InventoryStackId.Parse("test.loot.4740")),
        });
        Ui(take, 3);
        Assert.Equal(playerBefore + 1, PlayerGold(session));
        LootPresentation afterTake = Assert.IsType<LootPresentation>(session.OpenLoot);
        Assert.Equal(opened.Container, afterTake.Container);
        Assert.Equal(corpseBefore - 1, CorpseGold(afterTake));
        Assert.Equal(stepsBefore, spatial.StepCalls);
        Assert.Contains("looted", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);

        // Replaying the same take is refused as stale: no duplicate transfer, no world step.
        Ui(take, 4);
        Assert.Equal(playerBefore + 1, PlayerGold(session));
        Assert.Equal(corpseBefore - 1, CorpseGold(Assert.IsType<LootPresentation>(session.OpenLoot)));
        Assert.Equal(stepsBefore, spatial.StepCalls);
        Assert.Contains("Loot changed", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);

        // Draining the container through the panel ends in the empty state, still modal.
        for (ulong step = 5; step < 30; step++)
        {
            LootPresentation current = Assert.IsType<LootPresentation>(session.OpenLoot);
            if (current.Empty) break;
            InventoryItemPresentation first = current.Items[0];
            Ui(JsonSerializer.Serialize(new { action = "loot-take", container = current.Container, revision = current.Revision, item = first.Key }), step);
            if (step == 29) Assert.Fail("draining the corpse did not reach the empty state");
        }
        LootPresentation drained = Assert.IsType<LootPresentation>(session.OpenLoot);
        Assert.True(drained.Empty);
        Assert.Equal(ProductMode.Modal, session.Mode);
        Assert.Equal(stepsBefore, spatial.StepCalls);
    }

    [Fact]
    public void Loot_transfer_at_the_player_capacity_boundary_leaves_both_engine_containers_unchanged()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        AimActivationAt(session, 2000);
        CorpseContainer corpse = session.Corpses[2000];
        InventoryStackId corpseCoins = InventoryStackId.Parse("test.encumbrance.corpse");
        session.State.Containers.Seed(corpse.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 1, Stack: corpseCoins)]);
        RegisterCorpseStack(session, definitions, 2000, corpseCoins.Value);
        InventoryStackId playerCoins = InventoryStackId.Parse("test.encumbrance.player");
        session.State.Inventory.Grant(new(new InventoryItemId("gold-piece"), playerCoins, checked((ulong)session.State.Encumbrance.Read().MaximumClassicUnits)));
        session.State.ItemInstances.RegisterDefaultStack(DaggerfallItemOwner.Player, session.State.Inventory.Read().Stacks.Single(stack => stack.Id == playerCoins), definitions.RequireItem(new DaggerfallItemId("gold-piece")));
        Assert.False(session.State.Encumbrance.Read().CanMove);
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));

        void Ui(string json, ulong step) => session.Update(new ProductUpdate(OuterUpdate(step), [Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes(json),
        }]));

        Ui("{\"action\":\"loot\"}", 1);
        session.ApplyProductMode(ProductMode.Modal);
        LootPresentation opened = Assert.IsType<LootPresentation>(session.OpenLoot);
        InventoryItemPresentation coin = Assert.Single(opened.Items, item => item.Key == DaggerfallInventoryPresentation.StackKey(corpseCoins));
        Ui(JsonSerializer.Serialize(new { action = "loot-take", container = opened.Container, revision = opened.Revision, item = coin.Key }), 2);

        Assert.Equal(checked((ulong)session.State.Encumbrance.Read().MaximumClassicUnits), session.State.Inventory.Read().Stacks.Single(stack => stack.Id == playerCoins).Quantity);
        Assert.Equal(1UL, session.State.Containers.Read(corpse.Owner).Stacks.Single(stack => stack.Id == corpseCoins).Quantity);
        Assert.Contains("Cannot take", Assert.IsType<LootPresentation>(session.OpenLoot).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Currency_actions_mutate_inventory_backed_values_and_restore_their_letter_identity()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        int? bankRegion = null;
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        RulesetSavePayload saved;
        ulong letterIdentity;
        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
        {
            bankRegion = session.Site.Region;
            Assert.NotNull(bankRegion);
            DaggerfallNpcSite bankerSite = new(bankRegion.Value, session.Site.ActiveSite!.Name, string.Empty);
            long bankerId = session.State.Npcs.RegisterStable(DaggerfallNpcKind.Static, "test.session.banker",
                bankerSite, new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, 0), "banker", ["banking"]);
            Assert.True(session.TryOpenBank(new DaggerfallServiceProvider(bankerId, bankerSite, "banking")));
            InventoryStackId coins = InventoryStackId.Parse("test.currency.session.coins");
            session.State.Inventory.Grant(new(new InventoryItemId("template-276"), coins, 200));
            session.State.ItemInstances.RegisterDefaultStack(DaggerfallItemOwner.Player, session.State.Inventory.Read().Stacks.Single(stack => stack.Id == coins), definitions.RequireItem(new DaggerfallItemId("template-276")));

            void Ui(string json, ulong step) => session.Update(new ProductUpdate(OuterUpdate(step), [Input(InputEventKind.DirectDigital) with
            {
                ValueKind = InputValueKind.ProductPayload,
                PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
                PayloadData = Encoding.UTF8.GetBytes(json),
            }]));

            Ui("{\"action\":\"currency-deposit-gold\",\"amount\":200}", 1);
            Assert.Equal(new DaggerfallCurrencyTotals(25, 0, 200), session.State.Currency.Read());
            Assert.Equal(200UL, session.State.Bank.BalanceForRegion(bankRegion.Value));
            Assert.Contains("Deposited 200 gold into region", Assert.IsType<string>(engine.PublishedField("lastOutcome")));
            Ui("{\"action\":\"currency-withdraw-letter\",\"amount\":100}", 2);
            Assert.Equal(new DaggerfallCurrencyTotals(25, 100, 99), session.State.Currency.Read());
            Assert.Equal(99UL, session.State.Bank.BalanceForRegion(bankRegion.Value));
            Rusty.Engine.Mechanics.UniqueInventoryItem letter = Assert.Single(session.State.Inventory.Read().UniqueItems,
                item => item.Definition.Value == "template-275");
            letterIdentity = session.State.Inventory.GetDurableItemId(letter.Entity).Value;
            Assert.Equal(100UL, session.State.ItemInstances.RequireUnique(letterIdentity).CreditValue);
            saved = session.CaptureSave();
        }

        DaggerfallSavePayload savedPayload = DaggerfallSavePayload.Read(saved);
        Assert.Equal(99UL, savedPayload.Bank.Accounts.Single(account => account.Region == bankRegion).Gold);

        JsonObject missingCurrency = JsonNode.Parse(saved.Bytes.Span)!.AsObject();
        Assert.True(missingCurrency.Remove("Currency"));
        RulesetSavePayload malformed = new(saved.Ruleset, JsonSerializer.SerializeToUtf8Bytes(missingCurrency));
        Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Read(malformed));

        JsonObject missingBank = JsonNode.Parse(saved.Bytes.Span)!.AsObject();
        Assert.True(missingBank.Remove("Bank"));
        RulesetSavePayload missingBankPayload = new(saved.Ruleset, JsonSerializer.SerializeToUtf8Bytes(missingBank));
        Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Read(missingBankPayload));

        JsonObject mismatchedBank = JsonNode.Parse(saved.Bytes.Span)!.AsObject();
        JsonObject firstAccount = mismatchedBank["Bank"]!["Accounts"]![0]!.AsObject();
        firstAccount["Gold"] = checked(firstAccount["Gold"]!.GetValue<ulong>() + 1UL);
        RulesetSavePayload mismatchedBankPayload = new(saved.Ruleset, JsonSerializer.SerializeToUtf8Bytes(mismatchedBank));
        ArgumentException bankError = Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Read(mismatchedBankPayload));
        Assert.Contains("must sum to the currency settlement account", bankError.Message, StringComparison.Ordinal);

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), saved);

        Assert.Equal(new DaggerfallCurrencyTotals(25, 100, 99), resumed.State.Currency.Read());
        Assert.Equal(99UL, resumed.State.Bank.BalanceForRegion(bankRegion!.Value));
        Rusty.Engine.Mechanics.UniqueInventoryItem restoredLetter = Assert.Single(resumed.State.Inventory.Read().UniqueItems,
            item => item.Definition.Value == "template-275");
        Assert.Equal(letterIdentity, resumed.State.Inventory.GetDurableItemId(restoredLetter.Entity).Value);
        Assert.Equal(100UL, resumed.State.ItemInstances.RequireUnique(letterIdentity).CreditValue);
    }

    [Fact]
    public void Corpse_save_roundtrip_preserves_populated_and_empty_contents_identity_and_allocation()
    {
        // The shared contents mapping must carry a populated corpse (stacks + a unique), a looted
        // registered-empty corpse, and an empty unregistered corpse, with durable identity
        // agreement into the next allocation.
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        sourceSpatial.KeepPosition = true;
        PerceptionFake sourcePerception = PerceptionFake.Create();
        EngineContextFake source = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases), sourcePerception.Service);
        RulesetSavePayload populatedPayload;
        RulesetSavePayload lootedPayload;
        static void Ui(DaggerfallSession session, string json, ulong step)
        {
            ProductInputEvent action = Input(InputEventKind.DirectDigital) with
            {
                ValueKind = InputValueKind.ProductPayload,
                PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
                PayloadData = Encoding.UTF8.GetBytes(json),
            };
            session.Update(new ProductUpdate(OuterUpdate(step), [action]));
        }
        using (DaggerfallSession original = DaggerfallSession.StartNew(source.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
        {
            static void Kill(DaggerfallSession session, long target)
            {
                session.State.Actors.Get(target).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
                session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
                session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, target, 1, 1, .125));
            }
            Kill(original, 2000);
            AimActivationAt(original, 2000);
            CorpseContainer thief = original.Corpses[2000];
            DaggerfallItemFactory itemFactory = new(definitions, RandomMinimum.Create());
            DaggerfallCreatedItem potion = itemFactory.Create(new DaggerfallItemCreateRequest(
                "UselessItems1", "test.loot.4903.potion", DaggerfallItemOwner.Corpse(2000), TemplateIndex: 83, PotionRecipeKey: 221871));
            DaggerfallCreatedItem recipe = itemFactory.Create(new DaggerfallItemCreateRequest(
                "MiscItems", "test.loot.4904.recipe", DaggerfallItemOwner.Corpse(2000), TemplateIndex: 278, PotionRecipeKey: 221871));
            original.State.Containers.Seed(thief.Owner, [
                new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: InventoryStackId.Parse("test.loot.4902")),
                new InventoryContainerSeed(potion.Item, potion.Quantity, Stack: InventoryStackId.Parse("test.loot.4903")),
                new InventoryContainerSeed(recipe.Item, UniqueItem: new(DurableIdentityKind.Item, 5002)),
                new InventoryContainerSeed(new InventoryItemId("iron-dagger"), 1, new(DurableIdentityKind.Item, 5001))]);
            RegisterCorpseStack(original, definitions, 2000, "test.loot.4902");
            original.State.ItemInstances.RegisterStack(DaggerfallItemOwner.Corpse(2000), InventoryStackId.Parse("test.loot.4903"), potion.Metadata);
            original.State.ItemInstances.RegisterUnique(5002, recipe.Metadata);
            original.State.ItemInstances.RegisterDefaultUnique(5001, definitions.Items[new DaggerfallItemId("iron-dagger")], DaggerfallItemOwner.Corpse(2000));
            // The giant-bat carries no loot table, so its corpse stays unregistered and empty.
            // It is tougher than the thief: repeat the explicit swing until the death lands.
            original.State.Actors.Get(2006).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
            ulong swing = 2;
            while (!original.State.Actors.Get(2006).IsDefeated && swing < 20)
            {
                original.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2006, swing, swing, .125));
                original.Update(new ProductUpdate(OuterUpdate(swing), []));
                swing++;
            }
            Assert.True(original.State.Actors.Get(2006).IsDefeated, "the giant-bat should die within bounded swings");
            Assert.False(original.Corpses[2006].IsRegistered);
            populatedPayload = original.CaptureSave();

            // Drain the thief through the panel: the looted save must carry a registered corpse
            // with empty contents, not an unregistered one.
            sourcePerception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
            Ui(original, "{\"action\":\"loot\"}", swing++);
            var activation = Assert.IsType<InteractionTargetingEvidence>(original.LastActivationTargeting);
            Assert.Equal(Rusty.Engine.Interaction.InteractionReason.Ready, activation.Focus.Reason);
            Assert.Equal(ProductMode.Modal, original.PendingModeRequest);
            original.ApplyProductMode(ProductMode.Modal);
            for (ulong step = swing; step < swing + 30; step++)
            {
                LootPresentation current = Assert.IsType<LootPresentation>(original.OpenLoot);
                if (current.Empty) break;
                InventoryItemPresentation first = current.Items[0];
                Ui(original, JsonSerializer.Serialize(new { action = "loot-take", container = current.Container, revision = current.Revision, item = first.Key }), step);
                if (step == swing + 29) Assert.Fail("draining the corpse did not reach the empty state");
            }
            LootPresentation drained = Assert.IsType<LootPresentation>(original.OpenLoot);
            Assert.True(drained.Empty);
            Assert.False(original.Corpses[2000].IsInteractable);
            lootedPayload = original.CaptureSave();
        }

        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(populatedPayload);
        DaggerfallCorpseSave savedThief = captured.Corpses.Single(corpse => corpse.ActorId == 2000);
        Assert.True(savedThief.IsRegistered);
        // The thief's loot table generates gold on top of the seeded stack; the save carries both.
        Assert.True(savedThief.Stacks.Where(stack => stack.ItemId == "gold-piece")
            .Aggregate(0UL, (total, stack) => total + stack.Quantity) >= 5UL);
        Assert.Equal(5001UL, Assert.Single(savedThief.UniqueItems, item => item.ItemId == "iron-dagger").EntityId);
        Assert.Equal(221871, savedThief.Stacks.Single(stack => stack.StackId == "test.loot.4903").Metadata.PotionRecipeKey);
        Assert.Equal(221871, savedThief.UniqueItems.Single(item => item.EntityId == 5002).Metadata.PotionRecipeKey);
        // The thief's own death rolled its enemy chances (key T, MapChance 2) on minimum random:
        // a map, a stacked potion and a recipe sheet, each carried by the save with its recipe.
        DaggerfallUniqueSave generatedMap = Assert.Single(savedThief.UniqueItems, item => item.ItemId == "template-287");
        DaggerfallStackSave generatedPotion = Assert.Single(savedThief.Stacks, stack => stack.ItemId == "template-83" && stack.StackId != "test.loot.4903");
        DaggerfallUniqueSave generatedRecipe = Assert.Single(savedThief.UniqueItems, item => item.ItemId == "template-278" && item.EntityId != 5002);
        Assert.Null(generatedMap.Metadata.PotionRecipeKey);
        Assert.Equal(221871, generatedPotion.Metadata.PotionRecipeKey);
        Assert.Equal(221871, generatedRecipe.Metadata.PotionRecipeKey);
        // The live corpse path now uses retained template definitions, and its material and
        // appearance facts must survive the normal save boundary rather than becoming defaults.
        DaggerfallUniqueSave savedTemplateWeapon = savedThief.UniqueItems.First(item => definitions.RequireItem(new DaggerfallItemId(item.ItemId)).Weapon is not null);
        DaggerfallCorpseSave savedBat = captured.Corpses.Single(corpse => corpse.ActorId == 2006);
        Assert.False(savedBat.IsRegistered);
        Assert.Empty(savedBat.Stacks);
        Assert.Empty(savedBat.UniqueItems);

        // The looted save keeps the thief registered with empty contents and no interaction.
        DaggerfallSavePayload looted = DaggerfallSavePayload.Read(lootedPayload);
        DaggerfallCorpseSave lootedThief = looted.Corpses.Single(corpse => corpse.ActorId == 2000);
        Assert.True(lootedThief.IsRegistered);
        Assert.Empty(lootedThief.Stacks);
        Assert.Empty(lootedThief.UniqueItems);
        Assert.False(lootedThief.IsInteractable);

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using (DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), populatedPayload))
        {
            InventoryView restoredThief = resumed.State.Containers.Read(resumed.Corpses[2000].Owner);
            Assert.Equal(
                savedThief.Stacks.Where(stack => stack.ItemId == "gold-piece")
                    .OrderBy(stack => stack.StackId, StringComparer.Ordinal)
                    .Select(stack => (stack.StackId, stack.Quantity)),
                restoredThief.Stacks.Where(stack => stack.Definition.Value == "gold-piece")
                    .OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
                    .Select(stack => (stack.Id.Value, stack.Quantity)));
            var restoredDagger = Assert.Single(restoredThief.UniqueItems, item => item.Definition.Value == "iron-dagger");
            Assert.Equal(5001UL, resumed.State.Actors.Entities.IdentityOf(restoredDagger.Entity).Value);
            var restoredTemplateWeapon = restoredThief.UniqueItems.First(item => definitions.RequireItem(new DaggerfallItemId(item.Definition.Value)).Weapon is not null);
            ulong restoredTemplateIdentity = resumed.State.Actors.Entities.IdentityOf(restoredTemplateWeapon.Entity).Value;
            Assert.Equal(savedTemplateWeapon.Metadata.Material, resumed.State.ItemInstances.RequireUnique(restoredTemplateIdentity).Material);
            Assert.Equal(221871, resumed.State.ItemInstances.RequireStack(DaggerfallItemOwner.Corpse(2000), InventoryStackId.Parse("test.loot.4903")).PotionRecipeKey);
            Assert.Equal(221871, resumed.State.ItemInstances.RequireUnique(5002).PotionRecipeKey);
            Assert.Contains(restoredThief.UniqueItems, item => item.Definition.Value == "template-287"
                && resumed.State.Actors.Entities.IdentityOf(item.Entity).Value == generatedMap.EntityId);
            Assert.Equal((generatedPotion.Quantity, 221871), (
                restoredThief.Stacks.Single(stack => stack.Id.Value == generatedPotion.StackId).Quantity,
                resumed.State.ItemInstances.RequireStack(DaggerfallItemOwner.Corpse(2000), InventoryStackId.Parse(generatedPotion.StackId)).PotionRecipeKey));
            Assert.Equal(221871, resumed.State.ItemInstances.RequireUnique(generatedRecipe.EntityId).PotionRecipeKey);
            Assert.False(resumed.Corpses[2006].IsRegistered);

            // The next generated unique must not reuse a restored live identity.
            ulong nextUnique = resumed.UniqueItemAllocator.AllocateReference().Value;
            Assert.NotEqual(5001UL, nextUnique);
            Assert.NotEqual(5002UL, nextUnique);
        }

        ContentFake lootedContent = new(releases);
        PopulateContent(lootedContent, inputs);
        SpatialFake lootedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake lootedEngine = EngineContextFake.Create(lootedContent, lootedSpatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        using DaggerfallSession lootedSession = DaggerfallSession.Restore(lootedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), lootedPayload);

        // The looted corpse restores registered and empty rather than unregistered or reseeded.
        Assert.True(lootedSession.Corpses[2000].IsRegistered);
        Assert.False(lootedSession.Corpses[2000].IsInteractable);
        InventoryView restoredLooted = lootedSession.State.Containers.Read(lootedSession.Corpses[2000].Owner);
        Assert.Empty(restoredLooted.Stacks);
        Assert.Empty(restoredLooted.UniqueItems);
        Assert.False(lootedSession.Corpses[2006].IsRegistered);

        // The next generated unique still allocates cleanly after the looted restore.
        _ = lootedSession.UniqueItemAllocator.AllocateReference();
    }
}
