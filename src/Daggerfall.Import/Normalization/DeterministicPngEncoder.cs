using System.IO.Compression;

namespace Daggerfall.Import.Normalization;

/// <summary>Writes the small, fixed PNG subset used by normalized RGBA artifacts.</summary>
public static class DeterministicPngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>
    /// Encodes row-major RGBA8 pixels as one non-interlaced PNG. The writer has
    /// no metadata, timestamps, filesystem input, or platform-specific chunks.
    /// </summary>
    public static byte[] EncodeRgba8(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ValidateDimensions(width, height);
        int pixelBytes = checked(width * height * 4);
        if (rgba.Length != pixelBytes)
        {
            throw new ArgumentException("RGBA input must contain exactly four bytes per pixel.", nameof(rgba));
        }

        byte[] scanlines = new byte[checked((width * 4 + 1) * height)];
        int sourceOffset = 0;
        int targetOffset = 0;
        for (int row = 0; row < height; row++)
        {
            scanlines[targetOffset++] = 0;
            rgba.Slice(sourceOffset, width * 4).CopyTo(scanlines.AsSpan(targetOffset));
            sourceOffset += width * 4;
            targetOffset += width * 4;
        }

        byte[] compressed = Compress(scanlines);
        using MemoryStream output = new(Signature.Length + compressed.Length + 64);
        output.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        WriteUInt32BigEndian(header, 0, checked((uint)width));
        WriteUInt32BigEndian(header, 4, checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, compressed);
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static byte[] Compress(byte[] scanlines)
    {
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(scanlines);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteUInt32BigEndian(length, 0, checked((uint)data.Length));
        output.Write(length);
        output.Write(kind);
        output.Write(data);

        uint crc = 0xffffffff;
        crc = UpdateCrc(crc, kind);
        crc = UpdateCrc(crc, data) ^ 0xffffffff;
        Span<byte> checksum = stackalloc byte[4];
        WriteUInt32BigEndian(checksum, 0, crc);
        output.Write(checksum);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            uint next = crc ^ value;
            for (int bit = 0; bit < 8; bit++)
            {
                next = (next & 1) == 0 ? next >> 1 : 0xedb88320 ^ (next >> 1);
            }

            crc = next;
        }

        return crc;
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "PNG dimensions must be positive.");
        }
    }

    private static void WriteUInt32BigEndian(Span<byte> output, int offset, uint value)
    {
        output[offset] = (byte)(value >> 24);
        output[offset + 1] = (byte)(value >> 16);
        output[offset + 2] = (byte)(value >> 8);
        output[offset + 3] = (byte)value;
    }
}
