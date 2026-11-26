using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Expedition map band readability and inspection (#11079):
/// distinct visual kinds/categories for caravans, couriers/sealed envoys, patrols, hostile pursuit squads,
/// and active faction clashes; revealed vs unrevealed envoy detail gating; discovered-place route gating;
/// and strict PartySight visibility gating so unspotted bands outside sight are never leaked.
/// </summary>
public sealed class ExpeditionBandReadabilityTests
{
    private const int DaggerfallRegion = 17;

    [Fact]
    public void Expedition_panel_projects_distinct_band_categories_clash_sides_gating_and_revealed_envoy_details()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord origin = definitions.Locations.Records.First(r =>
            r.Id.Region == DaggerfallRegion && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteRecord knownDest = definitions.Locations.Records.First(r =>
            r.Id.Region == DaggerfallRegion && r.Id != origin.Id && r.Exterior is not null && r.Discovered);
        DaggerfallSiteRecord unknownDest = definitions.Locations.Records.First(r =>
            r.Id.Region == DaggerfallRegion && r.Id != origin.Id && r.Id != knownDest.Id && r.Exterior is not null && !r.Discovered);

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
        double sight = session.Expedition.PartySight;
        Assert.True(sight >= 1.0);

        // 1. Stage a caravan bound for a DISCOVERED town within PartySight, and another far outside PartySight.
        session.Expedition.RegisterPersistentBand(new DaggerfallPersistentBandSave(
            BandId: "traffic:901:caravan",
            Name: "Daggerfall Caravan",
            Kind: DaggerfallBandKind.Caravan,
            X: px + 0.5,
            Y: py + 0.2,
            Heading: 90d,
            SpeedPixelsPerHour: 1.0,
            Destination: new DaggerfallExpeditionMark(new(knownDest.MapPixelX, knownDest.MapPixelY), knownDest.Id),
            Faction: "Daggerfall",
            OriginSite: new DaggerfallSiteIdSave(origin.Id.Region, origin.Id.Index)));

        session.Expedition.RegisterPersistentBand(new DaggerfallPersistentBandSave(
            BandId: "traffic:902:caravan",
            Name: "Hidden Far Caravan",
            Kind: DaggerfallBandKind.Caravan,
            X: Math.Min(DaggerfallTravelMapPixel.Width - 1.5, px + sight + 10.0),
            Y: py,
            Heading: 90d,
            SpeedPixelsPerHour: 1.0,
            Destination: new DaggerfallExpeditionMark(new(knownDest.MapPixelX, knownDest.MapPixelY), knownDest.Id)));

        // 2. Stage a hostile bounty hunter squad within sight.
        session.Expedition.RegisterPersistentBand(new DaggerfallPersistentBandSave(
            BandId: "hostile:bounty:903",
            Name: "Daggerfall Bounty Hunters",
            Kind: DaggerfallBandKind.Hostile,
            X: px - 0.6,
            Y: py + 0.1,
            Heading: 90d,
            SpeedPixelsPerHour: 2.2,
            Hostile: true,
            Faction: "Daggerfall"));

        // 3. Stage a sealed board-game envoy heading to an UNDISCOVERED site within sight:
        //    unrevealed dispatch MUST NOT leak its secret summary OR the undiscovered destination name!
        long nowMinute = ((IDaggerfallExpeditionSurroundings)session).Calendar.MinuteNumber;
        int nowDay = checked((int)((IDaggerfallExpeditionSurroundings)session).Calendar.DayNumber);
        DaggerfallCourierDispatchSave dispatch = session.CourierDispatches.StageDispatch(
            dispatchId: "disp:readability-test",
            seatId: "crown:daggerfall",
            actionKind: "secret-embargo",
            summary: "Secret embargo against Wayrest merchants",
            origin: origin,
            destination: unknownDest,
            currentMinute: nowMinute,
            currentDay: nowDay,
            bribePriceGold: 50,
            stageOverlandBand: true);
        DaggerfallPersistentOverlandBand envoyBand = Assert.Single(
            session.Expedition.PersistentBands, b => b.BandId == dispatch.OverlandBandId);
        envoyBand.X = px + 0.3;
        envoyBand.Y = py - 0.3;

        // 4. Stage an active two-sided faction clash via opposing world conditions within sight!
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            "cond:monster-surge:r17", DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, Severity: 2, StartedDay: nowDay, SubjectTag: "orcs"));
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            "cond:border-tension:r17", DaggerfallWorldConditionKind.BorderTension, DaggerfallRegion, Severity: 2, StartedDay: nowDay));
        string? stageErr = null;
        for (int bearing = 0; bearing < 360; bearing += 30)
        {
            stageErr = session.Expedition.StageBand(Math.Min(0.8, sight * 0.5), bearing, camp: false);
            if (stageErr is null) break;
        }
        Assert.Null(stageErr);

        // Read the UI projection via DaggerfallSession (same surface published to the TS UI).
        DaggerfallExpeditionView view = session.TravelPanel.Read()!;
        Assert.NotNull(view);

        // Unspotted band outside PartySight MUST NOT appear!
        Assert.DoesNotContain(view.Bands, b => b.PersistentBandId == "traffic:902:caravan");

        // Caravan shows Caravan kind/category + discovered origin & destination.
        DaggerfallExpeditionBandView caravanView = Assert.Single(view.Bands, b => b.PersistentBandId == "traffic:901:caravan");
        Assert.Equal(DaggerfallBandKind.Caravan, caravanView.Kind);
        Assert.Equal("Caravan", caravanView.Category);
        Assert.Equal("Daggerfall", caravanView.Faction);
        Assert.Equal(origin.Name, caravanView.OriginName);
        Assert.Equal(knownDest.Name, caravanView.DestinationName);
        Assert.False(caravanView.Hostile);
        Assert.False(caravanView.IsClash);

        // Hostile pursuit squad shows Hostile kind + Bounty Hunters category + Hostile flag.
        DaggerfallExpeditionBandView hostileView = Assert.Single(view.Bands, b => b.PersistentBandId == "hostile:bounty:903");
        Assert.Equal(DaggerfallBandKind.Hostile, hostileView.Kind);
        Assert.Equal("Bounty Hunters", hostileView.Category);
        Assert.True(hostileView.Hostile);

        // Unrevealed sealed envoy hides secret summary AND hides undiscovered destination!
        DaggerfallExpeditionBandView sealedEnvoy = Assert.Single(view.Bands, b => b.PersistentBandId == dispatch.OverlandBandId);
        Assert.Equal(DaggerfallBandKind.Courier, sealedEnvoy.Kind);
        Assert.Equal("Sealed Envoy", sealedEnvoy.Category);
        Assert.False(string.IsNullOrWhiteSpace(sealedEnvoy.Faction));
        Assert.Null(sealedEnvoy.DestinationName);
        Assert.DoesNotContain("Secret embargo", sealedEnvoy.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // After buying/revealing the dispatch, the inspection detail now reveals the dispatch summary!
        session.State.Inventory.Grant(new InventoryGrant(
            new InventoryItemId("gold-piece"),
            InventoryStackId.Parse("test.bribe.gold"), 200));
        DaggerfallDispatchActionResult bribe = session.CourierDispatches.BuyDispatch(dispatch.DispatchId, nowMinute, nowDay);
        Assert.True(bribe.Accepted);
        DaggerfallExpeditionView revealedView = session.TravelPanel.Read()!;
        DaggerfallExpeditionBandView revealedEnvoy = Assert.Single(revealedView.Bands, b => b.PersistentBandId == dispatch.OverlandBandId);
        Assert.Contains("Secret embargo against Wayrest merchants", revealedEnvoy.Detail ?? string.Empty, StringComparison.Ordinal);

        // Staged condition clash projects Clash kind, IsClash=true, and opposing ClashSides!
        DaggerfallExpeditionBandView clashView = Assert.Single(revealedView.Bands, b => b.IsClash);
        Assert.Equal(DaggerfallBandKind.Clash, clashView.Kind);
        Assert.Equal("Faction Clash", clashView.Category);
        Assert.Contains(" vs ", clashView.ClashSides, StringComparison.Ordinal);
        Assert.Contains("Active clash", clashView.Detail ?? string.Empty, StringComparison.Ordinal);

        // R11079-1: Two simultaneous same-seat envoys sharing kind and name receive strictly distinct unique Ids.
        DaggerfallCourierDispatchSave secondDispatch = session.CourierDispatches.StageDispatch(
            dispatchId: "disp:readability-test-2",
            seatId: "crown:daggerfall",
            actionKind: "border-orders",
            summary: "Second envoy orders",
            origin: origin,
            destination: knownDest,
            currentMinute: nowMinute,
            currentDay: nowDay,
            stageOverlandBand: true);
        DaggerfallPersistentOverlandBand secondEnvoyBand = Assert.Single(
            session.Expedition.PersistentBands, b => b.BandId == secondDispatch.OverlandBandId);
        secondEnvoyBand.X = px + 0.35;
        secondEnvoyBand.Y = py - 0.35;

        DaggerfallExpeditionView twoEnvoysView = session.TravelPanel.Read()!;
        DaggerfallExpeditionBandView[] sameNameEnvoys = [.. twoEnvoysView.Bands.Where(b => b.Name == revealedEnvoy.Name && b.Kind == DaggerfallBandKind.Courier)];
        Assert.Equal(2, sameNameEnvoys.Length);
        Assert.NotEqual(sameNameEnvoys[0].Id, sameNameEnvoys[1].Id);
    }
}
