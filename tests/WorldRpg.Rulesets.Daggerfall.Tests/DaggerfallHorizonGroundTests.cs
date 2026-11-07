using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Heightfields;
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
    public void Ground_is_lifted_above_its_valley_floor_and_more_with_distance()
    {
        DaggerfallHorizonGround ground = Ground(Terrain(8, 8));
        DaggerfallHorizonTuning tuning = DaggerfallHorizonTuning.Default;
        DaggerfallHorizonGround.Frame frame = new(0, 0, 3, false);
        double floor = ground.Floor(tuning.LiftFullMetres, 0), height = floor + 300;

        Assert.Equal(floor - 50, ground.Lifted(floor - 50, tuning.LiftFullMetres, 0, frame));
        Assert.Equal(ground.Floor(tuning.LiftFromMetres, 0) + 300, ground.Lifted(ground.Floor(tuning.LiftFromMetres, 0) + 300, tuning.LiftFromMetres, 0, frame), 6);
        Assert.Equal(floor + (300 * 3), ground.Lifted(height, tuning.LiftFullMetres, 0, frame), 6);
        double middle = (tuning.LiftFromMetres + tuning.LiftFullMetres) / 2, between = ground.Floor(middle, 0);
        Assert.InRange(ground.Lifted(between + 300, middle, 0, frame), between + 300.5, between + 899.5);
    }

    [Fact]
    public void A_range_only_ever_grows_as_the_player_walks_toward_it()
    {
        DaggerfallHorizonTuning tuning = DaggerfallHorizonTuning.Default;
        DaggerfallHorizonGround ground = Ground(Terrain(8, 8));
        const double peakX = 30_000;
        double floor = ground.Floor(peakX, 0), peak = floor + 1_000, last = 0;
        for (double away = tuning.LiftFullMetres + 6_000; away >= tuning.LiftFromMetres; away -= 500)
        {
            double seen = (ground.Lifted(peak, peakX, 0, new(peakX - away, 0, tuning.Lift, false)) - floor) / away;
            Assert.True(seen > last, $"The peak shrinks to {seen:F4} from {last:F4} at {away} m.");
            last = seen;
        }
    }

    [Fact]
    public void A_valley_floor_lies_under_the_ground_and_is_fixed_on_the_map()
    {
        DaggerfallTerrainSet terrain = Terrain(8, 8);
        DaggerfallHorizonGround ground = Ground(terrain);

        for (int pixel = 1; pixel < 7; pixel++)
            Assert.True(ground.Floor(pixel * Size, pixel * Size) <= ground.Height(pixel * Size, pixel * Size));
        Assert.Equal(ground.Lifted(900, 50_000, 0, new(0, 0, 3, false)), ground.Lifted(900, 50_000, 0, new(0, 0, 3, true)));
    }

    [Fact]
    public void Far_ground_sinks_under_the_walked_window_and_stands_at_its_height_past_it()
    {
        DaggerfallHorizonGround ground = Ground(Terrain(8, 8));
        DaggerfallHorizonGround.Frame frame = new(0, 0, 3, false);
        DaggerfallHorizonTuning tuning = DaggerfallHorizonTuning.Default;
        double windowEdge = 3.5 * Size;

        Assert.Equal(tuning.SinkMetres, ground.Sink(0, 0, frame));
        Assert.Equal(tuning.SinkMetres, ground.Sink(windowEdge, 0, frame));
        Assert.Equal(0, ground.Sink(tuning.SinkGoneMetres, 0, frame));
        Assert.Equal(0, ground.Sink(0, -tuning.SinkGoneMetres, frame));
    }

    [Fact]
    public void A_lift_of_one_draws_true_height()
    {
        DaggerfallHorizonGround ground = Ground(Terrain(8, 8));

        Assert.Equal(400, ground.Lifted(400, 50_000, 0, new(0, 0, 1, false)));
    }

    private static readonly DaggerfallHorizonGround.GroundMaterials Materials = new(0, new Dictionary<int, DaggerfallHorizonGround.ArchiveMaterials>
    {
        [302] = new(1, 1, 2, 3, true, false), [303] = new(4, 4, 5, 6, false, false), [402] = new(7, 8, 9, 10, false, true),
    });

    [Fact]
    public void Open_ground_is_the_same_wherever_it_is_drawn_and_only_near_ground_has_a_shore()
    {
        DaggerfallHorizonGround ground = Ground(Terrain(8, 8));
        double sea = DaggerfallTerrainSurfaceBuilder.OceanElevation * DaggerfallTerrainSurfaceBuilder.TerrainScale;

        Assert.Equal(ground.PaintOpen(1000, 1000, 200, 0, false, Materials), ground.PaintNear(1000, 1000, 200, 0, false, Materials));
        DaggerfallHorizonGround.Paint seabed = ground.PaintNear(1000, 1000, sea - 3, 0, false, Materials);
        Assert.Equal((3u, 1f), (seabed.Material, seabed.Weights.Y));
        DaggerfallHorizonGround.Paint beach = ground.PaintNear(1000, 1000, sea + 2, 0, false, Materials);
        Assert.Equal((3u, 1f), (beach.Material, beach.Weights.Y));
    }

    [Fact]
    public void Winter_changes_a_climates_layers_and_real_height_brings_the_snow()
    {
        DaggerfallHorizonGround ground = Ground(Terrain(8, 8));

        Assert.Equal(1u, ground.PaintOpen(1000, 1000, 200, 0, false, Materials).Material);
        Assert.Equal(4u, ground.PaintOpen(1000, 1000, 200, 0, true, Materials).Material);
        Assert.Equal(1f, ground.PaintOpen(1000, 1000, DaggerfallGroundTuning.Default.SnowFullMetres, 0, false, Materials).Weights.W);
    }

    [Theory]
    [InlineData(200f, 8u)]
    [InlineData(5_000f, 7u)]
    public void A_swamp_wears_its_lush_ground_below_the_snow(float height, uint material)
    {
        DaggerfallHorizonGround ground = new(Terrain(8, 8), (_, _) => 227, DaggerfallHorizonTuning.Default, DaggerfallGroundTuning.Default, NoLocations);

        Assert.Equal(material, ground.PaintOpen(1000, 1000, height, 0, false, Materials).Material);
    }

    [Fact]
    public void Tile_corners_stand_where_the_terrain_lookup_puts_them()
    {
        // Record 5 is dirt in one corner of water. Unturned that is the tile's far left; the lookup's quarter turn
        // brings it to near left and its half turn to near right.
        Assert.Equal(DaggerfallGroundKinds.Dirt, DaggerfallGroundKinds.At(5, 0, 1));
        Assert.Equal(DaggerfallGroundKinds.Water, DaggerfallGroundKinds.At(5, 0, 0));
        Assert.Equal(DaggerfallGroundKinds.Dirt, DaggerfallGroundKinds.At(5 | 0x40, 0, 0));
        Assert.Equal(DaggerfallGroundKinds.Dirt, DaggerfallGroundKinds.At(5 | 0x80, 1, 0));
        Assert.Equal(DaggerfallGroundKinds.Dirt, DaggerfallGroundKinds.At(5 | 0xC0, 1, 1));
        // A paving edge is paving but for one corner.
        Assert.Equal(DaggerfallGroundKinds.Dirt, DaggerfallGroundKinds.At(47, 0, 1));
        Assert.Equal(DaggerfallGroundKinds.Paving, DaggerfallGroundKinds.At(47, 1, 0));
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
    public void Far_and_near_ground_stand_on_the_same_flattened_location()
    {
        DaggerfallTerrainSet terrain = Terrain(8, 8);
        DaggerfallSiteExterior location = new(3, 3, 1, 1, 0, 0, false, 0, 32, 96, 32, 96);
        DaggerfallHorizonGround ground = new(terrain, (_, _) => 231, DaggerfallHorizonTuning.Default, DaggerfallGroundTuning.Default,
            new Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> { [new(3, 3)] = location }) { Materials = Materials };
        // Far from the player and with no lift, far ground draws its ground as it stands.
        ground.Current = new(-1_000_000, -1_000_000, 1, false);
        const double step = DaggerfallTerrainSurfaceBuilder.SampleSpacing * 8;
        const double x = 3 * Size, z = (3 * Size) - (step * 16);
        HeightfieldTileSamples far = Samples(new(16, 0, 0, 0, 0, x, z, step));
        HeightfieldTileSamples near = Samples(new(16, 0, 0, 0, 0, x, z, step));
        ground.Sample(far.Tile, far);
        ground.SampleNear(near.Tile, near, 0, 0);

        Assert.Equal(near.Heights, far.Heights);
        DaggerfallTerrainSurface flattened = DaggerfallTerrainSurfaceBuilder.ApplyLocationFlattening(
            DaggerfallTerrainSurfaceBuilder.Build(terrain, 3, 3), DaggerfallLocationTerrainFrame.Flattening(location));
        Assert.NotEqual(ground.Height(x + (step * 8), z + (step * 8)), far.Heights[(9 * far.Tile.HeightSide) + 9], 1);
        Assert.Equal(flattened.Vertices[64 + (64 * 129)].Y, far.Heights[(9 * far.Tile.HeightSide) + 9], 1);
    }

    [Fact]
    public void Collision_stands_on_the_heights_near_ground_draws_without_its_materials()
    {
        DaggerfallTerrainSet terrain = Terrain(8, 8);
        DaggerfallSiteExterior location = new(3, 3, 1, 1, 0, 0, false, 0, 32, 96, 32, 96);
        Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> locations = new() { [new(3, 3)] = location };
        DaggerfallHorizonGround drawn = new(terrain, (_, _) => 231, DaggerfallHorizonTuning.Default, DaggerfallGroundTuning.Default, locations) { Materials = Materials };
        // The collider samples before anything has bound the ground's materials.
        DaggerfallHorizonGround collided = new(terrain, (_, _) => 231, DaggerfallHorizonTuning.Default, DaggerfallGroundTuning.Default, locations);
        const double cell = DaggerfallHorizonGround.FinestNearCellMetres, x = 3 * Size, z = (3 * Size) - 32;
        HeightfieldTileSamples near = Samples(new(16, 0, 0, 0, 0, x, z, cell));
        HeightfieldTileSamples collision = Samples(new(16, 0, 0, 0, 0, x, z, cell));

        drawn.SampleNear(near.Tile, near, 0, 0);
        collided.SampleHeights(collision.Tile, collision, 0, 0);

        Assert.Equal(near.Heights, collision.Heights);
    }

    [Fact]
    public void The_seabed_falls_away_from_the_shore_and_levels_off_without_a_step_at_a_map_pixel_edge()
    {
        DaggerfallHorizonGround ground = Ground(Coast());
        const double cell = DaggerfallHorizonGround.FinestNearCellMetres;
        double sea = DaggerfallHorizonGround.OceanMetres;
        // Westward from the shore, and on across the map pixel edge at 2 pixels.
        List<double> bed = [];
        for (double tileX = 3 * Size; tileX > (1 * Size) + 200; tileX -= 16 * cell)
        {
            HeightfieldTileSamples samples = Samples(new(16, 0, 0, 0, 0, tileX - (16 * cell), 4 * Size, cell));
            ground.SampleHeights(samples.Tile, samples, 0, 0);
            int middle = 9 * samples.Tile.HeightSide;
            for (int i = 16; i >= 1; i--) bed.Add(samples.Heights[middle + i]);
        }

        Assert.All(bed, height => Assert.True(height < sea + 3, $"{height} stands proud of the water."));
        Assert.True(bed.Min() < sea - 5, "The sea has depth.");
        Assert.True(bed.Zip(bed.Skip(1)).All(pair => Math.Abs(pair.First - pair.Second) < 1), "The seabed has no steps.");
        Assert.True(ground.HoldsSea(2, 4));
    }

    [Fact]
    public void Dry_land_holds_no_sea_and_the_outer_ring_wears_the_far_sea_past_the_sheets()
    {
        DaggerfallHorizonGround ground = new(Coast(), (_, _) => 231, DaggerfallHorizonTuning.Default, DaggerfallGroundTuning.Default, NoLocations) { Materials = Materials };
        HeightfieldTileSamples land = Samples(new(16, 0, 0, 0, 0, 6 * Size, 4 * Size, DaggerfallHorizonGround.FinestNearCellMetres));
        ground.SampleNear(land.Tile, land, 0, 0);
        Assert.False(ground.HoldsSea(6, 4));

        HeightfieldTileSamples outer = Samples(new(16, 0, 0, 0, 0, 1 * Size, 4 * Size, DaggerfallTerrainSurfaceBuilder.SampleSpacing * 4));
        ground.SampleNear(outer.Tile, outer, 0, 0);
        Assert.Contains(Materials.Sea, outer.Materials.ToArray());
    }

    /// <summary>Samples for a tile: the Engine makes them, and a fixture has no Engine to ask.</summary>
    private static HeightfieldTileSamples Samples(HeightfieldTileRequest tile) =>
        (HeightfieldTileSamples)Activator.CreateInstance(typeof(HeightfieldTileSamples), BindingFlags.NonPublic | BindingFlags.Instance, null, [tile], null)!;

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
        new(terrain, (_, _) => 231, DaggerfallHorizonTuning.Default, DaggerfallGroundTuning.Default, NoLocations);

    private static readonly Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> NoLocations = [];

    /// <summary>The sea west of map pixel 3, and land rising east of it.</summary>
    private static DaggerfallTerrainSet Coast()
    {
        const int width = 8, height = 8;
        byte[] heights = new byte[width * height], samples = new byte[width * height * 25];
        for (int cell = 0; cell < heights.Length; cell++)
            heights[cell] = cell % width < 3 ? (byte)0 : (byte)(30 + (cell % width * 10));
        return new DaggerfallTerrainSet(width, height, heights, () => samples);
    }

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
