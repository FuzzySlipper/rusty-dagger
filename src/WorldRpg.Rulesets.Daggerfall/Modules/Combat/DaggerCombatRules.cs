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
    private readonly IRandomService _random;
    internal CombatResolution Rules { get; }
    private readonly ActorsState _actors;
    private readonly MechanicsEquipmentCoordinator _equipment;
    private readonly Func<long, MechanicsInventoryCoordinator?> _actorInventories;
    /// <summary>The selected physical mitigation seam owner: classic's material requirement unless a variant claims it.</summary>
    private readonly IDaggerfallPhysicalMitigation _mitigation;
    private readonly Func<long, MechanicsEquipmentCoordinator> _actorEquipment;
    private readonly DaggerfallItemInstances _itemInstances;
    private readonly DaggerfallItemConditionService _itemCondition;
    private readonly DaggerfallDefinitions _catalog;
    private readonly IReadOnlyDictionary<string, int> _weaponMaterialModifiers;
    private readonly IReadOnlyDictionary<string, DaggerfallActionDefinition> _actions;
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _definitions;
    private readonly Action<DaggerfallSkillUse> _skillUses;
    private readonly Func<int> _playerBiographyAvoidHit;
    private readonly Func<long, DaggerfallAdrenalineRush> _adrenalineRush;
    private readonly Func<WorldPoint?> _playerPosition;
    private readonly Func<DaggerfallCharacterState?> _character;
    private readonly Func<DaggerfallSwingDirection> _playerSwing;
    /// <summary>Whether admitted static geometry stands between a shot's release and its aim.</summary>
    private readonly Func<WorldPoint, WorldPoint, bool> coverBlocksShot;
    /// <summary>The armor-value shift the defender's worn enchantments give, zero when none do.</summary>
    private readonly Func<int> _armorValueModifier;
    private readonly Func<int> _attackChanceModifier;
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
    /// <summary>The selected physical delivery seam owner: classic's to-hit and target-locked flight unless a variant claims it.</summary>
    private readonly IDaggerfallPhysicalDelivery _delivery;
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
        TargetingService targeting, Action<DaggerfallSkillUse> skillUses, Func<int> playerBiographyAvoidHit,
        Func<long, MechanicsEquipmentCoordinator> actorEquipment, DaggerfallItemConditionService itemCondition,
        CombatResolution rules, Func<long, DaggerfallAdrenalineRush> adrenalineRush,
        Func<WorldPoint?> playerPosition, Func<DaggerfallCharacterState?> character,
        Func<DaggerfallSwingDirection> playerSwing, Func<WorldPoint, WorldPoint, bool> coverBlocksShot,
        Func<int> armorValueModifier,
        Action<long, DaggerfallWeaponPoisonSource> deliverWeaponPoison, Func<int> attackChanceModifier,
        Func<long, long, ulong, ulong, ulong, DaggerfallWabbajackResult> transformActor, Func<long, DaggerfallMagicDefense> magicDefense,
        Func<long, bool> physicalAttacksBlocked,
        Func<long, long, ulong, int, int, ulong, ulong, (double Magicka, int Strength)> molagBalStrike, Func<long, long, DaggerfallItemStrikeSource, int, int> itemStrike,
        Action<DaggerfallMonsterHitExposure> monsterHit, Func<long, bool> actorGameplayActive, Func<long, string?> actorTeam,
        IDaggerfallPhysicalMitigation? mitigation = null, Func<IDaggerfallPhysicalHarmOperations, IDaggerfallPhysicalDelivery>? delivery = null,
        ActorContact? contact = null, Func<long, DaggerfallStrikePose?>? strikePose = null, Func<CharacterStepEnvironment?>? contactEnvironment = null)
    {
        _contact = contact;
        _strikePose = strikePose;
        _contactEnvironment = contactEnvironment;
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        _actorInventories = actorInventories ?? throw new ArgumentNullException(nameof(actorInventories));
        _itemInstances = itemInstances ?? throw new ArgumentNullException(nameof(itemInstances));
        _catalog = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _definitions = definitionsByEntity ?? throw new ArgumentNullException(nameof(definitionsByEntity));
        Targeting = targeting ?? throw new ArgumentNullException(nameof(targeting));
        _skillUses = skillUses ?? throw new ArgumentNullException(nameof(skillUses));
        _playerBiographyAvoidHit = playerBiographyAvoidHit ?? throw new ArgumentNullException(nameof(playerBiographyAvoidHit));
        _actorEquipment = actorEquipment ?? throw new ArgumentNullException(nameof(actorEquipment));
        _itemCondition = itemCondition ?? throw new ArgumentNullException(nameof(itemCondition));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _adrenalineRush = adrenalineRush ?? throw new ArgumentNullException(nameof(adrenalineRush));
        _playerPosition = playerPosition ?? throw new ArgumentNullException(nameof(playerPosition));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _playerSwing = playerSwing ?? throw new ArgumentNullException(nameof(playerSwing));
        this.coverBlocksShot = coverBlocksShot ?? throw new ArgumentNullException(nameof(coverBlocksShot));
        _armorValueModifier = armorValueModifier ?? throw new ArgumentNullException(nameof(armorValueModifier));
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
        _mitigation = mitigation ?? new DaggerfallClassicPhysicalMitigation(character);
        _weaponMaterialModifiers = DaggerfallFormulaPolicy.ClassicWeaponMaterialModifiers;
        _actions = definitions.Actions;
        _delivery = delivery?.Invoke(this) ?? new DaggerfallClassicPhysicalDelivery(this);
        Execution = new(actors, this, DeferRangedImpact, _delivery.ImpactsOutliveTheirTarget);
        Attacks = new(PlayerId, Targeting, Execution, ReachOf, facts => facts.Append(new AttackRejectedFact(AttackRejection.MissingPlayerPosition)),
            _delivery.UntargetedSwingsWaitForImpact);
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
        bool rangedAttack = !enemyAttack && PlayerHoldsBow();
        string? selectedAction = request.Action;
        WorldRpg.Kit.Inventory.UniqueInventoryItem? rangedWeapon = null;
        DaggerfallSwingDirection gesture = DaggerfallSwingDirection.None;
        if (!enemyAttack)
        {
            if (!TryAdmitPlayerAttack(request, attacker, out attack, out gesture, facts)) return false;
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
            if (rangedAttack && !TrySpendArrow(request.AttackerId, facts)) return false;
        }
        DaggerfallStrikeFeedback feedback = rangedAttack ? new(true, "sound.3") : StrikeFeedback(request.AttackerId, false);
        ulong? wabbajackSource = rangedAttack ? null : CaptureWabbajackSource(request.AttackerId);
        var molagBalSource = rangedAttack ? null : CaptureMolagBalSource(request.AttackerId);
        var poisonSource = CaptureWeaponPoison(request.AttackerId, rangedWeapon);
        var strikeSource = DaggerfallItemCastTriggers.CaptureStrike(_catalog.Magic, _itemInstances, _actors.Entities, rangedWeapon ?? EquippedWeapon(request.AttackerId));
        DaggerfallAdmittedAttack admitted = new(request, attacker, attack, gesture, rangedAttack, selectedAction, feedback,
            wabbajackSource, molagBalSource, poisonSource, strikeSource, rangedWeapon);
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
        // An already released projectile remains its own admitted flight; paralysis stops new strikes and releases.
        if (_physicalAttacksBlocked(request.AttackerId) && attack is not DaggerfallPreparedAttack { ReleasedProjectile: true })
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
    /// own admitted flight. The delivery owner takes every other released shot into its flight.
    /// </summary>
    private bool DeferRangedImpact(DeferredAttackImpact impact, FactBuffer<IProductFact> facts)
    {
        if (impact.Request.Delayed && impact.Attack is DaggerfallPreparedAttack { IsRanged: true } && _physicalAttacksBlocked(impact.Request.AttackerId))
        { facts.Append(new AttackRejectedFact(AttackRejection.Incapacitated, impact.Request.AttackerId)); return true; }
        return _delivery.DeferImpact(impact, facts);
    }

    /// <summary>Advances the delivery owner's flights one admitted step.</summary>
    internal void AdvanceRangedFlight(ulong generation, ulong simulationStep, double fixedDeltaSeconds,
        IReadOnlyDictionary<long, WorldPoint> positions, FactBuffer<IProductFact> facts) =>
        _delivery.AdvanceFlight(generation, simulationStep, fixedDeltaSeconds, positions, facts);

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
        string? id = requested ?? (bow ? RangedPlayerActionId() : player.Definition.ActionId);
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

    private bool TryAdmitPlayerAttack(AttackRequest request, Combatant player, out DaggerfallAttackDefinition attack,
        out DaggerfallSwingDirection gesture, FactBuffer<IProductFact> facts)
    {
        ulong generation = request.Generation, simulationStep = request.SimulationStep;
        gesture = DaggerfallSwingDirection.None;
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
        int staminaCost = playerAction.StaminaCost!.Value;
        bool ranged = playerAction.Interpretation == "player-equipped-ranged";
        double playerCooldown = ranged
            ? DaggerfallFormulaPolicy.BowCooldownSeconds(ReadStat(player, DaggerfallMechanicsIds.Speed))
            : playerAction.CooldownSeconds!.Value;
        // The shot is refused before it is admitted, from the same quiver owner the enemy archer uses,
        // so a player with an empty quiver stops shooting rather than silently missing.
        if (ranged && !TrySpendArrow(PlayerId, facts)) { attack = default!; return false; }
        attack = equippedWeapon is { } selectedWeapon
            ? new DaggerfallAttackDefinition(selectedWeapon.Weapon.Skill, selectedWeapon.Weapon.MinimumDamage, selectedWeapon.Weapon.MaximumDamage,
                playerCooldown, selectedWeapon.Material, playerAction.DamageBonus)
            : new DaggerfallAttackDefinition(
                "hand-to-hand",
                DaggerfallFormulaPolicy.HandToHandMinimumDamage(ReadStat(player, DaggerfallMechanicsIds.HandToHand)),
                DaggerfallFormulaPolicy.HandToHandMaximumDamage(ReadStat(player, DaggerfallMechanicsIds.HandToHand)),
                playerCooldown,
                DamageBonus: playerAction.DamageBonus);
        SpendPlayerStamina(player, staminaCost);
        // Melee consumes its gesture once. Bows play their fixed release strike, but use neutral
        // swing modifiers: these rules prepare damage before flight and do not read presentation
        // state at landing (the provisional DEC-11 bow rule).
        gesture = ranged ? DaggerfallSwingDirection.None : _playerSwing();
        // A targeted swing hands its timing to the strike animation: the tick time the classic
        // animation plays at, and the target whose impact frame will deliver it.
        // A swing waits for its hit frame when execution delays it: one with a target, or, where the
        // delivery owner decides contact there, one without.
        bool deferred = request.Delayed;
        facts.Append(new PlayerAttackStartedFact(generation, simulationStep,
            deferred ? request.TargetId : null,
            deferred ? DaggerfallFormulaPolicy.MeleeWeaponAnimationSeconds(ReadStat(player, DaggerfallMechanicsIds.Speed)) : 0d,
            DaggerfallFormulaPolicy.WeaponHitFrame(ranged),
            ranged ? DaggerfallFormulaPolicy.BowSwing : gesture, WaitsWithoutTarget: deferred && request.TargetId is null) { Feedback = StrikeFeedback(PlayerId) });
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
    /// Charges the admitted swing's fatigue, clamped at zero. The donor (WeaponManager) never refuses a
    /// swing for fatigue; a swing that empties the pool leaves the session's exhaustion owner to decide.
    /// </summary>
    private static void SpendPlayerStamina(Combatant player, int staminaCost)
    {
        Track stamina = player.Stats.GetTrack(TrackId.Parse(StaminaTrack));
        _ = stamina.Spend(Math.Min(stamina.Current, staminaCost));
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
        DaggerfallAttackDefinition attack, bool enemy, int attackDamageMod, int backstabChance) => damage =>
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
        // The donor triples on a successful backstab roll only when the backstab chance exceeds one,
        // so a one-point chance never rolls; the bounded zero floor is the donor's own final clamp.
        if (backstabChance > 1)
            rolled = DaggerfallFormulaPolicy.CalculateBackstabDamage(rolled, backstabChance,
                Draw(request, attacker.Id, target.Id, CombatRandomKey.BackstabRollSalt, 1, 100, enemy) <= backstabChance);
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
    /// The separate track mutation and fact prevent a stamina consequence from being misreported as health loss.
    /// </summary>
    private void ApplyFatigueConsequence(long attacker, long target, int acceptedHealthDamage, ulong generation, ulong step, FactBuffer<IProductFact> facts)
    {
        if (acceptedHealthDamage <= 0) return;
        if (!TryResolve(target, out Combatant victim)) return;

        Track stamina = victim.Stats.GetTrack(TrackId.Parse(StaminaTrack));
        double before = stamina.Current;
        int calculated = DaggerfallFormulaPolicy.FatigueDamage(acceptedHealthDamage);
        stamina.SetCurrent(Math.Max(stamina.Minimum, before - calculated), clamp: true);
        double actual = before - stamina.Current;
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
        DaggerfallStrikeFeedback Feedback, ulong? WabbajackSource = null, bool ReleasedProjectile = false,
        (WorldRpg.Kit.Inventory.UniqueInventoryItem Weapon, ulong Identity)? MolagBalSource = null, DaggerfallItemStrikeSource? ItemStrikeSource = null, DaggerfallWeaponPoisonSource? WeaponPoison = null,
        DaggerfallMonsterHit[]? MonsterHits = null, DaggerfallAttackDetail Detail = default, bool IsRanged = false, WorldRpg.Kit.Inventory.UniqueInventoryItem? RangedWeapon = null, string? Action = null,
        bool ContactInFlight = false, DaggerfallPhysicalHarm? Harm = null, DaggerfallAdmittedAttack? Admission = null,
        DaggerfallHarmMitigation Mitigated = default) : PreparedAttack(CooldownSeconds, Outcome);

    /// <summary>The struck body part and the d100 hit roll against its chance, kept for hit facts and miss reports.</summary>
    internal readonly record struct DaggerfallAttackDetail(int Body, int Roll, int Chance);

    private static DaggerfallStrikeFeedback PreparedFeedback(PreparedAttack attack) => attack is DaggerfallPreparedAttack accepted
        ? accepted.Feedback : throw new InvalidOperationException("A Daggerfall strike requires its accepted source feedback; re-reading changed equipment would misidentify the strike.");

    // DaggerfallUnityItem.GetSwingSound: native weapon template semantics, not media filenames.
    private DaggerfallStrikeFeedback StrikeFeedback(long owner, bool? ranged = null)
    {
        if (ranged ?? IsRangedAction(owner)) return new(true, "sound.3");
        if (EquippedWeapon(owner) is not { } weapon) return DaggerfallStrikeFeedback.Unarmed;
        DaggerfallItemDefinition definition = _catalog.RequireItem(new DaggerfallItemId(weapon.Definition.Value));
        int? template = DaggerfallTemplateItemDefinitions.TemplateIndexForAuthoredItem(definition.Id) ?? definition.Template?.Index;
        return DaggerfallCombatFeedbackPolicy.ForWeaponTemplate(template);
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
    private string PlayerWeaponSkill() => ReadWeapon(_equipment.Read(), "right-hand")?.Weapon.Skill
        ?? ReadWeapon(_equipment.Read(), "left-hand")?.Weapon.Skill
        ?? DaggerfallMechanicsIds.HandToHand.Value;

    private bool BackstabOpportunity(Combatant attacker, Combatant target)
    {
        if (attacker.Id != PlayerId || target.Id == PlayerId || !_actors.TryGet(target.Id, out ActorState targetActor)
            || _playerPosition() is not WorldPoint playerPosition)
            return false;

        float dx = playerPosition.X - targetActor.Position.X;
        float dz = playerPosition.Z - targetActor.Position.Z;
        float distanceSquared = (dx * dx) + (dz * dz);
        if (!float.IsFinite(distanceSquared) || distanceSquared <= float.Epsilon)
            return false;

        // DFU rounds the target-to-player angle into eight 45-degree facings using
        // nearest-even ties, then treats facing indices 3, 4 and 5 as back-facing.
        // ActorPose's zero heading faces -Z and positive yaw turns toward +X.
        Vector3 targetForward = ActorHeading.Forward(targetActor.HeadingYawRadians);
        float inverseDistance = 1f / MathF.Sqrt(distanceSquared);
        float dot = Math.Clamp(((targetForward.X * dx) + (targetForward.Z * dz)) * inverseDistance, -1f, 1f);
        float facingSector = MathF.Acos(dot) / (MathF.PI / 4f);
        int roundedSector = (int)MathF.Round(facingSector, MidpointRounding.ToEven);
        return roundedSector is >= 3 and <= 5;
    }



    /// <summary>
    /// The to-hit and damage modifiers a player attack carries into both the hit roll and the damage
    /// roll. Swing and racial modifiers require a weapon; expert proficiency also applies to
    /// hand-to-hand under the selected donor policy. An enemy attempt carries none of them.
    /// </summary>
    private (int ToHit, int Damage) PlayerAttackModifiers(DaggerfallAttackDefinition attack, DaggerfallSwingDirection gesture)
    {
        int level = _actors.Player.Progression.Level;
        (int swingToHit, int swingDamage) = attack.Material is not null
            ? DaggerfallFormulaPolicy.CalculateSwingModifiers(gesture)
            : (0, 0);
        (int proficiencyToHit, int proficiencyDamage) = DaggerfallFormulaPolicy.CalculateProficiencyModifiers(
            _character()?.Career.ExpertProficiencies.Contains(attack.Skill, StringComparer.Ordinal) == true, level);
        (int racialToHit, int racialDamage) = attack.Material is not null
            ? DaggerfallFormulaPolicy.CalculateRacialModifiers(DonorRaceId(), attack.Skill == DaggerfallSkills.Archery, level)
            : (0, 0);
        return (checked(swingToHit + proficiencyToHit + racialToHit), checked(swingDamage + proficiencyDamage + racialDamage));
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
            target.Id == PlayerId ? _character()?.RacialOverrides?.Current?.IsVampire == true ? DaggerfallEnemyGroup.Undead : DaggerfallEnemyGroup.Humanoid
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

    // The shared physical harm operations a delivery owner resolves and applies harm through.
    CombatResolution IDaggerfallPhysicalHarmOperations.Rules => Rules;
    IDaggerfallPhysicalMitigation IDaggerfallPhysicalHarmOperations.Mitigation => _mitigation;
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
        DaggerfallAttackDefinition attack, bool enemy, int attackDamageMod, int backstabChance) =>
        DamageRoll(request, attacker, target, attack, enemy, attackDamageMod, backstabChance);
    (int ToHit, int Damage) IDaggerfallPhysicalHarmOperations.PlayerAttackModifiers(DaggerfallAttackDefinition attack, DaggerfallSwingDirection gesture) =>
        PlayerAttackModifiers(attack, gesture);
    bool IDaggerfallPhysicalHarmOperations.BackstabOpportunity(Combatant attacker, Combatant target) => BackstabOpportunity(attacker, target);
    int IDaggerfallPhysicalHarmOperations.EnemyTypeBonus(Combatant attacker, Combatant target) => EnemyTypeBonus(attacker, target);
    EquipmentRead IDaggerfallPhysicalHarmOperations.EquipmentOf(long owner) => owner == PlayerId ? _equipment.Read() : _actorEquipment(owner).Read();
    DaggerfallAdrenalineRush IDaggerfallPhysicalHarmOperations.AdrenalineRush(long actor) => _adrenalineRush(actor);
    int IDaggerfallPhysicalHarmOperations.AttackChanceModifier() => _attackChanceModifier();
    int IDaggerfallPhysicalHarmOperations.ArmorValueModifier() => _armorValueModifier();
    int IDaggerfallPhysicalHarmOperations.PlayerBiographyAvoidHit() => _playerBiographyAvoidHit();
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
    void IDaggerfallPhysicalHarmOperations.RecordPlayerWeaponHit(string weaponSkill) => RecordPlayerWeaponHit(weaponSkill);
    int IDaggerfallPhysicalHarmOperations.ItemStrike(long attacker, long target, DaggerfallItemStrikeSource source, int damage) => _itemStrike(attacker, target, source, damage);
    bool IDaggerfallPhysicalHarmOperations.WieldsRazor(long attacker) => WieldsRazor(attacker);
    void IDaggerfallPhysicalHarmOperations.ApplyDamage(CombatParticipants participants, long attacker, long target, int damage, int body, bool enemy,
        ulong generation, ulong step, DaggerfallStrikeFeedback feedback, FactBuffer<IProductFact> facts,
        (WorldRpg.Kit.Inventory.UniqueInventoryItem Weapon, ulong Identity)? mace, bool emitHitFact, int? admittedBaseDamage, WorldRpg.Kit.Inventory.UniqueInventoryItem? rangedWeapon) =>
        ApplyDamage(participants, attacker, target, damage, body, enemy, generation, step, feedback, facts, mace, emitHitFact, admittedBaseDamage, rangedWeapon);
    bool IDaggerfallPhysicalHarmOperations.CoverBlocksShot(WorldPoint origin, WorldPoint aim) => coverBlocksShot(origin, aim);
    double? IDaggerfallPhysicalHarmOperations.ReachOf(long attacker) => ReachOf(attacker);
    ActorContact IDaggerfallPhysicalHarmOperations.Contact => _contact
        ?? throw new InvalidOperationException("Contact delivery needs the session's actor contact, which this composition did not supply.");
    CharacterStepEnvironment? IDaggerfallPhysicalHarmOperations.ContactEnvironment() => _contactEnvironment?.Invoke();
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
        string? playerWeaponSkill = request.AttackerId == PlayerId ? PlayerWeaponSkill() : null;
        ApplyDamage(Participants(request.AttackerId, target, action), request.AttackerId, target, strikeDamage, attack.Detail.Body,
            enemyAttack, request.Generation, request.SimulationStep, attack.Feedback, facts, attack.MolagBalSource,
            admittedBaseDamage: strikeDamage != outcome.Damage ? outcome.Damage : null, rangedWeapon: attack.RangedWeapon, mitigated: attack.Mitigated);
        if (playerWeaponSkill is not null) RecordPlayerWeaponHit(playerWeaponSkill);
        if (transformation is not null) facts.Append(transformation);
    }

    internal static int CalculateHitChance(int skill, int struckArmor, int attackerLuck, int targetLuck, int attackerAgility, int targetAgility, int targetDodge, int targetBiographyAvoidHit = 0) => DaggerfallFormulaPolicy.CalculateHitChance(skill, struckArmor, attackerLuck, targetLuck, attackerAgility, targetAgility, targetDodge, targetBiographyAvoidHit);
    private int StrengthModifier(Combatant attacker) => DaggerfallFormulaPolicy.DamageModifier(ReadStat(attacker, DaggerfallMechanicsIds.Strength));
    internal static int ReadStat(Combatant actor, DaggerfallStatId stat) =>
        actor.Stats.GetStat(StatId.Parse(stat.Value)).ValueInt;
    private int Draw(ExplicitMeleeRequest request, long attacker, long target, int salt, int minimum, int maximum, bool enemy) => checked((int)_random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, enemy ? CombatRandomKey.EnemyScope : CombatRandomKey.PlayerScope, CombatRandomKey.For(request.Generation, request.SimulationStep, attacker, target, salt), minimum, maximum)).Value);
    /// <summary>A live combat participant: its durable id, live stats and authored definition.</summary>
    internal readonly record struct Combatant(long Id, StatsComponent Stats, DaggerfallActorDefinition Definition)
    {
        internal Track Health => Stats.GetTrack(TrackId.Parse(HealthTrack));
    }
    private readonly record struct DaggerfallEquippedWeapon(DaggerfallWeaponDefinition Weapon, string Material);
}

internal readonly record struct RangedShotIdentity(ulong Generation, long AttackerId, long TargetId, ulong ReleaseStep);

/// <summary>A transient flight's current world pose, read from the canonical combat queue.</summary>
internal readonly record struct DaggerfallRangedFlightView(RangedShotIdentity Identity, WorldPoint Position, Vector3 Direction);
