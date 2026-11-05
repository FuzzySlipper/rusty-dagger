using System.Buffers.Binary;
using Daggerfall.Import.Normalization;
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
    public void The_published_form_is_a_header_then_heights_then_shares()
    {
        DaggerfallRelief relief = new(2, 1, [1.5F, 2.5F], [0, 255]);

        byte[] bytes = relief.Encode();

        Assert.Equal("RLF1"u8.ToArray(), bytes[..4]);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(2.5F, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(16)));
        Assert.Equal(255, bytes[^1]);
        Assert.Equal(12 + 10, bytes.Length);
    }

    [Fact]
    public void A_classic_height_is_the_donors_scales_within_its_clamps()
    {
        Assert.Equal(ReliefBake.OceanMetres, ReliefBake.ClassicMetres(0, 0), 6);
        Assert.Equal(((100 * 8) + (10 * 4)) * 1.5, ReliefBake.ClassicMetres(100, 10), 6);
        Assert.Equal(1539 * 1.5, ReliefBake.ClassicMetres(255, 255), 6);
    }

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
