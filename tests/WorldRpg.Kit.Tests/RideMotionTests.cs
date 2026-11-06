using System.Numerics;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>A mount builds and carries its speed, turns at its own rate, and lets the view look only so far past it.</summary>
public sealed class RideMotionTests
{
    private const float Step = 1f / 60f;
    private const float Top = 10f;
    private static readonly RideTuning Tuning = new RideTuning(WalkShare: .2f, TrotShare: .6f, Acceleration: 3f, Coasting: 2.5f, Braking: 9f,
        ReverseSpeed: 1f, StandingTurnDegrees: 150f, FullTurnDegrees: 60f, LookRangeDegrees: 40f, UphillDrag: .5f, WaterShare: .5f).Validate();
    private static readonly RideIntent Forward = new(Vector2.UnitY, false, false, true, false);
    private static readonly RideIntent Gallop = Forward with { Urged = true };
    private static readonly RideIntent Idle = Forward with { Planar = Vector2.Zero };

    private readonly RideMotion ride = new();
    private readonly PlayerControlState player = new(new WorldPoint(0, 0, 0), 0, 0);

    private RideStep Ride(RideIntent intent, float seconds)
    {
        RideStep last = default;
        for (float elapsed = 0; elapsed < seconds; elapsed += Step) last = ride.Step(Tuning, Top, intent, player, Step);
        return last;
    }

    private static float Degrees(float radians) => radians * 180 / MathF.PI;

    [Fact]
    public void A_mount_builds_speed_to_a_trot_and_is_urged_to_its_top()
    {
        Assert.InRange(Ride(Forward, 1f).Speed, 2.9f, 3.1f);
        Assert.Equal(6f, Ride(Forward, 3f).Speed, .01f);

        RideStep galloping = Ride(Gallop, 3f);
        Assert.Equal(Top, galloping.Speed, .01f);
        Assert.True(galloping.Galloping);
    }

    [Fact]
    public void A_mount_reined_in_walks()
    {
        Assert.Equal(2f, Ride(Gallop with { ReinedIn = true }, 3f).Speed, .01f);
    }

    [Fact]
    public void A_tired_rider_keeps_it_to_a_trot()
    {
        RideStep tired = Ride(Gallop with { MayGallop = false }, 5f);
        Assert.Equal(6f, tired.Speed, .01f);
        Assert.False(tired.Galloping);
    }

    [Fact]
    public void A_released_mount_carries_its_speed_and_a_held_one_stops_short()
    {
        Ride(Gallop, 5f);
        Assert.InRange(Ride(Idle, 1f).Speed, 7.3f, 7.7f);

        Assert.True(Ride(Forward with { Planar = -Vector2.UnitY }, 1f).Speed <= 0f);
    }

    [Fact]
    public void A_mount_slows_gently_from_a_gallop_to_a_trot()
    {
        Ride(Gallop, 5f);
        Assert.InRange(Ride(Forward, 1f).Speed, 7.3f, 7.7f);
    }

    [Fact]
    public void A_standing_mount_backs_slowly()
    {
        RideStep backing = Ride(Forward with { Planar = -Vector2.UnitY }, 2f);
        Assert.Equal(-1f, backing.Speed, .01f);
        CharacterStepControls controls = backing.Apply(default);
        Assert.Equal(new Vector2(0, -1), controls.PlanarIntent);
        Assert.Equal(1f, controls.BackwardSpeed);
    }

    [Fact]
    public void The_heading_follows_the_view_at_a_rate_that_falls_with_speed()
    {
        Ride(Idle, Step);
        player.YawRadians = .5f;
        float standing = Ride(Idle, .1f).HeadingRadians;

        ride.Reset();
        player.YawRadians = 0;
        Ride(Gallop, 5f);
        player.YawRadians = .5f;
        float galloping = Ride(Gallop, .1f).HeadingRadians;

        Assert.InRange(Degrees(standing), 14f, 16.5f);
        Assert.InRange(Degrees(galloping), 5.5f, 7f);
    }

    [Fact]
    public void The_view_looks_only_so_far_past_the_heading()
    {
        Ride(Gallop, 5f);
        player.YawRadians = 2f;
        RideStep step = Ride(Gallop, Step);

        Assert.Equal(40f, Degrees(player.YawRadians - step.HeadingRadians), .01f);
        Assert.Equal(-40f, Degrees(ride.TurnFrom(player.YawRadians)), .01f);
    }

    [Fact]
    public void The_view_range_holds_across_the_wrap_of_a_turn()
    {
        player.YawRadians = MathF.PI - .1f;
        Ride(Idle, Step);
        player.YawRadians = -MathF.PI + .1f;
        Ride(Idle, Step);
        Assert.InRange(MathF.Abs(ride.TurnFrom(player.YawRadians)), 0f, .2f);
    }

    [Fact]
    public void Turning_keys_rein_the_mount_round_and_carry_the_view_with_it()
    {
        Ride(Idle, Step);
        player.YawRadians = .2f;
        Ride(Idle, Step);
        float past = ride.TurnFrom(player.YawRadians);

        RideStep turned = Ride(Idle with { Planar = Vector2.UnitX }, .5f);

        Assert.InRange(Degrees(turned.HeadingRadians), 70f, 80f);
        Assert.Equal(past, ride.TurnFrom(player.YawRadians), .001f);
    }

    [Fact]
    public void A_wall_stops_it_but_a_glancing_one_and_a_slope_do_not()
    {
        Ride(Gallop, 5f);
        ride.Moved(new Vector3(0, 0, -Top * Step * .1f), Step, true);
        Assert.Equal(1f, ride.Speed, .01f);

        Ride(Gallop, 5f);
        ride.Moved(new Vector3(Top * Step * .9f, 0, 0), Step, true);
        Assert.Equal(Top, ride.Speed, .01f);

        ride.Moved(new Vector3(Top * Step * .7f, Top * Step * .7f, 0), Step, true);
        Assert.Equal(Top, ride.Speed, .01f);
    }

    [Fact]
    public void A_mount_setting_out_is_not_taken_for_a_blocked_one()
    {
        Ride(Forward, Step);
        ride.Moved(Vector3.Zero, Step, true);
        Assert.InRange(Ride(Forward, 1f).Speed, 2.9f, 3.1f);
    }

    [Fact]
    public void In_the_air_nothing_stops_it()
    {
        Ride(Gallop, 5f);
        ride.Moved(Vector3.Zero, Step, false);
        Assert.Equal(Top, ride.Speed, .01f);
    }

    [Fact]
    public void Uphill_and_in_water_it_keeps_less_of_its_pace()
    {
        Ride(Gallop, 5f);
        ride.Moved(new Vector3(.1f, .1f, 0), Step, true);
        Assert.Equal(Top - 2.5f * Step, ride.Step(Tuning, Top, Gallop, player, Step).Speed, .01f);
        Assert.Equal(5f, Ride(Gallop, 3f).Speed, .01f);

        ride.Reset();
        Assert.Equal(5f, Ride(Gallop with { InWater = true }, 5f).Speed, .01f);
    }

    [Fact]
    public void Reset_stands_it_still_to_face_the_view()
    {
        Ride(Gallop, 5f);
        ride.Reset();
        player.YawRadians = 1f;

        Assert.Equal(0f, ride.Speed);
        Assert.Equal(0f, ride.TurnFrom(1f));
        Assert.Equal(1f, Ride(Idle, Step).HeadingRadians, .001f);
    }

    [Theory]
    [InlineData(.7f, .6f)]
    [InlineData(0f, .6f)]
    [InlineData(.2f, 1f)]
    public void Tuning_needs_its_gaits_in_order(float walk, float trot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (Tuning with { WalkShare = walk, TrotShare = trot }).Validate());
    }
}
