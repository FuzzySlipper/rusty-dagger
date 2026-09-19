using Rusty.Engine;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

/// <summary>
/// The deliberately small live enemy policy for the Daggerfall ruleset.
/// It consumes Engine perception and navigation evidence, while the Engine
/// remains the authority for visibility, path projection, and movement
/// admission.  The old donor PatrolService is intentionally not ported:
/// there is no authored patrol route or wandering policy in this ruleset.
/// </summary>
internal sealed class DaggerfallEnemyBehaviorModule
{
    private readonly ActorsState _actors;
    private readonly PursuitCoordinator<IProductFact> _pursuit;
    private readonly DaggerfallEnemyBehaviorTuning _tuning;

    internal DaggerfallEnemyBehaviorModule(
        IPerceptionService perception,
        SpatialMovementSystem spatial,
        ActorNavigationCoordinator navigation,
        ActorsState actors,
        IAttackCapabilities<IProductFact> combat,
        DaggerfallEnemyBehaviorTuning tuning)
    {
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _pursuit = new PursuitCoordinator<IProductFact>(perception, spatial, navigation, combat);
        foreach (ActorState actor in _actors.All)
            _actors.Store.Add(actor.Actor.Entity, new PursuitMemoryComponent());
    }

    internal IReadOnlyDictionary<long, EnemyBehaviorEvidence> LastEvidence { get; private set; } = new Dictionary<long, EnemyBehaviorEvidence>();

    internal void Update(PlayerControlState player, ulong generation, ulong simulationStep, float deltaSeconds, FactBuffer<IProductFact> facts)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(facts);
        if (player.Position is not WorldPoint playerPosition) return;

        Dictionary<long, EnemyBehaviorEvidence> evidence = [];
        foreach (ActorState actor in _actors.All.OrderBy(value => value.DurableId))
        {
            PursuitEvidence pursuit = _pursuit.Update(
                actor,
                actor.Pursuit,
                new PursuitTarget(DaggerfallActorIdentity.PlayerEntityId, playerPosition),
                new PursuitTuning(_tuning.DetectionDistance, _tuning.MinimumFacingCosine, _tuning.ChaseSpeedUnitsPerSecond, _tuning.NavigationMaximumVisited),
                new PursuitPerceptionOptions(DaggerfallPerceptionQueryDefaults.AnyProjectionIdentity, DaggerfallPerceptionQueryDefaults.FirstPairCursor, DaggerfallPerceptionQueryDefaults.CompleteQueryPageSize),
                generation,
                simulationStep,
                deltaSeconds,
                facts);
            EnemyBehaviorState previous = ToDaggerState(pursuit.Previous);
            EnemyBehaviorState current = ToDaggerState(pursuit.Current);
            if (previous != current)
            {
                facts.Append(new EnemyBehaviorTransitionFact(actor.DurableId, previous, current, generation, simulationStep));
            }
            evidence.Add(actor.DurableId, new EnemyBehaviorEvidence(actor.DurableId, current, pursuit.Visibility, pursuit.Navigation));
        }
        LastEvidence = evidence;
    }

    private static EnemyBehaviorState ToDaggerState(PursuitState value) => value switch
    {
        PursuitState.Idle => EnemyBehaviorState.Idle,
        PursuitState.Chase => EnemyBehaviorState.Chase,
        PursuitState.Attack => EnemyBehaviorState.Attack,
        PursuitState.Dead => EnemyBehaviorState.Dead,
        _ => throw new InvalidOperationException($"Unknown Daggerfall enemy behavior state '{value}'."),
    };
}

internal enum EnemyBehaviorState { Idle, Chase, Attack, Dead }

/// <summary>Copied Engine receipts used to explain one enemy's most recent ruleset decision.</summary>
internal sealed record EnemyBehaviorEvidence(long ActorId, EnemyBehaviorState State, PerceptionReadoutLeaseReceipt? Visibility, NavigationStepReceipt? Navigation);
