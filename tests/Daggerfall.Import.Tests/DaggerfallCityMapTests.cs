using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DaggerfallCityMapTests
{
    [Fact]
    public void Source_corners_keep_north_south_coordinates_and_ordinary_kinds()
    {
        byte[] cells = new byte[4096];
        cells[0] = cells[1] = 16; cells[63] = 12; cells[4032] = 15; cells[4095] = 9;
        cells[64] = 251; cells[65] = 25; cells[66] = 224;
        var map = DaggerfallCityMapBuilder.Build("TEST.RMB", cells);
        Assert.Equal(102.4f, map.BlockSize);
        Assert.Equal(4, map.Footprints.Count);
        var northWest = map.Footprints[0];
        Assert.Equal(0, northWest.MinX); Assert.Equal(3.2f, northWest.MaxX);
        Assert.Equal(-102.4f, northWest.MinZ); Assert.Equal(-100.8f, northWest.MaxZ);
        var southEast = map.Footprints.Last();
        Assert.Equal(100.8f, southEast.MinX); Assert.Equal(0, southEast.MaxZ); Assert.Equal(-1.6f, southEast.MinZ);
        Assert.DoesNotContain(map.Footprints, rect => rect.Kind is 251 or 25 or 224);
        Assert.Throws<ArgumentException>(() => DaggerfallCityMapBuilder.Build("TEST.RMB", new byte[4095]));
    }

    [CorpusFact]
    public void Every_compacted_map_roundtrips_the_ordinary_source_cells_without_losing_block_identity()
    {
        var inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "data/content-source-manifest.csv")));
        var blocks = DaggerfallBlocksBuilder.Build(File.ReadAllBytes(TestData.Corpus("BLOCKS.BSA")), "arena2/BLOCKS.BSA", inventory);
        var published = DaggerfallBlockBuildingSet.From(blocks);
        Assert.Equal(920, published.Maps.Count);
        Assert.Equal(9005, published.Buildings.Count);
        foreach (var map in published.Maps)
        {
            var source = blocks.Records.Single(record => record.SourceKey == map.Block).Rmb!;
            byte[] restored = new byte[4096];
            foreach (var rect in map.Footprints)
            {
                int row = 64 + (int)MathF.Round(rect.MinZ / 1.6f);
                int start = (int)MathF.Round(rect.MinX / 1.6f), end = (int)MathF.Round(rect.MaxX / 1.6f);
                for (int x = start; x < end; x++) { Assert.Equal(0, restored[row * 64 + x]); restored[row * 64 + x] = (byte)rect.Kind; }
            }
            Assert.Equal(source.AutoMapData.Select(kind => kind is 25 or 117 or 224 or 250 or 251 ? (byte)0 : kind), restored);
            foreach (var building in source.Buildings)
                Assert.Equal(building.MapPosition, published.Buildings.Single(value => value.Block == map.Block && value.Index == building.Index).MapPosition);
        }
    }
}
