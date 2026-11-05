using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallHorizonGroundTests
{
    private const double Size = DaggerfallTerrainSurfaceBuilder.HorizontalSize;

    [Fact]
    public void The_horizon_stands_on_the_walked_grounds_own_vertices()
    {
        DaggerfallTerrainSet terrain = Terrain(8, 8);
        DaggerfallHorizonGround ground = Ground(terrain);
        DaggerfallTerrainSurface surface = DaggerfallTerrainSurfaceBuilder.Build(terrain, 3, 3);

        // Rows run north from the pixel's own z, so vertex (x, y) of pixel (3, 3) stands at (3 + x, 3 - y) in vertices.
        foreach ((int x, int y) in new[] { (0, 0), (8, 0), (0, 8), (64, 40), (120, 96) })
            Assert.Equal(surface.Vertices[x + (y * 129)].Y,
                ground.Height((3 * Size) + (x * DaggerfallTerrainSurfaceBuilder.SampleSpacing), (3 * Size) - (y * DaggerfallTerrainSurfaceBuilder.SampleSpacing)), 3);
    }

    [Fact]
    public void Past_the_map_the_ground_runs_on_at_the_edges_height()
    {
        DaggerfallTerrainSet terrain = Terrain(8, 8);
        DaggerfallHorizonGround ground = Ground(terrain);

        Assert.True(double.IsFinite(ground.Height(-40 * Size, -40 * Size)));
        Assert.True(double.IsFinite(ground.Height(40 * Size, 40 * Size)));
    }

    [Fact]
    public void Each_rings_tile_is_a_whole_number_of_the_last_and_the_finest_is_one_map_pixel()
    {
        var rings = DaggerfallHorizonGround.Rings;

        Assert.Equal(Size, rings[0].CellSize * DaggerfallHorizonGround.TileCells, 6);
        for (int ring = 1; ring < rings.Length; ring++) Assert.Equal(4, rings[ring].CellSize / rings[ring - 1].CellSize, 6);
        foreach (var ring in rings)
            Assert.Equal(0, ring.CellSize % DaggerfallTerrainSurfaceBuilder.SampleSpacing, 6);
    }

    [Fact]
    public void Only_ground_above_the_player_is_lifted_and_more_with_distance()
    {
        DaggerfallHorizonGround ground = Ground(Terrain(8, 8));
        DaggerfallHorizonGround.Frame frame = new(0, 0, 100, 3, false);
        DaggerfallHorizonTuning tuning = DaggerfallHorizonTuning.Default;

        Assert.Equal(50, ground.Lifted(50, tuning.LiftFullMetres, 0, frame));
        Assert.Equal(400, ground.Lifted(400, tuning.LiftFromMetres, 0, frame));
        Assert.Equal(100 + (300 * 3), ground.Lifted(400, tuning.LiftFullMetres, 0, frame), 6);
        Assert.Equal(100 + (300 * 3), ground.Lifted(400, 0, -tuning.LiftFullMetres * 2, frame), 6);
        double between = ground.Lifted(400, (tuning.LiftFromMetres + tuning.LiftFullMetres) / 2, 0, frame);
        Assert.InRange(between, 400.5, 999.5);
    }

    [Fact]
    public void A_lift_of_one_draws_true_height()
    {
        DaggerfallHorizonGround ground = Ground(Terrain(8, 8));

        Assert.Equal(400, ground.Lifted(400, 50_000, 0, new(0, 0, 100, 1, false)));
    }

    [Fact]
    public void Far_ground_wears_its_climate_with_rock_snow_and_sea_over_it()
    {
        DaggerfallHorizonGround ground = Ground(Terrain(8, 8));
        DaggerfallHorizonTuning tuning = DaggerfallHorizonTuning.Default;
        double sea = DaggerfallTerrainSurfaceBuilder.OceanElevation * DaggerfallTerrainSurfaceBuilder.TerrainScale;

        Assert.Equal(tuning.Sea, ground.Paint(231, sea, 0, false));
        Assert.Equal(tuning.Climates[231], ground.Paint(231, 200, 0, false));
        Assert.Equal(tuning.Rock, ground.Paint(231, 200, tuning.RockFullSlope, false));
        Assert.Equal(tuning.Snow, ground.Paint(231, tuning.SnowFullMetres, 0, false));
        Assert.NotEqual(tuning.Climates[231], ground.Paint(231, 200, 0, true));
        Assert.Equal(tuning.Climates[224], ground.Paint(224, 200, 0, true));
    }

    [Fact]
    public void A_climate_without_a_colour_is_reported()
    {
        DaggerfallHorizonGround ground = Ground(Terrain(8, 8));

        Assert.Contains("999", Assert.Throws<InvalidOperationException>(() => ground.Paint(999, 200, 0, false)).Message);
    }

    [Fact]
    public void Where_the_relief_gives_way_the_classic_height_stands()
    {
        DaggerfallTerrainSet classic = Terrain(8, 8);
        DaggerfallTerrainSet shore = Terrain(8, 8, Relief(metres: 900, share: 0));

        Assert.Equal(DaggerfallTerrainSurfaceBuilder.Build(classic, 3, 3).Vertices, DaggerfallTerrainSurfaceBuilder.Build(shore, 3, 3).Vertices);
    }

    [Fact]
    public void Walked_and_far_ground_stand_on_the_relief_alike_and_run_on_across_map_pixels()
    {
        DaggerfallTerrainSet terrain = Terrain(8, 8, Relief(metres: 500, share: 255));
        DaggerfallTerrainSurface west = DaggerfallTerrainSurfaceBuilder.Build(terrain, 3, 3), east = DaggerfallTerrainSurfaceBuilder.Build(terrain, 4, 3);

        Assert.Equal(500, west.Vertices[64 + (64 * 129)].Y, 1);
        Assert.Equal(500, Ground(terrain).Height((3 * Size) + 400, (3 * Size) - 400), 1);
        for (int y = 0; y <= 128; y += 16) Assert.Equal(west.Vertices[128 + (y * 129)].Y, east.Vertices[y * 129].Y, 3);
    }

    [Fact]
    public void A_relief_of_the_wrong_length_is_refused()
    {
        byte[] bytes = Relief(metres: 900, share: 255);

        Assert.Throws<InvalidOperationException>(() => DaggerfallTerrainRelief.Read(bytes.AsSpan(0, bytes.Length - 1)));
    }

    /// <summary>A flat relief over the whole map: flat ground takes no gullies, so a height reads back as written.</summary>
    private static byte[] Relief(float metres, byte share)
    {
        const int width = 2_000, height = 1_000;
        byte[] bytes = new byte[12 + (width * height * 5)];
        "RLF1"u8.CopyTo(bytes);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), height);
        for (int i = 0; i < width * height; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(12 + (i * 4)), metres);
        bytes.AsSpan(12 + (width * height * 4)).Fill(share);
        return bytes;
    }

    private static DaggerfallHorizonGround Ground(DaggerfallTerrainSet terrain) =>
        new(terrain, (_, _) => 231, DaggerfallHorizonTuning.Default);

    private static DaggerfallTerrainSet Terrain(int width, int height, byte[]? relief = null)
    {
        byte[] heights = new byte[width * height];
        byte[] samples = new byte[width * height * 25];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int cell = (y * width) + x;
            heights[cell] = checked((byte)(80 + (x * 7) + (y * 9)));
            for (int sample = 0; sample < 25; sample++)
                samples[(cell * 25) + sample] = checked((byte)(20 + (x * 3) + (y * 5) + sample));
        }

        return new DaggerfallTerrainSet(width, height, heights, () => samples,
            relief is null ? null : () => DaggerfallTerrainRelief.Read(relief));
    }
}
