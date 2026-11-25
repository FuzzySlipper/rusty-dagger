using System.Numerics;
using System.Text.Json;
using Daggerfall.Import.Normalized;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Presentation;
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
    public void Source_civilian_draws_its_feet_on_its_pose_and_is_aimed_at_mid_body()
    {
        using ResidentPopulationFixture fixture = ResidentPopulationFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc sourceNpc = Assert.Single(session.State.Npcs.All,
            npc => npc.StableKey == "population/source/0/0" && npc.Profile == fixture.Source.ProfileKey);
        ActorState actor = session.State.Actors.Get(sourceNpc.DurableId);
        NormalizedBillboardSprite billboard = fixture.Source.BillboardSprites[(sourceNpc.Appearance.BillboardArchive, sourceNpc.Appearance.BillboardRecord)];

        // The billboard publishes a centred pivot; a pose is where the person stands, so a centred sprite would be half underground.
        Assert.Equal(new Vector2(.5F, .5F), billboard.Pivot);
        Assert.Equal(new Vector2(.5F, 0F), fixture.Appearance.SpriteRequests.Last(request => request.Size == billboard.Size).Pivot);
        Assert.Equal(actor.Position.ToVector() + Vector3.UnitY * (billboard.Size.Y * .5F), session.State.Kit.Targeting.AimPoint(actor));
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
            session.Strike(new ExplicitMeleeRequest(1, sourceNpc.DurableId, 1, step, .125));
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
        Assert.Contains(captured.ActorLedger, entry => entry.DurableId == sourceNpc.DurableId
            && entry.Location is { Kind: ActorLocationKind.Dead, Detached: true, Profile: { } p }
            && p.Require() == fixture.Source.ProfileKey);
        DaggerfallNpcEntry savedNpc = Assert.Single(captured.RestoredNpcs().Entries,
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
        DaggerfallNpcEntry savedNpc = Assert.Single(captured.RestoredNpcs().Entries, entry => entry.DurableId == sourceId);
        Assert.Equal((int)DaggerfallNpcPresence.Hidden, savedNpc.Presence);
        Assert.Contains(captured.ActorLedger, entry => entry.DurableId == sourceId
            && entry.Location is { Kind: ActorLocationKind.AtSite, Detached: true, Profile: { } p }
            && p.Require() == fixture.Source.ProfileKey);

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
        DaggerfallWorldProfileKey activeKey = saved.Site.ActiveProfile!.Require();
        // Untouched deterministic source population walkers write zero ActorLedger / ActorInventories bytes.
        Assert.Empty(saved.ActorLedger);
        Assert.Empty(saved.ActorInventories);

        // Civilians are projected only after the save, its effects and its residency are restored:
        // deterministic admission restores the exact same registry identities, appearances, and presences.
        (EngineContextFake restoredEngine, _) = fixture.CreateEngine();
        using DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, fixture.Composition, session.CaptureSave());
        DaggerfallSavePayload again = DaggerfallSavePayload.Read(restored.CaptureSave());
        Assert.Empty(again.ActorLedger);
        Assert.Equal(session.State.Npcs.All.OrderBy(n => n.DurableId).Select(entry => (entry.DurableId, entry.StableKey, entry.Presence)),
            restored.State.Npcs.All.OrderBy(n => n.DurableId).Select(entry => (entry.DurableId, entry.StableKey, entry.Presence)));
        Assert.Equal(saved.ActiveDynamicActors(activeKey).Select(actor => actor.EntityId),
            again.ActiveDynamicActors(activeKey).Select(actor => actor.EntityId));
        // Each townsperson comes back as who they were, so they wear the same townsperson art: their town's race, their gender and seed.
        Assert.Equal(session.State.Npcs.All.OrderBy(n => n.DurableId).Select(entry => (entry.DurableId, entry.Appearance.Race, entry.Appearance.Gender, entry.Appearance.NameSeed)),
            restored.State.Npcs.All.OrderBy(n => n.DurableId).Select(entry => (entry.DurableId, entry.Appearance.Race, entry.Appearance.Gender, entry.Appearance.NameSeed)));
        long[] townsfolk = [.. session.State.Npcs.All.Where(DaggerfallCivilians.IsSourcePopulation).Select(npc => npc.DurableId)];
        Assert.NotEmpty(townsfolk);
        Assert.All(townsfolk, id => Assert.NotNull(session.Population.Townsperson(id)));
        Assert.Equal(townsfolk.Select(session.Population.Townsperson), townsfolk.Select(restored.Population.Townsperson));
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
        DaggerfallSiteProfile template = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile source = PopulationProfile(template, sourceRecord.Id,
            includeAuthoredActors: false);
        DaggerfallSiteProfile resident = PopulationProfile(template, residentRecord.Id,
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
        SpatialFake spatial = SpatialFake.Create(source.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        session.AdvanceElapsedTime(8 * 60 * 60);
        Assert.True(session.TryTransitionTo(resident.ProfileKey));

        DaggerfallNpc residentNpc = Assert.Single(session.State.Npcs.All,
            npc => npc.StableKey == "population/source/0/0" && npc.Profile == resident.ProfileKey);
        long residentId = residentNpc.DurableId;
        Assert.True(session.State.Actors.TryGet(residentId, out ActorState? admitted));
        WorldPoint authoredPose = new(2F, 1F, 3F);
        // The live actor's Transform is the only pose owner; the registry keeps identity and binding.
        Assert.Null(residentNpc.X);
        admitted!.ApplyPose(new ActorPose(session.Exterior.ProfileToLocal(authoredPose), admitted.HeadingYawRadians));

        // The resident actor remains live while the active source changes. No copy is written into
        // the registry; the resident actor still owns its pose in its own cell's frame.
        Assert.True(session.TryTransitionTo(source.ProfileKey));
        DaggerfallNpc returnedNpc = session.State.Npcs.Require(residentId);
        Assert.Equal(resident.ProfileKey, returnedNpc.Profile);
        Assert.Null(returnedNpc.X);
        Assert.True(session.State.Actors.TryGet(residentId, out ActorState? live));
        Vector3 liveExpected = authoredPose.ToVector() + session.Exterior.ExteriorProfileFrameTranslation(resident.ProfileKey);
        Assert.Equal(liveExpected.X, live!.Position.X, 3);
        Assert.Equal(liveExpected.Z, live.Position.Z, 3);

        // The save detaches the resident closure into its site delta through the one profile-frame
        // conversion; the NPC entry carries no second pose.
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        DaggerfallNpcEntry savedNpc = Assert.Single(saved.RestoredNpcs().Entries,
            entry => entry.DurableId == residentId);
        Assert.Null(savedNpc.X);
        Assert.Null(savedNpc.Y);
        Assert.Null(savedNpc.Z);
        ActorLedgerEntry savedEntry = Assert.Single(saved.ActorLedger, entry => entry.DurableId == residentId
            && entry.Location is { Kind: ActorLocationKind.AtSite, Detached: true, Profile: { } p }
            && p.LogicalId == resident.ProfileKey.LogicalId);
        Assert.Equal(authoredPose.X, savedEntry.Pose!.X, 3);
        Assert.Equal(authoredPose.Y, savedEntry.Pose.Y, 3);
        Assert.Equal(authoredPose.Z, savedEntry.Pose.Z, 3);

        // Retire and re-admit the resident closure. The retained actor's profile pose must receive
        // the cell translation exactly once when its actor becomes live again.
        Assert.True(session.TryTransitionTo(interior.ProfileKey));
        Assert.False(session.State.Actors.TryGet(residentId, out _));
        Assert.Null(session.State.Npcs.Require(residentId).X);
        Assert.True(session.TryTransitionTo(source.ProfileKey));
        Assert.True(session.State.Actors.TryGet(residentId, out ActorState? restored));
        Vector3 expected = authoredPose.ToVector()
            + session.Exterior.ExteriorProfileFrameTranslation(resident.ProfileKey);
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

        DaggerfallSiteProfile template = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile source = PopulationProfile(template, towns[0].Id);
        DaggerfallSiteProfile destination = PopulationProfile(template, towns[1].Id,
            includeAuthoredActors: false, includePopulation: false);
        DaggerfallSessionComposition composition = new(definitions, source, DaggerfallTuning.Defaults)
        {
            Profiles = new DaggerfallSiteProfiles([source, destination]),
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, destination);
        SpatialFake spatial = SpatialFake.Create(source.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        // PopulationManager admits outdoor people only during the source daytime window; the
        // canonical new-game clock starts at midnight.
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc civilian = Assert.Single(session.State.Npcs.All.Where(npc => npc.StableKey == "population/source/0/0"));
        long id = civilian.DurableId;
        DaggerfallFactionDefinition sourceFaction = TestPayload.Definitions.Factions.Factions.Values.First(faction => faction.Id > 0
            && TestPayload.Definitions.Catalogs.Races.Any(race => race.DonorRaceId == faction.Race));
        Assert.Equal(DaggerfallSourcePopulation.Role(sourceFaction), civilian.Role);
        Assert.True(session.State.Actors.TryGet(id, out ActorState? actor));
        Assert.Equal(session.Exterior.ProfileToLocal(new WorldPoint(1F, 1F, 1F)), actor!.Position);

        actor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60;
        for (ulong step = 1; step < 16 && !actor.IsDefeated; step++)
            session.Strike(new ExplicitMeleeRequest(1, id, 1, step, .125));
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

    [Theory]
    [InlineData(17, "Daggerfall", "breton")]
    [InlineData(20, "Sentinel", "redguard")]
    [InlineData(16, "Wrothgaria", "nord")]
    public void Untouched_resident_exterior_admits_daytime_population_with_climate_race_and_eligibility_before_reconcile(
        int region, string townName, string expectedRace)
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord residentRecord = definitions.Locations.Records.Single(record =>
            record.Id.Region == region && record.Name == townName && record.Exterior is not null);
        Assert.Equal(expectedRace,
            definitions.Grids.Climate.GetCell(residentRecord.MapPixelX + 1, residentRecord.MapPixelY).People,
            StringComparer.OrdinalIgnoreCase);

        DaggerfallSiteRecord sourceRecord = definitions.Locations.Records.First(candidate =>
            candidate.Exterior is not null
            && candidate.Id != residentRecord.Id
            && (candidate.MapPixelX != residentRecord.MapPixelX || candidate.MapPixelY != residentRecord.MapPixelY)
            && Math.Abs(candidate.MapPixelX - residentRecord.MapPixelX) <= DaggerfallExteriorCellResidency.StreamingRadius
            && Math.Abs(candidate.MapPixelY - residentRecord.MapPixelY) <= DaggerfallExteriorCellResidency.StreamingRadius);

        int validFactionId = definitions.Factions.Factions.Values.First(faction => faction.Id > 0
            && definitions.Catalogs.Races.Any(race => race.DonorRaceId == faction.Race)).Id;
        DaggerfallPopulationPlacement[] residentPlacements =
        [
            new("population/source/0/0", new WorldPoint(1F, 1F, 1F), 210, 4, validFactionId, 0x00, 1007),
            new("population/source/0/1", new WorldPoint(3F, 1F, 2F), 210, 4, validFactionId, 0x20, 1044),
            new("population/source/0/missing-billboard", new WorldPoint(5F, 1F, 2F), 999, 99, validFactionId, 0x00, 1081),
            new("population/source/0/missing-faction", new WorldPoint(6F, 1F, 2F), 210, 4, 999999, 0x00, 1118),
        ];

        DaggerfallSiteProfile template = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile source = PopulationProfile(template, sourceRecord.Id,
            includeAuthoredActors: false, includePopulation: false);
        DaggerfallSiteProfile resident = PopulationProfile(template, residentRecord.Id,
            includeAuthoredActors: false, customPopulation: residentPlacements);
        DaggerfallSiteProfile interior = SameContentAt(template, sourceRecord.Id,
            DaggerfallWorldProfileKind.Interior, "resident-climate-race-interior");

        DaggerfallSessionComposition composition = new(definitions, interior, DaggerfallTuning.Defaults)
        {
            Profiles = new DaggerfallSiteProfiles([interior, source, resident]),
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, interior);
        PopulateContent(content, source);
        PopulateContent(content, resident);
        SpatialFake spatial = SpatialFake.Create(source.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.AdvanceElapsedTime(8 * 60 * 60);

        // Transitioning into the neighboring exterior during daytime admits the untouched town as a resident
        // exterior without active-site DaggerfallSourcePopulation.Reconcile ever running on it.
        Assert.True(session.TryTransitionTo(source.ProfileKey));
        Assert.True(session.Exterior.HoldsResidentLocation(resident.ProfileKey));

        ActorPersonComponent[] residentPeople = [.. session.State.Actors.All
            .Select(actor => actor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? person) ? person : null)
            .Where(person => person is not null && person.Snapshot.Profile == resident.ProfileKey)!];

        // Only the two eligible placements are admitted; missing billboard and unknown faction entries are skipped.
        Assert.Equal(2, residentPeople.Length);
        DaggerfallRaceDefinition raceDefinition = definitions.Catalogs.RequireRace(expectedRace);
        Dictionary<string, (long Id, string? Name, DaggerfallNpcAppearance Appearance, string Role)> admittedBeforeReconcile = [];
        foreach (DaggerfallPopulationPlacement placement in residentPlacements.Take(2))
        {
            ActorPersonComponent person = Assert.Single(residentPeople, p => p.Snapshot.StableKey == placement.Id);
            string expectedGender = (placement.Flags & 0x20) != 0 ? "Female" : "Male";
            Assert.Equal(expectedRace, person.Snapshot.Appearance.Race);
            Assert.Equal(expectedGender, person.Snapshot.Appearance.Gender);
            Assert.Equal((int)placement.NameSeed, person.Snapshot.Appearance.NameSeed);
            Assert.False(string.IsNullOrWhiteSpace(person.Snapshot.DisplayName));
            Assert.Equal(
                new DaggerfallNames(definitions, engine.Context.Random).NpcName(person.Snapshot.Appearance, residentRecord.Id.Region),
                person.Snapshot.DisplayName);
            Assert.Equal(raceDefinition.Townsperson(expectedGender, placement.NameSeed),
                session.Population.Townsperson(person.Snapshot.DurableId));
            admittedBeforeReconcile[placement.Id] = (person.Snapshot.DurableId, person.Snapshot.DisplayName,
                person.Snapshot.Appearance, person.Snapshot.Role);
        }

        // Entering the resident town runs active-site reconciliation and keeps the exact same identities, races, and names.
        Assert.True(session.TryTransitionTo(resident.ProfileKey));
        foreach ((string stableKey, (long id, string? name, DaggerfallNpcAppearance appearance, string role)) in admittedBeforeReconcile)
        {
            DaggerfallNpc reconciled = session.State.Npcs.Require(id);
            Assert.Equal(stableKey, reconciled.StableKey);
            Assert.Equal(appearance, reconciled.Appearance);
            Assert.Equal(name, reconciled.DisplayName);
            Assert.Equal(role, reconciled.Role);
        }
    }

    [Fact]
    public void Untouched_resident_exterior_with_raceless_climate_admits_zero_population_during_daytime()
    {
        DaggerfallDefinitions sourceDefinitions = TestPayload.Definitions;
        DaggerfallSiteRecord[] exteriorRecords = [.. sourceDefinitions.Locations.Records.Where(record => record.Exterior is not null)];
        DaggerfallSiteRecord sourceRecord = exteriorRecords.First(record => exteriorRecords.Any(candidate =>
            candidate.Id != record.Id
            && (candidate.MapPixelX != record.MapPixelX || candidate.MapPixelY != record.MapPixelY)
            && Math.Abs(candidate.MapPixelX - record.MapPixelX) <= 1
            && Math.Abs(candidate.MapPixelY - record.MapPixelY) <= 1));
        DaggerfallSiteRecord residentRecord = exteriorRecords.First(record =>
            record.Id != sourceRecord.Id
            && (record.MapPixelX != sourceRecord.MapPixelX || record.MapPixelY != sourceRecord.MapPixelY)
            && Math.Abs(record.MapPixelX - sourceRecord.MapPixelX) <= 1
            && Math.Abs(record.MapPixelY - sourceRecord.MapPixelY) <= 1);

        // Clear the People race on the resident cell's climate value while keeping both sites in-bounds
        // and Catalogs.Races populated, so a fallback to Races.FirstOrDefault() would have minted townspeople.
        int residentClimateValue = sourceDefinitions.Grids.Climate.GetCell(residentRecord.MapPixelX + 1, residentRecord.MapPixelY).Value;
        DaggerfallClimateGridDefinition racelessClimate = sourceDefinitions.Grids.Climate with
        {
            Values = [.. sourceDefinitions.Grids.Climate.Values.Select(value =>
                value.Value == residentClimateValue ? value with { People = string.Empty } : value)],
        };
        DaggerfallWorldGridsSet racelessGrids = sourceDefinitions.Grids with { Climate = racelessClimate };
        DaggerfallLocationSet locations = sourceDefinitions.Locations with
        {
            Keys = [(sourceRecord.Id.Region, sourceRecord.Id.Index), (residentRecord.Id.Region, residentRecord.Id.Index)],
            Records = [sourceRecord, residentRecord],
        };
        DaggerfallDefinitions definitions = new DaggerfallDefinitions(
            sourceDefinitions.Catalogs,
            sourceDefinitions.Vocabulary,
            sourceDefinitions.Actors,
            sourceDefinitions.Items,
            sourceDefinitions.EquipmentSlots,
            sourceDefinitions.ArmorValuesByMaterial,
            sourceDefinitions.Actions,
            sourceDefinitions.LootTables,
            sourceDefinitions.HudResources,
            sourceDefinitions.CharacterPresentation,
            locations,
            sourceDefinitions.Text,
            sourceDefinitions.Magic,
            sourceDefinitions.Mobiles,
            sourceDefinitions.Names,
            sourceDefinitions.Rumors,
            sourceDefinitions.Biographies,
            racelessGrids,
            sourceDefinitions.Books,
            sourceDefinitions.Factions,
            sourceDefinitions.Terrain,
            sourceDefinitions.ItemTemplateCatalog,
            sourceDefinitions.QuestSources,
            sourceDefinitions.Cinematics,
            sourceDefinitions.Encounters)
        {
            EnemySpells = sourceDefinitions.EnemySpells,
            EnemyRanged = sourceDefinitions.EnemyRanged,
            NewGame = sourceDefinitions.NewGame,
            BuildingNames = sourceDefinitions.BuildingNames,
            DialogueWorldRules = sourceDefinitions.DialogueWorldRules,
        }.WithPeople(sourceDefinitions.People);
        Assert.Null(DaggerfallSourcePopulation.TryResolveClimateRace(definitions, residentRecord, out _));

        DaggerfallSiteProfile template = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile source = PopulationProfile(template, sourceRecord.Id,
            includeAuthoredActors: false, includePopulation: false);
        DaggerfallSiteProfile resident = PopulationProfile(template, residentRecord.Id,
            includeAuthoredActors: false, includePopulation: true);
        DaggerfallSiteProfile interior = SameContentAt(template, sourceRecord.Id,
            DaggerfallWorldProfileKind.Interior, "raceless-resident-interior");

        DaggerfallSessionComposition composition = new(definitions, interior, DaggerfallTuning.Defaults)
        {
            Profiles = new DaggerfallSiteProfiles([interior, source, resident]),
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, interior);
        PopulateContent(content, source);
        PopulateContent(content, resident);
        SpatialFake spatial = SpatialFake.Create(source.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.AdvanceElapsedTime(8 * 60 * 60);
        Assert.True(session.TryTransitionTo(source.ProfileKey));
        Assert.True(session.Exterior.HoldsResidentLocation(resident.ProfileKey));

        Assert.DoesNotContain(session.State.Actors.All, actor =>
            actor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? person)
            && person.Snapshot.Profile == resident.ProfileKey);
        Assert.DoesNotContain(session.State.Npcs.All, npc => npc.Profile == resident.ProfileKey);
    }

    [Fact]
    public void Seeded_townspeople_materialize_content_archetype_stats_and_persist_wounded_sparse_diff_with_zero_untouched_bytes()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord town = definitions.Locations.Records.First(record => record.Exterior is not null
            && DaggerfallSourcePopulation.TryResolveClimateRace(definitions, record, out _) is not null);
        int validFactionId = definitions.Factions.Factions.Values.First(faction => faction.Id > 0
            && definitions.Catalogs.Races.Any(race => race.DonorRaceId == faction.Race)).Id;
        DaggerfallPopulationPlacement[] placements =
        [
            new("population/source/0/0", new WorldPoint(1F, 1F, 1F), 210, 4, validFactionId, 0x00, 101),
            new("population/source/0/1", new WorldPoint(3F, 1F, 2F), 210, 4, validFactionId, 0x20, 202),
        ];
        DaggerfallSiteProfile template = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile source = PopulationProfile(template, town.Id,
            includeAuthoredActors: false, customPopulation: placements);

        DaggerfallSessionComposition composition = new(definitions, source, DaggerfallTuning.Defaults)
        {
            Profiles = new DaggerfallSiteProfiles([source]),
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        SpatialFake spatial = SpatialFake.Create(source.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.AdvanceElapsedTime(8 * 60 * 60);

        ArchetypeProfile civilian = definitions.People.RequireArchetype(DaggerfallArchetypeIds.Civilian);
        DaggerfallNpc[] walkers = [.. session.State.Npcs.All.Where(DaggerfallCivilians.IsSourcePopulation).OrderBy(n => n.StableKey, StringComparer.Ordinal)];
        Assert.Equal(2, walkers.Length);

        foreach (DaggerfallNpc walker in walkers)
        {
            ActorState actor = session.State.Actors.Get(walker.DurableId);
            Track health = actor.Stats.GetTrack(TrackId.Parse("health"));
            int expectedMax = civilian.RollDeterministicHealth(walker.DurableId);
            Assert.Equal(expectedMax, (int)health.Maximum.Value);
            Assert.Equal(expectedMax, (int)health.Current);
            Assert.True(expectedMax >= civilian.Health.Minimum && expectedMax <= civilian.Health.Maximum);
            Assert.Equal(civilian.Fatigue, (int)actor.Stats.GetTrack(TrackId.Parse("fatigue")).Maximum.Value);
            Assert.Equal(50, (int)actor.Stats.GetStat(StatId.Parse("personality")).BaseValue);
            Assert.Equal(25, (int)actor.Stats.GetStat(StatId.Parse("etiquette")).BaseValue);
            Assert.Equal(DaggerfallArchetypeIds.Civilian, actor.Actor.Get<ActorOverlayComponent>().ArchetypeId);
        }

        // Untouched baselines cost 0 ActorLedger and 0 ActorInventories bytes.
        DaggerfallSavePayload untouchedSave = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Empty(untouchedSave.ActorLedger);
        Assert.Empty(untouchedSave.ActorInventories);

        // Wound the first townsperson without killing them; leave the second untouched.
        long woundedId = walkers[0].DurableId;
        long untouchedId = walkers[1].DurableId;
        Track woundedHealth = session.State.Actors.Get(woundedId).Stats.GetTrack(TrackId.Parse("health"));
        int woundedMax = (int)woundedHealth.Maximum.Value;
        int woundedTarget = woundedMax - 6;
        Assert.True(woundedTarget > 1);
        woundedHealth.SetCurrent(woundedTarget, clamp: true);

        RulesetSavePayload saveWithWound = session.CaptureSave();
        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(saveWithWound);
        ActorLedgerEntry sparseEntry = Assert.Single(captured.ActorLedger);
        Assert.Equal(woundedId, sparseEntry.DurableId);
        Assert.NotNull(sparseEntry.Vitals);
        Assert.DoesNotContain(captured.ActorLedger, entry => entry.DurableId == untouchedId);
        Assert.Empty(captured.ActorInventories);

        // Reload restores the wounded townsperson's exact HP while untouched neighbors re-materialize at full baseline HP.
        List<string> restoreReleases = [];
        ContentFake restoreContent = new(restoreReleases);
        PopulateContent(restoreContent, source);
        SpatialFake restoreSpatial = SpatialFake.Create(source.Geometry.Spatial[0].Sha256, restoreReleases);
        EngineContextFake restoreEngine = EngineContextFake.Create(restoreContent, restoreSpatial.Service, new AppearanceFake(restoreReleases));
        using DaggerfallSession restored = DaggerfallSession.Restore(restoreEngine.Context, composition, saveWithWound);

        Track restoredWounded = restored.State.Actors.Get(woundedId).Stats.GetTrack(TrackId.Parse("health"));
        Assert.Equal(woundedMax, (int)restoredWounded.Maximum.Value);
        Assert.Equal(woundedTarget, (int)restoredWounded.Current);

        Track restoredUntouched = restored.State.Actors.Get(untouchedId).Stats.GetTrack(TrackId.Parse("health"));
        Assert.Equal(civilian.RollDeterministicHealth(untouchedId), (int)restoredUntouched.Current);
    }

    [Fact]
    public void Static_spawner_npc_materializes_spawner_archetype_stats_and_fungible_loadout_with_zero_untouched_save_bytes()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord town = definitions.Locations.Records.First(record => record.Exterior is not null
            && DaggerfallSourcePopulation.TryResolveClimateRace(definitions, record, out _) is not null);
        DaggerfallSiteProfile template = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        NormalizedBillboardSprite billboard = new("sprite/population.png", Hash, 8, 8,
            [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new Vector2(.5F, .5F), Vector2.One);
        Dictionary<(int Archive, int Record), NormalizedBillboardSprite> billboards = template.BillboardSprites.ToDictionary();
        billboards[(183, 5)] = billboard;

        DaggerfallStaticNpcPlacement banker = new(
            "person/bank/0/0/b-0-p00",
            new WorldPoint(2F, 1F, 2F),
            new DaggerfallNpcAppearance("breton", "Male", 183, 5, 42, 0),
            "bank teller",
            ["talk", "banking"],
            billboard.Standing((183, 5)),
            ActorInteractionPolicyKind.FullDialogue,
            ActorHarmPolicyKind.LawProtectedCivilian,
            SpawnerId: "banker");

        DaggerfallSiteProfile baseExterior = SameContentAt(template, town.Id, DaggerfallWorldProfileKind.Exterior,
            DaggerfallWorldProfileIds.Exterior(town.Id).LogicalId);
        DaggerfallSiteProfile site = new(
            new ProjectFacts(baseExterior.Project.PlayerPosition, new Dictionary<long, AuthoredActor>()),
            baseExterior.Geometry,
            baseExterior.WorldAppearance,
            baseExterior.InitialLook,
            baseExterior.Materials,
            baseExterior.ActorSprites,
            baseExterior.MobileSprites,
            baseExterior.Audio,
            baseExterior.ClassicPresentation,
            baseExterior.Site,
            baseExterior.Doors,
            baseExterior.ProfileKind,
            baseExterior.ProfileKey.LogicalId,
            Array.Empty<DaggerfallSitePortal>(),
            baseExterior.Anchors.Values.ToArray(),
            baseExterior.Lights,
            baseExterior.GroundContainerSprites,
            baseExterior.DungeonMap,
            baseExterior.DungeonActions,
            baseExterior.DungeonActionModels,
            baseExterior.InteriorBuilding,
            baseExterior.QuestMarkers,
            billboardSprites: billboards,
            terrainTextures: template.TerrainTextures,
            staticNpcs: [banker]);

        DaggerfallSessionComposition composition = new(definitions, site, DaggerfallTuning.Defaults)
        {
            Profiles = new DaggerfallSiteProfiles([site]),
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, site);
        SpatialFake spatial = SpatialFake.Create(site.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);

        ArchetypeProfile merchant = definitions.People.RequireArchetype("merchant");
        DaggerfallNpc bankerNpc = Assert.Single(session.State.Npcs.All, npc => npc.StableKey == banker.Id);
        ActorState actor = session.State.Actors.Get(bankerNpc.DurableId);
        Assert.Equal("merchant", actor.Actor.Get<ActorOverlayComponent>().ArchetypeId);
        Assert.Equal(merchant.RollDeterministicHealth(bankerNpc.DurableId), (int)actor.Stats.GetTrack(TrackId.Parse("health")).Maximum.Value);
        Assert.Equal(60, (int)actor.Stats.GetStat(StatId.Parse("personality")).BaseValue);
        Assert.Equal(55, (int)actor.Stats.GetStat(StatId.Parse("intelligence")).BaseValue);
        Assert.Equal(50, (int)actor.Stats.GetStat(StatId.Parse("mercantile")).BaseValue);

        // Merchant's default fungible loadout (gold-piece) is present live, yet costs 0 save bytes while untouched.
        var initialStack = Assert.Single(session.State.ActorInventories.InventoryFor(bankerNpc.DurableId)!.Read().Stacks);
        Assert.Equal("gold-piece", initialStack.Definition.Value);
        Assert.Equal(15UL, initialStack.Quantity);

        DaggerfallSavePayload untouchedSave = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Empty(untouchedSave.ActorLedger);
        Assert.Empty(untouchedSave.ActorInventories);

        // Consuming the merchant's default pocket gold writes a non-default inventory delta and stays empty across reload.
        session.State.ActorInventories.InventoryFor(bankerNpc.DurableId)!.Consume(new WorldRpg.Kit.Inventory.InventoryConsume(initialStack.Id, initialStack.Quantity));
        session.State.ItemInstances.RemoveStack(DaggerfallItemOwner.Actor(bankerNpc.DurableId), initialStack.Id);
        Assert.Empty(session.State.ActorInventories.InventoryFor(bankerNpc.DurableId)!.Read().Stacks);

        RulesetSavePayload lootedSave = session.CaptureSave();
        DaggerfallSavePayload lootedPayload = DaggerfallSavePayload.Read(lootedSave);
        DaggerfallActorInventorySave savedInv = Assert.Single(lootedPayload.ActorInventories, inv => inv.EntityId == bankerNpc.DurableId);
        Assert.Empty(savedInv.Inventory.Stacks);

        List<string> restoreReleases = [];
        ContentFake restoreContent = new(restoreReleases);
        PopulateContent(restoreContent, site);
        SpatialFake restoreSpatial = SpatialFake.Create(site.Geometry.Spatial[0].Sha256, restoreReleases);
        EngineContextFake restoreEngine = EngineContextFake.Create(restoreContent, restoreSpatial.Service, new AppearanceFake(restoreReleases));
        using DaggerfallSession restored = DaggerfallSession.Restore(restoreEngine.Context, composition, lootedSave);
        Assert.Empty(restored.State.ActorInventories.InventoryFor(bankerNpc.DurableId)!.Read().Stacks);
    }

    [Fact]
    public void Relocated_and_party_staged_spawner_npc_preserves_archetype_stats_policies_and_loadout_across_unload_and_save_reload()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord[] towns = [.. definitions.Locations.Records
            .Where(record => record.Exterior is not null
                && DaggerfallSourcePopulation.TryResolveClimateRace(definitions, record, out _) is not null)
            .Take(2)];
        Assert.Equal(2, towns.Length);

        DaggerfallSiteProfile template = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        NormalizedBillboardSprite billboard = new("sprite/population.png", Hash, 8, 8,
            [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new Vector2(.5F, .5F), Vector2.One);
        Dictionary<(int Archive, int Record), NormalizedBillboardSprite> billboards = template.BillboardSprites.ToDictionary();
        billboards[(183, 5)] = billboard;

        DaggerfallStaticNpcPlacement banker = new(
            "person/bank/0/0/b-0-p00",
            new WorldPoint(2F, 1F, 2F),
            new DaggerfallNpcAppearance("breton", "Male", 183, 5, 42, 0),
            "bank teller",
            ["talk", "banking"],
            billboard.Standing((183, 5)),
            ActorInteractionPolicyKind.AmbientBark,
            ActorHarmPolicyKind.LawProtectedCivilian,
            SpawnerId: "banker");

        DaggerfallSiteProfile BuildSite(DaggerfallSiteId siteId, IReadOnlyList<DaggerfallStaticNpcPlacement> statics)
        {
            DaggerfallSiteProfile baseExterior = SameContentAt(template, siteId, DaggerfallWorldProfileKind.Exterior,
                DaggerfallWorldProfileIds.Exterior(siteId).LogicalId);
            return new DaggerfallSiteProfile(
                new ProjectFacts(baseExterior.Project.PlayerPosition, new Dictionary<long, AuthoredActor>()),
                baseExterior.Geometry,
                baseExterior.WorldAppearance,
                baseExterior.InitialLook,
                baseExterior.Materials,
                baseExterior.ActorSprites,
                baseExterior.MobileSprites,
                baseExterior.Audio,
                baseExterior.ClassicPresentation,
                baseExterior.Site,
                baseExterior.Doors,
                baseExterior.ProfileKind,
                baseExterior.ProfileKey.LogicalId,
                Array.Empty<DaggerfallSitePortal>(),
                baseExterior.Anchors.Values.ToArray(),
                baseExterior.Lights,
                baseExterior.GroundContainerSprites,
                baseExterior.DungeonMap,
                baseExterior.DungeonActions,
                baseExterior.DungeonActionModels,
                baseExterior.InteriorBuilding,
                baseExterior.QuestMarkers,
                billboardSprites: billboards,
                terrainTextures: template.TerrainTextures,
                staticNpcs: statics);
        }

        DaggerfallSiteProfile homeSite = BuildSite(towns[0].Id, [banker]);
        DaggerfallSiteProfile awaySite = BuildSite(towns[1].Id, []);

        DaggerfallSessionComposition composition = new(definitions, homeSite, DaggerfallTuning.Defaults)
        {
            Profiles = new DaggerfallSiteProfiles([homeSite, awaySite]),
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, homeSite);
        PopulateContent(content, awaySite);
        SpatialFake spatial = SpatialFake.Create(homeSite.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);

        ArchetypeProfile merchant = definitions.People.RequireArchetype("merchant");
        DaggerfallNpc bankerNpc = Assert.Single(session.State.Npcs.All, npc => npc.StableKey == banker.Id);
        long bankerId = bankerNpc.DurableId;
        int expectedMaxHp = merchant.RollDeterministicHealth(bankerId);

        // 1. Party-staging path (MaterializeLedgerActorAt): join WithParty (sparse capture + unload), travel to awaySite, stage into 3D.
        session.OverlandParties.JoinParty(bankerId, ActorPartyRole.Companion);
        Assert.False(session.State.Actors.TryGet(bankerId, out _));
        Assert.True(session.TryTransitionTo(awaySite.ProfileKey));

        IReadOnlyList<long> stagedIds = session.OverlandParties.StagePartyMembers(awaySite, new WorldPoint(4f, 1f, 4f), 0f);
        Assert.Equal([bankerId], stagedIds);
        ActorState staged = session.State.Actors.Get(bankerId);
        Assert.Equal("merchant", staged.Actor.Get<ActorOverlayComponent>().ArchetypeId);
        Assert.Equal(ActorHarmPolicyKind.LawProtectedCivilian, staged.Actor.Get<ActorHarmPolicy>().Policy);
        Assert.Equal(ActorInteractionPolicyKind.AmbientBark, staged.Actor.Get<ActorInteractionPolicy>().Policy);
        Assert.Equal(expectedMaxHp, (int)staged.Stats.GetTrack(TrackId.Parse("health")).Maximum.Value);
        Assert.Equal(expectedMaxHp, (int)staged.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.Equal(60, (int)staged.Stats.GetStat(StatId.Parse("personality")).BaseValue);
        Assert.Equal(55, (int)staged.Stats.GetStat(StatId.Parse("intelligence")).BaseValue);
        Assert.Equal(50, (int)staged.Stats.GetStat(StatId.Parse("mercantile")).BaseValue);
        var stagedStack = Assert.Single(session.State.ActorInventories.InventoryFor(bankerId)!.Read().Stacks);
        Assert.Equal("gold-piece", stagedStack.Definition.Value);
        Assert.Equal(15UL, stagedStack.Quantity);

        // 2. Relocated site-admission path (MaterializeRelocatedLedgerActors across save/reload):
        // wound the relocated banker at awaySite, save & reload into awaySite.
        int woundedHp = expectedMaxHp - 5;
        staged.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(woundedHp, clamp: true);

        RulesetSavePayload relocatedSave = session.CaptureSave();
        DaggerfallSavePayload capturedPayload = DaggerfallSavePayload.Read(relocatedSave);
        ActorLedgerEntry savedEntry = Assert.Single(capturedPayload.ActorLedger, e => e.DurableId == bankerId);
        Assert.Equal("banker", savedEntry.Origin.SpawnerId);
        Assert.Equal(ActorHarmPolicyKind.LawProtectedCivilian, savedEntry.Origin.HarmPolicy);
        Assert.Equal(ActorInteractionPolicyKind.AmbientBark, savedEntry.Origin.InteractionPolicy);
        // Untouched baseline loadout still stays 0 bytes in ActorInventories even after relocation.
        Assert.DoesNotContain(capturedPayload.ActorInventories, inv => inv.EntityId == bankerId);

        List<string> restoreReleases = [];
        ContentFake restoreContent = new(restoreReleases);
        PopulateContent(restoreContent, homeSite);
        PopulateContent(restoreContent, awaySite);
        SpatialFake restoreSpatial = SpatialFake.Create(homeSite.Geometry.Spatial[0].Sha256, restoreReleases);
        EngineContextFake restoreEngine = EngineContextFake.Create(restoreContent, restoreSpatial.Service, new AppearanceFake(restoreReleases));
        using DaggerfallSession restored = DaggerfallSession.Restore(restoreEngine.Context, composition, relocatedSave);

        ActorState relocated = restored.State.Actors.Get(bankerId);
        Assert.Equal("merchant", relocated.Actor.Get<ActorOverlayComponent>().ArchetypeId);
        Assert.Equal(ActorHarmPolicyKind.LawProtectedCivilian, relocated.Actor.Get<ActorHarmPolicy>().Policy);
        Assert.Equal(ActorInteractionPolicyKind.AmbientBark, relocated.Actor.Get<ActorInteractionPolicy>().Policy);
        Assert.Equal(expectedMaxHp, (int)relocated.Stats.GetTrack(TrackId.Parse("health")).Maximum.Value);
        Assert.Equal(woundedHp, (int)relocated.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.Equal(50, (int)relocated.Stats.GetStat(StatId.Parse("mercantile")).BaseValue);
        var relocatedStack = Assert.Single(restored.State.ActorInventories.InventoryFor(bankerId)!.Read().Stacks);
        Assert.Equal("gold-piece", relocatedStack.Definition.Value);
        Assert.Equal(15UL, relocatedStack.Quantity);
    }

    private static DaggerfallSiteProfile PopulationProfile(DaggerfallSiteProfile source, DaggerfallSiteId site,
        bool includeAuthoredActors = true, bool includePopulation = true,
        IReadOnlyList<DaggerfallPopulationPlacement>? customPopulation = null)
    {
        // An exterior is named by its location, as the window streams it.
        DaggerfallSiteProfile exterior = SameContentAt(source, site, DaggerfallWorldProfileKind.Exterior, DaggerfallWorldProfileIds.Exterior(site).LogicalId);
        ProjectFacts project = includeAuthoredActors
            ? exterior.Project
            : new ProjectFacts(exterior.Project.PlayerPosition, new Dictionary<long, AuthoredActor>());
        NormalizedBillboardSprite billboard = new("sprite/population.png", Hash, 8, 8,
            [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new Vector2(.5F, .5F), Vector2.One);
        IReadOnlyList<DaggerfallPopulationPlacement> population = customPopulation
            ?? (includePopulation
                ? [new DaggerfallPopulationPlacement("population/source/0/0", new WorldPoint(1F, 1F, 1F), 210, 4,
                    TestPayload.Definitions.Factions.Factions.Values.First(faction => faction.Id > 0
                        && TestPayload.Definitions.Catalogs.Races.Any(race => race.DonorRaceId == faction.Race)).Id, 0, 7)]
                : []);
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
            exterior.GroundContainerSprites,
            exterior.DungeonMap,
            exterior.DungeonActions,
            exterior.DungeonActionModels,
            exterior.InteriorBuilding,
            exterior.QuestMarkers,
            billboardSprites: billboardSprites,
            // SameContentAt intentionally trims optional content for generic fixtures. Keep the
            // source publication's normalized terrain catalog for the lifecycle's real wilderness
            // tilemap instead of turning this population profile into an empty-media test double.
            terrainTextures: source.TerrainTextures,
            population: population);
    }

    [Fact]
    public void People_rig_draws_every_walking_civilian_as_the_test_figure_facing_its_way_and_off_draws_its_own_art_again()
    {
        // The art set's people rig alone: the rest of the refreshed set has no bearing on the switch.
        using JsonDocument payload = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/packs/daggerfall.art-replacements/payload.json")));
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(System.Text.Encoding.UTF8.GetBytes(
            """{"artReplacements": {"images": [], "peopleRig": """ + payload.RootElement.GetProperty("artReplacements").GetProperty("peopleRig").GetRawText() + "}}"),
            "daggerfall.art-replacements");
        using ResidentPopulationFixture fixture = ResidentPopulationFixture.Create(art);
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(8 * 60 * 60);
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        PartsRigsRecorder rigs = fixture.Engine.PartsRigs;
        ActorState[] civilians = [.. session.State.Actors.All.Where(actor => actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian)];
        Assert.NotEmpty(civilians);

        Assert.False(session.Commands<IDeveloperPeopleCommands>().Rig("6").Succeeded);
        Assert.True(session.Commands<IDeveloperPeopleCommands>().Rig("4").Succeeded);
        Assert.Equal(["front", "side", "back"], rigs.Loaded.Select(path => Path.GetFileName(Path.GetDirectoryName(path))));
        session.Update(new ProductUpdate(OuterUpdate(3), []));

        AppearanceFact[] snapshot = fixture.Appearance.Snapshots[^1];
        NormalizedBillboardSprite sprite = fixture.Source.BillboardSprites.Values.First(value => value.TexturePath == "sprite/population.png");
        foreach (ActorState civilian in civilians)
        {
            AppearanceFact fact = Assert.Single(snapshot, fact => fact.ObjectId == DaggerfallPresentationObjectIds.Gameplay(civilian.DurableId, "Actor"));
            PartsRigAppearanceRequest request = Assert.Single(rigs.Appearances, pair => pair.Appearance == fact.Appearance).Request;
            // Four views: the front, the side mirrored for a camera at her left (+x), the back, and the side at her right.
            Assert.Equal([(0F, false), (90F, true), (180F, false), (-90F, false)],
                request.Views.ToArray().Select(view => (MathF.Round(float.RadiansToDegrees(view.Heading), 3), view.Mirror)));
            // She takes the light as a person's sprite does and casts her shape into the sun's shadow.
            Assert.Equal(DaggerfallSiteAppearance.LitSprite with { Shadow = SpriteShadowPolicy.CastAndReceive }, request.Material);
            // She is as tall as her own sprite, and faces where she heads.
            Assert.Equal(sprite.Size.Y / art.PeopleRig!.Height, request.Scale, 5);
            Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitZ, fact.Transform.Rotation), ActorHeading.Forward(civilian.HeadingYawRadians)) < 1e-4F);
            // She walks while she moves to a waypoint and idles otherwise, with the attention layer over both.
            PartsRigPlayRequest body = Assert.Single(rigs.Plays, play => play.ObjectId == fact.ObjectId && play.Track == 0);
            Assert.Equal(IWalkerCrowd.Walks(civilian.Wander.State) ? "walk" : "idle", body.Clip);
            Assert.Contains(rigs.Plays, play => play.ObjectId == fact.ObjectId && play.Track == 2 && play.Clip == "attention" && play.Looping);
        }

        // The eight-direction set adds the three-quarter views, each with its mirrored twin.
        Assert.True(session.Commands<IDeveloperPeopleCommands>().Rig("8").Succeeded);
        session.Update(new ProductUpdate(OuterUpdate(4), []));
        AppearanceFact eight = fixture.Appearance.Snapshots[^1].First(fact => fact.ObjectId == DaggerfallPresentationObjectIds.Gameplay(civilians[0].DurableId, "Actor"));
        Assert.Equal([0F, 45F, 90F, 135F, 180F, -135F, -90F, -45F], Assert.Single(rigs.Appearances, pair => pair.Appearance == eight.Appearance).Request.Views.ToArray()
            .Select(view => MathF.Round(float.RadiansToDegrees(view.Heading), 3)));

        // Off draws her own sprite again, and the figure's appearances and rigs are released once no snapshot draws them.
        Assert.True(session.Commands<IDeveloperPeopleCommands>().Rig("off").Succeeded);
        session.Update(new ProductUpdate(OuterUpdate(5), []));
        session.Update(new ProductUpdate(OuterUpdate(6), []));
        AppearanceFact own = fixture.Appearance.Snapshots[^1].First(fact => fact.ObjectId == DaggerfallPresentationObjectIds.Gameplay(civilians[0].DurableId, "Actor"));
        Assert.DoesNotContain(rigs.Appearances, pair => pair.Appearance == own.Appearance);
        Assert.Equal(rigs.Appearances.Count, rigs.ReleasedAppearances.Count);
        Assert.Equal(5, rigs.ReleasedRigs);
    }

    [Fact]
    public void People_splat_draws_every_walking_civilian_as_one_lit_gsplat_character_walking_or_idling_and_off_releases_them()
    {
        using ResidentPopulationFixture fixture = ResidentPopulationFixture.Create(DaggerfallPeopleSwitchTests.Art());
        DaggerfallSession session = fixture.Session;
        AnimationRecorder animation = fixture.Engine.Animation;
        animation.Clips = DaggerfallPeopleSwitchTests.Clips(DaggerfallPeopleSwitchTests.Art().SplatPeople!);
        session.AdvanceElapsedTime(8 * 60 * 60);
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        ActorState[] civilians = [.. session.State.Actors.All.Where(actor => actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian)];
        Assert.NotEmpty(civilians);

        Assert.True(session.Commands<IDeveloperPeopleCommands>().Splat("385").Succeeded);
        session.Update(new ProductUpdate(OuterUpdate(3), []));
        Appearance character = Drawn(animation, fixture.Composition.ArtReplacements!.SplatPeople!, "385");
        // The walks and idles it picks among, each played under its own name here.
        DaggerfallSplatPeople splats = fixture.Composition.ArtReplacements!.SplatPeople!;
        SplatFigure splatFigure = new("385", character, [.. splats.Walks.Select(clip => new SplatWalk(clip, 1F, 1F))], splats.Idles, 0F);
        AppearanceFact[] snapshot = fixture.Appearance.Snapshots[^1];
        foreach (ActorState civilian in civilians)
        {
            // Every walker is the one character, at his own size, facing where it heads, on an instance of its own.
            AppearanceFact fact = Assert.Single(snapshot, fact => fact.ObjectId == DaggerfallPresentationObjectIds.Gameplay(civilian.DurableId, "Actor"));
            Assert.Same(character, fact.Appearance);
            Assert.Equal(Vector3.One, fact.Transform.Scale);
            Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitZ, fact.Transform.Rotation), ActorHeading.Forward(civilian.HeadingYawRadians)) < 1e-4F);
            int instance = animation.InstanceRequests.FindIndex(request => request.ObjectId == fact.ObjectId && request.Appearance == character);
            Assert.True(instance >= 0, $"Walker {fact.ObjectId} has no instance.");
            // It walks the walk its name seed picks while it moves to a waypoint, and stands in the idle it picks otherwise.
            AnimationPlaybackRequest play = Assert.Single(animation.Playbacks, play => play.Instance.Handle.Value == 7001UL + (ulong)instance);
            SplatGait gait = splatFigure.Gait(session.State.Npcs.Require(civilian.DurableId).Appearance.NameSeed, DaggerfallSplatPeopleTuning.Default.GaitJitter);
            Assert.Equal(IWalkerCrowd.Walks(civilian.Wander.State) ? gait.Walk.Clip : gait.Idle, play.Clip);
        }

        // Off draws their own sprites again, and their instances are gone by the time that snapshot is published: the
        // Engine refuses a pose for an object that shows a sprite.
        int released = animation.ReleasedInstances;
        int? releasedAtPublish = null;
        fixture.Appearance.OnPublish = _ => releasedAtPublish ??= animation.ReleasedInstances - released;
        Assert.True(session.Commands<IDeveloperPeopleCommands>().Splat("off").Succeeded);
        session.Update(new ProductUpdate(OuterUpdate(4), []));
        AppearanceFact own = fixture.Appearance.Snapshots[^1].First(fact => fact.ObjectId == DaggerfallPresentationObjectIds.Gameplay(civilians[0].DurableId, "Actor"));
        Assert.NotSame(character, own.Appearance);
        Assert.Equal(civilians.Length, releasedAtPublish);
    }

    [Fact]
    public void A_walking_civilian_draws_by_default_as_the_gsplat_character_of_its_townsperson_art_and_off_draws_its_sprite()
    {
        DaggerfallArtReplacements art = DaggerfallPeopleSwitchTests.Art();
        using ResidentPopulationFixture fixture = ResidentPopulationFixture.Create(art);
        DaggerfallSession session = fixture.Session;
        AnimationRecorder animation = fixture.Engine.Animation;
        animation.Clips = DaggerfallPeopleSwitchTests.Clips(DaggerfallPeopleSwitchTests.Art().SplatPeople!);
        session.AdvanceElapsedTime(8 * 60 * 60);
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        session.Update(new ProductUpdate(OuterUpdate(3), []));
        ActorState[] civilians = [.. session.State.Actors.All.Where(actor => actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian)];
        Assert.NotEmpty(civilians);

        AppearanceFact[] snapshot = fixture.Appearance.Snapshots[^1];
        Dictionary<long, Appearance> worn = [];
        foreach (ActorState civilian in civilians)
        {
            // With no command given, each wears the character of the classic townsperson art its race and gender dress it in.
            DaggerfallNpcAppearance look = session.State.Npcs.Require(civilian.DurableId).Appearance;
            Assert.True(TestPayload.Definitions.Catalogs.TryGetRace(look.Race, out DaggerfallRaceDefinition race));
            int archive = Assert.IsType<int>(race.Townsperson(look.Gender, look.NameSeed));
            Appearance character = Drawn(animation, art.SplatPeople!, art.SplatPeople!.ByArchive[archive]);
            AppearanceFact fact = Assert.Single(snapshot, fact => fact.ObjectId == DaggerfallPresentationObjectIds.Gameplay(civilian.DurableId, "Actor"));
            Assert.Same(character, fact.Appearance);
            Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitZ, fact.Transform.Rotation), ActorHeading.Forward(civilian.HeadingYawRadians)) < 1e-4F);
            worn[civilian.DurableId] = character;
        }

        // Off draws their sprites, their instances gone by the time that snapshot is published.
        int released = animation.ReleasedInstances;
        int? releasedAtPublish = null;
        fixture.Appearance.OnPublish = _ => releasedAtPublish ??= animation.ReleasedInstances - released;
        Assert.True(session.Commands<IDeveloperPeopleCommands>().Splat("off").Succeeded);
        session.Update(new ProductUpdate(OuterUpdate(4), []));
        Assert.Equal(civilians.Length, releasedAtPublish);
        foreach (ActorState civilian in civilians)
            Assert.NotSame(worn[civilian.DurableId], fixture.Appearance.Snapshots[^1].First(fact => fact.ObjectId == DaggerfallPresentationObjectIds.Gameplay(civilian.DurableId, "Actor")).Appearance);

        // That snapshot accepted, the characters it no longer draws are let go, so the sprites cost what sprites cost.
        Assert.Equal(worn.Values.Distinct().Count(), animation.ReleasedAppearances);

        // Own dresses them in their characters again, opened anew.
        Assert.True(session.Commands<IDeveloperPeopleCommands>().Splat("own").Succeeded);
        session.Update(new ProductUpdate(OuterUpdate(5), []));
        foreach (ActorState civilian in civilians)
        {
            DaggerfallNpcAppearance look = session.State.Npcs.Require(civilian.DurableId).Appearance;
            Assert.True(TestPayload.Definitions.Catalogs.TryGetRace(look.Race, out DaggerfallRaceDefinition race));
            string character = art.SplatPeople!.ByArchive[race.Townsperson(look.Gender, look.NameSeed)!.Value];
            AppearanceFact fact = fixture.Appearance.Snapshots[^1].First(fact => fact.ObjectId == DaggerfallPresentationObjectIds.Gameplay(civilian.DurableId, "Actor"));
            Assert.Same(Drawn(animation, art.SplatPeople!, character), fact.Appearance);
            Assert.NotSame(worn[civilian.DurableId], fact.Appearance);
        }
    }

    /// <summary>The appearance gsplat character <paramref name="id"/> was last opened as.</summary>
    private static Appearance Drawn(AnimationRecorder animation, DaggerfallSplatPeople splats, string id) =>
        animation.Splats[animation.OpenedPaths.LastIndexOf(splats.Characters[id].Body.Path)].Appearance;

    private static int PopulationTextureOpens(AppearanceFake appearance) =>
        appearance.OpenedResourcePaths.Count(path => path == "sprite/population.png");

    private sealed class ResidentPopulationFixture : IDisposable
    {
        private ResidentPopulationFixture(DaggerfallSession session, DaggerfallSessionComposition composition,
            DaggerfallSiteProfile source, DaggerfallSiteProfile resident, DaggerfallSiteProfile interior,
            AppearanceFake appearance, EngineContextFake engine)
        {
            Engine = engine;
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
        internal EngineContextFake Engine { get; }

        internal static ResidentPopulationFixture Create(DaggerfallArtReplacements? art = null)
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
            DaggerfallSiteProfile template = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
            DaggerfallSiteProfile source = PopulationProfile(template, sourceRecord.Id,
                includeAuthoredActors: false);
            DaggerfallSiteProfile resident = PopulationProfile(template, residentRecord.Id,
                includeAuthoredActors: false, includePopulation: false);
            DaggerfallSiteProfile interior = SameContentAt(template, sourceRecord.Id,
                DaggerfallWorldProfileKind.Interior, "resident-population-interior");
            DaggerfallSiteProfiles profiles = new([source, resident, interior]);
            DaggerfallSessionComposition composition = new(definitions, source, DaggerfallTuning.Defaults)
            {
                Profiles = profiles,
                ArtReplacements = art,
                Animations = DaggerfallPeopleSwitchTests.Shipped(),
            };
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, source);
            PopulateContent(content, resident);
            PopulateContent(content, interior);
            SpatialFake spatial = SpatialFake.Create(source.Geometry.Spatial[0].Sha256, releases);
            AppearanceFake appearance = new(releases);
            EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance);
            DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
            // StartNew admits the selected profile before the catalog is attached. Let the
            // ordinary first update reconcile the real neighboring exterior so these
            // tests exercise retained source actors through the same runtime path as the host.
            session.Update(new ProductUpdate(OuterUpdate(1), []));
            return new(session, composition, source, resident, interior, appearance, engine);
        }

        internal (EngineContextFake Engine, AppearanceFake Appearance) CreateEngine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, Source);
            PopulateContent(content, Resident);
            PopulateContent(content, Interior);
            SpatialFake spatial = SpatialFake.Create(Source.Geometry.Spatial[0].Sha256, releases);
            AppearanceFake appearance = new(releases);
            return (EngineContextFake.Create(content, spatial.Service, appearance), appearance);
        }

        public void Dispose() => Session.Dispose();
    }
}
