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

/// <summary>
/// Reads the BSS sprite container three Daggerfall story sprites are stored in.
/// </summary>
/// <remarks>
/// The layout is the donor's <c>Assets/Scripts/API/BssFile.cs</c>: five little-endian 16-bit fields -
/// an authored offset, the frame size, and a frame count - followed by uncompressed palette indices,
/// one byte per pixel per frame. There is no compression and no palette of its own, so a caller
/// pairs the indices with the classic art palette.
/// <para>
/// The header is checked against the file rather than trusted: a container whose declared frame size
/// and count do not account for exactly its bytes is refused with the arithmetic named. That check is
/// what makes this a reader rather than a guess - the three supplied files satisfy it to the byte.
/// </para>
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

        CheckedLittleEndianReader reader = new(bytes[..HeaderBytes].ToArray(), source);
        int x = reader.ReadInt16();
        int y = reader.ReadInt16();
        int width = reader.ReadInt16();
        int height = reader.ReadInt16();
        int frames = reader.ReadInt16();
        if (width <= 0 || height <= 0 || frames <= 0)
        {
            reason = $"'{source}' declares {frames} frames of {width}x{height}, which is not a container this reader can decode";
            return false;
        }

        long expected = HeaderBytes + ((long)width * height * frames);
        if (expected != bytes.Length)
        {
            reason = $"'{source}' declares {frames} frames of {width}x{height}, which need {expected} bytes, but it has {bytes.Length}";
            return false;
        }

        container = new BssContainer(x, y, width, height, frames);
        reason = string.Empty;
        return true;
    }
}
