namespace Daggerfall.Import.Arena2;

/// <summary>
/// The classic spectral texture archives (Ghost 273, Wraith 278 and Lysandus 473), whose
/// nonzero pixels are not palette colours but shade levels that the classic renderer applies
/// as a translucency table; their <c>PAL.PAL</c> entries are magenta placeholders. Conversion
/// follows Daggerfall Unity's <c>TextureReader.SetSpectral</c> and its spectral
/// <c>GetColor32</c> call: shade <c>i</c> becomes the grey ramp entry <c>96 - i</c> (bones
/// brighter than the shroud), the reserved eye shade becomes the opaque eye colour, and the
/// body keeps the donor's partial alpha.
/// </summary>
public static class Arena2SpectralTextures
{
    private const int GreyRampEnd = 96;
    private const byte EyeShade = 14;
    private const byte EyePaletteIndex = 247;
    private const byte BodyAlpha = 180;

    /// <summary>Whether a TEXTURE archive stores spectral shade levels rather than palette colours.</summary>
    public static bool IsSpectralArchive(ushort archive) => archive is 273 or 278 or 473;

    /// <summary>Converts spectral shade levels to row-major RGBA bytes; shade zero is transparent.</summary>
    public static byte[] ToRgbaBytes(ReadOnlySpan<byte> shades, Arena2Palette palette, string source)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ReadOnlySpan<Rgb24> colors = palette.Colors.Span;
        byte[] rgba = new byte[checked(shades.Length * 4)];
        for (int index = 0; index < shades.Length; index++)
        {
            byte shade = shades[index];
            if (shade == 0)
            {
                continue;
            }

            // A shade at or beyond the ramp would wrap to an unrelated colour, a silently
            // wrong artifact rather than a ghost.
            if (shade >= GreyRampEnd)
            {
                throw new Arena2FormatException(source, 0, $"spectral shade {shade} lies outside the grey ramp (1-{GreyRampEnd - 1})");
            }

            bool eye = shade == EyeShade;
            Rgb24 color = colors[eye ? EyePaletteIndex : GreyRampEnd - shade];
            int offset = index * 4;
            rgba[offset] = color.Red;
            rgba[offset + 1] = color.Green;
            rgba[offset + 2] = color.Blue;
            rgba[offset + 3] = eye ? byte.MaxValue : BodyAlpha;
        }

        return rgba;
    }
}
