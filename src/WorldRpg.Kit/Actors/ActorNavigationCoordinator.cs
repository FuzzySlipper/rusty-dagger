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
    private readonly EntityCharacterController? _characterController;
    private readonly CharacterControllerConfig _characterControllerConfig;
    private readonly Func<ActorState, CharacterStepEnvironment>? _environmentProvider;

    /// <summary>The supplied session remains owned by its spatial composition system.</summary>
    public ActorNavigationCoordinator(ISpatialService spatial, SpatialSession session,
        EntityStore? actors = null, CharacterControllerConfig? characterControllerConfig = null,
        Func<ActorState, CharacterStepEnvironment>? environmentProvider = null)
    {
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _characterController = actors is null ? null : new EntityCharacterController(actors, _spatial);
        _characterControllerConfig = characterControllerConfig ?? default;
        _environmentProvider = environmentProvider;
    }

    /// <summary>
    /// Evaluates one Engine-owned navigation step. Only an explicit Reached
    /// outcome changes product pose; every other Engine fact leaves it intact.
    /// </summary>
    public NavigationStepResult Evaluate(ActorState actor, ActorNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(actor);
        request.Validate();

        if (request.Mode is not ActorNavigationMode.Ground && _characterController is not null)
            return EvaluateCharacterStep(actor, request);

        ActorPose before = actor.Pose;
        NavigationStepResult receipt = _spatial.EvaluateNavigationStep(new NavigationStepRequest(
            _session,
            before.Position.ToVector(),
            request.Target.ToVector(),
            request.MaximumStepUnits,
            request.MaximumVisited));

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

        CharacterWaterVolume? water = null;
        if (request.Mode == ActorNavigationMode.Swimming)
        {
            foreach (CharacterWaterVolume volume in environment.WaterVolumes.Span)
            {
                if (!Contains(volume, before.Position.ToVector())) continue;
                water = volume;
                break;
            }
        }
        if (request.Mode == ActorNavigationMode.Swimming && water is null)
            return CharacterReceipt(NavigationPathOutcome.ProjectionUnavailable, before.Position.ToVector(), 0);

        float stepDistance = MathF.Min(distance, request.MaximumStepUnits);
        Vector3 direction = delta / distance;
        float planarDistance = MathF.Sqrt((direction.X * direction.X) + (direction.Z * direction.Z));
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
            ActorNavigationMode.WaterWalking => CharacterMovementMode.Walking,
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
        EntityCharacterController controller = _characterController
            ?? throw new InvalidOperationException("Character actor navigation has no Engine controller.");
        EntityCharacterControllerReceipt accepted = controller.Step(
            actor.Actor.Entity,
            _session,
            environment.Support,
            _characterControllerConfig,
            command,
            environment.Obstacles,
            environment.MeshInstances);

        Vector3 position = accepted.Native.Transform.Translation;
        bool moved = (accepted.Native.Displacement.LengthSquared() > .0000001f)
            || Vector3.DistanceSquared(position, request.Target.ToVector()) <= .04f;
        if (moved)
        {
            float acceptedHeading = planarDistance > .0001f ? heading : before.HeadingYawRadians;
            actor.ApplyPose(new ActorPose(WorldPoint.From(position), acceptedHeading));
            return CharacterReceipt(NavigationPathOutcome.Reached, position, 1);
        }

        NavigationPathOutcome outcome = accepted.Native.BlockFlags != CharacterBlockFlags.None
            ? NavigationPathOutcome.StartBlocked
            : NavigationPathOutcome.NoPath;
        return CharacterReceipt(outcome, position, 0);
    }

    private static bool Contains(CharacterWaterVolume volume, Vector3 position) =>
        position.X >= volume.Minimum.X && position.X <= volume.Maximum.X
        && position.Y >= volume.Minimum.Y && position.Y <= volume.Maximum.Y
        && position.Z >= volume.Minimum.Z && position.Z <= volume.Maximum.Z;

    private static NavigationStepResult CharacterReceipt(NavigationPathOutcome outcome, Vector3 waypoint, uint reached) =>
        new(ReadOnlyMemory<PlanarNavCell>.Empty, ReadOnlyMemory<NavigationPathEdge>.Empty, outcome, waypoint,
            default, NavigationEdgeKind.Walk, reached, 1, 0, 0, 0, false, default, default);

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
