using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The map art inventory: every documented FMAP, AMAP, TMAP, TRAV, TOWN and palette file with its
/// region, call-site and palette binding. Three region-map stubs are unsupported by donor name;
/// regions with no supplied screen stay not supplied.
/// </summary>
public sealed class MapArtInventoryTests
{
    [Fact]
    public void Enumerates_the_documented_set_with_donor_bindings()
    {
        string arena2 = Arena2Directory();
        if (!Directory.Exists(arena2)) return;

        MapArtInventory inventory = Enumerate(arena2);

        // Seventy-two region-map slots plus eighteen companions.
        Assert.Equal(90, inventory.Records.Count);
        Assert.Equal(inventory.Records.Select(record => record.FileName).OrderBy(name => name, StringComparer.Ordinal), inventory.Records.Select(record => record.FileName));
        Assert.Equal(69, inventory.Records.Count(record => record.Disposition == MapArtDisposition.Decoded));
        Assert.DoesNotContain(inventory.Records, record => record.Disposition is MapArtDisposition.Malformed or MapArtDisposition.Unreadable);

        // The donor's three unsupported stubs agree with the donor rather than this product.
        foreach (string stub in MapArtInventory.UnsupportedFiles)
        {
            Assert.True(inventory.TryGet(stub, out MapArtRecord? record));
            Assert.Equal(MapArtDisposition.Unsupported, record!.Disposition);
        }

        // Regions with no supplied screen are retained, not dropped.
        MapArtRecord missing = inventory.Records.Single(record => record.FileName == "FMAP0I02.IMG");
        Assert.Equal(MapArtDisposition.NotSupplied, missing.Disposition);
        Assert.Equal([2], missing.Regions);
        Assert.Equal(18, inventory.Records.Count(record => record.Kind == MapArtKind.RegionMap && record.Disposition == MapArtDisposition.NotSupplied));

        // Region 17 draws the single screen its number names with the region-map palette.
        Assert.True(inventory.TryGet("FMAP0I17.IMG", out MapArtRecord? region));
        Assert.Equal(MapArtDisposition.Decoded, region!.Disposition);
        Assert.Equal([17], region.Regions);
        Assert.Equal("FMAP_PAL.COL", region.Palette);
        Assert.Equal((320, 160), (region.Width, region.Height));

        // The multi-screen regions tile their variants.
        Assert.True(inventory.TryGet("FMAPAI00.IMG", out MapArtRecord? betony));
        Assert.Equal([0], betony!.Regions);
        Assert.Equal(MapArtDisposition.Decoded, betony.Disposition);

        // The travel chrome, automap and caption bind their call sites.
        Assert.True(inventory.TryGet("TRAV0I00.IMG", out MapArtRecord? overworld));
        Assert.Contains("overworld", overworld!.Binding, StringComparison.Ordinal);
        Assert.True(inventory.TryGet("AMAP00I0.IMG", out MapArtRecord? automap));
        Assert.Contains("Automap", automap!.Binding, StringComparison.Ordinal);
        Assert.True(inventory.TryGet("TMAP00I0.IMG", out MapArtRecord? race));
        Assert.Equal("MAP.PAL", race!.Palette);
        // Decoded shapes the corpus states: caption strip, grid overlay, popup and arrows.
        Assert.Equal((320, 10), Shape(inventory, "TOWN00I0.IMG"));
        Assert.Equal((27, 19), Shape(inventory, "AMAP01I0.IMG"));
        Assert.Equal((223, 96), Shape(inventory, "TRAV0I04.IMG"));
        Assert.Equal((22, 20), Shape(inventory, "TRAVAI05.IMG"));
        // Two full-screen canvases decode with no donor reader: kept, not dropped.
        Assert.True(inventory.TryGet("TRAV00I0.IMG", out MapArtRecord? orphan));
        Assert.Equal(MapArtDisposition.Decoded, orphan!.Disposition);
        Assert.Contains("No donor call site", orphan.Binding, StringComparison.Ordinal);

        // The palettes decode.
        Assert.True(inventory.TryGet("FMAP_PAL.COL", out MapArtRecord? fmapPal));
        Assert.Equal(MapArtDisposition.Decoded, fmapPal!.Disposition);
        Assert.True(inventory.TryGet("MAP.PAL", out MapArtRecord? mapPal));
        Assert.Equal(MapArtDisposition.Decoded, mapPal!.Disposition);
    }

    [Fact]
    public void Refuses_duplicates_and_reports_unreadable_shapes()
    {
        Assert.Throws<InvalidOperationException>(() => MapArtInventory.Enumerate(
            [("TMAP00I0.IMG", "local/arena2/TMAP00I0.IMG", new byte[64000]), ("TMAP00I0.IMG", "local/arena2/TMAP00I0.IMG", new byte[64000])], "local/arena2"));

        MapArtInventory inventory = MapArtInventory.Enumerate(
            [("TRAVAI05.IMG", "local/arena2/TRAVAI05.IMG", new byte[452])], "local/arena2");
        Assert.True(inventory.TryGet("TRAVAI05.IMG", out MapArtRecord? arrow));
        Assert.NotEqual(MapArtDisposition.Decoded, arrow!.Disposition);
    }

    private static MapArtInventory Enumerate(string arena2)
    {
        List<(string Name, string Path, byte[] Bytes)> files = [];
        foreach (string path in Directory.EnumerateFiles(arena2, "*.IMG").Concat(Directory.EnumerateFiles(arena2, "*.COL")).Concat(Directory.EnumerateFiles(arena2, "*.PAL")).Order(StringComparer.Ordinal))
        {
            files.Add((Path.GetFileName(path), $"local/arena2/{Path.GetFileName(path)}", File.ReadAllBytes(path)));
        }

        return MapArtInventory.Enumerate(files, "local/arena2");
    }

    private static (int Width, int Height) Shape(MapArtInventory inventory, string fileName)
    {
        Assert.True(inventory.TryGet(fileName, out MapArtRecord? record));
        Assert.Equal(MapArtDisposition.Decoded, record!.Disposition);
        return (record.Width, record.Height);
    }

    private static string Arena2Directory() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../local/arena2"));
}
