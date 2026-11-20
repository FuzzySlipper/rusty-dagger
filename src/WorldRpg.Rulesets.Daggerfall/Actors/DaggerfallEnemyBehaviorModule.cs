using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
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
/// The live enemy behaviour for the Daggerfall ruleset: each step it selects an enemy's target, runs pursuit over
/// Engine perception and navigation, and admits the one action of the step through casting and combat. How an enemy
/// moves and which action it takes is its tactic's (<see cref="IDaggerfallEnemyTactic"/>, by its mobile record), asked
/// with an observation this module assembles. The Engine remains the authority for visibility, path projection and
/// movement admission. The old donor PatrolService is intentionally not ported: there is no authored patrol route or
/// wandering policy in this ruleset.
/// </summary>
internal sealed class DaggerfallEnemyBehaviorModule
{
    private readonly ActorReactions? _reactions;
    private readonly Func<long, PursuitTarget?> _selectTarget;
    private readonly Func<long, bool> _questRestrained;
    private readonly ActorsState _actors;
    private readonly PursuitCoordinator<IProductFact> _pursuit;
    private readonly Func<long, long, DaggerfallEnemyPerceptionContext?> _contextProvider;
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
    private readonly Func<long, ActorControlRestrictions> _controlRestrictions;
    private DaggerfallEnemyTactics? _tactics;
    private Func<DaggerfallEnemySpellAttempt, ulong, ulong, List<DaggerfallDecisionGate>?, bool>? _cast;
    private Func<ActorState, PursuitTarget, bool>? _laneClear;
    private readonly Dictionary<long, double> _lastTargetDistances = [];
    // One Engine outer update can contain several admitted fixed steps.  Keep the action door
    // closed for an actor after either spell or physical admission so catch-up cannot turn one
    // decision into two different attacks.
    private readonly HashSet<long> _admittedActions = [];
    private readonly Func<long, PursuitPolicy> _movementPolicy;
    private readonly Func<long, bool> _canOpenDoors;
    private readonly Func<ActorState, bool>? _openBlockedDoor;
    private readonly ActorWanderCoordinator _wander;
    private readonly Func<long, long, DaggerfallEnemyMotorContext> _motorContext;
    private readonly Func<long, long, double, (double Reach, bool Ranged)?>? _attackAtDistance;
    private readonly DaggerfallPursuitManeuverTuning _maneuvers;

    internal DaggerfallEnemyBehaviorModule(
        IPerceptionService perception,
        SpatialMovementSystem spatial,
        ActorNavigationCoordinator navigation,
        ActorsState actors,
        IAttackCapabilities<IProductFact> combat,
        DaggerfallEnemyBehaviorTuning tuning,
        Func<long, long, DaggerfallEnemyPerceptionContext?> contextProvider,
        Action<DaggerfallSkillUse> recordSkillUse,
        Func<long, long, DaggerfallEnemyMotorContext> motorContext, Func<long, PursuitTarget?> selectTarget,
        Func<long, bool>? isPlayerAllied = null,
        Func<long, ActorControlRestrictions>? controlRestrictions = null,
        Func<long, PursuitPolicy>? movementPolicy = null,
        Func<long, bool>? canOpenDoors = null,
        Func<ActorState, bool>? openBlockedDoor = null, Func<long, bool>? questRestrained = null, Func<long, long, double, (double Reach, bool Ranged)?>? attackAtDistance = null,
        ActorReactions? reactions = null)
    {
        _reactions = reactions;
        _motorContext = motorContext;
        _attackAtDistance = attackAtDistance;
        _maneuvers = tuning.Maneuvers;
        _selectTarget = selectTarget ?? throw new ArgumentNullException(nameof(selectTarget));
        _questRestrained = questRestrained ?? (_ => false);
        _combat = combat;
        _isPlayerAllied = isPlayerAllied ?? (_ => false);
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
        _perceptionOptions = new PursuitPerceptionOptions(DaggerfallPerceptionPaging.FirstPairCursor, DaggerfallPerceptionPaging.CompleteQueryPageSize).Validate();
        _pursuit = new PursuitCoordinator<IProductFact>(new DaggerfallEnemyPerceptionService(perception, Filter), spatial, navigation, combat);
        _wander = new ActorWanderCoordinator(navigation);
    }

    internal IReadOnlyDictionary<long, EnemyBehaviorEvidence> LastEvidence { get; private set; } = new Dictionary<long, EnemyBehaviorEvidence>();
    internal IReadOnlyDictionary<long, DaggerfallEnemyPerceptionDecision> LastPerception { get; private set; } = new Dictionary<long, DaggerfallEnemyPerceptionDecision>();
    /// <summary>The senses each creature's last perception decision was judged with: its sight and hearing radii and modifiers.</summary>
    internal IReadOnlyDictionary<long, DaggerfallEnemyPerceptionSource> LastSenses => _lastSources;
    /// <summary>How far the perception query looks for a creature's target; the policy decides within it.</summary>
    internal double DetectionDistance => _pursuitTuning.DetectionDistance;
    private readonly Dictionary<long, DaggerfallEnemyPerceptionSource> _lastSources = [];

    /// <summary>The developer's run-only overrides, read at this module's own inputs.</summary>
    internal DaggerfallEnemyOverrides Overrides { get; } = new();
    /// <summary>The opt-in decision trace of one enemy.</summary>
    internal DaggerfallEnemyDecisionTrace Trace { get; } = new();
    /// <summary>The traced actor's gates while its decision is being made; null for every other actor.</summary>
    private List<DaggerfallDecisionGate>? _gates;
    /// <summary>What the traced actor's motor last weighed, for the attack gate's reason.</summary>
    private (PursuitObservation Observation, PursuitPolicy Policy)? _tracedMotor;
    /// <summary>The target whose perception pair the current actor's pursuit is reading, for the senses filter.</summary>
    private long? _sensing;

    /// <summary>
    /// Attaches the decisions, once casting exists: the tactics that choose for each enemy, the release of a chosen
    /// spell (whether it took the action) and the missile lane an observation reports.
    /// </summary>
    internal void BindDecisions(DaggerfallEnemyTactics tactics, Func<DaggerfallEnemySpellAttempt, ulong, ulong, List<DaggerfallDecisionGate>?, bool> cast,
        Func<ActorState, PursuitTarget, bool> laneClear)
    {
        _tactics = tactics ?? throw new ArgumentNullException(nameof(tactics));
        _cast = cast ?? throw new ArgumentNullException(nameof(cast));
        _laneClear = laneClear ?? throw new ArgumentNullException(nameof(laneClear));
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
        DaggerfallPursuitManeuverTuning maneuvers,
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
        return new PursuitPolicy(mode, CanRetreat: true, CanStrafe: true, maneuvers.RetreatDistance, maneuvers.StrafeDistance, EmitTargetLost: true);

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
    /// <summary>How an actor moves, which is how a blow throws it.</summary>
    internal ActorNavigationMode NavigationMode(long actorId) => _movementPolicy(actorId).NavigationMode;

    internal bool IsPacified(long actorId) => _actors.TryGet(actorId, out ActorState actor) && !Overrides.IsHostile(actorId)
        && (Senses(actor).Pacified || _questRestrained(actorId));

    /// <summary>
    /// A blow tells an actor who struck it: it turns on its attacker, even from behind. It knows at once, since an
    /// actor that can't move skips its senses, and being struck is already an encounter no language can pacify.
    /// </summary>
    internal void Provoke(long actorId, long gameMinute)
    {
        if (!_actors.TryGet(actorId, out ActorState actor)) return;
        DaggerfallEnemyPerceptionMemory memory = Senses(actor);
        memory.ProvokedUntilMinute = gameMinute + 1;
        memory.Detected = memory.HasEncounteredPlayer = true;
    }

    /// <summary>Whether an actor hasn't noticed the player. Its pursuit state can't say: paralysis idles an actor that knows.</summary>
    internal bool Unaware(long actorId) => _actors.TryGet(actorId, out ActorState actor) && !Senses(actor).Detected;

    internal void Pacify(long actorId)
    {
        if (!_actors.TryGet(actorId,out var actor) || actor.IsDefeated) return;
        var memory=Senses(actor);
        memory.SetForcedHostile(false);
        memory.MagicallyPacified=true;
        memory.HasEncounteredPlayer=true;
    }

    internal void MakeHostile(long actorId, bool includePlayerAllies = false)
    {
        if (!_actors.TryGet(actorId, out var actor) || actor.IsDefeated || !includePlayerAllies && _isPlayerAllied(actorId)) return;
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
        foreach (ActorState actor in _actors.All)
        {
            Senses(actor).Clear();
            actor.Pursuit.ForgetTarget();
        }
        _lastDecisions.Clear();
        _lastSources.Clear();
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
                _lastSources.Remove(actor.DurableId);
                continue;
            }
            _gates = Trace.Open(actor.DurableId);
            _tracedMotor = null;
            EnemyBehaviorState decided = UpdateEnemy(actor, playerPosition, generation, simulationStep, deltaSeconds, facts, evidence, perceptions);
            Trace.Close(_gates, decided, generation, simulationStep);
            _gates = null;
        }
        LastEvidence = evidence;
        LastPerception = perceptions;
    }

    /// <summary>One enemy's decision this step: control, target, pursuit with its motor, then the single action.</summary>
    private EnemyBehaviorState UpdateEnemy(ActorState actor, WorldPoint playerPosition, ulong generation, ulong simulationStep,
        float deltaSeconds, FactBuffer<IProductFact> facts, Dictionary<long, EnemyBehaviorEvidence> evidence,
        Dictionary<long, DaggerfallEnemyPerceptionDecision> perceptions)
    {
        bool reacting = _reactions?.Holds(actor.DurableId) == true;
        if (!actor.IsDefeated && (Overrides.Frozen || _controlRestrictions(actor.DurableId).Movement || reacting))
        {
            _gates?.Add(new("control", "movement", DaggerfallGateOutcome.Refused,
                Overrides.Frozen ? "ai.freeze" : reacting ? "reacting" : "restricted"));
            var restrictedPrevious = actor.Pursuit.TransitionTo(PursuitState.Idle);
            _combat.InterruptPendingAttack(actor.DurableId, generation);
            _lastDecisions.Remove(actor.DurableId);
            _lastSources.Remove(actor.DurableId);
            _lastTargetDistances.Remove(actor.DurableId);
            if (restrictedPrevious != PursuitState.Idle)
                facts.Append(new EnemyBehaviorTransitionFact(actor.DurableId, ToDaggerState(restrictedPrevious), EnemyBehaviorState.Idle, generation, simulationStep));
            evidence.Add(actor.DurableId, new(actor.DurableId, EnemyBehaviorState.Idle, null, null));
            return EnemyBehaviorState.Idle;
        }
        PursuitTarget? target = SelectTarget(actor.DurableId, playerPosition);
        if (target is null)
        {
            PursuitState desired = actor.IsDefeated ? PursuitState.Dead : PursuitState.Idle;
            PursuitState previousState = actor.Pursuit.TransitionTo(desired);
            _combat.InterruptPendingAttack(actor.DurableId, generation);
            _lastDecisions.Remove(actor.DurableId);
            _lastSources.Remove(actor.DurableId);
            _lastTargetDistances.Remove(actor.DurableId);
            if (previousState != desired)
                facts.Append(new EnemyBehaviorTransitionFact(actor.DurableId, ToDaggerState(previousState), ToDaggerState(desired), generation, simulationStep));
            evidence.Add(actor.DurableId, new(actor.DurableId, ToDaggerState(desired), null, null));
            return ToDaggerState(desired);
        }
        IDaggerfallEnemyTactic tactic = (_tactics ?? throw new InvalidOperationException("Enemy decisions are not bound.")).For(actor.DurableId);
        _sensing = target.Value.DurableId;
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
            _movementPolicy(actor.DurableId), admitAttack: false,
            selectPolicy: observation => SelectMotorPolicy(actor, tactic, target.Value, observation, generation, simulationStep, deltaSeconds));
        _sensing = null;
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
        evidence.Add(actor.DurableId, new EnemyBehaviorEvidence(actor.DurableId, current, pursuit.Visibility, pursuit.Navigation) { Target = target });
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
        DaggerfallEnemyActionChoice choice = tactic.Act(new(actor, target.Value, pursuit.Visibility, SensesOf(actor.DurableId, target.Value.DurableId),
            targetRateOfApproach, !actionAlreadyAdmitted, Lane(actor, target.Value), generation, simulationStep, deltaSeconds), _gates);
        bool magicAdmitted = !actionAlreadyAdmitted && choice.Spell is { } spell && _cast!(spell, generation, simulationStep, _gates);
        bool struck = false;
        // The donor chooses a single action: an admitted spell owns this update's action window,
        // whether or not a strike was chosen or could be, and vetoes the delayed physical swing.
        // If magic declined or could not be admitted, the same attack owner receives the one
        // physical admission after the spell decision has had its chance.
        if (magicAdmitted)
        {
            _admittedActions.Add(actor.DurableId);
            _combat.InterruptPendingAttack(actor.DurableId, generation);
        }
        if (!choice.Strike)
            _gates?.Add(new("attack", "physical", DaggerfallGateOutcome.Refused, "not-chosen"));
        else if (!pursuit.AttackEligible)
            _gates?.Add(new("attack", "physical", DaggerfallGateOutcome.Refused, IneligibleReason(), MotorValues()));
        else if (actionAlreadyAdmitted)
            _gates?.Add(new("attack", "physical", DaggerfallGateOutcome.Refused, "action-taken-this-update"));
        else if (magicAdmitted)
            _gates?.Add(new("attack", "physical", DaggerfallGateOutcome.Refused, "spell-took-action"));
        // An enemy that is still swinging or recovering simply waits: its pacing is not a refused
        // request, and reporting it as one would put the enemy's cooldown on the player's line.
        else if (!_combat.IsReady(actor.DurableId, generation, simulationStep))
            _gates?.Add(new("attack", "physical", DaggerfallGateOutcome.Refused, actor.Attack.Pending is null ? "recovering" : "swinging",
                new DaggerfallGateValue("readyAtStep", actor.Attack.ReadyAtStep), new DaggerfallGateValue("step", simulationStep)));
        else if (_combat.TryBeginEnemyAttack(actor.DurableId, target.Value.DurableId, generation, simulationStep, deltaSeconds, facts))
        {
            _gates?.Add(new("attack", "physical", DaggerfallGateOutcome.Passed, "began", MotorValues()));
            _admittedActions.Add(actor.DurableId);
            struck = true;
        }
        else
            _gates?.Add(new("attack", "physical", DaggerfallGateOutcome.Refused, "refused-by-combat"));
        tactic.Admitted(actor, choice, magicAdmitted ? DaggerfallEnemyAdmission.Cast : struck ? DaggerfallEnemyAdmission.Struck : DaggerfallEnemyAdmission.None);
        if (actor.IsDefeated)
        {
            _lastDecisions.Remove(actor.DurableId);
            _lastSources.Remove(actor.DurableId);
        }
        else if (_lastDecisions.TryGetValue(actor.DurableId, out DaggerfallEnemyPerceptionDecision? decision))
            perceptions[actor.DurableId] = decision;
        return current;
    }

    /// <summary>
    /// Who an enemy pursues: none while passive; the developer's chosen target; the player when forced hostile; else
    /// the team rule's choice (the player, an ally's foe, or a foe across a quest's infighting).
    /// </summary>
    private PursuitTarget? SelectTarget(long actorId, WorldPoint playerPosition)
    {
        PursuitTarget player = new(DaggerfallActorIdentity.PlayerEntityId, playerPosition);
        if (Overrides.IsPassive(actorId))
        {
            _gates?.Add(new("target", "select", DaggerfallGateOutcome.Refused, "passive"));
            return null;
        }
        if (Overrides.Target(actorId) is { } chosen)
        {
            PursuitTarget? overridden = chosen == DaggerfallActorIdentity.PlayerEntityId ? player
                : _actors.TryGet(chosen, out ActorState other) && !other.IsDefeated ? new PursuitTarget(chosen, other.Position) : null;
            if (overridden is not null)
            {
                _gates?.Add(new("target", "select", DaggerfallGateOutcome.Chose, "actor.target", new DaggerfallGateValue("target", chosen)));
                return overridden;
            }
        }
        if (Overrides.IsHostile(actorId))
        {
            _gates?.Add(new("target", "select", DaggerfallGateOutcome.Chose, "actor.hostile", new DaggerfallGateValue("target", player.DurableId)));
            return player;
        }
        PursuitTarget? target = _selectTarget(actorId);
        if (_gates is not null)
            _gates.Add(target is { } selected
                ? new("target", "select", DaggerfallGateOutcome.Chose, selected.DurableId == player.DurableId ? "player" : "foe", new DaggerfallGateValue("target", selected.DurableId))
                : new("target", "select", DaggerfallGateOutcome.Refused, "none"));
        return target;
    }

    /// <summary>Why pursuit left the traced enemy's attack ineligible, from what its motor last weighed.</summary>
    private string IneligibleReason() => _tracedMotor is not { } motor ? "no-observation"
        : !motor.Policy.AllowAttack ? "held"
        : !motor.Observation.Visible ? "not-visible"
        : motor.Policy.AttackReach is not double reach ? "no-attack"
        : motor.Observation.Distance > reach ? "out-of-reach" : "not-observed";

    private DaggerfallGateValue[] MotorValues() => _tracedMotor is { } motor
        ? [new("distance", motor.Observation.Distance), new("reach", motor.Policy.AttackReach ?? 0)] : [];

    /// <summary>The tactic's movement intent from what this module shows it, then the developer's forced state over it.</summary>
    private PursuitPolicy SelectMotorPolicy(ActorState actor, IDaggerfallEnemyTactic tactic, PursuitTarget target, PursuitObservation observation,
        ulong generation, ulong step, float deltaSeconds)
    {
        PursuitPolicy chosen = tactic.Move(new(actor, target, observation, SensesOf(actor.DurableId, target.DurableId),
            _attackAtDistance?.Invoke(actor.DurableId, target.DurableId, observation.Distance), _motorContext(actor.DurableId, target.DurableId),
            _movementPolicy(actor.DurableId), MaySearch: !IsPacified(actor.DurableId), Lane(actor, target), generation, step, deltaSeconds), _gates);
        if (Overrides.ForcedState(actor.DurableId) is { } forced)
        {
            chosen = Force(chosen, forced, Senses(actor).Motor.StrafeDirection);
            _gates?.Add(new("override", "force-state", DaggerfallGateOutcome.Chose, forced.ToString()));
        }
        if (_gates is not null) _tracedMotor = (observation, chosen);
        return chosen;
    }

    /// <summary>The senses' last judgement of an actor's target, if they judged that target; null where only the Engine's sight applies.</summary>
    private DaggerfallEnemyPerceptionDecision? SensesOf(long actorId, long targetId) =>
        _lastDecisions.TryGetValue(actorId, out DaggerfallEnemyPerceptionDecision? decision) && decision.TargetId == targetId ? decision : null;

    /// <summary>The missile lane from where the actor stands to its target, swept only if a tactic reads it.</summary>
    private Lazy<bool> Lane(ActorState actor, PursuitTarget target) =>
        new(() => _laneClear!(actor, target), LazyThreadSafetyMode.None);

    /// <summary>
    /// Holds a maneuver whatever the motor rolled: Chase closes without swinging, Attack closes and swings in reach,
    /// Retreat and Strafe move as the motor would, Search goes to where the target was last known, Idle stands still.
    /// The moving states act as if the enemy knew where its target is, so they hold even where it has only noticed
    /// the target (a dark dungeon) and would otherwise idle; the Engine's visibility still gates a swing. Idle also
    /// gives up searching where the target was last seen, which pursuit would otherwise do before any maneuver: a held
    /// enemy loses track of a target that leaves its sight.
    /// </summary>
    private PursuitPolicy Force(PursuitPolicy chosen, PursuitState state, int strafeDirection) => state switch
    {
        PursuitState.Chase => chosen with { Maneuver = PursuitState.Chase, AllowAttack = false, SpeedMultiplier = 1f, TargetObserved = true },
        PursuitState.Attack => chosen with { Maneuver = PursuitState.Chase, AllowAttack = true, SpeedMultiplier = 1f, TargetObserved = true },
        PursuitState.Retreat => chosen with { Maneuver = PursuitState.Retreat, SpeedMultiplier = .5f, TargetObserved = true },
        PursuitState.Strafe => chosen with { Maneuver = PursuitState.Strafe, SpeedMultiplier = .25f, StrafeDirection = strafeDirection, TargetObserved = true },
        PursuitState.Search => chosen with { TargetObserved = false, SearchLastKnownPosition = true,
            SearchDistance = _maneuvers.SearchDistance, SearchStep = _maneuvers.SearchStep },
        PursuitState.Idle => chosen with { Maneuver = PursuitState.Idle, AllowAttack = false, SearchLastKnownPosition = false },
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

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
            _lastSources.Remove(actorId);
            return receipt;
        }
        if (actor.IsDefeated)
        {
            _lastDecisions.Remove(actorId);
            _lastSources.Remove(actorId);
            return receipt;
        }
        bool forcedHostile = Overrides.IsHostile(actorId);
        // The senses judge the pair with the pursued target where they have a rule for it (the player's stealth,
        // language and illusions); an ally's visibility of its foes is never reinterpreted by them. Elsewhere the
        // Engine's sight stands as it is.
        long targetId = _sensing ?? throw new InvalidOperationException("Enemy perception is read only inside an actor's pursuit.");
        DaggerfallEnemyPerceptionContext? sensed = !forcedHostile && _isPlayerAllied(actorId) ? null : _contextProvider(actorId, targetId);
        if (sensed is not { } context)
        {
            if (_gates is not null)
            {
                PerceptionPair[] seen = receipt.Pairs.ToArray().Where(value => value.Observer == checked((ulong)actorId)).ToArray();
                _gates.Add(new("perception", "engine", seen.Any(value => value.Kind == PerceptionPairKind.Visible) ? DaggerfallGateOutcome.Passed : DaggerfallGateOutcome.Refused,
                    seen.Length == 0 ? "no-pair" : seen[0].Kind.ToString().ToLowerInvariant(), [.. seen.Take(1).Select(value => new DaggerfallGateValue("distance", value.Distance))]));
            }
            return receipt;
        }
        PerceptionPair? pair = receipt.Pairs.ToArray()
            .Where(value => value.Observer == checked((ulong)actorId) && value.Target == checked((ulong)targetId))
            .OrderBy(value => value.Distance)
            .Select(value => (PerceptionPair?)value)
            .FirstOrDefault();
        DaggerfallEnemyPerceptionMemory memory = Senses(actor);

        if (memory.ForcedHostile || forcedHostile)
            context = context with { EnemyHostile = true, TargetPacified = false };
        DaggerfallEnemyPerceptionSource source = new(
            SeesThroughInvisibility: context.EnemySeesThroughInvisibility);
        DaggerfallEnemyPerceptionDecision decision = DaggerfallPerceptionPolicy.Evaluate(memory, pair,
            source, context, context.RollPercent);
        // Forced hostility overrides a pacification for the run without changing the saved disposition.
        if (forcedHostile && decision.Pacified) decision = decision with { Pacified = false };
        decision = decision with { TargetId = targetId };
        _lastDecisions[actorId] = decision;
        _gates?.Add(PerceptionGate(decision, pair));
        _lastSources[actorId] = source;
        foreach (DaggerfallSkillUse use in decision.SkillUses)
            _recordSkillUse(use);

        if (pair is null || decision.PursuitVisible == (pair.Value.Kind == PerceptionPairKind.Visible))
            return receipt;

        PerceptionPair[] projected = receipt.Pairs.ToArray();
        for (int index = 0; index < projected.Length; index++)
        {
            PerceptionPair value = projected[index];
            if (value.Observer != checked((ulong)actorId) || value.Target != checked((ulong)targetId)) continue;
            projected[index] = decision.PursuitVisible
                ? value with { Kind = PerceptionPairKind.Visible, FacingCosine = 1d }
                : value with { Kind = PerceptionPairKind.Occluded, Evidence = 0d };
        }
        return receipt with { Pairs = projected };
    }

    private static DaggerfallDecisionGate PerceptionGate(DaggerfallEnemyPerceptionDecision decision, PerceptionPair? pair)
    {
        List<DaggerfallGateValue> values = [new("inSight", decision.InSight ? 1 : 0), new("inEarshot", decision.InEarshot ? 1 : 0),
            new("stealthCheck", decision.StealthCheckAttempted ? 1 : 0)];
        if (pair is { } seen) values.Insert(0, new("distance", seen.Distance));
        if (decision.Pacification is { } attempt)
        {
            values.Add(new("pacifyChance", attempt.Chance));
            values.Add(new("pacifyRoll", attempt.Roll));
        }
        string reason = pair is null ? "no-pair" : decision.BlockedByIllusion ? "illusion" : decision.Pacified ? "pacified"
            : decision.Detected ? decision.InSight ? "seen" : decision.InEarshot ? "heard" : "stealth-roll" : "unnoticed";
        return new("perception", "senses", decision.PursuitVisible ? DaggerfallGateOutcome.Passed : DaggerfallGateOutcome.Refused, reason, [.. values]);
    }

    private static EnemyBehaviorState ToDaggerState(PursuitState value) => value switch
    {
        PursuitState.Idle => EnemyBehaviorState.Idle,
        PursuitState.Chase => EnemyBehaviorState.Chase,
        PursuitState.Retreat => EnemyBehaviorState.Retreat,
        PursuitState.Strafe => EnemyBehaviorState.Strafe,
        PursuitState.Attack => EnemyBehaviorState.Attack,
        PursuitState.Blocked => EnemyBehaviorState.Blocked,
        PursuitState.Search => EnemyBehaviorState.Search,
        PursuitState.TargetLost => EnemyBehaviorState.TargetLost,
        PursuitState.Unloaded => EnemyBehaviorState.Unloaded,
        PursuitState.Dead => EnemyBehaviorState.Dead,
        _ => throw new InvalidOperationException($"Unknown Daggerfall enemy behavior state '{value}'."),
    };
}

/// <summary>Copied Engine receipts used to explain one enemy's most recent ruleset decision.</summary>
internal sealed record EnemyBehaviorEvidence(long ActorId, EnemyBehaviorState State, PerceptionReadoutResult? Visibility, NavigationStepResult? Navigation)
{
    /// <summary>Who the creature pursued in this decision: the player, an ally's foe, or none.</summary>
    internal PursuitTarget? Target { get; init; }
}
