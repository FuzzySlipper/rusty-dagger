using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class SwingSweepTests
{
    private static readonly Vector3 Eye = new(0f, 1f, 0f);
    private static readonly Vector3 East = Vector3.UnitX;

    [Fact]
    public void An_arc_turns_right_of_the_view_and_its_tilt_turns_the_plane_clockwise()
    {
        Vector3 north = -Vector3.UnitZ;

        AssertNear(Vector3.UnitX, new SwingArc(90f, 90f, 0f, 1f).Direction(0f, north));
        AssertNear(-Vector3.UnitX, new SwingArc(90f, -90f, 0f, 1f).Direction(1f, north));
        AssertNear(-Vector3.UnitY, new SwingArc(90f, 90f, 90f, 1f).Direction(0f, north));
        AssertNear(north, new SwingArc(-40f, 40f, 30f, 1f).Direction(.5f, north));
    }

    [Fact]
    public void An_arc_stays_in_view_space_as_the_view_turns()
    {
        AssertNear(Vector3.Normalize(new Vector3(1f, 0f, 1f)), new SwingArc(45f, 45f, 0f, 1f).Direction(0f, East));
        AssertNear(Vector3.UnitZ, new SwingArc(90f, 90f, 0f, 1f).Direction(0f, East));
    }

    [Theory]
    [InlineData(-40f, 40f, 2L)]
    [InlineData(40f, -40f, 3L)]
    public void A_cut_across_two_adjacent_bodies_meets_the_first_in_its_path(float start, float end, long first)
    {
        using ActorContactTests.Scene scene = Adjacent();
        SwingSweep sweep = new(scene.Contact, 1, new SwingArc(start, end, 0f, 3f));

        Assert.Equal(first, sweep.Advance(Eye, East, 1f));
    }

    [Fact]
    public void A_cut_goes_through_a_body_it_passes_and_meets_the_next()
    {
        using ActorContactTests.Scene scene = Adjacent();
        SwingSweep sweep = new(scene.Contact, 1, new SwingArc(-40f, 40f, 0f, 3f), passes: actor => actor == 2);

        Assert.Equal(3L, sweep.Advance(Eye, East, 1f));
    }

    [Fact]
    public void A_cut_that_passes_beside_a_body_misses_it()
    {
        using ActorContactTests.Scene scene = new();
        SwingSweep sweep = new(scene.Contact, 1, new SwingArc(60f, 25f, 0f, 3f));

        Assert.Null(sweep.Advance(Eye, East, 1f));
        Assert.All(sweep.Rays, ray => Assert.Equal(ContactKind.None, ray.Kind));
    }

    [Fact]
    public void A_body_past_the_arcs_range_is_out_of_reach()
    {
        using ActorContactTests.Scene scene = new();

        Assert.Null(new SwingSweep(scene.Contact, 1, new SwingArc(-30f, 30f, 0f, 1.5f)).Advance(Eye, East, 1f));
    }

    [Fact]
    public void A_sweep_covers_the_arc_a_tick_at_a_time_and_never_returns_a_body_twice()
    {
        using ActorContactTests.Scene scene = new();
        SwingSweep sweep = new(scene.Contact, 1, new SwingArc(-40f, 40f, 0f, 3f));

        Assert.Null(sweep.Advance(Eye, East, .25f));
        Assert.Equal(2L, sweep.Advance(Eye, East, .75f));
        Assert.Null(sweep.Advance(Eye, East, 1f));
    }

    [Fact]
    public void A_wide_arc_in_one_tick_leaves_no_gap_a_body_could_pass_through()
    {
        using ActorContactTests.Scene scene = new();
        SwingSweep sweep = new(scene.Contact, 1, new SwingArc(-90f, 90f, 0f, 3f));

        sweep.Advance(Eye, East, 1f);

        Assert.True(sweep.Rays.Count >= 15, $"{sweep.Rays.Count} rays cross 90 degrees");
    }

    [Fact]
    public void The_world_stops_a_ray_but_not_the_swing()
    {
        using ActorContactTests.Scene scene = new() { WallAt = 1f };
        SwingSweep sweep = new(scene.Contact, 1, new SwingArc(-20f, 20f, 0f, 3f));

        Assert.Null(sweep.Advance(Eye, East, 1f));
        Assert.All(sweep.Rays, ray => Assert.Equal(ContactKind.World, ray.Kind));
    }

    [Fact]
    public void A_swept_arc_with_no_width_still_casts_its_one_ray()
    {
        using ActorContactTests.Scene scene = new();

        Assert.Equal(2L, new SwingSweep(scene.Contact, 1, new SwingArc(0f, 0f, 0f, 3f)).Advance(Eye, East, .1f));
    }

    // Actor 2 straight ahead to the east and actor 3 just to its right, so which comes first depends on the cut's direction.
    private static ActorContactTests.Scene Adjacent()
    {
        ActorContactTests.Scene scene = new();
        scene.Actors.Get(3).ApplyPose(new ActorPose(new WorldPoint(2f, 0f, 1f), 0f));
        return scene;
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");
}
