using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>One cell-prefix byte with the values the corpus states for it.</summary>
/// <param name="Index">The byte's index within the 22-byte prefix.</param>
/// <param name="Values">Every distinct value the corpus states, in ascending order.</param>
public sealed record DaggerfallTerrainPrefixByte(int Index, IReadOnlyList<int> Values)
{
    public void Validate()
    {
        if (Index is < 0 or >= WoodsReader.CellPrefixBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(Index), Index, "A published terrain prefix byte names no prefix position.");
        }

        if (Values.Count == 0 || Values.Count > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(Values), Values.Count, $"Terrain prefix byte {Index} states no value domain.");
        }

        foreach (int value in Values)
        {
            if (value is < 0 or > 0xff)
            {
                throw new ArgumentOutOfRangeException(nameof(Values), value, $"Terrain prefix byte {Index} states no source byte.");
            }
        }
    }
}

/// <summary>The normalized wilderness terrain: the heightmap every coordinate reads and the cell samples behind it.</summary>
/// <param name="Source">The source the contract was read from.</param>
/// <param name="Width">The heightmap width.</param>
/// <param name="Height">The heightmap height.</param>
/// <param name="Heightmap">The heightmap rows in order, each tiling the full width.</param>
/// <param name="Samples">
/// The content-root-relative file the 25 elevation samples of every cell are published in, as raw bytes in
/// row-major cell order (<see cref="SampleBytes"/>), so a reader maps them rather than decoding them from text.
/// </param>
/// <param name="CellCount">How many cells the samples carry.</param>
/// <param name="CellBase">The source offset of the first cell record.</param>
/// <param name="CellStride">The source stride between cell records.</param>
/// <param name="Prefix">The value domain of every cell-prefix byte.</param>
/// <param name="DataSection">The donor-unread data section, base-64.</param>
public sealed record DaggerfallTerrain(
    PublishedSource Source,
    int Width,
    int Height,
    IReadOnlyList<DaggerfallGridRow> Heightmap,
    string Samples,
    int CellCount,
    long CellBase,
    int CellStride,
    IReadOnlyList<DaggerfallTerrainPrefixByte> Prefix,
    string DataSection)
{
    /// <summary>Where the samples are published, below the content root.</summary>
    public const string SamplesPath = "worldrpg/terrain/samples.bin";

    /// <summary>The samples themselves, which <see cref="Samples"/> names the file of; not part of the section.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] SampleBytes { get; init; } = [];

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        Source.Validate();
        if (Width != WoodsReader.MapWidth || Height != WoodsReader.MapHeight)
        {
            throw new InvalidOperationException($"Terrain covers {Width}x{Height} for a {WoodsReader.MapWidth}x{WoodsReader.MapHeight} map.");
        }

        if (Heightmap.Count != Height)
        {
            throw new InvalidOperationException($"Terrain carries {Heightmap.Count} heightmap rows for a {Height}-row map.");
        }

        NormalizedImportDocument.ValidateUnique(Heightmap, row => row.Y.ToString(), "terrain heightmap rows");
        foreach (DaggerfallGridRow row in Heightmap)
        {
            row.Validate(Width);
        }

        if (CellCount != Width * Height)
        {
            throw new InvalidOperationException($"Terrain carries {CellCount} cells for a {Width * Height}-cell map.");
        }

        if (string.IsNullOrWhiteSpace(Samples) || string.IsNullOrWhiteSpace(DataSection))
        {
            throw new InvalidOperationException("Terrain carries no samples or no data section.");
        }

        // A section read back carries the samples' file, not their bytes; one built from the source carries both.
        if (SampleBytes.Length != 0 && SampleBytes.Length != checked(CellCount * WoodsReader.CellSamples))
        {
            throw new InvalidOperationException($"Terrain carries {SampleBytes.Length} sample bytes for {CellCount} cells of {WoodsReader.CellSamples}.");
        }

        if (Prefix.Count != WoodsReader.CellPrefixBytes)
        {
            throw new InvalidOperationException($"Terrain describes {Prefix.Count} prefix bytes for a {WoodsReader.CellPrefixBytes}-byte prefix.");
        }

        foreach (DaggerfallTerrainPrefixByte prefix in Prefix)
        {
            prefix.Validate();
        }

        NormalizedImportDocument.ValidateUnique(Prefix, prefix => prefix.Index.ToString(), "terrain prefix bytes");
    }
}

/// <summary>
/// Builds the normalized wilderness terrain from the classic file. The heightmap tiles rows the
/// way the grids do; the cell samples travel as one raw file because run-length encoding
/// cannot compress them. The 22 prefix bytes no reader consumes stay bytes in the source, not in
/// the contract: the contract publishes their value domains with the verification behind them,
/// and republishes the samples the donor actually reads. Seasonal interpretation reads the
/// climate grid beside this contract rather than duplicating it.
/// </summary>
public static class DaggerfallTerrainBuilder
{
    /// <summary>The inventory family the wilderness file is documented under.</summary>
    public const string TerrainFamily = "CNT-004";

    /// <summary>Builds the terrain contract.</summary>
    public static DaggerfallTerrain Build(ReadOnlySpan<byte> bytes, string label, IReadOnlyList<SourceInventoryRow> inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        SourceInventoryRow family = SourceInventoryRow.RequireFamily(inventory, TerrainFamily);
        WoodsFile woods = WoodsReader.Read(bytes, label);

        List<DaggerfallGridRow> rows = [];
        for (int y = 0; y < WoodsReader.MapHeight; y++)
        {
            List<DaggerfallGridRun> runs = [];
            int x = 0;
            while (x < WoodsReader.MapWidth)
            {
                byte value = woods.Heightmap[(y * WoodsReader.MapWidth) + x];
                int count = 1;
                while (x + count < WoodsReader.MapWidth && woods.Heightmap[(y * WoodsReader.MapWidth) + x + count] == value)
                {
                    count++;
                }

                runs.Add(new DaggerfallGridRun(count, value));
                x += count;
            }

            DaggerfallGridRow row = new(y, runs);
            row.Validate(WoodsReader.MapWidth);
            rows.Add(row);
        }

        byte[] samples = new byte[checked(woods.Cells.Count * WoodsReader.CellSamples)];
        for (int cell = 0; cell < woods.Cells.Count; cell++)
        {
            woods.Cells[cell].Samples.CopyTo(samples, cell * WoodsReader.CellSamples);
        }

        List<uint> offsets = [.. woods.Cells.Select(cell => cell.Offset).Order()];
        for (int index = 1; index < offsets.Count; index++)
        {
            if (offsets[index] - offsets[index - 1] != WoodsReader.CellBytes)
            {
                throw new InvalidOperationException($"Wilderness cell records are not contiguous, so no base and stride describe them.");
            }
        }

        List<DaggerfallTerrainPrefixByte> prefix = [];
        for (int position = 0; position < WoodsReader.CellPrefixBytes; position++)
        {
            int at = position;
            prefix.Add(new DaggerfallTerrainPrefixByte(at, [.. woods.Cells.Select(cell => (int)cell.Prefix[at]).Distinct().Order()]));
        }

        DaggerfallTerrain terrain = new(
            PublishedSource.Of(GetFilePath(inventory, label), bytes),
            WoodsReader.MapWidth,
            WoodsReader.MapHeight,
            rows,
            DaggerfallTerrain.SamplesPath,
            woods.Cells.Count,
            offsets[0],
            WoodsReader.CellBytes,
            prefix,
            Convert.ToBase64String(woods.DataSection))
        {
            SampleBytes = samples,
        };
        terrain.Validate();
        return terrain;
    }

    private static string GetFilePath(IReadOnlyList<SourceInventoryRow> inventory, string label)
    {
        return inventory.FirstOrDefault(row => row.FamilyId == TerrainFamily && StringComparer.Ordinal.Equals(row.PathOrPattern, label))?.PathOrPattern
            ?? throw new InvalidOperationException($"The documented inventory does not carry '{label}', so the terrain cites no provenance.");
    }
}
