using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests.Exterior;

public sealed class DaggerfallOverlandRoadsTests
{
    [Fact]
    public void Connects_land_settlements_in_the_real_world_and_stays_off_open_ocean()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        Func<int, int, int> climate = (x, y) => definitions.Grids.ClimateAtWorldPixel(x, y).Value;
        DaggerfallOverlandRoads roads = new(definitions.Terrain, climate, definitions.Locations.Records);

        Assert.True(roads.SegmentCount > 500, $"Expected regional road network across Iliac Bay, got {roads.SegmentCount} segments.");

        // Daggerfall City (region 17) should have a visible trade highway leaving its map pixel.
        DaggerfallSiteRecord daggerfall = definitions.Locations.Records.First(s =>
            s.Region == 17 && s.Kind == DaggerfallSiteKind.TownCity && s.Name.Equals("Daggerfall", StringComparison.OrdinalIgnoreCase));
        double dfX = (daggerfall.MapPixelX + .5) * DaggerfallWorldGrid.HorizontalSize;
        double dfZ = (daggerfall.MapPixelY - .5) * DaggerfallWorldGrid.HorizontalSize;
        Assert.True(roads.Sample(dfX, dfZ, 51.2) > .4f, "Expected a highway reaching Daggerfall City.");

        // Sentinel City (region 20) should also connect to its regional road network.
        DaggerfallSiteRecord sentinel = definitions.Locations.Records.First(s =>
            s.Region == 20 && s.Kind == DaggerfallSiteKind.TownCity && s.Name.Equals("Sentinel", StringComparison.OrdinalIgnoreCase));
        double snX = (sentinel.MapPixelX + .5) * DaggerfallWorldGrid.HorizontalSize;
        double snZ = (sentinel.MapPixelY - .5) * DaggerfallWorldGrid.HorizontalSize;
        Assert.True(roads.Sample(snX, snZ, 51.2) > .4f, "Expected a highway reaching Sentinel City.");

        // Wayrest City (region 23) should also connect to its regional road network.
        DaggerfallSiteRecord wayrest = definitions.Locations.Records.First(s =>
            s.Region == 23 && s.Kind == DaggerfallSiteKind.TownCity && s.Name.Equals("Wayrest", StringComparison.OrdinalIgnoreCase));
        double wrX = (wayrest.MapPixelX + .5) * DaggerfallWorldGrid.HorizontalSize;
        double wrZ = (wayrest.MapPixelY - .5) * DaggerfallWorldGrid.HorizontalSize;
        Assert.True(roads.Sample(wrX, wrZ, 51.2) > .4f, $"Expected a highway reaching Wayrest City at ({wayrest.MapPixelX}, {wayrest.MapPixelY}).");

        // Open Iliac Bay ocean pixel must carry zero road coverage.
        double bayX = (260 + .5) * DaggerfallWorldGrid.HorizontalSize;
        double bayZ = (290 - .5) * DaggerfallWorldGrid.HorizontalSize;
        Assert.Equal(223, climate(260, 290));
        Assert.Equal(0f, roads.Sample(bayX, bayZ, 51.2));

        // Coarse strategic ring (> 260 m cells) fades roads out cleanly to prevent aliasing.
        Assert.Equal(0f, roads.Sample(dfX, dfZ, 819.2));
    }

    [Fact]
    public void Palette_blends_worn_roads_by_climate_softens_in_winter_and_fades_on_steep_cliffs()
    {
        DaggerfallMapPalette palette = DaggerfallMapPalette.Default;
        Vector4 grass = new(1f, 0f, 0f, 0f);
        Vector4 sheerRock = new(0f, 0f, 1f, 0f);

        Vector3 bareGrass = palette.Ground(231, grass, lush: false, patch: .5f, woods: 0f, winter: false, road: 0f);
        Vector3 summerRoad = palette.Ground(231, grass, lush: false, patch: .5f, woods: 0f, winter: false, road: .85f);
        Vector3 winterSnow = palette.Ground(231, grass, lush: false, patch: .5f, woods: 0f, winter: true, road: 0f);
        Vector3 winterRoad = palette.Ground(231, grass, lush: false, patch: .5f, woods: 0f, winter: true, road: .85f);
        Vector3 bareCliff = palette.Ground(226, sheerRock, lush: false, patch: .5f, woods: 0f, winter: false, road: 0f);
        Vector3 cliffWithRoad = palette.Ground(226, sheerRock, lush: false, patch: .5f, woods: 0f, winter: false, road: .85f);

        Assert.True(summerRoad.X > bareGrass.X + .12f, "Temperate road should read warmer ochre-earth over green grass.");
        Assert.True(Vector3.Distance(winterRoad, winterSnow) < Vector3.Distance(summerRoad, bareGrass),
            "Winter road should soften naturally under snow rather than cutting as harsh summer dirt.");
        Assert.True(Vector3.Distance(cliffWithRoad, bareCliff) < 1e-4f,
            "Steep 100% rock cliff should completely reject road colour bleed.");
    }

    [Fact]
    public void Point_segment_distance_handles_endpoints_midpoints_and_degenerate_segments()
    {
        Assert.Equal(0f, DaggerfallOverlandRoads.PointSegmentDistance(5f, 0f, 0f, 0f, 10f, 0f), 4);
        Assert.Equal(3f, DaggerfallOverlandRoads.PointSegmentDistance(5f, 3f, 0f, 0f, 10f, 0f), 4);
        Assert.Equal(5f, DaggerfallOverlandRoads.PointSegmentDistance(-3f, 4f, 0f, 0f, 10f, 0f), 4);
        Assert.Equal(5f, DaggerfallOverlandRoads.PointSegmentDistance(3f, 4f, 0f, 0f, 0f, 0f), 4);
    }
}
