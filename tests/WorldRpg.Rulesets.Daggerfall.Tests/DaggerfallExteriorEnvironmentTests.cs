using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallExteriorEnvironmentTests
{
    [Fact]
    public void Ocean_clamp_publishes_queryable_water_volumes_in_the_cell_frame()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallExteriorEnvironment environment = new();
        environment.Reconcile([cell], DaggerfallExteriorWorldOrigin.At(cell),
            _ => Surface(0F), Locations(), Grids(224, "temperate"));

        Assert.Equal(256, environment.WaterVolumes.Count);
        Assert.True(environment.TryReadWater(new Vector3(25F, 10F, 25F), out DaggerfallExteriorWaterVolume volume));
        Assert.Equal(cell, volume.Cell);
        Assert.Equal(40.8F, volume.Maximum.Y, 3);
        Assert.False(environment.TryReadWater(new Vector3(900F, 10F, 25F), out _));
    }

    [Fact]
    public void Nature_is_deterministic_and_excludes_the_expanded_location_footprint()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallSiteExterior location = new(1, 0, 1, 1, 0, 0, false, 2, 60, 68, 60, 68);
        Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> locations = new() { [cell] = location };
        DaggerfallExteriorEnvironment first = new();
        DaggerfallExteriorEnvironment second = new();
        first.Reconcile([cell], DaggerfallExteriorWorldOrigin.At(cell), _ => Surface(100F), locations, Grids(224, "temperate"));
        second.Reconcile([cell], DaggerfallExteriorWorldOrigin.At(cell), _ => Surface(100F), locations, Grids(224, "temperate"));

        Assert.Equal(first.NaturePlacements, second.NaturePlacements);
        Assert.NotEmpty(first.NaturePlacements);
        Assert.DoesNotContain(first.NaturePlacements, placement =>
            placement.TileX >= 56 && placement.TileX <= 72 && placement.TileY >= 56 && placement.TileY <= 72);
    }

    [Fact]
    public void Winter_variant_uses_the_donor_archive_offset_only_for_non_desert_climates()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallExteriorEnvironment temperate = new(DaggerfallExteriorSeason.Winter);
        temperate.Reconcile([cell], DaggerfallExteriorWorldOrigin.At(cell), _ => Surface(0F), Locations(), Grids(231, "Woodlands"));
        DaggerfallExteriorEnvironment desert = new(DaggerfallExteriorSeason.Winter);
        desert.Reconcile([cell], DaggerfallExteriorWorldOrigin.At(cell), _ => Surface(0F), Locations(), Grids(224, "Desert"));

        Assert.Equal(303, Assert.Single(temperate.TerrainVariants).GroundTextureArchive);
        Assert.Equal(2, Assert.Single(desert.TerrainVariants).GroundTextureArchive);
        Assert.Equal(505, Assert.Single(temperate.TerrainVariants).NatureTextureArchive);
        Assert.Equal(503, Assert.Single(desert.TerrainVariants).NatureTextureArchive);
    }

    [Fact]
    public void Nature_object_ids_keep_cell_and_tile_coordinates_distinct()
    {
        ulong first = DaggerfallExteriorEnvironment.NatureObjectId(new(12, 34), 56, 78);
        ulong secondCell = DaggerfallExteriorEnvironment.NatureObjectId(new(12, 35), 56, 78);
        ulong secondTile = DaggerfallExteriorEnvironment.NatureObjectId(new(12, 34), 57, 78);

        Assert.NotEqual(first, secondCell);
        Assert.NotEqual(first, secondTile);
        Assert.Equal(first, DaggerfallExteriorEnvironment.NatureObjectId(new(12, 34), 56, 78));
    }

    private static DaggerfallTerrainSurface Surface(float height)
    {
        int count = DaggerfallTerrainSurfaceBuilder.SampleDimension * DaggerfallTerrainSurfaceBuilder.SampleDimension;
        Vector3[] vertices = Enumerable.Range(0, count)
            .Select(index => new Vector3(index % DaggerfallTerrainSurfaceBuilder.SampleDimension, height,
                index / DaggerfallTerrainSurfaceBuilder.SampleDimension)).ToArray();
        return new DaggerfallTerrainSurface(1, 0, vertices, [],
            Enumerable.Repeat(height / DaggerfallTerrainSurfaceBuilder.TerrainVerticalSize, count).ToArray());
    }

    private static Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> Locations() => [];

    private static DaggerfallWorldGridsSet Grids(int climateValue, string climateName)
    {
        DaggerfallClimateGridDefinition climate = new(3, 1, [0, 0, (byte)climateValue],
            [new DaggerfallClimateValueDefinition(climateValue, climateName, DaggerfallClimateDisposition.Named)]);
        DaggerfallPoliticGridDefinition politic = new(3, 1, [64, 64, 64], []);
        return new(climate, politic);
    }
}
