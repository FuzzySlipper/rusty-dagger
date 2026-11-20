using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Where the player stands on the world map, built from its inputs without a session.</summary>
public sealed class DaggerfallMapPositionTests
{
    private static readonly DaggerfallSiteId Wayrest = new(0, 0);
    private static readonly DaggerfallSiteId Hidden = new(0, 1);
    private static readonly DaggerfallSiteId Capital = new(0, 2);

    [Fact]
    public void The_world_map_position_is_the_exterior_cell_outdoors_and_the_sites_pixel_inside()
    {
        Stage stage = new();
        stage.Cell = new(2, 1);
        Assert.Equal(new DaggerfallTravelMapPixel(2, 1), stage.Position.Position());
        stage.Active = DaggerfallWorldProfileIds.Dungeon(Wayrest);
        Assert.Equal(new DaggerfallTravelMapPixel(1, 1), stage.Position.Position());
    }

    [Fact]
    public void A_map_reveals_one_unknown_location_of_the_region_and_then_has_nothing_left()
    {
        Stage stage = new();
        DaggerfallMapReveal first = stage.Position.RevealFromMap("unique:7");
        Assert.Equal(new DaggerfallMapReveal("Hidden", null), first);
        Assert.True(stage.Sites.IsDiscovered(Hidden));
        KeyedRngRequest draw = Assert.Single(stage.Random.Requests);
        Assert.Equal("daggerfall.inventory.map-use.v1", draw.Scope);
        Assert.Equal("region:0:unique:7", draw.Key);

        Assert.Equal(new DaggerfallMapReveal(null, "You have already discovered every location in this region."),
            stage.Position.RevealFromMap("unique:7"));
        Stage nowhere = new(atSite: false);
        Assert.Equal(new DaggerfallMapReveal(null, "A map can only reveal a location while you are in a region."),
            nowhere.Position.RevealFromMap("unique:7"));
    }

    private sealed class Stage
    {
        internal DaggerfallSiteContext Sites { get; }
        internal KeyedRandomFake Random { get; } = KeyedRandomFake.Create(0);
        internal DaggerfallMapPosition Position { get; }
        internal DaggerfallWorldProfileKey Active { get; set; } = DaggerfallWorldProfileIds.Exterior(Wayrest);
        internal DaggerfallExteriorCellId Cell { get; set; } = new(1, 1);

        internal Stage(bool atSite = true)
        {
            DaggerfallSiteRecord wayrest = Site(Wayrest, "Wayrest Hall", 1, 1, discovered: true);
            DaggerfallSiteRecord hidden = Site(Hidden, "Hidden", 4, 1, discovered: false);
            DaggerfallSiteRecord capital = Site(Capital, "Daggerfall", 3, 1, discovered: true);
            DaggerfallLocationSet locations = new([(0, 0), (0, 1), (0, 2)], [wayrest, hidden, capital], Dungeons: 0, RegionGaps: 0, Regions: 1);
            Sites = new(locations, atSite ? Wayrest : null, null, []);
            byte[] cells = Enumerable.Repeat((byte)231, 8 * 3).ToArray();
            DaggerfallWorldGridsSet grids = new(
                new DaggerfallClimateGridDefinition(8, 3, cells, [new(223, "Ocean", DaggerfallClimateDisposition.Named), new(231, "Woodlands", DaggerfallClimateDisposition.Named)]),
                new DaggerfallPoliticGridDefinition(1, 1, [128], [new(128, 0, DaggerfallPoliticDisposition.Region)]));
            Position = new(new DaggerfallTravelPolicy(Sites, grids, DaggerfallTuning.Defaults.Transport),
                new DaggerfallTransportPolicy(DaggerfallTuning.Defaults.Transport), Sites, Random.Service,
                new DaggerfallExteriorWorldBounds(8, 3), activeProfile: () => Active, exteriorCell: () => Cell);
        }
    }

    private static DaggerfallSiteRecord Site(DaggerfallSiteId id, string name, int mapPixelX, int mapPixelY, bool discovered) => new(
        id, name, MapId: 0, Longitude: mapPixelX * 128, Latitude: (499 - mapPixelY) * 128, DungeonType: 0,
        Kind: DaggerfallSiteKind.TownCity, Discovered: discovered,
        Exterior: new DaggerfallSiteExterior(mapPixelX, mapPixelY, 1, 1, 0, 0, false, 0, 0, 1, 0, 1));
}
