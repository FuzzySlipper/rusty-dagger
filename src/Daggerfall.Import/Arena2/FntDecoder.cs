namespace Daggerfall.Import.Arena2;

/// <summary>One classic 16x16 glyph decoded from an Arena2 FNT table.</summary>
public sealed class FntGlyph
{
    internal FntGlyph(ushort dataOffset, ushort width, bool[] pixels)
    {
        DataOffset = dataOffset;
        Width = width;
        Pixels = pixels;
    }

    /// <summary>Source offset of the glyph's 32-byte bitmap.</summary>
    public ushort DataOffset { get; }

    /// <summary>Glyph advance width from the source table.</summary>
    public ushort Width { get; }

    /// <summary>Decoded row-major 16 by 16 glyph pixels.</summary>
    public ReadOnlyMemory<bool> Pixels { get; }

    /// <summary>Reads one decoded glyph pixel.</summary>
    public bool IsSet(int x, int y)
    {
        if ((uint)x >= 16 || (uint)y >= 16)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Glyph coordinates must be within 0..15.");
        }

        return Pixels.Span[(y * 16) + x];
    }
}

/// <summary>Decoded classic fixed-grid FNT font data.</summary>
public sealed record FntFont(string Source, ushort FixedWidth, ushort FixedHeight, IReadOnlyList<FntGlyph> Glyphs);

/// <summary>Decoder for classic Arena2 FNT glyph tables.</summary>
public static class FntDecoder
{
    /// <summary>Decodes fixed-grid FNT glyphs, preserving the donor bit orientation.</summary>
    public static FntFont Decode(ReadOnlySpan<byte> bytes, string source)
    {
        CheckedLittleEndianReader reader = new(bytes, source);
        ushort fixedWidth = reader.ReadUInt16();
        ushort fixedHeight = reader.ReadUInt16();
        if (fixedWidth is 0 or > 16 || fixedHeight is 0 or > 16)
        {
            throw reader.Error($"unsupported FNT fixed metrics {fixedWidth}x{fixedHeight}");
        }

        ReadOnlySpan<byte> table = reader.ReadBytes(Arena2FormatConstants.FntGlyphTableBytes);
        List<FntGlyph> glyphs = new(Arena2FormatConstants.FntGlyphCount);
        for (int index = 0; index < Arena2FormatConstants.FntGlyphCount; index++)
        {
            int tableOffset = index * 4;
            ushort dataOffset = ReadUInt16(table, tableOffset);
            ushort width = ReadUInt16(table, tableOffset + 2);
            if (width > 16)
            {
                throw new Arena2FormatException(source, Arena2FormatConstants.FntHeaderBytes + tableOffset + 2, $"FNT glyph {index} width {width} exceeds 16 pixels");
            }

            int glyphOffset = dataOffset;
            if (glyphOffset > bytes.Length - Arena2FormatConstants.FntGlyphBytes)
            {
                throw new Arena2FormatException(source, glyphOffset, $"FNT glyph {index} bitmap exceeds source length {bytes.Length}");
            }

            bool[] pixels = DecodeGlyph(bytes.Slice(glyphOffset, Arena2FormatConstants.FntGlyphBytes));
            glyphs.Add(new FntGlyph(dataOffset, width, pixels));
        }

        return new FntFont(source, fixedWidth, fixedHeight, glyphs);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
    }

    private static bool[] DecodeGlyph(ReadOnlySpan<byte> source)
    {
        bool[] pixels = new bool[16 * 16];
        for (int row = 0; row < 16; row++)
        {
            // Daggerfall Unity's FntFile reads byte 1 as x=0..7 and byte 0 as x=8..15.
            byte left = source[(row * 2) + 1];
            byte right = source[row * 2];
            for (int bit = 0; bit < 8; bit++)
            {
                // Source bit zero is the rightmost pixel in each eight-pixel byte.
                pixels[(row * 16) + (7 - bit)] = (left & (1 << bit)) != 0;
                pixels[(row * 16) + 8 + (7 - bit)] = (right & (1 << bit)) != 0;
            }
        }

        return pixels;
    }
}
