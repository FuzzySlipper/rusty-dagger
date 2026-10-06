using System.Numerics;
using System.Text;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Spawned and encounter actors: registration, retirement and restore.</summary>
public sealed class ActorLifecycleSessionTests
{
    [Fact]
    public void Canonical_engine_actor_transform_flows_through_facade_and_save_capture()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        long actorId = session.SpawnActor("rat", new ActorPose(new WorldPoint(10f, 0f, 10f), 0f));
        ActorState actor = session.State.Actors.Get(actorId);
        WorldPoint externalPosition = new(13f, 2f, -7f);
        float externalHeading = .75f;
        session.State.Actors.Store.Set(actor.Actor.Entity, EngineComponentTypes.Transform, new Transform(
            externalPosition.ToVector(),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, -externalHeading),
            Vector3.One));

        Assert.Equal(externalPosition, actor.Position);
        Assert.Equal(externalHeading, actor.HeadingYawRadians, precision: 5);

        DaggerfallDynamicActorSave saved = Assert.Single(
            DaggerfallSavePayload.Read(session.CaptureSave()).DynamicActors,
            value => value.EntityId == actorId);
        Assert.Equal(externalPosition.X, saved.X);
        Assert.Equal(externalPosition.Y, saved.Y);
        Assert.Equal(externalPosition.Z, saved.Z);
        Assert.Equal(externalHeading, saved.HeadingRadians, precision: 5);
    }

    [Fact]
    public void Spawned_actors_register_retire_and_restore_with_distinct_state()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        long authoredPlacement = inputs.Project.Actors.Keys.Order().First();
        RulesetSavePayload payload;
        long first;
        long second;
        using (DaggerfallSession session = FreshSession())
        {
            first = session.SpawnActor("imp", new ActorPose(new WorldPoint(10, 0, 10), 0f));
            second = session.SpawnActor("imp", new ActorPose(new WorldPoint(20, 0, 20), 1f));
            Assert.NotEqual(first, second);
            Assert.True(first >= (long)DaggerfallSession.DynamicActorFirstIdentity);
            Assert.True(second >= (long)DaggerfallSession.DynamicActorFirstIdentity);
            Assert.Equal("imp", session.DefinitionsByActor[first].Id.Value);
            Assert.Equal(new DaggerfallActorId("imp"), session.DynamicActors[second]);
            // The spawn carries the same Mechanics binding an authored actor is built with.
            Assert.NotNull(session.State.Actors.Get(second).Stats.GetTrack(TrackId.Parse("health")));
            Assert.NotNull(session.State.ActorInventories.InventoryFor(second));
            // Distinct state before retirement: only the second moves.
            session.State.Actors.Get(second).ApplyPose(new ActorPose(new WorldPoint(21, 0, 21), 2f));
            // The retiring actor holds a stack, so its store registrations can only go once it is emptied.
            EntityId firstOwner = session.State.Actors.Get(first).Actor.Entity;
            session.State.ActorInventories.InventoryFor(first)!.Grant(
                new InventoryGrant(new InventoryItemId("arrow"), InventoryStackId.Parse("test.quiver"), 3));
            Assert.Contains(firstOwner, session.State.InventoryStore.InventoryOwners);
            session.RetireActor(first);
            Assert.False(session.State.Actors.TryGet(first, out _));
            Assert.DoesNotContain(firstOwner, session.State.InventoryStore.InventoryOwners);
            Assert.DoesNotContain(firstOwner, session.State.InventoryStore.EquipmentOwners);
            Assert.False(session.DefinitionsByActor.ContainsKey(first));
            Assert.False(session.DynamicActors.ContainsKey(first));
            // Removal is terminal and distinct from never-loaded: every other shape refuses loudly.
            Assert.Throws<InvalidOperationException>(() => session.RetireActor(first));
            Assert.Throws<InvalidOperationException>(() => session.RetireActor(1));
            Assert.Throws<InvalidOperationException>(() => session.RetireActor(authoredPlacement));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.RetireActor(-5));
            Assert.Throws<InvalidOperationException>(() => session.SpawnActor("missing-definition", new ActorPose(new WorldPoint(0, 0, 0), 0f)));
            Assert.Throws<InvalidOperationException>(() => session.SpawnActor("player", new ActorPose(new WorldPoint(0, 0, 0), 0f)));
            payload = session.CaptureSave();
        }

        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(payload);
        Assert.DoesNotContain(captured.DynamicActors, actor => actor.EntityId == first);
        DaggerfallDynamicActorSave saved = Assert.Single(captured.DynamicActors);
        Assert.Equal(second, saved.EntityId);
        Assert.Equal("imp", saved.Definition);
        // No stale bindings survive the retired actor: no cooldown, corpse, or inventory section names it.
        Assert.DoesNotContain(captured.CombatCooldowns, cooldown => cooldown.AttackerId == first);
        Assert.DoesNotContain(captured.Corpses, corpse => corpse.ActorId == first);
        Assert.DoesNotContain(captured.ActorInventories, section => section.EntityId == first);
        Assert.Contains(captured.ActorInventories, section => section.EntityId == second);

        List<string> releases = [];
        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), payload);

        Assert.False(resumed.State.Actors.TryGet(first, out _));
        Assert.True(resumed.State.Actors.TryGet(second, out ActorState? restored));
        Assert.Equal(new WorldPoint(21, 0, 21), restored.Position);
        Assert.Equal("imp", resumed.DefinitionsByActor[second].Id.Value);
        Assert.Equal(new DaggerfallActorId("imp"), resumed.DynamicActors[second]);
        // The tombstone holds: a later spawn never reissues the retired identity.
        long third = resumed.SpawnActor("rat", new ActorPose(new WorldPoint(30, 0, 30), 0f));
        Assert.NotEqual(first, third);
        Assert.NotEqual(second, third);
    }

    [Fact]
    public void Human_encounter_actor_materializes_donor_level_equipment_and_persists_without_reroll()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        long actorId = session.SpawnActor("encounter-warrior", new ActorPose(new WorldPoint(10, 0, 10), 0f), level: 4);
        DaggerfallActorDefinition definition = session.DefinitionsByActor[actorId];
        Assert.Equal((144, "class16", "enemy-class-equipped-melee", "T"), (definition.MobileId, definition.Career, definition.ActionId, definition.LootTableKey));
        Assert.Equal(50, session.State.Actors.Get(actorId).Stats.GetStat(StatId.Parse("long-blade")).ValueInt);
        Assert.NotEmpty(session.State.ActorInventories.InventoryFor(actorId)!.Read().UniqueItems);
        Assert.NotEmpty(session.State.ActorInventories.EquipmentFor(actorId).Read().Assignments);

        // The generated class actor enters the same defeat/corpse owner as an authored mobile;
        // its normalized T loot table is populated through the canonical corpse coordinator.
        session.State.Actors.Get(actorId).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, actorId, 1, 1, .125));
        Assert.True(session.State.Actors.Get(actorId).IsDefeated);
        Assert.True(session.Corpses.TryGetValue(actorId, out CorpseContainer? corpse));
        Assert.NotNull(corpse);
        Assert.True(corpse.IsRegistered);

        RulesetSavePayload saved = session.CaptureSave();
        DaggerfallActorInventorySave before = Assert.Single(DaggerfallSavePayload.Read(saved).ActorInventories, value => value.EntityId == actorId);
        using DaggerfallSession restored = fixture.Restore(saved);
        DaggerfallActorInventorySave after = Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).ActorInventories, value => value.EntityId == actorId);
        Assert.Equal(before.Inventory.UniqueItems.Select(item => (item.ItemId, item.EntityId, item.Metadata.Material)), after.Inventory.UniqueItems.Select(item => (item.ItemId, item.EntityId, item.Metadata.Material)));
        Assert.Equal(before.Inventory.Equipment.Select(item => (item.SlotId, item.ItemEntityId)), after.Inventory.Equipment.Select(item => (item.SlotId, item.ItemEntityId)));
        Assert.Equal(50, restored.State.Actors.Get(actorId).Stats.GetStat(StatId.Parse("long-blade")).ValueInt);
        Assert.Equal("enemy-class-equipped-melee", restored.DefinitionsByActor[actorId].ActionId);
        Assert.Equal(0, restored.DefinitionsByActor[actorId].Rewards.ExperienceReward);
        Assert.True(restored.Corpses.TryGetValue(actorId, out CorpseContainer? restoredCorpse));
        Assert.NotNull(restoredCorpse);
        Assert.True(restoredCorpse.IsRegistered);
    }

    [Fact]
    public void Keyless_class_encounter_actor_leaves_a_corpse_with_no_table_loot()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        long actorId = session.SpawnActor("encounter-city-watch-the-haltmeister", new ActorPose(new WorldPoint(10, 0, 10), 0f), level: 4);
        DaggerfallActorDefinition definition = session.DefinitionsByActor[actorId];
        // EnemyBasics gives the City Watch no loot table key; the donor then selects its all-zero "-" matrix.
        Assert.Equal((146, "class18", (string?)null), (definition.MobileId, definition.Career, definition.LootTableKey));

        DaggerfallInventorySave carried = DaggerfallSavePayload.Read(session.CaptureSave()).ActorInventories
            .Single(entry => entry.EntityId == actorId).Inventory;
        Assert.NotEmpty(carried.UniqueItems);
        session.State.Actors.Get(actorId).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, actorId, 1, 1, .125));
        Assert.True(session.State.Actors.Get(actorId).IsDefeated);
        Assert.True(session.Corpses.TryGetValue(actorId, out CorpseContainer? corpse));
        Assert.NotNull(corpse);
        // The donor moves the dead entity's own items to its loot, and the all-zero matrix generates
        // nothing beside them: the corpse holds exactly the watchman's carried equipment, no gold or
        // table items.
        Assert.True(corpse.IsInteractable);
        DaggerfallCorpseSave saved = DaggerfallSavePayload.Read(session.CaptureSave()).Corpses.Single(value => value.ActorId == actorId);
        Assert.Empty(saved.Stacks);
        Assert.Equal(carried.UniqueItems.Select(item => item.EntityId).Order(), saved.UniqueItems.Select(item => item.EntityId).Order());
    }

    [Fact]
    public void Retiring_a_dynamic_caster_cancels_its_effect_on_another_actor_before_save()
    {
        DaggerfallEffectCatalog catalog = new(
        [new DaggerfallEffectDefinition("retire-bound", "retire-bound", DaggerfallEffectStacking.Stack, 1, 1)]);
        using DaggerfallSession session = FreshSession(catalog);
        long caster = session.SpawnActor("rat", new ActorPose(new WorldPoint(10, 0, 10), 0f));
        using JsonDocument state = JsonDocument.Parse("{\"bound\":true}");
        _ = session.State.Effects.Start(new DaggerfallEffectRequest(
            "retire-caster-effect", "retire-bound", "caster-spell", caster,
            DaggerfallActorIdentity.PlayerEntityId, "classic", "magic", null, 1, 5, state.RootElement.Clone()));
        Assert.Single(session.State.Effects.Active);

        session.RetireActor(caster);

        Assert.Empty(session.State.Effects.Active);
        Assert.Empty(DaggerfallSavePayload.Read(session.CaptureSave()).ActiveEffects);
    }

    [Fact]
    public void Retiring_an_actor_with_open_loot_closes_the_interaction_without_throwing()
    {
        // B1 (behavior lane): retiring a corpse with its loot open used to leave the loot
        // presentation naming a definition that no longer exists, so the next Read threw
        // KeyNotFoundException instead of showing no loot. The retire now closes the container
        // and the mode machine follows back to play through the ordinary Read-null path.
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

        WorldPoint playerPosition = session.State.PlayerControl.Position ?? throw new InvalidOperationException("The test session has no player position.");
        // A rat carries no minimum-metal gate, so the player's iron weapon can kill it; an imp
        // would honestly refuse the same swing (steel gate) and never produce the corpse.
        long spawned = session.SpawnActor("rat", new ActorPose(playerPosition, 0f));
        session.State.Actors.Get(spawned).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, spawned, 1, 1, .125));
        Assert.True(session.State.Actors.Get(spawned).IsDefeated);
        Assert.True(session.Corpses.ContainsKey(spawned));
        AimActivationAt(session, spawned);
        perception.Receipt = Receipt(new PerceptionPair(1, checked((ulong)spawned), 1d, 1d, PerceptionPairKind.Visible, 1d));
        ProductInputEvent loot = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes("""{"action":"loot"}"""),
        };
        perception.Requests.Clear();
        session.Update(new ProductUpdate(OuterUpdate(2), [loot]));
        Assert.IsType<LootPresentation>(session.OpenLoot);
        Assert.Single(perception.Requests, request => request.Targets.Span.ToArray()
            .Any(target => target.Entity == checked((ulong)spawned)));
        session.ApplyProductMode(ProductMode.Modal);
        session.RetireActor(spawned);
        Assert.Null(session.OpenLoot);
        Assert.Equal(ProductMode.Playing, session.PendingModeRequest);
        session.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.Null(session.OpenLoot);
    }
}
