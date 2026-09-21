namespace Daggerfall.Import.Arena2;

/// <summary>One decoded WOODS.WLD file header.</summary>
/// <param name="OffsetTableBytes">The byte length the file declares for its cell offset table.</param>
/// <param name="Width">The declared map width.</param>
/// <param name="Height">The declared map height.</param>
/// <param name="DataSectionOffset">The offset of the donor-unread data section.</param>
/// <param name="Unknown1">The first retained unknown field.</param>
/// <param name="Unknown2">The second retained unknown field.</param>
/// <param name="HeightmapOffset">The offset of the heightmap.</param>
public sealed record WoodsHeader(
    uint OffsetTableBytes,
    uint Width,
    uint Height,
    uint DataSectionOffset,
    uint Unknown1,
    uint Unknown2,
    uint HeightmapOffset);

/// <summary>One decoded wilderness cell: where its record sits and the bytes it states.</summary>
/// <param name="X">The cell's column.</param>
/// <param name="Y">The cell's row.</param>
/// <param name="Offset">The record's file offset.</param>
/// <param name="Prefix">The 22 record bytes the donor skips past.</param>
/// <param name="Samples">The 25 elevation samples the donor reads: row-major 5 by 5.</param>
public sealed record WoodsCell(int X, int Y, uint Offset, byte[] Prefix, byte[] Samples);

/// <summary>One decoded WOODS.WLD file: header, heightmap and every cell record.</summary>
/// <param name="Source">Logical source identity supplied at parse time.</param>
/// <param name="Header">The parsed header.</param>
/// <param name="DataSection">The donor-unread data section bytes.</param>
/// <param name="Heightmap">The row-major heightmap: width times height bytes.</param>
/// <param name="Cells">The cell records in row-major order.</param>
public sealed record WoodsFile(string Source, WoodsHeader Header, byte[] DataSection, byte[] Heightmap, IReadOnlyList<WoodsCell> Cells);

/// <summary>Reader for classic WOODS.WLD wilderness files.</summary>
public static class WoodsReader
{
    public const int MapWidth = 1000;
    public const int MapHeight = 500;
    public const int HeaderBytes = 144;
    public const int CellBytes = 47;
    public const int CellPrefixBytes = 22;
    public const int CellSamples = 25;

    /// <summary>
    /// Reads one wilderness file: the header it states, the heightmap, and every cell record the
    /// offset table addresses. The table must tile the full grid exactly once with records that
    /// fit the file; a short table, a shared offset, or a record past the end is refused rather
    /// than sampled around, because the exterior owner reads cells by coordinates and a gap it
    /// could not see would be terrain it invents.
    /// </summary>
    public static WoodsFile Read(ReadOnlySpan<byte> bytes, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (bytes.Length < HeaderBytes)
        {
            throw new Arena2FormatException(source, bytes.Length, $"Wilderness requires a {HeaderBytes}-byte header");
        }

        WoodsHeader header = new(
            ReadUInt32(bytes, 0), ReadUInt32(bytes, 4), ReadUInt32(bytes, 8),
            ReadUInt32(bytes, 16), ReadUInt32(bytes, 20), ReadUInt32(bytes, 24), ReadUInt32(bytes, 28));
        if (header.Width != MapWidth || header.Height != MapHeight)
        {
            throw new Arena2FormatException(source, 4, $"Wilderness declares a {header.Width}x{header.Height} grid for a {MapWidth}x{MapHeight} map.");
        }

        if (header.OffsetTableBytes != MapWidth * MapHeight * sizeof(uint))
        {
            throw new Arena2FormatException(source, 0, $"Wilderness declares {header.OffsetTableBytes} offset-table bytes for a {MapWidth * MapHeight * sizeof(uint)}-byte table.");
        }

        long tableEnd = HeaderBytes + header.OffsetTableBytes;
        long heightmapEnd = header.HeightmapOffset + (MapWidth * MapHeight);
        if (tableEnd > bytes.Length || heightmapEnd > bytes.Length || header.DataSectionOffset > bytes.Length)
        {
            throw new Arena2FormatException(source, bytes.Length, $"Wilderness addresses past its {bytes.Length} bytes.");
        }

        HashSet<uint> offsets = [];
        List<WoodsCell> cells = new(MapWidth * MapHeight);
        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                uint offset = ReadUInt32(bytes, HeaderBytes + (((y * MapWidth) + x) * sizeof(uint)));
                if (!offsets.Add(offset))
                {
                    throw new Arena2FormatException(source, HeaderBytes + (((y * MapWidth) + x) * sizeof(uint)), $"Wilderness cell ({x}, {y}) shares its record offset.");
                }

                if (offset > bytes.Length - CellBytes)
                {
                    throw new Arena2FormatException(source, checked((int)offset), $"Wilderness cell ({x}, {y}) ends past {bytes.Length} bytes.");
                }

                cells.Add(new WoodsCell(
                    x, y, offset,
                    bytes.Slice((int)offset, CellPrefixBytes).ToArray(),
                    bytes.Slice((int)offset + CellPrefixBytes, CellSamples).ToArray()));
            }
        }

        int dataLength = (int)Math.Min((long)header.HeightmapOffset - header.DataSectionOffset, (long)bytes.Length - header.DataSectionOffset);
        return new WoodsFile(
            source,
            header,
            bytes.Slice((int)header.DataSectionOffset, dataLength).ToArray(),
            bytes.Slice((int)header.HeightmapOffset, MapWidth * MapHeight).ToArray(),
            cells);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
}
