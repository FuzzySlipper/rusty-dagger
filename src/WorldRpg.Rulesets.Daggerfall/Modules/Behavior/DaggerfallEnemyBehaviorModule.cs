using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.StateMachine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
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
    private const ulong MachineId = 0x4446_454E_454D_5931;
    private const ulong Idle = 1;
    private const ulong Chase = 2;
    private const ulong Attack = 3;
    private const ulong Dead = 4;

    private static readonly StateMachineDefinition States = new(
        MachineId,
        [Idle, Chase, Attack, Dead],
        [
            new StateMachineTransition(Idle, Chase), new StateMachineTransition(Idle, Attack), new StateMachineTransition(Idle, Dead),
            new StateMachineTransition(Chase, Idle), new StateMachineTransition(Chase, Attack), new StateMachineTransition(Chase, Dead),
            new StateMachineTransition(Attack, Idle), new StateMachineTransition(Attack, Chase), new StateMachineTransition(Attack, Dead),
        ]);

    private readonly IPerceptionService _perception;
    private readonly SpatialMovementSystem _spatial;
    private readonly ActorNavigationCoordinator _navigation;
    private readonly ActorsState _actors;
    private readonly CombatModule _combat;
    private readonly DaggerfallEnemyBehaviorTuning _tuning;
    private readonly Dictionary<long, StateMachineInstance> _instances = [];

    internal DaggerfallEnemyBehaviorModule(
        IPerceptionService perception,
        SpatialMovementSystem spatial,
        ActorNavigationCoordinator navigation,
        ActorsState actors,
        CombatModule combat,
        DaggerfallEnemyBehaviorTuning tuning)
    {
        _perception = perception ?? throw new ArgumentNullException(nameof(perception));
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        foreach (ActorState actor in _actors.All.Values)
            _instances.Add(actor.EntityId, States.CreateInstance(Idle));
    }

    internal IReadOnlyDictionary<long, EnemyBehaviorEvidence> LastEvidence { get; private set; } = new Dictionary<long, EnemyBehaviorEvidence>();

    internal void Update(PlayerControlState player, ulong generation, ulong simulationStep, float deltaSeconds, FactBuffer<IProductFact> facts)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(facts);
        if (player.Position is not WorldPoint playerPosition) return;

        Dictionary<long, EnemyBehaviorEvidence> evidence = [];
        foreach (ActorState actor in _actors.All.Values.OrderBy(value => value.EntityId))
        {
            StateMachineInstance current = _instances[actor.EntityId];
            EnemyBehaviorState desired;
            PerceptionReadoutLeaseReceipt? visibility = null;
            NavigationStepReceipt? navigation = null;

            if (actor.IsDefeated)
            {
                desired = EnemyBehaviorState.Dead;
            }
            else
            {
                visibility = QueryPlayer(actor, playerPosition);
                PerceptionPair[] pairs = visibility.Value.Pairs.ToArray()
                    .Where(value => value.Observer == checked((ulong)actor.EntityId) && value.Target == (ulong)DaggerfallActorIdentity.PlayerEntityId)
                    .OrderBy(value => value.Distance).ToArray();
                PerceptionPair pair = pairs.FirstOrDefault();
                bool visible = pairs.Length == 1 && pair.Kind == PerceptionPairKind.Visible;
                desired = !visible ? EnemyBehaviorState.Idle
                    : pair.Distance <= _tuning.AttackReach ? EnemyBehaviorState.Attack
                    : EnemyBehaviorState.Chase;
                if (desired == EnemyBehaviorState.Chase)
                {
                    navigation = _navigation.Evaluate(actor, new ActorNavigationRequest(
                        playerPosition,
                        checked(_tuning.ChaseSpeedUnitsPerSecond * deltaSeconds),
                        _tuning.NavigationMaximumVisited));
                }
            }

            EnemyBehaviorState previous = ToState(current.Current);
            if (previous != desired)
            {
                StateMachineTransitionReceipt transition = States.Transition(current, current.Current, ToValue(desired), current.Revision);
                _instances[actor.EntityId] = transition.Instance;
                facts.Append(new EnemyBehaviorTransitionFact(actor.EntityId, previous, desired, generation, simulationStep));
            }

            StateMachineInstance after = _instances[actor.EntityId];
            if (desired == EnemyBehaviorState.Attack)
            {
                _combat.ResolveExplicit(new ExplicitMeleeRequest(actor.EntityId, DaggerfallActorIdentity.PlayerEntityId, generation, simulationStep, deltaSeconds), facts);
            }
            evidence.Add(actor.EntityId, new EnemyBehaviorEvidence(actor.EntityId, ToState(after.Current), visibility, navigation));
        }
        LastEvidence = evidence;
    }

    private PerceptionReadoutLeaseReceipt QueryPlayer(ActorState actor, WorldPoint player)
    {
        Vector3 forward = new(MathF.Sin(actor.HeadingYawRadians), 0f, -MathF.Cos(actor.HeadingYawRadians));
        return _perception.QueryVisibility(new PerceptionQueryRequest(
            _spatial.Session,
            new[] { new PerceptionObserver(checked((ulong)actor.EntityId), actor.Position.ToVector(), forward, _tuning.DetectionDistance, _tuning.MinimumFacingCosine, 1d) },
            new[] { new PerceptionTarget((ulong)DaggerfallActorIdentity.PlayerEntityId, player.ToVector()) },
            ReadOnlyMemory<SpatialEntityCollider>.Empty,
            DaggerfallPerceptionQueryDefaults.AnyProjectionIdentity,
            DaggerfallPerceptionQueryDefaults.FirstPairCursor,
            DaggerfallPerceptionQueryDefaults.CompleteQueryPageSize));
    }

    private static EnemyBehaviorState ToState(ulong value) => value switch
    {
        Idle => EnemyBehaviorState.Idle,
        Chase => EnemyBehaviorState.Chase,
        Attack => EnemyBehaviorState.Attack,
        Dead => EnemyBehaviorState.Dead,
        _ => throw new InvalidOperationException($"Unknown Daggerfall enemy behavior state '{value}'."),
    };
    private static ulong ToValue(EnemyBehaviorState value) => value switch
    {
        EnemyBehaviorState.Idle => Idle,
        EnemyBehaviorState.Chase => Chase,
        EnemyBehaviorState.Attack => Attack,
        EnemyBehaviorState.Dead => Dead,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}

internal enum EnemyBehaviorState { Idle, Chase, Attack, Dead }

/// <summary>Copied Engine receipts used to explain one enemy's most recent ruleset decision.</summary>
internal sealed record EnemyBehaviorEvidence(long ActorId, EnemyBehaviorState State, PerceptionReadoutLeaseReceipt? Visibility, NavigationStepReceipt? Navigation);
