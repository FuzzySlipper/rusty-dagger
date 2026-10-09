using System.Numerics;
using Daggerfall.Import.Normalized;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallPopulationSessionTests
{
    [Fact]
    public void Resident_source_civilian_readds_its_billboard_to_the_retained_projection()
    {
        using ResidentPopulationFixture fixture = ResidentPopulationFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(8 * 60 * 60);

        DaggerfallNpc sourceNpc = Assert.Single(session.State.Npcs.All,
            npc => npc.StableKey == "population/source/0/0" && npc.Profile == fixture.Source.ProfileKey);
        int before = PopulationTextureOpens(fixture.Appearance);
        Assert.True(before > 0);

        Assert.True(session.TryTransitionTo(fixture.Resident.ProfileKey));

        DaggerfallNpc retained = session.State.Npcs.Require(sourceNpc.DurableId);
        Assert.Equal(fixture.Source.ProfileKey, retained.Profile);
        Assert.True(session.State.Actors.TryGet(sourceNpc.DurableId, out _));
        Assert.True(PopulationTextureOpens(fixture.Appearance) > before);
    }

    [Fact]
    public void Resident_defeated_source_civilian_rebuilds_billboard_before_corpse_sync_and_save_restore()
    {
        using ResidentPopulationFixture fixture = ResidentPopulationFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc sourceNpc = Assert.Single(session.State.Npcs.All,
            npc => npc.StableKey == "population/source/0/0" && npc.Profile == fixture.Source.ProfileKey);
        Assert.True(session.State.Actors.TryGet(sourceNpc.DurableId, out ActorState? sourceActor));
        sourceActor!.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60;
        for (ulong step = 1; step < 16 && !sourceActor.IsDefeated; step++)
            session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, sourceNpc.DurableId, 1, step, .125));
        Assert.True(sourceActor.IsDefeated);
        Assert.True(session.Corpses.ContainsKey(sourceNpc.DurableId));
        int before = PopulationTextureOpens(fixture.Appearance);

        Assert.True(session.TryTransitionTo(fixture.Resident.ProfileKey));

        Assert.True(session.State.Actors.TryGet(sourceNpc.DurableId, out ActorState? residentActor));
        Assert.True(residentActor!.IsDefeated);
        Assert.True(session.Corpses.ContainsKey(sourceNpc.DurableId));
        Assert.True(PopulationTextureOpens(fixture.Appearance) > before);
        RulesetSavePayload save = session.CaptureSave();
        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(save);
        Assert.Contains(captured.SiteDeltas, delta => delta.Profile.Require() == fixture.Source.ProfileKey
            && delta.DynamicActors.Any(actor => actor.EntityId == sourceNpc.DurableId));
        DaggerfallNpcEntry savedNpc = Assert.Single(captured.Npcs.Entries,
            entry => entry.DurableId == sourceNpc.DurableId);
        Assert.Equal(fixture.Source.ProfileKey, savedNpc.Profile!.Require());

        (EngineContextFake restoredEngine, AppearanceFake restoredAppearance) = fixture.CreateEngine();
        using DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, fixture.Composition, save);
        DaggerfallNpc restoredNpc = restored.State.Npcs.Require(sourceNpc.DurableId);
        Assert.Equal(fixture.Source.ProfileKey, restoredNpc.Profile);
        Assert.True(restored.State.Actors.TryGet(sourceNpc.DurableId, out ActorState? restoredActor));
        Assert.True(restoredActor!.IsDefeated);
        Assert.True(restored.Corpses.ContainsKey(sourceNpc.DurableId));
        Assert.True(PopulationTextureOpens(restoredAppearance) > 0);
    }

    [Fact]
    public void Resident_hidden_source_civilian_keeps_actor_identity_without_billboard_through_readmission_and_save_restore()
    {
        using ResidentPopulationFixture fixture = ResidentPopulationFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc sourceNpc = Assert.Single(session.State.Npcs.All,
            npc => npc.StableKey == "population/source/0/0" && npc.Profile == fixture.Source.ProfileKey);
        long sourceId = sourceNpc.DurableId;
        Assert.True(session.State.Actors.TryGet(sourceId, out _));
        int beforeHide = PopulationTextureOpens(fixture.Appearance);
        Assert.True(beforeHide > 0);

        // Dusk hides the source-backed identity and retires only its admitted billboard. The
        // dynamic actor stays retained so its durable pose and identity can cross the site window.
        session.AdvanceElapsedTime(12 * 60 * 60);
        sourceNpc = session.State.Npcs.Require(sourceId);
        Assert.Equal(DaggerfallNpcPresence.Hidden, sourceNpc.Presence);
        Assert.True(session.State.Actors.TryGet(sourceId, out _));
        Assert.Equal(beforeHide, PopulationTextureOpens(fixture.Appearance));

        Assert.True(session.TryTransitionTo(fixture.Resident.ProfileKey));
        DaggerfallNpc retained = session.State.Npcs.Require(sourceId);
        Assert.Equal(DaggerfallNpcPresence.Hidden, retained.Presence);
        Assert.True(session.State.Actors.TryGet(sourceId, out _));
        Assert.Equal(beforeHide, PopulationTextureOpens(fixture.Appearance));

        RulesetSavePayload save = session.CaptureSave();
        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(save);
        DaggerfallNpcEntry savedNpc = Assert.Single(captured.Npcs.Entries, entry => entry.DurableId == sourceId);
        Assert.Equal((int)DaggerfallNpcPresence.Hidden, savedNpc.Presence);
        Assert.Contains(captured.SiteDeltas, delta => delta.Profile.Require() == fixture.Source.ProfileKey
            && delta.DynamicActors.Any(actor => actor.EntityId == sourceId));

        (EngineContextFake restoredEngine, AppearanceFake restoredAppearance) = fixture.CreateEngine();
        using DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, fixture.Composition, save);
        DaggerfallNpc restoredNpc = restored.State.Npcs.Require(sourceId);
        Assert.Equal(DaggerfallNpcPresence.Hidden, restoredNpc.Presence);
        Assert.True(restored.State.Actors.TryGet(sourceId, out _));
        Assert.Equal(0, PopulationTextureOpens(restoredAppearance));
    }

    [Fact]
    public void Restore_reconciles_civilians_from_the_restored_save_without_changing_their_registry()
    {
        using ResidentPopulationFixture fixture = ResidentPopulationFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Contains(saved.Npcs.Entries, entry => entry.Presence == (int)DaggerfallNpcPresence.Active);

        // Civilians are projected only after the save, its effects and its residency are restored:
        // the restored registry, identities and presences are exactly the saved ones.
        (EngineContextFake restoredEngine, _) = fixture.CreateEngine();
        using DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, fixture.Composition, session.CaptureSave());
        DaggerfallSavePayload again = DaggerfallSavePayload.Read(restored.CaptureSave());
        Assert.Equal(saved.Npcs.Entries.Select(entry => (entry.DurableId, entry.StableKey, entry.Presence)),
            again.Npcs.Entries.Select(entry => (entry.DurableId, entry.StableKey, entry.Presence)));
        Assert.Equal(saved.DynamicActors.Select(actor => actor.EntityId), again.DynamicActors.Select(actor => actor.EntityId));
    }

    [Fact]
    public void Resident_population_actor_owns_its_pose_through_save_unload_and_readmission()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord[] exteriorRecords = [.. definitions.Locations.Records.Where(record => record.Exterior is not null)];
        DaggerfallSiteRecord sourceRecord = exteriorRecords.First(record => exteriorRecords.Any(candidate =>
            candidate.Id != record.Id
            && (candidate.MapPixelX != record.MapPixelX || candidate.MapPixelY != record.MapPixelY)
            && Math.Abs(candidate.MapPixelX - record.MapPixelX) <= DaggerfallExteriorCellResidency.StreamingRadius
            && Math.Abs(candidate.MapPixelY - record.MapPixelY) <= DaggerfallExteriorCellResidency.StreamingRadius));
        DaggerfallSiteRecord residentRecord = exteriorRecords.First(record =>
            record.Id != sourceRecord.Id
            && (record.MapPixelX != sourceRecord.MapPixelX || record.MapPixelY != sourceRecord.MapPixelY)
            && Math.Abs(record.MapPixelX - sourceRecord.MapPixelX) <= DaggerfallExteriorCellResidency.StreamingRadius
            && Math.Abs(record.MapPixelY - sourceRecord.MapPixelY) <= DaggerfallExteriorCellResidency.StreamingRadius);
        DaggerfallSiteProfile template = ReadProfile(root, FullContent(root, "worldrpg/imports/charing"), definitions,
            "daggerfall.charing-exterior.json");
        DaggerfallSiteProfile source = PopulationProfile(template, sourceRecord.Id, "resident-population-source",
            includeAuthoredActors: false);
        DaggerfallSiteProfile resident = PopulationProfile(template, residentRecord.Id, "resident-population-neighbor",
            includeAuthoredActors: false);
        DaggerfallSiteProfile interior = SameContentAt(template, sourceRecord.Id,
            DaggerfallWorldProfileKind.Interior, "resident-population-interior");
        DaggerfallSiteProfiles profiles = new([source, resident, interior]);
        DaggerfallSessionComposition composition = new(definitions, source, DaggerfallTuning.Defaults)
        {
            Profiles = profiles,
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, resident);
        PopulateContent(content, interior);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        session.AdvanceElapsedTime(8 * 60 * 60);
        Assert.True(session.TryTransitionTo(resident.ProfileKey));

        DaggerfallNpc residentNpc = Assert.Single(session.State.Npcs.All,
            npc => npc.StableKey == "population/source/0/0" && npc.Profile == resident.ProfileKey);
        long residentId = residentNpc.DurableId;
        Assert.True(session.State.Actors.TryGet(residentId, out ActorState? admitted));
        WorldPoint authoredPose = new(1F, 1F, 1F);
        // The live actor's Transform is the only pose owner; the registry keeps identity and binding.
        Assert.Null(residentNpc.X);
        admitted!.ApplyPose(new ActorPose(session.Sites.ProfileToLocal(authoredPose), admitted.HeadingYawRadians));

        // The resident actor remains live while the active source changes. No copy is written into
        // the registry; the resident actor still owns its pose in its own cell's frame.
        Assert.True(session.TryTransitionTo(source.ProfileKey));
        DaggerfallNpc returnedNpc = session.State.Npcs.Require(residentId);
        Assert.Equal(resident.ProfileKey, returnedNpc.Profile);
        Assert.Null(returnedNpc.X);
        Assert.True(session.State.Actors.TryGet(residentId, out ActorState? live));
        Vector3 liveExpected = authoredPose.ToVector() + session.Sites.ExteriorProfileFrameTranslation(resident.ProfileKey);
        Assert.Equal(liveExpected.X, live!.Position.X, 3);
        Assert.Equal(liveExpected.Z, live.Position.Z, 3);

        // The save detaches the resident closure into its site delta through the one profile-frame
        // conversion; the NPC entry carries no second pose.
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        DaggerfallNpcEntry savedNpc = Assert.Single(saved.Npcs.Entries,
            entry => entry.DurableId == residentId);
        Assert.Null(savedNpc.X);
        Assert.Null(savedNpc.Y);
        Assert.Null(savedNpc.Z);
        DaggerfallDynamicActorSave savedActor = Assert.Single(saved.SiteDeltas
            .Where(delta => delta.Profile.LogicalId == resident.ProfileKey.LogicalId)
            .SelectMany(delta => delta.DynamicActors), actor => actor.EntityId == residentId);
        Assert.Equal(authoredPose.X, savedActor.X, 3);
        Assert.Equal(authoredPose.Y, savedActor.Y, 3);
        Assert.Equal(authoredPose.Z, savedActor.Z, 3);

        // Retire and re-admit the resident closure. The retained actor's profile pose must receive
        // the cell translation exactly once when its actor becomes live again.
        Assert.True(session.TryTransitionTo(interior.ProfileKey));
        Assert.False(session.State.Actors.TryGet(residentId, out _));
        Assert.Null(session.State.Npcs.Require(residentId).X);
        Assert.True(session.TryTransitionTo(source.ProfileKey));
        Assert.True(session.State.Actors.TryGet(residentId, out ActorState? restored));
        Vector3 expected = authoredPose.ToVector()
            + session.Sites.ExteriorProfileFrameTranslation(resident.ProfileKey);
        Assert.Equal(expected.X, restored!.Position.X, 3);
        Assert.Equal(expected.Y, restored.Position.Y, 3);
        Assert.Equal(expected.Z, restored.Position.Z, 3);
        DaggerfallNpc restoredNpc = session.State.Npcs.Require(residentId);
        Assert.Null(restoredNpc.X);
    }

    [Fact]
    public void Source_civilian_keeps_identity_corpse_and_pose_through_unload_and_save_restore()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord[] towns = [.. definitions.Locations.Records.Where(record => record.Exterior is not null).Take(2)];
        Assert.Equal(2, towns.Length);

        DaggerfallSiteProfile template = ReadProfile(root, FullContent(root, "worldrpg/imports/charing"), definitions,
            "daggerfall.charing-exterior.json");
        DaggerfallSiteProfile source = PopulationProfile(template, towns[0].Id, "population-source");
        DaggerfallSiteProfile destination = PopulationProfile(template, towns[1].Id, "population-destination",
            includeAuthoredActors: false, includePopulation: false);
        DaggerfallSessionComposition composition = new(definitions, source, DaggerfallTuning.Defaults)
        {
            Profiles = new DaggerfallSiteProfiles([source, destination]),
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, destination);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        // PopulationManager admits outdoor people only during the source daytime window; the
        // canonical new-game clock starts at midnight.
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc civilian = Assert.Single(session.State.Npcs.All.Where(npc => npc.StableKey == "population/source/0/0"));
        long id = civilian.DurableId;
        DaggerfallFactionDefinition sourceFaction = TestPayload.Definitions.Factions.Factions.Values.First(faction => faction.Id > 0
            && TestPayload.Definitions.Catalogs.Races.Any(race => race.DonorRaceId == faction.Race));
        Assert.Equal(DaggerfallSession.PopulationRole(sourceFaction), civilian.Role);
        Assert.True(session.State.Actors.TryGet(id, out ActorState? actor));
        Assert.Equal(session.Sites.ProfileToLocal(new WorldPoint(1F, 1F, 1F)), actor!.Position);

        actor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60;
        for (ulong step = 1; step < 16 && !actor.IsDefeated; step++)
            session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, id, 1, step, .125));
        Assert.True(actor.IsDefeated);
        Assert.True(session.Corpses.ContainsKey(id));

        Assert.True(session.TryTransitionTo(destination.ProfileKey));
        Assert.False(session.State.Actors.TryGet(id, out _));
        Assert.True(session.TryTransitionTo(source.ProfileKey));
        session.ReconcileNpcProjection();
        Assert.True(session.State.Actors.TryGet(id, out ActorState? returned));
        Assert.True(returned!.IsDefeated);
        Assert.True(session.Corpses.ContainsKey(id));

        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, saved);
        DaggerfallNpc restoredNpc = Assert.Single(restored.State.Npcs.All.Where(npc => npc.StableKey == "population/source/0/0"));
        Assert.Equal(id, restoredNpc.DurableId);
        Assert.True(restored.State.Actors.Get(id).IsDefeated);
        Assert.True(restored.Corpses.ContainsKey(id));
        Assert.Equal(returned.Position, restored.State.Actors.Get(id).Position);
    }

    private static DaggerfallSiteProfile PopulationProfile(DaggerfallSiteProfile source, DaggerfallSiteId site, string logicalId,
        bool includeAuthoredActors = true, bool includePopulation = true)
    {
        DaggerfallSiteProfile exterior = SameContentAt(source, site, DaggerfallWorldProfileKind.Exterior, logicalId);
        ProjectFacts project = includeAuthoredActors
            ? exterior.Project
            : new ProjectFacts(exterior.Project.PlayerPosition, new Dictionary<long, AuthoredActor>());
        NormalizedBillboardSprite billboard = new("sprite/population.png", Hash, 8, 8,
            [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new Vector2(.5F, 0F), Vector2.One);
        IReadOnlyList<DaggerfallPopulationPlacement> population = includePopulation
            ? [new DaggerfallPopulationPlacement("population/source/0/0", new WorldPoint(1F, 1F, 1F), 210, 4,
                TestPayload.Definitions.Factions.Factions.Values.First(faction => faction.Id > 0
                    && TestPayload.Definitions.Catalogs.Races.Any(race => race.DonorRaceId == faction.Race)).Id, 0, 7)]
            : [];
        Dictionary<(int Archive, int Record), NormalizedBillboardSprite> billboardSprites = source.BillboardSprites.ToDictionary();
        billboardSprites[(210, 4)] = billboard;
        return new DaggerfallSiteProfile(
            project,
            exterior.Geometry,
            exterior.WorldAppearance,
            exterior.InitialLook,
            exterior.Materials,
            exterior.ActorSprites,
            exterior.MobileSprites,
            exterior.Audio,
            exterior.ClassicPresentation,
            exterior.Site,
            exterior.Doors,
            exterior.ProfileKind,
            exterior.ProfileKey.LogicalId,
            // Population lifecycle coverage does not exercise profile portals. Omitting the
            // source site's unrelated portal graph keeps each synthetic profile's entity closure
            // focused on civilian admission and avoids reusing portal identities across profiles.
            Array.Empty<DaggerfallSitePortal>(),
            exterior.Anchors.Values.ToArray(),
            exterior.Lights,
            exterior.GroundContainerSprite,
            exterior.DungeonMap,
            exterior.DungeonActions,
            exterior.DungeonActionModels,
            exterior.InteriorBuilding,
            exterior.Music,
            exterior.QuestMarkers,
            billboardSprites: billboardSprites,
            // SameContentAt intentionally trims optional content for generic fixtures. Keep the
            // source publication's normalized terrain catalog for the lifecycle's real wilderness
            // tilemap instead of turning this population profile into an empty-media test double.
            terrainTextures: source.TerrainTextures,
            population: population);
    }

    private static int PopulationTextureOpens(AppearanceFake appearance) =>
        appearance.OpenResourceRequests.Count(request => request.Path == "sprite/population.png");

    private sealed class ResidentPopulationFixture : IDisposable
    {
        private ResidentPopulationFixture(DaggerfallSession session, DaggerfallSessionComposition composition,
            DaggerfallSiteProfile source, DaggerfallSiteProfile resident, DaggerfallSiteProfile interior,
            AppearanceFake appearance)
        {
            Session = session;
            Composition = composition;
            Source = source;
            Resident = resident;
            Interior = interior;
            Appearance = appearance;
        }

        internal DaggerfallSession Session { get; }
        internal DaggerfallSessionComposition Composition { get; }
        internal DaggerfallSiteProfile Source { get; }
        internal DaggerfallSiteProfile Resident { get; }
        internal DaggerfallSiteProfile Interior { get; }
        internal AppearanceFake Appearance { get; }

        internal static ResidentPopulationFixture Create()
        {
            string root = TestData.RepositoryRoot;
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            DaggerfallSiteRecord[] exteriorRecords = [.. definitions.Locations.Records.Where(record => record.Exterior is not null)];
            // Keep both real exterior sites within the current resident window after the normal
            // world-origin transition. A one-row map-pixel bound avoids making appearance coverage
            // depend on the separate terrain-origin alignment owner.
            DaggerfallSiteRecord sourceRecord = exteriorRecords.First(record => exteriorRecords.Any(candidate =>
                candidate.Id != record.Id
                && Math.Abs(candidate.MapPixelX - record.MapPixelX) <= 1
                && Math.Abs(candidate.MapPixelY - record.MapPixelY) <= 1));
            DaggerfallSiteRecord residentRecord = exteriorRecords.First(record =>
                record.Id != sourceRecord.Id
                && Math.Abs(record.MapPixelX - sourceRecord.MapPixelX) <= 1
                && Math.Abs(record.MapPixelY - sourceRecord.MapPixelY) <= 1);
            DaggerfallSiteProfile template = ReadProfile(root, FullContent(root, "worldrpg/imports/charing"), definitions,
                "daggerfall.charing-exterior.json");
            DaggerfallSiteProfile source = PopulationProfile(template, sourceRecord.Id, "resident-population-source",
                includeAuthoredActors: false);
            DaggerfallSiteProfile resident = PopulationProfile(template, residentRecord.Id, "resident-population-neighbor",
                includeAuthoredActors: false, includePopulation: false);
            DaggerfallSiteProfile interior = SameContentAt(template, sourceRecord.Id,
                DaggerfallWorldProfileKind.Interior, "resident-population-interior");
            DaggerfallSiteProfiles profiles = new([source, resident, interior]);
            DaggerfallSessionComposition composition = new(definitions, source, DaggerfallTuning.Defaults)
            {
                Profiles = profiles,
            };
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, source);
            PopulateContent(content, resident);
            PopulateContent(content, interior);
            SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
            AppearanceFake appearance = new(releases);
            EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance);
            DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
            // StartNew admits the selected profile before the catalog is attached. Let the
            // ordinary first update reconcile the real neighboring exterior closure so these
            // tests exercise retained source actors through the same runtime path as the host.
            session.Update(new ProductUpdate(OuterUpdate(1), []));
            return new(session, composition, source, resident, interior, appearance);
        }

        internal (EngineContextFake Engine, AppearanceFake Appearance) CreateEngine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, Source);
            PopulateContent(content, Resident);
            PopulateContent(content, Interior);
            SpatialFake spatial = SpatialFake.Create(Source.SpatialArtifact.Sha256, releases);
            AppearanceFake appearance = new(releases);
            return (EngineContextFake.Create(content, spatial.Service, appearance), appearance);
        }

        public void Dispose() => Session.Dispose();
    }
}
