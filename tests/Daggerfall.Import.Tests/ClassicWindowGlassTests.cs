using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class ClassicWindowGlassTests
{
    private static IndexedTextureFrame Frame(int width, int height, Func<int, int, byte> pixel)
    {
        byte[] pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = pixel(x, y);
        return new("test", 0, 0, (ushort)width, (ushort)height, pixels);
    }

    private static bool Lit(byte[] rgba, int width, int x, int y) => rgba[(y * width + x) * 4] == byte.MaxValue;

    [Fact]
    public void A_window_lights_the_pixels_painted_in_the_glass_index()
    {
        byte[] mask = ClassicWindowGlass.Mask(112, 3, Frame(4, 2, (x, y) => x == 1 && y == 1 ? (byte)255 : (byte)7))!;

        Assert.True(Lit(mask, 4, 1, 1));
        Assert.False(Lit(mask, 4, 2, 1));
        Assert.All(Enumerable.Range(0, 8), pixel => Assert.Equal(byte.MaxValue, mask[pixel * 4 + 3]));
    }

    [Fact]
    public void A_painted_window_lights_its_colour_only_within_the_window()
    {
        byte[] mask = ClassicWindowGlass.Mask(109, 3, Frame(64, 128, (_, _) => 127))!;

        Assert.True(Lit(mask, 64, 30, 40));
        Assert.False(Lit(mask, 64, 2, 40));
        Assert.False(Lit(mask, 64, 30, 100));
    }

    [Theory]
    [InlineData(112, 0)] // a plain wall of a window set
    [InlineData(375, 0)] // a spell, which the archive arithmetic would otherwise call a gable
    [InlineData(210, 3)] // a lamp
    public void A_record_that_is_no_window_has_no_glass(int archive, int record) =>
        Assert.Null(ClassicWindowGlass.Mask(archive, record, Frame(2, 2, (_, _) => 255)));

    [Fact]
    public void A_window_record_showing_no_glass_publishes_no_mask() =>
        Assert.Null(ClassicWindowGlass.Mask(36, 3, Frame(4, 4, (_, _) => 7)));
}
