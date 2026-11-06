using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallGroundBlendTests
{
    private static readonly DaggerfallGroundTuning Tuning = DaggerfallGroundTuning.Default;

    [Theory]
    [InlineData(.1F, .1F, 300F)]
    [InlineData(.5F, .4F, 900F)]
    [InlineData(.7F, .7F, 1_800F)]
    [InlineData(.95F, 1.5F, 2_500F)]
    public void A_vertexs_layers_always_add_up_to_the_whole_ground(float noise, float slope, float height)
    {
        Vector4 weights = DaggerfallGroundBlend.Weights(Tuning, noise, slope, height);

        Assert.Equal(1, weights.X + weights.Y + weights.Z + weights.W, 4);
        Assert.All(new[] { weights.X, weights.Y, weights.Z, weights.W }, weight => Assert.InRange(weight, 0, 1));
    }

    [Fact]
    public void The_noise_lays_out_grass_and_dirt_on_gentle_low_ground()
    {
        Assert.Equal(1, DaggerfallGroundBlend.Weights(Tuning, .7F, .1F, 300).X, 4);
        Assert.Equal(1, DaggerfallGroundBlend.Weights(Tuning, .2F, .1F, 300).Y, 4);
    }

    [Fact]
    public void Steep_ground_is_bare_rock_whatever_the_noise_says()
    {
        Assert.Equal(1, DaggerfallGroundBlend.Weights(Tuning, .7F, 1.2F, 300).Z, 4);
    }

    [Fact]
    public void High_ground_is_snow_and_a_cliff_up_there_holds_less_of_it()
    {
        Assert.Equal(1, DaggerfallGroundBlend.Weights(Tuning, .7F, .1F, 2_400).W, 4);
        Assert.True(DaggerfallGroundBlend.Weights(Tuning, .7F, 1.2F, 2_400).Z > 0);
    }

    [Fact]
    public void Borrowed_rock_shows_only_where_the_ground_is_steep()
    {
        Assert.Equal(0, DaggerfallGroundBlend.Weights(Tuning, 1F, .1F, 300, noiseStone: false).Z, 4);
        Assert.Equal(1, DaggerfallGroundBlend.Weights(Tuning, 1F, 1.2F, 300, noiseStone: false).Z, 4);
    }

    [Theory]
    [InlineData(302, 303)]
    [InlineData(304, 303)]
    [InlineData(102, 103)]
    [InlineData(402, 403)]
    public void A_climates_snow_is_the_grass_of_its_winter_archive(int archive, int winter)
    {
        Assert.Equal((winter, 2), DaggerfallGroundLayers.Records(archive)[3]);
    }

    [Theory]
    [InlineData(103, 102)]
    [InlineData(303, 302)]
    [InlineData(402, 102)]
    [InlineData(403, 102)]
    [InlineData(404, 104)]
    public void Ground_without_rock_of_its_own_borrows_some(int archive, int stone)
    {
        Assert.Equal((stone, 3), DaggerfallGroundLayers.Records(archive)[2]);
    }

    [Theory]
    [InlineData(402)]
    [InlineData(403)]
    [InlineData(404)]
    public void A_swamp_keeps_its_own_third_ground_as_a_fifth_layer(int archive)
    {
        (int, int)[] records = DaggerfallGroundLayers.Records(archive);

        Assert.Equal((archive, 3), records[4]);
        Assert.Equal((archive, 0), records[^1]);
    }

    [Fact]
    public void The_noise_turns_a_swamps_soil_lush_where_it_would_bare_stone()
    {
        Assert.Equal(0, DaggerfallGroundBlend.Lush(Tuning, .5F), 4);
        Assert.Equal(1, DaggerfallGroundBlend.Lush(Tuning, 1F), 4);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void A_deserts_heights_go_to_stone_for_want_of_a_winter(int archive)
    {
        Assert.Equal((archive, 3), DaggerfallGroundLayers.Records(archive)[3]);
    }

    [Theory]
    [InlineData(6.4F, true)]
    [InlineData(12.8F, true)]
    [InlineData(7F, false)]
    [InlineData(0F, false)]
    public void A_ground_texture_runs_on_only_if_a_cell_holds_whole_repeats(float repeat, bool fills)
    {
        Assert.Equal(fills, DaggerfallGroundBlend.FillsCell(repeat));
    }
}
