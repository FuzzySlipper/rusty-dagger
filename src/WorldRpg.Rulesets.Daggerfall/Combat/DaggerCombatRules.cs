using WorldRpg.Kit.Combat;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Targeting;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>Direct Daggerfall attack policy over Engine-authoritative Mechanics state.</summary>
internal sealed partial class DaggerCombatRules : IAttackRules<IProductFact>, IDaggerfallPhysicalHarmOperations
{
    private const long PlayerId = DaggerfallActorIdentity.PlayerEntityId;
    private const string HealthTrack = "health";
    private const string StaminaTrack = "stamina";
    private const string FatigueTrack = "fatigue";
    private readonly DaggerfallStrikes? _strikes;
    private readonly Func<long, bool, bool> _passesThrough;
    private bool _playerHarmsCivilians;
    private readonly IRandomService _random;
    internal CombatResolution Rules { get; }
    private readonly ActorsState _actors;
    private readonly MechanicsEquipmentCoordinator _equipment;
    private readonly Func<long, MechanicsInventoryCoordinator?> _actorInventories;
    private readonly DaggerfallContactMitigation _mitigation;
    private readonly Func<long, MechanicsEquipmentCoordinator> _actorEquipment;
    private readonly DaggerfallItemInstances _itemInstances;
    private readonly DaggerfallItemConditionService _itemCondition;
    private readonly DaggerfallDefinitions _catalog;
    private readonly IReadOnlyDictionary<string, int> _weaponMaterialModifiers;
    private readonly IReadOnlyDictionary<string, DaggerfallActionDefinition> _actions;
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _definitions;
    private readonly Action<DaggerfallSkillUse> _skillUses;
    private readonly Func<WorldPoint?> _playerPosition;
    private readonly Func<DaggerfallCharacterState?> _character;

    /// <summary>The curse the player's race is under, if any: a vampire player is undead.</summary>
    internal Func<DaggerfallRacialOverrideView?> PlayerCurse { private get; init; } = () => null;
    /// <summary>Whether admitted static geometry stands between a shot's release and its aim.</summary>
    /// <summary>The armor-value shift the defender's worn enchantments give, zero when none do.</summary>
    private readonly Func<int> _attackChanceModifier;
    private readonly DaggerfallAttackTuning _attackTuning;
    private string _playerAttack = DaggerfallAttackIds.Fast[0];
    private long? _sweptTarget;
    private bool _playerFinisher;
    private readonly Func<long, bool> _targetUnaware;
    private readonly Action<long> _alertTarget;
    private readonly Func<DaggerfallAdrenalineRush?> _playerRush;
    private readonly Func<long, long, ulong, ulong, ulong, DaggerfallWabbajackResult> _transformActor;
    internal DaggerfallHandEquipCountdown EquipCountdown { get; set; } = new(0, 0);
    internal void EquipmentChanged(DaggerfallEquipmentChange change) => EquipCountdown = new(
        EquipCountdown.RightSeconds + change.Timing.RightHandMilliseconds / 1000d,
        EquipCountdown.LeftSeconds + change.Timing.LeftHandMilliseconds / 1000d);
    internal void AdvanceEquipment(double seconds) => EquipCountdown = new(
        Math.Max(0, EquipCountdown.RightSeconds - seconds), Math.Max(0, EquipCountdown.LeftSeconds - seconds));
    internal bool PlayerWeaponReady => (ReadWeapon(_equipment.Read(), "right-hand") is not null
        || ReadWeapon(_equipment.Read(), "left-hand") is null ? EquipCountdown.RightSeconds : EquipCountdown.LeftSeconds) <= 0;

    internal AttackCapabilities<IProductFact> Attacks { get; }
    internal TargetingService Targeting { get; }
    internal AttackExecution<IProductFact> Execution { get; }
    private readonly ActorContact? _contact;
    private readonly Func<long, DaggerfallStrikePose?>? _strikePose;
    private readonly Func<CharacterStepEnvironment?>? _contactEnvironment;
    private readonly Func<long, Vector3> _actorVelocity;
    private readonly DaggerfallContactDelivery _delivery;
    internal DaggerfallContactDelivery Delivery => _delivery;
    internal bool HasPlayerRangedFlight => _delivery.HasPlayerFlight;
    // The pack's arrow item is the one ammunition the adopted ranged shots draw. A second ranged
    // action with different ammunition would move this name onto the authored action.
    private const string ArrowItemId = "arrow";
    // The classic skeleton mobile: its mobile id, as the donor's skeleton-warrior damage adjustment keys on it.
    private const int SkeletalWarriorMobileId = 15;

    private readonly Func<long, DaggerfallMagicDefense> _magicDefense;
    private readonly Func<long, bool> _physicalAttacksBlocked;
    private readonly Func<long, bool> _actorGameplayActive;
    private readonly Func<long, string?> _actorTeam;
    private readonly Func<long, long, DaggerfallItemStrikeSource, int, int> _itemStrike;
    private readonly Action<DaggerfallMonsterHitExposure> _monsterHit;
    internal DaggerCombatRules(IRandomService random, ActorsState actors, MechanicsEquipmentCoordinator equipment,
        Func<long, MechanicsInventoryCoordinator?> actorInventories, DaggerfallItemInstances itemInstances,
        DaggerfallDefinitions definitions, IReadOnlyDictionary<long, DaggerfallActorDefinition> definitionsByEntity,
        TargetingService targeting, Action<DaggerfallSkillUse> skillUses,
        Func<long, MechanicsEquipmentCoordinator> actorEquipment, DaggerfallItemConditionService itemCondition,
        CombatResolution rules,
        Func<WorldPoint?> playerPosition, Func<DaggerfallCharacterState?> character,
        Action<long, DaggerfallWeaponPoisonSource> deliverWeaponPoison, Func<int> attackChanceModifier,
        Func<long, long, ulong, ulong, ulong, DaggerfallWabbajackResult> transformActor, Func<long, DaggerfallMagicDefense> magicDefense,
        Func<long, bool> physicalAttacksBlocked,
        Func<long, long, ulong, int, int, ulong, ulong, (double Magicka, int Strength)> molagBalStrike, Func<long, long, DaggerfallItemStrikeSource, int, int> itemStrike,
        Action<DaggerfallMonsterHitExposure> monsterHit, Func<long, bool> actorGameplayActive, Func<long, string?> actorTeam,
        DaggerfallContactMitigation mitigation, DaggerfallContactTuning contactTuning,
        DaggerfallAttackTuning attackTuning,
        Func<long, bool>? targetUnaware = null, Action<long>? alertTarget = null, Func<DaggerfallAdrenalineRush?>? playerRush = null,
        ActorContact? contact = null, Func<long, DaggerfallStrikePose?>? strikePose = null, Func<CharacterStepEnvironment?>? contactEnvironment = null,
        Func<long, Vector3>? actorVelocity = null, DaggerfallStrikes? strikes = null, Func<long, bool, bool>? passesThrough = null)
    {
        _strikes = strikes;
        _passesThrough = passesThrough ?? ((_, _) => false);
        _contact = contact;
        _strikePose = strikePose;
        _contactEnvironment = contactEnvironment;
        _actorVelocity = actorVelocity ?? (_ => Vector3.Zero);
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        _actorInventories = actorInventories ?? throw new ArgumentNullException(nameof(actorInventories));
        _itemInstances = itemInstances ?? throw new ArgumentNullException(nameof(itemInstances));
        _catalog = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _definitions = definitionsByEntity ?? throw new ArgumentNullException(nameof(definitionsByEntity));
        Targeting = targeting ?? throw new ArgumentNullException(nameof(targeting));
        _skillUses = skillUses ?? throw new ArgumentNullException(nameof(skillUses));
        _actorEquipment = actorEquipment ?? throw new ArgumentNullException(nameof(actorEquipment));
        _itemCondition = itemCondition ?? throw new ArgumentNullException(nameof(itemCondition));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _playerPosition = playerPosition ?? throw new ArgumentNullException(nameof(playerPosition));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _deliverWeaponPoison = deliverWeaponPoison ?? throw new ArgumentNullException(nameof(deliverWeaponPoison));
        _attackChanceModifier = attackChanceModifier ?? throw new ArgumentNullException(nameof(attackChanceModifier));
        _transformActor = transformActor ?? throw new ArgumentNullException(nameof(transformActor));
        _magicDefense = magicDefense ?? throw new ArgumentNullException(nameof(magicDefense));
        _physicalAttacksBlocked = physicalAttacksBlocked ?? throw new ArgumentNullException(nameof(physicalAttacksBlocked));
        _molagBalStrike = molagBalStrike ?? throw new ArgumentNullException(nameof(molagBalStrike));
        _itemStrike = itemStrike ?? throw new ArgumentNullException(nameof(itemStrike));
        _monsterHit = monsterHit ?? throw new ArgumentNullException(nameof(monsterHit));
        _actorGameplayActive = actorGameplayActive ?? throw new ArgumentNullException(nameof(actorGameplayActive));
        _actorTeam = actorTeam ?? throw new ArgumentNullException(nameof(actorTeam));
        _mitigation = mitigation ?? throw new ArgumentNullException(nameof(mitigation));
        _attackTuning = attackTuning ?? throw new ArgumentNullException(nameof(attackTuning));
        _targetUnaware = targetUnaware ?? (_ => false);
        _alertTarget = alertTarget ?? (_ => { });
        _playerRush = playerRush ?? (() => null);
        _weaponMaterialModifiers = DaggerfallFormulaPolicy.ClassicWeaponMaterialModifiers;
        _actions = definitions.Actions;
        _delivery = new DaggerfallContactDelivery(this, contactTuning ?? throw new ArgumentNullException(nameof(contactTuning)));
        Execution = new(actors, this, DeferRangedImpact, impactsOutliveTheirTarget: true, _delivery.Interrupt);
        Attacks = new(PlayerId, Targeting, Execution, ReachOf, facts => facts.Append(new AttackRejectedFact(AttackRejection.MissingPlayerPosition)),
            untargetedSwingsWaitForImpact: true);
    }

    internal void ResolveExplicit(ExplicitMeleeRequest request, FactBuffer<IProductFact> facts)
    {
        request.Validate();
        if (request.AttackerId != PlayerId) throw new ArgumentException("Enemy attacks use the delayed attack capability.", nameof(request));
        Execution.Start(new(request.AttackerId, request.TargetId, request.Generation, request.SimulationStep, request.FixedDeltaSeconds, false, request.Action?.Value), facts);
    }
    public void Refused(long attackerId, AttackRefusal reason, FactBuffer<IProductFact> facts) => facts.Append(new AttackRejectedFact(reason switch
    {
        AttackRefusal.UnknownActor => AttackRejection.UnknownExplicitCombatant,
        AttackRefusal.TargetDefeated => AttackRejection.TargetDefeated,
        AttackRefusal.InProgress => AttackRejection.AttackInProgress,
        AttackRefusal.Cooldown => AttackRejection.Cooldown,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Attack execution reported a refusal this ruleset does not know."),
    }, attackerId));
    public bool TryPrepare(AttackRequest request, FactBuffer<IProductFact> facts, out PreparedAttack prepared)
    {
        prepared = null!;
        if (!TryResolve(request.AttackerId, out Combatant attacker)) { Refused(request.AttackerId, AttackRefusal.UnknownActor, facts); return false; }
        if (_physicalAttacksBlocked(request.AttackerId))
        { facts.Append(new AttackRejectedFact(AttackRejection.Incapacitated, request.AttackerId)); return false; }
        // Whether an attack is the player's or an enemy's decides the admission policy, the random
        // scope and which facts describe it. Delivery timing is a separate question: a targeted
        // player swing and every enemy swing wait for their animation's impact frame.
        bool enemyAttack = request.AttackerId != PlayerId;
        DaggerfallAttackDefinition attack;
        bool rangedAttack = !enemyAttack && PlayerHoldsBow() && _playerAttack != DaggerfallAttackIds.Kick;
        string? selectedAction = request.Action;
        WorldRpg.Kit.Inventory.UniqueInventoryItem? rangedWeapon = null;
        if (!enemyAttack)
        {
            if (!TryAdmitPlayerAttack(request, attacker, out attack, facts)) return false;
        }
        else
        {
            var selection = EnemyAction(attacker, EnemyTargetDistance(request.AttackerId, request.TargetId));
            if (selection.Action is not { CooldownSeconds: double cooldown, Reach: > 0d } action)
            { facts.Append(new AttackRejectedFact(AttackRejection.NoAttackPolicy, request.AttackerId)); return false; }
            selectedAction = action.Id;
            rangedWeapon = selection.Bow;
            rangedAttack = action.Interpretation is "fixed-ranged" or "enemy-equipped-ranged";
            attack = rangedWeapon is { } bow ? CarriedBowAttack(bow, action)
                : action.Interpretation == "enemy-equipped-melee"
                    ? ResolveEquippedEnemyAttack(attacker, cooldown, action.Reach.Value)
                    : ResolveFixedAttack(attacker.Definition, action, cooldown);
            // A held shot spends nothing: delivery decides before an arrow leaves the quiver.
            if (rangedAttack && request.TargetId is long mark && _delivery.HoldsFire(request.AttackerId, mark)) return false;
            if (rangedAttack && !TrySpendArrow(request.AttackerId, facts)) return false;
        }
        DaggerfallStrikeFeedback feedback = rangedAttack ? new(true, "sound.3") : StrikeFeedback(request.AttackerId, false);
        ulong? wabbajackSource = rangedAttack ? null : CaptureWabbajackSource(request.AttackerId);
        var molagBalSource = rangedAttack ? null : CaptureMolagBalSource(request.AttackerId);
        var poisonSource = CaptureWeaponPoison(request.AttackerId, rangedWeapon);
        var strikeSource = DaggerfallItemCastTriggers.CaptureStrike(_catalog.Magic, _itemInstances, _actors.Entities, rangedWeapon ?? EquippedWeapon(request.AttackerId));
        DaggerfallAdmittedAttack admitted = new(request, attacker, attack, rangedAttack, enemyAttack ? 1d : PlayerDamageScale, !enemyAttack && (_playerFinisher || _playerAttack == DaggerfallAttackIds.PowerOut), selectedAction, feedback,
            wabbajackSource, molagBalSource, poisonSource, strikeSource, rangedWeapon) { PlayerAttack = enemyAttack ? null : _playerAttack, HarmsCivilians = !enemyAttack && _playerHarmsCivilians };
        if (!_delivery.TryPrepare(admitted, facts, out DaggerfallPreparedAttack delivered)) return false;
        prepared = delivered;
        return true;
    }
    public void Started(AttackRequest request, PreparedAttack attack, FactBuffer<IProductFact> facts)
    {
        // A player swing announces itself at admission, where its guard and stamina cost live; an
        // enemy swing announces the decision its authored damage frame will carry out.
        if (request.AttackerId != PlayerId) facts.Append(new EnemyAttackStartedFact(request.AttackerId, request.TargetId!.Value, attack.Outcome.Hit, request.Generation, request.SimulationStep) { Feedback = PreparedFeedback(attack), Ranged = ((DaggerfallPreparedAttack)attack).IsRanged });
    }
    public void Apply(AttackRequest request, PreparedAttack attack, FactBuffer<IProductFact> facts)
    {
        // A released projectile flies on its own; paralysis stops new strikes and releases.
        if (_physicalAttacksBlocked(request.AttackerId))
        { facts.Append(new AttackRejectedFact(AttackRejection.Incapacitated, request.AttackerId)); return; }
        _delivery.Apply(request, (DaggerfallPreparedAttack)attack, facts);
    }

    private void RecordPlayerWeaponHit(string weaponSkill)
    {
        _skillUses(new DaggerfallSkillUse(weaponSkill, DaggerfallSkillUseReason.WeaponHit, DaggerfallSkillUseOutcome.Succeeded));
        _skillUses(new DaggerfallSkillUse("critical-strike", DaggerfallSkillUseReason.CriticalStrikeHit, DaggerfallSkillUseOutcome.Succeeded));
    }

    /// <summary>
    /// Paralysis stops a new release, whoever owns the flight; an already released projectile remains its
    /// own admitted flight. Delivery takes every other released shot into its flight.
    /// </summary>
    private bool DeferRangedImpact(DeferredAttackImpact impact, FactBuffer<IProductFact> facts)
    {
        if (impact.Request.Delayed && impact.Attack is DaggerfallPreparedAttack { IsRanged: true } && _physicalAttacksBlocked(impact.Request.AttackerId))
        { facts.Append(new AttackRejectedFact(AttackRejection.Incapacitated, impact.Request.AttackerId)); return true; }
        return _delivery.DeferImpact(impact, facts);
    }

    /// <summary>The item template of the weapon the player holds, or null for bare hands.</summary>
    internal int? PlayerWeaponTemplate() => EquippedWeapon(PlayerId) is { } weapon
        ? _catalog.SourceTemplateFor(_catalog.RequireItem(new DaggerfallItemId(weapon.Definition.Value)))?.Index : null;

    /// <summary>Records whom the player's sweep met, which the blow's impact takes.</summary>
    internal void SetSweptTarget(long target) => _sweptTarget = target;

    /// <summary>Names the attack the next player admission carries, from the attack clock.</summary>
    internal void SelectPlayerAttack(string attack, bool finisher = false, bool harmsCivilians = false) =>
        (_playerAttack, _playerFinisher, _playerHarmsCivilians) = (attack, finisher, harmsCivilians);

    /// <summary>Whether the player is too short of stamina for a power attack, a kick or a guard.</summary>
    internal bool PlayerWinded => _actors.Player.Stats.GetTrack(TrackId.Parse(StaminaTrack)).Current <= _attackTuning.Combatant.WindedStamina;

    /// <summary>What an active Adrenaline Rush scales the player's attack time by: it wakes below an eighth of full health.</summary>
    internal double PlayerRushTimeScale => PlayerRushing is { } rush ? rush.Improved ? _attackTuning.Combatant.ImprovedRushTimeScale : _attackTuning.Combatant.RushTimeScale : 1d;

    private double PlayerRushDamageScale => PlayerRushing is { } rush ? rush.Improved ? _attackTuning.Combatant.ImprovedRushDamageScale : _attackTuning.Combatant.RushDamageScale : 1d;

    private DaggerfallAdrenalineRush? PlayerRushing
    {
        get
        {
            Track health = _actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
            return health.Current < health.Maximum.Value / 8d ? _playerRush() : null;
        }
    }

    /// <summary>What a player blow's damage is scaled by: a power release, a winded blow and an Adrenaline Rush.</summary>
    private double PlayerDamageScale => (_playerAttack == DaggerfallAttackIds.PowerOut ? _attackTuning.PowerDamageMultiplier : 1d)
        * (PlayerWinded ? _attackTuning.Combatant.WindedDamageScale : 1d) * PlayerRushDamageScale;

    /// <summary>Whether a blow past its hit frame is still winding up to land on <paramref name="target"/>.</summary>
    internal bool HoldsImpactAt(long target) => _delivery.HoldsImpactAt(target);

    /// <summary>Lands delivery's held blows due by this step.</summary>
    internal void LandDueImpacts(ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts) =>
        _delivery.LandDueImpacts(generation, simulationStep, facts);

    /// <summary>Advances delivery's flights by the update's elapsed seconds.</summary>
    internal void AdvanceRangedFlight(ulong generation, ulong simulationStep, double elapsedSeconds,
        IReadOnlyDictionary<long, WorldPoint> positions, FactBuffer<IProductFact> facts) =>
        _delivery.AdvanceFlight(generation, simulationStep, elapsedSeconds, positions, facts);

    /// <summary>The current shot-owned positions consumed by the ordinary world appearance snapshot.</summary>
    internal IReadOnlyList<DaggerfallRangedFlightView> ReadRangedFlights(ulong generation, ulong simulationStep) =>
        _delivery.ReadFlights(generation, simulationStep);

    /// <summary>A site transition retires its transient releases and in-flight shots together.</summary>
    internal void ClearRangedFlight() => _delivery.ClearFlight();

    internal void RebaseRangedFlight(Vector3 delta) => _delivery.RebaseFlight(delta);

    /// <summary>A shot is any attack whose own action carries it to a target beyond a swing: the
    /// enemy's authored fixed-ranged action, or the player's ranged action while a bow is held.</summary>
    private bool IsRangedAction(long attackerId)
    {
        if (attackerId == PlayerId) return PlayerHoldsBow() && RangedPlayerActionId() is not null;
        return _definitions.TryGetValue(attackerId, out DaggerfallActorDefinition? actor)
            && actor.ActionId is string actionId && _actions.TryGetValue(actionId, out DaggerfallActionDefinition? action)
            && action.Interpretation == "fixed-ranged";
    }

    /// <summary>Live timing from the same equipped weapon and action used at admission.</summary>
    internal (double CooldownSeconds, double FrameSeconds, string Equipment) InspectPlayerTiming()
    {
        EquipmentRead equipment = _equipment.Read();
        DaggerfallEquippedWeapon? selected = ReadWeapon(equipment, "right-hand") ?? ReadWeapon(equipment, "left-hand");
        bool bow = selected is { Weapon.IsBow: true };
        int speed = _actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Speed.Value)).ValueInt;
        string? actionId = bow ? RangedPlayerActionId() : _definitions[PlayerId].ActionId;
        double cooldown = bow ? DaggerfallFormulaPolicy.BowCooldownSeconds(speed)
            : actionId is not null && _actions.TryGetValue(actionId, out var action) ? action.CooldownSeconds ?? 0 : 0;
        return (cooldown, DaggerfallFormulaPolicy.MeleeWeaponAnimationSeconds(speed), selected?.Weapon.Skill ?? DaggerfallMechanicsIds.HandToHand.Value);
    }

    internal bool PlayerHoldsBow()
    {
        EquipmentRead equipment = _equipment.Read();
        return (ReadWeapon(equipment, "right-hand") ?? ReadWeapon(equipment, "left-hand"))
            is { Weapon.IsBow: true };
    }

    private bool IsLiveCombatant(long actorId)
    {
        if (!_actorGameplayActive(actorId)) return false;
        if (actorId == PlayerId) return ! _actors.Player.IsDefeated && _definitions.ContainsKey(actorId);
        return _actors.TryGet(actorId, out ActorState actor) && !actor.IsDefeated && _definitions.ContainsKey(actorId);
    }


    /// <summary>
    /// A ranged shot draws one arrow from the shooter's managed quiver and refuses the shot
    /// outright when it is empty: an out-of-arrows archer stops shooting rather than silently
    /// missing. The arrow is spent when the shot begins, so a swing interrupted after its
    /// decision was a drawn-and-not-loosed shot and stays spent; the donor's own quiver
    /// discipline does not refund a drawn arrow either.
    /// </summary>
    private bool TrySpendArrow(long shooterId, FactBuffer<IProductFact> facts)
    {
        if (_actorInventories(shooterId) is not MechanicsInventoryCoordinator quiver)
        {
            // A ranged actor without a managed quiver is a composition defect its owner has to
            // see, not an empty quiver the player could misread as a bad roll.
            facts.Append(new AttackRejectedFact(AttackRejection.NoAttackPolicy, shooterId));
            return false;
        }
        if (!quiver.Read().Stacks.Any(stack => IsArrow(stack) && stack.Quantity > 0))
        {
            facts.Append(new AttackRejectedFact(AttackRejection.EmptyQuiver, shooterId));
            return false;
        }
        InventoryStackId stack = quiver.Read().Stacks
            .Where(IsArrow)
            .OrderBy(value => value.Id.Value, StringComparer.Ordinal)
            .Select(value => value.Id)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("The archer's selected arrow stack disappeared before the shot could consume it.");
        InventoryMutationReceipt receipt = quiver.Consume(new InventoryConsume(stack, 1));
        if (receipt.AfterQuantity == 0)
            _itemInstances.RemoveStack(shooterId == PlayerId ? DaggerfallItemOwner.Player : DaggerfallItemOwner.Actor(shooterId), stack);
        return true;
    }

    private bool IsArrow(InventoryStack stack)
    {
        DaggerfallItemDefinition definition = _catalog.RequireItem(new DaggerfallItemId(stack.Definition.Value));
        return definition.Id.Value == ArrowItemId || definition.Template?.Index == 131;
    }

    private bool TryReadPlayerAttackPolicy(Combatant player, string? requested,
        out DaggerfallEquippedWeapon? weapon, out DaggerfallActionDefinition action)
    {
        EquipmentRead equipment = _equipment.Read();
        weapon = ReadWeapon(equipment, "right-hand") ?? ReadWeapon(equipment, "left-hand");
        bool bow = weapon is { Weapon.IsBow: true };
        string? id = requested ?? (bow && _playerAttack != DaggerfallAttackIds.Kick ? RangedPlayerActionId() : player.Definition.ActionId);
        action = null!;
        if (id is null || !_actions.TryGetValue(id, out var selected)
            || selected.Interpretation is not ("player-equipped-melee" or "player-equipped-ranged")
            || selected.StaminaCost is not int
            || selected.Interpretation == "player-equipped-ranged" && !bow
            || selected.Interpretation == "player-equipped-melee" && selected.CooldownSeconds is not double) return false;
        action = selected;
        return true;
    }

    // Reads policy/resources only. Actual admission still owns the atomic spends and their receipts.
    internal AttackRejection? InspectPlayerAttackRefusal()
    {
        if (_physicalAttacksBlocked(PlayerId)) return AttackRejection.Incapacitated;
        if (!TryResolve(PlayerId, out Combatant player)
            || !TryReadPlayerAttackPolicy(player, null, out _, out var action)) return AttackRejection.NoAttackPolicy;
        if (action.Interpretation == "player-equipped-ranged")
        {
            var quiver = _actorInventories(PlayerId);
            if (quiver is null) return AttackRejection.NoAttackPolicy;
            if (!quiver.Read().Stacks.Any(stack => IsArrow(stack) && stack.Quantity > 0))
                return AttackRejection.EmptyQuiver;
        }
        // Low fatigue never refuses a swing: the donor charges the swing and lets exhaustion decide.
        return null;
    }

    private bool TryAdmitPlayerAttack(AttackRequest request, Combatant player, out DaggerfallAttackDefinition attack, FactBuffer<IProductFact> facts)
    {
        ulong generation = request.Generation, simulationStep = request.SimulationStep;
        // A swing that was cancelled after its sweep met someone never took its target to an impact.
        _sweptTarget = null;
        if (!PlayerWeaponReady)
        {
            attack = default!;
            facts.Append(new AttackRejectedFact(AttackRejection.Equipping));
            return false;
        }
        if (!TryReadPlayerAttackPolicy(player, request.Action, out DaggerfallEquippedWeapon? equippedWeapon,
            out DaggerfallActionDefinition playerAction))
        {
            attack = default!;
            facts.Append(new AttackRejectedFact(AttackRejection.NoAttackPolicy));
            return false;
        }
        bool kick = _playerAttack == DaggerfallAttackIds.Kick;
        // A winded character has no breath for a kick or a power release; a fast attack still goes, slower and weaker.
        if (PlayerWinded && (kick || _playerAttack == DaggerfallAttackIds.PowerOut))
        {
            attack = default!;
            facts.Append(new AttackRejectedFact(AttackRejection.Winded));
            return false;
        }
        int staminaCost = kick ? _attackTuning.KickStaminaCost
            : _playerAttack == DaggerfallAttackIds.PowerOut ? (int)Math.Round(playerAction.StaminaCost!.Value * _attackTuning.PowerStaminaMultiplier, MidpointRounding.AwayFromZero)
            : playerAction.StaminaCost!.Value;
        bool ranged = playerAction.Interpretation == "player-equipped-ranged";
        double playerCooldown = ranged
            ? DaggerfallFormulaPolicy.BowCooldownSeconds(ReadStat(player, DaggerfallMechanicsIds.Speed))
            : playerAction.CooldownSeconds!.Value;
        // The shot is refused before it is admitted, from the same quiver owner the enemy archer uses,
        // so a player with an empty quiver stops shooting rather than silently missing.
        if (ranged && !TrySpendArrow(PlayerId, facts)) { attack = default!; return false; }
        attack = kick
            ? new DaggerfallAttackDefinition("hand-to-hand", _attackTuning.KickDamage, _attackTuning.KickDamage, playerCooldown, DamageBonus: playerAction.DamageBonus, Reach: playerAction.Reach)
            : equippedWeapon is { } selectedWeapon
            ? new DaggerfallAttackDefinition(selectedWeapon.Weapon.Skill, selectedWeapon.Weapon.MinimumDamage, selectedWeapon.Weapon.MaximumDamage,
                playerCooldown, selectedWeapon.Material, playerAction.DamageBonus)
            : new DaggerfallAttackDefinition(
                "hand-to-hand",
                DaggerfallFormulaPolicy.HandToHandMinimumDamage(ReadStat(player, DaggerfallMechanicsIds.HandToHand)),
                DaggerfallFormulaPolicy.HandToHandMaximumDamage(ReadStat(player, DaggerfallMechanicsIds.HandToHand)),
                playerCooldown,
                DamageBonus: playerAction.DamageBonus);
        SpendPlayerStamina(player, staminaCost);
        facts.Append(new PlayerAttackStartedFact(generation, simulationStep, request.TargetId) { Feedback = StrikeFeedback(PlayerId) });
        return true;
    }

    /// <summary>
    /// The authored action a player's bow looses with. The action declares the interpretation; nothing
    /// hardcodes an action id, so renaming the authored action in the pack is enough.
    /// </summary>
    private string? RangedPlayerActionId() => _actions.Values
        .Where(value => value.Interpretation == "player-equipped-ranged")
        .Select(value => value.Id)
        .OrderBy(value => value, StringComparer.Ordinal)
        .FirstOrDefault();

    /// <summary>
    /// Charges the admitted swing its stamina and the donor's small fatigue, each clamped at zero. The donor
    /// (WeaponManager) never refuses a swing for fatigue; a swing that empties that pool leaves the session's
    /// exhaustion owner to decide.
    /// </summary>
    private void SpendPlayerStamina(Combatant player, int staminaCost)
    {
        Track stamina = player.Stats.GetTrack(TrackId.Parse(StaminaTrack));
        _ = stamina.Spend(Math.Min(stamina.Current, staminaCost));
        Track fatigue = player.Stats.GetTrack(TrackId.Parse(FatigueTrack));
        _ = fatigue.Spend(Math.Min(fatigue.Current, _attackTuning.Stamina.FatiguePerSwing));
    }

    private CombatParticipants Participants(long attacker, long target, string action) => new(
        new Actor(_actors.Store, _actors.Entities.Resolve(ActorsState.Identity(attacker))),
        new Actor(_actors.Store, _actors.Entities.Resolve(ActorsState.Identity(target))), action);


    /// <summary>
    /// The damage roll behind a connected, non-immune hit in the donor's order: the attack takes its
    /// damage through the hand-to-hand or weapon path, a successful backstab roll triples what those
    /// paths produced, and the total is bounded at zero.
    /// </summary>
    private Action<DamageEvent> DamageRoll(ExplicitMeleeRequest request, Combatant attacker, Combatant target,
        DaggerfallAttackDefinition attack, bool enemy, int attackDamageMod) => damage =>
    {
        int rolled = attack.Skill == DaggerfallMechanicsIds.HandToHand.Value
            ? DaggerfallFormulaPolicy.CalculateHandToHandAttackDamage(
                    Draw(request, attacker.Id, target.Id, CombatRandomKey.DamageSalt, attack.MinimumDamage, attack.MaximumDamage, enemy),
                    checked(attackDamageMod + attack.DamageBonus),
                    attacker.Id == PlayerId ? StrengthModifier(attacker) : 0,
                    EnemyTypeBonus(attacker, target))
            : DaggerfallFormulaPolicy.CalculateWeaponAttackDamage(
                Draw(request, attacker.Id, target.Id, CombatRandomKey.DamageSalt, attack.MinimumDamage, attack.MaximumDamage, enemy),
                checked(attackDamageMod + attack.DamageBonus),
                target.Definition.Kind == DaggerfallActorKinds.Monster && target.Definition.MobileId == SkeletalWarriorMobileId,
                DaggerfallFormulaPolicy.WeaponIsEdged(attack.Skill),
                attack.Material == "silver",
                StrengthModifier(attacker),
                DaggerfallFormulaPolicy.WeaponMaterialDamageModifier(attack.Material, _weaponMaterialModifiers),
                EnemyTypeBonus(attacker, target));
        damage.Damage = Math.Max(0, rolled);
    };


    private readonly Action<long, DaggerfallWeaponPoisonSource> _deliverWeaponPoison;

    private DaggerfallWeaponPoisonSource? CaptureWeaponPoison(long attacker, WorldRpg.Kit.Inventory.UniqueInventoryItem? selectedWeapon = null)
    {
        if ((selectedWeapon ?? EquippedWeapon(attacker)) is not { } weapon) return null;
        ulong id = (attacker == PlayerId ? _equipment : _actorEquipment(attacker))
            .GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        return _itemInstances.ContainsUnique(id) && _itemInstances.RequireUnique(id).PoisonVariant is int variant
            ? new(id, variant) : null;
    }

    private void ApplyDamage(CombatParticipants participants, long attacker, long target, int damage, int body, bool enemy,
        ulong generation, ulong step, DaggerfallStrikeFeedback feedback, FactBuffer<IProductFact> facts,
        (WorldRpg.Kit.Inventory.UniqueInventoryItem Weapon, ulong Identity)? mace, bool emitHitFact = true, int? admittedBaseDamage = null, WorldRpg.Kit.Inventory.UniqueInventoryItem? rangedWeapon = null,
        DaggerfallHarmMitigation mitigated = default)
    {
        Track health = participants.TargetStats.GetTrack(TrackId.Parse(HealthTrack));
        int physicalDamage = damage;
        WorldRpg.Kit.Inventory.UniqueInventoryItem? razorWeapon = null;
        int razorWear = 0;
        if (rangedWeapon is null)
            damage = ApplyRazor(attacker, target, damage, health, enemy, generation, step, facts, out razorWeapon, out razorWear);
        ApplyHitEvent applied = Rules.ApplyToHealth(participants, damage, health);
        if (emitHitFact) facts.Append(new AttackHitFact(attacker, target, applied.CalculatedDamage, applied.ActualHealthLost, body, enemy, generation, step) { Feedback = feedback, Mitigated = mitigated });
        facts.Append(new DamageAppliedFact(attacker, target, DaggerfallDamageCause.PhysicalAttack,
            applied.CalculatedDamage, applied.ActualHealthLost, body, generation, step));
        if (applied.ActualHealthLost > 0)
            facts.Append(new ActorDamagedFact(target, attacker, DaggerfallDamageCause.PhysicalAttack,
                applied.CalculatedDamage, applied.ActualHealthLost) { TargetDefeated = applied.Defeated });
        if (applied.Defeated)
            facts.Append(new ActorDiedFact(target, attacker, DaggerfallDamageCause.PhysicalAttack,
                applied.CalculatedDamage, applied.ActualHealthLost, generation, step));
        // Revalidate the admitted source before wear can break and remove it.
        bool acceptedMace = mace is { } admittedMace
            && CaptureMolagBalSource(attacker) is { } impactMace && impactMace.Identity == admittedMace.Identity;
        int physicalWearDamage = admittedBaseDamage ?? (razorWear > 0 ? physicalDamage : applied.Damage);
        if (physicalWearDamage > 0) ApplyPhysicalWear(attacker, target, body, physicalWearDamage, enemy, generation, step, facts, rangedWeapon);
        if (acceptedMace && mace is { } maceSource && applied.Damage > 0)
            ApplyMolagBal(attacker, target, maceSource, applied.Damage, enemy, generation, step, facts);
        if (razorWeapon is { } charged) DamageCondition(charged, attacker, razorWear, generation, step, facts);
        ReflectNamira(participants, attacker, target, applied, generation, step, facts);
    }

    /// <summary>
    /// FormulaHelper applies this for each positive natural nymph/lamia slot, before health damage.
    /// The separate track mutation and fact prevent a fatigue consequence from being misreported as health loss.
    /// </summary>
    private void ApplyFatigueConsequence(long attacker, long target, int acceptedHealthDamage, ulong generation, ulong step, FactBuffer<IProductFact> facts)
    {
        if (acceptedHealthDamage <= 0) return;
        if (!TryResolve(target, out Combatant victim)) return;

        Track fatigue = victim.Stats.GetTrack(TrackId.Parse(FatigueTrack));
        double before = fatigue.Current;
        int calculated = DaggerfallFormulaPolicy.FatigueDamage(acceptedHealthDamage);
        fatigue.SetCurrent(Math.Max(fatigue.Minimum, before - calculated), clamp: true);
        double actual = before - fatigue.Current;
        facts.Append(new FatigueAppliedFact(attacker, target, calculated, actual, generation, step));
    }

    /// <summary>
    /// Physical wear happens only after the one accepted hit result. Classic wears equipment only for a
    /// weapon strike, so an unarmed or natural attack leaves the target's armour untouched: the donor
    /// reaches its whole wear block from the branch that has a weapon in hand. The attacker's weapon
    /// wears first, then the shield covering the struck body part, or that part's armour when no shield
    /// covers it. Separate keyed draws preserve the donor's independent minimum-wear rolls without
    /// adding a second hit path.
    /// </summary>
    private void ApplyPhysicalWear(long attacker, long target, int body, int damage, bool enemy, ulong generation, ulong step, FactBuffer<IProductFact> facts, WorldRpg.Kit.Inventory.UniqueInventoryItem? rangedWeapon = null)
    {
        if ((rangedWeapon ?? EquippedWeapon(attacker)) is not WorldRpg.Kit.Inventory.UniqueInventoryItem weapon) return;
        WorldRpg.Kit.Inventory.UniqueInventoryItem? shield = EquippedShield(target, body);
        WorldRpg.Kit.Inventory.UniqueInventoryItem? armour = shield is null ? EquippedArmour(target, body) : null;
        DaggerfallStruckEquipment struck = DaggerfallFormulaPolicy.DamageEquipment(
            weaponStrike: true, shieldCoversStruckBodyPart: shield is not null, armourAtStruckBodyPart: armour is not null);
        DamageCondition(weapon, attacker, ConditionUnits(attacker, target, damage, enemy, generation, step, CombatRandomKey.WeaponConditionSalt), generation, step, facts);
        WorldRpg.Kit.Inventory.UniqueInventoryItem? struckItem = struck switch
        {
            DaggerfallStruckEquipment.Shield => shield,
            DaggerfallStruckEquipment.Armour => armour,
            _ => null,
        };
        if (struckItem is { } worn)
            DamageCondition(worn, target, ConditionUnits(attacker, target, damage, enemy, generation, step, CombatRandomKey.ArmorConditionSalt), generation, step, facts);
    }

    /// <summary>
    /// One item's wear: the condition service owns the durable mutation and the break's single
    /// equipment removal, and the emitted fact is how the rest of the product sees the result.
    /// </summary>
    private void DamageCondition(WorldRpg.Kit.Inventory.UniqueInventoryItem item, long owner, int units,
        ulong generation, ulong step, FactBuffer<IProductFact> facts)
    {
        if (units <= 0) return;
        DaggerfallItemConditionResult result = owner == PlayerId
            ? _itemCondition.Damage(item, units)
            : _itemCondition.Damage(item, DaggerfallItemOwner.Actor(owner), _actorEquipment(owner), units);
        // The plural break belongs to the item's native template, not to the identifier the line
        // prints: a materialized 'template-104-iron' instance is the donor's greaves.
        int? templateIndex = _catalog.TryResolveItem(new DaggerfallItemId(item.Definition.Value), out DaggerfallItemDefinition wornDefinition)
            ? DaggerfallTemplateItemDefinitions.TemplateIndexForAuthoredItem(wornDefinition.Id) ?? wornDefinition.Template?.Index
            : null;
        facts.Append(new EquipmentWornFact(owner, result.DurableItemId, result.Metadata.ItemId,
            DaggerfallTemplateItemDefinitions.BreaksInPlural(templateIndex),
            result.PreviousCondition, result.Metadata.CurrentCondition,
            result.Outcome == DaggerfallItemConditionOutcome.Broken, generation, step));
    }

    private int ConditionUnits(long attacker, long target, int damage, bool enemy, ulong generation, ulong step, int salt)
    {
        int scaled = DaggerfallFormulaPolicy.ConditionDamageScale(damage);
        if (scaled != 0) return scaled;
        return DaggerfallFormulaPolicy.ApplyConditionDamageThroughPhysicalHit(damage,
            Draw(new ExplicitMeleeRequest(attacker, target, generation, step, 1d), attacker, target, salt, 1, 100, enemy));
    }

    internal sealed record DaggerfallPreparedAttack(double CooldownSeconds, AttackOutcome Outcome,
        DaggerfallStrikeFeedback Feedback, ulong? WabbajackSource = null,
        (WorldRpg.Kit.Inventory.UniqueInventoryItem Weapon, ulong Identity)? MolagBalSource = null, DaggerfallItemStrikeSource? ItemStrikeSource = null, DaggerfallWeaponPoisonSource? WeaponPoison = null,
        DaggerfallMonsterHit[]? MonsterHits = null, int Body = 0, bool IsRanged = false, WorldRpg.Kit.Inventory.UniqueInventoryItem? RangedWeapon = null, string? Action = null,
        DaggerfallAdmittedAttack? Admission = null,
        DaggerfallHarmMitigation Mitigated = default) : PreparedAttack(CooldownSeconds, Outcome);

    private static DaggerfallStrikeFeedback PreparedFeedback(PreparedAttack attack) => attack is DaggerfallPreparedAttack accepted
        ? accepted.Feedback : throw new InvalidOperationException("A Daggerfall strike requires its accepted source feedback; re-reading changed equipment would misidentify the strike.");

    // DaggerfallUnityItem.GetSwingSound: native weapon template semantics, not media filenames.
    private DaggerfallStrikeFeedback StrikeFeedback(long owner, bool? ranged = null)
    {
        if (ranged ?? IsRangedAction(owner)) return new(true, "sound.3");
        if (EquippedWeapon(owner) is not { } weapon) return DaggerfallStrikeFeedback.Unarmed;
        DaggerfallItemDefinition definition = _catalog.RequireItem(new DaggerfallItemId(weapon.Definition.Value));
        return DaggerfallCombatFeedbackPolicy.ForWeaponTemplate(_catalog.SourceTemplateFor(definition)?.ClassicIndex);
    }

    private WorldRpg.Kit.Inventory.UniqueInventoryItem? EquippedWeapon(long owner)
    {
        EquipmentRead equipment = owner == PlayerId ? _equipment.Read() : _actorEquipment(owner).Read();
        foreach (string slot in new[] { "right-hand", "left-hand" })
            if (equipment.TryGet(new WorldRpg.Kit.Inventory.EquipmentSlotId(slot), out WorldRpg.Kit.Inventory.UniqueInventoryItem item)
                && ReadWeapon(equipment, slot) is not null) return item;
        return null;
    }

    /// <summary>A shield the owner wears in the left hand when it covers the struck body part, if any.</summary>
    private WorldRpg.Kit.Inventory.UniqueInventoryItem? EquippedShield(long owner, int body)
    {
        EquipmentRead equipment = owner == PlayerId ? _equipment.Read() : _actorEquipment(owner).Read();
        return equipment.TryGet(new WorldRpg.Kit.Inventory.EquipmentSlotId("left-hand"), out WorldRpg.Kit.Inventory.UniqueInventoryItem shield)
            && _catalog.RequireItem(new DaggerfallItemId(shield.Definition.Value)) is { Shield: not null } shieldDefinition
            && ShieldCovers(shieldDefinition, body)
            ? shield : null;
    }

    /// <summary>The equipment slot whose armour covers a struck body part.</summary>
    internal static string BodySlot(int body) => body switch
    {
        0 => "head", 1 => "right-arm", 2 => "left-arm", 3 => "chest-armor",
        4 => "gloves", 5 => "legs-armor", 6 => "feet", _ => throw new ArgumentOutOfRangeException(nameof(body)),
    };

    /// <summary>Armour worn on the struck body part. Classic never wears the armour a shield already covers.</summary>
    private WorldRpg.Kit.Inventory.UniqueInventoryItem? EquippedArmour(long owner, int body)
    {
        string slot = BodySlot(body);
        EquipmentRead equipment = owner == PlayerId ? _equipment.Read() : _actorEquipment(owner).Read();
        return equipment.TryGet(new WorldRpg.Kit.Inventory.EquipmentSlotId(slot), out WorldRpg.Kit.Inventory.UniqueInventoryItem armor)
            && _catalog.RequireItem(new DaggerfallItemId(armor.Definition.Value)).Armor is not null ? armor : null;
    }

    internal static bool ShieldCovers(DaggerfallItemDefinition shield, int body) => shield.Id.Value switch
    {
        "buckler" => body is 2 or 4,
        "round-shield" or "kite-shield" => body is 2 or 4 or 5,
        "tower-shield" => body is 0 or 2 or 4 or 5,
        _ when shield.Template?.Index is 109 => body is 2 or 4,
        _ when shield.Template?.Index is 110 or 111 => body is 2 or 4 or 5,
        _ when shield.Template?.Index is 112 => body is 0 or 2 or 4 or 5,
        _ => false,
    };

    private bool TryResolve(long id, out Combatant combatant)
    {
        if (id == PlayerId && _definitions.TryGetValue(PlayerId, out DaggerfallActorDefinition? player)) { combatant = new(id, _actors.Player.Stats, player); return true; }
        if (_actorGameplayActive(id) && _actors.TryGet(id, out ActorState actor) && _definitions.TryGetValue(id, out DaggerfallActorDefinition? definition) && definition.Kind != DaggerfallActorKinds.StaticNpc) { combatant = new(id, actor.Stats, definition); return true; }
        combatant = default;
        return false;
    }

    /// <summary>
    /// One enemy attempt's attack: the equipped weapon's authored damage when the enemy wears one, and
    /// the classic hand-to-hand range scaled by the actor's own hand-to-hand skill when it wears none.
    /// A weapon-wielding enemy uses its equipped weapon even where its unarmed numbers would be
    /// higher — the donor's stronger-unarmed substitution is a documented deviation it does not keep.
    /// The reach is the authored action's, the same value the behavior gate admitted the attack with.
    /// </summary>
    private DaggerfallAttackDefinition ResolveEquippedEnemyAttack(Combatant attacker, double cooldown, double reach)
    {
        DaggerfallEquippedWeapon? weapon = ReadWeapon(_actorEquipment(attacker.Id).Read(), "right-hand")
            ?? ReadWeapon(_actorEquipment(attacker.Id).Read(), "left-hand");
        return weapon is { } selected
            ? new DaggerfallAttackDefinition(selected.Weapon.Skill, selected.Weapon.MinimumDamage, selected.Weapon.MaximumDamage, cooldown, selected.Material, Reach: reach)
            : new DaggerfallAttackDefinition(DaggerfallMechanicsIds.HandToHand.Value,
                DaggerfallFormulaPolicy.HandToHandMinimumDamage(ReadStat(attacker, DaggerfallMechanicsIds.HandToHand)),
                DaggerfallFormulaPolicy.HandToHandMaximumDamage(ReadStat(attacker, DaggerfallMechanicsIds.HandToHand)),
                cooldown, Reach: reach);
    }

    private static DaggerfallAttackDefinition ResolveFixedAttack(DaggerfallActorDefinition actor, DaggerfallActionDefinition action, double cooldown) => action.AttackRangeIndex is int index
        ? new DaggerfallAttackDefinition(action.Skill, actor.Attacks[index].MinimumDamage, actor.Attacks[index].MaximumDamage, cooldown, Reach: action.Reach)
        : new DaggerfallAttackDefinition(action.Skill, action.MinimumDamage!.Value, action.MaximumDamage!.Value, cooldown, Reach: action.Reach);

    /// <summary>
    /// How far the given actor's authored attack carries, or null when it has no attack to carry.
    /// </summary>
    /// <remarks>
    /// The behavior owner reads this to decide whether the player is in reach, so the gate that decides an
    /// attack happens and the attack that then resolves use one number from one place.
    /// </remarks>
    public double? ReachOf(long entityId)
    {
        if (entityId == PlayerId)
        {
            string? selected = PlayerHoldsBow()
                ? RangedPlayerActionId()
                : _definitions.TryGetValue(PlayerId, out DaggerfallActorDefinition? player) ? player.ActionId : null;
            return selected is not null && _actions.TryGetValue(selected, out DaggerfallActionDefinition? playerAction) && playerAction.Reach is > 0d
                ? playerAction.Reach : null;
        }
        return _definitions.TryGetValue(entityId, out DaggerfallActorDefinition? attacker)
            && attacker.ActionId is { } actionId
            && _actions.TryGetValue(actionId, out DaggerfallActionDefinition? action)
            && action.Reach is > 0d
            ? action.Reach
            : null;
    }
    private DaggerfallEquippedWeapon? ReadWeapon(EquipmentRead equipment, string slot)
    {
        if (!equipment.TryGet(new WorldRpg.Kit.Inventory.EquipmentSlotId(slot), out WorldRpg.Kit.Inventory.UniqueInventoryItem item)
            || _catalog.RequireItem(new DaggerfallItemId(item.Definition.Value)).Weapon is not DaggerfallWeaponDefinition weapon) return null;
        DurableIdentityReference identity = _actors.Entities.IdentityOf(new EntityId(item.EntityId));
        if (identity.Kind != DurableIdentityKind.Item)
            throw new InvalidOperationException($"Equipped weapon '{item.EntityId}' has no durable item identity.");
        return new(weapon, _itemInstances.RequireUnique(identity.Value).Material);
    }
    internal string PlayerWeaponSkill() => ReadWeapon(_equipment.Read(), "right-hand")?.Weapon.Skill
        ?? ReadWeapon(_equipment.Read(), "left-hand")?.Weapon.Skill
        ?? DaggerfallMechanicsIds.HandToHand.Value;

    /// <summary>
    /// The damage modifier a player attack carries into the damage roll. Racial modifiers require a
    /// weapon; expert proficiency also applies to hand-to-hand under the selected donor policy. An enemy
    /// attempt carries none of them.
    /// </summary>
    private int PlayerDamageModifier(DaggerfallAttackDefinition attack)
    {
        int level = _actors.Player.Progression.Level;
        int skill = attack.Material is null ? 0 : (int)(_actors.Player.Stats.GetStat(StatId.Parse(attack.Skill)).ValueInt * _attackTuning.Combatant.SkillDamagePerPoint);
        int proficiency = DaggerfallFormulaPolicy.CalculateProficiencyDamage(
            _character()?.Career.ExpertProficiencies.Contains(attack.Skill, StringComparer.Ordinal) == true, level);
        int racial = attack.Material is not null
            ? DaggerfallFormulaPolicy.CalculateRacialDamage(DonorRaceId(), attack.Skill == DaggerfallSkills.Archery, level)
            : 0;
        return checked(skill + proficiency + racial);
    }

    /// <summary>
    /// The career's bonus or penalty against the target's enemy group. The player acts through the
    /// chosen career's attack-modifier byte, every other actor through its authored career or, for a
    /// monster, the byte its classic enemy configuration record carried. A vampire player is undead;
    /// other player identities retain the humanoid modifier.
    /// </summary>
    private int EnemyTypeBonus(Combatant attacker, Combatant target)
    {
        int flags = AttackModifierFlagsFor(attacker);
        return flags == 0 ? 0 : DaggerfallFormulaPolicy.BonusOrPenaltyByEnemyType(flags,
            target.Id == PlayerId ? PlayerCurse()?.IsVampire == true ? DaggerfallEnemyGroup.Undead : DaggerfallEnemyGroup.Humanoid
                : DaggerfallFormulaPolicy.EnemyGroupFor(target.Definition),
            AttackerLevel(attacker));
    }

    private int AttackModifierFlagsFor(Combatant attacker)
    {
        if (attacker.Id == PlayerId) return _character()?.Career.AttackModifierFlags ?? 0;
        if (attacker.Definition.Career is string careerId && _catalog.Catalogs.TryGetCareer(careerId, out DaggerfallCareerDefinition? career))
            return career.AttackModifierFlags;
        return _catalog.Mobiles.ForActor(attacker.Definition.Id.Value)?.AttackModifierFlags ?? 0;
    }

    private int AttackerLevel(Combatant attacker) => attacker.Id == PlayerId
        ? _actors.Player.Progression.Level
        : attacker.Definition.Level ?? 0;

    /// <summary>
    /// The donor's race number for the player's race: the donor's racial attack modifiers are keyed by
    /// classic race index rather than the product's race identity. A character with no catalogued race
    /// carries no racial modifier.
    /// </summary>
    private int DonorRaceId() => _character() is DaggerfallCharacterState character
        && _catalog.Catalogs.TryGetRace(character.Identity.RaceId, out DaggerfallRaceDefinition? race)
        ? race.DonorRaceId
        : 0;

    // The shared physical harm operations delivery resolves and applies harm through.
    CombatResolution IDaggerfallPhysicalHarmOperations.Rules => Rules;
    DaggerfallContactMitigation IDaggerfallPhysicalHarmOperations.Mitigation => _mitigation;
    AttackExecution<IProductFact> IDaggerfallPhysicalHarmOperations.Execution => Execution;
    DaggerfallDefinitions IDaggerfallPhysicalHarmOperations.Catalog => _catalog;
    bool IDaggerfallPhysicalHarmOperations.TryResolve(long id, out Combatant combatant) => TryResolve(id, out combatant);
    bool IDaggerfallPhysicalHarmOperations.IsDefeated(long id) => _actors.Get(id).IsDefeated;
    bool IDaggerfallPhysicalHarmOperations.IsLiveCombatant(long id) => IsLiveCombatant(id);
    string? IDaggerfallPhysicalHarmOperations.DefaultAction(long attacker) => _definitions[attacker].ActionId;
    CombatParticipants IDaggerfallPhysicalHarmOperations.Participants(long attacker, long target, string action) => Participants(attacker, target, action);
    int IDaggerfallPhysicalHarmOperations.Draw(ExplicitMeleeRequest request, long attacker, long target, int salt, int minimum, int maximum, bool enemy) =>
        Draw(request, attacker, target, salt, minimum, maximum, enemy);
    Action<DamageEvent> IDaggerfallPhysicalHarmOperations.DamageRoll(ExplicitMeleeRequest request, Combatant attacker, Combatant target,
        DaggerfallAttackDefinition attack, bool enemy, int attackDamageMod) =>
        DamageRoll(request, attacker, target, attack, enemy, attackDamageMod);
    int IDaggerfallPhysicalHarmOperations.PlayerDamageModifier(DaggerfallAttackDefinition attack) => PlayerDamageModifier(attack);
    int IDaggerfallPhysicalHarmOperations.EnemyTypeBonus(Combatant attacker, Combatant target) => EnemyTypeBonus(attacker, target);
    EquipmentRead IDaggerfallPhysicalHarmOperations.EquipmentOf(long owner) => owner == PlayerId ? _equipment.Read() : _actorEquipment(owner).Read();
    int IDaggerfallPhysicalHarmOperations.AttackChanceModifier() => _attackChanceModifier();
    void IDaggerfallPhysicalHarmOperations.RecordSkillUse(DaggerfallSkillUse use) => _skillUses(use);
    void IDaggerfallPhysicalHarmOperations.ApplyMonsterConsequences(AttackRequest request, long target, IReadOnlyList<DaggerfallMonsterHit> slots, FactBuffer<IProductFact> facts)
    {
        foreach (DaggerfallMonsterHit slot in slots)
        {
            if (slot.Consequence == DaggerfallMonsterHitConsequence.Fatigue)
                ApplyFatigueConsequence(request.AttackerId, target, slot.Damage, request.Generation, request.SimulationStep, facts);
            else if (slot.Consequence != DaggerfallMonsterHitConsequence.None)
                _monsterHit(new(request.AttackerId, target, request.Generation, request.SimulationStep, slot));
        }
    }
    void IDaggerfallPhysicalHarmOperations.DeliverWeaponPoison(long target, DaggerfallWeaponPoisonSource coating) => _deliverWeaponPoison(target, coating);
    bool IDaggerfallPhysicalHarmOperations.TryTransform(AttackRequest request, PreparedAttack attack, FactBuffer<IProductFact> facts, out ActorTransformedFact? transformation) =>
        TryTransform(request, attack, facts, out transformation);
    void IDaggerfallPhysicalHarmOperations.WearAttackersWeapon(AttackRequest request, long target, int damage, FactBuffer<IProductFact> facts)
    {
        if (EquippedWeapon(request.AttackerId) is { } source)
            DamageCondition(source, request.AttackerId, ConditionUnits(request.AttackerId, target, damage, request.AttackerId != PlayerId,
                request.Generation, request.SimulationStep, CombatRandomKey.WeaponConditionSalt), request.Generation, request.SimulationStep, facts);
    }
    string IDaggerfallPhysicalHarmOperations.PlayerWeaponSkill() => PlayerWeaponSkill();
    bool IDaggerfallPhysicalHarmOperations.TargetUnaware(long target) => _targetUnaware(target);
    void IDaggerfallPhysicalHarmOperations.AlertTarget(long target) => _alertTarget(target);
    double IDaggerfallPhysicalHarmOperations.BackstabMultiplier(Combatant attacker) => _attackTuning.Combatant.BackstabMultiplier(ReadStat(attacker, new DaggerfallStatId("backstabbing")));
    bool IDaggerfallPhysicalHarmOperations.PassesThrough(long target, bool harmsCivilians) => _passesThrough(target, harmsCivilians);
    void IDaggerfallPhysicalHarmOperations.Strike(AttackRequest request, long target, string attack, FactBuffer<IProductFact> facts) =>
        _strikes?.Land(request, target, attack, facts);
    void IDaggerfallPhysicalHarmOperations.DrainFatigue(AttackRequest request, long target, int damage, FactBuffer<IProductFact> facts) =>
        ApplyFatigueConsequence(request.AttackerId, target, damage, request.Generation, request.SimulationStep, facts);
    long? IDaggerfallPhysicalHarmOperations.TakeSweptTarget()
    {
        long? target = _sweptTarget;
        _sweptTarget = null;
        return target;
    }
    void IDaggerfallPhysicalHarmOperations.RecordPlayerWeaponHit(string weaponSkill) => RecordPlayerWeaponHit(weaponSkill);
    int IDaggerfallPhysicalHarmOperations.ItemStrike(long attacker, long target, DaggerfallItemStrikeSource source, int damage) => _itemStrike(attacker, target, source, damage);
    bool IDaggerfallPhysicalHarmOperations.WieldsRazor(long attacker) => WieldsRazor(attacker);
    void IDaggerfallPhysicalHarmOperations.ApplyDamage(CombatParticipants participants, long attacker, long target, int damage, int body, bool enemy,
        ulong generation, ulong step, DaggerfallStrikeFeedback feedback, FactBuffer<IProductFact> facts,
        (WorldRpg.Kit.Inventory.UniqueInventoryItem Weapon, ulong Identity)? mace, bool emitHitFact, int? admittedBaseDamage, WorldRpg.Kit.Inventory.UniqueInventoryItem? rangedWeapon) =>
        ApplyDamage(participants, attacker, target, damage, body, enemy, generation, step, feedback, facts, mace, emitHitFact, admittedBaseDamage, rangedWeapon);
    double? IDaggerfallPhysicalHarmOperations.ReachOf(long attacker) => ReachOf(attacker);
    ActorContact IDaggerfallPhysicalHarmOperations.Contact => _contact
        ?? throw new InvalidOperationException("Contact delivery needs the session's actor contact, which this composition did not supply.");
    CharacterStepEnvironment? IDaggerfallPhysicalHarmOperations.ContactEnvironment() => _contactEnvironment?.Invoke();
    Vector3 IDaggerfallPhysicalHarmOperations.VelocityOf(long actor) => _actorVelocity(actor);
    DaggerfallStrikePose? IDaggerfallPhysicalHarmOperations.StrikePose(long actor) => (_strikePose
        ?? throw new InvalidOperationException("Contact delivery needs the session's strike poses, which this composition did not supply."))(actor);
    void IDaggerfallPhysicalHarmOperations.ApplyConnectedHarm(AttackRequest request, long target, DaggerfallPreparedAttack attack, FactBuffer<IProductFact> facts)
    {
        AttackOutcome outcome = attack.Outcome;
        bool enemyAttack = request.AttackerId != PlayerId;
        // Natural consequences and coatings belong to positive admitted physical hits, before
        // enchantments or health absorption can change the result or defeat the target.
        ((IDaggerfallPhysicalHarmOperations)this).ApplyMonsterConsequences(request, target, attack.MonsterHits ?? [], facts);
        if (outcome.Damage > 0 && attack.WeaponPoison is { } coating) _deliverWeaponPoison(target, coating);
        // Wabbajack's replacement inherits prior wounds before the removed enemy receives this strike's damage.
        if (TryTransform(request, attack, facts, out ActorTransformedFact? transformation))
        {
            string? weaponSkill = request.AttackerId == PlayerId ? PlayerWeaponSkill() : null;
            if (outcome.Allowed && outcome.Damage > 0)
                ((IDaggerfallPhysicalHarmOperations)this).WearAttackersWeapon(request, target, outcome.Damage, facts);
            if (weaponSkill is not null) RecordPlayerWeaponHit(weaponSkill);
            if (_actors.Get(target).IsDefeated)
                facts.Append(new ActorDiedFact(target, request.AttackerId, DaggerfallDamageCause.Effect, 0, 0, request.Generation, request.SimulationStep));
            return;
        }
        int strikeDamage = outcome.Damage;
        // An enemy's weapon runs its Strikes payloads against the player only on a damaging blow;
        // the player's own weapon, and one enemy striking another, run them on every swing.
        bool strikes = !(enemyAttack && target == PlayerId && strikeDamage <= 0);
        if (strikes && attack.ItemStrikeSource is { } sourceItem)
            strikeDamage = _itemStrike(request.AttackerId, target, sourceItem, strikeDamage);
        string action = attack.Action ?? request.Action ?? _definitions[request.AttackerId].ActionId ?? "attack";
        // Capture the weapon skill at the admitted operation boundary. Applying the hit may break
        // the weapon through physical wear and unequip it before the skill-use reaction runs.
        string? playerWeaponSkill = request.AttackerId == PlayerId ? attack.Admission?.Attack.Skill ?? PlayerWeaponSkill() : null;
        ApplyDamage(Participants(request.AttackerId, target, action), request.AttackerId, target, strikeDamage, attack.Body,
            enemyAttack, request.Generation, request.SimulationStep, attack.Feedback, facts, attack.MolagBalSource,
            admittedBaseDamage: strikeDamage != outcome.Damage ? outcome.Damage : null, rangedWeapon: attack.RangedWeapon, mitigated: attack.Mitigated);
        if (playerWeaponSkill is not null) RecordPlayerWeaponHit(playerWeaponSkill);
        if (transformation is not null) facts.Append(transformation);
    }

    private int StrengthModifier(Combatant attacker) => DaggerfallFormulaPolicy.DamageModifier(ReadStat(attacker, DaggerfallMechanicsIds.Strength));
    internal static int ReadStat(Combatant actor, DaggerfallStatId stat) =>
        actor.Stats.GetStat(StatId.Parse(stat.Value)).ValueInt;
    private int Draw(ExplicitMeleeRequest request, long attacker, long target, int salt, int minimum, int maximum, bool enemy) => checked((int)_random.DrawKeyed(new KeyedRngRequest(DaggerfallRandomKey.Seed, enemy ? CombatRandomKey.EnemyScope : DaggerfallRandomKey.PlayerScope, DaggerfallRandomKey.For(request.Generation, request.SimulationStep, attacker, target, salt), minimum, maximum)).Value);
    /// <summary>A live combat participant: its durable id, live stats and authored definition.</summary>
    internal readonly record struct Combatant(long Id, StatsComponent Stats, DaggerfallActorDefinition Definition)
    {
        internal Track Health => Stats.GetTrack(TrackId.Parse(HealthTrack));
    }
    private readonly record struct DaggerfallEquippedWeapon(DaggerfallWeaponDefinition Weapon, string Material);
}
