using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Interaction;

namespace WorldRpg.Kit;

/// <summary>
/// Read-only scene for the Engine's standard interaction debug module over an inspection the ruleset builds afresh at
/// each read, so it follows what the player can reach now.
/// </summary>
public sealed class CurrentInteractionInspectionScene(Func<WorldInteraction> current) : IWorldInteractionScene
{
    public InteractionSceneSnapshot ReadInteraction() => current().Inspect().Scene;
    public InteractionActionResult UseInteraction(InteractionTarget target) =>
        throw new InvalidOperationException("An inspection scene cannot activate a target.");
}

/// <summary>Loaded target meaning stays in the ruleset, which reads its targets at each call.</summary>
public sealed class PlaytestTargetsDebugModule(Func<DebugCommandResult> targets) : IDebugCommandModule
{
    [DebugCommand("navigation.targets", Description = "Read named currently loaded gameplay targets; no movement or route guarantee.")]
    public DebugCommandResult Targets() => targets();
}

/// <summary>
/// The floor and route a tester asks for (navigation.floor, navigation.route), answered by the ruleset's spatial
/// inspection from the collision movement meets and the navigation its actors walk. Each
/// <see cref="DebugCommandAttribute"/> here is the command: the Engine's catalogue is generated from it and calls the
/// ruleset's owner that implements this. Neither moves the player or advances time.
/// </summary>
public interface IPlaytestNavigationCommands : IDebugCommandModule
{
    [DebugCommand("navigation.floor", Description = "Read the floor at or below scene-local X Y Z (searching from a step above Y): floor with its height, no-floor, or not-loaded where that collision has not streamed in yet. Never a guess.")]
    DebugCommandResult ReadFloor(double x, double y, double z);

    [DebugCommand("navigation.route", Description = "Read the Engine's navigation route from the player's feet to scene-local feet X Y Z over the navigation actors walk: outcome, turning waypoints with their floors, and the floor at the goal. Nothing moves.")]
    DebugCommandResult ReadRoute(double x, double y, double z);
}

/// <summary>A point in a playtest reply, in scene-local metres.</summary>
public readonly record struct PlaytestPoint(double X, double Y, double Z)
{
    public static PlaytestPoint From(Vector3 value) => new(value.X, value.Y, value.Z);
}

/// <summary>
/// What is under a point: <c>floor</c> with its height, <c>no-floor</c> where the loaded collision has nothing to stand
/// on within the search, or <c>not-loaded</c> where that collision is not in the scene yet, which says nothing about
/// whether a floor is there.
/// </summary>
/// <param name="Standable">Whether the surface is within the controller's slope limit; a support fact, not a landing.</param>
/// <param name="Detail">Why there is no floor, or why it is not known.</param>
public sealed record PlaytestFloor(string Status, double? FloorY, PlaytestPoint? Normal, bool? Standable, string? Detail)
{
    public const string FloorStatus = "floor", NoFloorStatus = "no-floor", NotLoadedStatus = "not-loaded";

    /// <summary>A floor not known because its collision is not loaded: nothing is cast, so nothing is guessed.</summary>
    public static PlaytestFloor NotLoaded(string reason) => new(NotLoadedStatus, null, null, null, reason);

    /// <summary>Reads a downward ray through loaded collision, judging its surface by the controller's slope limit.</summary>
    public static PlaytestFloor FromDownwardHit(SpatialHit hit, float maximumSlopeRadians, double searchMetres)
    {
        if (!hit.Present)
            return new(NoFloorStatus, null, null, null, FormattableString.Invariant($"Nothing within {searchMetres:0.##} m below."));
        if (hit.StartSolid)
            return new(NoFloorStatus, null, null, null, "The search began inside collision.");
        if (hit.Normal.Y <= 0)
            return new(NoFloorStatus, null, PlaytestPoint.From(hit.Normal), null, "The first surface below faces down or sideways.");
        return new(FloorStatus, hit.Point.Y, PlaytestPoint.From(hit.Normal),
            MathF.Acos(Math.Clamp(hit.Normal.Y, -1F, 1F)) <= maximumSlopeRadians, null);
    }
}

/// <summary>One turn of a navigation route: a path cell where the direction or the kind of edge into it changes.</summary>
/// <param name="Index">The cell's place in the Engine's path, from 0 at the start.</param>
/// <param name="Edge">How the cell is reached from the one before: walk, drop or jump (walk for the start).</param>
public readonly record struct PlaytestRouteTurn(int Index, PlanarNavCell Cell, NavigationEdgeKind Edge)
{
    /// <summary>
    /// The path's start, its goal and every cell where the step's direction or the edge kind changes, so a route of
    /// hundreds of cells reads as the few legs a tester walks. <paramref name="edges"/> is the path's own: as long as
    /// it, or empty when the Engine gave none.
    /// </summary>
    public static IReadOnlyList<PlaytestRouteTurn> Of(ReadOnlySpan<PlanarNavCell> path, ReadOnlySpan<NavigationPathEdge> edges)
    {
        if (!edges.IsEmpty && edges.Length != path.Length)
            throw new ArgumentException("A path's edges are as many as its cells.", nameof(edges));
        List<PlaytestRouteTurn> turns = [];
        for (int index = 0; index < path.Length; index++)
        {
            NavigationEdgeKind edge = index == 0 || edges.IsEmpty ? NavigationEdgeKind.Walk : edges[index].Kind;
            bool keep = index == 0 || index == path.Length - 1 || edge != NavigationEdgeKind.Walk
                || Direction(path[index - 1], path[index]) != Direction(path[index], path[index + 1])
                || (!edges.IsEmpty && edges[index + 1].Kind != NavigationEdgeKind.Walk);
            if (keep) turns.Add(new(index, path[index], edge));
        }
        return turns;
    }

    /// <summary>The X/Z centre of a path cell on a grid of <paramref name="cellSize"/>; its level is only a height band.</summary>
    public static (double X, double Z) Centre(PlanarNavCell cell, double cellSize) => ((cell.X + .5) * cellSize, (cell.Z + .5) * cellSize);

    private static (long X, long Y, long Z) Direction(PlanarNavCell from, PlanarNavCell to) =>
        (Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y), Math.Sign(to.Z - from.Z));
}
