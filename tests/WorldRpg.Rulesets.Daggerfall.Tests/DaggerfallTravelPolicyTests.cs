using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallTravelPolicyTests
{
    [Fact]
    public void Quote_uses_the_donor_route_walk_and_stable_canonical_options()
    {
        DaggerfallTravelPolicy policy = CreatePolicy();
        DaggerfallTravelMapPixel origin = new(1, 1);
        DaggerfallSiteId destination = new(0, 1);
        DaggerfallTravelOptions options = new(
            SpeedCautious: true,
            SleepModeInn: true,
            TravelShip: false,
            HasHorse: false,
            HasCart: true,
            HasShip: false,
            AvailableGold: 100,
            AvailableGoldPieces: 100);

        DaggerfallTravelQuote first = policy.Quote(origin, destination, options, requireDiscovered: false);
        DaggerfallTravelQuote second = policy.Quote(new(1, 1), new(0, 1), options, requireDiscovered: false);

        Assert.Equal(3, first.DistanceMapPixels);
        Assert.Equal(0, first.OceanPixels);
        Assert.Equal(231, first.TravelMinutes);
        Assert.Equal(5, first.InnCost);
        Assert.Equal(0, first.ShipCost);
        Assert.Equal(5, first.TotalCost);
        Assert.True(first.CanAfford);
        Assert.Equal(first.Identity, second.Identity);
        Assert.Equal("daggerfall.travel.v1/1,1/0,1/cautious=1;inn=1;ship=0;horse=0;cart=1;owned-ship=0;gold=100;pieces=100", first.Identity);

        DaggerfallTravelQuote changed = policy.Quote(origin, destination, options with { SpeedCautious = false }, requireDiscovered: false);
        Assert.NotEqual(first.Identity, changed.Identity);
        Assert.Equal(first.TravelMinutes / 2, changed.TravelMinutes);

        DaggerfallTravelQuote moneyChanged = policy.Quote(origin, destination,
            options with { AvailableGold = 4 }, requireDiscovered: false);
        Assert.NotEqual(first.Identity, moneyChanged.Identity);
        Assert.False(moneyChanged.CanAfford);
    }

    [Fact]
    public void Ocean_route_uses_ship_minutes_and_never_reproduces_negative_fare()
    {
        DaggerfallTravelPolicy policy = CreatePolicy((x, y) => x >= 2 && x <= 4 ? 223 : 231);
        DaggerfallTravelOptions charter = new(
            SpeedCautious: true,
            SleepModeInn: true,
            TravelShip: true,
            HasHorse: false,
            HasCart: false,
            HasShip: false);

        DaggerfallTravelQuote quote = policy.Quote(new(1, 1), new(0, 1), charter, requireDiscovered: false);

        Assert.Equal(3, quote.OceanPixels);
        Assert.Equal(153, quote.TravelMinutes);
        Assert.Equal(5, quote.InnCost);
        Assert.Equal(25, quote.ShipCost);
        Assert.Equal(30, quote.TotalCost);
        Assert.True(quote.InnCost >= 0);
        Assert.True(quote.ShipCost >= 0);
        Assert.True(quote.TotalCost >= 0);

        DaggerfallTravelQuote owned = policy.Quote(new(1, 1), new(0, 1), charter with { HasShip = true }, requireDiscovered: false);
        Assert.Equal(0, owned.ShipCost);
        Assert.Equal(5, owned.TotalCost);
    }

    [Fact]
    public void Quest_clock_legs_use_cautious_cart_facts_and_one_day_minimum()
    {
        DaggerfallTravelPolicy policy = CreatePolicy();

        int oneWay = policy.CautiousQuestLegMinutes(new(1, 1), new(0, 1));

        // Three donor cart/camp movements take 270 minutes, then Clock applies its one-day minimum.
        Assert.Equal(24 * 60, oneWay);
        Assert.Equal(24 * 60, policy.CautiousQuestLegMinutes(new(1, 1), new(0, 0)));
        Assert.Equal(216_000, DaggerfallTravelPolicy.ReturnTripSeconds(DaggerfallTravelPolicy.ToQuestSeconds(oneWay)));
        Assert.Equal(216_000, DaggerfallTravelPolicy.ToQuestSeconds(3_600));
        Assert.Equal(216_150, DaggerfallTravelPolicy.ReturnTripSeconds(DaggerfallTravelPolicy.ToQuestSeconds(1_441)));
    }

    [Fact]
    public void Destination_preview_is_discovered_and_exterior_backed_in_identity_order()
    {
        DaggerfallTravelPolicy policy = CreatePolicy();

        IReadOnlyList<DaggerfallTravelDestination> destinations = policy.SupportedDestinations();

        Assert.Equal([new DaggerfallSiteId(0, 0)], destinations.Select(destination => destination.Id));
        Assert.Equal("Known", destinations[0].Name);
        Assert.Equal(DaggerfallSiteKind.TownCity, destinations[0].Kind);
        Assert.Equal(new DaggerfallTravelMapPixel(1, 1), destinations[0].MapPixel);
    }

    [Fact]
    public void Unknown_undiscovered_same_and_invalid_climate_routes_are_rejected()
    {
        DaggerfallTravelPolicy policy = CreatePolicy();
        DaggerfallTravelOptions options = new(true, false, false, false, false, false);

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Quote(new(-1, 1), new(0, 1), options));
        Assert.Throws<InvalidOperationException>(() => policy.Quote(new(1, 1), new(9, 9), options));
        Assert.Throws<InvalidOperationException>(() => policy.Quote(new(1, 1), new(0, 2), options));
        Assert.Throws<InvalidOperationException>(() => policy.Quote(new(1, 1), new(0, 0), options));

        DaggerfallTravelPolicy unresolved = CreatePolicy((x, y) => x == 3 ? 7 : 231);
        Assert.Throws<InvalidOperationException>(() => unresolved.Quote(new(1, 1), new(0, 1), options, requireDiscovered: false));
    }

    private static DaggerfallTravelPolicy CreatePolicy(Func<int, int, int>? climate = null)
    {
        const int width = 8;
        const int height = 3;
        byte[] cells = Enumerable.Repeat((byte)231, width * height).ToArray();
        if (climate is not null)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    cells[(y * width) + x] = checked((byte)climate(x - 1, y));
            }
        }

        DaggerfallClimateGridDefinition climateGrid = new(width, height, cells,
        [
            new(223, "Ocean", DaggerfallClimateDisposition.Named),
            new(231, "Woodlands", DaggerfallClimateDisposition.Named),
        ]);
        DaggerfallPoliticGridDefinition politicGrid = new(1, 1, [128],
            [new(128, 0, DaggerfallPoliticDisposition.Region)]);
        DaggerfallWorldGridsSet grids = new(climateGrid, politicGrid);

        DaggerfallSiteRecord known = Site(new(0, 0), "Known", 1, 1, discovered: true);
        DaggerfallSiteRecord hidden = Site(new(0, 1), "Hidden", 4, 1, discovered: false);
        DaggerfallLocationSet locations = new(
            [(0, 0), (0, 1)], [known, hidden], Dungeons: 0, RegionGaps: 0, Regions: 1);
        DaggerfallSiteContext sites = new(locations, null, null, [known.Id]);
        return new DaggerfallTravelPolicy(sites, grids);
    }

    private static DaggerfallSiteRecord Site(DaggerfallSiteId id, string name, int mapPixelX, int mapPixelY,
        bool discovered) => new(
        id,
        name,
        MapId: 0,
        Longitude: mapPixelX * 128,
        Latitude: (499 - mapPixelY) * 128,
        DungeonType: 0,
        Kind: DaggerfallSiteKind.TownCity,
        Discovered: discovered,
        Exterior: new DaggerfallSiteExterior(mapPixelX, mapPixelY, 1, 1, 0, 0, false, 0, 0, 1, 0, 1));
}
