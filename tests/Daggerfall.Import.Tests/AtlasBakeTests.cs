using Daggerfall.Import.Normalization;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class AtlasBakeTests
{
    // A small world: 16 by 8 map pixels at four relief nodes each, the sea to the south and a ridge across the land.
    private const int MapWidth = 16, MapHeight = 8, Nodes = 4, Width = MapWidth * Nodes, Height = MapHeight * Nodes;

    private static AtlasInput Input(IReadOnlyList<AtlasRiver>? rivers = null)
    {
        float[] metres = new float[Width * Height];
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            metres[(y * Width) + x] = y >= Height - 8 ? 40.8f : 60 + (400 * MathF.Exp(-MathF.Pow((y - 8) / 4f, 2)));
        string[] climate = [.. Enumerable.Range(0, MapWidth * MapHeight).Select(i => i % MapWidth < MapWidth / 2 ? "Woodlands" : "Desert")];
        int[] region = [.. Enumerable.Range(0, MapWidth * MapHeight).Select(i => i / MapWidth >= MapHeight - 2 ? -1 : i % MapWidth < MapWidth / 2 ? 3 : 4)];
        return new(Width, Height, metres, Nodes, climate, region, rivers ?? []);
    }

    private static (byte R, byte G, byte B) Pixel(byte[] rgba, int x, int y) =>
        (rgba[((y * Width) + x) * 4], rgba[(((y * Width) + x) * 4) + 1], rgba[(((y * Width) + x) * 4) + 2]);

    [Fact]
    public void An_atlas_depends_on_its_input_alone()
    {
        Assert.Equal(AtlasBake.Draw(Input(), AtlasPalette.Atlas), AtlasBake.Draw(Input(), AtlasPalette.Atlas));
    }

    [Fact]
    public void The_sea_is_drawn_as_water_and_the_land_in_its_climates_colour()
    {
        byte[] atlas = AtlasBake.Draw(Input(), AtlasPalette.Atlas);
        (byte r, byte g, byte b) sea = Pixel(atlas, 10, Height - 2);
        Assert.True(sea.b > sea.r, $"Open sea {sea} is drawn bluer than red.");
        (byte r, byte g, byte b) woodland = Pixel(atlas, 6, 20), desert = Pixel(atlas, Width - 6, 20);
        Assert.True(desert.r > woodland.r, "The desert is drawn warmer than the woodland.");
        Assert.All(Enumerable.Range(0, atlas.Length / 4), i => Assert.Equal(255, atlas[(i * 4) + 3]));
    }

    [Fact]
    public void A_river_is_inked_on_land_where_it_runs_and_stops_at_the_coast()
    {
        AtlasRiver river = new([20, 20], [12, Height - 1], [60, 60]);
        byte[] plain = AtlasBake.Draw(Input(), AtlasPalette.Atlas), drawn = AtlasBake.Draw(Input([river]), AtlasPalette.Atlas);
        Assert.NotEqual(Pixel(plain, 20, 18), Pixel(drawn, 20, 18));
        Assert.Equal(Pixel(plain, 40, 18), Pixel(drawn, 40, 18));
        Assert.Equal(Pixel(plain, 20, Height - 2), Pixel(drawn, 20, Height - 2));
    }

    [Fact]
    public void Each_level_halves_the_next_and_is_cut_into_whole_tiles_coarsest_first()
    {
        IReadOnlyList<AtlasLevel> levels = AtlasBake.Levels(4000, 2000);
        Assert.Equal([new(0, 1000, 500, 4, 2), new(1, 2000, 1000, 8, 4), new(2, 4000, 2000, 16, 8)], levels);
        AtlasTile[] tiles = [.. AtlasBake.Tiles(Input(), AtlasPalette.Atlas)];
        Assert.Equal(AtlasBake.LevelCount, tiles.Select(tile => tile.Level).Distinct().Count());
        Assert.All(tiles, tile => Assert.Equal(AtlasBake.TileSize * AtlasBake.TileSize * 4, tile.Rgba.Length));
        // A level smaller than a tile is padded with its own edge, so a scaled tile shows no seam at the map's edge.
        AtlasTile finest = Assert.Single(tiles, tile => tile.Level == AtlasBake.LevelCount - 1);
        Assert.Equal(finest.Rgba.AsSpan(((10 * AtlasBake.TileSize) + Width - 1) * 4, 4).ToArray(), finest.Rgba.AsSpan(((10 * AtlasBake.TileSize) + Width + 30) * 4, 4).ToArray());
    }
}
