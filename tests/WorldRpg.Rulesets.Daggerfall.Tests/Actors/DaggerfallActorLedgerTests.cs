using System.Numerics;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Focused tests for #11015: single sparse ActorLedger, Tier-2 ActorRenderComponent, capability policies,
/// 22 authored creature-family factions, unified QueryNearby, and HUD-only WithParty escorts.
/// </summary>
public sealed class DaggerfallActorLedgerTests
{
    [Fact]
    public void ActorLedger_sparse_diff_self_prunes_untouched_slots_to_zero_bytes_and_suppresses_home_duplication()
    {
        DaggerfallActorLedger ledger = new();
        DaggerfallWorldProfileKey townA = DaggerfallWorldProfileIds.Exterior(new DaggerfallSiteId(17, 0));
        DaggerfallWorldProfileKey townB = DaggerfallWorldProfileIds.Exterior(new DaggerfallSiteId(17, 1));
        ActorOrigin slotOrigin = ActorOrigin.Seeded(townA, "rmb:17:0:b0:p00");
        ActorOrigin castOrigin = ActorOrigin.Cast("cast.king-gothryd");

        // Untouched deterministic slots and cast individuals write zero entries (0 bytes).
        Assert.False(ledger.Upsert(new ActorLedgerEntry(5001, slotOrigin)));
        Assert.False(ledger.Upsert(new ActorLedgerEntry(5002, castOrigin)));
        Assert.Equal(0, ledger.Count);
        Assert.Empty(ledger.Capture());
        Assert.False(ledger.IsHomeSlotSuppressed(5001, townA));

        // Mutating pose creates a sparse entry; returning deltas to null self-prunes back to zero.
        Assert.True(ledger.Upsert(new ActorLedgerEntry(5001, slotOrigin, Pose: new ActorPoseOverride(4f, 0f, 8f, 1.5f))));
        Assert.Equal(1, ledger.Count);
        Assert.False(ledger.IsHomeSlotSuppressed(5001, townA));

        Assert.False(ledger.Upsert(new ActorLedgerEntry(5001, slotOrigin)));
        Assert.Equal(0, ledger.Count);
        Assert.Empty(ledger.Capture());

        // Relocating to another site, joining WithParty, or marking Removed suppresses home-slot admission.
        Assert.True(ledger.Upsert(new ActorLedgerEntry(5001, slotOrigin, Location: ActorLocationOverride.AtSite(townB))));
        Assert.True(ledger.IsHomeSlotSuppressed(5001, townA));
        Assert.False(ledger.IsHomeSlotSuppressed(5001, townB));
        Assert.Single(ledger.EntriesAtSite(townB));

        ledger.SetWithParty(5001, ActorPartyRole.Escort, escortPortrait: "face-01", escortOrder: 1);
        Assert.True(ledger.IsHomeSlotSuppressed(5001, townA));
        Assert.Single(ledger.EntriesWithParty());

        ledger.MarkRemoved(5001, slotOrigin);
        Assert.True(ledger.IsRemoved(5001));
        Assert.True(ledger.IsHomeSlotSuppressed(5001, townA));
        Assert.Empty(ledger.EntriesWithParty());
    }

    [Fact]
    public void Creature_factions_load_all_22_classic_teams_and_govern_hostility_and_actor_components()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallCreatureFactionSet factions = definitions.CreatureFactions;

        Assert.Equal(22, factions.Factions.Count);
        Assert.True(factions.TryGet("player-ally", out _));
        Assert.True(factions.TryGet("undead", out _));
        Assert.True(factions.TryGet("daedra", out _));
        Assert.True(factions.TryGet("city-watch", out _));
        Assert.True(factions.TryGet("tigers", out _));

        // Same team never infights; distinct teams are hostile; pacified actors leave player-ally alone.
        Assert.False(factions.AreTeamsHostile("undead", "undead", observerPacified: false));
        Assert.True(factions.AreTeamsHostile("undead", "daedra", observerPacified: false));
        Assert.False(factions.AreTeamsHostile("undead", "player-ally", observerPacified: true));
        Assert.True(factions.AreTeamsHostile("undead", "player-ally", observerPacified: false));

        // Spawned monsters carry FactionComponent, Combatant harm policy, and DirectionalMobile render spec.
        using var fixture = new ConditionSessionFixture();
        long ratId = fixture.Session.SpawnActor("rat", new ActorPose(new WorldPoint(4f, 0f, 4f), 0f));
        ActorState rat = fixture.Session.State.Actors.Get(ratId);
        Assert.True(rat.Actor.Has<StatsComponent>());
        Assert.Equal(ActorHarmPolicyKind.Combatant, rat.Actor.Get<ActorHarmPolicy>().Policy);
        Assert.True(rat.Actor.Get<ActorHarmPolicy>().CanDeflectAttacks);
        Assert.Equal(ActorInteractionPolicyKind.None, rat.Actor.Get<ActorInteractionPolicy>().Policy);
        Assert.Equal(ActorVisualKind.DirectionalMobile, rat.Actor.Get<ActorRenderComponent>().Spec.Kind);
        Assert.Equal(fixture.Session.DefinitionsByActor[ratId].Team, rat.Actor.Get<FactionComponent>().CreatureFactionId);
    }

    [Fact]
    public void Unified_QueryNearby_returns_active_scene_combatants_civilians_and_questors_sorted_nearest_first()
    {
        using var fixture = new SanguineRoseSessionTests.Fixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteProfile profile = fixture.Inputs;
        var address = profile.BillboardSprites.Keys.First();
        DaggerfallSiteRecord site = TestPayload.Definitions.Locations.Records.Single(record => record.Id == profile.Site!.Value);
        DaggerfallNpcSite npcSite = new(site.Region, site.Name, string.Empty, profile.ProfileKey.LogicalId);
        DaggerfallNpcAppearance look = new("breton", "Female", address.Archive, address.Record, 11, 0);

        WorldPoint origin = new(50f, 0f, 50f);
        long ratId = session.SpawnActor("rat", new ActorPose(new WorldPoint(56f, 0f, 50f), 0f)); // distance 6
        long civId = session.State.Npcs.RegisterCivilian(npcSite, look, "townsperson", ["talk"]);
        session.Civilians.Materialize(civId, new ActorPose(new WorldPoint(52f, 0f, 50f), 0f)); // distance 2
        session.State.Npcs.Bind(civId, profile.ProfileKey);

        long questorId = session.State.Npcs.RegisterStable(DaggerfallNpcKind.Questor, "nearby-questor", npcSite, look, "quest person", ["talk"]);
        session.State.Npcs.Place(questorId, profile.ProfileKey, new WorldPoint(54f, 0f, 50f)); // distance 4
        session.ReconcileNpcProjection();

        IReadOnlyList<NearbyActorHit> hits = session.QueryNearby(origin, radius: 10d);
        Assert.Equal([civId, questorId, ratId], hits.Select(hit => hit.Actor.DurableId).ToArray());

        // Capability policies and regional people faction are live on all people in EntityStore.
        ActorState civActor = session.State.Actors.Get(civId);
        ActorState questorActor = session.State.Actors.Get(questorId);
        Assert.Equal(ActorHarmPolicyKind.LawProtectedCivilian, civActor.Actor.Get<ActorHarmPolicy>().Policy);
        Assert.False(civActor.Actor.Get<ActorHarmPolicy>().CanDeflectAttacks);
        Assert.Equal(ActorHarmPolicyKind.Protected, questorActor.Actor.Get<ActorHarmPolicy>().Policy);
        Assert.False(session.SpellTargets.IsEligible(questorId));
        Assert.True(session.SpellTargets.IsEligible(ratId));
        Assert.NotNull(questorActor.Actor.Get<FactionComponent>().RegionalPeopleFactionId);

        // Hiding an NPC excludes it from QueryNearby.
        session.State.Npcs.SetPresence(civId, DaggerfallNpcPresence.Hidden);
        Assert.DoesNotContain(session.QueryNearby(origin, radius: 10d), hit => hit.Actor.DurableId == civId);
    }

    [Fact]
    public void Hud_escort_moves_to_WithParty_absent_from_QueryNearby_survives_save_reload_and_delivers_without_duplicate_body()
    {
        var definitions = EscortQuestDefinitions();
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        DaggerfallSession session = fixture.Session;

        var started = session.State.Quests.Start(new("escort", "escort.txt", "escort", DaggerfallQuestLifecycle.Active, null, [], []));
        fixture.Update();
        Assert.True(session.State.Quests.TryGet(started.InstanceId, out var admitted));
        long escortId = admitted!.Resources.Single().Binding.ActorIds.Single();

        // Initially placed at the site: live in EntityStore and visible to QueryNearby.
        Assert.True(session.State.Actors.TryGet(escortId, out ActorState? placed));
        WorldPoint startPos = placed!.Position;
        Assert.Contains(session.QueryNearby(startPos, radius: 5d), hit => hit.Actor.DurableId == escortId);

        // Trigger AddFace -> escort moves to WithParty in ActorLedger and unloads from EntityStore.
        TriggerTask(session, started.InstanceId, "join");
        fixture.Update();

        Assert.True(session.State.Quests.IsEscortWithParty(escortId));
        Assert.False(session.State.Actors.TryGet(escortId, out _));
        Assert.DoesNotContain(session.QueryNearby(startPos, radius: 1000d), hit => hit.Actor.DurableId == escortId);
        Assert.True(session.State.ActorLedger.TryGet(escortId, out ActorLedgerEntry? ledgerEntry));
        Assert.Equal(ActorLocationKind.WithParty, ledgerEntry!.Location?.Kind);
        Assert.Equal(ActorPartyRole.Escort, ledgerEntry.Location?.PartyRole);

        // Save and reload while WithParty: still in ActorLedger as WithParty, absent from EntityStore and QueryNearby.
        RulesetSavePayload saveBytes = session.CaptureSave();
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(saveBytes);
        ActorLedgerEntry savedEscort = Assert.Single(saved.ActorLedger, entry => entry.DurableId == escortId);
        Assert.Equal(ActorLocationKind.WithParty, savedEscort.Location?.Kind);

        using DaggerfallSession restored = DaggerfallSession.Restore(fixture.Engine.Context, fixture.Composition, saveBytes);
        Assert.True(restored.State.Quests.IsEscortWithParty(escortId));
        Assert.False(restored.State.Actors.TryGet(escortId, out _));
        Assert.DoesNotContain(restored.QueryNearby(startPos, radius: 1000d), hit => hit.Actor.DurableId == escortId);

        // Deliver: DropFace + explicit placement at destination produces exactly ONE ActorState with the same durable ID.
        TriggerTask(restored, started.InstanceId, "deliver");
        restored.Update(new ProductUpdate(TestSessions.OuterUpdate(10), []));

        Assert.False(restored.State.Quests.IsEscortWithParty(escortId));
        ActorState delivered = Assert.Single(restored.State.Actors.All, actor => actor.DurableId == escortId);
        Assert.Single(restored.State.Npcs.All, npc => npc.DurableId == escortId);
        Assert.Contains(restored.QueryNearby(delivered.Position, radius: 5d), hit => hit.Actor.DurableId == escortId);
    }

    [Fact]
    public void Untouched_populated_site_writes_zero_ledger_entries_and_self_prunes_and_suppresses_relocated_seeded_and_cast_actors()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallCastIndividualDefinition cast = definitions.People.Individuals.Values.First();
        DaggerfallSiteProfile exteriorTemplate = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        // Real populated exterior town (Charing source walkers) + an authored CastIndividual placement admitted
        // automatically by production MaterializeStaticNpcs via its Individual FactionId.
        DaggerfallSiteProfile homeTown = StaticNpcAdmissionTests.StaticProviderSite(exteriorTemplate, factionId: cast.FactionId);
        DaggerfallSiteProfile otherTown = StaticNpcAdmissionTests.StaticProviderSite(Assembled(DaggerfallWorldProfileIds.Interior(Charing, new(0, 0, 0))));
        DaggerfallSiteProfiles profiles = new([homeTown, otherTown]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, homeTown);
        PopulateContent(content, otherTown);
        SpatialFake spatial = SpatialFake.Create(homeTown.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, homeTown, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        // Advance into daytime so exterior town walkers are admitted via SourcePopulation.Reconcile.
        session.AdvanceElapsedTime(8 * 60 * 60);

        DaggerfallNpc[] walkers = [.. session.State.Npcs.All.Where(DaggerfallCivilians.IsSourcePopulation)];
        Assert.NotEmpty(walkers);
        Assert.All(walkers, walker => Assert.True(DaggerfallPeopleSlots.IsDeterministicId(walker.DurableId)));
        DaggerfallNpc walkerNpc = walkers[0];
        long walkerId = walkerNpc.DurableId;
        Assert.True(session.State.Actors.TryGet(walkerId, out ActorState? walkerLive));
        Assert.Equal(ActorOriginKind.SeededSlot, walkerLive!.Actor.Get<ActorOriginComponent>().Origin.Kind);

        // CastIndividual was admitted automatically by MaterializeStaticNpcs (no direct test-only call).
        long castId = DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id);
        Assert.True(session.State.Actors.TryGet(castId, out ActorState? castLive));
        Assert.Equal(ActorOriginKind.CastIndividual, castLive!.Actor.Get<ActorOriginComponent>().Origin.Kind);
        Assert.Equal(cast.Name, session.State.Npcs.Require(castId).DisplayName);

        // Untouched populated town (walkers + cast individual) saves 0 ledger entries and 0 actor inventory entries.
        DaggerfallSavePayload untouched = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Empty(untouched.ActorLedger);
        Assert.Empty(untouched.ActorInventories);

        // R11015-7: Move the CastIndividual, SAVE, RELOAD, then reset back to authored baseline pose/heading,
        // SAVE again, and RELOAD — must self-prune to 0 ledger entries without resurrecting the old moved pose.
        WorldPoint baselineLocal = castLive.Position;
        WorldPoint movedLocal = new(baselineLocal.X + 3f, baselineLocal.Y, baselineLocal.Z + 1f);
        castLive.ApplyPose(new ActorPose(movedLocal, 0.5f));
        RulesetSavePayload movedSaveBytes = session.CaptureSave();
        DaggerfallSavePayload moved = DaggerfallSavePayload.Read(movedSaveBytes);
        ActorLedgerEntry sparseMoved = Assert.Single(moved.ActorLedger, entry => entry.DurableId == castId);
        Assert.Equal(ActorOriginKind.CastIndividual, sparseMoved.Origin.Kind);
        Assert.NotNull(sparseMoved.Pose);
        Assert.Null(sparseMoved.Vitals);

        using (DaggerfallSession reloadedMoved = DaggerfallSession.Restore(engine.Context, composition, movedSaveBytes))
        {
            ActorState reloadedCast = reloadedMoved.State.Actors.Get(castId);
            Assert.Equal(movedLocal.X, reloadedCast.Position.X, 3);
            Assert.Equal(movedLocal.Z, reloadedCast.Position.Z, 3);

            // Reset back to authored baseline pose & heading after reload.
            reloadedCast.ApplyPose(new ActorPose(baselineLocal, 0f));
            RulesetSavePayload prunedSaveBytes = reloadedMoved.CaptureSave();
            DaggerfallSavePayload pruned = DaggerfallSavePayload.Read(prunedSaveBytes);
            Assert.Empty(pruned.ActorLedger);

            using DaggerfallSession reloadedPruned = DaggerfallSession.Restore(engine.Context, composition, prunedSaveBytes);
            ActorState restoredBaselineCast = reloadedPruned.State.Actors.Get(castId);
            Assert.Equal(baselineLocal.X, restoredBaselineCast.Position.X, 3);
            Assert.Equal(baselineLocal.Z, restoredBaselineCast.Position.Z, 3);
        }

        // Relocate the CastIndividual to otherTown via ActorLedger and save:
        // Step 1 must suppress home-slot re-admission at homeTown, and Step 2 must materialize it at otherTown.
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            castId,
            ActorOrigin.Cast(cast.Id),
            Pose: new ActorPoseOverride(4f, 0f, 5f, 0f),
            Social: DaggerfallSessionPersistence.ToSocialDiff(session.State.Npcs.Require(castId)),
            Location: ActorLocationOverride.AtSite(otherTown.ProfileKey)));
        session.Roster.UnloadActor(castId);
        RulesetSavePayload relocatedSave = session.CaptureSave();

        using DaggerfallSession reloadedAtHome = DaggerfallSession.Restore(engine.Context, composition, relocatedSave);
        Assert.False(reloadedAtHome.State.Actors.TryGet(castId, out _));
        Assert.True(reloadedAtHome.TryTransitionTo(otherTown.ProfileKey));
        Assert.True(reloadedAtHome.State.Actors.TryGet(castId, out ActorState? relocatedLive));
        Assert.Equal(new WorldPoint(4f, 0f, 5f), relocatedLive!.Position);
    }

    [Fact]
    public void Source_civilian_HUD_escort_is_not_rematerialized_by_population_reconcile_across_save_and_reload()
    {
        DaggerfallDefinitions definitions = CivilianEscortQuestDefinitions();
        DaggerfallSiteProfile town = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, town);
        SpatialFake spatial = SpatialFake.Create(town.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, town, DaggerfallTuning.Defaults)
        {
            Profiles = new DaggerfallSiteProfiles([town]),
        };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc walker = session.State.Npcs.All.First(DaggerfallCivilians.IsSourcePopulation);
        long escortId = walker.DurableId;
        Assert.True(DaggerfallPeopleSlots.IsDeterministicId(escortId));
        Assert.True(session.State.Actors.TryGet(escortId, out ActorState? liveWalker));
        WorldPoint startPos = liveWalker!.Position;

        var started = session.State.Quests.Start(new(
            "escort-civ", "escort-civ.txt", "escort-civ", DaggerfallQuestLifecycle.Active, null,
            [new("vip", DaggerfallQuestResourceBinding.Actors(escortId))
            {
                SelectedPerson = new(walker.Appearance.FactionId, walker.Appearance.Race, walker.Appearance.Gender,
                    0, null, walker.Appearance.NameSeed, walker.DisplayName ?? walker.Role, false, null, escortId)
                {
                    Appearance = walker.Appearance,
                },
            }], []));

        // AddFace moves the source civilian WithParty and unloads their world actor.
        TriggerTask(session, started.InstanceId, "join");
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.True(session.State.Quests.IsEscortWithParty(escortId));
        Assert.False(session.State.Actors.TryGet(escortId, out _));

        // Population reconciliation must NOT rematerialize a WithParty source civilian.
        session.Population.Reconcile();
        Assert.False(session.State.Actors.TryGet(escortId, out _));
        Assert.DoesNotContain(session.QueryNearby(startPos, radius: 1000d), hit => hit.Actor.DurableId == escortId);

        // Save and reload while WithParty: ActorLedger preserves WithParty and Population.Reconcile still suppresses it.
        RulesetSavePayload saveBytes = session.CaptureSave();
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(saveBytes);
        ActorLedgerEntry savedEntry = Assert.Single(saved.ActorLedger, entry => entry.DurableId == escortId);
        Assert.Equal(ActorLocationKind.WithParty, savedEntry.Location?.Kind);

        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, saveBytes);
        restored.Population.Reconcile();
        Assert.True(restored.State.Quests.IsEscortWithParty(escortId));
        Assert.False(restored.State.Actors.TryGet(escortId, out _));
        Assert.DoesNotContain(restored.QueryNearby(startPos, radius: 1000d), hit => hit.Actor.DurableId == escortId);

        // Delivery (DropFace) rematerializes exactly one actor with the same deterministic ID.
        TriggerTask(restored, started.InstanceId, "deliver");
        restored.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.False(restored.State.Quests.IsEscortWithParty(escortId));
        ActorState delivered = Assert.Single(restored.State.Actors.All, actor => actor.DurableId == escortId);
        Assert.Contains(restored.QueryNearby(delivered.Position, radius: 5d), hit => hit.Actor.DurableId == escortId);
    }

    [Fact]
    public void Non_default_deterministic_NPC_inventory_round_trips_across_save_reload_and_site_transitions()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile homeTown = StaticNpcAdmissionTests.StaticProviderSite(Assembled(DaggerfallWorldProfileIds.Interior(Charing, new(1, 1, 0))));
        DaggerfallSiteProfile otherTown = StaticNpcAdmissionTests.StaticProviderSite(Assembled(DaggerfallWorldProfileIds.Interior(Charing, new(0, 0, 0))));
        DaggerfallSiteProfiles profiles = new([homeTown, otherTown]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, homeTown);
        PopulateContent(content, otherTown);
        SpatialFake spatial = SpatialFake.Create(homeTown.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, homeTown, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        DaggerfallNpc npc = Assert.Single(session.State.Npcs.All, value => value.Kind == DaggerfallNpcKind.Static);
        long staticId = npc.DurableId;
        Assert.True(DaggerfallPeopleSlots.IsDeterministicId(staticId));

        // Grant a non-default stack item to the deterministic static NPC's inventory.
        InventoryStackId testStackId = InventoryStackId.Parse("test.static.gold");
        DaggerfallItemDefinition goldDef = definitions.RequireItem(new DaggerfallItemId("gold-piece"));
        MechanicsInventoryCoordinator npcInventory = session.State.ActorInventories.InventoryFor(staticId)!;
        npcInventory.Grant(new InventoryGrant(new InventoryItemId(goldDef.Id.Value), testStackId, 42));
        session.State.ItemInstances.RegisterDefaultStack(DaggerfallItemOwner.Actor(staticId),
            new InventoryStack(testStackId, ItemDefinitionId.Parse(goldDef.Id.Value), 42), goldDef);

        // Path A: Direct save -> reload must materialize the deterministic owner BEFORE applying ActorInventories.
        RulesetSavePayload directSave = session.CaptureSave();
        Assert.Contains(DaggerfallSavePayload.Read(directSave).ActorInventories, inv => inv.EntityId == staticId);
        using (DaggerfallSession directRestored = DaggerfallSession.Restore(engine.Context, composition, directSave))
        {
            InventoryStack restoredStack = Assert.Single(directRestored.State.ActorInventories.InventoryFor(staticId)!.Read().Stacks);
            Assert.Equal(testStackId, restoredStack.Id);
            Assert.Equal(42UL, restoredStack.Quantity);
        }

        // Path B: Leave site -> save while away -> reload while away -> return to homeTown -> inventory preserved!
        Assert.True(session.TryTransitionTo(otherTown.ProfileKey));
        Assert.False(session.State.Actors.TryGet(staticId, out _));
        RulesetSavePayload awaySave = session.CaptureSave();
        ActorLedgerEntry awayEntry = Assert.Single(DaggerfallSavePayload.Read(awaySave).ActorLedger, entry => entry.DurableId == staticId);
        Assert.NotNull(awayEntry.Combat?.DetachedInventory);

        using DaggerfallSession awayRestored = DaggerfallSession.Restore(engine.Context, composition, awaySave);
        Assert.True(awayRestored.TryTransitionTo(homeTown.ProfileKey));
        Assert.True(awayRestored.State.Actors.TryGet(staticId, out _));
        InventoryStack returnedStack = Assert.Single(awayRestored.State.ActorInventories.InventoryFor(staticId)!.Read().Stacks);
        Assert.Equal(testStackId, returnedStack.Id);
        Assert.Equal(42UL, returnedStack.Quantity);
    }

    [Fact]
    public void Encounter_save_retains_only_pending_outcomes_while_materialized_encounters_live_in_ActorLedger()
    {
        using var fixture = new SanguineRoseSessionTests.Fixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallEncounterRequest request = new(DaggerfallEncounterContext.Dungeon, 1, DungeonType: 2, EnemyAlert: true);

        // Step 1: Queue first encounter and let it materialize onto the floor.
        HashSet<long> existingActorIds = session.State.Actors.All.Select(a => a.DurableId).ToHashSet();
        WorldPoint playerPos = session.State.PlayerControl.Position!.Value;
        fixture.Spatial.FloorHit = r => r.Direction.Y < -.99f
            ? new SpatialHit { Present = true, Kind = SpatialHitKind.StaticMesh, Normal = Vector3.UnitY, Point = r.Origin with { Y = playerPos.Y } }
            : default;
        DaggerfallEncounterResolution first = session.WorldEncounters.Queue(request);
        Assert.NotNull(first.ActorDefinition);
        fixture.Update();

        // Step 2: Remove floor so second encounter remains strictly Pending.
        fixture.Spatial.FloorHit = _ => default;
        DaggerfallEncounterResolution second = session.WorldEncounters.Queue(request);
        Assert.NotNull(second.ActorDefinition);
        fixture.Update();

        RulesetSavePayload saveBytes = session.CaptureSave();
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(saveBytes);
        // Encounters save holds ONLY the unmaterialized second outcome; first lives exclusively in ActorLedger.
        DaggerfallEncounterResolution savedPending = Assert.Single(saved.Encounters.Pending);
        Assert.Equal(second.Sequence, savedPending.Sequence);
        Assert.Null(savedPending.SpawnedActorId);
        ActorLedgerEntry materializedInLedger = Assert.Single(saved.ActorLedger,
            entry => !existingActorIds.Contains(entry.DurableId)
                && entry.Origin.Kind == ActorOriginKind.Minted
                && entry.Origin.DefinitionId == first.ActorDefinition);

        using DaggerfallSession restored = DaggerfallSession.Restore(fixture.Engine.Context, fixture.Composition, saveBytes);
        Assert.True(restored.State.Actors.TryGet(materializedInLedger.DurableId, out _));
        Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).Encounters.Pending);
    }

    [Fact]
    public void Live_FactionComponent_override_governs_hostility_and_quest_relation_takes_precedence()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["change foe _enemy_ team Undead"]);
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        DaggerfallSession session = fixture.Session;

        // Ordinary creature: mutating FactionComponent.TeamOverride flips hostility directly without quest involvement.
        long ratId = session.SpawnActor("rat", new ActorPose(new WorldPoint(4f, 0f, 4f), 0f));
        ActorState rat = session.State.Actors.Get(ratId);
        DaggerfallActorDefinition ratDef = session.DefinitionsByActor[ratId];
        Assert.True(session.Senses.IsHostile(ratId, ratDef));
        rat.Actor.Get<FactionComponent>().SetTeamOverride("player-ally");
        Assert.False(session.Senses.IsHostile(ratId, ratDef));

        // Quest foe: explicit quest FoeRelations team override takes precedence over FactionComponent.
        var site = definitions.Locations.Records.Single(value => value.Id == fixture.Inputs.Site);
        session.State.Quests.Start(new("qteam", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(fixture.Inputs.ProfileKind, site.MapId, null, 0) })], []));
        session.State.Quests.RequestPlacement("qteam", "initial", "enemy", "location");
        session.State.Quests.AdmitPlacements(fixture.Inputs, session.QuestPlacement);
        long foeId = session.State.Quests.Capture().Instances.Single().Resources.Single(r => r.Symbol == "enemy").Binding.ActorIds.Single();
        ActorState foe = session.State.Actors.Get(foeId);
        DaggerfallActorDefinition foeDef = session.DefinitionsByActor[foeId];
        foe.Actor.Get<FactionComponent>().SetTeamOverride("player-ally");
        Assert.False(session.Senses.IsHostile(foeId, foeDef));
        fixture.Update(); // runs "change foe _enemy_ team Undead"
        Assert.True(session.Senses.IsHostile(foeId, foeDef));
    }

    [Fact]
    public void Save_load_retire_spawned_actor_then_save_load_removes_stale_ledger_entry_for_live_and_detached_actors()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["remove foe _enemy_"]);
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        DaggerfallSiteProfiles profiles = new([fixture.Inputs, fixture.Castle]);
        fixture.Session.AdmitSiteProfiles(profiles);

        // Path A: live spawned actor save -> load -> retire -> save -> load.
        long liveId = fixture.Session.SpawnActor("rat", new ActorPose(new WorldPoint(5f, 0f, 5f), 0f));
        RulesetSavePayload save1 = fixture.Session.CaptureSave();
        using (DaggerfallSession restored1 = DaggerfallSession.Restore(fixture.Engine.Context, fixture.Composition with { Profiles = profiles }, save1))
        {
            Assert.True(restored1.State.Actors.TryGet(liveId, out _));
            restored1.RetireActor(liveId);
            Assert.False(restored1.State.Actors.TryGet(liveId, out _));
            RulesetSavePayload save2 = restored1.CaptureSave();
            Assert.DoesNotContain(DaggerfallSavePayload.Read(save2).ActorLedger, entry => entry.DurableId == liveId);

            using DaggerfallSession restored2 = DaggerfallSession.Restore(fixture.Engine.Context, fixture.Composition with { Profiles = profiles }, save2);
            Assert.False(restored2.State.Actors.TryGet(liveId, out _));
        }

        // Path B: detached actor save -> load -> detached retire -> save -> load.
        var site = definitions.Locations.Records.Single(value => value.Id == fixture.Inputs.Site);
        fixture.Session.State.Quests.Start(new("det", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(fixture.Inputs.ProfileKind, site.MapId, null, 0) })], []));
        fixture.Session.State.Quests.RequestPlacement("det", "initial", "enemy", "location");
        fixture.Session.State.Quests.AdmitPlacements(fixture.Inputs, fixture.Session.QuestPlacement);
        long detachedId = fixture.Session.State.Quests.Capture().Instances.Single().Resources.Single(r => r.Symbol == "enemy").Binding.ActorIds.Single();
        Assert.True(fixture.Session.TryTransitionTo(fixture.Castle.ProfileKey));
        RulesetSavePayload awaySave1 = fixture.Session.CaptureSave();

        using DaggerfallSession awayRestored = DaggerfallSession.Restore(fixture.Engine.Context, fixture.Composition with { Profiles = profiles }, awaySave1);
        awayRestored.Update(new ProductUpdate(OuterUpdate(5), [])); // runs "remove foe _enemy_" while off-site
        RulesetSavePayload awaySave2 = awayRestored.CaptureSave();
        Assert.DoesNotContain(DaggerfallSavePayload.Read(awaySave2).ActorLedger, entry => entry.DurableId == detachedId);
        using DaggerfallSession awayRestored2 = DaggerfallSession.Restore(fixture.Engine.Context, fixture.Composition with { Profiles = profiles }, awaySave2);
        Assert.True(awayRestored2.TryTransitionTo(fixture.Inputs.ProfileKey));
        Assert.False(awayRestored2.State.Actors.TryGet(detachedId, out _));
    }

    private static void TriggerTask(DaggerfallSession session, string instanceId, string taskSymbol)
    {
        DaggerfallQuestInstancesSave snapshot = session.State.Quests.Capture();
        session.State.Quests.Restore(snapshot with
        {
            Instances = snapshot.Instances.Select(instance => instance.InstanceId == instanceId
                ? instance with
                {
                    Tasks = instance.Tasks.Select(task => task.Symbol == taskSymbol
                        ? task with { IsSet = true }
                        : task).ToArray(),
                }
                : instance).ToArray(),
        });
    }

    private static DaggerfallDefinitions CivilianEscortQuestDefinitions()
    {
        JsonArray root = TestPayload.QuestRecords();
        root.Add(JsonNode.Parse("""
            {
              "name": "escort-civ",
              "displayName": "",
              "sourceFile": "escort-civ.txt",
              "messages": [],
              "blocks": [
                { "kind": "person", "firstLine": 1, "lines": ["Person _vip_ group Questor"] },
                { "kind": "task", "firstLine": 10, "lines": ["_join_ task:", "add _vip_ face"] },
                { "kind": "task", "firstLine": 20, "lines": ["_deliver_ task:", "drop _vip_ face"] }
              ]
            }
            """)!);
        return TestPayload.WithQuests(root);
    }

    private static DaggerfallDefinitions EscortQuestDefinitions()
    {
        JsonArray root = TestPayload.QuestRecords();
        root.Add(JsonNode.Parse("""
            {
              "name": "escort",
              "displayName": "",
              "sourceFile": "escort.txt",
              "messages": [],
              "blocks": [
                { "kind": "person", "firstLine": 1, "lines": ["Person _vip_ named Queen_Akorithi atHome female"] },
                { "kind": "task", "firstLine": 10, "lines": ["_join_ task:", "add _vip_ face"] },
                { "kind": "task", "firstLine": 20, "lines": ["_deliver_ task:", "drop _vip_ face", "place npc _vip_ at _vip_.home"] }
              ]
            }
            """)!);
        return TestPayload.WithQuests(root);
    }
}
