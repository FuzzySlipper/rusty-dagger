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

    /// <summary>The supplied session remains owned by its spatial composition system.</summary>
    public ActorNavigationCoordinator(ISpatialService spatial, SpatialSession session,
        EntityStore? actors = null, CharacterControllerConfig? characterControllerConfig = null,
        Func<ActorState, CharacterStepEnvironment>? environmentProvider = null)
    {
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
    /// be (<see cref="CharacterPenetration"/>). A walker the navigation cannot
    /// start from (off every grid, as in ground no navigation covers, above its
    /// floor, or with no navigation published at all) walks straight for the target under the Engine character
    /// controller instead, which owns its collision, gravity and steps.
    /// </summary>
    public NavigationStepResult Evaluate(ActorState actor, ActorNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(actor);
        request.Validate();

        if (request.Mode is not ActorNavigationMode.Ground && _useCharacterController)
            return EvaluateCharacterStep(actor, request);

        ActorPose before = actor.Pose;
        NavigationStepResult receipt = _spatial.EvaluateNavigationStep(new NavigationStepRequest(
            _session,
            before.Position.ToVector(),
            request.Target.ToVector(),
            request.MaximumStepUnits,
            request.MaximumVisited));

        if (receipt.Outcome is NavigationPathOutcome.StartNotWalkable or NavigationPathOutcome.ProjectionUnavailable && _useCharacterController)
            return EvaluateCharacterStep(actor, request);

        if (receipt.Outcome == NavigationPathOutcome.Reached)
        {
            actor.ApplyPose(WithHeadingForAcceptedWaypoint(before, receipt.NextWaypoint));
        }

        return receipt;
    }

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
