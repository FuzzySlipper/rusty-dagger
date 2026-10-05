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
    private readonly Func<long, PursuitTarget?>? _selectTarget;
    private readonly Func<long, bool> _questRestrained;
    private readonly ActorsState _actors;
    private readonly PursuitCoordinator<IProductFact> _pursuit;
    private readonly Func<long, DaggerfallEnemyPerceptionContext> _contextProvider;
    private readonly Action<DaggerfallSkillUse> _recordSkillUse;
    /// <summary>
    /// Fixed pursuit configuration admitted once at composition. Detection, chase, and query
    /// bounds come from module tuning and never change per actor or per update; only the target,
    /// pose, and timeline inputs vary per call. Validated here, not per frame.
    /// </summary>
    private readonly PursuitTuning _pursuitTuning;
    private readonly PursuitPerceptionOptions _perceptionOptions;
    private readonly IAttackCapabilities<IProductFact> _combat;
    private readonly Func<long, bool> _isPlayerAllied;
    private readonly Func<long, PursuitTarget?>? _selectAllyTarget;
    private readonly Func<long, ActorControlRestrictions> _controlRestrictions;
    private Func<ActorState, PursuitTarget, PerceptionReadoutResult?, DaggerfallEnemyPerceptionDecision?, double, ulong, ulong, float, bool>? _enemyMagic;
    private readonly Dictionary<long, double> _lastTargetDistances = [];
    // One Engine outer update can contain several admitted fixed steps.  Keep the action door
    // closed for an actor after either spell or physical admission so catch-up cannot turn one
    // decision into two different attacks.
    private readonly HashSet<long> _admittedActions = [];
    private readonly Func<long, PursuitPolicy> _movementPolicy;
    private readonly Func<long, bool> _canOpenDoors;
    private readonly Func<ActorState, bool>? _openBlockedDoor;
    private readonly ActorWanderCoordinator _wander;

    internal DaggerfallEnemyBehaviorModule(
        IPerceptionService perception,
        SpatialMovementSystem spatial,
        ActorNavigationCoordinator navigation,
        ActorsState actors,
        IAttackCapabilities<IProductFact> combat,
        DaggerfallEnemyBehaviorTuning tuning,
        Func<long, DaggerfallEnemyPerceptionContext> contextProvider,
        Action<DaggerfallSkillUse> recordSkillUse,
        Func<long, bool>? isPlayerAllied = null, Func<long, PursuitTarget?>? selectAllyTarget = null,
        Func<long, ActorControlRestrictions>? controlRestrictions = null,
        Func<long, PursuitPolicy>? movementPolicy = null,
        Func<long, bool>? canOpenDoors = null,
        Func<ActorState, bool>? openBlockedDoor = null, Func<long, PursuitTarget?>? selectTarget = null, Func<long, bool>? questRestrained = null)
    {
        _selectTarget = selectTarget;
        _questRestrained = questRestrained ?? (_ => false);
        _combat = combat;
        _isPlayerAllied = isPlayerAllied ?? (_ => false);
        _selectAllyTarget = selectAllyTarget;
        _controlRestrictions = controlRestrictions ?? (_ => default);
        _movementPolicy = movementPolicy ?? (_ => new PursuitPolicy());
        _canOpenDoors = canOpenDoors ?? (_ => false);
        _openBlockedDoor = openBlockedDoor;
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
        _recordSkillUse = recordSkillUse ?? throw new ArgumentNullException(nameof(recordSkillUse));
        DaggerfallEnemyBehaviorTuning admitted = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        // EnemySenses admits its own 102.4-unit / 180-degree query envelope; the Daggerfall
        // perception policy then decides within it.
        double detectionDistance = Math.Max(admitted.DetectionDistance, DaggerfallPerceptionQueryDefaults.SightRadius);
        double minimumFacingCosine = DaggerfallPerceptionQueryDefaults.MinimumFacingCosine;
        _pursuitTuning = new PursuitTuning(detectionDistance, minimumFacingCosine, admitted.ChaseSpeedUnitsPerSecond, admitted.NavigationMaximumVisited).Validate();
        _perceptionOptions = new PursuitPerceptionOptions(DaggerfallPerceptionQueryDefaults.AnyProjectionIdentity, DaggerfallPerceptionQueryDefaults.FirstPairCursor, DaggerfallPerceptionQueryDefaults.CompleteQueryPageSize).Validate();
        _pursuit = new PursuitCoordinator<IProductFact>(new DaggerfallEnemyPerceptionService(perception, Filter), spatial, navigation, combat);
        _wander = new ActorWanderCoordinator(navigation);
    }

    internal IReadOnlyDictionary<long, EnemyBehaviorEvidence> LastEvidence { get; private set; } = new Dictionary<long, EnemyBehaviorEvidence>();
    internal IReadOnlyDictionary<long, DaggerfallEnemyPerceptionDecision> LastPerception { get; private set; } = new Dictionary<long, DaggerfallEnemyPerceptionDecision>();

    /// <summary>Attaches the compiled spell decision to this module's existing actor update.</summary>
    internal void BindEnemyMagic(Func<ActorState, PursuitTarget, PerceptionReadoutResult?, DaggerfallEnemyPerceptionDecision?, double, ulong, ulong, float, bool> decide)
    {
        _enemyMagic = decide ?? throw new ArgumentNullException(nameof(decide));
    }

    internal void ClearEnemyMagic()
    {
        _lastTargetDistances.Clear();
        _admittedActions.Clear();
    }

    /// <summary>Starts the one action-admission window owned by the next Engine outer update.</summary>
    internal void BeginAdmittedUpdate() => _admittedActions.Clear();

    /// <summary>
    /// Resolves the movement medium and close-range choices from the authored mobile record.  The
    /// source behaviour names are retained in the catalog; this adapter only maps them to the Kit
    /// movement policy and never invents a second navigation implementation.
    /// </summary>
    internal static PursuitPolicy PolicyFor(long actorId,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> actors,
        DaggerfallDefinitions definitions,
        bool waterWalking = false,
        bool levitating = false)
    {
        if (!actors.TryGetValue(actorId, out DaggerfallActorDefinition? actor))
            return new PursuitPolicy();

        ActorNavigationMode mode = ActorNavigationMode.Ground;
        if (actor.MobileId is int mobileId && definitions.Mobiles.Mobiles.TryGetValue(mobileId, out DaggerfallMobileDefinition? mobile))
        {
            mode = mobile.Behaviour switch
            {
                "Aquatic" => ActorNavigationMode.Swimming,
                "Flying" or "Spectral" => ActorNavigationMode.Flying,
                _ => ActorNavigationMode.Ground,
            };
        }
        // The effect owner may grant a non-player actor the same movement protections as the
        // player. Those typed capabilities select the Engine mode; no actor-local vertical or
        // water timer is introduced here.
        if (levitating) mode = ActorNavigationMode.Flying;
        else if (waterWalking) mode = ActorNavigationMode.WaterWalking;
        // EnemyMotor exposes both retreat and strafe decisions to all authored mobile records. The
        // distance/phase gates belong to the Kit policy; this source mapping supplies the capability.
        return new PursuitPolicy(mode, CanRetreat: true, CanStrafe: true, EmitTargetLost: true);
    }

    /// <summary>Reads the donor EnemyBasics CanOpenDoors fact from the composed mobile catalog.</summary>
    internal static bool CanOpenDoors(long actorId,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> actors,
        DaggerfallDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(definitions);
        return actors.TryGetValue(actorId, out DaggerfallActorDefinition? actor)
            && actor.MobileId is int mobileId
            && definitions.Mobiles.Mobiles.TryGetValue(mobileId, out DaggerfallMobileDefinition? mobile)
            && mobile.CanOpenDoors;
    }

    /// <summary>
    /// Reads the retained disposition for the same enemy memory that the live
    /// policy mutates. The session uses this to feed the next admitted context;
    /// it does not keep a second hostility cache.
    /// </summary>
    internal bool IsPacified(long actorId) =>
        _actors.TryGet(actorId, out ActorState actor) && (Senses(actor).Pacified || _questRestrained(actorId));

    internal void Pacify(long actorId)
    {
        if (!_actors.TryGet(actorId,out var actor) || actor.IsDefeated) return;
        var memory=Senses(actor);
        memory.SetForcedHostile(false);
        memory.MagicallyPacified=true;
        memory.HasEncounteredPlayer=true;
    }

    internal void MakeHostile(long actorId)
    {
        if (!_actors.TryGet(actorId, out var actor) || actor.IsDefeated || _isPlayerAllied(actorId)) return;
        var memory = Senses(actor);
        memory.SetForcedHostile(true);
    }

    /// <summary>The enemy-senses memory the actor factory attaches to every non-player actor.</summary>
    private static DaggerfallEnemyPerceptionMemory Senses(ActorState actor) => actor.Actor.Get<DaggerfallEnemyPerceptionMemory>();

    /// <summary>Applies a local action-door trespass to live enemies in the current site.</summary>
    internal void MakeActiveEnemiesHostile()
    {
        foreach (ActorState actor in _actors.All)
        {
            if (actor.IsDefeated || actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian || _isPlayerAllied(actor.DurableId)) continue;
            DaggerfallEnemyPerceptionMemory memory = Senses(actor);
            memory.SetForcedHostile(true);
        }
    }

    /// <summary>Clears remembered detection and pacification when a site leaves the live world.</summary>
    internal void ClearPerceptionMemory()
    {
        foreach (ActorState actor in _actors.All) Senses(actor).Clear();
        _lastDecisions.Clear();
        _lastTargetDistances.Clear();
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
            if (actor.IsDefeated) _lastTargetDistances.Remove(actor.DurableId);
            // Source civilians have their own bounded wander owner. They carry the common enemy
            // sense component for actor/save compatibility, but must never acquire hostile pursuit
            // merely because they are now canonical Mechanics actors.
            if (actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian)
            {
                actor.Pursuit.TransitionTo(actor.IsDefeated ? PursuitState.Dead : PursuitState.Idle);
                _combat.InterruptPendingAttack(actor.DurableId, generation);
                _lastDecisions.Remove(actor.DurableId);
                continue;
            }
            if (!actor.IsDefeated && _controlRestrictions(actor.DurableId).Movement)
            {
                var restrictedPrevious = actor.Pursuit.TransitionTo(PursuitState.Idle);
                _combat.InterruptPendingAttack(actor.DurableId, generation);
                _lastDecisions.Remove(actor.DurableId);
                _lastTargetDistances.Remove(actor.DurableId);
                if (restrictedPrevious != PursuitState.Idle)
                    facts.Append(new EnemyBehaviorTransitionFact(actor.DurableId, ToDaggerState(restrictedPrevious), EnemyBehaviorState.Idle, generation, simulationStep));
                evidence.Add(actor.DurableId, new(actor.DurableId, EnemyBehaviorState.Idle, null, null));
                continue;
            }
            PursuitTarget? target = _selectTarget is not null ? _selectTarget(actor.DurableId) : _isPlayerAllied(actor.DurableId)
                ? _selectAllyTarget?.Invoke(actor.DurableId)
                : new PursuitTarget(DaggerfallActorIdentity.PlayerEntityId, playerPosition);
            if (target is null)
            {
                PursuitState desired = actor.IsDefeated ? PursuitState.Dead : PursuitState.Idle;
                PursuitState previousState = actor.Pursuit.TransitionTo(desired);
                _combat.InterruptPendingAttack(actor.DurableId, generation);
                _lastDecisions.Remove(actor.DurableId);
                _lastTargetDistances.Remove(actor.DurableId);
                if (previousState != desired)
                    facts.Append(new EnemyBehaviorTransitionFact(actor.DurableId, ToDaggerState(previousState), ToDaggerState(desired), generation, simulationStep));
                evidence.Add(actor.DurableId, new(actor.DurableId, ToDaggerState(desired), null, null));
                continue;
            }
            PursuitEvidence pursuit = _pursuit.Update(
                actor,
                actor.Pursuit,
                target.Value,
                _pursuitTuning,
                _perceptionOptions,
                generation,
                simulationStep,
                deltaSeconds,
                facts,
                _movementPolicy(actor.DurableId), admitAttack: false);
            if (pursuit.Current == PursuitState.Blocked
                && pursuit.Navigation is { Outcome: not NavigationPathOutcome.Reached }
                && _canOpenDoors(actor.DurableId)
                && _openBlockedDoor?.Invoke(actor) == true)
            {
                // The canonical door owner has changed state. Leave the Engine navigation receipt
                // and pursuit state intact; the next admitted step retries the same target through
                // the now-opened collision rather than introducing a local detour.
            }
            EnemyBehaviorState previous = ToDaggerState(pursuit.Previous);
            EnemyBehaviorState current = ToDaggerState(pursuit.Current);
            if (previous != current)
            {
                facts.Append(new EnemyBehaviorTransitionFact(actor.DurableId, previous, current, generation, simulationStep));
            }
            evidence.Add(actor.DurableId, new EnemyBehaviorEvidence(actor.DurableId, current, pursuit.Visibility, pursuit.Navigation));
            double targetRateOfApproach = 0d;
            PerceptionPair? observed = pursuit.Visibility?.Pairs.ToArray()
                .Where(pair => pair.Observer == checked((ulong)actor.DurableId)
                    && pair.Target == checked((ulong)target.Value.DurableId))
                .OrderBy(pair => pair.Distance)
                .Select(pair => (PerceptionPair?)pair)
                .FirstOrDefault();
            if (observed is { } pair && double.IsFinite(pair.Distance) && pair.Distance >= 0d)
            {
                if (_lastTargetDistances.TryGetValue(actor.DurableId, out double previousDistance))
                    targetRateOfApproach = Math.Max(0d, previousDistance - pair.Distance);
                _lastTargetDistances[actor.DurableId] = pair.Distance;
            }
            else
            {
                _lastTargetDistances.Remove(actor.DurableId);
            }
            bool actionAlreadyAdmitted = _admittedActions.Contains(actor.DurableId);
            bool magicAdmitted = !actionAlreadyAdmitted && _enemyMagic?.Invoke(actor, target.Value, pursuit.Visibility,
                _lastDecisions.GetValueOrDefault(actor.DurableId), targetRateOfApproach,
                generation, simulationStep, deltaSeconds) == true;
            // The donor chooses a single action: a successful spell selection owns this action
            // window and vetoes the delayed physical swing that pursuit would otherwise admit.
            // If magic declined or could not be admitted, the same attack owner receives the one
            // physical admission after the spell decision has had its chance.
            if (!actionAlreadyAdmitted && pursuit.Current == PursuitState.Attack)
            {
                if (magicAdmitted)
                {
                    _admittedActions.Add(actor.DurableId);
                    _combat.InterruptPendingAttack(actor.DurableId, generation);
                }
                else if (_combat.TryBeginEnemyAttack(actor.DurableId, target.Value.DurableId, generation,
                    simulationStep, deltaSeconds, facts))
                {
                    _admittedActions.Add(actor.DurableId);
                }
            }
            if (actor.IsDefeated)
                _lastDecisions.Remove(actor.DurableId);
            else if (_lastDecisions.TryGetValue(actor.DurableId, out DaggerfallEnemyPerceptionDecision? decision))
                perceptions[actor.DurableId] = decision;
        }
        LastEvidence = evidence;
        LastPerception = perceptions;
    }

    /// <summary>Runs one source-backed civilian activity step through Engine navigation.</summary>
    internal WanderEvidence UpdateCivilian(ActorState actor, WanderPolicy policy,
        ulong simulationStep, float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Actor.TypeId.Value != DaggerfallActorKinds.Civilian)
            throw new InvalidOperationException($"Actor {actor.DurableId} is not a Daggerfall civilian.");
        return _wander.Update(actor, actor.Wander, policy, simulationStep, deltaSeconds);
    }

    private readonly Dictionary<long, DaggerfallEnemyPerceptionDecision> _lastDecisions = [];

    private PerceptionReadoutResult Filter(long actorId, PerceptionReadoutResult receipt)
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
        // The player's stealth/language policy does not reinterpret an ally's enemy visibility.
        if (_isPlayerAllied(actorId) || receipt.Pairs.Length > 0 && receipt.Pairs.ToArray().All(pair => pair.Target != DaggerfallActorIdentity.PlayerEntityId)) return receipt;
        PerceptionPair? pair = receipt.Pairs.ToArray()
            .Where(value => value.Observer == checked((ulong)actorId) && value.Target == checked((ulong)DaggerfallActorIdentity.PlayerEntityId))
            .OrderBy(value => value.Distance)
            .Select(value => (PerceptionPair?)value)
            .FirstOrDefault();
        DaggerfallEnemyPerceptionMemory memory = Senses(actor);

        DaggerfallEnemyPerceptionContext context = _contextProvider(actorId);
        if (memory.ForcedHostile)
            context = context with { EnemyHostile = true, TargetPacified = false };
        DaggerfallEnemyPerceptionSource source = new(
            SeesThroughInvisibility: context.EnemySeesThroughInvisibility);
        DaggerfallEnemyPerceptionDecision decision = DaggerfallPerceptionPolicy.Evaluate(memory, pair,
            source, context, context.RollPercent);
        _lastDecisions[actorId] = decision;
        foreach (DaggerfallSkillUse use in decision.SkillUses)
            _recordSkillUse(use);

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
        PursuitState.Retreat => EnemyBehaviorState.Retreat,
        PursuitState.Strafe => EnemyBehaviorState.Strafe,
        PursuitState.Attack => EnemyBehaviorState.Attack,
        PursuitState.Blocked => EnemyBehaviorState.Blocked,
        PursuitState.TargetLost => EnemyBehaviorState.TargetLost,
        PursuitState.Unloaded => EnemyBehaviorState.Unloaded,
        PursuitState.Dead => EnemyBehaviorState.Dead,
        _ => throw new InvalidOperationException($"Unknown Daggerfall enemy behavior state '{value}'."),
    };
}

internal enum EnemyBehaviorState { Idle, Chase, Retreat, Strafe, Attack, Blocked, TargetLost, Unloaded, Dead }

/// <summary>Copied Engine receipts used to explain one enemy's most recent ruleset decision.</summary>
internal sealed record EnemyBehaviorEvidence(long ActorId, EnemyBehaviorState State, PerceptionReadoutResult? Visibility, NavigationStepResult? Navigation);
