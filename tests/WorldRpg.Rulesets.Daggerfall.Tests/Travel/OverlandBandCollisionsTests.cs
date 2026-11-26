using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Overland band-vs-band collisions and caravan raids (#11080):
/// - Opposing transient bands (e.g. MonsterSurge orcs vs BorderTension patrol) colliding on the map coalesce into one live joinable clash.
/// - Raiders/hostile bands colliding with passing caravans within player sight hold a live joinable Caravan Raid clash that
///   survives save/load and stages BOTH opposing sides facing each other with LairTruce=false and FactionInfighting=true on entry.
/// - Off-screen band-vs-band collisions resolve cleanly with zero orphaned OnOverlandBand ActorLedger entries.
/// </summary>
public sealed class OverlandBandCollisionsTests
{
    private const int DaggerfallRegion = 17;

    [Fact]
    public void Opposing_transient_bands_within_collision_distance_coalesce_into_single_joinable_map_clash()
    {
        DaggerfallDefinitions defs = TestPayload.Definitions;
        DaggerfallSiteRecord site = defs.Locations.Records.First(r => r.Id.Region == DaggerfallRegion && r.Exterior is not null);
        DaggerfallSiteContext sites = new(defs.Locations);
        DaggerfallTravelPolicy policy = new(sites, defs.Grids, DaggerfallTuning.Defaults.Transport);
        DaggerfallExpeditionBands bands = new(policy, defs.Encounters, defs.Mobiles, DaggerfallBandTuning.Default, _ => false, defs.CreatureFactions);

        (double X, double Y) party = (site.MapPixelX + 0.5, site.MapPixelY + 0.5);
        long now = 12L * DaggerfallCalendar.MinutesPerHour * DaggerfallCalendar.SecondsPerMinute;
        Func<string, int, int, int> lowRoll = (_, min, _) => min;

        // Pick a valid land bearing 2.2 pixels from party so both bands can be staged on the same spot.
        double chosenBearing = 0;
        bool staged = false;
        DaggerfallWorldConditionToken orcSurge = new(
            "cond:monster-surge:r17", DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, Severity: 2, StartedDay: 0, SubjectTag: "orcs");
        DaggerfallWorldConditionToken borderPatrol = new(
            "cond:border-tension:r17", DaggerfallWorldConditionKind.BorderTension, DaggerfallRegion, Severity: 2, StartedDay: 0);

        for (int b = 0; b < 360; b += 15)
        {
            var (orcBand, err1) = bands.StageAt(now, party, playerLevel: 5, lowRoll, distance: 2.2, bearing: b, camp: false, [orcSurge]);
            if (err1 is not null || orcBand is null) continue;
            var (patrolBand, err2) = bands.StageAt(now + 10, party, playerLevel: 5, lowRoll, distance: 2.25, bearing: b, camp: false, [borderPatrol]);
            if (err2 is not null || patrolBand is null)
            {
                bands.Remove(orcBand);
                continue;
            }
            chosenBearing = b;
            staged = true;
            break;
        }
        Assert.True(staged, $"Failed to stage opposing transient bands at bearing {chosenBearing}.");
        Assert.Equal(2, bands.All.Count);
        Assert.All(bands.All, b => Assert.False(b.IsFactionClash));

        // Resolving collisions coalesces the two opposing bands into ONE live Clash token held at their midpoint!
        int formed = bands.ResolveTransientBandCollisions();
        Assert.Equal(1, formed);
        DaggerfallExpeditionBand clash = Assert.Single(bands.All);
        Assert.True(clash.IsFactionClash);
        Assert.Equal(DaggerfallBandKind.Clash, clash.Kind);
        Assert.True(clash.Camp);
        Assert.Contains(" vs ", clash.Name, StringComparison.Ordinal);
        Assert.NotNull(clash.RivalChoice);
        Assert.True(clash.RivalSize > 0);
    }

    [Fact]
    public void Hostile_raiders_colliding_with_caravan_within_sight_holds_joinable_raid_across_save_and_stages_both_sides_on_entry()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord origin = definitions.Locations.Records.First(r =>
            r.Id.Region == DaggerfallRegion && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteProfile originProfile = Assembled(DaggerfallWorldProfileIds.Exterior(origin.Id));
        DaggerfallSiteProfiles profiles = new([originProfile]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, originProfile);
        SpatialFake spatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, originProfile, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.ApplyProductMode(ProductMode.Playing);
        session.OpenExpedition();
        (double px, double py) = session.Expedition.Position;

        // Dispatch a real seeded regional caravan AND a real hostile bounty hunter squad right next to each other within sight.
        DaggerfallPersistentOverlandBand? caravan = session.OverlandTraffic.TryDispatchWindow(701L, (px, py));
        Assert.NotNull(caravan);
        long caravanMemberId = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(caravan!.BandId)).DurableId;

        session.State.Social.ChangeRegionalReputation(origin.Id.Region, -25);
        DaggerfallPersistentOverlandBand? raiders = session.HostileOverlandBands.TryDispatchWindow(702L, (px, py));
        Assert.NotNull(raiders);
        long[] raiderMemberIds = [.. session.State.ActorLedger.EntriesOnOverlandBand(raiders!.BandId).Select(e => e.DurableId)];
        Assert.NotEmpty(raiderMemberIds);

        // Move both persistent bands right beside the player (within BOTH collision distance AND player meet/sight distance)
        // and advance normally through Expedition.Advance (no manual ResolveBandCollisions call!).
        // Collisions must resolve BEFORE player spotting or early caravan CameUpon can suppress the clash.
        caravan.X = px + 0.10;
        caravan.Y = py;
        raiders.X = px + 0.14;
        raiders.Y = py;

        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        session.Expedition.SetSpeed(1);
        _ = session.Expedition.Advance(0.25);

        // Rival band is absorbed into caravan.ActiveClash as ONE live Caravan Raid token, and Advance pauses on the raid!
        Assert.DoesNotContain(session.Expedition.PersistentBands, b => b.BandId == raiders.BandId);
        DaggerfallPersistentOverlandBand heldRaid = Assert.Single(session.Expedition.PersistentBands, b => b.BandId == caravan.BandId);
        Assert.NotNull(heldRaid.ActiveClash);
        Assert.Equal(raiders.BandId, heldRaid.ActiveClash!.RivalBandId);
        Assert.Equal(raiders.Name, heldRaid.ActiveClash.RivalName);
        Assert.NotNull(session.Expedition.BandAhead);
        Assert.True(session.Expedition.BandAhead!.IsClash);
        Assert.Contains("intervene", session.Expedition.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // TravelPanel projects it as a live Caravan Raid Clash within PartySight.
        DaggerfallExpeditionBandView raidView = Assert.Single(session.TravelPanel.Read()!.Bands, b => b.PersistentBandId == caravan.BandId);
        Assert.True(raidView.IsClash);
        Assert.Equal(DaggerfallBandKind.Clash, raidView.Kind);
        Assert.Equal("Caravan Raid", raidView.Category);
        Assert.Contains(" vs ", raidView.ClashSides, StringComparison.Ordinal);

        // Save and reload while the live raid is held on the map: ActiveClash and BOTH sides' OnOverlandBand ledger entries survive cleanly!
        RulesetSavePayload savedRaid = session.CaptureSave();
        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(savedRaid);
        DaggerfallPersistentBandSave savedBand = Assert.Single(decoded.Expedition.PersistentBands!, b => b.BandId == caravan.BandId);
        Assert.NotNull(savedBand.ActiveClash);
        Assert.Equal(raiders.BandId, savedBand.ActiveClash!.RivalBandId);

        List<string> restoreReleases = [];
        ContentFake restoreContent = new(restoreReleases);
        PopulateContent(restoreContent, originProfile);
        SpatialFake restoreSpatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, restoreReleases);
        EngineContextFake restoreEngine = EngineContextFake.Create(restoreContent, restoreSpatial.Service, new AppearanceFake(restoreReleases));
        using DaggerfallSession restored = DaggerfallSession.Restore(restoreEngine.Context, composition, savedRaid);
        restored.ApplyProductMode(ProductMode.Playing);

        // Advance one stretch to come upon the raid, then Enter the 3D scene: BOTH caravan AND raiders materialize facing each other with LairTruce=false and FactionInfighting=true!
        restored.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        restored.Expedition.SetSpeed(1);
        _ = restored.Expedition.Advance(0.25);
        Assert.NotNull(restored.Expedition.BandAhead);
        Assert.True(restored.Expedition.BandAhead!.IsClash);
        Assert.Contains("intervene", restored.Expedition.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        restored.EnterExpedition();
        Assert.False(restored.Expedition.Open);
        Assert.True(restored.State.Actors.TryGet(caravanMemberId, out ActorState? liveMerchant));
        List<ActorState> liveRaiders = [];
        foreach (long raiderId in raiderMemberIds)
        {
            Assert.True(restored.State.Actors.TryGet(raiderId, out ActorState? liveRaider));
            liveRaiders.Add(liveRaider!);
            DaggerfallEnemyPerceptionMemory mem = liveRaider!.Actor.Get<DaggerfallEnemyPerceptionMemory>();
            Assert.False(mem.LairTruce);
            Assert.True(mem.FactionInfighting);
            // Raiders stand ahead of the merchant (distinct positions facing each other).
            Assert.True(Vector3Distance(liveMerchant!.Position, liveRaider.Position) >= 2.0f);
        }
        AssertSidesFaceEachOther([liveMerchant!], liveRaiders);

        // Zero orphaned OnOverlandBand entries remain for either side!
        Assert.Empty(restored.State.ActorLedger.EntriesOnOverlandBand(caravan.BandId));
        Assert.Empty(restored.State.ActorLedger.EntriesOnOverlandBand(raiders.BandId));
    }

    [Fact]
    public void Expedition_advance_resolves_persistent_collision_inside_band_sight_before_spotting_suppresses_it()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord origin = definitions.Locations.Records.First(r =>
            r.Id.Region == DaggerfallRegion && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteProfile originProfile = Assembled(DaggerfallWorldProfileIds.Exterior(origin.Id));
        DaggerfallSiteProfiles profiles = new([originProfile]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, originProfile);
        SpatialFake spatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, originProfile, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.ApplyProductMode(ProductMode.Playing);
        session.OpenExpedition();
        (double px, double py) = session.Expedition.Position;

        DaggerfallPersistentOverlandBand? caravan = session.OverlandTraffic.TryDispatchWindow(751L, (px, py));
        Assert.NotNull(caravan);
        session.State.Social.ChangeRegionalReputation(origin.Id.Region, -25);
        DaggerfallPersistentOverlandBand? raiders = session.HostileOverlandBands.TryDispatchWindow(752L, (px, py));
        Assert.NotNull(raiders);

        // Place both bands strictly OUTSIDE MeetDistance, strictly INSIDE BandSight, and within collision distance of each other.
        double sightOffset = Math.Min(session.Expedition.BandSight * 0.75, 1.0);
        Assert.True(sightOffset > DaggerfallBandTuning.Default.MeetDistance + 0.1);
        Assert.True(sightOffset < session.Expedition.BandSight);
        caravan!.X = px + sightOffset;
        caravan.Y = py;
        raiders!.X = px + sightOffset + 0.05;
        raiders.Y = py;

        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        session.Expedition.SetSpeed(1);
        _ = session.Expedition.Advance(0.25);

        // Collisions resolve before player spotting: raiders are absorbed into caravan.ActiveClash instead of spotting/chasing the player.
        Assert.DoesNotContain(session.Expedition.PersistentBands, b => b.BandId == raiders.BandId);
        DaggerfallPersistentOverlandBand held = Assert.Single(session.Expedition.PersistentBands, b => b.BandId == caravan.BandId);
        Assert.NotNull(held.ActiveClash);
        Assert.Equal(raiders.BandId, held.ActiveClash!.RivalBandId);
        Assert.False(held.Spotted);
        Assert.False(session.Expedition.EncounterWaiting);
    }

    [Fact]
    public void Transient_raiders_colliding_with_caravan_via_advance_stage_facing_each_other_on_entry()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord origin = definitions.Locations.Records.First(r =>
            r.Id.Region == DaggerfallRegion && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteProfile originProfile = Assembled(DaggerfallWorldProfileIds.Exterior(origin.Id));
        DaggerfallSiteProfiles profiles = new([originProfile]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, originProfile);
        SpatialFake spatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, originProfile, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.ApplyProductMode(ProductMode.Playing);
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            "cond:monster-surge:r17:raid-test", DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, Severity: 2, StartedDay: 0, SubjectTag: "orcs"));
        session.OpenExpedition();

        // Pick an unlocated land pixel adjacent to origin (whose HostAt is origin) so transient raiders can be staged right inside meet distance.
        DaggerfallTravelMapPixel? wildPixel = null;
        for (int dy = -2; dy <= 2 && wildPixel is null; dy++)
        {
            for (int dx = -2; dx <= 2 && wildPixel is null; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                DaggerfallTravelMapPixel candidate = new(origin.MapPixelX + dx, origin.MapPixelY + dy);
                if (!candidate.IsInBounds || session.Expedition.Policy.IsOcean(candidate)) continue;
                if (session.Expedition.LocationAt(candidate) is not null) continue;
                if (session.Expedition.HostAt(candidate).Id != origin.Id) continue;
                wildPixel = candidate;
            }
        }
        Assert.NotNull(wildPixel);

        // Stand on the unlocated land pixel with a non-zero arrival direction (different from caravan map heading).
        session.Expedition.Close();
        session.Expedition.Begin(wildPixel!.Value, aboardOwnShip: false);
        (double px, double py) = session.Expedition.Position;

        DaggerfallPersistentOverlandBand? caravan = session.OverlandTraffic.TryDispatchWindow(781L, (px, py));
        Assert.NotNull(caravan);
        long caravanMemberId = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(caravan!.BandId)).DurableId;
        caravan.X = px + 0.10;
        caravan.Y = py;
        caravan.Heading = 225.0; // Intentionally unrelated to landingYaw (0 rad) to verify raiders orient to the merchant, not map heading.

        // Stage a hostile transient band right beside the caravan, inside BOTH collision distance AND player meet/sight distance.
        string? stageErr = session.Expedition.StageBand(distance: 0.14, bearing: 90.0, camp: true);
        Assert.Null(stageErr);
        DaggerfallExpeditionBandView stagedTransient = Assert.Single(session.Expedition.AllBands, b => b.PersistentBandId is null);
        Assert.True(stagedTransient.Hostile);

        // Advance normally through Expedition.Advance (no manual ResolveBandCollisions call): transient band must coalesce with caravan
        // into a live transient-rival Caravan Raid instead of being suppressed by player spotting/Caught or caravan CameUpon.
        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        session.Expedition.SetSpeed(1);
        _ = session.Expedition.Advance(0.25);

        Assert.DoesNotContain(session.Expedition.AllBands, b => b.PersistentBandId is null && b.Id == stagedTransient.Id);
        DaggerfallPersistentOverlandBand heldRaid = Assert.Single(session.Expedition.PersistentBands, b => b.BandId == caravan.BandId);
        Assert.NotNull(heldRaid.ActiveClash);
        Assert.Null(heldRaid.ActiveClash!.RivalBandId);
        Assert.NotNull(heldRaid.ActiveClash.RivalMobileId);
        Assert.True(heldRaid.ActiveClash.RivalSize > 0);
        int expectedRivalMobileId = heldRaid.ActiveClash.RivalMobileId!.Value;
        int expectedRivalSize = heldRaid.ActiveClash.RivalSize;

        Assert.NotNull(session.Expedition.BandAhead);
        Assert.True(session.Expedition.BandAhead!.IsClash);
        Assert.Contains("intervene", session.Expedition.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // Save/restore round-trip preserves the transient-rival raid, and EnterExpedition materializes BOTH sides facing each other via TrySpawnPose!
        RulesetSavePayload saved = session.CaptureSave();
        List<string> restoreReleases = [];
        ContentFake restoreContent = new(restoreReleases);
        PopulateContent(restoreContent, originProfile);
        SpatialFake restoreSpatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, restoreReleases);
        restoreSpatial.FloorHit = WalkableFlatFloor;
        EngineContextFake restoreEngine = EngineContextFake.Create(restoreContent, restoreSpatial.Service, new AppearanceFake(restoreReleases));
        using DaggerfallSession restored = DaggerfallSession.Restore(restoreEngine.Context, composition, saved);
        restored.ApplyProductMode(ProductMode.Playing);

        restored.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        restored.Expedition.SetSpeed(1);
        _ = restored.Expedition.Advance(0.25);
        Assert.NotNull(restored.Expedition.BandAhead);
        Assert.True(restored.Expedition.BandAhead!.IsClash);

        HashSet<long> actorsBeforeEntry = [.. restored.State.Actors.All.Select(a => a.DurableId)];
        restored.EnterExpedition();
        Assert.False(restored.Expedition.Open);
        Assert.NotEmpty(restoreSpatial.FloorProbes);
        Assert.NotEmpty(restoreSpatial.OverlapRequests);
        Assert.Equal(0, restored.OverlandParties.PendingTransientClashCount);

        Assert.True(restored.State.Actors.TryGet(caravanMemberId, out ActorState? liveMerchant));
        List<ActorState> spawnedRaiders = [.. restored.State.Actors.All
            .Where(a => a.DurableId != caravanMemberId
                && !actorsBeforeEntry.Contains(a.DurableId)
                && restored.DefinitionsByActor.TryGetValue(a.DurableId, out DaggerfallActorDefinition? def)
                && def.MobileId == expectedRivalMobileId)];
        Assert.Equal(expectedRivalSize, spawnedRaiders.Count);

        foreach (ActorState raider in spawnedRaiders)
        {
            DaggerfallEnemyPerceptionMemory mem = raider.Actor.Get<DaggerfallEnemyPerceptionMemory>();
            Assert.False(mem.LairTruce);
            Assert.True(mem.FactionInfighting);
            Assert.True(Vector3Distance(liveMerchant!.Position, raider.Position) >= 2.0f);
        }
        AssertSidesFaceEachOther([liveMerchant!], spawnedRaiders);
        Assert.Empty(restored.State.ActorLedger.EntriesOnOverlandBand(caravan.BandId));
    }

    [Fact]
    public void Obstructed_transient_raid_anchor_never_spawns_into_blocked_pose_and_retries_when_clear()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord origin = definitions.Locations.Records.First(r =>
            r.Id.Region == DaggerfallRegion && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteProfile originProfile = Assembled(DaggerfallWorldProfileIds.Exterior(origin.Id));
        DaggerfallSiteProfiles profiles = new([originProfile]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, originProfile);
        SpatialFake spatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, originProfile, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.ApplyProductMode(ProductMode.Playing);
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            "cond:monster-surge:r17:obstructed-test", DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, Severity: 2, StartedDay: 0, SubjectTag: "orcs"));
        session.OpenExpedition();

        DaggerfallTravelMapPixel? wildPixel = null;
        for (int dy = -2; dy <= 2 && wildPixel is null; dy++)
        {
            for (int dx = -2; dx <= 2 && wildPixel is null; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                DaggerfallTravelMapPixel candidate = new(origin.MapPixelX + dx, origin.MapPixelY + dy);
                if (!candidate.IsInBounds || session.Expedition.Policy.IsOcean(candidate)) continue;
                if (session.Expedition.LocationAt(candidate) is not null) continue;
                if (session.Expedition.HostAt(candidate).Id != origin.Id) continue;
                wildPixel = candidate;
            }
        }
        Assert.NotNull(wildPixel);

        session.Expedition.Close();
        session.Expedition.Begin(wildPixel!.Value, aboardOwnShip: false);
        (double px, double py) = session.Expedition.Position;

        DaggerfallPersistentOverlandBand? caravan = session.OverlandTraffic.TryDispatchWindow(791L, (px, py));
        Assert.NotNull(caravan);
        long caravanMemberId = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(caravan!.BandId)).DurableId;
        caravan.X = px + 0.10;
        caravan.Y = py;
        Assert.Null(session.Expedition.StageBand(distance: 0.14, bearing: 90.0, camp: true));

        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        session.Expedition.SetSpeed(1);
        _ = session.Expedition.Advance(0.25);

        DaggerfallPersistentOverlandBand heldRaid = Assert.Single(session.Expedition.PersistentBands, b => b.BandId == caravan.BandId);
        Assert.NotNull(heldRaid.ActiveClash);
        int expectedRivalMobileId = heldRaid.ActiveClash!.RivalMobileId!.Value;
        int expectedRivalSize = heldRaid.ActiveClash.RivalSize;
        Assert.True(expectedRivalSize > 0);
        RulesetSavePayload savedRaid = session.CaptureSave();

        // Case A: A solid obstacle blocks the 9m anchor directly ahead of the player (radius 3m), while nearby space is clear.
        // TrySpawnPose must deflect raiders away from the obstructed 9m zone and orient both sides from their actual resolved poses.
        List<string> partialReleases = [];
        ContentFake partialContent = new(partialReleases);
        PopulateContent(partialContent, originProfile);
        SpatialFake partialSpatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, partialReleases);
        Vector3? blocked9mAnchor = null;
        const float obstacleRadius = 3.0f;
        partialSpatial.FloorHit = request =>
        {
            if (request.Direction.Y < -0.5f)
                return WalkableFlatFloor(request);
            // Horizontal wall ray check: block rays passing directly into the 9m obstacle ahead of the origin.
            if (blocked9mAnchor is { } obs)
            {
                Vector3 rel = obs - request.Origin;
                float proj = Vector3.Dot(rel, request.Direction);
                if (proj > 0f && proj <= request.MaxDistance)
                {
                    Vector3 closest = request.Origin + request.Direction * proj;
                    float dx = closest.X - obs.X;
                    float dz = closest.Z - obs.Z;
                    if (dx * dx + dz * dz <= obstacleRadius * obstacleRadius)
                        return new SpatialHit { Present = true, Distance = MathF.Max(0.5f, proj - obstacleRadius), Point = closest, Normal = -request.Direction };
                }
            }
            return default;
        };
        partialSpatial.OverlapHit = request =>
        {
            if (blocked9mAnchor is { } obs)
            {
                float dx = request.Center.X - obs.X;
                float dz = request.Center.Z - obs.Z;
                if (dx * dx + dz * dz <= obstacleRadius * obstacleRadius)
                    return new SpatialHit { Present = true };
            }
            return default;
        };
        EngineContextFake partialEngine = EngineContextFake.Create(partialContent, partialSpatial.Service, new AppearanceFake(partialReleases));
        using DaggerfallSession partialSession = DaggerfallSession.Restore(partialEngine.Context, composition, savedRaid);
        partialSession.ApplyProductMode(ProductMode.Playing);
        partialSession.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        partialSession.Expedition.SetSpeed(1);
        _ = partialSession.Expedition.Advance(0.25);

        // Arm the obstacle at the 9m forward anchor dynamically from the first horizontal placement ray's origin (the landing pose).
        Func<SpatialRaycastRequest, SpatialHit> baseFloor = partialSpatial.FloorHit;
        partialSpatial.FloorHit = request =>
        {
            if (blocked9mAnchor is null && MathF.Abs(request.Direction.Y) < 0.1f)
                blocked9mAnchor = request.Origin + ActorHeading.Forward(0f) * 9.0f;
            return baseFloor(request);
        };

        HashSet<long> partialBefore = [.. partialSession.State.Actors.All.Select(a => a.DurableId)];
        partialSession.EnterExpedition();
        Assert.NotNull(blocked9mAnchor);
        Assert.True(partialSession.State.Actors.TryGet(caravanMemberId, out ActorState? partialMerchant));
        List<ActorState> partialRaiders = [.. partialSession.State.Actors.All
            .Where(a => a.DurableId != caravanMemberId
                && !partialBefore.Contains(a.DurableId)
                && partialSession.DefinitionsByActor.TryGetValue(a.DurableId, out DaggerfallActorDefinition? def)
                && def.MobileId == expectedRivalMobileId)];
        Assert.Equal(expectedRivalSize, partialRaiders.Count);
        foreach (ActorState raider in partialRaiders)
        {
            float distToBlocked = HorizontalDistance(raider.Position.ToVector(), blocked9mAnchor!.Value);
            Assert.True(distToBlocked > obstacleRadius, $"Raider {raider.DurableId} spawned inside blocked 9m obstacle (dist={distToBlocked:F2}).");
        }
        AssertSidesFaceEachOther([partialMerchant!], partialRaiders);

        // Case B: Placement is completely obstructed on entry -> ZERO raiders spawn into blocked poses,
        // pending members are retained, and they materialize facing the merchant once clear space returns on a later update!
        List<string> blockedReleases = [];
        ContentFake blockedContent = new(blockedReleases);
        PopulateContent(blockedContent, originProfile);
        SpatialFake blockedSpatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, blockedReleases);
        blockedSpatial.FloorHit = WalkableFlatFloor;
        blockedSpatial.OverlapHit = _ => new SpatialHit { Present = true };
        EngineContextFake blockedEngine = EngineContextFake.Create(blockedContent, blockedSpatial.Service, new AppearanceFake(blockedReleases));
        using DaggerfallSession blockedSession = DaggerfallSession.Restore(blockedEngine.Context, composition, savedRaid);
        blockedSession.ApplyProductMode(ProductMode.Playing);
        blockedSession.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        blockedSession.Expedition.SetSpeed(1);
        _ = blockedSession.Expedition.Advance(0.25);

        HashSet<long> blockedBefore = [.. blockedSession.State.Actors.All.Select(a => a.DurableId)];
        blockedSession.EnterExpedition();
        Assert.True(blockedSession.State.Actors.TryGet(caravanMemberId, out ActorState? blockedMerchant));
        List<ActorState> spawnedWhileBlocked = [.. blockedSession.State.Actors.All
            .Where(a => a.DurableId != caravanMemberId
                && !blockedBefore.Contains(a.DurableId)
                && blockedSession.DefinitionsByActor.TryGetValue(a.DurableId, out DaggerfallActorDefinition? def)
                && def.MobileId == expectedRivalMobileId)];
        Assert.Empty(spawnedWhileBlocked);
        Assert.Equal(expectedRivalSize, blockedSession.OverlandParties.PendingTransientClashCount);

        // Clear the obstruction and run one admitted update: retained raiders materialize via TrySpawnPose facing the merchant!
        blockedSpatial.OverlapHit = _ => default;
        blockedSession.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(0, blockedSession.OverlandParties.PendingTransientClashCount);
        List<ActorState> spawnedAfterRetry = [.. blockedSession.State.Actors.All
            .Where(a => a.DurableId != caravanMemberId
                && !blockedBefore.Contains(a.DurableId)
                && blockedSession.DefinitionsByActor.TryGetValue(a.DurableId, out DaggerfallActorDefinition? def)
                && def.MobileId == expectedRivalMobileId)];
        Assert.Equal(expectedRivalSize, spawnedAfterRetry.Count);
        foreach (ActorState raider in spawnedAfterRetry)
        {
            DaggerfallEnemyPerceptionMemory mem = raider.Actor.Get<DaggerfallEnemyPerceptionMemory>();
            Assert.False(mem.LairTruce);
            Assert.True(mem.FactionInfighting);
        }
        AssertSidesFaceEachOther([blockedMerchant!], spawnedAfterRetry);
    }

    [Fact]
    public void Off_screen_persistent_band_collision_resolves_cleanly_with_zero_orphaned_ledger_entries_and_valid_save()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord origin = definitions.Locations.Records.First(r =>
            r.Id.Region == DaggerfallRegion && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteProfile originProfile = Assembled(DaggerfallWorldProfileIds.Exterior(origin.Id));
        DaggerfallSiteProfiles profiles = new([originProfile]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, originProfile);
        SpatialFake spatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, originProfile, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.ApplyProductMode(ProductMode.Playing);
        (double px, double py) = (origin.MapPixelX + 0.5, origin.MapPixelY + 0.5);

        // Dispatch a caravan and a hostile squad far away from the player (off-map / off-screen) at the same coordinates.
        DaggerfallPersistentOverlandBand? caravan = session.OverlandTraffic.TryDispatchWindow(801L, (px, py));
        Assert.NotNull(caravan);
        session.State.Social.ChangeRegionalReputation(origin.Id.Region, -25);
        DaggerfallPersistentOverlandBand? raiders = session.HostileOverlandBands.TryDispatchWindow(802L, (px, py));
        Assert.NotNull(raiders);

        double farX = Math.Min(DaggerfallTravelMapPixel.Width - 2.5, px + 30.0);
        caravan!.X = farX;
        caravan.Y = py;
        raiders!.X = farX + 0.05;
        raiders.Y = py;

        // Advance off-map time: collision resolves cleanly without player pauses, leaving zero orphaned OnOverlandBand entries!
        session.AdvanceElapsedTime(2L * DaggerfallCalendar.MinutesPerHour * DaggerfallCalendar.SecondsPerMinute);
        Assert.DoesNotContain(session.Expedition.PersistentBands, b => b.BandId == raiders.BandId);
        Assert.Empty(session.State.ActorLedger.EntriesOnOverlandBand(raiders.BandId));
        if (!session.Expedition.PersistentBands.Any(b => b.BandId == caravan.BandId))
            Assert.Empty(session.State.ActorLedger.EntriesOnOverlandBand(caravan.BandId));

        // CaptureSave + Restore passes full save validation (every OnOverlandBand ledger entry matches an active band).
        RulesetSavePayload saved = session.CaptureSave();
        List<string> restoreReleases = [];
        ContentFake restoreContent = new(restoreReleases);
        PopulateContent(restoreContent, originProfile);
        SpatialFake restoreSpatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, restoreReleases);
        EngineContextFake restoreEngine = EngineContextFake.Create(restoreContent, restoreSpatial.Service, new AppearanceFake(restoreReleases));
        using DaggerfallSession restored = DaggerfallSession.Restore(restoreEngine.Context, composition, saved);
        Assert.NotNull(restored);
    }

    private static void AssertSidesFaceEachOther(IReadOnlyList<ActorState> sideA, IReadOnlyList<ActorState> sideB)
    {
        Vector3 centerA = MeanPosition(sideA);
        Vector3 centerB = MeanPosition(sideB);
        foreach (ActorState a in sideA)
        {
            Vector3 toB = HorizontalNormalize(centerB - a.Position.ToVector());
            float dot = Vector3.Dot(ActorHeading.Forward(a.HeadingYawRadians), toB);
            Assert.True(dot > 0.95f, $"Expected primary actor {a.DurableId} to face rival centroid (dot={dot:F3}).");
        }
        foreach (ActorState b in sideB)
        {
            Vector3 toA = HorizontalNormalize(centerA - b.Position.ToVector());
            float dot = Vector3.Dot(ActorHeading.Forward(b.HeadingYawRadians), toA);
            Assert.True(dot > 0.95f, $"Expected rival actor {b.DurableId} to face primary centroid (dot={dot:F3}).");
        }
    }

    private static Vector3 MeanPosition(IReadOnlyList<ActorState> actors)
    {
        Vector3 sum = Vector3.Zero;
        foreach (ActorState actor in actors) sum += actor.Position.ToVector();
        return sum / actors.Count;
    }

    private static Vector3 HorizontalNormalize(Vector3 v) => Vector3.Normalize(new Vector3(v.X, 0f, v.Z));

    private static SpatialHit WalkableFlatFloor(SpatialRaycastRequest request) =>
        request.Direction.Y < -0.5f
            ? new SpatialHit { Present = true, Point = new Vector3(request.Origin.X, 0f, request.Origin.Z), Normal = Vector3.UnitY, Distance = MathF.Max(0f, request.Origin.Y) }
            : default;

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static float Vector3Distance(WorldPoint a, WorldPoint b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
