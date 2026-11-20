using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallTravelPolicyTests
{
    [Fact]
    public void A_pixel_costs_the_donors_minutes_by_transport_over_its_terrain_and_at_sea_by_ship_or_swimming()
    {
        DaggerfallTravelPolicy policy = CreatePolicy((x, y) => x >= 2 && x <= 4 ? 223 : 231);
        // Woodland: the donor's foot, cart and horse modifiers over its terrain, without the camp-out share.
        Assert.Equal(104, policy.PixelMinutes(new(1, 1), DaggerfallTransportMode.Foot, aboard: false));
        Assert.Equal(77, policy.PixelMinutes(new(1, 1), DaggerfallTransportMode.Cart, aboard: false));
        Assert.Equal(52, policy.PixelMinutes(new(1, 1), DaggerfallTransportMode.Horse, aboard: false));
        Assert.True(policy.IsOcean(new(3, 1)));
        Assert.Equal(51, policy.PixelMinutes(new(3, 1), DaggerfallTransportMode.Horse, aboard: true));
        Assert.Equal(255, policy.PixelMinutes(new(3, 1), DaggerfallTransportMode.Foot, aboard: false));
        Assert.Equal("Woodlands", policy.ClimateName(new(1, 1)));
    }

    /// <summary>
    /// Privateer's Hold sits on the mainland coast in CLIMATE.PAK (map pixel 109,158, woodland), so the donor walk to
    /// Daggerfall crosses no ocean pixel; a route across the bay to Sentinel is mostly sea.
    /// </summary>
    [Fact]
    public void Shipped_climate_routes_privateers_hold_to_daggerfall_over_land_and_daggerfall_to_sentinel_over_sea()
    {
        DaggerfallTravelPolicy policy = new(new DaggerfallSiteContext(TestPayload.Definitions.Locations),
            TestPayload.Definitions.Grids, DaggerfallTuning.Defaults.Transport);
        DaggerfallTravelMapPixel At(DaggerfallSiteId id) => TestPayload.Definitions.Locations.Records.Single(site => site.Id == id) is var site
            ? new(site.MapPixelX, site.MapPixelY) : default;
        IReadOnlyList<DaggerfallTravelMapPixel> overland = DaggerfallTravelPolicy.Route(new(109, 158), At(new(17, 1231)));
        Assert.Equal((98, 0), (overland.Count, overland.Count(policy.IsOcean)));

        IReadOnlyList<DaggerfallTravelMapPixel> bay = DaggerfallTravelPolicy.Route(new(207, 213), At(new(20, 6)));
        Assert.Equal((190, 174), (bay.Count, bay.Count(policy.IsOcean)));
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
    public void A_climate_the_donor_has_no_terrain_for_is_refused()
    {
        DaggerfallTravelPolicy unresolved = CreatePolicy((x, y) => x == 3 ? 7 : 231);
        Assert.Throws<InvalidOperationException>(() => unresolved.PixelMinutes(new(3, 1), DaggerfallTransportMode.Foot, aboard: false));
        Assert.Throws<InvalidOperationException>(() => unresolved.CautiousQuestLegMinutes(new(1, 1), new(0, 1)));
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
        return new DaggerfallTravelPolicy(sites, grids, DaggerfallTuning.Defaults.Transport);
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
