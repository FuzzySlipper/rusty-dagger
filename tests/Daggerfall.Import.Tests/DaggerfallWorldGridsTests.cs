using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The normalized climate and politic grids: row tiling with the sentinel column kept, donor value
/// interpretation, deterministic order, and explicit failure for malformed sources.
/// </summary>
public sealed class DaggerfallWorldGridsTests
{
    [Fact]
    public void Builds_tiled_rows_with_the_sentinel_column_kept()
    {
        DaggerfallClimateGrid climate = DaggerfallWorldGridsBuilder.BuildClimate(CreateStripedPak(223, 224), "local/arena2/CLIMATE.PAK", Inventory());
        DaggerfallPoliticGrid politic = DaggerfallWorldGridsBuilder.BuildPolitic(CreateStripedPak(64, 145), "local/arena2/POLITIC.PAK", Inventory());

        Assert.Equal(500, climate.Rows.Count);
        Assert.Equal(500, politic.Rows.Count);
        // Rows publish in order, each tiling the full width with the sentinel column first.
        Assert.Equal([.. Enumerable.Range(0, 500)], climate.Rows.Select(row => row.Y));
        Assert.All(climate.Rows, row => Assert.Equal(PakMap.Width, row.Runs.Sum(run => run.Count)));
        Assert.All(climate.Rows, row => Assert.Equal(223, row.Runs[0].Value));
        Assert.All(politic.Rows, row => Assert.Equal(64, row.Runs[0].Value));
        // Values publish in first-appearance order with the donor's interpretation.
        Assert.Equal([223, 224], climate.Values.Select(value => value.Value));
        Assert.Equal("Ocean", climate.Values[0].Name);
        Assert.Equal("Desert", climate.Values[1].Name);
        Assert.All(climate.Values, value => Assert.Equal(DaggerfallClimateDisposition.Named, value.Disposition));
        Assert.Equal([64, 145], politic.Values.Select(value => value.Value));
        Assert.Equal(DaggerfallPoliticDisposition.Ocean, politic.Values[0].Disposition);
        Assert.Equal(-1, politic.Values[0].Region);
        Assert.Equal(DaggerfallPoliticDisposition.Region, politic.Values[1].Disposition);
        Assert.Equal(17, politic.Values[1].Region);
    }

    [Fact]
    public void Records_unresolved_values_rather_than_dropping_them()
    {
        DaggerfallClimateGrid climate = DaggerfallWorldGridsBuilder.BuildClimate(CreateStripedPak(223, 100), "local/arena2/CLIMATE.PAK", Inventory());
        DaggerfallClimateValue unknown = climate.Values.Single(value => value.Value == 100);
        Assert.Equal(DaggerfallClimateDisposition.Unresolved, unknown.Disposition);
        Assert.Equal(string.Empty, unknown.Name);

        DaggerfallPoliticGrid politic = DaggerfallWorldGridsBuilder.BuildPolitic(CreateStripedPak(64, 200), "local/arena2/POLITIC.PAK", Inventory());
        DaggerfallPoliticValue stray = politic.Values.Single(value => value.Value == 200);
        Assert.Equal(DaggerfallPoliticDisposition.Unresolved, stray.Disposition);
        Assert.Equal(-1, stray.Region);
    }

    [Fact]
    public void Refuses_malformed_sources_and_missing_provenance()
    {
        Assert.Throws<Arena2FormatException>(() => DaggerfallWorldGridsBuilder.BuildClimate([1, 2, 3], "local/arena2/CLIMATE.PAK", Inventory()));
        Assert.Throws<InvalidOperationException>(() => DaggerfallWorldGridsBuilder.BuildClimate(CreateStripedPak(223, 224), "local/arena2/CLIMATE.PAK", []));
    }

    [Fact]
    public void Reads_both_supplied_grids_end_to_end()
    {
        string arena2 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../local/arena2"));
        if (!File.Exists(Path.Combine(arena2, "CLIMATE.PAK")) || !File.Exists(Path.Combine(arena2, "POLITIC.PAK"))) return;

        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")));
        DaggerfallClimateGrid climate = DaggerfallWorldGridsBuilder.BuildClimate(File.ReadAllBytes(Path.Combine(arena2, "CLIMATE.PAK")), "local/arena2/CLIMATE.PAK", inventory);
        DaggerfallPoliticGrid politic = DaggerfallWorldGridsBuilder.BuildPolitic(File.ReadAllBytes(Path.Combine(arena2, "POLITIC.PAK")), "local/arena2/POLITIC.PAK", inventory);
        climate.Validate();
        politic.Validate();

        // Ten climates, all named; the sentinel column is ocean everywhere.
        Assert.Equal(10, climate.Values.Count);
        Assert.All(climate.Values, value => Assert.Equal(DaggerfallClimateDisposition.Named, value.Disposition));
        Assert.All(climate.Rows, row => Assert.Equal(223, row.Runs[0].Value));
        // Forty-five regions plus the ocean; nothing unresolved in the supplied grids.
        Assert.Equal(46, politic.Values.Count);
        Assert.Equal(45, politic.Values.Count(value => value.Disposition == DaggerfallPoliticDisposition.Region));
        Assert.Single(politic.Values, value => value.Disposition == DaggerfallPoliticDisposition.Ocean);
        Assert.DoesNotContain(politic.Values, value => value.Disposition == DaggerfallPoliticDisposition.Unresolved);
        Assert.All(politic.Values.Where(value => value.Disposition == DaggerfallPoliticDisposition.Region),
            value => Assert.InRange(value.Region, 0, 61));

        // Privateer's Hold reads woodlands in region 17 through the same pixels the dungeon
        // normalizer reads: the grid agrees with the location's own region.
        Assert.Equal(231, Cell(climate, 109, 158));
        Assert.Equal(145, Cell(politic, 109, 158));
    }

    private static int Cell(DaggerfallClimateGrid grid, int x, int y)
    {
        int column = 0;
        foreach (DaggerfallGridRun run in grid.Rows[y].Runs)
        {
            if (x < column + run.Count) return run.Value;
            column += run.Count;
        }

        throw new InvalidOperationException("test cell outside its published row");
    }

    private static int Cell(DaggerfallPoliticGrid grid, int x, int y)
    {
        int column = 0;
        foreach (DaggerfallGridRun run in grid.Rows[y].Runs)
        {
            if (x < column + run.Count) return run.Value;
            column += run.Count;
        }

        throw new InvalidOperationException("test cell outside its published row");
    }

    private static byte[] CreateStripedPak(byte first, byte rest)
    {
        const int tableBytes = PakMap.Height * sizeof(uint);
        // Column zero carries the first value; the remaining columns carry the second.
        byte[] result = new byte[tableBytes + (PakMap.Height * 6)];
        for (int row = 0; row < PakMap.Height; row++)
        {
            int runOffset = tableBytes + (row * 6);
            BitConverter.GetBytes((uint)runOffset).CopyTo(result, row * sizeof(uint));
            BitConverter.GetBytes((ushort)1).CopyTo(result, runOffset);
            result[runOffset + 2] = first;
            BitConverter.GetBytes((ushort)(PakMap.Width - 1)).CopyTo(result, runOffset + 3);
            result[runOffset + 5] = rest;
        }

        return result;
    }

    private static IReadOnlyList<SourceInventoryRow> Inventory() =>
    [
        new SourceInventoryRow("CNT-002.file.CLIMATE.PAK", "file", "CNT-002", "source-file", "local/arena2/CLIMATE.PAK", "CLIMATE", "imported", string.Empty),
        new SourceInventoryRow("CNT-003.file.POLITIC.PAK", "file", "CNT-003", "source-file", "local/arena2/POLITIC.PAK", "POLITIC", "imported", string.Empty),
    ];

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }
}
