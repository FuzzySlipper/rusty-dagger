using System.Text;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Save slots driven through the Host product: close/reopen, slot actions and unique-item ledger checks.</summary>
public sealed class HostSaveSlotTests
{
    [Fact]
    public void The_title_screen_loads_a_named_save_without_beginning_character_creation()
    {
        string root = TestData.RepositoryRoot;
        var inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        var spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        InMemoryPersistenceService persistence = new();
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), persistence: persistence);
        using WorldRpgSaveSlots slots = new(engine.Context, "worldrpg.saves");
        using (var creation = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults)))
        {
            NewGameSessionTests.Commit(creation, "class13");
            using var game = Assert.IsType<DaggerfallSession>(creation.CreateNewGame());
            game.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(7);
            slots.SaveSlot("slot-archer", "Archer", new GameSaveEnvelope(game.CaptureSave()));
        }

        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new();
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset,
            new GameBundleId("daggerfall.classic"));
        product.Start();
        var title = ruleset.RequireSession();
        Assert.Equal(ProductMode.Title, product.Mode);

        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"load-slot\",\"key\":\"missing\"}")]));
        Assert.Same(title, ruleset.RequireSession());
        Assert.Equal(ProductMode.Title, product.Mode);
        Assert.Contains("No save is indexed under 'missing'.", engine.PublishedField("lastOutcome"));

        product.Update(new ProductUpdate(OuterUpdate(2), [Ui("{\"action\":\"load-slot\",\"key\":\"slot-archer\"}")]));
        var restored = ruleset.RequireSession();
        Assert.NotSame(title, restored);
        Assert.Equal(ProductMode.Playing, product.Mode);
        Assert.Equal("class13", restored.State.Character.Identity.CareerId);
        Assert.Equal(7, restored.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.False(restored.RequiresCharacterInitialization);
        Assert.Null(restored.State.Character.Pending);
    }

    [Fact]
    public void Slot_save_close_reopen_resumes_a_fresh_daggerfall_session_with_current_state()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        InMemoryPersistenceService persistence = new();
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake sourceEngine = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases), persistence: persistence);
        CapturingDaggerfallRuleset sourceRuleset = new();
        double savedHealth;
        double savedMaximum;
        WorldPoint savedPosition;
        ulong savedStackQuantity;
        ulong savedNpcUniqueId;
        HashSet<ulong> savedUniqueIds;
        DaggerfallItemInstanceMetadata savedStackMetadata = null!;
        DaggerfallItemInstanceMetadata savedUniqueMetadata = null!;
        DaggerfallSession sourceSession;
        using (WorldRpgSaveSlots slots = new(sourceEngine.Context, "worldrpg.saves"))
        using (WorldRpgProduct sourceProduct = new(new ProductCreateContext(sourceEngine.Context, FullContent(root), input), sourceRuleset, new GameBundleId("daggerfall.classic")))
        {
            sourceProduct.Begin();
            sourceSession = sourceRuleset.RequireSession();
            PlayerActorState player = sourceSession.State.Actors.Player;
            Stat maximum = player.Stats.GetStat(StatId.Parse("health-maximum"));
            Track health = player.Stats.GetTrack(TrackId.Parse("health"));
            _ = maximum.AddModifier(3);
            health.SetCurrent(health.Current - 1);
            savedPosition = new WorldPoint(12, 3, -7);
            sourceSession.State.PlayerControl.Restore(savedPosition, default);
            var stack = sourceSession.State.Inventory.Read().Stacks.First();
            sourceSession.State.Inventory.Grant(new InventoryGrant(new InventoryItemId(stack.Definition.Value), stack.Id, 2));
            savedStackMetadata = new DaggerfallItemInstanceMetadata(stack.Definition.Value, "steel", 4, 3, 8,
                false, true, "quest-99", "relic", null, DaggerfallItemOwner.Player).Validate();
            sourceSession.State.ItemInstances.ReplaceStack(DaggerfallItemOwner.Player, stack.Id, savedStackMetadata);
            DurableIdentityReference npcUniqueIdentity = sourceSession.UniqueItemAllocator.AllocateReference();
            MechanicsEquipmentCoordinator npcEquipment = sourceSession.State.ActorInventories.EquipmentFor(2000);
            var npcUnique = npcEquipment.Materialize(npcUniqueIdentity, new InventoryItemId("iron-dagger"));
            sourceSession.State.ItemInstances.RegisterDefaultUnique(npcUniqueIdentity.Value,
                definitions.Items[new DaggerfallItemId("iron-dagger")], DaggerfallItemOwner.Actor(2000));
            savedUniqueMetadata = new DaggerfallItemInstanceMetadata("iron-dagger", "ebony", 2, 7, 12,
                false, true, "quest-99", "blade", null, DaggerfallItemOwner.Actor(2000)).Validate();
            sourceSession.State.ItemInstances.ReplaceUnique(npcUniqueIdentity.Value, savedUniqueMetadata);
            npcEquipment.Equip(npcUnique, [new WorldRpg.Kit.Inventory.EquipmentSlotId("right-hand")]);
            savedHealth = health.Current;
            savedMaximum = maximum.Value;
            savedStackQuantity = sourceSession.State.Inventory.Read().Stacks.Single(value => value.Definition == stack.Definition).Quantity;
            savedNpcUniqueId = npcUniqueIdentity.Value;

            // The Host's one save layout: catalog metadata plus the slot's payload under its own key.
            WorldRpgSaveSlotEntry saved = slots.SaveSlot("slot-1", "Before the reopen", new GameSaveEnvelope(sourceSession.CaptureSave()),
                PersistenceRevisionGuard.Absent);
            Assert.Equal(1UL, saved.Revision);
            savedUniqueIds = [.. CapturedUniqueItemIds(DaggerfallSavePayload.Read(slots.LoadSlot("slot-1", "daggerfall").Envelope!.Payload))];
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases), persistence: persistence);
        CapturingDaggerfallRuleset resumedRuleset = new();
        // A reopened process reads the slot through the same catalog and resumes through the ruleset's
        // restore path, exactly as the product's load does, before any session state exists.
        using WorldRpgSaveSlots reopened = new(resumedEngine.Context, "worldrpg.saves");
        (GameSaveEnvelope? envelope, WorldRpgSlotLoadDiagnostic? diagnostic) = reopened.LoadSlot("slot-1", "daggerfall");
        Assert.Null(diagnostic);
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition();
        using IGameSession resumed = resumedRuleset.CreateSession(new GameSessionContext(resumedEngine.Context, composition), envelope!.Payload);
        DaggerfallSession restoredSession = resumedRuleset.RequireSession();
        PlayerActorState restoredPlayer = restoredSession.State.Actors.Player;
        Stat restoredMaximum = restoredPlayer.Stats.GetStat(StatId.Parse("health-maximum"));
        Track restoredHealth = restoredPlayer.Stats.GetTrack(TrackId.Parse("health"));
        Assert.NotSame(sourceSession, restoredSession);
        Assert.NotSame(sourceSession.State.Actors.Player.Actor, restoredPlayer.Actor);
        Assert.Equal(savedPosition, restoredSession.State.PlayerControl.Position);
        Assert.Equal(savedHealth, restoredHealth.Current);
        Assert.Equal(savedMaximum, restoredMaximum.Value);
        Assert.Same(restoredMaximum, restoredHealth.Maximum);
        Assert.Equal(savedStackQuantity, restoredSession.State.Inventory.Read().Stacks.Single(value => value.Definition == sourceSession.State.Inventory.Read().Stacks.First().Definition).Quantity);
        Assert.Equal(savedStackMetadata, restoredSession.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player,
            restoredSession.State.Inventory.Read().Stacks.Single(value => value.Definition == sourceSession.State.Inventory.Read().Stacks.First().Definition).Id));
        MechanicsInventoryCoordinator restoredNpcInventory = Assert.IsType<MechanicsInventoryCoordinator>(restoredSession.State.ActorInventories.InventoryFor(2000));
        var restoredNpcUnique = Assert.Single(restoredNpcInventory.Read().UniqueItems, item => item.Definition.Value == "iron-dagger");
        Assert.Equal(savedNpcUniqueId, restoredSession.State.Actors.Entities.IdentityOf(restoredNpcUnique.Entity).Value);
        Assert.Equal(savedUniqueMetadata, restoredSession.State.ItemInstances.RequireUnique(savedNpcUniqueId));
        Assert.Contains(restoredSession.State.ActorInventories.EquipmentFor(2000).Read().Assignments,
            assignment => assignment.Slot.Value == "right-hand" && assignment.Item.EntityId == restoredNpcUnique.Entity.Value);

        _ = restoredMaximum.AddModifier(1);
        Assert.Equal(savedMaximum + 1, restoredHealth.MaximumValue);
        Assert.Same(restoredMaximum, restoredHealth.Maximum);
        Assert.DoesNotContain(restoredSession.UniqueItemAllocator.AllocateReference().Value, savedUniqueIds);
        Assert.Equal(0, resumedSpatial.StepCalls);
    }

    [Fact]
    public void Ordinary_save_slot_actions_roundtrip_selected_state_through_the_product()
    {
        // 8342 acceptance through ordinary controls: real input changes state, the menu save
        // action persists it through the product's own store, more input changes state again,
        // and the menu load action restores the saved point with honest outcome feedback.
        static ProductInputEvent Ui(string json) => Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes(json),
        };

        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        InMemoryPersistenceService persistence = new();
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, 1d, PerceptionPairKind.Visible, 1d));
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service, persistence: persistence);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new();
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId("daggerfall.classic"));
        product.Start();
        NewGameSessionTests.Commit(ruleset.RequireSession());
        product.Begin();
        DaggerfallSession session = ruleset.RequireSession();
        TrackId staminaId = TrackId.Parse("stamina");

        // Loading an empty named slot is an honest outcome, not a session replacement.
        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"load-slot\",\"key\":\"slot-1\"}")]));
        product.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Contains("No save is indexed under 'slot-1'.", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);

        // Ordinary held-key input steps the world: the spatial answer moves the player, so
        // position is the cooldown-free proof that gameplay input changed world state.
        static WorldPoint PlayerPos(DaggerfallSession target) => target.State.PlayerControl.Position
            ?? throw new InvalidOperationException("The player has no position.");
        WorldPoint positionBefore = PlayerPos(session);
        product.Update(new ProductUpdate(
            new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 3, 3, 60, 3, 0, 1d / 60d),
            [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        WorldPoint positionStepped = PlayerPos(session);
        Assert.True(positionStepped.X > positionBefore.X);

        // A named save uses the Host catalog and captures the first selected state.
        product.Update(new ProductUpdate(OuterUpdate(4), [Ui("{\"action\":\"save-slot\",\"label\":\"Before the dungeon\"}")]));
        WorldPoint firstPosition = PlayerPos(session);
        product.Update(new ProductUpdate(OuterUpdate(5), []));
        Assert.Contains("Saved 'Before the dungeon' (revision 1).", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);

        // More ordinary input produces a distinct second slot state.
        product.Update(new ProductUpdate(
            new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 6, 6, 60, 3, 0, 1d / 60d),
            [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        WorldPoint movedSecondPosition = PlayerPos(session);
        Assert.True(movedSecondPosition.X > firstPosition.X);
        product.Update(new ProductUpdate(OuterUpdate(7), [Ui("{\"action\":\"save-slot\",\"label\":\"After the dungeon\"}")]));
        WorldPoint secondPosition = PlayerPos(session);

        // Overwriting an explicit selection requires a confirmation action and cannot alter the
        // second slot. The confirmed overwrite records the later world state in slot 1.
        product.Update(new ProductUpdate(
            new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 8, 8, 60, 3, 0, 1d / 60d),
            [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        WorldPoint overwrittenPosition = PlayerPos(session);
        product.Update(new ProductUpdate(OuterUpdate(9), [Ui("{\"action\":\"save-slot\",\"key\":\"slot-1\",\"label\":\"Revisited dungeon\"}")]));
        Assert.Contains("Confirm overwriting 'slot-1'.", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);
        product.Update(new ProductUpdate(OuterUpdate(10), [Ui("{\"action\":\"save-slot\",\"key\":\"slot-1\",\"label\":\"Revisited dungeon\",\"confirm\":true}")]));
        Assert.Contains("Saved 'Revisited dungeon'", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);

        // Loading the selected second slot replaces the session with its own earlier state rather
        // than the overwritten first slot.
        product.Update(new ProductUpdate(OuterUpdate(11), [Ui("{\"action\":\"load-slot\",\"key\":\"slot-2\"}")]));
        DaggerfallSession restored = ruleset.RequireSession();
        Assert.NotSame(session, restored);
        Assert.Equal(secondPosition, PlayerPos(restored));
        Assert.NotEqual(overwrittenPosition, PlayerPos(restored));
        product.Update(new ProductUpdate(OuterUpdate(12), []));
        Assert.Contains("Game loaded.", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);

        // Deletion also demands confirmation and removes the first payload from reopened storage.
        product.Update(new ProductUpdate(OuterUpdate(13), [Ui("{\"action\":\"delete-slot\",\"key\":\"slot-1\"}")]));
        Assert.Contains("Confirm deleting 'slot-1'.", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);
        product.Update(new ProductUpdate(OuterUpdate(14), [Ui("{\"action\":\"delete-slot\",\"key\":\"slot-1\",\"confirm\":true}")]));
        using WorldRpgSaveSlots reopened = new(engine.Context, "worldrpg.saves");
        (GameSaveEnvelope? deleted, WorldRpgSlotLoadDiagnostic? deletedDiagnostic) = reopened.LoadSlot("slot-1", "daggerfall");
        Assert.Null(deleted);
        Assert.Equal("missing", deletedDiagnostic!.Kind);
    }

    [Fact]
    public void Slot_resume_refuses_unique_items_that_are_unissued_or_tombstoned_in_the_saved_ledger()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallSavePayload valid = CapturedSave(root);
        DaggerfallUniqueSave tombstoned = Assert.IsType<DaggerfallUniqueSave>(valid.Inventory.UniqueItems.FirstOrDefault());
        DurableIdentityAllocator ledger = DurableIdentityAllocator.Restore(valid.RestoredIdentities());
        ulong unissued = ledger.NextIdentity(DurableIdentityKind.Item);
        DaggerfallSavePayload forged = valid with
        {
            Inventory = valid.Inventory with
            {
                UniqueItems = [.. valid.Inventory.UniqueItems, new DaggerfallUniqueSave("iron-tanto", unissued, valid.Inventory.UniqueItems[0].Metadata)],
            },
        };
        KindAllocatorState[] removedKinds = valid.Identities.Kinds.Select(state => state.Kind == DurableIdentityKind.Item
            ? state with
            {
                Reserved = state.Reserved.Where(value => value != tombstoned.EntityId).ToArray(),
                Removed = [.. state.Removed, tombstoned.EntityId],
            }
            : state).ToArray();
        DaggerfallSavePayload removed = valid with { Identities = new DurableIdentityState(removedKinds) };

        foreach ((DaggerfallSavePayload rejected, string classification) in new[]
        {
            (forged, nameof(DurableIdentityClassification.NeverIssued)),
            (removed, nameof(DurableIdentityClassification.Removed)),
        })
        {
            InMemoryPersistenceService persistence = new();
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, inputs);
            SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
            EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), persistence: persistence);
            using WorldRpgSaveSlots slots = new(engine.Context, "worldrpg.saves");
            slots.SaveSlot("slot-1", "Forged", new GameSaveEnvelope(DaggerfallSavePayload.Encode(rejected)), PersistenceRevisionGuard.Absent);
            CapturingDaggerfallRuleset ruleset = new();
            ResolvedGameComposition composition = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition();

            // Resolution refuses the save before any session state or Engine resource exists.
            GameSaveEnvelope envelope = slots.LoadSlot("slot-1", "daggerfall").Envelope!;
            ArgumentException rejection = Assert.Throws<ArgumentException>(() =>
                ruleset.CreateSession(new GameSessionContext(engine.Context, composition), envelope.Payload));
            Assert.Contains(classification, rejection.Message, StringComparison.Ordinal);
            Assert.Null(ruleset.Session);
            Assert.Equal(0, spatial.StepCalls);
        }
    }

    [Fact]
    public void Save_slots_and_player_preferences_live_in_separate_persistence_scopes()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        InMemoryPersistenceService persistence = new();
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), persistence: persistence);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        string preferenceKey = DaggerfallRuleset.Identity.Value;
        CapturingDaggerfallRuleset scopedRuleset = new();
        using (WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), scopedRuleset, new GameBundleId("daggerfall.classic")))
        {
            product.Start();
            NewGameSessionTests.Commit(scopedRuleset.RequireSession());
            product.Begin();
            // A control rebind is a player preference and a named save is a slot: the ordinary actions
            // that write each of the two Host stores.
            product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"controls-rebind","item":"move.forward","key":"KeyQ"}""")]));
            product.Update(new ProductUpdate(OuterUpdate(2), [Ui("""{"action":"save-slot","label":"Scoped"}""")]));
            product.Update(new ProductUpdate(OuterUpdate(3), []));
            Assert.Contains("Saved 'Scoped' (revision 1).", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);
        }

        // Each store opened its own scope, and every key sits only in the scope of the store that wrote it:
        // the slot catalog and its payload in the save scope, the ruleset's preference value in the other.
        string slotScope = Assert.Single(persistence.OpenedScopes.Distinct(), scope => persistence.Keys(scope).Contains(WorldRpgSaveSlots.IndexKey));
        string preferenceScope = Assert.Single(persistence.OpenedScopes.Distinct(), scope => persistence.Keys(scope).Contains(preferenceKey));
        Assert.NotEqual(slotScope, preferenceScope);
        Assert.Equal([WorldRpgSaveSlots.IndexKey, WorldRpgSaveSlots.PayloadKey("slot-1", 1)], persistence.Keys(slotScope));
        Assert.Equal([preferenceKey], persistence.Keys(preferenceScope));

        // A fresh product over the same persistence reads each value back through its own scope: the
        // rebind is live before any slot loads, and the slot list names the one save.
        List<string> resumedReleases = [];
        ContentFake resumedContent = new(resumedReleases);
        PopulateContent(resumedContent, inputs);
        EngineContextFake resumed = EngineContextFake.Create(resumedContent, SpatialFake.Create(inputs.SpatialArtifact.Sha256, resumedReleases).Service,
            new AppearanceFake(resumedReleases), persistence: persistence);
        using WorldRpgProduct reopened = new(new ProductCreateContext(resumed.Context, FullContent(root), input), new CapturingDaggerfallRuleset(), new GameBundleId("daggerfall.classic"));
        Assert.Equal(KeyboardControl.KeyQ, Assert.Single(resumed.PhysicalInput.Mappings, mapping => Encoding.UTF8.GetString(mapping.Intent.Span) == "move.forward").Keyboard);
        using WorldRpgSaveSlots slots = new(resumed.Context, "worldrpg.saves");
        Assert.Equal("Scoped", Assert.Single(slots.List()).Label);
    }

    private static IEnumerable<ulong> CapturedUniqueItemIds(DaggerfallSavePayload saved) =>
        saved.Inventory.UniqueItems.Select(item => item.EntityId)
            .Concat(saved.Corpses.SelectMany(corpse => corpse.UniqueItems).Select(item => item.EntityId))
            .Concat(saved.ActorInventories.SelectMany(actor => actor.Inventory.UniqueItems).Select(item => item.EntityId));
}
