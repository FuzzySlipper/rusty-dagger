namespace Daggerfall.Import.Arena2;

/// <summary>Decoded 1001-by-500 PAK source pixels, including the donor sentinel column.</summary>
public sealed class PakMap
{
    internal PakMap(string source, byte[] pixels)
    {
        Source = source;
        Pixels = pixels;
    }

    /// <summary>Logical source identity supplied to the decoder.</summary>
    public string Source { get; }

    /// <summary>Decoded donor row width: 1000 map pixels plus one sentinel column.</summary>
    public const int Width = 1001;

    /// <summary>Decoded donor map height in rows.</summary>
    public const int Height = 500;

    /// <summary>Row-major decoded source pixels, retaining the sentinel column.</summary>
    public ReadOnlyMemory<byte> Pixels { get; }

    /// <summary>Attempts to read one source pixel, including the sentinel column.</summary>
    public bool TryGetPixel(int x, int y, out byte value)
    {
        if ((uint)x >= Width || (uint)y >= Height)
        {
            value = default;
            return false;
        }

        value = Pixels.Span[(y * Width) + x];
        return true;
    }
}

/// <summary>Decoder for CLIMATE.PAK and POLITIC.PAK row-offset RLE data.</summary>
public static class PakDecoder
{
    private const int RowOffsetBytes = sizeof(uint);

    /// <summary>Decodes all fixed PAK rows and rejects underfilled, overfilled, or truncated runs.</summary>
    public static PakMap Decode(ReadOnlySpan<byte> bytes, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        int offsetTableBytes = checked(PakMap.Height * RowOffsetBytes);
        if (bytes.Length < offsetTableBytes)
        {
            throw new Arena2FormatException(source, bytes.Length, $"PAK requires a {offsetTableBytes}-byte row offset table");
        }

        byte[] output = new byte[checked(PakMap.Width * PakMap.Height)];
        for (int row = 0; row < PakMap.Height; row++)
        {
            CheckedLittleEndianReader offsets = new(bytes, source);
            offsets.Seek(row * RowOffsetBytes);
            uint rawOffset = offsets.ReadUInt32();
            if (rawOffset > int.MaxValue)
            {
                throw offsets.Error($"PAK row {row} offset exceeds the importer quota");
            }

            CheckedLittleEndianReader runs = new(bytes, source);
            runs.Seek((int)rawOffset);
            int column = 0;
            while (column < PakMap.Width)
            {
                ushort count = runs.ReadUInt16();
                byte value = runs.ReadByte();
                if (count == 0)
                {
                    throw runs.Error($"PAK row {row} contains an empty RLE run");
                }

                if (count > PakMap.Width - column)
                {
                    throw runs.Error($"PAK row {row} RLE run exceeds its {PakMap.Width}-pixel width");
                }

                output.AsSpan((row * PakMap.Width) + column, count).Fill(value);
                column += count;
            }
        }

        return new PakMap(source, output);
    }
}
