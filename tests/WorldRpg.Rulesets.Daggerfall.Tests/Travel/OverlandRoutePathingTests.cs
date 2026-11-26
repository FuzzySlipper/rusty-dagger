using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Terrain-aware overland route pathing for persistent bands (#11078):
/// bays/inlets detour around water onto land, wide cross-bay journeys take sea crossings cleanly,
/// mountain/rough terrain slows march pace, courier arrival stays synchronized with ExpectedArrivalMinute,
/// and mid-route progress (RouteOrigin + RouteStep) round-trips cleanly across save/load.
/// </summary>
public sealed class OverlandRoutePathingTests
{
    [Fact]
    public void Coastal_bay_detour_routes_persistent_band_around_water_on_land_without_stepping_into_ocean()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteContext sites = new(definitions.Locations);
        DaggerfallTravelPolicy policy = new(sites, definitions.Grids, DaggerfallTuning.Defaults.Transport);

        // Find a real pair of nearby coastal towns (span <= 40 px) whose straight-line Bresenham Path crosses ocean,
        // yet a clean land route around the bay/inlet exists.
        (DaggerfallTravelMapPixel Origin, DaggerfallTravelMapPixel Destination, DaggerfallOverlandRoute Route)? found = null;
        DaggerfallSiteRecord[] coastal = [.. definitions.Locations.Records
            .Where(r => r.Exterior is not null)
            .Take(2000)];
        for (int i = 0; i < coastal.Length && found is null; i++)
        {
            DaggerfallTravelMapPixel a = new(coastal[i].MapPixelX, coastal[i].MapPixelY);
            if (!a.IsInBounds || policy.IsOcean(a)) continue;
            for (int j = i + 1; j < coastal.Length; j++)
            {
                DaggerfallTravelMapPixel b = new(coastal[j].MapPixelX, coastal[j].MapPixelY);
                if (!b.IsInBounds || b == a || policy.IsOcean(b)) continue;
                int span = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
                if (span is < 8 or > 36) continue;

                IReadOnlyList<DaggerfallTravelMapPixel> straight = DaggerfallTravelPolicy.Path(a, b);
                int straightOceanCount = straight.Count(policy.IsOcean);
                if (straightOceanCount < 3) continue;

                DaggerfallOverlandRoute candidate = policy.OverlandRoute(a, b);
                if (!candidate.UsesOcean && candidate.Waypoints.Count > 0)
                {
                    found = (a, b, candidate);
                    break;
                }
            }
        }

        Assert.NotNull(found);
        (DaggerfallTravelMapPixel origin, DaggerfallTravelMapPixel destination, DaggerfallOverlandRoute route) = found!.Value;
        Assert.False(route.UsesOcean);
        Assert.Equal(destination, route.Waypoints[^1]);
        Assert.All(route.Waypoints, wp => Assert.False(policy.IsOcean(wp)));

        // Step a persistent band hour-by-hour along the route: every intermediate position remains strictly on land!
        DaggerfallPersistentOverlandBand band = new(
            "bay-detour-caravan", -1, "Coastal Caravan", DaggerfallBandKind.Travellers,
            origin.X + 0.5, origin.Y + 0.5, 0d, speedPixelsPerHour: 1.5,
            destination: new DaggerfallExpeditionMark(destination, null),
            routeOrigin: origin, routeStep: 0);

        bool arrived = false;
        for (int hour = 0; hour < 200 && !arrived; hour++)
        {
            arrived = band.StepMove((0, 0), hours: 0.5, allowPursuit: false, policy: policy);
            DaggerfallTravelMapPixel current = new((int)Math.Floor(band.X), (int)Math.Floor(band.Y));
            Assert.False(policy.IsOcean(current),
                $"Persistent band stepped onto ocean pixel {current} at step {hour} on a land-detourable coastal route ({origin} -> {destination}).");
        }

        Assert.True(arrived);
        Assert.Equal(destination.X + 0.5, band.X, precision: 4);
        Assert.Equal(destination.Y + 0.5, band.Y, precision: 4);
    }

    [Fact]
    public void Wide_cross_bay_route_uses_sea_crossing_and_synchronizes_courier_dispatch_arrival_with_expected_minute()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        // Region 17 (Daggerfall, north shore) -> Region 20 (Sentinel, south shore across Iliac Bay).
        DaggerfallSiteRecord northShore = definitions.Locations.Records.First(r =>
            r.Id.Region == 17 && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteRecord southShore = definitions.Locations.Records.First(r =>
            r.Id.Region == 20 && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);

        DaggerfallSiteProfile originProfile = Assembled(DaggerfallWorldProfileIds.Exterior(northShore.Id));
        DaggerfallSiteProfile destProfile = Assembled(DaggerfallWorldProfileIds.Exterior(southShore.Id));
        DaggerfallSiteProfiles profiles = new([originProfile, destProfile]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, originProfile);
        PopulateContent(content, destProfile);
        SpatialFake spatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, originProfile, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);

        DaggerfallTravelMapPixel from = new(northShore.MapPixelX, northShore.MapPixelY);
        DaggerfallTravelMapPixel to = new(southShore.MapPixelX, southShore.MapPixelY);
        DaggerfallOverlandRoute crossBay = session.Expedition.Policy.OverlandRoute(from, to);
        Assert.True(crossBay.UsesOcean, "Daggerfall -> Sentinel across Iliac Bay should take a sea crossing.");
        Assert.Equal(to, crossBay.Waypoints[^1]);
        double direct = Math.Sqrt((to.X - from.X) * (double)(to.X - from.X) + (to.Y - from.Y) * (double)(to.Y - from.Y));
        Assert.True(crossBay.TotalEquivalentPixels > direct + 2.0);

        // Stage a board-game courier dispatch across Iliac Bay: verify physical band arrives in lockstep with ExpectedArrivalMinute.
        long startMinute = ((IDaggerfallExpeditionSurroundings)session).Calendar.MinuteNumber;
        int startDay = checked((int)((IDaggerfallExpeditionSurroundings)session).Calendar.DayNumber);
        DaggerfallCourierDispatchSave dispatch = session.CourierDispatches.StageDispatch(
            dispatchId: "disp:iliac-crossing",
            seatId: "crown:daggerfall",
            actionKind: "maritime-treaty",
            summary: "Cross-bay sealed treaty",
            origin: northShore,
            destination: southShore,
            currentMinute: startMinute,
            currentDay: startDay,
            stageOverlandBand: true);

        Assert.NotNull(dispatch.OverlandBandId);
        DaggerfallPersistentOverlandBand courierBand = Assert.Single(
            session.Expedition.PersistentBands, b => b.BandId == dispatch.OverlandBandId);
        double totalHours = (dispatch.ExpectedArrivalMinute - dispatch.DispatchedAtMinute) / 60d;

        // 1 hour before ExpectedArrivalMinute: still in transit on the route.
        bool earlyArrived = courierBand.StepMove((0, 0), Math.Max(0.5, totalHours - 1.0), allowPursuit: false, policy: session.Expedition.Policy);
        Assert.False(earlyArrived);
        Assert.True(courierBand.RouteStep > 0 && courierBand.RouteStep < crossBay.Waypoints.Count);

        // Remaining 1 hour + tiny epsilon: reaches destination pixel cleanly!
        bool arrivedOnTime = courierBand.StepMove((0, 0), 1.01, allowPursuit: false, policy: session.Expedition.Policy);
        Assert.True(arrivedOnTime);
        Assert.Equal(to.X + 0.5, courierBand.X, precision: 4);
        Assert.Equal(to.Y + 0.5, courierBand.Y, precision: 4);
    }

    [Fact]
    public void Mountain_terrain_slows_band_march_and_mid_route_progress_persists_cleanly_across_save_reload()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord originSite = definitions.Locations.Records.First(r =>
            r.Id.Region == 17 && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteProfile originProfile = Assembled(DaggerfallWorldProfileIds.Exterior(originSite.Id));
        DaggerfallSiteProfiles profiles = new([originProfile]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, originProfile);
        SpatialFake spatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, originProfile, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);

        // Untouched save writes zero persistent band bytes.
        DaggerfallSavePayload untouched = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Null(untouched.Expedition.PersistentBands);

        DaggerfallTravelPolicy policy = session.Expedition.Policy;
        // Find a horizontal 12-pixel flat-land segment and a horizontal 12-pixel mountain/rough segment.
        DaggerfallTravelMapPixel? flatStart = null, roughStart = null;
        for (int y = 40; y < DaggerfallTravelMapPixel.Height - 40 && (flatStart is null || roughStart is null); y += 2)
        {
            for (int x = 40; x < DaggerfallTravelMapPixel.Width - 60 && (flatStart is null || roughStart is null); x += 4)
            {
                bool allFlat = true;
                bool allRough = true;
                for (int dx = 0; dx <= 12; dx++)
                {
                    DaggerfallTravelMapPixel p = new(x + dx, y);
                    if (policy.IsOcean(p)) { allFlat = false; allRough = false; break; }
                    double tf = policy.TerrainFactor(p);
                    if (tf > 1.01) allFlat = false;
                    if (tf < 1.10) allRough = false;
                }
                if (allFlat && flatStart is null) flatStart = new(x, y);
                if (allRough && roughStart is null) roughStart = new(x, y);
            }
        }

        Assert.NotNull(flatStart);
        Assert.NotNull(roughStart);
        DaggerfallTravelMapPixel flatEnd = new(flatStart!.Value.X + 12, flatStart.Value.Y);
        DaggerfallTravelMapPixel roughEnd = new(roughStart!.Value.X + 12, roughStart.Value.Y);

        DaggerfallPersistentOverlandBand flatBand = new(
            "flat-pace", -10, "Plains Caravan", DaggerfallBandKind.Travellers,
            flatStart.Value.X + 0.5, flatStart.Value.Y + 0.5, 90d, speedPixelsPerHour: 2.0,
            destination: new DaggerfallExpeditionMark(flatEnd, null), routeOrigin: flatStart.Value, routeStep: 0);
        DaggerfallPersistentOverlandBand roughBand = new(
            "rough-pace", -11, "Mountain Caravan", DaggerfallBandKind.Travellers,
            roughStart.Value.X + 0.5, roughStart.Value.Y + 0.5, 90d, speedPixelsPerHour: 2.0,
            destination: new DaggerfallExpeditionMark(roughEnd, null), routeOrigin: roughStart.Value, routeStep: 0);

        Assert.False(flatBand.StepMove((0, 0), hours: 3.0, allowPursuit: false, policy: policy));
        Assert.False(roughBand.StepMove((0, 0), hours: 3.0, allowPursuit: false, policy: policy));
        double flatProgress = flatBand.X - (flatStart.Value.X + 0.5);
        double roughProgress = roughBand.X - (roughStart.Value.X + 0.5);
        Assert.True(roughProgress < flatProgress - 0.3,
            $"Expected mountain/rough terrain march ({roughProgress:F2} px) to advance slower than flat plains ({flatProgress:F2} px).");

        // Persist mid-route band in session, save, restore, and verify exact RouteOrigin/RouteStep continuation!
        DaggerfallPersistentBandSave midSave = roughBand.ToSave();
        Assert.Equal(roughStart.Value, midSave.RouteOrigin);
        Assert.True(midSave.RouteStep > 0);
        session.Expedition.RegisterPersistentBand(midSave);

        RulesetSavePayload savedBytes = session.CaptureSave();
        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(savedBytes);
        DaggerfallPersistentBandSave savedBand = Assert.Single(decoded.Expedition.PersistentBands!, b => b.BandId == "rough-pace");
        Assert.Equal(roughStart.Value, savedBand.RouteOrigin);
        Assert.Equal(roughBand.RouteStep, savedBand.RouteStep);

        List<string> restoreReleases = [];
        ContentFake restoreContent = new(restoreReleases);
        PopulateContent(restoreContent, originProfile);
        SpatialFake restoreSpatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, restoreReleases);
        EngineContextFake restoreEngine = EngineContextFake.Create(restoreContent, restoreSpatial.Service, new AppearanceFake(restoreReleases));
        using DaggerfallSession restored = DaggerfallSession.Restore(restoreEngine.Context, composition, savedBytes);

        DaggerfallPersistentOverlandBand restoredBand = Assert.Single(restored.Expedition.PersistentBands, b => b.BandId == "rough-pace");
        Assert.Equal(roughBand.X, restoredBand.X, precision: 6);
        Assert.Equal(roughBand.Y, restoredBand.Y, precision: 6);
        Assert.Equal(roughBand.RouteOrigin, restoredBand.RouteOrigin);
        Assert.Equal(roughBand.RouteStep, restoredBand.RouteStep);

        // Stepping both the original band and the restored band 2 more hours yields identical coordinates.
        bool origDone = roughBand.StepMove((0, 0), hours: 2.0, allowPursuit: false, policy: policy);
        bool restDone = restoredBand.StepMove((0, 0), hours: 2.0, allowPursuit: false, policy: restored.Expedition.Policy);
        Assert.Equal(origDone, restDone);
        Assert.Equal(roughBand.X, restoredBand.X, precision: 6);
        Assert.Equal(roughBand.Y, restoredBand.Y, precision: 6);
        Assert.Equal(roughBand.RouteStep, restoredBand.RouteStep);
    }
}
