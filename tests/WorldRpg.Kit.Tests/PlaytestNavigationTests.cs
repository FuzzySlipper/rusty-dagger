using System.Numerics;
using Rusty.Engine;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>How the floor and route a tester asks for read the Engine's ray and path, without a session.</summary>
public sealed class PlaytestNavigationTests
{
    private const float Slope = MathF.PI / 4;

    [Fact]
    public void A_floor_is_an_upward_surface_below_and_says_whether_it_is_within_the_slope_limit()
    {
        PlaytestFloor level = PlaytestFloor.FromDownwardHit(Hit(new(1, 2.5f, 3), Vector3.UnitY), Slope, 16);
        Assert.Equal(PlaytestFloor.FloorStatus, level.Status);
        Assert.Equal(2.5, level.FloorY);
        Assert.True(level.Standable);

        PlaytestFloor steep = PlaytestFloor.FromDownwardHit(Hit(new(0, 1, 0), Vector3.Normalize(new(1, .5f, 0))), Slope, 16);
        Assert.Equal(PlaytestFloor.FloorStatus, steep.Status);
        Assert.False(steep.Standable);
    }

    [Fact]
    public void Nothing_below_a_ray_that_began_inside_collision_or_a_downward_face_is_no_floor()
    {
        Assert.Equal("Nothing within 16 m below.", AssertNoFloor(PlaytestFloor.FromDownwardHit(default, Slope, 16)).Detail);
        Assert.Equal("The search began inside collision.",
            AssertNoFloor(PlaytestFloor.FromDownwardHit(Hit(Vector3.Zero, Vector3.UnitY) with { StartSolid = true }, Slope, 16)).Detail);
        AssertNoFloor(PlaytestFloor.FromDownwardHit(Hit(Vector3.Zero, -Vector3.UnitY), Slope, 16));
    }

    [Fact]
    public void Not_loaded_has_no_height()
    {
        PlaytestFloor unknown = PlaytestFloor.NotLoaded("still arriving");
        Assert.Equal(PlaytestFloor.NotLoadedStatus, unknown.Status);
        Assert.Null(unknown.FloorY);
        Assert.Equal("still arriving", unknown.Detail);
    }

    [Fact]
    public void A_route_reads_as_its_start_goal_and_turns()
    {
        // East three cells, then north two, then a drop and on east.
        PlanarNavCell[] path = [new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(3, 0, 0), new(3, 0, 1), new(3, 0, 2), new(4, -1, 2), new(5, -1, 2)];
        NavigationPathEdge[] edges = [.. path.Select((_, index) => new NavigationPathEdge(index == 6 ? NavigationEdgeKind.Drop : NavigationEdgeKind.Walk))];
        IReadOnlyList<PlaytestRouteTurn> turns = PlaytestRouteTurn.Of(path, edges);
        Assert.Equal([0, 3, 5, 6, 7], turns.Select(turn => turn.Index));
        Assert.Equal(NavigationEdgeKind.Drop, turns.Single(turn => turn.Index == 6).Edge);
        Assert.All(turns.Where(turn => turn.Index != 6), turn => Assert.Equal(NavigationEdgeKind.Walk, turn.Edge));
    }

    [Fact]
    public void A_route_without_edges_turns_on_direction_alone_and_a_one_cell_path_is_its_start()
    {
        PlanarNavCell[] straight = [new(0, 0, 0), new(0, 0, 1), new(0, 0, 2)];
        Assert.Equal([0, 2], PlaytestRouteTurn.Of(straight, []).Select(turn => turn.Index));
        Assert.Single(PlaytestRouteTurn.Of([new PlanarNavCell(4, 0, 4)], []));
        Assert.Empty(PlaytestRouteTurn.Of([], []));
        Assert.Throws<ArgumentException>(() => PlaytestRouteTurn.Of(straight, [new NavigationPathEdge(NavigationEdgeKind.Walk)]));
    }

    [Fact]
    public void A_cell_centre_is_half_a_cell_in_on_both_axes_including_negative_cells()
    {
        Assert.Equal((1.25, -.75), PlaytestRouteTurn.Centre(new PlanarNavCell(2, 0, -2), .5));
    }

    private static PlaytestFloor AssertNoFloor(PlaytestFloor floor)
    {
        Assert.Equal(PlaytestFloor.NoFloorStatus, floor.Status);
        Assert.Null(floor.FloorY);
        return floor;
    }

    private static SpatialHit Hit(Vector3 point, Vector3 normal) =>
        new(true, SpatialHitKind.StaticMesh, 0, 0, 0, 0, 0, 0, 0, default, point, normal, 1, 0, 0, false, true);
}
