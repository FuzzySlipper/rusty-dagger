namespace Daggerfall.Import.Arena2;

/// <summary>One 8-bit RGB palette color.</summary>
public readonly record struct Rgb24(byte Red, byte Green, byte Blue);

/// <summary>One 8-bit RGBA output pixel.</summary>
public readonly record struct Rgba32(byte Red, byte Green, byte Blue, byte Alpha);

/// <summary>Controls Arena2 palette index-zero alpha interpretation.</summary>
public enum PaletteAlphaMode
{
    /// <summary>Every palette index, including zero, is opaque.</summary>
    Opaque,

    /// <summary>Palette index zero is transparent; all other indices are opaque.</summary>
    IndexZeroTransparent,
}

/// <summary>Decoded Arena2 palette with explicit indexed-pixel alpha conversion.</summary>
public sealed class Arena2Palette
{
    internal Arena2Palette(string source, Rgb24[] colors)
    {
        Source = source;
        Colors = colors;
    }

    /// <summary>Logical source identity supplied to the decoder.</summary>
    public string Source { get; }

    /// <summary>All 256 source palette colors in index order.</summary>
    public ReadOnlyMemory<Rgb24> Colors { get; }

    /// <summary>Converts indexed source pixels using an explicit alpha policy.</summary>
    public Rgba32[] ToRgba(ReadOnlySpan<byte> indexedPixels, PaletteAlphaMode alphaMode)
    {
        if (alphaMode is not PaletteAlphaMode.Opaque and not PaletteAlphaMode.IndexZeroTransparent)
        {
            throw new ArgumentOutOfRangeException(nameof(alphaMode), alphaMode, "Unsupported palette alpha mode.");
        }

        Rgba32[] output = new Rgba32[indexedPixels.Length];
        ReadOnlySpan<Rgb24> colors = Colors.Span;
        for (int index = 0; index < indexedPixels.Length; index++)
        {
            byte paletteIndex = indexedPixels[index];
            Rgb24 color = colors[paletteIndex];
            byte alpha = alphaMode == PaletteAlphaMode.IndexZeroTransparent && paletteIndex == 0 ? (byte)0 : byte.MaxValue;
            output[index] = new Rgba32(color.Red, color.Green, color.Blue, alpha);
        }

        return output;
    }
}

/// <summary>Decoder for 256-color Arena2 palette files.</summary>
public static class PaletteDecoder
{
    /// <summary>Decodes a bare 768-byte palette or an 8-byte-header-plus-palette source.</summary>
    public static Arena2Palette Decode(ReadOnlySpan<byte> bytes, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ReadOnlySpan<byte> rgb = bytes.Length switch
        {
            Arena2FormatConstants.PaletteRgbBytes => bytes,
            Arena2FormatConstants.PaletteHeaderedBytes => bytes[8..],
            _ => throw new Arena2FormatException(source, 0, $"palette length must be {Arena2FormatConstants.PaletteRgbBytes} or {Arena2FormatConstants.PaletteHeaderedBytes} bytes, got {bytes.Length}"),
        };

        Rgb24[] colors = new Rgb24[256];
        for (int index = 0; index < colors.Length; index++)
        {
            int offset = index * 3;
            colors[index] = new Rgb24(rgb[offset], rgb[offset + 1], rgb[offset + 2]);
        }

        return new Arena2Palette(source, colors);
    }
}
