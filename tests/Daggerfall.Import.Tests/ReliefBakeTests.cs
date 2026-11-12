using System.Buffers.Binary;
using Daggerfall.Import.Relief;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class ReliefBakeTests
{
    private const int Width = 48, Height = 32, Cols = Width * ReliefBake.NodesPerPixel;

    [Fact]
    public void A_bake_depends_on_its_input_alone()
    {
        Assert.Equal(Bake().Encode(), Bake().Encode());
    }

    [Fact]
    public void The_sea_stays_the_sea_and_gives_the_ground_back_to_the_classic_height()
    {
        DaggerfallRelief relief = Bake();

        Assert.Equal(0, relief.Share[0]);
        Assert.Equal(ReliefBake.OceanMetres, relief.Metres[0], 3);
        Assert.Equal(255, relief.Share[Node(Width / 2, Height / 2)]);
    }

    [Fact]
    public void Erosion_keeps_the_land_near_its_classic_heights_and_cuts_valleys_into_it()
    {
        ReliefInput input = Island([]);
        DaggerfallRelief relief = ReliefBake.Bake(input, rounds: 4, steps: 20);

        double peak = input.Classic.Max();
        Assert.InRange(relief.Metres.Max(), peak * .5, peak * 1.6);
        // A smooth cone has one summit; drained ground has ridges and valleys, so neighbours rise and fall around the slope.
        int bumps = 0;
        for (int i = Node(Width / 4, Height / 2); i < Node(Width / 2, Height / 2) - 1; i++)
            if ((relief.Metres[i] - relief.Metres[i - 1]) * (relief.Metres[i + 1] - relief.Metres[i]) < 0) bumps++;
        Assert.True(bumps > 0, "the bake left a smooth slope");
    }

    [Fact]
    public void A_location_gets_calmer_ground_than_the_land_around_it()
    {
        ReliefPlace place = new(Width / 2 + 7, Height / 2, 4);
        DaggerfallRelief wild = ReliefBake.Bake(Island([]), rounds: 2, steps: 15);
        DaggerfallRelief settled = ReliefBake.Bake(Island([place]), rounds: 2, steps: 15);

        Assert.True(Roughness(settled, place) < Roughness(wild, place));
    }

    [Fact]
    public void The_published_form_is_a_header_then_heights_in_eighths_then_shares()
    {
        DaggerfallRelief relief = new(2, 1, [1.5F, 2.5F], 2, 1, [0, 255]);

        byte[] bytes = relief.Encode();

        Assert.Equal("RLF2"u8.ToArray(), bytes[..4]);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)));
        Assert.Equal(20, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(22)));
        Assert.Equal(255, bytes[^1]);
        Assert.Equal(20 + 4 + 2, bytes.Length);
    }

    [Fact]
    public void A_classic_height_is_the_donors_scales_within_its_clamps()
    {
        Assert.Equal(ReliefBake.OceanMetres, ReliefBake.ClassicMetres(0, 0), 6);
        Assert.Equal(((100 * 8) + (10 * 4)) * 1.5, ReliefBake.ClassicMetres(100, 10), 6);
        Assert.Equal(1539 * 1.5, ReliefBake.ClassicMetres(255, 255), 6);
    }

    [Fact]
    public void A_finer_stage_refines_the_coarse_landscape_on_its_own_lattice()
    {
        ReliefStage[] stages = [new(ReliefBake.NodesPerPixel, 2, 10), new(ReliefBake.NodesPerPixel * 2, 1, 5)];

        DaggerfallRelief relief = ReliefBake.Bake(Island([]), stages);

        Assert.Equal(Width * ReliefBake.NodesPerPixel * 2, relief.Width);
        Assert.Equal(relief.Width * relief.Height, relief.Metres.Length);
        Assert.Equal(ReliefBake.OceanMetres, relief.Metres[0], 3);
        Assert.Equal(ReliefBake.Bake(Island([]), stages).Encode(), relief.Encode());
    }

    [Fact]
    public void A_river_falls_all_the_way_from_its_head_and_ends_on_the_sea_or_what_it_joins()
    {
        DaggerfallRivers rivers = Rivers(Island([]));

        Assert.NotEmpty(rivers.Courses);
        foreach (DaggerfallRiverStation[] course in rivers.Courses)
        {
            for (int s = 1; s < course.Length; s++) Assert.True(course[s].Surface < course[s - 1].Surface, "a river rose on its way down");
            DaggerfallRiverStation end = course[^1];
            bool sea = Math.Abs(end.Surface - ReliefBake.OceanMetres) < .01;
            bool joins = rivers.Courses.Any(other => other != course && other.Any(station => station.X == end.X && station.Z == end.Z && station.Surface == end.Surface));
            Assert.True(sea || joins, "a river stopped in the hills");
        }
    }

    [Fact]
    public void A_river_tapers_in_from_its_head_rather_than_starting_at_full_width()
    {
        DaggerfallRiverStation[] longest = Rivers(Island([])).Courses.MaxBy(course => course.Length)!;

        Assert.True(longest[0].HalfWidth < longest[^1].HalfWidth / 2, $"the head stands {longest[0].HalfWidth} m half wide, the mouth {longest[^1].HalfWidth} m");
    }

    [Fact]
    public void A_river_keeps_off_every_location_it_would_otherwise_run_through()
    {
        // Wall the island's middle off: a location levels its map pixel, and any channel through it with that.
        ReliefPlace place = new(Width / 2, Height / 2, 4);
        DaggerfallRivers rivers = Rivers(Island([place]));

        double size = 819.2, x0 = (place.MapPixelX - 1) * size, z0 = (place.MapPixelY - 1) * size;
        Assert.DoesNotContain(rivers.Courses.SelectMany(course => course),
            station => station.X > x0 && station.X < x0 + size && station.Z > z0 && station.Z < z0 + size);
    }

    [Fact]
    public void Rivers_publish_their_stations_course_by_course()
    {
        DaggerfallRivers rivers = new([[new(1, 2, 3, 4), new(5, 6, 7, 8)], [new(9, 10, 11, 12)]]);

        byte[] bytes = rivers.Encode();

        Assert.Equal("RVR1"u8.ToArray(), bytes[..4]);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)));
        Assert.Equal(8 + 8 + (3 * 16), bytes.Length);
        Assert.Equal(12F, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(bytes.Length - 4)));
    }

    private static DaggerfallRivers Rivers(ReliefInput input) => ReliefBake.Bake(input, rounds: 3, steps: 20, sourceSquareKilometres: 8).Rivers;

    private static DaggerfallRelief Bake() => ReliefBake.Bake(Island([new(Width / 2, Height / 2, 1)]), rounds: 2, steps: 10);

    private static int Node(int pixelX, int pixelY) => (pixelY * ReliefBake.NodesPerPixel * Cols) + (pixelX * ReliefBake.NodesPerPixel);

    private static double Roughness(DaggerfallRelief relief, ReliefPlace place)
    {
        double total = 0;
        int centre = Node(place.MapPixelX, place.MapPixelY);
        for (int y = -2; y <= 2; y++)
        for (int x = -2; x <= 2; x++)
            total += Math.Abs(relief.Metres[centre + (y * Cols) + x] - relief.Metres[centre + (y * Cols) + x + 1]);
        return total;
    }

    /// <summary>A cone of land in a square sea.</summary>
    private static ReliefInput Island(IReadOnlyList<ReliefPlace> places)
    {
        double[] classic = new double[Width * Height], rain = new double[Width * Height];
        Array.Fill(rain, 1);
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            double away = Math.Sqrt(Math.Pow(x - (Width / 2.0), 2) + Math.Pow(y - (Height / 2.0), 2));
            classic[(y * Width) + x] = Math.Max(ReliefBake.OceanMetres, 1_400 - (away * 110));
        }

        return new(Width, Height, classic, rain, places);
    }
}
