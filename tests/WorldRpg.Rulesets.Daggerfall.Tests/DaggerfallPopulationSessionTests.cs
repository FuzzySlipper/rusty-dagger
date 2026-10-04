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
    public void Resident_population_sync_keeps_the_owner_profile_pose_through_unload_and_readmission()
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
        Assert.Equal(authoredPose.X, residentNpc.X!.Value);
        Assert.Equal(authoredPose.Y, residentNpc.Y!.Value);
        Assert.Equal(authoredPose.Z, residentNpc.Z!.Value);
        admitted!.ApplyPose(new ActorPose(session.Sites.ProfileToLocal(authoredPose), admitted.HeadingYawRadians));

        // The resident actor remains live while the active source changes. Sync must use its
        // owning profile cell instead of the active origin compensation alone.
        Assert.True(session.TryTransitionTo(source.ProfileKey));
        DaggerfallNpc synced = session.State.Npcs.Require(residentId);
        Assert.Equal(authoredPose.X, synced.X!.Value);
        Assert.Equal(authoredPose.Y, synced.Y!.Value);
        Assert.Equal(authoredPose.Z, synced.Z!.Value);
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        DaggerfallNpcEntry savedNpc = Assert.Single(saved.Npcs.Entries,
            entry => entry.DurableId == residentId);
        Assert.Equal(authoredPose.X, savedNpc.X!.Value);
        Assert.Equal(authoredPose.Y, savedNpc.Y!.Value);
        Assert.Equal(authoredPose.Z, savedNpc.Z!.Value);

        // Retire and re-admit the resident closure. The authored profile pose must receive the
        // cell translation exactly once when its actor becomes live again.
        Assert.True(session.TryTransitionTo(interior.ProfileKey));
        Assert.True(session.TryTransitionTo(source.ProfileKey));
        Assert.True(session.State.Actors.TryGet(residentId, out ActorState? restored));
        Vector3 expected = authoredPose.ToVector()
            + session.Sites.ExteriorProfileFrameTranslation(resident.ProfileKey);
        Assert.Equal(expected.X, restored!.Position.X, 3);
        Assert.Equal(expected.Y, restored.Position.Y, 3);
        Assert.Equal(expected.Z, restored.Position.Z, 3);
        DaggerfallNpc restoredNpc = session.State.Npcs.Require(residentId);
        Assert.Equal(authoredPose.X, restoredNpc.X!.Value);
        Assert.Equal(authoredPose.Y, restoredNpc.Y!.Value);
        Assert.Equal(authoredPose.Z, restoredNpc.Z!.Value);
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
            exterior.SpatialArtifact,
            exterior.StaticMesh,
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
            exterior.AudioBundle,
            exterior.QuestMarkers,
            billboardSprites: billboardSprites,
            // SameContentAt intentionally trims optional content for generic fixtures. Keep the
            // source publication's normalized terrain catalog for the lifecycle's real wilderness
            // tilemap instead of turning this population profile into an empty-media test double.
            terrainTextures: source.TerrainTextures,
            population: population);
    }
}
