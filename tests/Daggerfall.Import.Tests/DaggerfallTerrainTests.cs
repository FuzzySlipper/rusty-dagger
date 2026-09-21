using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The classic wilderness: header grammar, offset-table tiling, heightmap and cell records, and
/// the normalized terrain contract behind them.
/// </summary>
public sealed class DaggerfallTerrainTests
{
    [Fact]
    public void Reads_header_offsets_heightmap_and_cells_from_fixtures()
    {
        byte[] bytes = Fixture(height: 0, prefix: 0, samples: 7);
        WoodsFile woods = WoodsReader.Read(bytes, "wilderness");
        Assert.Equal(1000u, woods.Header.Width);
        Assert.Equal(500u, woods.Header.Height);
        Assert.Equal(500000, woods.Cells.Count);
        Assert.All(woods.Cells, cell => Assert.Equal(Enumerable.Repeat((byte)7, 25), cell.Samples));
    }

    [Fact]
    public void Refuses_short_tables_shared_offsets_and_short_grids()
    {
        byte[] truncated = Fixture(height: 0, prefix: 0, samples: 7)[..100];
        Assert.Throws<Arena2FormatException>(() => WoodsReader.Read(truncated, "wilderness"));

        byte[] bytes = Fixture(height: 0, prefix: 0, samples: 7);
        // Two cells share the first record offset.
        BitConverter.GetBytes(BitConverter.ToUInt32(bytes, 144)).CopyTo(bytes, 148);
        Assert.Throws<Arena2FormatException>(() => WoodsReader.Read(bytes, "wilderness"));

        byte[] narrow = Fixture(height: 0, prefix: 0, samples: 7);
        BitConverter.GetBytes(999u).CopyTo(narrow, 4);
        Assert.Throws<Arena2FormatException>(() => WoodsReader.Read(bytes: narrow, source: "wilderness"));
    }

    [Fact]
    public void Builds_the_terrain_contract_end_to_end()
    {
        string arena2 = Arena2Directory();
        if (!File.Exists(Path.Combine(arena2, "WOODS.WLD"))) return;

        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")));
        byte[] bytes = File.ReadAllBytes(Path.Combine(arena2, "WOODS.WLD"));
        DaggerfallTerrain terrain = DaggerfallTerrainBuilder.Build(bytes, "local/arena2/WOODS.WLD", inventory);
        terrain.Validate();

        Assert.Equal(500, terrain.Heightmap.Count);
        Assert.All(terrain.Heightmap, row => Assert.Equal(1000, row.Runs.Sum(run => run.Count)));
        Assert.Equal(500000, terrain.CellCount);
        Assert.Equal(2501168, terrain.CellBase);
        Assert.Equal(47, terrain.CellStride);
        Assert.Equal(22, terrain.Prefix.Count);
        // Samples round-trip byte-exact through the published span.
        byte[] samples = Convert.FromBase64String(terrain.Samples);
        Assert.Equal(12500000, samples.Length);
        WoodsFile woods = WoodsReader.Read(bytes, "local/arena2/WOODS.WLD");
        Assert.Equal(woods.Cells[158 * 1000 + 109].Samples, samples.AsSpan((158 * 1000 + 109) * 25, 25).ToArray());
        // Heightmap rows tile the source bytes.
        byte[] heightmap = new byte[500000];
        foreach (DaggerfallGridRow row in terrain.Heightmap)
        {
            int column = 0;
            foreach (DaggerfallGridRun run in row.Runs)
            {
                heightmap.AsSpan(row.Y * 1000 + column, run.Count).Fill((byte)run.Value);
                column += run.Count;
            }
        }

        Assert.Equal(woods.Heightmap, heightmap);
    }

    private static byte[] Fixture(byte height, byte prefix, byte samples)
    {
        List<byte> bytes = [];
        bytes.AddRange(BitConverter.GetBytes(2000000u));
        bytes.AddRange(BitConverter.GetBytes(1000u));
        bytes.AddRange(BitConverter.GetBytes(500u));
        bytes.AddRange(BitConverter.GetBytes(0u));
        bytes.AddRange(BitConverter.GetBytes(2000144u));
        bytes.AddRange(BitConverter.GetBytes(1u));
        bytes.AddRange(BitConverter.GetBytes(22u));
        bytes.AddRange(BitConverter.GetBytes(2001168u));
        bytes.AddRange(new byte[112]);
        // Offset table: contiguous records from the first cell base.
        for (int cell = 0; cell < 500000; cell++)
        {
            bytes.AddRange(BitConverter.GetBytes(2501168u + (uint)(cell * 47)));
        }

        // Data section and heightmap.
        bytes.AddRange(new byte[1024]);
        bytes.AddRange(Enumerable.Repeat(height, 500000).ToArray());
        // Cells: prefix plus samples.
        for (int cell = 0; cell < 500000; cell++)
        {
            bytes.AddRange(Enumerable.Repeat(prefix, 22).ToArray());
            bytes.AddRange(Enumerable.Repeat(samples, 25).ToArray());
        }

        return [.. bytes];
    }

    private static string Arena2Directory() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../local/arena2"));

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
