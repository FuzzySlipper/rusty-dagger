using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Kit.Ai;

/// <summary>The bounded activity states of an actor that is choosing ordinary world waypoints.</summary>
public enum WanderState
{
    Idle,
    Seeking,
    Moving,
    Blocked,
    Unloaded,
    Dead,
}

/// <summary>Ruleset-owned limits for one Engine-backed wander update.</summary>
/// <remarks>
/// The Kit owns no movement numbers; the ruleset supplies speed, waypoint and idle values. A walker turns to face the
/// way it walks at <paramref name="TurnRadiansPerSecond"/> at most (infinite: at once) and walks as fast as it faces that
/// way: slower into a sharp turn, turning on the spot where the way lies behind it. It sets off and comes to a stop over
/// <paramref name="EaseSeconds"/> (0: at full speed straight away).
/// </remarks>
public readonly record struct WanderPolicy(
    float MovementSpeedUnitsPerSecond,
    float WaypointDistance,
    float IdleDurationSeconds,
    uint NavigationMaximumVisited,
    uint MaximumFailures = 2,
    ActorNavigationMode NavigationMode = ActorNavigationMode.Ground,
    float TurnRadiansPerSecond = float.PositiveInfinity,
    float EaseSeconds = 0f)
{
    public WanderPolicy Validate()
    {
        if (!float.IsFinite(MovementSpeedUnitsPerSecond) || MovementSpeedUnitsPerSecond <= 0f)
            throw new ArgumentOutOfRangeException(nameof(MovementSpeedUnitsPerSecond));
        if (!float.IsFinite(WaypointDistance) || WaypointDistance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(WaypointDistance));
        if (!float.IsFinite(IdleDurationSeconds) || IdleDurationSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(IdleDurationSeconds));
        if (NavigationMaximumVisited == 0) throw new ArgumentOutOfRangeException(nameof(NavigationMaximumVisited));
        if (MaximumFailures == 0) throw new ArgumentOutOfRangeException(nameof(MaximumFailures));
        if (!Enum.IsDefined(NavigationMode)) throw new ArgumentOutOfRangeException(nameof(NavigationMode));
        if (!(TurnRadiansPerSecond > 0f)) throw new ArgumentOutOfRangeException(nameof(TurnRadiansPerSecond));
        if (!float.IsFinite(EaseSeconds) || EaseSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(EaseSeconds));
        return this;
    }
}

/// <summary>
/// Transient actor-local wander memory. The target is deliberately transient: the durable NPC
/// registry stores the accepted actor pose, while a site admission can choose a new waypoint.
/// </summary>
public sealed class WanderMemoryComponent
{
    public WanderState State { get; private set; } = WanderState.Idle;
    public uint NavigationFailures { get; private set; }
    public uint WaypointIndex { get; private set; }
    public float IdleRemainingSeconds { get; private set; }
    public WorldPoint? Target { get; private set; }

    /// <summary>The heading the actor walks toward its target, which its facing turns to; null while it has no target.</summary>
    public float? Course { get; private set; }

    /// <summary>The share of its pace the actor walked its last step at, easing up from 0 as it sets off and down as it stops or turns.</summary>
    public float Stride { get; private set; }

    /// <summary>
    /// A route set for a developer's look at the actor, followed in place of its own waypoints: one point it walks to
    /// and stands at, keeping the heading it has there, or points it walks back and forth along. Null while it wanders
    /// on its own.
    /// </summary>
    public IReadOnlyList<WorldPoint>? Route { get; private set; }

    /// <summary>The share of the wander policy's speed the actor walks at: 1 unless something chose its own pace for it.</summary>
    public float Pace { get; private set; } = 1f;

    /// <summary>Walks the actor at <paramref name="pace"/> of the wander policy's speed from its next step.</summary>
    public void SetPace(float pace)
    {
        if (!float.IsFinite(pace) || pace <= 0f) throw new ArgumentOutOfRangeException(nameof(pace), pace, "An actor walks at a pace above 0.");
        Pace = pace;
    }

    /// <summary>Follows <paramref name="route"/> from its first point, the actor's next waypoint chosen afresh.</summary>
    public void Stage(IReadOnlyList<WorldPoint> route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Count == 0) throw new ArgumentException("A staged route has at least one point.", nameof(route));
        foreach (WorldPoint point in route) point.Validate();
        Route = [.. route];
        WaypointIndex = 0;
        IdleRemainingSeconds = 0f;
        if (State != WanderState.Unloaded) TransitionTo(WanderState.Idle);
        else Target = null;
    }

    /// <summary>Returns the actor to its own waypoints.</summary>
    public void Unstage()
    {
        Route = null;
        if (State != WanderState.Unloaded) TransitionTo(WanderState.Idle);
    }

    public WanderState TransitionTo(WanderState next)
    {
        if (!Enum.IsDefined(next)) throw new ArgumentOutOfRangeException(nameof(next));
        WanderState previous = State;
        State = next;
        if (next is WanderState.Idle or WanderState.Unloaded or WanderState.Dead)
        {
            NavigationFailures = 0;
            Target = null;
        }
        if (next is not (WanderState.Seeking or WanderState.Moving)) Halt();
        return previous;
    }

    /// <summary>Marks a hidden or unloaded actor so no stale waypoint can move it.</summary>
    public void MarkUnloaded()
    {
        TransitionTo(WanderState.Unloaded);
        IdleRemainingSeconds = 0f;
    }

    /// <summary>Readmits an actor after site/time projection restored its ordinary activity.</summary>
    public void MarkLoaded()
    {
        if (State != WanderState.Unloaded) return;
        TransitionTo(WanderState.Idle);
    }

    internal void BeginWaypoint(WorldPoint target, float course)
    {
        target.Validate();
        Target = target;
        Course = course;
        State = WanderState.Seeking;
        NavigationFailures = 0;
    }

    internal void Walk(float stride, float course)
    {
        Stride = stride;
        Course = course;
    }

    private void Halt()
    {
        Stride = 0f;
        Course = null;
    }

    internal void RecordNavigationSuccess() => NavigationFailures = 0;
    internal uint RecordNavigationFailure() => ++NavigationFailures;
    internal void CompleteWaypoint(float idleSeconds)
    {
        Target = null;
        State = WanderState.Idle;
        Halt();
        NavigationFailures = 0;
        IdleRemainingSeconds = idleSeconds;
        WaypointIndex = checked(WaypointIndex + 1);
    }

    internal void BeginBlockedRecovery(float idleSeconds)
    {
        TransitionTo(WanderState.Blocked);
        IdleRemainingSeconds = idleSeconds;
    }

    internal bool ConsumeIdle(float deltaSeconds)
    {
        if (IdleRemainingSeconds <= 0f) return true;
        IdleRemainingSeconds = MathF.Max(0f, IdleRemainingSeconds - deltaSeconds);
        return IdleRemainingSeconds <= 0f;
    }
}

/// <summary>The receipts and state transition produced by one wander update.</summary>
public sealed record WanderEvidence(
    WanderState Previous,
    WanderState Current,
    WorldPoint? Target,
    NavigationStepResult? Navigation);

/// <summary>
/// Chooses bounded ordinary waypoints and delegates every movement decision to Engine navigation.
/// This coordinator never probes, grids, or invents a local detour when Engine rejects a step.
/// </summary>
public sealed class ActorWanderCoordinator
{
    private static readonly Vector3[] Directions =
    [
        new(1f, 0f, 0f),
        new(0f, 0f, 1f),
        new(-1f, 0f, 0f),
        new(0f, 0f, -1f),
    ];

    /// <summary>How near its target an actor has reached it, in metres.</summary>
    private const float ReachedDistance = .2f;

    private readonly ActorNavigationCoordinator _navigation;

    public ActorWanderCoordinator(ActorNavigationCoordinator navigation) =>
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));

    public WanderEvidence Update(ActorState actor, WanderMemoryComponent memory, WanderPolicy policy,
        ulong simulationStep, float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(memory);
        policy.Validate();
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        WanderState previous = memory.State;
        if (actor.IsDefeated)
        {
            memory.TransitionTo(WanderState.Dead);
            return new(previous, memory.State, memory.Target, null);
        }
        if (memory.State == WanderState.Unloaded || deltaSeconds == 0f)
            return new(previous, memory.State, memory.Target, null);

        // A staged actor at its one point stands there, as it was placed.
        if (memory.Route is [var stand] && memory.State == WanderState.Idle
            && Vector3.Distance(actor.Position.ToVector(), stand.ToVector()) <= ReachedDistance)
            return new(previous, memory.State, memory.Target, null);
        if (memory.State is WanderState.Idle or WanderState.Blocked)
        {
            if (!memory.ConsumeIdle(deltaSeconds))
                return new(previous, memory.State, memory.Target, null);
            BeginWaypoint(actor, memory, NextWaypoint(actor, memory, simulationStep, policy));
        }
        else if (memory.Target is null)
        {
            BeginWaypoint(actor, memory, NextWaypoint(actor, memory, simulationStep, policy));
        }

        WorldPoint target = memory.Target!.Value;
        ActorPose before = actor.Pose;
        float course = memory.Course ?? before.HeadingYawRadians;
        float speed = policy.MovementSpeedUnitsPerSecond * memory.Pace;
        float turned = ActorHeading.Turn(before.HeadingYawRadians, course, policy.TurnRadiansPerSecond * deltaSeconds);
        float stride = Stride(memory.Stride, MathF.Cos(ActorHeading.Between(turned, course)),
            Vector3.Distance(before.Position.ToVector(), target.ToVector()) - ReachedDistance, speed, policy.EaseSeconds, deltaSeconds);
        // Still facing away from its way, the actor turns on the spot before it walks on.
        if (stride <= 0f)
        {
            memory.Walk(0f, course);
            actor.ApplyPose(new ActorPose(before.Position, turned));
            return new(previous, memory.State, memory.Target, null);
        }

        NavigationStepResult navigation = _navigation.Evaluate(actor, new ActorNavigationRequest(
            target,
            checked(speed * stride * deltaSeconds),
            policy.NavigationMaximumVisited,
            policy.NavigationMode,
            deltaSeconds));

        if (navigation.Outcome == NavigationPathOutcome.Reached)
        {
            // Navigation faces the actor the way it stepped: that is its course, and its facing turns toward it.
            ActorPose stepped = actor.Pose;
            Vector3 step = stepped.Position.ToVector() - before.Position.ToVector();
            if (step.X != 0f || step.Z != 0f) course = stepped.HeadingYawRadians;
            memory.Walk(stride, course);
            actor.ApplyPose(new ActorPose(stepped.Position,
                ActorHeading.Turn(before.HeadingYawRadians, course, policy.TurnRadiansPerSecond * deltaSeconds)));
            memory.RecordNavigationSuccess();
            if (Vector3.Distance(actor.Position.ToVector(), target.ToVector()) <= ReachedDistance)
                memory.CompleteWaypoint(policy.IdleDurationSeconds);
            else
                memory.TransitionTo(WanderState.Moving);
        }
        else if (memory.RecordNavigationFailure() >= policy.MaximumFailures)
        {
            memory.BeginBlockedRecovery(policy.IdleDurationSeconds);
        }

        return new(previous, memory.State, memory.Target, navigation);
    }

    /// <summary>Begins walking to <paramref name="target"/>, its course first the straight way there.</summary>
    private static void BeginWaypoint(ActorState actor, WanderMemoryComponent memory, WorldPoint target)
    {
        Vector3 way = target.ToVector() - actor.Position.ToVector();
        memory.BeginWaypoint(target, way.X != 0f || way.Z != 0f ? ActorHeading.Yaw(way) : actor.HeadingYawRadians);
    }

    /// <summary>
    /// The share of its pace an actor walks this step at: as much of it as the actor faces its course
    /// (<paramref name="facing"/>, the cosine between them; none while its course lies behind it), and no more than it
    /// eases up to from its last step's <paramref name="stride"/> over <paramref name="easeSeconds"/>, nor than lets it
    /// ease to a stop over those seconds in the <paramref name="remaining"/> distance until it has reached its target.
    /// It always walks at least one step's easing, so it reaches a target however close.
    /// </summary>
    internal static float Stride(float stride, float facing, float remaining, float speed, float easeSeconds, float deltaSeconds)
    {
        float wanted = Math.Clamp(facing, 0f, 1f);
        if (easeSeconds == 0f) return wanted;
        float ease = deltaSeconds / easeSeconds;
        // Slowing at a steady rate from speed v to rest over easeSeconds T covers v * T / 2, so at distance d it walks sqrt(2d / vT) of v.
        float stopping = MathF.Sqrt(2f * MathF.Max(remaining, 0f) / (speed * easeSeconds));
        return MathF.Min(wanted, MathF.Max(ease, MathF.Min(stopping, stride + ease)));
    }

    private static WorldPoint NextWaypoint(ActorState actor, WanderMemoryComponent memory,
        ulong simulationStep, WanderPolicy policy)
    {
        if (memory.Route is { } route)
        {
            // Back and forth: 0, 1, ..., n-1, n-2, ..., 1, 0, ...
            int turn = Math.Max(1, 2 * (route.Count - 1));
            int step = (int)(memory.WaypointIndex % (uint)turn);
            return route[step < route.Count ? step : turn - step];
        }
        // The direction is deterministic per actor and waypoint. The sequence is only target
        // selection; collision, projection and any usable movement remain Engine-owned.
        ulong seed = checked((ulong)actor.DurableId) + memory.WaypointIndex + simulationStep / 60UL;
        Vector3 offset = Directions[(int)(seed % (ulong)Directions.Length)] * policy.WaypointDistance;
        Vector3 origin = actor.Position.ToVector();
        return WorldPoint.From(origin + offset);
    }
}
