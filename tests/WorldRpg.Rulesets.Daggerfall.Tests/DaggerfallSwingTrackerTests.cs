using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The player swing gesture read from the committed look turns: a trail below the swing threshold
/// stays a straight attack, a full gesture classifies on the donor's fifteen-degree sector map and
/// is consumed, stale aiming turns fall out of the trailing window, and a turn through the ±π seam
/// counts as the short turn it is.
/// </summary>
public sealed class DaggerfallSwingTrackerTests
{
    [Fact]
    public void A_short_twitch_stays_a_straight_attack_until_the_trail_carries_a_full_gesture()
    {
        DaggerfallSwingTracker tracker = new(minimumGestureRadians: 0.35);
        tracker.Observe(0, 0f, 0f);

        tracker.Observe(.1d, .1f, 0f);
        Assert.Equal(DaggerfallSwingDirection.None, tracker.TryGesture());

        tracker.Observe(.1d, .2f, 0f);
        tracker.Observe(.1d, .3f, 0f);
        tracker.Observe(.1d, .4f, 0f);
        tracker.Observe(.1d, .5f, 0f);
        Assert.Equal(DaggerfallSwingDirection.StrikeRight, tracker.TryGesture());

        // The classified swing is consumed; a follow-up attack without new motion swings straight.
        Assert.Equal(DaggerfallSwingDirection.None, tracker.TryGesture());
    }

    [Theory]
    [InlineData(0.4, 0.0, "StrikeRight")]
    [InlineData(0.0, 0.4, "StrikeUp")]
    [InlineData(-0.4, 0.0, "StrikeLeft")]
    [InlineData(-0.3, -0.3, "StrikeDownLeft")]
    [InlineData(0.0, -0.4, "StrikeDown")]
    [InlineData(0.3, -0.3, "StrikeDownRight")]
    public void The_gesture_sector_follows_the_donors_fifteen_degree_map(double yaw, double pitch, string expected)
    {
        DaggerfallSwingTracker tracker = new(minimumGestureRadians: 0.2);
        tracker.Observe(0, 0f, 0f);
        tracker.Observe(.1d, (float)yaw, (float)pitch);

        Assert.Equal(Enum.Parse<DaggerfallSwingDirection>(expected), tracker.TryGesture());
    }

    [Theory]
    [InlineData(14d, "StrikeRight")]     // a full sector to the right of up
    [InlineData(16d, "StrikeUp")]        // one fifteenth past the sector line
    [InlineData(344d, "StrikeDownRight")]
    [InlineData(346d, "StrikeRight")]    // the seam back to straight right
    public void Sector_boundaries_split_at_exact_fifteenths_of_a_degree(double degrees, string expected)
    {
        DaggerfallSwingTracker tracker = new(minimumGestureRadians: 0.2);
        tracker.Observe(0, 0f, 0f);
        tracker.Observe(.1d, (float)(0.4 * Math.Cos(degrees * Math.PI / 180d)), (float)(0.4 * Math.Sin(degrees * Math.PI / 180d)));

        Assert.Equal(Enum.Parse<DaggerfallSwingDirection>(expected), tracker.TryGesture());
    }

    [Fact]
    public void Turns_held_longer_than_the_gesture_window_are_aiming_not_swing()
    {
        DaggerfallSwingTracker tracker = new(minimumGestureRadians: 0.2, gestureWindowSeconds: 1d);
        tracker.Observe(0, 0f, 0f);
        tracker.Observe(.1d, .4f, 0f);

        // Standing still for longer than the window ages the gesture out of the trail.
        for (int i = 0; i < 11; i++) tracker.Observe(.1d, .4f, 0f);
        Assert.Equal(DaggerfallSwingDirection.None, tracker.TryGesture());

        // The next swing from the held aim still classifies on its own motion.
        tracker.Observe(.1d, .8f, 0f);
        Assert.Equal(DaggerfallSwingDirection.StrikeRight, tracker.TryGesture());
    }

    [Fact]
    public void A_look_turn_through_the_pole_seam_counts_as_the_short_turn()
    {
        DaggerfallSwingTracker tracker = new(minimumGestureRadians: 0.2);
        tracker.Observe(0, 3.0f, 0f);
        tracker.Observe(.1d, -3.0f, 0f); // a 0.28 radian turn east through ±π, not a 6 radian turn west

        Assert.Equal(DaggerfallSwingDirection.StrikeRight, tracker.TryGesture());
    }
}
