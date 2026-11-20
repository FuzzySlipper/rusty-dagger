using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Kit.Actors;

/// <summary>Movement medium selected by the ruleset for one Engine navigation evaluation.</summary>
/// <remarks>
/// The medium is policy metadata.  The Engine remains the owner of navigation, collision and
/// movement admission; a ruleset must never replace this call with a local grid or detour.
/// </remarks>
public enum ActorNavigationMode
{
    Ground,
    Swimming,
    Flying,
    WaterWalking,
}

/// <summary>Ruleset-supplied limits for one Engine navigation evaluation.</summary>
public readonly record struct ActorNavigationRequest(
    WorldPoint Target,
    float MaximumStepUnits,
    uint MaximumVisited,
    ActorNavigationMode Mode = ActorNavigationMode.Ground,
    float StepSeconds = 1f / 60f)
{
    public ActorNavigationRequest Validate()
    {
        Target.Validate();
        if (!float.IsFinite(MaximumStepUnits) || MaximumStepUnits <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumStepUnits));
        }

        if (MaximumVisited == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumVisited));
        }

        if (!Enum.IsDefined(Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(Mode));
        }

        if (!float.IsFinite(StepSeconds) || StepSeconds is < .001f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(StepSeconds));
        }

        return this;
    }
}

/// <summary>
/// Applies Engine navigation facts to a generic actor pose. The Engine owns
/// navigation, collision, and path evaluation; rulesets supply a target and
/// bounded request policy.
/// </summary>
public sealed class ActorNavigationCoordinator
{
    private readonly ISpatialService _spatial;
    private readonly SpatialSession _session;
    private readonly bool _useCharacterController;
    // Actors held in collision with no clear spot in reach, each reported once until it steps (CharacterPenetration).
    private readonly HashSet<EntityId> _heldInCollision = [];
    private readonly CharacterControllerConfig _characterControllerConfig;
    private readonly Func<ActorState, CharacterStepEnvironment>? _environmentProvider;
    private readonly Func<IReadOnlyList<ulong>> _grids;
    // The grid each actor's last step went by, or none when it walked straight (Route).
    private readonly Dictionary<EntityId, ulong?> _routes = [];
    // Where each actor crosses from the grid that holds it to one that holds its goal (none when no crossing was found),
    // with the goal and start it was last checked for and the grids it was found on (Handoff).
    private readonly Dictionary<EntityId, KeptHandoff> _handoffs = [];

    private readonly record struct KeptHandoff(Vector3 Goal, Vector3 Start, Vector3? Point, ulong Grid, ulong Other, ulong Grids);

    /// <summary>The supplied session remains owned by its spatial composition system.</summary>
    /// <param name="grids">
    /// The navigation grids a walker's step tries, in order: the first that can take its start and reach its goal
    /// carries it. None names no grid, so the Engine reads the grid published last.
    /// </param>
    public ActorNavigationCoordinator(ISpatialService spatial, SpatialSession session,
        EntityStore? actors = null, CharacterControllerConfig? characterControllerConfig = null,
        Func<ActorState, CharacterStepEnvironment>? environmentProvider = null, Func<IReadOnlyList<ulong>>? grids = null)
    {
        _grids = grids ?? (static () => [0UL]);
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _useCharacterController = actors is not null;
        _characterControllerConfig = characterControllerConfig ?? default;
        _environmentProvider = environmentProvider;
    }

    /// <summary>
    /// Evaluates one Engine-owned navigation step. Only an explicit Reached
    /// outcome changes product pose; every other Engine fact leaves it intact,
    /// but for a body the Engine cannot free, which is stood clear if it can
    /// be (<see cref="CharacterPenetration"/>). A walker's step tries each grid in
    /// turn, going on to the next when one cannot take its start or reach its goal,
    /// as a route from the wilds into a town's own grid does; one no grid can start
    /// from (off every grid, above its floor, or with no navigation published at
    /// all) walks straight for the target under the Engine character controller
    /// instead, which owns its collision, gravity and steps.
    /// </summary>
    public NavigationStepResult Evaluate(ActorState actor, ActorNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(actor);
        request.Validate();

        if (request.Mode is not ActorNavigationMode.Ground && _useCharacterController)
            return EvaluateCharacterStep(actor, request);

        ActorPose before = actor.Pose;
        NavigationStepResult? first = null;
        ulong? carried = null, firstGrid = null;
        NavigationStepResult receipt = default;
        foreach (ulong grid in _grids())
        {
            receipt = _spatial.EvaluateNavigationStep(new NavigationStepRequest(
                _session,
                before.Position.ToVector(),
                request.Target.ToVector(),
                request.MaximumStepUnits,
                request.MaximumVisited,
                grid));
            if (receipt.Outcome is NavigationPathOutcome.StartNotWalkable or NavigationPathOutcome.ProjectionUnavailable) continue;
            if (first is null) (first, firstGrid) = (receipt, grid);
            if (receipt.Outcome == NavigationPathOutcome.GoalNotWalkable) continue;
            carried = grid;
            break;
        }

        if (first is null && _useCharacterController)
        {
            _routes[actor.Actor.Entity] = null;
            return EvaluateCharacterStep(actor, request);
        }
        // No one grid holds both ends: the grid that took the start carries the walker to where it overlaps one that
        // holds the goal, from where that grid takes it on.
        if (carried is null && first is { Outcome: NavigationPathOutcome.GoalNotWalkable } && firstGrid is ulong startGrid && _grids().Count > 1
            && Handoff(actor, before.Position.ToVector(), request.Target.ToVector(), startGrid, request.MaximumVisited) is { } handoff)
        {
            receipt = _spatial.EvaluateNavigationStep(new NavigationStepRequest(_session, before.Position.ToVector(), handoff,
                request.MaximumStepUnits, request.MaximumVisited, startGrid));
            carried = startGrid;
        }
        // A goal no grid holds is answered by the first grid that took the start.
        else if (carried is null && first is { } held) receipt = held;
        _routes[actor.Actor.Entity] = carried ?? firstGrid;

        if (receipt.Outcome == NavigationPathOutcome.Reached)
        {
            actor.ApplyPose(WithHeadingForAcceptedWaypoint(before, receipt.NextWaypoint));
        }

        return receipt;
    }

    /// <summary>How far apart the points tried for a handoff are, along the way and across it, in metres.</summary>
    private const float HandoffSpacing = 2f;

    /// <summary>How far the target may move in all, from where an actor's handoff was last checked, before it is checked again, in metres.</summary>
    private const float HandoffDrift = 2f;

    /// <summary>How far either side of the straight way a handoff is looked for, in metres.</summary>
    private const float HandoffReach = 16f;

    /// <summary>How far above the way's height the floor under a point is first looked for, then the farther probe, in metres.</summary>
    private const float HandoffNearProbe = 4f, HandoffFarProbe = 32f;

    /// <summary>
    /// Where a walker the start grid holds, chasing a goal only another grid holds, crosses from one to the other: a
    /// point both grids hold that the start grid routes the walker to and the other grid routes on to the goal, sought
    /// across a band either side of the straight way (so around what stands on it), nearest the goal and nearest the way
    /// first. Each point tried stands on the floor under it. What was found, or that none was, is kept per actor while
    /// the grids stay as they were published: a crossing is checked again once the goal has gone
    /// <see cref="HandoffDrift"/> from where it was last checked, and one none was found for is sought again only once
    /// the goal has gone that far or the walker <see cref="HandoffSpacing"/>.
    /// </summary>
    private Vector3? Handoff(ActorState actor, Vector3 start, Vector3 goal, ulong startGrid, uint visited)
    {
        IReadOnlyList<ulong> grids = _grids();
        ulong published = Published(grids);
        if (_handoffs.TryGetValue(actor.Actor.Entity, out KeptHandoff kept) && kept.Grid == startGrid && kept.Grids == published)
        {
            bool still = Vector3.Distance(kept.Goal, goal) <= HandoffDrift;
            if (kept.Point is not { } point)
            {
                if (still && Vector3.Distance(kept.Start, start) <= HandoffSpacing) return null;
            }
            else if (still) return point;
            else if (Routes(kept.Other, point, goal, visited))
            {
                _handoffs[actor.Actor.Entity] = kept with { Goal = goal, Start = start };
                return point;
            }
        }
        _handoffs[actor.Actor.Entity] = new(goal, start, null, startGrid, startGrid, published);
        Vector3 way = (goal - start) with { Y = 0 };
        float length = way.Length();
        if (length < HandoffSpacing) return null;
        Vector3 along = way / length, across = new(-along.Z, 0, along.X);
        for (float distance = length - HandoffSpacing; distance > HandoffSpacing; distance -= HandoffSpacing)
        {
            Vector3 onWay = start + (goal - start) * (distance / length);
            for (float offset = 0; offset <= HandoffReach; offset += HandoffSpacing)
                foreach (float side in offset == 0 ? new[] { 0f } : new[] { offset, -offset })
                {
                    if (Floor(onWay + across * side) is not { } point || !Holds(startGrid, point)) continue;
                    foreach (ulong other in grids)
                    {
                        if (other == startGrid || !Holds(other, point)) continue;
                        // A usable crossing: the start grid routes the walker to it, the other on to the goal.
                        if (!Routes(other, point, goal, visited) || !Routes(startGrid, start, point, visited)) continue;
                        _handoffs[actor.Actor.Entity] = new(goal, start, point, startGrid, other, published);
                        return point;
                    }
                }
        }
        return null;
    }

    /// <summary>The grids as they are published now: changes whenever one is published again with other cells.</summary>
    private ulong Published(IReadOnlyList<ulong> grids)
    {
        ulong hash = 17;
        foreach (NavigationGridInfo info in _spatial.ReadNavigationGrids(new NavigationGridsRequest(_session)).Span)
            if (grids.Contains(info.GridId)) hash = hash * 31 + info.ProjectionHash ^ info.GridId;
        return hash;
    }

    /// <summary>The floor under a point, looked for from just above it and then from well above; null over none.</summary>
    private Vector3? Floor(Vector3 point)
    {
        foreach (float above in new[] { HandoffNearProbe, HandoffFarProbe })
        {
            SpatialHit floor = _spatial.CastRay(new SpatialRaycastRequest(_session, point + Vector3.UnitY * above, -Vector3.UnitY,
                above + HandoffFarProbe, new SpatialQueryFilter(uint.MaxValue, uint.MaxValue), default, default, default));
            if (floor.Present && !floor.StartSolid) return floor.Point;
        }
        return null;
    }

    private bool Holds(ulong grid, Vector3 point) => _spatial.EvaluateNavigationStep(new NavigationStepRequest(_session, point, point, 1f, 1,
        grid)).Outcome is not (NavigationPathOutcome.StartNotWalkable or NavigationPathOutcome.GoalNotWalkable or NavigationPathOutcome.ProjectionUnavailable);

    private bool Routes(ulong grid, Vector3 from, Vector3 to, uint visited) => _spatial.EvaluateNavigationStep(new NavigationStepRequest(_session, from, to, 1f,
        visited, grid)).Outcome == NavigationPathOutcome.Reached;

    /// <summary>
    /// The grid an actor's last ground step went by, or null when it walked straight for its target off every grid;
    /// false when it has taken no ground step.
    /// </summary>
    public bool TryRoute(EntityId actor, out ulong? grid) => _routes.TryGetValue(actor, out grid);

    /// <summary>
    /// Runs an actor's accepted target intent through the Engine character controller. The
    /// controller owns collision, gravity, water immersion, and the accepted Transform; this
    /// coordinator only converts a ruleset target into a bounded command and receipt.
    /// </summary>
    private NavigationStepResult EvaluateCharacterStep(ActorState actor, ActorNavigationRequest request)
    {
        if (_environmentProvider is null)
            throw new InvalidOperationException("Character actor navigation requires an admitted environment provider.");
        if (!actor.Actor.Store.Has(actor.Actor.Entity, EngineComponentTypes.Transform)
            || !actor.Actor.Store.Has(actor.Actor.Entity, EngineComponentTypes.CharacterMotion))
            throw new InvalidOperationException($"Actor {actor.DurableId} has no Engine Transform and CharacterMotion components.");

        CharacterStepEnvironment environment = _environmentProvider(actor);
        ActorPose before = actor.Pose;
        Vector3 delta = request.Target.ToVector() - before.Position.ToVector();
        float distance = delta.Length();
        if (!float.IsFinite(distance))
            return CharacterReceipt(NavigationPathOutcome.NonFinitePosition, before.Position.ToVector(), 0);
        if (distance <= .0001f)
            return CharacterReceipt(NavigationPathOutcome.Reached, before.Position.ToVector(), 1);

        CharacterWaterVolume? water = request.Mode == ActorNavigationMode.Swimming
            ? WaterAt(environment, before.Position.ToVector())
            : null;
        if (request.Mode == ActorNavigationMode.Swimming && water is null)
            return CharacterReceipt(NavigationPathOutcome.ProjectionUnavailable, before.Position.ToVector(), 0);

        float stepDistance = MathF.Min(distance, request.MaximumStepUnits);
        Vector3 direction = delta / distance;
        // The normalized three-dimensional direction can round the horizontal magnitude a few
        // ulps above one. Engine command admission treats the planar intent envelope as strict,
        // so keep this derived input inside that published contract.
        float planarDistance = Math.Clamp(
            MathF.Sqrt((direction.X * direction.X) + (direction.Z * direction.Z)), 0f, 1f);
        float heading = planarDistance > .0001f
            ? MathF.Atan2(direction.X, -direction.Z)
            : before.HeadingYawRadians;
        Vector2 planarIntent = planarDistance > .0001f ? new(0f, planarDistance) : Vector2.Zero;
        float verticalIntent = request.Mode is ActorNavigationMode.Flying or ActorNavigationMode.Swimming
            ? Math.Clamp(direction.Y, -1f, 1f)
            : 0f;
        float speed = stepDistance / request.StepSeconds;
        float acceleration = MathF.Max(speed, speed / request.StepSeconds);
        CharacterMovementMode engineMode = request.Mode switch
        {
            ActorNavigationMode.Swimming => CharacterMovementMode.Swimming,
            ActorNavigationMode.Flying => CharacterMovementMode.Flying,
            ActorNavigationMode.WaterWalking or ActorNavigationMode.Ground => CharacterMovementMode.Walking,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        CharacterMovementRequest movement = new(
            engineMode,
            verticalIntent,
            speed,
            acceleration,
            Drag: 0f,
            Minimum: water?.Minimum ?? Vector3.Zero,
            Maximum: water?.Maximum ?? Vector3.Zero,
            GravityScale: 0f,
            Buoyancy: request.Mode == ActorNavigationMode.Swimming ? 1f : 0f,
            ClimbReach: 0f);
        CharacterMotion motion = actor.Actor.Store.Get(actor.Actor.Entity, EngineComponentTypes.CharacterMotion);
        CharacterControllerCommand command = new(
            movement,
            planarIntent,
            heading,
            JumpPressed: false,
            JumpHeld: false,
            CrouchRequested: false,
            ExternalVelocity: Vector3.Zero,
            ExternalImpulse: Vector3.Zero,
            request.StepSeconds,
            checked(motion.LastCommandSequence + 1));
        Vector3 centerOffset = CenterOffset(motion);
        CharacterStepReceipt accepted;
        try
        {
            accepted = _spatial.ProposeCharacterStep(new CharacterStepRequest(
                _session, before.Position.ToVector() + centerOffset, motion, environment.Support,
                environment.Obstacles, environment.MeshInstances, _characterControllerConfig, command));
        }
        catch (EngineCallException failure) when (CharacterPenetration.IsUnresolved(failure))
        {
            StandClear(actor, motion, environment, failure);
            return CharacterReceipt(NavigationPathOutcome.StartBlocked, actor.Pose.Position.ToVector(), 0);
        }

        _heldInCollision.Remove(actor.Actor.Entity);
        actor.Actor.Store.Set(actor.Actor.Entity, EngineComponentTypes.CharacterMotion, accepted.Motion);
        Vector3 position = accepted.Transform.Translation - centerOffset;
        bool moved = (accepted.Displacement.LengthSquared() > .0000001f)
            || Vector3.DistanceSquared(position, request.Target.ToVector()) <= .04f;
        if (moved)
        {
            float acceptedHeading = planarDistance > .0001f ? heading : before.HeadingYawRadians;
            actor.ApplyPose(new ActorPose(WorldPoint.From(position), acceptedHeading));
            return CharacterReceipt(NavigationPathOutcome.Reached, position, 1);
        }

        NavigationPathOutcome outcome = accepted.BlockFlags != CharacterBlockFlags.None
            ? NavigationPathOutcome.StartBlocked
            : NavigationPathOutcome.NoPath;
        return CharacterReceipt(outcome, position, 0);
    }

    /// <summary>
    /// One step of an actor thrown at <paramref name="velocity"/>, or falling once it stops. The Engine
    /// character controller owns collision and gravity; a flying actor drifts without falling. Null when
    /// the actor is wedged and cannot move: it is then stood clear or held (<see cref="CharacterPenetration"/>).
    /// </summary>
    public CharacterStepReceipt? Throw(ActorState actor, Vector3 velocity, ActorNavigationMode mode, float stepSeconds)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (_environmentProvider is null)
            throw new InvalidOperationException("A thrown actor requires an admitted environment provider.");
        CharacterStepEnvironment environment = _environmentProvider(actor);
        ActorPose before = actor.Pose;
        CharacterMotion motion = actor.Actor.Store.Get(actor.Actor.Entity, EngineComponentTypes.CharacterMotion) with { ExternalVelocity = velocity };
        CharacterMovementRequest movement = mode == ActorNavigationMode.Flying
            ? new(CharacterMovementMode.Flying, 0f, 1f, 1f, Drag: 0f, Minimum: Vector3.Zero, Maximum: Vector3.Zero, GravityScale: 0f, Buoyancy: 0f, ClimbReach: 0f)
            : default;
        CharacterControllerCommand command = new(movement, Vector2.Zero, before.HeadingYawRadians, JumpPressed: false, JumpHeld: false,
            CrouchRequested: false, ExternalVelocity: Vector3.Zero, ExternalImpulse: Vector3.Zero, stepSeconds, checked(motion.LastCommandSequence + 1));
        Vector3 centerOffset = CenterOffset(motion);
        CharacterStepReceipt accepted;
        try
        {
            accepted = _spatial.ProposeCharacterStep(new CharacterStepRequest(
                _session, before.Position.ToVector() + centerOffset, motion, environment.Support,
                environment.Obstacles, environment.MeshInstances, _characterControllerConfig, command));
        }
        catch (EngineCallException failure) when (CharacterPenetration.IsUnresolved(failure))
        {
            StandClear(actor, motion, environment, failure);
            return null;
        }
        _heldInCollision.Remove(actor.Actor.Entity);
        actor.Actor.Store.Set(actor.Actor.Entity, EngineComponentTypes.CharacterMotion, accepted.Motion);
        actor.ApplyPose(new ActorPose(WorldPoint.From(accepted.Transform.Translation - centerOffset), before.HeadingYawRadians));
        return accepted;
    }

    /// <summary>Ends a throw: what the blow gave the actor stops carrying it.</summary>
    public void Settle(ActorState actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        CharacterMotion motion = actor.Actor.Store.Get(actor.Actor.Entity, EngineComponentTypes.CharacterMotion);
        actor.Actor.Store.Set(actor.Actor.Entity, EngineComponentTypes.CharacterMotion, motion with { ExternalVelocity = Vector3.Zero, ControlledVelocity = Vector3.Zero });
    }

    // Navigation/actor poses are feet; the Engine character proposal takes a capsule center.
    // Keep that projection local so switching movement medium never changes the actor's origin.
    private Vector3 CenterOffset(CharacterMotion motion)
    {
        CharacterShapeConfig shape = _characterControllerConfig.Shape;
        float height = motion.Stance == CharacterStance.Crouched ? shape.CrouchedHeight : shape.StandingHeight;
        return Vector3.UnitY * (height * .5f + shape.ContactSkin + shape.ClearancePadding);
    }

    /// <summary>
    /// The Engine refused an actor's step for a body its recovery cannot free. The actor is stood at the nearest clear
    /// spot in reach, at rest in its stance, and steps on from there; with none clear it stays where it is, reported once
    /// until it steps.
    /// </summary>
    private void StandClear(ActorState actor, CharacterMotion motion, CharacterStepEnvironment environment, EngineCallException failure)
    {
        ActorPose before = actor.Pose;
        Vector3 centerOffset = CenterOffset(motion);
        if (CharacterPenetration.NearestClearCentre(_spatial, _session, _characterControllerConfig, motion.Stance,
                before.Position.ToVector() + centerOffset, environment) is { } clear)
        {
            actor.Actor.Store.Set(actor.Actor.Entity, EngineComponentTypes.CharacterMotion,
                default(CharacterMotion) with { Stance = motion.Stance, PeakY = clear.Y, FallOriginY = clear.Y });
            actor.ApplyPose(new ActorPose(WorldPoint.From(clear - centerOffset), before.HeadingYawRadians));
            _heldInCollision.Remove(actor.Actor.Entity);
            Console.Error.WriteLine($"Actor {actor.DurableId} is stood clear at {actor.Pose.Position}, out of collision at {before.Position}: {failure.Message}");
            return;
        }
        if (_heldInCollision.Add(actor.Actor.Entity))
            Console.Error.WriteLine($"Actor {actor.DurableId} stays at {before.Position}, with no clear spot within "
                + $"{_characterControllerConfig.Recovery.MaximumDistance} m: {failure.Message}");
    }

    /// <summary>
    /// The first of the environment's water volumes whose Engine trigger contains the position. The
    /// environment says which volumes apply to this actor; the Engine trigger point query decides
    /// containment, under the same rule as trigger overlap, from each trigger's current registered row.
    /// A product that offers water to a swimming actor therefore registers each volume as a spatial
    /// trigger under its <see cref="CharacterWaterVolume.Trigger"/> identity and keeps its row current
    /// through the trigger reconcile, as it does for the player's own water.
    /// </summary>
    private CharacterWaterVolume? WaterAt(CharacterStepEnvironment environment, Vector3 position)
    {
        if (environment.WaterVolumes.IsEmpty) return null;
        SpatialTriggerPointQueryResult containing = _spatial.QueryTriggersAtPoints(
            new SpatialTriggerPointQueryRequest(_session, new[] { position }));
        if (containing.Hits.IsEmpty) return null;
        foreach (CharacterWaterVolume volume in environment.WaterVolumes.Span)
            foreach (SpatialTriggerPointHit hit in containing.Hits.Span)
                if (hit.Trigger == volume.Trigger) return volume;
        return null;
    }

    private static NavigationStepResult CharacterReceipt(NavigationPathOutcome outcome, Vector3 waypoint, uint reached) =>
        new(ReadOnlyMemory<PlanarNavCell>.Empty, ReadOnlyMemory<NavigationPathEdge>.Empty, outcome, waypoint,
            default, NavigationEdgeKind.Walk, 0f, reached, 1, 0, 0, 0, false, default, default);

    private static ActorPose WithHeadingForAcceptedWaypoint(ActorPose before, Vector3 waypoint)
    {
        WorldPoint next = WorldPoint.From(waypoint);
        float deltaX = next.X - before.Position.X;
        float deltaZ = next.Z - before.Position.Z;
        float heading = (deltaX == 0f && deltaZ == 0f)
            ? before.HeadingYawRadians
            : MathF.Atan2(deltaX, -deltaZ);
        return new ActorPose(next, heading);
    }
}
