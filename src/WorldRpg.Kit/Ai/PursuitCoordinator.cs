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
    Search,
    TargetLost,
    Unloaded,
    Dead,
}

/// <summary>Ruleset policy for an actor's movement medium and close-range maneuvers.</summary>
public readonly record struct PursuitPolicy(
    ActorNavigationMode NavigationMode = ActorNavigationMode.Ground,
    bool CanRetreat = false,
    bool CanStrafe = false,
    float RetreatDistance = 0f,
    float StrafeDistance = 0f,
    bool EmitTargetLost = false)
{
    /// <summary>The owning ruleset's current maneuver decision; capability alone never requests retreat.</summary>
    public PursuitState Maneuver { get; init; } = PursuitState.Chase;
    public bool AllowAttack { get; init; } = true;
    /// <summary>An observation-specific attack reach, for weapons selected by distance or available ammunition.</summary>
    public double? AttackReach { get; init; }
    public float SpeedMultiplier { get; init; } = 1f;
    public int StrafeDirection { get; init; } = 1;
    /// <summary>Whether this observation updates the remembered position; null uses visibility.</summary>
    public bool? TargetObserved { get; init; }
    public bool SearchLastKnownPosition { get; init; }
    public float SearchDistance { get; init; }
    public float SearchStep { get; init; }

    /// <summary>Consecutive failed navigation probes that make pursuit explicitly blocked; the first is a transient observation.</summary>
    public uint BlockedFailures { get; init; } = 2;

    public PursuitPolicy Validate()
    {
        if (AttackReach is double reach && (!double.IsFinite(reach) || reach <= 0)) throw new ArgumentOutOfRangeException(nameof(AttackReach));
        if (Maneuver is not (PursuitState.Chase or PursuitState.Idle or PursuitState.Retreat or PursuitState.Strafe)) throw new ArgumentOutOfRangeException(nameof(Maneuver));
        if (!float.IsFinite(SpeedMultiplier) || SpeedMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(SpeedMultiplier));
        if (StrafeDirection is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(StrafeDirection));
        if (SearchLastKnownPosition && (!float.IsFinite(SearchDistance) || SearchDistance <= 0 || !float.IsFinite(SearchStep) || SearchStep <= 0))
            throw new ArgumentOutOfRangeException(nameof(SearchDistance));
        if (!Enum.IsDefined(NavigationMode)) throw new ArgumentOutOfRangeException(nameof(NavigationMode));
        if (CanRetreat && (!float.IsFinite(RetreatDistance) || RetreatDistance <= 0f)) throw new ArgumentOutOfRangeException(nameof(RetreatDistance));
        if (CanStrafe && (!float.IsFinite(StrafeDistance) || StrafeDistance <= 0f)) throw new ArgumentOutOfRangeException(nameof(StrafeDistance));
        if (BlockedFailures == 0) throw new ArgumentOutOfRangeException(nameof(BlockedFailures));
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

    public WorldPoint? LastKnownPosition { get; private set; }
    private Vector3 _lastDirection;
    private long? _target;
    private float _searchDistance;

    internal void Observe(long target, WorldPoint position)
    {
        if (_target != target) ForgetTarget();
        if (LastKnownPosition is { } prior)
        {
            Vector3 difference = position.ToVector() - prior.ToVector();
            _lastDirection = difference.LengthSquared() > .0001f ? Vector3.Normalize(difference) : Vector3.Zero;
        }
        _target = target;
        LastKnownPosition = position;
        _searchDistance = 0;
    }

    internal WorldPoint? Search(long target, WorldPoint position, double reach, PursuitPolicy policy)
    {
        if (_target != target || LastKnownPosition is not { } known) return null;
        var destination = WorldPoint.From(known.ToVector() + _lastDirection * _searchDistance);
        Vector3 difference = position.ToVector() - destination.ToVector();
        if (policy.NavigationMode is ActorNavigationMode.Ground or ActorNavigationMode.WaterWalking) difference.Y = 0;
        if (difference.Length() <= reach)
        {
            if (_lastDirection == Vector3.Zero || _searchDistance >= policy.SearchDistance) { ForgetTarget(); return null; }
            _searchDistance = Math.Min(policy.SearchDistance, _searchDistance + policy.SearchStep);
            destination = WorldPoint.From(known.ToVector() + _lastDirection * _searchDistance);
        }
        return destination;
    }

    public void ForgetTarget() { _target = null; LastKnownPosition = null; _lastDirection = default; _searchDistance = 0; }

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
    NavigationStepResult? Navigation)
{
    public bool AttackEligible { get; init; }
}

/// <summary>The current observation given to the explicitly composed maneuver policy.</summary>
public readonly record struct PursuitObservation(bool Visible, double Distance, double? Reach);

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
        bool admitAttack = true,
        Func<PursuitObservation, PursuitPolicy>? selectPolicy = null)
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
        bool attackEligible = false;
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
            if (selectPolicy is not null) policy = selectPolicy(new(visible, pair.Distance, reach)).Validate();
            reach = policy.AttackReach ?? reach;
            bool observed = policy.TargetObserved ?? visible;
            if (observed) memory.Observe(target.DurableId, target.Position);
            WorldPoint? search = !observed && policy.SearchLastKnownPosition && reach is not null
                ? memory.Search(target.DurableId, actor.Position, reach.Value, policy) : null;
            if (!observed && !policy.SearchLastKnownPosition) memory.ForgetTarget();
            attackEligible = policy.AllowAttack && observed && visible && reach is not null && pair.Distance <= reach;
            desired = reach is null ? PursuitState.Idle
                : !observed ? search is not null ? PursuitState.Search
                    : policy.EmitTargetLost && memory.State is not PursuitState.Idle and not PursuitState.TargetLost ? PursuitState.TargetLost : PursuitState.Idle
                : policy.Maneuver == PursuitState.Retreat && policy.CanRetreat ? PursuitState.Retreat
                : policy.Maneuver == PursuitState.Strafe && policy.CanStrafe ? PursuitState.Strafe
                : attackEligible ? PursuitState.Attack : policy.Maneuver == PursuitState.Idle ? PursuitState.Idle : PursuitState.Chase;
            if (desired is PursuitState.Chase or PursuitState.Search or PursuitState.Retreat or PursuitState.Strafe)
            {
                WorldPoint destination = search ?? target.Position;
                WorldPoint navigationTarget = policy.NavigationMode is ActorNavigationMode.Ground or ActorNavigationMode.WaterWalking
                    ? new WorldPoint(destination.X, actor.Position.Y, destination.Z) : destination;
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
                        Vector3 side = new Vector3(-toward.Z, 0f, toward.X) * policy.StrafeDistance * policy.StrafeDirection;
                        Vector3 radius = actor.Position.ToVector() - target.Position.ToVector();
                        radius.Y = 0;
                        Vector3 next = target.Position.ToVector() + Vector3.Normalize(radius + side) * radius.Length();
                        navigationTarget = new(next.X, actor.Position.Y, next.Z);
                    }
                }
                navigation = _navigation.Evaluate(actor, new ActorNavigationRequest(
                    navigationTarget,
                    checked(tuning.ChaseSpeedUnitsPerSecond * deltaSeconds * policy.SpeedMultiplier),
                    tuning.NavigationMaximumVisited,
                    policy.NavigationMode,
                    deltaSeconds));
                if (navigation.Value.Outcome == NavigationPathOutcome.Reached)
                {
                    memory.RecordNavigationSuccess();
                    if (desired is PursuitState.Retreat or PursuitState.Strafe)
                        actor.ApplyPose(new(actor.Position, ActorHeading.Yaw(target.Position.ToVector() - actor.Position.ToVector())));
                }
                else if (memory.RecordNavigationFailure() >= policy.BlockedFailures)
                {
                    // The first failed probe is a normal transient collision/door observation.  A
                    // repeated failure becomes an explicit blocked state while retaining Engine's
                    // receipt for diagnostics and leaving the actor pose untouched.
                    desired = PursuitState.Blocked;
                }
            }
        }

        PursuitState previous = memory.TransitionTo(desired);
        if (desired is PursuitState.Dead or PursuitState.Unloaded) memory.ForgetTarget();
        if (attackEligible)
        {
            if (admitAttack)
                _attacks.TryBeginEnemyAttack(actor.DurableId, target.DurableId, generation, simulationStep, deltaSeconds, facts);
        }
        else
            _attacks.InterruptPendingAttack(actor.DurableId, generation);
        return new PursuitEvidence(previous, desired, visibility, navigation) { AttackEligible = attackEligible };
    }

    private PerceptionReadoutResult QueryVisibility(
        ActorState actor,
        PursuitTarget target,
        PursuitTuning tuning,
        PursuitPerceptionOptions options)
    {
        Vector3 forward = ActorHeading.Forward(actor.HeadingYawRadians);
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
