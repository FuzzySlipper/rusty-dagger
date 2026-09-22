using System.Security.Cryptography;
using System.Text.Json;
using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// Residual screens: the six canvases that carry their palette inside the file publish with
/// that palette scaled the way the classic reader scales it, and repeat conversions agree.
/// </summary>
public sealed class ResidualScreenPublicationTests
{
    private static readonly string[] Screens = ["CHGN00I0.IMG", "DIE_00I0.IMG", "PICK02I0.IMG", "PICK03I0.IMG", "PRIS00I0.IMG", "TITL00I0.IMG"];

    [Fact]
    public void Screens_publish_with_their_embedded_palette_scaled_by_four()
    {
        string root = RepositoryRoot();
        JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/imports/privateers-hold/media/classic/manifest.json")));
        Dictionary<string, JsonElement> ui = manifest.RootElement.GetProperty("uiImages").EnumerateArray().ToDictionary(image => image.GetProperty("sourceFile").GetString()!);

        foreach (string screen in Screens)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, "local/arena2", screen));
            Assert.Equal(ImgDecoder.EmbeddedPaletteScreenBytes, bytes.Length);
            Assert.True(ImgDecoder.TryReadEmbeddedPalette(bytes, screen, out Arena2Palette? palette, out string reason), reason);
            Assert.NotNull(palette);

            // The conversion fact: every channel is the trailing byte scaled by four.
            for (int index = 0; index < 256; index++)
            {
                Assert.Equal(Math.Min(255, bytes[320 * 200 + (index * 3)] * 4), palette.Colors.Span[index].Red);
                Assert.Equal(Math.Min(255, bytes[320 * 200 + (index * 3) + 1] * 4), palette.Colors.Span[index].Green);
                Assert.Equal(Math.Min(255, bytes[320 * 200 + (index * 3) + 2] * 4), palette.Colors.Span[index].Blue);
            }

            // The published artifact names the palette it was painted with.
            Assert.True(ui[screen].GetProperty("ownEmbeddedPalette").GetBoolean());
        }
    }

    [Fact]
    public void Repeating_a_screen_conversion_reproduces_its_bytes()
    {
        string root = RepositoryRoot();
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "local/arena2/TITL00I0.IMG"));
        Assert.True(ImgDecoder.TryReadEmbeddedPalette(bytes, "TITL00I0.IMG", out Arena2Palette? palette, out _));
        Assert.NotNull(palette);

        string first = ConvertHash(bytes, palette);
        string second = ConvertHash(bytes, palette);
        Assert.Equal(first, second);
    }

    private static string ConvertHash(byte[] bytes, Arena2Palette palette)
    {
        Assert.True(ImgDecoder.TryDecodeHeaderless(bytes, "TITL00I0.IMG", out IndexedImg? image, out string decodeReason), decodeReason);
        Assert.NotNull(image);
        byte[] indexed = image.Pixels.ToArray();
        Rgba32[] converted = palette.ToRgba(indexed, PaletteAlphaMode.Opaque);
        byte[] pixels = new byte[converted.Length * 4];
        for (int index = 0; index < converted.Length; index++)
        {
            pixels[(index * 4)] = converted[index].Red;
            pixels[(index * 4) + 1] = converted[index].Green;
            pixels[(index * 4) + 2] = converted[index].Blue;
            pixels[(index * 4) + 3] = converted[index].Alpha;
        }

        return Convert.ToHexString(SHA256.HashData(pixels));
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }
}
