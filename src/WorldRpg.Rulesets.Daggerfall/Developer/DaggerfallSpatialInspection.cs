using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Interaction;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Read-only inspection of the interaction and spatial owners the player moves through (interaction.*, spatial.grid,
/// spatial.probe, spatial.clearance, playtest.jump-plan, navigation.floor, navigation.route). A probe or clearance answer
/// is also drawn as the overlays' probe set. A floor is read only where the exterior owner says its collision is in the
/// scene, and a route is the Engine's navigation step query, the one walkers and pursuers make. Nothing here moves the
/// player or advances time.
/// </summary>
/// <param name="engine">The Engine, whose spatial service the snapshots read.</param>
/// <param name="generation">The latest admitted update's generation, or null before one.</param>
/// <param name="step">The latest admitted simulation step, or null before one.</param>
internal sealed class DaggerfallSpatialInspection(IEngineContext engine, SpatialMovementSystem spatial, DaggerfallSiteCrossing sites,
    PlayerControlState player, PlayerInputSystem input, ActorsState actors, DaggerfallActivation activation, DaggerfallPlayerControls controls,
    Func<ulong?> generation, Func<ulong?> step, DaggerfallDebugOverlays overlays, DaggerfallWildNavigation wild) : IPlaytestNavigationCommands
{
    /// <summary>How far below its start a floor is searched for.</summary>
    private const float FloorSearchMetres = 16F;
    /// <summary>How far a route's first step reaches.</summary>
    private const float RouteStepMetres = 2F;
    /// <summary>The cells a route may visit: a long town crossing, where actors' own steps are far shorter.</summary>
    private const uint RouteVisitedCells = 262_144;

    internal WorldInteraction CreateInteractionInspection() =>
        (activation.Module ?? throw new InvalidOperationException("The activation owner is unavailable."))
            .CreateInspection(actors.Player.Actor.Entity, player, input.ResolveCurrentLook(player));

    internal DebugCommandResult ReadGrid(int radius, int verticalRadius, double cellSize)
    {
        if (radius is < 0 or > 15 || verticalRadius is < 0 or > 15 || !double.IsFinite(cellSize) || cellSize <= 0)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Grid radii must be 0–15 and cell size positive and finite.");
        Vector3 center = (player.Position ?? throw new InvalidOperationException("Spatial inspection requires a player position.")).ToVector();
        Vector3 origin = center - new Vector3((float)((radius + .5) * cellSize),
            (float)((verticalRadius + .5) * cellSize), (float)((radius + .5) * cellSize));
        uint width = (uint)(radius * 2 + 1), layers = (uint)(verticalRadius * 2 + 1);
        return DebugCommandResult.Success(SpatialGridSnapshot.Capture(engine.Spatial,
            new SpatialMapRequest(spatial.Session, origin, cellSize, width, width,
                origin.Y, origin.Y + cellSize, origin.Y, origin.Y + cellSize, ReadOnlyMemory<SpatialEntityCollider>.Empty),
            layers, new SpatialMapObservation($"{generation()}:{step()}", center,
                input.ResolveCurrentLook(player).Forward)));
    }

    internal DebugCommandResult ReadProbe(double distance)
    {
        if (!double.IsFinite(distance) || distance is <= 0 or > 8)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Probe distance must be finite and within (0, 8].");
        var config = spatial.CurrentController;
        float height = player.Motion.Stance == CharacterStance.Crouched ? config.Shape.CrouchedHeight : config.Shape.StandingHeight;
        string probe = PlaytestTraversal.Probe(engine.Spatial, spatial.Session,
            PlayerFeet(), height, config.Surface.MaximumStepHeight, (float)distance, SpatialMovementSystem.SpatialColliders(LiveCollision()));
        overlays.PublishProbe(probe);
        return DebugCommandResult.Success(probe);
    }

    internal DebugCommandResult ReadClearance(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Clearance target must be finite.");
        var config = spatial.CurrentController;
        Vector3 center = (player.Position ?? throw new InvalidOperationException("Spatial inspection requires a player position.")).ToVector();
        float height = player.Motion.Stance == CharacterStance.Crouched ? config.Shape.CrouchedHeight : config.Shape.StandingHeight;
        string clearance = SpatialClearanceSnapshot.Capture(engine.Spatial, spatial.Session, center, new((float)x, (float)y, (float)z),
            height, config, SpatialMovementSystem.SpatialColliders(LiveCollision()));
        overlays.PublishProbe(clearance);
        return DebugCommandResult.Success(clearance);
    }

    internal DebugCommandResult ReadJumpPlan(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Jump target must be finite.");
        var config = spatial.CurrentController;
        Vector3 center = (player.Position ?? throw new InvalidOperationException("Jump inspection requires a player position.")).ToVector();
        float height = player.Motion.Stance == CharacterStance.Crouched ? config.Shape.CrouchedHeight : config.Shape.StandingHeight;
        var plan = PlaytestTraversal.JumpToward(center - Vector3.UnitY * (height * .5f),
            input.ResolveCurrentLook(player).Forward, new Vector3((float)x, (float)y, (float)z),
            config, player.Motion.Grounded,
            controls.Settings.KeysFor("jump").First(), controls.Settings.KeysFor("move.forward").First());
        return DebugCommandResult.Success(JsonSerializer.Serialize(plan, DaggerfallPlaytestJsonContext.Default.PlaytestJumpPlan));
    }

    public DebugCommandResult ReadFloor(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Floor point must be finite.");
        return DebugCommandResult.Success(JsonSerializer.Serialize(Floor(new Vector3((float)x, (float)y, (float)z) + Vector3.UnitY * StepAbove()),
            DaggerfallPlaytestJsonContext.Default.PlaytestFloor));
    }

    private IReadOnlyList<ulong> Grids() => wild.Grids(sites.Projection.Inputs.Geometry.NavigationGridId);

    public DebugCommandResult SetWild(string state)
    {
        if (state is not ("on" or "off")) return DaggerfallDeveloperReplies.Refused("navigation.wild takes on or off.");
        wild.Enabled = state == "on";
        return DebugCommandResult.Success(wild.Enabled
            ? "The wild grid is derived about the player outdoors."
            : "No wild grid: walkers past a location's own grid walk straight for their target.");
    }

    public DebugCommandResult ReadGrids() => DaggerfallDeveloperReplies.Json(w =>
    {
        w.WriteStartObject();
        w.WriteStartArray("order");
        foreach (ulong id in Grids()) w.WriteNumberValue(id);
        w.WriteEndArray();
        w.WriteStartArray("grids");
        foreach (NavigationGridInfo grid in engine.Spatial.ReadNavigationGrids(new NavigationGridsRequest(spatial.Session)).Span)
        {
            w.WriteStartObject();
            w.WriteNumber("id", grid.GridId);
            w.WriteString("kind", grid.Kind.ToString());
            w.WriteBoolean("latest", grid.Latest);
            w.WriteNumber("cells", grid.WalkableCellCount);
            w.WriteNumber("cellSize", grid.CellSize);
            w.WriteNumber("minX", grid.MinX); w.WriteNumber("maxX", grid.MaxX);
            w.WriteNumber("minZ", grid.MinZ); w.WriteNumber("maxZ", grid.MaxZ);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartObject("wild");
        w.WriteNumber("id", DaggerfallWildNavigation.GridId);
        w.WriteBoolean("enabled", wild.Enabled);
        if (wild.Centre is { } centre) DaggerfallDeveloperReplies.Vector(w, "centre", centre); else w.WriteNull("centre");
        w.WriteBoolean("pending", wild.Pending);
        w.WriteNumber("derivations", wild.Derivations);
        w.WriteNumber("radius", wild.Tuning.Radius);
        if (wild.Installed is { } installed)
        {
            w.WriteStartObject("installed");
            w.WriteNumber("walkableCells", installed.WalkableCellCount);
            w.WriteNumber("derivedColumns", installed.DerivedColumnCount);
            w.WriteNumber("edgeTests", installed.EdgeTestCount);
            w.WriteNumber("derivationMilliseconds", installed.DerivationMicroseconds / 1000d);
            w.WriteNumber("components", installed.ComponentCount);
            w.WriteEndObject();
        }
        else w.WriteNull("installed");
        w.WriteEndObject();
        w.WriteEndObject();
    });

    public DebugCommandResult ReadRoute(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Route goal must be finite.");
        Vector3 from = PlayerFeet(), goal = new((float)x, (float)y, (float)z);
        // As a walker's step does: the first grid that takes the start and reaches the goal, else the first that takes it.
        NavigationGridInfo[] published = engine.Spatial.ReadNavigationGrids(new NavigationGridsRequest(spatial.Session)).ToArray();
        NavigationStepResult result = default;
        NavigationGridInfo? grid = null;
        (NavigationStepResult Result, NavigationGridInfo? Grid)? first = null;
        foreach (ulong id in Grids())
        {
            result = engine.Spatial.EvaluateNavigationStep(new NavigationStepRequest(spatial.Session, from, goal, RouteStepMetres, RouteVisitedCells, id));
            grid = published.FirstOrDefault(value => value.GridId == id) is { GridId: > 0 } found ? found : null;
            if (result.Outcome is NavigationPathOutcome.StartNotWalkable or NavigationPathOutcome.ProjectionUnavailable) continue;
            first ??= (result, grid);
            if (result.Outcome != NavigationPathOutcome.GoalNotWalkable) { first = (result, grid); break; }
        }
        if (first is { } taken) (result, grid) = taken;
        // The result is borrowed until the next Spatial call, and the goal's floor is one.
        PlanarNavCell[] path = result.Path.ToArray();
        NavigationPathEdge[] edges = result.Edges.ToArray();
        List<PlaytestRouteWaypoint> waypoints = [];
        if (grid is { CellSize: > 0 } cells)
            foreach (PlaytestRouteTurn turn in PlaytestRouteTurn.Of(path, edges))
            {
                (double centreX, double centreZ) = PlaytestRouteTurn.Centre(turn.Cell, cells.CellSize);
                waypoints.Add(new(turn.Index, centreX, centreZ, turn.Cell.Y, turn.Edge.ToString().ToLowerInvariant()));
            }
        PlaytestRoute route = new(result.Outcome.ToString(), PlaytestPoint.From(from), PlaytestPoint.From(goal), Floor(goal + Vector3.UnitY * StepAbove()),
            grid?.GridId, grid?.CellSize, result.Visited, path.Length, waypoints,
            path.Length != 0 ? PlaytestPoint.From(result.NextWaypoint) : null,
            result.NearestPresent ? PlaytestPoint.From(result.Nearest) : null,
            "A bounded suggestion over the navigation actors walk: ordinary movement and collision decide. Waypoints are the "
            + "path's start, goal and turns at cell centres; a level orders the navigation's height bands and is not metres "
            + "(navigation.floor X Y Z reads a floor). GoalNotWalkable with a not-loaded goalFloor means the goal's collision "
            + "has not streamed in; with no-floor, there is nothing to stand on there.");
        return DebugCommandResult.Success(JsonSerializer.Serialize(route, DaggerfallPlaytestJsonContext.Default.PlaytestRoute));
    }

    /// <summary>The floor under the player's feet, or null where it is not loaded or there is none.</summary>
    internal double? PlayerFloorY() => player.Position is null ? null : Floor(PlayerFeet() + Vector3.UnitY * StepAbove()).FloorY;

    /// <summary>The floor at or below <paramref name="from"/>, only where its collision is in the scene.</summary>
    private PlaytestFloor Floor(Vector3 from)
    {
        if (sites.Exterior.CollisionUnknownAt(from) is { } unknown) return PlaytestFloor.NotLoaded(unknown);
        SpatialHit hit = spatial.CastRay(from, -Vector3.UnitY, FloorSearchMetres, LiveCollision());
        return PlaytestFloor.FromDownwardHit(hit, spatial.CurrentController.Surface.MaximumSlopeRadians, FloorSearchMetres);
    }

    /// <summary>A floor is searched for from a step above the point asked about, so a point on the floor finds it.</summary>
    private float StepAbove() => spatial.CurrentController.Surface.MaximumStepHeight + .05F;

    private Vector3 PlayerFeet()
    {
        var config = spatial.CurrentController;
        float height = player.Motion.Stance == CharacterStance.Crouched ? config.Shape.CrouchedHeight : config.Shape.StandingHeight;
        return (player.Position ?? throw new InvalidOperationException("Spatial inspection requires a player position.")).ToVector() - Vector3.UnitY * (height * .5f);
    }

    /// <summary>The collision movement meets now: the admitted scene plus the live doors and supports.</summary>
    private CharacterStepEnvironment LiveCollision() => sites.Exterior.CharacterEnvironment(player.Motion);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PlaytestJumpPlan))]
[JsonSerializable(typeof(PlaytestFloor))]
[JsonSerializable(typeof(PlaytestRoute))]
internal partial class DaggerfallPlaytestJsonContext : JsonSerializerContext;

/// <summary>
/// One waypoint of <see cref="PlaytestRoute"/>: a path cell's centre and its level, which orders the navigation's height
/// bands; the Engine gives no support height for a path cell, so none is reported.
/// </summary>
internal sealed record PlaytestRouteWaypoint(int Index, double X, double Z, long Level, string Edge);

/// <summary>The navigation.route reply: the Engine's outcome and path, read as the turns a tester walks.</summary>
internal sealed record PlaytestRoute(string Outcome, PlaytestPoint From, PlaytestPoint Goal, PlaytestFloor GoalFloor, ulong? GridId,
    double? CellSize, uint Visited, int PathCells, IReadOnlyList<PlaytestRouteWaypoint> Waypoints, PlaytestPoint? FirstStep,
    PlaytestPoint? NearestReachable, string Meaning);
