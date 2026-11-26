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
        long now = ((IDaggerfallExpeditionSurroundings)session).Calendar.ToAbsoluteSeconds();

        // Dispatch a real seeded regional caravan AND a real hostile bounty hunter squad right next to each other within sight.
        DaggerfallPersistentOverlandBand? caravan = session.OverlandTraffic.TryDispatchWindow(701L, (px, py));
        Assert.NotNull(caravan);
        long caravanMemberId = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(caravan!.BandId)).DurableId;

        session.State.Social.ChangeRegionalReputation(origin.Id.Region, -25);
        DaggerfallPersistentOverlandBand? raiders = session.HostileOverlandBands.TryDispatchWindow(702L, (px, py));
        Assert.NotNull(raiders);
        long[] raiderMemberIds = [.. session.State.ActorLedger.EntriesOnOverlandBand(raiders!.BandId).Select(e => e.DurableId)];
        Assert.NotEmpty(raiderMemberIds);

        // Move both persistent bands right beside the player (within meet distance) so they collide in sight.
        caravan.X = px + 0.10;
        caravan.Y = py;
        raiders.X = px + 0.14;
        raiders.Y = py;

        int collisions = session.Expedition.ResolveBandCollisions(now, (px, py), mapOpen: true);
        Assert.Equal(1, collisions);

        // Rival band is absorbed into caravan.ActiveClash as ONE live Caravan Raid token!
        Assert.DoesNotContain(session.Expedition.PersistentBands, b => b.BandId == raiders.BandId);
        DaggerfallPersistentOverlandBand heldRaid = Assert.Single(session.Expedition.PersistentBands, b => b.BandId == caravan.BandId);
        Assert.NotNull(heldRaid.ActiveClash);
        Assert.Equal(raiders.BandId, heldRaid.ActiveClash!.RivalBandId);
        Assert.Equal(raiders.Name, heldRaid.ActiveClash.RivalName);

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
        foreach (long raiderId in raiderMemberIds)
        {
            Assert.True(restored.State.Actors.TryGet(raiderId, out ActorState? liveRaider));
            DaggerfallEnemyPerceptionMemory mem = liveRaider!.Actor.Get<DaggerfallEnemyPerceptionMemory>();
            Assert.False(mem.LairTruce);
            Assert.True(mem.FactionInfighting);
            // Raiders stand ahead of the merchant (distinct positions facing each other).
            Assert.True(Vector3Distance(liveMerchant!.Position, liveRaider.Position) >= 2.0f);
        }
        // Zero orphaned OnOverlandBand entries remain for either side!
        Assert.Empty(restored.State.ActorLedger.EntriesOnOverlandBand(caravan.BandId));
        Assert.Empty(restored.State.ActorLedger.EntriesOnOverlandBand(raiders.BandId));
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

    private static float Vector3Distance(WorldPoint a, WorldPoint b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
