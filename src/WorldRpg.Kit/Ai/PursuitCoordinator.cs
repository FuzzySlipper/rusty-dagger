using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;

namespace WorldRpg.Kit.Ai;

public enum PursuitState { Idle, Chase, Attack, Dead }

/// <summary>Entity-local memory for one actor's current pursuit decision.</summary>
public sealed class PursuitMemoryComponent
{
    public PursuitState State { get; private set; } = PursuitState.Idle;

    public PursuitState TransitionTo(PursuitState next)
    {
        PursuitState previous = State;
        State = next;
        return previous;
    }
}

/// <summary>One target supplied by a ruleset's target-selection policy.</summary>
public readonly record struct PursuitTarget(long DurableId, WorldPoint Position);

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
    PerceptionReadoutLeaseReceipt? Visibility,
    NavigationStepReceipt? Navigation);

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
        FactBuffer<TFact> facts)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(facts);
        tuning.Validate();
        perceptionOptions.Validate();
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        PerceptionReadoutLeaseReceipt? visibility = null;
        NavigationStepReceipt? navigation = null;
        PursuitState desired;
        if (actor.IsDefeated)
        {
            desired = PursuitState.Dead;
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
            desired = !visible || reach is null ? PursuitState.Idle
                : pair.Distance <= reach.Value ? PursuitState.Attack
                : PursuitState.Chase;
            if (desired == PursuitState.Chase)
            {
                navigation = _navigation.Evaluate(actor, new ActorNavigationRequest(
                    new WorldPoint(target.Position.X, actor.Position.Y, target.Position.Z),
                    checked(tuning.ChaseSpeedUnitsPerSecond * deltaSeconds),
                    tuning.NavigationMaximumVisited));
            }
        }

        PursuitState previous = memory.TransitionTo(desired);
        if (desired == PursuitState.Attack)
            _attacks.TryBeginEnemyAttack(actor.DurableId, target.DurableId, generation, simulationStep, deltaSeconds, facts);
        else
            _attacks.InterruptPendingAttack(actor.DurableId, generation);
        return new PursuitEvidence(previous, desired, visibility, navigation);
    }

    private PerceptionReadoutLeaseReceipt QueryVisibility(
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
