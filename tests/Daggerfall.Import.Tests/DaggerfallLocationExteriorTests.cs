using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DaggerfallLocationExteriorTests
{
    [Fact]
    public void Index_keyed_exterior_layout_preserves_duplicate_names_in_the_same_region()
    {
        BsaArchive maps = BsaArchive.Parse(File.ReadAllBytes(Corpus("MAPS.BSA")), "arena2/MAPS.BSA");
        MapsLocationRecord[] duplicates = MapsDecoder.DecodeRegionLocations(maps, 17)
            .GroupBy(location => location.Name, StringComparer.Ordinal)
            .First(group => group.Count() > 1)
            .ToArray();

        MapsExteriorLayout first = MapsDecoder.DecodeExteriorLayout(maps, 17, duplicates[0].Index);
        MapsExteriorLayout second = MapsDecoder.DecodeExteriorLayout(maps, 17, duplicates[1].Index);

        Assert.Equal(duplicates[0].Index, first.LocationIndex);
        Assert.Equal(duplicates[1].Index, second.LocationIndex);
        Assert.NotEqual(first.LocationIndex, second.LocationIndex);
        Assert.Equal(duplicates[0].Name, first.LocationName);
        Assert.Equal(duplicates[1].Name, second.LocationName);
    }

    [Fact]
    public void Publishes_complete_exterior_metadata_for_every_real_location()
    {
        BsaArchive maps = BsaArchive.Parse(File.ReadAllBytes(Corpus("MAPS.BSA")), "arena2/MAPS.BSA");
        BsaArchive blocks = BsaArchive.Parse(File.ReadAllBytes(Corpus("BLOCKS.BSA")), "arena2/BLOCKS.BSA");

        DaggerfallLocations locations = DaggerfallLocationBuilder.Build(maps, blocks);

        Assert.Equal(15251, locations.Locations.Count);
        Assert.Equal(15251, locations.Locations.Count(location => location.Exterior is not null));
        Assert.Equal(["arena2/MAPS.BSA", "arena2/BLOCKS.BSA"], locations.Sources);
        Assert.All(locations.Locations, location =>
        {
            DaggerfallLocationExterior exterior = Assert.IsType<DaggerfallLocationExterior>(location.Exterior);
            Assert.Equal(checked(exterior.Width * exterior.Height), exterior.Blocks.Count);
            Assert.InRange(exterior.MapPixelX, 0, 999);
            Assert.InRange(exterior.MapPixelY, 0, 499);
        });

        DaggerfallLocationMap hold = Assert.Single(locations.Locations, location => location.Region == 17 && location.Index == 179);
        DaggerfallLocationExterior holdExterior = Assert.IsType<DaggerfallLocationExterior>(hold.Exterior);
        Assert.Equal("Privateer's Hold", hold.Name);
        Assert.Equal(109, holdExterior.MapPixelX);
        Assert.Equal(158, holdExterior.MapPixelY);
        Assert.Equal(2, holdExterior.BlendClearance);
        Assert.Equal(72, holdExterior.TileOriginX);
        Assert.Equal(55, holdExterior.TileOriginY);
        Assert.True(holdExterior.UsesCustomLocationPosition);
        Assert.Equal(new DaggerfallLocationTerrainRect(70, 89, 53, 72), holdExterior.FlattenRect);
        Assert.NotEmpty(holdExterior.Blocks);
    }

    private static string Corpus(string name) => Path.Combine(RepositoryRoot(), "local/arena2", name);

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
                return current.FullName;
        }

        throw new InvalidOperationException("repository root not found");
    }
}
