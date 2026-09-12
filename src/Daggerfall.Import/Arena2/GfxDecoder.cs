namespace Daggerfall.Import.Arena2;

/// <summary>One row of one GFX frame: where its bytes begin and whether they are encoded.</summary>
/// <param name="Offset">Row position within the source file.</param>
/// <param name="IsRleEncoded">Whether the row is run-length encoded rather than raw pixels.</param>
public sealed record GfxRow(int Offset, bool IsRleEncoded);

/// <summary>One frame of a classic GFX container.</summary>
public sealed record GfxFrame(int Index, int Width, int Height, IReadOnlyList<GfxRow> Rows);

/// <summary>
/// The classic GFX container: one record whose header declares the frame count and the shape
/// every frame shares, followed by one row entry per row of every frame.
/// </summary>
/// <remarks>
/// The header is the donor's <c>GfxFile.Header</c> layout, and the row table is the donor's
/// <c>GfxFile.ReadImageData</c> layout. This establishes how many frames the container carries
/// and where each row's bytes are; it does not decode row pixels, which the publication that
/// emits frames owns.
/// </remarks>
public sealed class GfxArchive
{
    /// <summary>
    /// GFX header byte count: frame count, width, height, pixel data length, one unknown and
    /// four bytes the donor records as zero.
    /// </summary>
    public const int HeaderBytes = 14;

    /// <summary>GFX row entry byte count: a file offset and a row encoding.</summary>
    public const int RowEntryBytes = 4;

    /// <summary>The row encoding bit that marks a run-length encoded row.</summary>
    public const ushort RleEncodedRow = 0x8000;

    private GfxArchive(string source, int width, int height, IReadOnlyList<GfxFrame> frames)
    {
        Source = source;
        Width = width;
        Height = height;
        Frames = frames;
    }

    /// <summary>Logical source identity supplied to <see cref="Parse"/>.</summary>
    public string Source { get; }

    /// <summary>Width every frame shares.</summary>
    public int Width { get; }

    /// <summary>Height every frame shares.</summary>
    public int Height { get; }

    /// <summary>Every frame, in file order.</summary>
    public IReadOnlyList<GfxFrame> Frames { get; }

    /// <summary>The number of frames the header declares and the row table addresses.</summary>
    public int FrameCount => Frames.Count;

    /// <summary>Reads the container header and the row table that addresses every frame row.</summary>
    /// <param name="bytes">The whole supplied GFX file.</param>
    /// <param name="source">Logical source identity for error messages.</param>
    public static GfxArchive Parse(ReadOnlySpan<byte> bytes, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        CheckedLittleEndianReader reader = new(bytes, source);
        short frameCount = reader.ReadInt16();
        short width = reader.ReadInt16();
        short height = reader.ReadInt16();
        short pixelDataLength = reader.ReadInt16();
        reader.ReadInt16();
        reader.ReadBytes(4);
        if (frameCount <= 0)
        {
            throw reader.Error($"GFX declares {frameCount} frames, so it addresses none");
        }

        if (width <= 0 || height <= 0)
        {
            throw reader.Error($"invalid GFX dimensions {width}x{height}");
        }

        int pixels;
        try
        {
            pixels = checked(width * height);
        }
        catch (OverflowException)
        {
            throw reader.Error($"GFX dimensions {width}x{height} overflow a 32-bit pixel count");
        }

        if (pixels != pixelDataLength)
        {
            throw reader.Error($"GFX pixel data length {pixelDataLength} does not match {width}x{height}");
        }

        long rowCount = (long)height * frameCount;
        long tableEnd = HeaderBytes + (rowCount * RowEntryBytes);
        if (tableEnd > bytes.Length)
        {
            throw reader.Error($"GFX declares {frameCount} frames of {height} rows, whose {rowCount}-entry row table needs {tableEnd} bytes within a {bytes.Length}-byte file");
        }

        List<GfxFrame> frames = [];
        for (int frame = 0; frame < frameCount; frame++)
        {
            List<GfxRow> rows = [];
            for (int row = 0; row < height; row++)
            {
                reader.Seek(HeaderBytes + (((frame * height) + row) * RowEntryBytes));
                int offset = reader.ReadUInt16();
                bool rle = reader.ReadUInt16() == RleEncodedRow;
                if (offset < tableEnd || offset >= bytes.Length)
                {
                    throw reader.Error($"GFX frame {frame} row {row} addresses byte {offset}, outside the {tableEnd}..{bytes.Length} range the file supplies");
                }

                if (!rle && offset + width > bytes.Length)
                {
                    throw reader.Error($"GFX frame {frame} row {row} reads {width} raw bytes from {offset}, past the {bytes.Length}-byte file");
                }

                rows.Add(new GfxRow(offset, rle));
            }

            frames.Add(new GfxFrame(frame, width, height, rows));
        }

        return new GfxArchive(source, width, height, frames);
    }
}
