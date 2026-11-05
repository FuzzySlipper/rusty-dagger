using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class ErosionFilterTests
{
    private static readonly ErosionFilter Filter = new(7, new(3_200, 6, .09, .9, 280, 700, 1_500, 3_000));

    [Fact]
    public void Flat_ground_stays_flat_and_low_ground_keeps_its_crest()
    {
        Assert.Equal(0, Filter.Offset(1_000, 2_000, 0, 0, 12.8));
        Assert.Equal(0, Filter.Crest(1_000, 2_000, 600));
    }

    [Fact]
    public void The_same_point_cuts_the_same_gully()
    {
        Assert.Equal(Filter.Offset(1_234, 5_678, .3, -.2, 12.8), Filter.Offset(1_234, 5_678, .3, -.2, 12.8));
    }

    [Fact]
    public void A_steeper_slope_cuts_deeper_and_a_coarse_sampling_leaves_out_the_fine_gullies()
    {
        double[] xs = [.. Enumerable.Range(0, 400).Select(i => i * 37.0)];
        double Range(double slope, double finest)
        {
            double[] offsets = [.. xs.Select(x => Filter.Offset(x, x * .7, slope, 0, finest))];
            return offsets.Max() - offsets.Min();
        }

        Assert.True(Range(.4, 12.8) > Range(.1, 12.8));
        Assert.Equal(Filter.Offset(900, 400, .3, 0, 3_000), Filter.Offset(900, 400, .3, 0, 3_199));
        Assert.NotEqual(Filter.Offset(900, 400, .3, 0, 3_000), Filter.Offset(900, 400, .3, 0, 12.8));
    }

    [Fact]
    public void High_ground_breaks_into_peaks_and_cols()
    {
        double[] crests = [.. Enumerable.Range(0, 200).Select(i => Filter.Crest(i * 311.0, i * 173.0, 2_000))];

        Assert.Contains(crests, value => value > 0);
        Assert.Contains(crests, value => value < 0);
    }
}
