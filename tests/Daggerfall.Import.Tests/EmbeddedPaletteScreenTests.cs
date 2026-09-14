using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// Six supplied screens carry their palette inside the file, after the canvas, and the classic reader
/// scales that palette's channels by four. These checks pin the donor's rule at both ends: only the
/// 64,768-byte shape carries a palette, and the screen the product publishes is painted in it.
/// </summary>
public sealed class EmbeddedPaletteScreenTests
{
    private const string DeathScreen = "DIE_00I0.IMG";

    [Fact]
    public void ReadsTheEmbeddedPaletteOnlyFromTheScreenShape()
    {
        byte[] screen = ReadArena2(DeathScreen);
        Assert.Equal(ImgDecoder.EmbeddedPaletteScreenBytes, screen.Length);
        Assert.True(ImgDecoder.TryReadEmbeddedPalette(screen, DeathScreen, out Arena2Palette? palette, out string reason), reason);

        // The palette is the file's own trailing bytes, scaled by four exactly as the donor does it.
        byte[] raw = screen[^ImgDecoder.EmbeddedPaletteBytes..];
        ReadOnlySpan<Rgb24> colors = palette!.Colors.Span;
        Assert.Equal(256, colors.Length);
        for (int index = 0; index < 256; index++)
        {
            Assert.Equal(Math.Min(255, raw[index * 3] * 4), colors[index].Red);
            Assert.Equal(Math.Min(255, raw[(index * 3) + 1] * 4), colors[index].Green);
            Assert.Equal(Math.Min(255, raw[(index * 3) + 2] * 4), colors[index].Blue);
        }

        // A canvas of the 320x200 shape reached from any other length carries no palette, and the refusal
        // says which length it would need rather than handing the canvas a palette it does not have.
        byte[] canvasOnly = new byte[320 * 200];
        Assert.False(ImgDecoder.TryReadEmbeddedPalette(canvasOnly, "synthetic canvas", out Arena2Palette? none, out string why));
        Assert.Null(none);
        Assert.Contains("64768", why, StringComparison.Ordinal);
        Assert.Contains("64000", why, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every one of the six supplied screens is published in its own palette, not just the first one that
    /// happened to have a consumer: a screen admitted later through a different path would show up here.
    /// </summary>
    [Theory]
    [InlineData("DIE_00I0.IMG", "screen-death")]
    [InlineData("CHGN00I0.IMG", "screen-character-generation")]
    [InlineData("PICK02I0.IMG", "screen-pick-02")]
    [InlineData("PICK03I0.IMG", "screen-start-menu")]
    [InlineData("PRIS00I0.IMG", "screen-prison")]
    [InlineData("TITL00I0.IMG", "screen-title")]
    public void PublishesTheScreenInItsOwnPaletteColours(string fileName, string artifact)
    {
        string root = RepositoryRoot();
        byte[] screen = ReadArena2(fileName);
        Assert.True(ImgDecoder.TryReadEmbeddedPalette(screen, fileName, out Arena2Palette? palette, out string reason), reason);

        // The published artifact must be the canvas painted in the file's own palette: a pixel's colour is
        // the palette entry its index names, which is four times the raw channel. Pairing the canvas with
        // another palette, or dropping the scaling, changes every colour and fails here.
        DeterministicPngImage published = DeterministicPngReader.ReadRgba8(
            File.ReadAllBytes(Path.Combine(root, "content", "worldrpg", "media", "ui", artifact + ".png")), artifact);
        IndexedImg canvas = ImgDecoder.DecodeHeaderless(screen.AsSpan(0, 320 * 200), fileName);
        Assert.Equal((320, 200), (published.Width, published.Height));
        Assert.Equal((canvas.Width, canvas.Height), (published.Width, published.Height));

        ReadOnlySpan<byte> indices = canvas.Pixels.Span;
        ReadOnlySpan<Rgb24> colors = palette!.Colors.Span;
        int opaque = 0;
        for (int pixel = 0; pixel < 320 * 200; pixel++)
        {
            byte index = indices[pixel];
            byte red = colors[index].Red;
            byte green = colors[index].Green;
            byte blue = colors[index].Blue;
            int offset = pixel * 4;
            Assert.Equal(red, published.Rgba[offset]);
            Assert.Equal(green, published.Rgba[offset + 1]);
            Assert.Equal(blue, published.Rgba[offset + 2]);
            if (published.Rgba[offset + 3] != 0) opaque++;
        }

        Assert.True(opaque > 320 * 200 / 2, $"only {opaque} of {320 * 200} published pixels carry colour");
    }

    private static byte[] ReadArena2(string name)
    {
        string path = Path.Combine(RepositoryRoot(), "local", "arena2", name);
        Assert.True(File.Exists(path), $"{name} is not staged at {path}; the classic corpus is required for this check.");
        return File.ReadAllBytes(path);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }
}
