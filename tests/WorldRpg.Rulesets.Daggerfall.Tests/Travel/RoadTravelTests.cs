using System.Diagnostics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The road (#11178): travelling bands far from the party leave the map for the road, where they go a segment at a time
/// and meet what the regions they cross hold, delayed or raided, and come back onto the map where they stand when the
/// party nears, keeping who they are through a save; a courier lost on the road loses its board move.
/// </summary>
public sealed class RoadTravelTests
{
    private const int Width = 200, Row = 10;
    private static readonly (double X, double Y) Home = (2.5, Row + .5);

    [Fact]
    public void A_band_far_off_goes_on_the_road_comes_onto_the_map_where_it_stands_and_keeps_itself_through_a_save()
    {
        (DaggerfallExpedition expedition, RoadWorld world) = Road();
        expedition.RegisterPersistentBand(Caravan("caravan", 150.5));

        Pass(expedition, world, 3600, Home);
        DaggerfallPersistentOverlandBand band = Assert.Single(expedition.PersistentBands);
        Assert.True(band.OnRoad);
        Assert.Equal(151.5, band.X, 6);

        // A save keeps it on the road, as far along and judged as it was.
        DaggerfallExpedition restored = new(Policy(), Sites(), DaggerfallExpeditionTuning.Default, world, expedition.Capture());
        DaggerfallPersistentOverlandBand kept = Assert.Single(restored.PersistentBands);
        Assert.Equal(("caravan", true, band.X, band.RoadSegment), (kept.BandId, kept.OnRoad, kept.X, kept.RoadSegment));

        // The party nearing brings it onto the map where it stood, and it goes on from there live.
        Pass(restored, world, 3600, (kept.X - 3, Row + .5));
        Assert.False(kept.OnRoad);
        Assert.Equal(band.X + 1, kept.X, 6);

        // Left far behind, it takes to the road again.
        Pass(restored, world, 3600, Home);
        Assert.True(kept.OnRoad);
    }

    [Fact]
    public void A_band_on_the_road_meets_what_the_region_holds_the_day_it_sets_out_along_each_segment()
    {
        // One keyed roll of 10 in a hundred: the road alone (delay 1) lets it by; a war of severity 3 (raid 9, delay 19)
        // delays it; raiders of severity 2 (raid 10) take it; a patrol the raiders cannot take is only delayed.
        static DaggerfallWorldConditionToken Token(DaggerfallWorldConditionKind kind, string? tag, int severity) =>
            new(TokenId: $"test:{kind}", Kind: kind, Region: 0, SubjectTag: tag, Severity: severity);

        (DaggerfallExpedition quiet, RoadWorld quietWorld) = Road(world => world.Draw = (_, _, _) => 10);
        quiet.RegisterPersistentBand(Caravan("caravan", 150.5));
        Pass(quiet, quietWorld, 6 * 3600, Home);
        Assert.Empty(quietWorld.Delayed);
        Assert.Empty(quietWorld.Retired);

        (DaggerfallExpedition war, RoadWorld warWorld) = Road(world =>
        {
            world.Draw = (_, _, _) => 10;
            world.Conditions = [Token(DaggerfallWorldConditionKind.BorderTension, null, 3)];
        });
        war.RegisterPersistentBand(Caravan("caravan", 150.5));
        Pass(war, warWorld, 3600, Home);
        Assert.Equal([("caravan", 6L * 3600)], warWorld.Delayed);
        DaggerfallPersistentOverlandBand held = Assert.Single(war.PersistentBands);
        double heldAt = held.X;
        Pass(war, warWorld, 3 * 3600, Home);
        Assert.Equal(heldAt, held.X);

        (DaggerfallExpedition raided, RoadWorld raidedWorld) = Road(world =>
        {
            world.Draw = (_, _, _) => 10;
            world.Conditions = [Token(DaggerfallWorldConditionKind.GrainShortage, DaggerfallRoadTuning.RaidersTag, 2)];
        });
        raided.RegisterPersistentBand(Caravan("caravan", 150.5));
        raided.RegisterPersistentBand(Caravan("patrol", 150.5, DaggerfallBandKind.Patrol));
        Pass(raided, raidedWorld, 3600, Home);
        Assert.Equal(["caravan"], raidedWorld.Retired);
        // The raid names the raiders' token it fell in, for a convoy to tell whose soldiers took it (#11180).
        Assert.Equal(["test:GrainShortage"], raidedWorld.RaidedUnder);
        Assert.Equal("patrol", Assert.Single(raided.PersistentBands).BandId);
        Assert.Equal([("patrol", 6L * 3600)], raidedWorld.Delayed);
    }

    [Fact]
    public void A_band_on_the_road_meets_the_same_road_however_its_time_is_cut()
    {
        static int Keyed(string key, int minimum, int maximum) =>
            minimum + (int)(StableHash.NonZeroFnv1a64(key) % (ulong)(maximum - minimum + 1));
        void Hazards(RoadWorld world)
        {
            world.Draw = Keyed;
            // A plague's quarantine delays (6 a point) and never raids, so the band goes the whole way to be compared.
            world.Conditions = [new("test:plague", DaggerfallWorldConditionKind.PlagueScare, Region: 0, Severity: 3)];
        }

        (DaggerfallExpedition whole, RoadWorld wholeWorld) = Road(Hazards);
        (DaggerfallExpedition hourly, RoadWorld hourlyWorld) = Road(Hazards);
        foreach (DaggerfallExpedition expedition in new[] { whole, hourly })
            expedition.RegisterPersistentBand(Caravan("caravan", 100.5, speed: .5));
        Pass(whole, wholeWorld, 3600, Home);
        Pass(hourly, hourlyWorld, 3600, Home);

        Pass(whole, wholeWorld, 7 * 24 * 3600, Home);
        for (int hour = 0; hour < 7 * 24; hour++) Pass(hourly, hourlyWorld, 3600, Home);

        Assert.NotEmpty(wholeWorld.Delayed);
        Assert.Equal(wholeWorld.Delayed, hourlyWorld.Delayed);
        Assert.Equal(wholeWorld.Retired, hourlyWorld.Retired);
        Assert.Equal(wholeWorld.Arrived, hourlyWorld.Arrived);
        Assert.Equal(whole.PersistentBands.Count, hourly.PersistentBands.Count);
        foreach ((DaggerfallPersistentOverlandBand a, DaggerfallPersistentOverlandBand b) in whole.PersistentBands.Zip(hourly.PersistentBands))
        {
            Assert.Equal((a.RouteStep, a.RoadSegment, a.HeldUntilSecond), (b.RouteStep, b.RoadSegment, b.HeldUntilSecond));
            Assert.Equal(a.X, b.X, 6);
        }
    }

    [Fact]
    public void Sixty_four_bands_on_the_road_through_a_four_week_rest_stay_within_the_budget()
    {
        (DaggerfallExpedition expedition, RoadWorld world) = Road(world =>
        {
            world.Draw = (key, minimum, maximum) => minimum + (int)(StableHash.NonZeroFnv1a64(key) % (ulong)(maximum - minimum + 1));
            world.Conditions = [new("test:surge", DaggerfallWorldConditionKind.MonsterSurge, Region: 0, Severity: 1)];
        });
        for (int i = 0; i < 64; i++)
            expedition.RegisterPersistentBand(Caravan($"caravan-{i}", 40.5 + i, speed: .05 + i % 4 * .02)
                with { Y = 2.5 + i % 15, Destination = new(new(Width - 4, 2 + (i * 7) % 15), null) });
        Pass(expedition, world, 3600, Home);

        // A four-week rest passes the road as the session does, a dispatch window of six hours at a time.
        Stopwatch watch = Stopwatch.StartNew();
        for (int window = 0; window < 4 * 7 * 4; window++) Pass(expedition, world, 6 * 3600, Home);
        watch.Stop();

        Assert.All(expedition.PersistentBands, band => Assert.True(band.OnRoad));
        Assert.True(world.Arrived.Count + world.Retired.Count + expedition.PersistentBands.Count == 64);
        // Measured at 4 to 5 ms on den-agents (#11178); the bound leaves room for a loaded machine.
        Assert.True(watch.ElapsedMilliseconds < 100, $"Four weeks on the road took {watch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void A_courier_lost_on_the_road_loses_its_move_and_leaves_no_one_on_the_road()
    {
        // The road raids every band at the first segment it sets out along.
        DaggerfallTuning tuning = Tuned(new(Raid: 100, Delay: 0));
        using DaggerfallSession session = Session(tuning);
        List<DaggerfallDispatchResolution> resolutions = [];
        session.CourierDispatches.BindBoardMoveCallback(resolutions.Add);
        DaggerfallCourierDispatchSave dispatch = Dispatch(session);
        string bandId = dispatch.OverlandBandId!;
        Assert.NotNull(dispatch.CourierActorId);

        session.AdvanceElapsedTime(3600);

        Assert.True(session.CourierDispatches.TryGet(dispatch.DispatchId, out DaggerfallCourierDispatchSave? lost));
        Assert.Equal(DaggerfallDispatchStatus.Waylaid, lost!.Status);
        DaggerfallDispatchResolution resolution = Assert.Single(resolutions);
        Assert.Equal((DaggerfallDispatchStatus.Waylaid, true, false), (resolution.Outcome, resolution.MoveCancelled, resolution.DeliveredByPlayer));
        Assert.DoesNotContain(session.Expedition.PersistentBands, band => band.BandId == bandId);
        Assert.Empty(session.State.ActorLedger.EntriesOnOverlandBand(bandId));
        _ = DaggerfallSavePayload.Read(session.CaptureSave());
    }

    [Fact]
    public void A_courier_delayed_on_the_road_comes_late_by_its_delays_and_keeps_itself_through_a_save()
    {
        // The road delays every band at every segment it sets out along.
        DaggerfallTuning tuning = Tuned(new(Raid: 0, Delay: 100));
        using DaggerfallSession session = Session(tuning);
        DaggerfallCourierDispatchSave dispatch = Dispatch(session);
        long scheduled = dispatch.ExpectedArrivalMinute;
        long delay = (long)(tuning.Expedition.Road.DelayHours * 60);

        session.AdvanceElapsedTime(3600);
        DaggerfallPersistentOverlandBand band = Assert.Single(session.Expedition.PersistentBands, band => band.BandId == dispatch.OverlandBandId);
        Assert.True(band.OnRoad);
        Assert.NotNull(band.HeldUntilSecond);
        Assert.True(session.CourierDispatches.TryGet(dispatch.DispatchId, out DaggerfallCourierDispatchSave? held));
        Assert.Equal(scheduled + delay, held!.ExpectedArrivalMinute);

        // A save keeps the courier on the road, held where it was, with its rider still on its band.
        using DaggerfallSession restored = Restore(session.CaptureSave(), tuning);
        DaggerfallPersistentOverlandBand kept = Assert.Single(restored.Expedition.PersistentBands, kept => kept.BandId == band.BandId);
        Assert.Equal((true, band.HeldUntilSecond, band.RoadSegment, band.X), (kept.OnRoad, kept.HeldUntilSecond, kept.RoadSegment, kept.X));
        ActorLedgerEntry rider = Assert.Single(restored.State.ActorLedger.EntriesOnOverlandBand(band.BandId));
        Assert.Equal(dispatch.CourierActorId, rider.DurableId);

        // It comes a delay late for each segment it set out along.
        for (int day = 0; day < 30 && restored.CourierDispatches.All.Any(d => d.DispatchId == dispatch.DispatchId && d.Status == DaggerfallDispatchStatus.InTransit); day++)
            restored.AdvanceElapsedTime(86400);
        Assert.True(restored.CourierDispatches.TryGet(dispatch.DispatchId, out DaggerfallCourierDispatchSave? delivered));
        Assert.Equal(DaggerfallDispatchStatus.Delivered, delivered!.Status);
        Assert.True(delivered.ExpectedArrivalMinute >= scheduled + 2 * delay);
        Assert.Equal(0, (delivered.ExpectedArrivalMinute - scheduled) % delay);
    }

    [Fact]
    public void A_session_resting_twenty_hours_at_once_moves_a_delayed_courier_as_twenty_hours_one_by_one_do()
    {
        // Every segment delays: a band resumes six hours after each, wherever the rest's windows fall.
        DaggerfallTuning tuning = Tuned(new(Raid: 0, Delay: 100));
        using DaggerfallSession whole = Session(tuning);
        using DaggerfallSession hourly = Session(tuning);
        DaggerfallCourierDispatchSave dispatch = Dispatch(whole);
        _ = Dispatch(hourly);

        // Twenty hours cross several of the rest's six-hour windows, short of the courier's door.
        whole.AdvanceElapsedTime(20 * 3600);
        for (int hour = 0; hour < 20; hour++) hourly.AdvanceElapsedTime(3600);

        DaggerfallPersistentOverlandBand a = Assert.Single(whole.Expedition.PersistentBands, band => band.BandId == dispatch.OverlandBandId);
        DaggerfallPersistentOverlandBand b = Assert.Single(hourly.Expedition.PersistentBands, band => band.BandId == dispatch.OverlandBandId);
        Assert.True(a.RoadSegment > 1, $"Twenty hours of six-hour holds should see the courier along several segments, not {a.RoadSegment}.");
        Assert.Equal((a.RouteStep, a.RoadSegment, a.HeldUntilSecond), (b.RouteStep, b.RoadSegment, b.HeldUntilSecond));
        Assert.Equal(a.X, b.X, 6);
        Assert.True(whole.CourierDispatches.TryGet(dispatch.DispatchId, out DaggerfallCourierDispatchSave? wholeDispatch));
        Assert.True(hourly.CourierDispatches.TryGet(dispatch.DispatchId, out DaggerfallCourierDispatchSave? hourlyDispatch));
        Assert.Equal(wholeDispatch!.ExpectedArrivalMinute, hourlyDispatch!.ExpectedArrivalMinute);
    }

    [Fact]
    public void A_hazard_that_lapses_during_a_rest_still_meets_the_courier_on_the_days_it_held()
    {
        // Raiders that take every band, held only until tomorrow, where the courier sets out today.
        DaggerfallTuning tuning = Tuned(new(Raid: 0, Delay: 0), new Dictionary<DaggerfallRoadHazard, DaggerfallRoadChances>
        {
            [DaggerfallRoadHazard.War] = new(0, 0), [DaggerfallRoadHazard.Raiders] = new(100, 0),
            [DaggerfallRoadHazard.Monsters] = new(0, 0), [DaggerfallRoadHazard.Plague] = new(0, 0),
        });
        foreach (bool atOnce in new[] { true, false })
        {
            using DaggerfallSession session = Session(tuning);
            DaggerfallCourierDispatchSave dispatch = Dispatch(session);
            int today = checked((int)session.Calendar.DayNumber);
            session.WorldConditions.Set(new DaggerfallWorldConditionToken(TokenId: "test:raiders", Kind: DaggerfallWorldConditionKind.GrainShortage,
                Region: dispatch.OriginSite.Require().Region, SubjectTag: DaggerfallRoadTuning.RaidersTag, Severity: 1, StartedDay: today, ExpiresDay: today + 1));

            if (atOnce) session.AdvanceElapsedTime(2 * 86400);
            else for (int hour = 0; hour < 48; hour++) session.AdvanceElapsedTime(3600);

            Assert.DoesNotContain(session.WorldConditions.ActiveForRegion(dispatch.OriginSite.Require().Region), token => token.TokenId == "test:raiders");
            Assert.True(session.CourierDispatches.TryGet(dispatch.DispatchId, out DaggerfallCourierDispatchSave? lost));
            Assert.Equal(DaggerfallDispatchStatus.Waylaid, lost!.Status);
        }
    }

    private static DaggerfallTuning Tuned(DaggerfallRoadChances wilds, IReadOnlyDictionary<DaggerfallRoadHazard, DaggerfallRoadChances>? hazards = null) =>
        DaggerfallTuning.Defaults with
        {
            Expedition = DaggerfallTuning.Defaults.Expedition with
            {
                Road = DaggerfallTuning.Defaults.Expedition.Road with { Wilds = wilds, Hazards = hazards ?? DaggerfallTuning.Defaults.Expedition.Road.Hazards },
            },
        };

    /// <summary>A board envoy between two towns well beyond the road distance of the player in Privateer's Hold.</summary>
    private static DaggerfallCourierDispatchSave Dispatch(DaggerfallSession session)
    {
        DaggerfallSiteRecord[] towns = [.. TestPayload.Definitions.Locations.Records.Where(site => site.Exterior is not null && site.Kind == DaggerfallSiteKind.TownCity)];
        DaggerfallSiteRecord hold = TestPayload.Definitions.Locations.Records.Single(site => site.Id == PrivateersHold);
        static double Apart(DaggerfallSiteRecord a, DaggerfallSiteRecord b) => Math.Sqrt(Math.Pow(a.MapPixelX - b.MapPixelX, 2) + Math.Pow(a.MapPixelY - b.MapPixelY, 2));
        (DaggerfallSiteRecord origin, DaggerfallSiteRecord destination) = towns.Where(town => Apart(town, hold) > 40)
            .SelectMany(town => towns.Where(other => other.Id.Region == town.Id.Region && Apart(town, other) is >= 12 and <= 24 && Apart(other, hold) > 40),
                (town, other) => (town, other))
            .First();
        long minute = session.Calendar.MinuteNumber;
        return session.CourierDispatches.StageDispatch("road-envoy", "crown:daggerfall", "envoy", "An envoy on the road", origin, destination,
            minute, checked((int)session.Calendar.DayNumber), stageOverlandBand: true);
    }

    private static DaggerfallSession Session(DaggerfallTuning tuning)
    {
        (EngineContextFake engine, DaggerfallSiteProfile inputs) = Engine();
        return DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, tuning));
    }

    private static DaggerfallSession Restore(RulesetSavePayload saved, DaggerfallTuning tuning)
    {
        (EngineContextFake engine, DaggerfallSiteProfile inputs) = Engine();
        return DaggerfallSession.Restore(engine.Context, new(TestPayload.Definitions, inputs, tuning), saved);
    }

    private static (EngineContextFake, DaggerfallSiteProfile) Engine()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
        return (EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service), inputs);
    }

    [Fact]
    public void A_band_kept_out_of_a_region_goes_around_its_shortcut_and_keeps_out_after_a_save()
    {
        // Two bordering regions, west (0) and east (1), and a third (2) jutting between them at (12, 10): the straight
        // way from (10, 10) to (14, 10) crosses it, the way round above or below it does not.
        const int width = Width + 1, height = 20;
        byte[] regions = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < Width; x++)
                regions[(y * width) + x + 1] = (byte)(x == 12 && y is >= 8 and <= 12 ? 130 : x < 12 ? 128 : 129);
        DaggerfallWorldGridsSet grids = new(new DaggerfallClimateGridDefinition(width, height, Enumerable.Repeat((byte)231, width * height).ToArray(),
                [new(231, "Woodlands", DaggerfallClimateDisposition.Named)]),
            new DaggerfallPoliticGridDefinition(width, height, regions,
                [new(128, 0, DaggerfallPoliticDisposition.Region), new(129, 1, DaggerfallPoliticDisposition.Region), new(130, 2, DaggerfallPoliticDisposition.Region)]));
        DaggerfallTravelPolicy policy = new(Sites(), grids, DaggerfallTuning.Defaults.Transport);
        DaggerfallTravelMapPixel from = new(10, Row), to = new(14, Row);
        Assert.Contains(policy.OverlandRoute(from, to).Waypoints, pixel => policy.RegionAt(pixel) == 2);

        DaggerfallOverlandRoute round = policy.OverlandRoute(from, to, [2]);
        Assert.DoesNotContain(round.Waypoints, pixel => policy.RegionAt(pixel) == 2);
        Assert.Equal(to, round.Waypoints[^1]);

        // A band kept out of it walks that way, and rebuilds the same way from its save.
        RoadWorld world = new();
        DaggerfallExpedition expedition = new(policy, Sites(), DaggerfallExpeditionTuning.Default, world);
        expedition.RegisterPersistentBand(new DaggerfallPersistentBandSave("convoy", "Convoy", DaggerfallBandKind.Convoy, from.X + .5, from.Y + .5, 90, 1,
            Destination: new(to, null), AvoidRegions: [2]));
        DaggerfallPersistentOverlandBand band = Assert.Single(expedition.PersistentBands);
        Assert.Equal(round.Waypoints, band.ResolveRoute(policy)!.Waypoints);
        DaggerfallExpedition restored = new(policy, Sites(), DaggerfallExpeditionTuning.Default, world, expedition.Capture());
        Assert.Equal(round.Waypoints, Assert.Single(restored.PersistentBands).ResolveRoute(policy)!.Waypoints);
        List<int> walked = [];
        for (int step = 0; step < 40 && restored.PersistentBands.Count > 0; step++)
        {
            long at = world.Calendar.ToAbsoluteSeconds();
            world.Calendar = DaggerfallCalendar.FromAbsoluteSeconds(at + 1800);
            _ = restored.AdvancePersistentBandsOffMap(at, at + 1800, (from.X + .5, from.Y + .5));
            if (restored.PersistentBands.FirstOrDefault() is { } moving) walked.Add(policy.RegionAt(new((int)moving.X, (int)moving.Y)));
        }
        Assert.Equal(["convoy"], world.Arrived);
        Assert.DoesNotContain(2, walked);
    }

    [Fact]
    public void A_band_with_no_way_that_keeps_out_waits_where_it_is_rather_than_take_a_forbidden_road()
    {
        // The origin's eight neighbours are all of region 2: no step leaves it without entering the region.
        const int width = Width + 1, height = 20;
        byte[] regions = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < Width; x++)
                regions[(y * width) + x + 1] = (byte)(Math.Max(Math.Abs(x - 10), Math.Abs(y - Row)) == 1 ? 130 : 128);
        DaggerfallWorldGridsSet grids = new(new DaggerfallClimateGridDefinition(width, height, Enumerable.Repeat((byte)231, width * height).ToArray(),
                [new(231, "Woodlands", DaggerfallClimateDisposition.Named)]),
            new DaggerfallPoliticGridDefinition(width, height, regions,
                [new(128, 0, DaggerfallPoliticDisposition.Region), new(130, 2, DaggerfallPoliticDisposition.Region)]));
        DaggerfallTravelPolicy policy = new(Sites(), grids, DaggerfallTuning.Defaults.Transport);
        DaggerfallTravelMapPixel from = new(10, Row), to = new(14, Row);
        Assert.NotEmpty(policy.OverlandRoute(from, to).Waypoints);
        Assert.Null(policy.OverlandRoute(from, to, [2]));

        // Live beside the party or out on the road, a band kept out of region 2 stays where it is.
        foreach ((double partyX, string tier) in new[] { (from.X + .5, "live"), (from.X + 60.5, "road") })
        {
            RoadWorld world = new();
            DaggerfallExpedition expedition = new(policy, Sites(), DaggerfallExpeditionTuning.Default, world);
            expedition.RegisterPersistentBand(new DaggerfallPersistentBandSave("convoy", "Convoy", DaggerfallBandKind.Convoy, from.X + .5, from.Y + .5, 90, 1,
                Destination: new(to, null), AvoidRegions: [2]));
            for (int hour = 0; hour < 12; hour++) Pass(expedition, world, 3600, (partyX, from.Y + .5));
            DaggerfallPersistentOverlandBand band = Assert.Single(expedition.PersistentBands);
            Assert.Equal((tier, from.X + .5, from.Y + .5), (tier, band.X, band.Y));
            Assert.Empty(world.Arrived);
        }
    }

    private static void Pass(DaggerfallExpedition expedition, RoadWorld world, long seconds, (double X, double Y) party)
    {
        long from = world.Calendar.ToAbsoluteSeconds();
        world.Calendar = DaggerfallCalendar.FromAbsoluteSeconds(from + seconds);
        _ = expedition.AdvancePersistentBandsOffMap(from, from + seconds, party);
    }

    private static DaggerfallPersistentBandSave Caravan(string id, double x, DaggerfallBandKind kind = DaggerfallBandKind.Caravan, double speed = 1) =>
        new(id, id, kind, x, Row + .5, 90, speed, Destination: new(new(190, Row), new(0, 1)));

    private static (DaggerfallExpedition, RoadWorld) Road(Action<RoadWorld>? setup = null)
    {
        RoadWorld world = new();
        setup?.Invoke(world);
        return (new DaggerfallExpedition(Policy(), Sites(), DaggerfallExpeditionTuning.Default, world), world);
    }

    private static DaggerfallTravelPolicy Policy() => new(Sites(), Grids(), DaggerfallTuning.Defaults.Transport);

    /// <summary>A strip of woodland <see cref="Width"/> pixels wide, all of region 0.</summary>
    private static DaggerfallWorldGridsSet Grids()
    {
        const int width = Width + 1, height = 20;
        return new(new DaggerfallClimateGridDefinition(width, height, Enumerable.Repeat((byte)231, width * height).ToArray(),
                [new(231, "Woodlands", DaggerfallClimateDisposition.Named)]),
            new DaggerfallPoliticGridDefinition(width, height, Enumerable.Repeat((byte)128, width * height).ToArray(),
                [new(128, 0, DaggerfallPoliticDisposition.Region)]));
    }

    private static DaggerfallSiteContext Sites()
    {
        DaggerfallSiteRecord[] records = [Site(new(0, 0), "Ashford", 2), Site(new(0, 1), "Farbrook", 190)];
        DaggerfallLocationSet locations = new([.. records.Select(site => (site.Id.Region, site.Id.Index))], records,
            Dungeons: 0, RegionGaps: 0, Regions: 1);
        return new(locations, null, null, [.. records.Select(site => site.Id)]);
    }

    private static DaggerfallSiteRecord Site(DaggerfallSiteId id, string name, int x) => new(id, name, MapId: 0, Longitude: x * 128,
        Latitude: (499 - Row) * 128, DungeonType: 0, Kind: DaggerfallSiteKind.TownCity, Discovered: true,
        Exterior: new DaggerfallSiteExterior(x, Row, 1, 1, 0, 0, false, 0, 0, 1, 0, 1));

    private sealed class RoadWorld : IDaggerfallExpeditionSurroundings
    {
        public DaggerfallCalendar Calendar { get; set; } = DaggerfallCalendar.FromAbsoluteSeconds(400L * DaggerfallCalendar.SecondsPerDay);
        public Func<string, int, int, int> Draw { get; set; } = (_, _, maximum) => maximum;
        public IReadOnlyList<DaggerfallWorldConditionToken> Conditions { get; set; } = [];
        public List<(string Band, long Seconds)> Delayed { get; } = [];
        public List<string> Retired { get; } = [];
        public List<string> Arrived { get; } = [];
        public DaggerfallTransportMode Transport => DaggerfallTransportMode.Foot;
        public bool OwnsShip => false;
        public bool FreeShipPassage => false;
        public double FatigueShare => 1;
        public bool Exhausted => false;
        public bool Defeated => false;
        public bool HeldElsewhere => false;
        public int PlayerLevel => 1;
        public double Scouting => 0;
        public double Concealment => 0;
        public DaggerfallWeatherKind Weather(int climate) => DaggerfallWeatherKind.Sunny;
        public bool TrySpendGold(int amount) => true;
        public long Pass(DaggerfallExpeditionStretch stretch) => stretch.Seconds;
        public void RecoverCampHour(bool sheltered) { }
        public void Collapse(bool inWater) { }
        public bool EncounterArrives(DaggerfallTravelMapPixel pixel, bool atLocation, long minute) => false;
        public int Roll(string key, int minimum, int maximum) => Draw(key, minimum, maximum);
        public void MeetBand(DaggerfallExpeditionBand band, DaggerfallTravelMapPixel pixel, DaggerfallEncounterStaging staging) { }
        public IReadOnlyList<DaggerfallWorldConditionToken> WorldConditionsOn(int region, int day) =>
            [.. Conditions.Where(token => token.Region == region && token.StartedDay <= day && (token.ExpiresDay is null || day < token.ExpiresDay))];
        public void PersistentBandDelayed(DaggerfallPersistentOverlandBand band, long seconds) => Delayed.Add((band.BandId, seconds));
        public List<string?> RaidedUnder { get; } = [];
        public void PersistentBandRetiredOffScreen(string bandId, long second, DaggerfallWorldConditionToken? raidedUnder)
        {
            Retired.Add(bandId);
            RaidedUnder.Add(raidedUnder?.TokenId);
        }
        public void PersistentBandArrived(DaggerfallPersistentOverlandBand band, long second) => Arrived.Add(band.BandId);
    }
}
