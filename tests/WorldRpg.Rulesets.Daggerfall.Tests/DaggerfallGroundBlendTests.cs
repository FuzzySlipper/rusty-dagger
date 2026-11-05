using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallGroundBlendTests
{
    [Theory]
    [InlineData(.1F, .1F, 300F)]
    [InlineData(.5F, .4F, 900F)]
    [InlineData(.7F, .7F, 1_800F)]
    [InlineData(.95F, 1.5F, 2_500F)]
    public void A_vertexs_layers_always_add_up_to_the_whole_ground(float noise, float slope, float height)
    {
        Vector4 weights = DaggerfallGroundBlend.Weights(noise, slope, height);

        Assert.Equal(1, weights.X + weights.Y + weights.Z + weights.W, 4);
        Assert.All(new[] { weights.X, weights.Y, weights.Z, weights.W }, weight => Assert.InRange(weight, 0, 1));
    }

    [Fact]
    public void The_noise_lays_out_grass_and_dirt_on_gentle_low_ground()
    {
        Assert.Equal(1, DaggerfallGroundBlend.Weights(.7F, .1F, 300).X, 4);
        Assert.Equal(1, DaggerfallGroundBlend.Weights(.2F, .1F, 300).Y, 4);
    }

    [Fact]
    public void Steep_ground_is_bare_rock_whatever_the_noise_says()
    {
        Assert.Equal(1, DaggerfallGroundBlend.Weights(.7F, 1.2F, 300).Z, 4);
    }

    [Fact]
    public void High_ground_is_snow_and_a_cliff_up_there_holds_less_of_it()
    {
        Assert.Equal(1, DaggerfallGroundBlend.Weights(.7F, .1F, 2_400).W, 4);
        Assert.True(DaggerfallGroundBlend.Weights(.7F, 1.2F, 2_400).Z > 0);
    }
}
