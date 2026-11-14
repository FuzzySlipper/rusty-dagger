using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallMoonsTests
{
    private const int Full = 0, New = 16;

    /// <summary>The calendar counts its cycle from the full moon, and the art's fullest frame is its eighth.</summary>
    [Theory]
    [InlineData(Full, 8)]
    [InlineData(New, 24)]
    [InlineData(23, 31)]
    [InlineData(24, 0)]
    public void The_sky_draws_the_phase_the_calendar_counts(int cycleDay, int frame)
    {
        Assert.Equal(frame, DaggerfallMoons.Frame(cycleDay));
    }

    [Fact]
    public void A_moon_gives_all_its_light_full_half_at_the_quarter_and_none_new()
    {
        Assert.Equal(1, DaggerfallMoons.Phase(Full), 5);
        Assert.Equal(.5F, DaggerfallMoons.Phase(New / 2), 5);
        Assert.Equal(0, DaggerfallMoons.Phase(New), 5);
    }

    [Theory]
    [InlineData(1F)]
    [InlineData(.45F)]
    public void The_outdoor_light_rises_from_the_moonlit_night_without_a_step_and_reaches_the_same_noon(float share)
    {
        const float night = .1333F, noon = .7F;
        // The last moment of night and the first of a dawn with no daylight yet are the same light.
        Assert.Equal(night * share, DaggerfallSiteLighting.ExteriorAmbient(night, noon, share, 0), 5);
        Assert.Equal(noon, DaggerfallSiteLighting.ExteriorAmbient(night, noon, share, 1), 5);
        Assert.True(DaggerfallSiteLighting.ExteriorAmbient(night, noon, share, .01F) - DaggerfallSiteLighting.ExteriorAmbient(night, noon, share, 0) < .01F);
    }

    [Fact]
    public void A_full_moon_stands_high_at_midnight_and_under_the_ground_at_noon()
    {
        Assert.True(DaggerfallMoons.Direction((DaggerfallCalendar.Start with { Hour = 0, Minute = 0 }).Sky, Full, 0).Y > .99F);
        Assert.True(DaggerfallMoons.Direction((DaggerfallCalendar.Start with { Hour = 12, Minute = 0 }).Sky, Full, 0).Y < -.99F);
    }

    [Fact]
    public void A_new_moon_stands_beside_the_sun()
    {
        Assert.True(DaggerfallMoons.Direction((DaggerfallCalendar.Start with { Hour = 12, Minute = 0 }).Sky, New, 0).Y > .99F);
    }

    [Fact]
    public void A_moon_falls_behind_the_sun_as_it_waxes()
    {
        // Eight days after new it stands a quarter turn behind, so at noon it is where the sun was at dawn.
        Vector3 noon = DaggerfallMoons.Direction((DaggerfallCalendar.Start with { Hour = 12, Minute = 0 }).Sky, New + 8, 0);
        Vector3 dawn = DaggerfallMoons.Direction((DaggerfallCalendar.Start with { Hour = 6, Minute = 0 }).Sky, New, 0);

        Assert.Equal(dawn.X, noon.X, 3);
        Assert.Equal(dawn.Y, noon.Y, 3);
    }

    [Fact]
    public void A_tilted_path_leans_south_and_stays_a_unit_direction()
    {
        Vector3 way = DaggerfallMoons.Direction((DaggerfallCalendar.Start with { Hour = 0, Minute = 0 }).Sky, Full, 30);

        Assert.True(way.Z > 0);
        Assert.Equal(1, way.Length(), 4);
    }

    [Theory]
    [InlineData(0, 0F, 0F)]
    [InlineData(9, .125F, .25F)]
    [InlineData(31, .875F, .75F)]
    public void A_frame_is_its_cell_of_the_atlas(int frame, float left, float top)
    {
        (Vector2 min, Vector2 max) = new DaggerfallSkyMoon("moon", true, 8, 32).Frame(frame);

        Assert.Equal(new Vector2(left, top), min);
        Assert.Equal(new Vector2(left + .125F, top + .25F), max);
    }
}
