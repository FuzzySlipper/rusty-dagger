using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Site transitions: admission, portals, relocation, return sites and their save/restore.</summary>
public sealed class SiteTransitionSessionTests
{
    [Fact]
    public void Site_transition_replaces_the_admitted_world_and_restores_the_source_pose_on_return()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSiteProfile destination = DaggerfallSiteContent.Read(FullContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, destination);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults));
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([source, destination]));
        WorldPoint sourcePosition = new(17f, 3f, -11f);
        session.State.PlayerControl.MoveTo(sourcePosition.ToVector());
        session.State.PlayerControl.YawRadians = .7f;
        session.State.PlayerControl.PitchRadians = -.2f;
        const long sourceActorId = 2000;
        ActorState sourceActor = session.State.Actors.Get(sourceActorId);
        WorldPoint actorPosition = new(21f, 4f, -13f);
        sourceActor.ApplyPose(new ActorPose(actorPosition, .4f));
        sourceActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, sourceActorId, 1, 1, .125));
        Assert.True(session.Corpses.ContainsKey(sourceActorId));
        CorpseContainer sourceCorpse = session.Corpses[sourceActorId];
        int sourceCorpseStacks = session.State.Containers.Read(sourceCorpse.Owner).Stacks.Count;
        DaggerfallRdbDoorId sourceDoor = source.Doors.First().Id;
        DaggerfallDungeonDiscovery sourceDiscovery = session.State.DungeonDiscoveries[source.ProfileKey];
        Assert.True(sourceDiscovery.ObserveDoor(sourceDoor));
        Vector3 observedSurface = source.DungeonMap!.GeometryPlacements.First().SamplePoints[0];
        Assert.True(sourceDiscovery.ObserveSurface(observedSurface));
        int partialCellCount = sourceDiscovery.Capture().DiscoveredSurfaceCells.Length;
        Assert.Equal(1, partialCellCount);
        Assert.Equal(DaggerfallDoorOperationResult.Started, session.Doors.Open(sourceDoor, DaggerfallDoorOperationSource.DungeonAction));
        DaggerfallDoorMotion sourceDoorMotion = session.Doors.Read(sourceDoor).Motion;
        long sourceArcher = Assert.Single(source.Project.Actors.Values, placement => placement.ActorId == new DaggerfallActorId("archer")).EntityId;
        DurableIdentityReference archerIdentity = new(DurableIdentityKind.Actor, checked((ulong)sourceArcher));
        Assert.Equal(DurableEntityResolution.Materialized,
            session.State.Actors.Entities.Classify(archerIdentity, session.State.Npcs.Identities!));
        MechanicsInventoryCoordinator archerInventory = Assert.IsType<MechanicsInventoryCoordinator>(session.State.ActorInventories.InventoryFor(sourceArcher));
        InventoryStackId arrows = archerInventory.Read().Stacks.Single(stack => stack.Definition.Value == "arrow").Id;
        archerInventory.Consume(new InventoryConsume(arrows, 1));

        Assert.True(session.TryTransitionTo(destination.ProfileKey));
        Assert.Equal(destination.Site, session.Site.Active);
        Assert.Equal(DurableEntityResolution.Unloaded,
            session.State.Actors.Entities.Classify(archerIdentity, session.State.Npcs.Identities!));
        Assert.Equal(DurableEntityResolution.NeverIssued,
            session.State.Actors.Entities.Classify(new(DurableIdentityKind.Actor, ulong.MaxValue), session.State.Npcs.Identities!));
        Assert.Equal(2, spatial.ReplaceCalls);
        // The behavior module was composed before this transition.  A destination actor must
        // carry its own pursuit component so the next real admitted update can inspect it.
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.True(session.TryTransitionTo(source.ProfileKey));
        Assert.Equal(DurableEntityResolution.Materialized,
            session.State.Actors.Entities.Classify(archerIdentity, session.State.Npcs.Identities!));
        Assert.Equal(source.Site, session.Site.Active);
        Assert.Equal(sourcePosition, session.State.PlayerControl.Position);
        Assert.Equal(.7f, session.State.PlayerControl.YawRadians);
        Assert.Equal(-.2f, session.State.PlayerControl.PitchRadians);
        Assert.Equal(3, spatial.ReplaceCalls);
        Assert.Equal(sourceDoorMotion, session.Doors.Read(sourceDoor).Motion);
        Assert.True(sourceDiscovery.IsDoorDiscovered(sourceDoor));
        Assert.Equal(partialCellCount, sourceDiscovery.Capture().DiscoveredSurfaceCells.Length);
        Assert.Equal(sourceDoorMotion, Assert.Single(DaggerfallSavePayload.Read(session.CaptureSave()).Doors, door => door.Id == sourceDoor).Motion);
        Assert.True(session.Corpses.ContainsKey(sourceActorId));
        Assert.Equal(sourceCorpseStacks, session.State.Containers.Read(session.Corpses[sourceActorId].Owner).Stacks.Count);
        Assert.Equal(actorPosition, session.State.Actors.Get(sourceActorId).Position);
        Assert.Equal(11UL, Assert.IsType<MechanicsInventoryCoordinator>(session.State.ActorInventories.InventoryFor(sourceArcher))
            .Read().Stacks.Single(stack => stack.Definition.Value == "arrow").Quantity);
    }

    [Fact]
    public void Site_light_resources_retire_on_transition_and_rebuild_on_save_restore()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        ProductContent admitted = FullContent(root);
        DaggerfallSiteProfile destination = ReadProfile(root, admitted, definitions, "daggerfall.castle-necromoghan.json");
        DaggerfallSiteProfiles profiles = new([source, destination]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, destination);
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(source.SpatialArtifact.Sha256, releases).Service, appearance);
        RulesetSavePayload save;

        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults)))
        {
            session.AdmitSiteProfiles(profiles);
            Assert.Equal(source.Lights.Count + 1, appearance.LightRequests.Count);
            Assert.True(session.TryTransitionTo(destination.ProfileKey));
            Assert.Equal(source.Lights.Count + destination.Lights.Count + 2, appearance.LightRequests.Count);
            Assert.Equal(source.Lights.Count + 1, appearance.DisposedLights);
            Assert.Equal(2, engine.BackgroundColors.Count);
            save = session.CaptureSave();
        }
        Assert.Equal(source.Lights.Count + destination.Lights.Count + 2, appearance.DisposedLights);

        List<string> restoredReleases = [];
        ContentFake restoredContent = new(restoredReleases);
        PopulateContent(restoredContent, source);
        PopulateContent(restoredContent, destination);
        AppearanceFake restoredAppearance = new(restoredReleases);
        EngineContextFake restoredEngine = EngineContextFake.Create(restoredContent, SpatialFake.Create(destination.SpatialArtifact.Sha256, restoredReleases).Service, restoredAppearance);
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(admitted, new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using (DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, new(definitions, source, DaggerfallTuning.Defaults, identity) { Profiles = profiles }, save))
        {
            Assert.Equal(destination.Lights.Count + 1, restoredAppearance.LightRequests.Count);
            Assert.True(restored.TryTransitionTo(source.ProfileKey));
            Assert.Equal(destination.Lights.Count + source.Lights.Count + 2, restoredAppearance.LightRequests.Count);
            Assert.Equal(destination.Lights.Count + 1, restoredAppearance.DisposedLights);
        }
        Assert.Equal(destination.Lights.Count + source.Lights.Count + 2, restoredAppearance.DisposedLights);
    }

    [Fact]
    public void Charing_portals_enter_and_return_between_profiles_that_share_a_geographic_site()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root);
        DaggerfallSiteProfile exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        DaggerfallSiteProfile interior = ReadProfile(root, admitted, definitions, "daggerfall.charing-interior-1-1-0.json");
        Assert.Equal(exterior.Site, interior.Site);
        Assert.NotEqual(exterior.ProfileKey, interior.ProfileKey);
        DaggerfallSiteProfiles profiles = new([exterior, interior]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, interior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        RulesetSavePayload save;
        DaggerfallSitePortal exteriorPortal = Assert.Single(exterior.Portals);
        DaggerfallSitePortal interiorPortal = Assert.Single(interior.Portals);
        long spawnedActor;
        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, exterior, DaggerfallTuning.Defaults)))
        {
            session.AdmitSiteProfiles(profiles);
            spawnedActor = session.SpawnActor("rat", new ActorPose(new WorldPoint(9f, 0f, 9f), 0f));
            AimActivationAt(session, exteriorPortal.Position);
            perception.Responder = request => PortalReceipt(request, exteriorPortal);
            session.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"loot\"}")]));

            Assert.Equal(interior.Site, session.Site.Active);
            Assert.False(session.State.Actors.TryGet(spawnedActor, out _));
            Assert.Equal(2, spatial.ReplaceCalls);
            Assert.Equal("You pass through the entrance.", session.ActivationView.Message);
            Assert.Equal(interior.ProfileKey, DaggerfallSavePayload.Read(session.CaptureSave()).Site.ActiveProfile!.Require());
            save = session.CaptureSave();
        }

        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(save);
        Assert.Equal(interior.ProfileKey, captured.Site.ActiveProfile!.Require());
        Assert.Equal(exterior.ProfileKey, captured.Site.ReturnProfile!.Require());
        Assert.Equal(exterior.ProfileKey, Assert.Single(captured.SiteDeltas).Profile.Require());
        Assert.Equal(spawnedActor, Assert.Single(captured.SiteDeltas[0].DynamicActors).EntityId);

        List<string> resumedReleases = [];
        ContentFake resumedContent = new(resumedReleases);
        PopulateContent(resumedContent, exterior);
        PopulateContent(resumedContent, interior);
        SpatialFake resumedSpatial = SpatialFake.Create(interior.SpatialArtifact.Sha256, resumedReleases);
        PerceptionFake resumedPerception = PerceptionFake.Create();
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(resumedReleases), resumedPerception.Service);
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(admitted, new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, exterior, DaggerfallTuning.Defaults, identity) { Profiles = profiles }, save);

        Assert.Equal(interior.Site, restored.Site.Active);
        AimActivationAt(restored, interiorPortal.Position);
        resumedPerception.Responder = request => PortalReceipt(request, interiorPortal);
        restored.Update(new ProductUpdate(OuterUpdate(2), [Ui("{\"action\":\"loot\"}")]));

        Assert.Equal(exterior.Site, restored.Site.Active);
        Assert.True(restored.State.Actors.TryGet(spawnedActor, out ActorState? returnedActor));
        Assert.Equal(new WorldPoint(9f, 0f, 9f), returnedActor.Position);
        Assert.Equal(new DaggerfallActorId("rat"), restored.DynamicActors[spawnedActor]);
        Assert.Equal(2, resumedSpatial.ReplaceCalls);
        Assert.Equal("You pass through the entrance.", restored.ActivationView.Message);
    }

    [Fact]
    public void Dynamic_actor_target_effect_suspends_with_an_inactive_site_and_resumes_after_save_return()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        ProductContent admitted = FullContent(root);
        DaggerfallSiteProfile destination = DaggerfallSiteContent.Read(admitted,
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        DaggerfallSiteProfiles profiles = new([source, destination]);
        DaggerfallEffectCatalog catalog = EffectCatalog();
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, destination);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        long dynamicActor;
        RulesetSavePayload save;

        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults) { Effects = catalog }))
        {
            session.AdmitSiteProfiles(profiles);
            dynamicActor = session.SpawnActor("rat", new ActorPose(new WorldPoint(9f, 0f, 9f), 0f));
            using JsonDocument state = JsonDocument.Parse("{\"site\":true}");
            _ = session.State.Effects.Start(new DaggerfallEffectRequest("inactive-target-effect", "inactive-site-effect", "site-spell",
                DaggerfallActorIdentity.PlayerEntityId, dynamicActor, "classic", "magic", null, 1, 5, state.RootElement.Clone()));
            _ = session.State.Effects.Start(new DaggerfallEffectRequest("inactive-caster-effect", "inactive-site-effect", "site-spell",
                dynamicActor, DaggerfallActorIdentity.PlayerEntityId, "classic", "magic", null, 1, 5, state.RootElement.Clone()));
            Assert.Equal(2, session.State.Effects.Active.Count);
            Assert.True(session.TryTransitionTo(destination.ProfileKey));
            DaggerfallActiveEffect retained = Assert.Single(session.State.Effects.Active);
            Assert.Equal("inactive-caster-effect", retained.Context.Instance.Value);
            Assert.Equal(dynamicActor, checked((long)retained.Context.Caster!.Value.Value));
            Assert.False(session.State.Actors.TryGet(dynamicActor, out _));
            save = session.CaptureSave();
        }

        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(save);
        DaggerfallSiteDeltaSave sourceDelta = Assert.Single(captured.SiteDeltas);
        Assert.Equal(dynamicActor, Assert.Single(sourceDelta.Effects).TargetId);
        DaggerfallActiveEffectSave retainedSave = Assert.Single(captured.ActiveEffects);
        Assert.Equal("inactive-caster-effect", retainedSave.Instance);
        Assert.Equal<long?>(dynamicActor, retainedSave.CasterId);
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, retainedSave.TargetId);

        List<string> resumedReleases = [];
        ContentFake resumedContent = new(resumedReleases);
        PopulateContent(resumedContent, source);
        PopulateContent(resumedContent, destination);
        SpatialFake resumedSpatial = SpatialFake.Create(destination.SpatialArtifact.Sha256, resumedReleases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(resumedReleases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(admitted, new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, source, DaggerfallTuning.Defaults, identity) { Effects = catalog, Profiles = profiles }, save);

        DaggerfallActiveEffect restoredRetained = Assert.Single(restored.State.Effects.Active);
        Assert.Equal(dynamicActor, checked((long)restoredRetained.Context.Caster!.Value.Value));
        Assert.True(restored.TryTransitionTo(source.ProfileKey));
        Assert.Equal(2, restored.State.Effects.Active.Count);
        DaggerfallActiveEffect effect = Assert.Single(restored.State.Effects.Active, value => value.Context.Instance.Value == "inactive-target-effect");
        Assert.Equal(dynamicActor, checked((long)effect.Context.Target.Value));
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, checked((long)effect.Context.Caster!.Value.Value));
        ActorState actor = restored.State.Actors.Get(dynamicActor);
        Assert.Contains(actor.Stats.GetStat(StatId.Parse("health-maximum")).Sources,
            source => source.Identity is EffectSourceIdentity effectSource && effectSource.Effect.Value == "inactive-target-effect");

        static DaggerfallEffectCatalog EffectCatalog() => new(
        [new DaggerfallEffectDefinition("inactive-site-effect", "inactive-site-effect", DaggerfallEffectStacking.Stack, 1, 1,
            effect =>
            {
                Stat maximum = effect.Target.Get<StatsComponent>().GetStat(StatId.Parse("health-maximum"));
                EffectSourceIdentity identity = new(effect.Target.Entity, effect.Context.Instance, 1,
                    SourceDefinitionId.Parse("daggerfall.inactive-site-effect.health"));
                maximum.SetSources(StatId.Parse("health-maximum"), maximum.Sources.Append(new StatSource(identity,
                    SourceDefinitionId.Parse("daggerfall.inactive-site-effect.health"), 0,
                    [new StatContributionDefinition(StatId.Parse("health-maximum"), StackingGroupId.Parse("daggerfall.inactive-site-effect.health"),
                        MechanicsStackingPolicy.Sum, new StatContribution.Add(5))])));
                return [new DelegateActiveEffectContribution(() => maximum.RemoveSource(identity))];
            },
            Resume: effect =>
            {
                Stat maximum = effect.Target.Get<StatsComponent>().GetStat(StatId.Parse("health-maximum"));
                EffectSourceIdentity identity = new(effect.Target.Entity, effect.Context.Instance, 1,
                    SourceDefinitionId.Parse("daggerfall.inactive-site-effect.health"));
                return [new DelegateActiveEffectContribution(() => maximum.RemoveSource(identity))];
            })]);
    }


    [Fact]
    public void Rejected_site_admission_keeps_the_source_world_and_actors_live()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSiteProfile destination = DaggerfallSiteContent.Read(FullContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, destination);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults));
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([source, destination]));
        long sourceActor = source.Project.Actors.Keys.First();
        spatial.RejectContentReplacement = true;

        Assert.Throws<InvalidOperationException>(() => session.TryTransitionTo(destination.ProfileKey));
        Assert.Equal(source.Site, session.Site.Active);
        Assert.True(session.State.Actors.TryGet(sourceActor, out _));
        Assert.Equal(2, spatial.ReplaceCalls);
    }

    [Fact]
    public void A_post_relocation_camera_rejection_restores_source_context_pose_and_save_state()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        ProductContent admitted = FullContent(root);
        DaggerfallSiteProfile destination = DaggerfallSiteContent.Read(admitted,
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, destination);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults));
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([source, destination]));
        long sourceActor = source.Project.Actors.Keys.First();
        WorldPoint sourcePosition = new(8f, 2f, -4f);
        session.State.PlayerControl.MoveTo(sourcePosition.ToVector());
        session.State.PlayerControl.YawRadians = .3f;
        session.State.PlayerControl.PitchRadians = -.1f;
        engine.FailNextCameraUpdate();

        Assert.Throws<InvalidOperationException>(() => session.TryRelocate(new DaggerfallRelocationDestination(destination.ProfileKey, "start")));

        Assert.Equal(source.Site, session.Site.Active);
        Assert.Equal(sourcePosition, session.State.PlayerControl.Position);
        Assert.Equal(.3f, session.State.PlayerControl.YawRadians);
        Assert.Equal(-.1f, session.State.PlayerControl.PitchRadians);
        Assert.True(session.State.Actors.TryGet(sourceActor, out _));
        Assert.Equal(3, spatial.ReplaceCalls);
        DaggerfallSiteSave savedSite = DaggerfallSavePayload.Read(session.CaptureSave()).Site;
        Assert.Equal(source.ProfileKey, savedSite.ActiveProfile!.Require());
        Assert.Null(savedSite.ReturnProfile);
        Assert.Empty(DaggerfallSavePayload.Read(session.CaptureSave()).SiteDeltas);
    }

    [Fact]
    public void Named_relocation_resets_control_state_preserves_player_and_round_trips_through_save()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        ProductContent admitted = FullContent(root);
        DaggerfallSiteProfile destination = DaggerfallSiteContent.Read(admitted,
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        DaggerfallSiteProfiles profiles = new([source, destination]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, destination);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        RulesetSavePayload save;
        long relocatedActor;

        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults)))
        {
            session.AdmitSiteProfiles(profiles);
            long authoredActor = source.Project.Actors.Keys.First();
            Assert.True(session.TryRelocate(new DaggerfallRelocationDestination(source.ProfileKey, "start", authoredActor)));
            Assert.Equal(source.Project.PlayerPosition, session.State.Actors.Get(authoredActor).Position);
            relocatedActor = session.SpawnActor("rat", new ActorPose(new WorldPoint(3f, 0f, 3f), 0f));
            Assert.True(session.TryRelocate(new DaggerfallRelocationDestination(source.ProfileKey, "start", relocatedActor)));
            Assert.Equal(source.Project.PlayerPosition, session.State.Actors.Get(relocatedActor).Position);
            EntityId player = session.State.Actors.Player.Actor.Entity;
            session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));

            Assert.True(session.TryRelocate(new DaggerfallRelocationDestination(source.ProfileKey, "start")));
            Assert.Equal(source.Project.PlayerPosition, session.State.PlayerControl.Position);
            Assert.Equal(source.InitialLook.YawRadians, session.State.PlayerControl.YawRadians);
            Assert.Equal(source.InitialLook.PitchRadians, session.State.PlayerControl.PitchRadians);
            Assert.Equal(default, session.State.PlayerControl.Motion);
            Assert.Equal(default, session.State.PlayerControl.Ground);
            session.Update(new ProductUpdate(OuterUpdate(2), []));
            Assert.Equal(Vector2.Zero, spatial.StepRequests.Last().Command.PlanarIntent);
            WorldPoint sourceReturnPosition = session.State.PlayerControl.Position!.Value;

            // An actor cannot be sent into a site the player is not in; the refusal changes nothing.
            Assert.Throws<InvalidOperationException>(() => session.TryRelocate(new DaggerfallRelocationDestination(destination.ProfileKey, "start", relocatedActor)));
            Assert.Equal(source.Site, session.Site.Active);
            Assert.Equal(sourceReturnPosition, session.State.PlayerControl.Position);
            Assert.Equal(source.Project.PlayerPosition, session.State.Actors.Get(relocatedActor).Position);
            Assert.True(session.TryRelocate(new DaggerfallRelocationDestination(destination.ProfileKey, "start")));
            Assert.Equal(destination.Site, session.Site.Active);
            Assert.False(session.State.Actors.TryGet(relocatedActor, out _));
            Assert.Equal(destination.Project.PlayerPosition, session.State.PlayerControl.Position);
            Assert.Equal(destination.InitialLook.YawRadians, session.State.PlayerControl.YawRadians);
            Assert.Equal(destination.InitialLook.PitchRadians, session.State.PlayerControl.PitchRadians);
            Assert.Equal(player, session.State.Actors.Player.Actor.Entity);
            Assert.Equal(2, spatial.ReplaceCalls);

            WorldPoint? positionBeforeInvalid = session.State.PlayerControl.Position;
            Assert.Throws<InvalidOperationException>(() => session.TryRelocate(new DaggerfallRelocationDestination(destination.ProfileKey, "missing")));
            Assert.Equal(positionBeforeInvalid, session.State.PlayerControl.Position);
            Assert.Equal(player, session.State.Actors.Player.Actor.Entity);
            Assert.Equal(2, spatial.ReplaceCalls);
            DaggerfallSiteReturnPoseSave savedReturnPose = DaggerfallSavePayload.Read(session.CaptureSave()).Site.ReturnPose!;
            Assert.Equal(sourceReturnPosition, new WorldPoint(savedReturnPose.X, savedReturnPose.Y, savedReturnPose.Z));
            save = session.CaptureSave();
        }

        List<string> resumedReleases = [];
        ContentFake resumedContent = new(resumedReleases);
        PopulateContent(resumedContent, source);
        PopulateContent(resumedContent, destination);
        SpatialFake resumedSpatial = SpatialFake.Create(destination.SpatialArtifact.Sha256, resumedReleases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(resumedReleases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(admitted, new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, source, DaggerfallTuning.Defaults, identity) { Profiles = profiles }, save);

        Assert.Equal(destination.Site, restored.Site.Active);
        Assert.Equal(destination.Project.PlayerPosition, restored.State.PlayerControl.Position);
        Assert.True(restored.TryTransitionTo(source.ProfileKey));
        Assert.Equal(source.Site, restored.Site.Active);
        // The spawned actor stayed in the source site's delta through the save and comes back with it.
        Assert.Equal(source.Project.PlayerPosition, restored.State.Actors.Get(relocatedActor).Position);
        DaggerfallSiteReturnPoseSave restoredReturnPose = DaggerfallSavePayload.Read(save).Site.ReturnPose!;
        Assert.Equal(new WorldPoint(restoredReturnPose.X, restoredReturnPose.Y, restoredReturnPose.Z), restored.State.PlayerControl.Position);
        Assert.Equal(2, resumedSpatial.ReplaceCalls);
    }

    [Fact]
    public void Save_restore_of_an_active_destination_retains_the_unloaded_return_site()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        ProductContent admitted = FullContent(root);
        DaggerfallSiteProfile destination = DaggerfallSiteContent.Read(admitted,
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        DaggerfallSiteProfiles profiles = new([source, destination]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, destination);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        RulesetSavePayload save;
        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults)))
        {
            session.AdmitSiteProfiles(profiles);
            session.State.PlayerControl.MoveTo(new Vector3(8f, 2f, -4f));
            session.State.PlayerControl.YawRadians = .3f;
            session.State.PlayerControl.PitchRadians = -.1f;
            Assert.True(session.TryTransitionTo(destination.ProfileKey));
            save = session.CaptureSave();
        }

        List<string> resumedReleases = [];
        ContentFake resumedContent = new(resumedReleases);
        PopulateContent(resumedContent, source);
        PopulateContent(resumedContent, destination);
        SpatialFake resumedSpatial = SpatialFake.Create(destination.SpatialArtifact.Sha256, resumedReleases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(resumedReleases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(admitted, new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, source, DaggerfallTuning.Defaults, identity) { Profiles = profiles }, save);

        Assert.Equal(destination.Site, restored.Site.Active);
        Assert.True(restored.TryTransitionTo(source.ProfileKey));
        Assert.Equal(source.Site, restored.Site.Active);
        Assert.Equal(new WorldPoint(8f, 2f, -4f), restored.State.PlayerControl.Position);
        Assert.Equal(.3f, restored.State.PlayerControl.YawRadians);
        Assert.Equal(-.1f, restored.State.PlayerControl.PitchRadians);
    }

    private static PerceptionReadoutResult PortalReceipt(PerceptionQueryRequest request, DaggerfallSitePortal portal) =>
        Receipt([.. request.Targets.Span.ToArray().Select(target => new PerceptionPair(
            1,
            target.Entity,
            1d,
            1d,
            target.Center == portal.Position.ToVector() ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded,
            1d))]);
}
