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
/// <remarks>The Kit owns no movement numbers; the ruleset supplies speed, waypoint and idle values.</remarks>
public readonly record struct WanderPolicy(
    float MovementSpeedUnitsPerSecond,
    float WaypointDistance,
    float IdleDurationSeconds,
    uint NavigationMaximumVisited,
    uint MaximumFailures = 2,
    ActorNavigationMode NavigationMode = ActorNavigationMode.Ground)
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

    internal void BeginWaypoint(WorldPoint target)
    {
        target.Validate();
        Target = target;
        State = WanderState.Seeking;
        NavigationFailures = 0;
    }

    internal void RecordNavigationSuccess() => NavigationFailures = 0;
    internal uint RecordNavigationFailure() => ++NavigationFailures;
    internal void CompleteWaypoint(float idleSeconds)
    {
        Target = null;
        State = WanderState.Idle;
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

        if (memory.State is WanderState.Idle or WanderState.Blocked)
        {
            if (!memory.ConsumeIdle(deltaSeconds))
                return new(previous, memory.State, memory.Target, null);
            memory.BeginWaypoint(NextWaypoint(actor, memory, simulationStep, policy));
        }
        else if (memory.Target is null)
        {
            memory.BeginWaypoint(NextWaypoint(actor, memory, simulationStep, policy));
        }

        WorldPoint target = memory.Target!.Value;
        NavigationStepResult navigation = _navigation.Evaluate(actor, new ActorNavigationRequest(
            target,
            checked(policy.MovementSpeedUnitsPerSecond * deltaSeconds),
            policy.NavigationMaximumVisited,
            policy.NavigationMode,
            deltaSeconds));

        if (navigation.Outcome == NavigationPathOutcome.Reached)
        {
            memory.RecordNavigationSuccess();
            if (Vector3.DistanceSquared(actor.Position.ToVector(), target.ToVector()) <= .04f)
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

    private static WorldPoint NextWaypoint(ActorState actor, WanderMemoryComponent memory,
        ulong simulationStep, WanderPolicy policy)
    {
        // The direction is deterministic per actor and waypoint. The sequence is only target
        // selection; collision, projection and any usable movement remain Engine-owned.
        ulong seed = checked((ulong)actor.DurableId) + memory.WaypointIndex + simulationStep / 60UL;
        Vector3 offset = Directions[(int)(seed % (ulong)Directions.Length)] * policy.WaypointDistance;
        Vector3 origin = actor.Position.ToVector();
        return WorldPoint.From(origin + offset);
    }
}
