using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;

namespace WorldRpg.Kit.Ai;

public enum PursuitState
{
    Idle,
    Chase,
    Retreat,
    Strafe,
    Attack,
    Blocked,
    TargetLost,
    Unloaded,
    Dead,
}

/// <summary>Ruleset policy for an actor's movement medium and close-range maneuvers.</summary>
public readonly record struct PursuitPolicy(
    ActorNavigationMode NavigationMode = ActorNavigationMode.Ground,
    bool CanRetreat = false,
    bool CanStrafe = false,
    float RetreatDistance = 2.5f,
    float StrafeDistance = 1.5f,
    bool EmitTargetLost = false)
{
    public PursuitPolicy Validate()
    {
        if (!Enum.IsDefined(NavigationMode)) throw new ArgumentOutOfRangeException(nameof(NavigationMode));
        if (!float.IsFinite(RetreatDistance) || RetreatDistance <= 0f) throw new ArgumentOutOfRangeException(nameof(RetreatDistance));
        if (!float.IsFinite(StrafeDistance) || StrafeDistance <= 0f) throw new ArgumentOutOfRangeException(nameof(StrafeDistance));
        return this;
    }
}

/// <summary>Entity-local memory for one actor's current pursuit decision.</summary>
public sealed class PursuitMemoryComponent
{
    public PursuitState State { get; private set; } = PursuitState.Idle;
    /// <summary>Consecutive Engine navigation failures for the current target.</summary>
    public uint NavigationFailures { get; private set; }

    public PursuitState TransitionTo(PursuitState next)
    {
        PursuitState previous = State;
        State = next;
        if (next is PursuitState.Idle or PursuitState.Attack or PursuitState.TargetLost or PursuitState.Unloaded or PursuitState.Dead)
            NavigationFailures = 0;
        return previous;
    }

    internal uint RecordNavigationFailure() => ++NavigationFailures;
    internal void RecordNavigationSuccess() => NavigationFailures = 0;
}

/// <summary>One target supplied by a ruleset's target-selection policy.</summary>
public readonly record struct PursuitTarget(long DurableId, WorldPoint Position, bool IsLoaded = true)
{
    public PursuitTarget Validate()
    {
        if (DurableId <= 0) throw new ArgumentOutOfRangeException(nameof(DurableId));
        Position.Validate();
        return this;
    }
}

/// <summary>Ruleset-selected perception and navigation limits for one pursuit update.</summary>
public readonly record struct PursuitTuning(
    double DetectionDistance,
    double MinimumFacingCosine,
    float ChaseSpeedUnitsPerSecond,
    uint NavigationMaximumVisited)
{
    public PursuitTuning Validate()
    {
        if (!double.IsFinite(DetectionDistance) || DetectionDistance <= 0d) throw new ArgumentOutOfRangeException(nameof(DetectionDistance));
        if (!double.IsFinite(MinimumFacingCosine) || MinimumFacingCosine is < -1d or > 1d) throw new ArgumentOutOfRangeException(nameof(MinimumFacingCosine));
        if (!float.IsFinite(ChaseSpeedUnitsPerSecond) || ChaseSpeedUnitsPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(ChaseSpeedUnitsPerSecond));
        if (NavigationMaximumVisited == 0) throw new ArgumentOutOfRangeException(nameof(NavigationMaximumVisited));
        return this;
    }
}

/// <summary>Engine query handles selected by the owning product composition.</summary>
public readonly record struct PursuitPerceptionOptions(ulong ProjectionIdentity, uint Cursor, uint PageSize)
{
    public PursuitPerceptionOptions Validate()
    {
        if (PageSize == 0) throw new ArgumentOutOfRangeException(nameof(PageSize));
        return this;
    }
}

/// <summary>Copied observations from one actor's pursuit update.</summary>
public sealed record PursuitEvidence(
    PursuitState Previous,
    PursuitState Current,
    PerceptionReadoutResult? Visibility,
    NavigationStepResult? Navigation);

/// <summary>
/// Coordinates the reusable pursuit loop: Engine visibility, a state decision,
/// optional Engine navigation, and a named attack capability. Rulesets choose
/// targets, tuning, and the meaning they publish from a transition.
/// </summary>
public sealed class PursuitCoordinator<TFact> where TFact : IWorldRpgFact
{
    private readonly IPerceptionService _perception;
    private readonly SpatialMovementSystem _spatial;
    private readonly ActorNavigationCoordinator _navigation;
    private readonly IAttackCapabilities<TFact> _attacks;

    public PursuitCoordinator(
        IPerceptionService perception,
        SpatialMovementSystem spatial,
        ActorNavigationCoordinator navigation,
        IAttackCapabilities<TFact> attacks)
    {
        _perception = perception ?? throw new ArgumentNullException(nameof(perception));
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _attacks = attacks ?? throw new ArgumentNullException(nameof(attacks));
    }

    public PursuitEvidence Update(
        ActorState actor,
        PursuitMemoryComponent memory,
        PursuitTarget target,
        PursuitTuning tuning,
        PursuitPerceptionOptions perceptionOptions,
        ulong generation,
        ulong simulationStep,
        float deltaSeconds,
        FactBuffer<TFact> facts,
        bool admitAttack = true)
        => Update(actor, memory, target, tuning, perceptionOptions, generation, simulationStep, deltaSeconds, facts, new PursuitPolicy(), admitAttack);

    /// <summary>
    /// Runs one policy-selected pursuit update. Visibility, path admission, and pose movement remain
    /// Engine-backed; this method only chooses the ruleset's next intent from their receipts.
    /// </summary>
    public PursuitEvidence Update(
        ActorState actor,
        PursuitMemoryComponent memory,
        PursuitTarget target,
        PursuitTuning tuning,
        PursuitPerceptionOptions perceptionOptions,
        ulong generation,
        ulong simulationStep,
        float deltaSeconds,
        FactBuffer<TFact> facts,
        PursuitPolicy policy,
        bool admitAttack = true)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(facts);
        tuning.Validate();
        perceptionOptions.Validate();
        target.Validate();
        policy.Validate();
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        PerceptionReadoutResult? visibility = null;
        NavigationStepResult? navigation = null;
        PursuitState desired;
        if (actor.IsDefeated)
        {
            desired = PursuitState.Dead;
        }
        else if (!target.IsLoaded)
        {
            // A target can leave the admitted site while its durable identity is still known.  Keep
            // that distinction visible to the owning policy instead of chasing a stale pose.
            desired = PursuitState.Unloaded;
        }
        else
        {
            visibility = QueryVisibility(actor, target, tuning, perceptionOptions);
            PerceptionPair[] pairs = visibility.Value.Pairs.ToArray()
                .Where(value => value.Observer == checked((ulong)actor.DurableId) && value.Target == checked((ulong)target.DurableId))
                .OrderBy(value => value.Distance)
                .ToArray();
            PerceptionPair pair = pairs.FirstOrDefault();
            bool visible = pairs.Length == 1 && pair.Kind == PerceptionPairKind.Visible;
            double? reach = _attacks.ReachOf(actor.DurableId);
            desired = !visible || reach is null
                ? (!visible && policy.EmitTargetLost && memory.State is not PursuitState.Idle and not PursuitState.TargetLost
                    ? PursuitState.TargetLost : PursuitState.Idle)
                : pair.Distance <= reach.Value ? PursuitState.Attack
                : SelectManeuver(actor.DurableId, pair.Distance, reach.Value, policy, simulationStep);
            if (desired is PursuitState.Chase or PursuitState.Retreat or PursuitState.Strafe)
            {
                WorldPoint navigationTarget = policy.NavigationMode is ActorNavigationMode.Ground or ActorNavigationMode.WaterWalking
                    ? new WorldPoint(target.Position.X, actor.Position.Y, target.Position.Z)
                    : target.Position;
                if (desired == PursuitState.Retreat)
                {
                    Vector3 away = actor.Position.ToVector() - target.Position.ToVector();
                    away.Y = 0f;
                    if (away.LengthSquared() > .0001f)
                    {
                        away = Vector3.Normalize(away) * policy.RetreatDistance;
                        navigationTarget = WorldPoint.From(actor.Position.ToVector() + away);
                    }
                }
                else if (desired == PursuitState.Strafe)
                {
                    Vector3 toward = target.Position.ToVector() - actor.Position.ToVector();
                    toward.Y = 0f;
                    if (toward.LengthSquared() > .0001f)
                    {
                        toward = Vector3.Normalize(toward);
                        Vector3 side = new Vector3(-toward.Z, 0f, toward.X) * policy.StrafeDistance;
                        navigationTarget = WorldPoint.From(actor.Position.ToVector() + side);
                    }
                }
                navigation = _navigation.Evaluate(actor, new ActorNavigationRequest(
                    navigationTarget,
                    checked(tuning.ChaseSpeedUnitsPerSecond * deltaSeconds),
                    tuning.NavigationMaximumVisited,
                    policy.NavigationMode,
                    deltaSeconds));
                if (navigation.Value.Outcome == NavigationPathOutcome.Reached)
                {
                    memory.RecordNavigationSuccess();
                }
                else if (memory.RecordNavigationFailure() >= 2)
                {
                    // The first failed probe is a normal transient collision/door observation.  A
                    // repeated failure becomes an explicit blocked state while retaining Engine's
                    // receipt for diagnostics and leaving the actor pose untouched.
                    desired = PursuitState.Blocked;
                }
            }
        }

        PursuitState previous = memory.TransitionTo(desired);
        if (desired == PursuitState.Attack)
        {
            if (admitAttack)
                _attacks.TryBeginEnemyAttack(actor.DurableId, target.DurableId, generation, simulationStep, deltaSeconds, facts);
        }
        else
            _attacks.InterruptPendingAttack(actor.DurableId, generation);
        return new PursuitEvidence(previous, desired, visibility, navigation);
    }

    private static PursuitState SelectManeuver(long actorId, double distance,
        double reach, PursuitPolicy policy, ulong simulationStep)
    {
        if (policy.CanRetreat && distance <= reach + policy.RetreatDistance)
            return PursuitState.Retreat;
        if (policy.CanStrafe && distance <= reach + (policy.StrafeDistance * 4f)
            // A deterministic phase gives the actor a real strafe choice without creating a second
            // random source or making movement depend on frame ordering.
            && ((simulationStep + checked((ulong)Math.Abs(actorId))) / 15UL) % 4UL == 0UL)
            return PursuitState.Strafe;
        return PursuitState.Chase;
    }

    private PerceptionReadoutResult QueryVisibility(
        ActorState actor,
        PursuitTarget target,
        PursuitTuning tuning,
        PursuitPerceptionOptions options)
    {
        Vector3 forward = new(MathF.Sin(actor.HeadingYawRadians), 0f, -MathF.Cos(actor.HeadingYawRadians));
        return _perception.QueryVisibility(new PerceptionQueryRequest(
            _spatial.Session,
            new PerceptionObserver[] { new(checked((ulong)actor.DurableId), actor.Position.ToVector(), forward, tuning.DetectionDistance, tuning.MinimumFacingCosine, 1d) },
            new PerceptionTarget[] { new(checked((ulong)target.DurableId), target.Position.ToVector()) },
            ReadOnlyMemory<SpatialEntityCollider>.Empty,
            options.ProjectionIdentity,
            options.Cursor,
            options.PageSize));
    }
}
