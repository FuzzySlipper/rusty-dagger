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
    private readonly Func<long, DaggerfallEnemyPerceptionContext>? _contextProvider;
    private readonly Action<DaggerfallSkillUse>? _recordSkillUse;
    private readonly Action<long, DaggerfallEnemyPerceptionDecision>? _decisionSink;
    private readonly Dictionary<long, DaggerfallEnemyPerceptionMemory> _senses = [];
    /// <summary>
    /// Fixed pursuit configuration admitted once at composition. Detection, chase, and query
    /// bounds come from module tuning and never change per actor or per update; only the target,
    /// pose, and timeline inputs vary per call. Validated here, not per frame.
    /// </summary>
    private readonly PursuitTuning _pursuitTuning;
    private readonly PursuitPerceptionOptions _perceptionOptions;

    internal DaggerfallEnemyBehaviorModule(
        IPerceptionService perception,
        SpatialMovementSystem spatial,
        ActorNavigationCoordinator navigation,
        ActorsState actors,
        IAttackCapabilities<IProductFact> combat,
        DaggerfallEnemyBehaviorTuning tuning,
        Func<long, DaggerfallEnemyPerceptionContext>? contextProvider = null,
        Action<DaggerfallSkillUse>? recordSkillUse = null,
        Action<long, DaggerfallEnemyPerceptionDecision>? decisionSink = null)
    {
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _contextProvider = contextProvider;
        _recordSkillUse = recordSkillUse;
        _decisionSink = decisionSink;
        DaggerfallEnemyBehaviorTuning admitted = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        // EnemySenses admits its own 102.4-unit / 180-degree query envelope.  Keep
        // legacy callers on their authored profile bounds until they provide a
        // real Daggerfall context; the live session opts into the donor envelope.
        double detectionDistance = contextProvider is null
            ? admitted.DetectionDistance
            : Math.Max(admitted.DetectionDistance, DaggerfallPerceptionQueryDefaults.SightRadius);
        double minimumFacingCosine = contextProvider is null
            ? admitted.MinimumFacingCosine
            : DaggerfallPerceptionQueryDefaults.MinimumFacingCosine;
        _pursuitTuning = new PursuitTuning(detectionDistance, minimumFacingCosine, admitted.ChaseSpeedUnitsPerSecond, admitted.NavigationMaximumVisited).Validate();
        _perceptionOptions = new PursuitPerceptionOptions(DaggerfallPerceptionQueryDefaults.AnyProjectionIdentity, DaggerfallPerceptionQueryDefaults.FirstPairCursor, DaggerfallPerceptionQueryDefaults.CompleteQueryPageSize).Validate();
        _pursuit = new PursuitCoordinator<IProductFact>(new DaggerfallEnemyPerceptionService(perception, Filter), spatial, navigation, combat);
        foreach (ActorState actor in _actors.All)
        {
            _actors.Store.Add(actor.Actor.Entity, new PursuitMemoryComponent());
            _senses.Add(actor.DurableId, new DaggerfallEnemyPerceptionMemory());
        }
    }

    internal IReadOnlyDictionary<long, EnemyBehaviorEvidence> LastEvidence { get; private set; } = new Dictionary<long, EnemyBehaviorEvidence>();
    internal IReadOnlyDictionary<long, DaggerfallEnemyPerceptionDecision> LastPerception { get; private set; } = new Dictionary<long, DaggerfallEnemyPerceptionDecision>();

    /// <summary>
    /// Reads the retained disposition for the same enemy memory that the live
    /// policy mutates. The session uses this to feed the next admitted context;
    /// it does not keep a second hostility cache.
    /// </summary>
    internal bool IsPacified(long actorId) =>
        _senses.TryGetValue(actorId, out DaggerfallEnemyPerceptionMemory? memory) && memory.Pacified;

    /// <summary>Clears remembered detection and pacification when a site leaves the live world.</summary>
    internal void ClearPerceptionMemory()
    {
        foreach (DaggerfallEnemyPerceptionMemory memory in _senses.Values) memory.Clear();
        _lastDecisions.Clear();
        LastPerception = new Dictionary<long, DaggerfallEnemyPerceptionDecision>();
    }

    internal void Update(PlayerControlState player, ulong generation, ulong simulationStep, float deltaSeconds, FactBuffer<IProductFact> facts)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(facts);
        if (player.Position is not WorldPoint playerPosition) return;

        Dictionary<long, EnemyBehaviorEvidence> evidence = [];
        Dictionary<long, DaggerfallEnemyPerceptionDecision> perceptions = [];
        foreach (ActorState actor in _actors.All.OrderBy(value => value.DurableId))
        {
            PursuitEvidence pursuit = _pursuit.Update(
                actor,
                actor.Pursuit,
                new PursuitTarget(DaggerfallActorIdentity.PlayerEntityId, playerPosition),
                _pursuitTuning,
                _perceptionOptions,
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
            if (actor.IsDefeated)
                _lastDecisions.Remove(actor.DurableId);
            else if (_senses.ContainsKey(actor.DurableId)
                && _lastDecisions.TryGetValue(actor.DurableId, out DaggerfallEnemyPerceptionDecision? decision))
                perceptions[actor.DurableId] = decision;
        }
        LastEvidence = evidence;
        LastPerception = perceptions;
    }

    private readonly Dictionary<long, DaggerfallEnemyPerceptionDecision> _lastDecisions = [];

    private PerceptionReadoutLeaseReceipt Filter(long actorId, PerceptionReadoutLeaseReceipt receipt)
    {
        if (!_actors.TryGet(actorId, out ActorState actor))
        {
            _lastDecisions.Remove(actorId);
            return receipt;
        }
        if (actor.IsDefeated)
        {
            _lastDecisions.Remove(actorId);
            return receipt;
        }
        // A policy context is an admitted product input, not a fabricated default. Existing
        // compositions that have not yet supplied movement, noise, effect and skill state keep
        // the Engine's visibility result until the session wires the real context provider.
        if (_contextProvider is null) return receipt;
        PerceptionPair? pair = receipt.Pairs.ToArray()
            .Where(value => value.Observer == checked((ulong)actorId) && value.Target == checked((ulong)DaggerfallActorIdentity.PlayerEntityId))
            .OrderBy(value => value.Distance)
            .Select(value => (PerceptionPair?)value)
            .FirstOrDefault();
        if (!_senses.TryGetValue(actorId, out DaggerfallEnemyPerceptionMemory? memory))
            _senses.Add(actorId, memory = new DaggerfallEnemyPerceptionMemory());

        DaggerfallEnemyPerceptionContext context = _contextProvider?.Invoke(actorId)
            ?? DaggerfallEnemyPerceptionContext.Default(0);
        DaggerfallEnemyPerceptionSource source = new(
            SeesThroughInvisibility: context.EnemySeesThroughInvisibility);
        DaggerfallEnemyPerceptionDecision decision = DaggerfallPerceptionPolicy.Evaluate(memory, pair,
            source, context, context.RollPercent);
        _lastDecisions[actorId] = decision;
        foreach (DaggerfallSkillUse use in decision.SkillUses)
            _recordSkillUse?.Invoke(use);
        _decisionSink?.Invoke(actorId, decision);

        if (pair is null || decision.PursuitVisible == (pair.Value.Kind == PerceptionPairKind.Visible))
            return receipt;

        PerceptionPair[] projected = receipt.Pairs.ToArray();
        for (int index = 0; index < projected.Length; index++)
        {
            PerceptionPair value = projected[index];
            if (value.Observer != checked((ulong)actorId) || value.Target != checked((ulong)DaggerfallActorIdentity.PlayerEntityId)) continue;
            projected[index] = decision.PursuitVisible
                ? value with { Kind = PerceptionPairKind.Visible, FacingCosine = 1d }
                : value with { Kind = PerceptionPairKind.Occluded, Evidence = 0d };
        }
        return receipt with { Pairs = projected };
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
