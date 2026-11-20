using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallOverlandViewTests
{
    private const double Cell = DaggerfallWorldGrid.HorizontalSize;

    [Fact]
    public void A_world_map_point_and_its_metres_agree_with_the_exterior_grounds_pixels()
    {
        // Map pixel (X, Y) spans x from X and z back from Y, as the exterior ground's do.
        (double x, double z) = DaggerfallOverlandView.Metres(207.5, 213.5);
        Assert.Equal((207.5 * Cell, 212.5 * Cell), (x, z));
        Assert.Equal(new DaggerfallTravelMapPixel(207, 213), DaggerfallOverlandView.Pixel(x, z));
        Assert.Equal(new DaggerfallTravelMapPixel(207, 213), DaggerfallOverlandView.Pixel(207 * Cell + 1, 212 * Cell + 1));
        Assert.Equal(new DaggerfallTravelMapPixel(0, 0), DaggerfallOverlandView.Pixel(-5_000, -5_000));
    }

    [Fact]
    public void The_view_opens_on_the_party_and_follows_it_until_panned_and_I_am_at_follows_again()
    {
        DaggerfallOverlandView view = new(DaggerfallOverlandTuning.Default);
        view.Open(100_000, 200_000);
        Assert.True(view.Following);
        view.Track(150_000, 250_000);
        Assert.Equal((150_000d, 250_000d), (view.TargetX, view.TargetZ));

        // Facing north, a pan right goes east and a pan forward north, by eye distances.
        view.Move(0, 0, 1, 0, 0);
        Assert.False(view.Following);
        Assert.Equal(150_000 + view.Distance, view.TargetX, 6);
        view.Move(0, 0, 0, 1, 0);
        Assert.Equal(250_000 - view.Distance, view.TargetZ, 6);
        view.Track(100_000, 100_000);
        Assert.NotEqual(100_000, view.TargetX);
        view.Follow();
        view.Track(100_000, 100_000);
        Assert.Equal((100_000d, 100_000d), (view.TargetX, view.TargetZ));

        // Turned to face east, forward is east.
        view.Move(90, 0, 0, 0, 0);
        double x = view.TargetX;
        view.Move(0, 0, 0, 1, 0);
        Assert.Equal(x + view.Distance, view.TargetX, 6);
    }

    [Fact]
    public void Turns_wrap_while_tilt_and_distance_keep_to_their_tuned_ranges()
    {
        DaggerfallOverlandTuning tuning = DaggerfallOverlandTuning.Default;
        DaggerfallOverlandView view = new(tuning);
        view.Move(-30, 0, 0, 0, 0);
        Assert.Equal(330, view.Heading, 6);
        view.Move(400, 90, 0, 0, 0);
        Assert.Equal(10, view.Heading, 6);
        Assert.Equal(tuning.Tilt.Most, view.Tilt);
        view.Move(0, -180, 0, 0, 0);
        Assert.Equal(tuning.Tilt.Least, view.Tilt);
        view.Move(0, 0, 0, 0, 1);
        Assert.Equal(tuning.Distance.Opening / DaggerfallOverlandView.ZoomStep, view.Distance, 6);
        view.Move(0, 0, 0, 0, 8); view.Move(0, 0, 0, 0, 8);
        Assert.Equal(tuning.Distance.Least, view.Distance);
        view.Move(0, 0, 0, 0, -8); view.Move(0, 0, 0, 0, -8); view.Move(0, 0, 0, 0, -8);
        Assert.Equal(tuning.Distance.Most, view.Distance);
        // A pan never takes the view off the map.
        view.Look(-1e9, 1e9);
        Assert.Equal(new DaggerfallTravelMapPixel(0, DaggerfallTravelMapPixel.Height - 1), DaggerfallOverlandView.Pixel(view.TargetX, view.TargetZ));
    }

    [Fact]
    public void The_eye_stands_back_from_the_point_against_the_heading_and_up_by_the_tilt()
    {
        DaggerfallOverlandView view = new(DaggerfallOverlandTuning.Default);
        view.Open(10_000, 10_000);
        Vector3 eye = view.Eye(100);
        double tilt = view.Tilt * Math.PI / 180;
        // Facing north, the eye stands south of the point.
        Assert.Equal(10_000, eye.X, 1);
        Assert.Equal(10_000 + (Math.Cos(tilt) * view.Distance), eye.Z, 1);
        Assert.Equal(100 + (Math.Sin(tilt) * view.Distance), eye.Y, 1);
        Assert.Equal(view.Distance, Vector3.Distance(eye, view.Target(100)), 1);
    }

    [Fact]
    public void The_map_tuning_refuses_ranges_out_of_order_and_a_tilt_past_straight_down()
    {
        DaggerfallOverlandTuning tuning = DaggerfallOverlandTuning.Default;
        Assert.Throws<InvalidOperationException>(() => (tuning with { Tilt = new(30, 95, 55) }).Validate());
        Assert.Throws<InvalidOperationException>(() => (tuning with { Distance = new(5_000, 1_000, 2_000) }).Validate());
        Assert.Throws<InvalidOperationException>(() => (tuning with { Relief = 0 }).Validate());
        Assert.Same(tuning, tuning.Validate());
    }
}
