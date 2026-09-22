namespace Daggerfall.Import.Arena2;

/// <summary>
/// A BSS sprite container: where it sits, its frame size, and how many frames it carries.
/// </summary>
/// <param name="XOffset">The sprite's authored x offset.</param>
/// <param name="YOffset">The sprite's authored y offset.</param>
/// <param name="Width">The width of every frame.</param>
/// <param name="Height">The height of every frame.</param>
/// <param name="FrameCount">How many frames the container carries.</param>
public sealed record BssContainer(int XOffset, int YOffset, int Width, int Height, int FrameCount);

/// <summary>One complete, uncompressed BSS frame, retaining the ordinal that names it in its source container.</summary>
public sealed record BssFrameImage(int Index, int XOffset, int YOffset, int Width, int Height, byte[] Pixels);

/// <summary>
/// Reads the BSS sprite container three Daggerfall story sprites are stored in.
/// </summary>
/// <remarks>
/// The layout is the donor's <c>Assets/Scripts/API/BssFile.cs</c>: five little-endian signed 16-bit
/// fields, in order <c>XPos</c>, <c>YPos</c>, <c>Width</c>, <c>Height</c>, and <c>FrameCount</c>, followed
/// by <c>FrameCount</c> uncompressed 8-bit palette-index frames. The apparent leading values
/// 48/40/32, 34/28/32, and 30/25/32 in the supplied files are their width/height/frame-count fields;
/// their preceding x/y positions are 272/157, 279/163, and 281/165. There is no palette in the
/// container, so the donor pairs it with <c>ART_PAL.COL</c>.
/// </remarks>
public static class BssDecoder
{
    /// <summary>The header's length in bytes.</summary>
    public const int HeaderBytes = 10;

    /// <summary>
    /// Reads a BSS container's header and reports where its frames are.
    /// </summary>
    /// <param name="bytes">The container's bytes.</param>
    /// <param name="source">Logical source identity, for diagnostics.</param>
    /// <param name="container">The container, when the bytes are one.</param>
    /// <param name="reason">Why the bytes are not a container this reader accepts.</param>
    public static bool TryRead(ReadOnlySpan<byte> bytes, string source, out BssContainer? container, out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        container = null;
        if (bytes.Length < HeaderBytes)
        {
            reason = $"a BSS container needs at least its {HeaderBytes}-byte header, and '{source}' has {bytes.Length} bytes";
            return false;
        }

        // BSS has no magic of its own.  An FLC magic at its declared offset is positive evidence
        // that the caller named a different container as BSS, so do not reinterpret its header words.
        if (bytes[4] == 0x12 && bytes[5] == 0xAF)
        {
            reason = $"'{source}' carries FLC magic 0xAF12 at offset 4, so it is not a BSS container";
            return false;
        }

        CheckedLittleEndianReader reader = new(bytes[..HeaderBytes].ToArray(), source);
        int x = reader.ReadInt16();
        int y = reader.ReadInt16();
        int width = reader.ReadInt16();
        int height = reader.ReadInt16();
        int frames = reader.ReadInt16();
        if (width <= 0 || height <= 0 || frames <= 0)
        {
            reason = $"'{source}' declares XPos={x}, YPos={y}, {frames} frame(s) of {width}x{height}, which is not a BSS container this reader can decode";
            return false;
        }

        long expected = HeaderBytes + ((long)width * height * frames);
        if (expected != bytes.Length)
        {
            reason = $"'{source}' declares XPos={x}, YPos={y}, {frames} frame(s) of {width}x{height}, which need {expected} bytes, but it has {bytes.Length}";
            return false;
        }

        container = new BssContainer(x, y, width, height, frames);
        reason = string.Empty;
        return true;
    }

    /// <summary>Decodes the uncompressed palette-index snapshots every BSS container carries.</summary>
    public static IReadOnlyList<BssFrameImage> DecodeFrames(ReadOnlySpan<byte> bytes, string source)
    {
        if (!TryRead(bytes, source, out BssContainer? container, out string reason))
            throw new Arena2FormatException(source, 0, reason);
        int pixelsPerFrame = checked(container!.Width * container.Height);
        List<BssFrameImage> frames = new(container.FrameCount);
        for (int index = 0; index < container.FrameCount; index++)
        {
            int offset = checked(HeaderBytes + (index * pixelsPerFrame));
            frames.Add(new BssFrameImage(index, container.XOffset, container.YOffset, container.Width, container.Height,
                bytes.Slice(offset, pixelsPerFrame).ToArray()));
        }

        return frames;
    }
}
