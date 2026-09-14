using System.IO.Compression;

namespace Daggerfall.Import.Normalization;

/// <summary>
/// Reads the small, fixed PNG subset the authored-UI inputs use, so a published artifact can be
/// derived from the operator's file instead of being a copy of it.
/// </summary>
/// <remarks>
/// The subset is exactly what an image generator emits here: non-interlaced eight-bit truecolour with
/// or without alpha, no palette, no sixteen-bit samples, no ancillary chunks that change meaning. A
/// file outside it is refused by name rather than half-read, because a silently wrong decode would
/// publish decoration that is not what the operator supplied.
/// </remarks>
public static class DeterministicPngReader
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>Decodes one supported PNG into row-major RGBA8 pixels.</summary>
    public static DeterministicPngImage ReadRgba8(byte[] bytes, string label)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < Signature.Length + 25 || !bytes.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new InvalidOperationException($"'{label}' is not a PNG: the signature does not match.");
        }

        int offset = Signature.Length;
        int width = 0;
        int height = 0;
        int channels = 0;
        bool header = false;
        using MemoryStream scanlines = new();
        while (offset + 8 <= bytes.Length)
        {
            int length = checked((int)ReadUInt32BigEndian(bytes, offset));
            if (length < 0 || offset + 12 + length > bytes.Length)
            {
                throw new InvalidOperationException($"'{label}' carries a PNG chunk whose declared length runs past the file.");
            }

            string kind = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);
            ReadOnlySpan<byte> data = bytes.AsSpan(offset + 8, length);
            switch (kind)
            {
                case "IHDR":
                    if (header || length != 13) throw new InvalidOperationException($"'{label}' does not start with one PNG image header.");
                    width = checked((int)ReadUInt32BigEndian(bytes, offset + 8));
                    height = checked((int)ReadUInt32BigEndian(bytes, offset + 12));
                    byte bitDepth = data[8];
                    byte colorType = data[9];
                    if (bitDepth != 8 || colorType is not (2 or 6) || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    {
                        throw new InvalidOperationException($"'{label}' is not eight-bit non-interlaced truecolour PNG (bit depth {bitDepth}, colour type {colorType}).");
                    }

                    channels = colorType == 6 ? 4 : 3;
                    header = true;
                    break;
                case "IDAT":
                    scanlines.Write(data);
                    break;
                case "IEND":
                    offset = bytes.Length;
                    continue;
                default:
                    break;
            }

            offset += 12 + length;
        }

        if (!header) throw new InvalidOperationException($"'{label}' carries no PNG image header.");
        if (width <= 0 || height <= 0) throw new InvalidOperationException($"'{label}' declares an empty PNG image.");
        long expected = checked(((long)width * channels + 1) * height);
        byte[] raw = Inflate(scanlines.ToArray(), label, expected);
        return new DeterministicPngImage(width, height, Unfilter(raw, width, height, channels, label));
    }

    private static byte[] Inflate(byte[] compressed, string label, long expected)
    {
        if (compressed.Length == 0) throw new InvalidOperationException($"'{label}' carries no PNG image data.");
        using MemoryStream input = new(compressed);
        using ZLibStream zlib = new(input, CompressionMode.Decompress);
        using MemoryStream output = new(checked((int)expected));
        try
        {
            zlib.CopyTo(output);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException($"'{label}' carries PNG image data that does not inflate: {exception.Message}");
        }

        if (output.Length != expected)
        {
            throw new InvalidOperationException($"'{label}' carries {output.Length} inflated bytes where its declared {expected} shape needs exactly that many.");
        }

        return output.ToArray();
    }

    private static byte[] Unfilter(byte[] raw, int width, int height, int channels, string label)
    {
        int stride = checked(width * channels);
        byte[] rgba = new byte[checked(width * height * 4)];
        byte[] previous = new byte[stride];
        byte[] current = new byte[stride];
        int source = 0;
        for (int row = 0; row < height; row++)
        {
            byte filter = raw[source++];
            raw.AsSpan(source, stride).CopyTo(current);
            source += stride;
            for (int index = 0; index < stride; index++)
            {
                int left = index >= channels ? current[index - channels] : 0;
                int up = previous[index];
                int upLeft = index >= channels ? previous[index - channels] : 0;
                current[index] = filter switch
                {
                    0 => current[index],
                    1 => unchecked((byte)(current[index] + left)),
                    2 => unchecked((byte)(current[index] + up)),
                    3 => unchecked((byte)(current[index] + ((left + up) / 2))),
                    4 => unchecked((byte)(current[index] + Paeth(left, up, upLeft))),
                    _ => throw new InvalidOperationException($"'{label}' row {row} uses PNG filter {filter}, which is not one of the five defined filters."),
                };
            }

            int target = checked(row * width * 4);
            for (int column = 0; column < width; column++)
            {
                int from = column * channels;
                rgba[target++] = current[from];
                rgba[target++] = current[from + 1];
                rgba[target++] = current[from + 2];
                rgba[target++] = channels == 4 ? current[from + 3] : (byte)255;
            }

            (previous, current) = (current, previous);
        }

        return rgba;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int estimate = left + up - upLeft;
        int toLeft = Math.Abs(estimate - left);
        int toUp = Math.Abs(estimate - up);
        int toUpLeft = Math.Abs(estimate - upLeft);
        return toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
    }

    private static uint ReadUInt32BigEndian(byte[] bytes, int offset) =>
        (uint)((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]);
}

/// <summary>One decoded PNG image, row-major RGBA8.</summary>
public sealed record DeterministicPngImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// Deterministic box-average resampling. A published artifact derived from an oversized authored
/// source keeps its aspect ratio, its tileable edges and its exact colour, and two runs over the same
/// bytes produce the same pixels.
/// </summary>
public static class DeterministicImageResample
{
    /// <summary>Scales an image down so neither dimension exceeds the bound; never enlarges.</summary>
    public static DeterministicPngImage FitWithin(DeterministicPngImage image, int maximumDimension, string label)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (maximumDimension <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        if (image.Width <= maximumDimension && image.Height <= maximumDimension) return image;
        double scale = (double)maximumDimension / Math.Max(image.Width, image.Height);
        int width = Math.Max(1, checked((int)Math.Round(image.Width * scale, MidpointRounding.ToZero)));
        int height = Math.Max(1, checked((int)Math.Round(image.Height * scale, MidpointRounding.ToZero)));
        if (width <= 0 || height <= 0) throw new InvalidOperationException($"'{label}' cannot be resampled to fit {maximumDimension} pixels.");
        byte[] target = new byte[checked(width * height * 4)];
        for (int row = 0; row < height; row++)
        {
            int fromRow = checked((int)((long)row * image.Height / height));
            int toRow = Math.Max(fromRow + 1, checked((int)((long)(row + 1) * image.Height / height)));
            for (int column = 0; column < width; column++)
            {
                int fromColumn = checked((int)((long)column * image.Width / width));
                int toColumn = Math.Max(fromColumn + 1, checked((int)((long)(column + 1) * image.Width / width)));
                long[] sums = new long[4];
                int samples = 0;
                for (int sourceRow = fromRow; sourceRow < toRow; sourceRow++)
                {
                    int rowOffset = checked(sourceRow * image.Width * 4);
                    for (int sourceColumn = fromColumn; sourceColumn < toColumn; sourceColumn++)
                    {
                        int offset = rowOffset + (sourceColumn * 4);
                        sums[0] += image.Rgba[offset];
                        sums[1] += image.Rgba[offset + 1];
                        sums[2] += image.Rgba[offset + 2];
                        sums[3] += image.Rgba[offset + 3];
                        samples++;
                    }
                }

                int index = checked((row * width * 4) + (column * 4));
                for (int channel = 0; channel < 4; channel++)
                {
                    target[index + channel] = (byte)((sums[channel] + (samples / 2)) / samples);
                }
            }
        }

        return new DeterministicPngImage(width, height, target);
    }
}
