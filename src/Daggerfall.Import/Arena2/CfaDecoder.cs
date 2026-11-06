namespace Daggerfall.Import.Arena2;

/// <summary>A decoded CFA animation: frames of one size, indexed, one after another.</summary>
public sealed record CfaFrames(int Width, int Height, int FrameCount, ReadOnlyMemory<byte> Pixels)
{
    /// <summary>The indexed pixels of one frame.</summary>
    public ReadOnlySpan<byte> Frame(int frame) => Pixels.Span.Slice(frame * Width * Height, Width * Height);
}

/// <summary>Decoder for classic CFA files: a header, then run-length encoded frames.</summary>
public static class CfaDecoder
{
    private const int HeaderBytes = 14;
    private const int BitsPerPixel = 8;

    /// <summary>
    /// Decodes every frame. A code above 127 repeats the next byte, one fewer times than its distance past 126; any other
    /// copies its value plus one bytes. A run past the file's end is refused, since the frames after it would be noise.
    /// </summary>
    public static CfaFrames Decode(ReadOnlySpan<byte> bytes, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (bytes.Length < HeaderBytes) throw new Arena2FormatException(source, bytes.Length, $"A CFA file requires a {HeaderBytes}-byte header.");
        int width = BitConverter.ToInt16(bytes[..2]), height = BitConverter.ToInt16(bytes[2..4]);
        int bits = bytes[10], frames = bytes[11], data = BitConverter.ToInt16(bytes[12..14]);
        if (width <= 0 || height <= 0 || frames <= 0 || bits != BitsPerPixel || data < HeaderBytes || data > bytes.Length)
            throw new Arena2FormatException(source, 0, $"CFA header states {width}x{height}, {frames} frames at {bits} bits from offset {data}.");

        byte[] pixels = new byte[checked(width * height * frames)];
        int read = data, written = 0;
        while (written < pixels.Length)
        {
            if (read >= bytes.Length) throw new Arena2FormatException(source, read, $"CFA data ends {pixels.Length - written} pixels short.");
            byte code = bytes[read++];
            int run = code > 127 ? code - 127 : code + 1;
            if (written + run > pixels.Length || read + (code > 127 ? 1 : run) > bytes.Length)
                throw new Arena2FormatException(source, read, "A CFA run passes the end of its frames or its file.");
            if (code > 127) pixels.AsSpan(written, run).Fill(bytes[read++]);
            else
            {
                bytes.Slice(read, run).CopyTo(pixels.AsSpan(written));
                read += run;
            }

            written += run;
        }

        return new(width, height, frames, pixels);
    }
}
