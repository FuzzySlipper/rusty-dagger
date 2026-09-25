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
internal sealed class DaggerCombatRules : IAttackRules<IProductFact>
{
    private const long PlayerId = DaggerfallActorIdentity.PlayerEntityId;
    private const string HealthTrack = "health";
    private const string StaminaTrack = "stamina";
    private readonly IRandomService _random;
    internal CombatResolution Rules { get; }
    private readonly ActorsState _actors;
    private readonly MechanicsEquipmentCoordinator _equipment;
    private readonly Func<long, MechanicsInventoryCoordinator?> _actorInventories;
    private readonly Func<long, MechanicsEquipmentCoordinator> _actorEquipment;
    private readonly DaggerfallItemInstances _itemInstances;
    private readonly DaggerfallItemConditionService? _itemCondition;
    private readonly DaggerfallDefinitions _catalog;
    private readonly IReadOnlyDictionary<string, int> _weaponMaterialRanks;
    private readonly IReadOnlyDictionary<string, int> _weaponMaterialModifiers;
    private readonly IReadOnlyDictionary<string, DaggerfallActionDefinition> _actions;
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _definitions;
    private readonly Action<DaggerfallSkillUse>? _skillUses;
    private readonly Func<int> _playerBiographyAvoidHit;
    private readonly Func<long, DaggerfallAdrenalineRush> _adrenalineRush;
    private readonly Func<WorldPoint?> _playerPosition;
    private readonly Func<DaggerfallCharacterState?> _character;
    private readonly Func<DaggerfallSwingDirection> _playerSwing;
    /// <summary>Whether admitted static geometry stands between a shot's release and its aim.</summary>
    private readonly Func<WorldPoint, WorldPoint, bool>? coverBlocksShot;
    /// <summary>The armor-value shift the defender's worn enchantments give, zero when none do.</summary>
    private readonly Func<int> _armorValueModifier;
    internal AttackCapabilities<IProductFact> Attacks { get; }
    internal TargetingService Targeting { get; }
    internal AttackExecution<IProductFact> Execution { get; }
    private readonly List<DeferredAttackImpact> _releasedRangedShots = [];
    private readonly Dictionary<RangedShotIdentity, InFlightRangedShot> _inFlightRangedShots = [];
    // The pack's arrow item is the one ammunition the adopted ranged shots draw. A second ranged
    // action with different ammunition would move this name onto the authored action.
    private const string ArrowItemId = "arrow";
    // The classic skeleton mobile: its mobile id, as the donor's skeleton-warrior damage adjustment keys on it.
    private const int SkeletalWarriorMobileId = 15;

    internal DaggerCombatRules(IRandomService random, ActorsState actors, MechanicsEquipmentCoordinator equipment,
        Func<long, MechanicsInventoryCoordinator?> actorInventories, DaggerfallItemInstances itemInstances,
        DaggerfallDefinitions definitions, IReadOnlyDictionary<long, DaggerfallActorDefinition> definitionsByEntity,
        TargetingService targeting, Action<DaggerfallSkillUse>? skillUses = null, Func<int>? playerBiographyAvoidHit = null,
        Func<long, MechanicsEquipmentCoordinator>? actorEquipment = null, DaggerfallItemConditionService? itemCondition = null,
        CombatResolution? rules = null, Func<long, DaggerfallAdrenalineRush>? adrenalineRush = null,
        Func<WorldPoint?>? playerPosition = null, Func<DaggerfallCharacterState?>? character = null,
        Func<DaggerfallSwingDirection>? playerSwing = null, Func<WorldPoint, WorldPoint, bool>? coverBlocksShot = null,
        Func<int>? armorValueModifier = null)
    {
        _random = random;
        Rules = rules ?? new CombatResolution();
        Execution = new(actors, this, DeferRangedImpact);
        _actors = actors;
        _equipment = equipment;
        _actorInventories = actorInventories;
        _actorEquipment = actorEquipment ?? (_ => equipment);
        _itemInstances = itemInstances ?? throw new ArgumentNullException(nameof(itemInstances));
        _itemCondition = itemCondition;
        _catalog = definitions;
        _weaponMaterialRanks = DaggerfallFormulaPolicy.ClassicWeaponMaterialRanks;
        _weaponMaterialModifiers = DaggerfallFormulaPolicy.ClassicWeaponMaterialModifiers;
        _actions = definitions.Actions;
        _definitions = definitionsByEntity;
        _skillUses = skillUses;
        _playerBiographyAvoidHit = playerBiographyAvoidHit ?? (() => 0);
        _adrenalineRush = adrenalineRush ?? (_ => default);
        _playerPosition = playerPosition ?? (() => null);
        _character = character ?? (() => null);
        _playerSwing = playerSwing ?? (() => DaggerfallSwingDirection.None);
        this.coverBlocksShot = coverBlocksShot;
        _armorValueModifier = armorValueModifier ?? (() => 0);
        Targeting = targeting;
        Attacks = new(PlayerId, Targeting, Execution, ReachOf, facts => facts.Append(new AttackRejectedFact(AttackRejection.MissingPlayerPosition)));
    }

    internal void ResolveExplicit(ExplicitMeleeRequest request, FactBuffer<IProductFact> facts)
    {
        request.Validate();
        if (request.AttackerId != PlayerId) throw new ArgumentException("Enemy attacks use the delayed attack capability.", nameof(request));
        Execution.Start(new(request.AttackerId, request.TargetId, request.Generation, request.SimulationStep, request.FixedDeltaSeconds, false, request.Action?.Value), facts);
    }
    public void Refused(AttackRefusal reason, FactBuffer<IProductFact> facts) => facts.Append(new AttackRejectedFact(reason switch
    {
        AttackRefusal.UnknownActor => AttackRejection.UnknownExplicitCombatant,
        AttackRefusal.TargetDefeated => AttackRejection.TargetDefeated,
        _ => AttackRejection.Cooldown,
    }));
    public bool TryPrepare(AttackRequest request, FactBuffer<IProductFact> facts, out PreparedAttack prepared)
    {
        prepared = default;
        if (!TryResolve(request.AttackerId, out Combatant attacker)) { Refused(AttackRefusal.UnknownActor, facts); return false; }
        // Whether an attack is the player's or an enemy's decides the admission policy, the random
        // scope and which facts describe it. Delivery timing is a separate question: a targeted
        // player swing and every enemy swing wait for their animation's impact frame.
        bool enemyAttack = request.AttackerId != PlayerId;
        DaggerfallAttackDefinition attack;
        if (!enemyAttack)
        {
            if (!TryAdmitPlayerAttack(request, attacker, out attack, facts)) return false;
        }
        else
        {
            if (attacker.Definition.ActionId is not string actionId || !_actions.TryGetValue(actionId, out var action)
                || action.CooldownSeconds is not double cooldown || action.Reach is not > 0d)
            { facts.Append(new AttackRejectedFact(AttackRejection.NoAttackPolicy, request.AttackerId)); return false; }
            attack = action.Interpretation == "enemy-equipped-melee"
                ? ResolveEquippedEnemyAttack(attacker, cooldown)
                : ResolveFixedAttack(attacker.Definition, action, cooldown);
            if (action.Interpretation == "fixed-ranged" && !TrySpendArrow(request.AttackerId, facts)) return false;
        }
        if (request.TargetId is not long targetId)
        { prepared = new(attack.CooldownSeconds, default); return true; }
        if (!TryResolve(targetId, out Combatant target)) { Refused(AttackRefusal.UnknownActor, facts); return false; }
        ExplicitMeleeRequest explicitRequest = new(request.AttackerId, targetId, request.Generation, request.SimulationStep, request.FixedDeltaSeconds);
        CombatParticipants participants = Participants(attacker.Id, targetId, request.Action ?? attacker.Definition.ActionId ?? "attack");
        bool backstabOpportunity = BackstabOpportunity(attacker, target);
        if (backstabOpportunity)
            _skillUses?.Invoke(new DaggerfallSkillUse("backstabbing", DaggerfallSkillUseReason.BackstabbingOpportunity, DaggerfallSkillUseOutcome.Accepted));
        // The donor computes the swing, proficiency and racial attack modifiers once and rides both
        // the hit roll and the damage roll with them; the career's enemy-type bonus and the backstab
        // chance cross the same two rolls at their own points. Resolve them once here and carry them
        // to both resolutions so one admitted attack shares one set of modifier values.
        var modifiers = attacker.Id == PlayerId ? PlayerAttackModifiers(attack) : default;
        int backstabChance = DaggerfallFormulaPolicy.CalculateBackstabChance(ReadStat(attacker, new DaggerfallStatId("backstabbing")), backstabOpportunity);
        int body = DaggerfallFormulaPolicy.CalculateStruckBodyPart(Draw(explicitRequest, attacker.Id, target.Id, CombatRandomKey.BodySalt, 0, 19, enemyAttack));
        if (attacker.Definition.Kind == DaggerfallActorKinds.Monster && attack.Skill == DaggerfallMechanicsIds.HandToHand.Value)
        {
            prepared = new(attack.CooldownSeconds, MonsterAttackSet(participants, explicitRequest, attacker, target, attack, body, enemyAttack));
            return true;
        }
        TryHitEvent hit = ResolveHit(participants, explicitRequest, attacker, target, attack, body, enemyAttack, modifiers.ToHit, backstabChance);
        DamageEvent? damage = hit.Hit ? ResolveDamage(participants, explicitRequest, attacker, target, attack, body, enemyAttack, modifiers.Damage, backstabChance) : null;
        prepared = new(attack.CooldownSeconds, new(hit.Hit, damage?.Allowed ?? true, body, damage?.Damage ?? 0, hit.Roll, hit.Chance));
        return true;
    }
    public void Started(AttackRequest request, PreparedAttack attack, FactBuffer<IProductFact> facts)
    {
        // A player swing announces itself at admission, where its guard and stamina cost live; an
        // enemy swing announces the decision its authored damage frame will carry out.
        if (request.AttackerId != PlayerId) facts.Append(new EnemyAttackStartedFact(request.AttackerId, request.TargetId!.Value, attack.Outcome.Hit, request.Generation, request.SimulationStep));
    }
    public void Apply(AttackRequest request, PreparedAttack attack, FactBuffer<IProductFact> facts)
    {
        if (request.TargetId is not long target)
        { facts.Append(new AttackRejectedFact(AttackRejection.NoTargetInReach)); return; }
        AttackOutcome outcome = attack.Outcome;
        bool enemyAttack = request.AttackerId != PlayerId;
        // This is the one admitted resolution boundary an enemy attempt reaches. The donor tallies
        // Dodging before damage, so a resolved miss contributes too; a rejected or unknown attack
        // never reaches this method and therefore cannot manufacture a use.
        if (request.AttackerId != PlayerId && target == PlayerId)
            _skillUses?.Invoke(new DaggerfallSkillUse(DaggerfallMechanicsIds.Dodging.Value, DaggerfallSkillUseReason.DodgingEnemyAttack, DaggerfallSkillUseOutcome.Attempted));
        if (!outcome.Hit)
        { facts.Append(new AttackMissedFact(request.AttackerId, target, outcome.Roll, outcome.Chance, enemyAttack, request.Generation, request.SimulationStep)); return; }
        if (!outcome.Allowed)
        { facts.Append(new AttackRejectedFact(AttackRejection.InsufficientWeaponMaterial)); return; }
        string action = request.Action ?? _definitions[request.AttackerId].ActionId ?? "attack";
        // Capture the weapon skill at the admitted operation boundary. Applying the hit may break
        // the weapon through physical wear and unequip it before the skill-use reaction runs.
        string? playerWeaponSkill = request.AttackerId == PlayerId ? PlayerWeaponSkill() : null;
        ApplyDamage(Participants(request.AttackerId, target, action), request.AttackerId, target, outcome.Damage, outcome.Body,
            enemyAttack, request.Generation, request.SimulationStep, facts);
        if (request.AttackerId == PlayerId)
        {
            _skillUses?.Invoke(new DaggerfallSkillUse(playerWeaponSkill!, DaggerfallSkillUseReason.WeaponHit, DaggerfallSkillUseOutcome.Succeeded));
            _skillUses?.Invoke(new DaggerfallSkillUse("critical-strike", DaggerfallSkillUseReason.CriticalStrikeHit, DaggerfallSkillUseOutcome.Succeeded));
        }
    }

    /// <summary>
    /// The authored release frame consumes the pending attack, while a fixed-ranged attack stays
    /// in this ruleset-owned transient queue until the session's admitted update advances it.
    /// Daggerfall Unity uses a travelling missile; this approximation still renders no arrow, so only
    /// the target's current position can dodge the release aim. Admitted static geometry between the
    /// release and the aim is asked of the caller's own Engine query: a shot that meets cover lands
    /// nowhere.
    /// </summary>
    private bool DeferRangedImpact(DeferredAttackImpact impact, FactBuffer<IProductFact> facts)
    {
        if (!impact.Request.Delayed || !IsRangedAction(impact.Request.AttackerId)) return false;
        _releasedRangedShots.Add(impact);
        return true;
    }

    /// <summary>Launches releases at their actual current admitted step, then resolves arrived shots.</summary>
    internal void AdvanceRangedFlight(ulong generation, ulong simulationStep, double fixedDeltaSeconds,
        IReadOnlyDictionary<long, WorldPoint> positions, FactBuffer<IProductFact> facts)
    {
        if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds <= 0d) throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));

        foreach (DeferredAttackImpact release in _releasedRangedShots)
        {
            if (release.Request.Generation != generation || !IsLiveCombatant(release.Request.AttackerId)
                || release.Request.TargetId is not long targetId || !IsLiveCombatant(targetId)
                || !positions.TryGetValue(release.Request.AttackerId, out WorldPoint origin)
                || !positions.TryGetValue(targetId, out WorldPoint aim)) continue;

            // Cover is asked once, on the line the shot was released along: a missile that meets a wall
            // dies there, so neither the shooter's roll nor the target's later movement decides it.
            if (coverBlocksShot is not null && coverBlocksShot(origin, aim))
            {
                facts.Append(new RangedShotBlockedFact(release.Request.AttackerId, targetId, generation, simulationStep));
                continue;
            }

            ulong arrival = checked(simulationStep + RequiredFlightSteps(origin, aim, fixedDeltaSeconds));
            RangedShotIdentity identity = new(generation, release.Request.AttackerId, targetId, simulationStep);
            _inFlightRangedShots[identity] = new(release, origin, aim, arrival);
        }
        _releasedRangedShots.Clear();

        foreach ((RangedShotIdentity identity, InFlightRangedShot shot) in _inFlightRangedShots.ToArray())
        {
            if (identity.Generation != generation || !IsLiveCombatant(shot.Release.Request.AttackerId)
                || shot.Release.Request.TargetId is not long targetId || !IsLiveCombatant(targetId))
            {
                _inFlightRangedShots.Remove(identity);
                continue;
            }
            if (simulationStep < shot.ArriveAtStep) continue;
            _inFlightRangedShots.Remove(identity);
            if (!positions.TryGetValue(targetId, out WorldPoint currentTarget)) continue;

            bool dodged = Vector3.Distance(currentTarget.ToVector(), shot.Aim.ToVector()) > ArrowDodgeRadiusMeters;
            if (!shot.Release.Attack.Outcome.Hit || dodged)
            {
                AttackOutcome outcome = shot.Release.Attack.Outcome;
                facts.Append(new AttackMissedFact(shot.Release.Request.AttackerId, targetId, outcome.Roll, outcome.Chance,
                    EnemyAttack: shot.Release.Request.AttackerId != PlayerId, shot.Release.Request.Generation, shot.Release.Request.SimulationStep));
                continue;
            }
            Execution.ApplyDeferredImpact(shot.Release, facts);
        }
    }

    /// <summary>A shot is any attack whose own action carries it to a target beyond a swing: the
    /// enemy's authored fixed-ranged action, or the player's ranged action while a bow is held.</summary>
    private bool IsRangedAction(long attackerId)
    {
        if (attackerId == PlayerId) return PlayerHoldsBow() && RangedPlayerActionId() is not null;
        return _definitions.TryGetValue(attackerId, out DaggerfallActorDefinition? actor)
            && actor.ActionId is string actionId && _actions.TryGetValue(actionId, out DaggerfallActionDefinition? action)
            && action.Interpretation == "fixed-ranged";
    }

    /// <summary>The bow the player is holding, in either hand, if any.</summary>
    private bool PlayerHoldsBow()
    {
        EquipmentRead equipment = _equipment.Read();
        return ReadWeapon(equipment, "right-hand") is { Weapon.Skill: var right } && right == DaggerfallSkills.Archery
            || ReadWeapon(equipment, "left-hand") is { Weapon.Skill: var left } && left == DaggerfallSkills.Archery;
    }

    private bool IsLiveCombatant(long actorId)
    {
        if (actorId == PlayerId) return ! _actors.Player.IsDefeated && _definitions.ContainsKey(actorId);
        return _actors.TryGet(actorId, out ActorState actor) && !actor.IsDefeated && _definitions.ContainsKey(actorId);
    }

    private static ulong RequiredFlightSteps(WorldPoint origin, WorldPoint target, double fixedDeltaSeconds)
    {
        double distance = Vector3.Distance(origin.ToVector(), target.ToVector());
        return checked((ulong)Math.Max(1d, Math.Ceiling((distance / ArrowSpeedMetersPerSecond) / fixedDeltaSeconds)));
    }

    // DFU DaggerfallMissile moves at 25m/s. The accepted ruleset approximation uses a 0.45m
    // target-position dodge radius and asks the caller's cover query about the release line; it still
    // renders no arrow and does not collide with an intervening actor's body, which the erratum and
    // the receiving task for the projectile visual record.
    private const double ArrowSpeedMetersPerSecond = 25d;
    private const float ArrowDodgeRadiusMeters = .45f;
    private readonly record struct RangedShotIdentity(ulong Generation, long AttackerId, long TargetId, ulong ReleaseStep);
    private readonly record struct InFlightRangedShot(DeferredAttackImpact Release, WorldPoint Origin, WorldPoint Aim, ulong ArriveAtStep);

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
        if (!quiver.Read().Stacks.Any(stack => stack.Definition.Value == ArrowItemId && stack.Quantity > 0))
        {
            facts.Append(new AttackRejectedFact(AttackRejection.EmptyQuiver, shooterId));
            return false;
        }
        InventoryStackId stack = quiver.Read().Stacks
            .Where(value => value.Definition.Value == ArrowItemId)
            .OrderBy(value => value.Id.Value, StringComparer.Ordinal)
            .Select(value => value.Id)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("The archer's selected arrow stack disappeared before the shot could consume it.");
        InventoryMutationReceipt receipt = quiver.Consume(new InventoryConsume(stack, 1));
        if (receipt.AfterQuantity == 0)
            _itemInstances.RemoveStack(DaggerfallItemOwner.Actor(shooterId), stack);
        return true;
    }

    private bool TryAdmitPlayerAttack(AttackRequest request, Combatant player, out DaggerfallAttackDefinition attack, FactBuffer<IProductFact> facts)
    {
        ulong generation = request.Generation, simulationStep = request.SimulationStep;
        DaggerfallActionId? action = request.Action is string requested ? new DaggerfallActionId(requested) : null;
        EquipmentRead equipment = _equipment.Read();
        DaggerfallEquippedWeapon? equippedWeapon = ReadWeapon(equipment, "right-hand") ?? ReadWeapon(equipment, "left-hand");
        bool bowShot = equippedWeapon is { } heldWeapon && heldWeapon.Weapon.Skill == DaggerfallSkills.Archery;
        string? selectedActionId = action?.Value ?? (bowShot ? RangedPlayerActionId() : player.Definition.ActionId);
        if (selectedActionId is null || !_actions.TryGetValue(selectedActionId, out DaggerfallActionDefinition? playerAction)
            || playerAction.Interpretation is not ("player-equipped-melee" or "player-equipped-ranged")
            || playerAction.StaminaCost is not int staminaCost
            || playerAction.Interpretation == "player-equipped-ranged" && !bowShot)
        {
            attack = default!;
            facts.Append(new AttackRejectedFact(AttackRejection.NoAttackPolicy));
            return false;
        }
        bool ranged = playerAction.Interpretation == "player-equipped-ranged";
        // A bow's cadence is the donor's own formula over live speed; a swing's is the action's.
        if (!ranged && playerAction.CooldownSeconds is not double)
        {
            attack = default!;
            facts.Append(new AttackRejectedFact(AttackRejection.NoAttackPolicy));
            return false;
        }
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
        if (!SpendPlayerStamina(player, staminaCost, facts)) return false;
        // A targeted swing hands its timing to the strike animation: the tick time the classic
        // animation plays at, and the target whose impact frame will deliver it.
        bool deferred = request.Delayed && request.TargetId is not null;
        facts.Append(new PlayerAttackStartedFact(generation, simulationStep,
            deferred ? request.TargetId : null,
            deferred ? DaggerfallFormulaPolicy.MeleeWeaponAnimationSeconds(ReadStat(player, DaggerfallMechanicsIds.Speed)) : 0d,
            ranged ? DaggerfallFormulaPolicy.BowWeaponHitFrame : DaggerfallFormulaPolicy.MeleeWeaponHitFrame));
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

    private bool SpendPlayerStamina(Combatant player, int staminaCost, FactBuffer<IProductFact> facts)
    {
        Track stamina = player.Stats.GetTrack(TrackId.Parse(StaminaTrack));
        if (stamina.Current < staminaCost)
        {
            facts.Append(new AttackRejectedFact(AttackRejection.InsufficientStamina));
            return false;
        }
        if (stamina.TrySpend(staminaCost)) return true;
        facts.Append(new AttackRejectedFact(AttackRejection.StaminaSpendNotAccepted));
        return false;
    }

    private CombatParticipants Participants(long attacker, long target, string action) => new(
        new Actor(_actors.Store, _actors.Entities.Resolve(ActorsState.Identity(attacker))),
        new Actor(_actors.Store, _actors.Entities.Resolve(ActorsState.Identity(target))), action);

    private TryHitEvent ResolveHit(CombatParticipants participants, ExplicitMeleeRequest request, Combatant attacker, Combatant target,
        DaggerfallAttackDefinition attack, int body, bool enemy, int attackToHitMod, int backstabChance,
        int hitSalt = CombatRandomKey.HitSalt, int criticalSalt = CombatRandomKey.CriticalStrikeSalt) => Rules.TryHit(participants, hit =>
    {
        hit.Chance = HitChance(request, attacker, target, attack, body, enemy, attackToHitMod, backstabChance, criticalSalt);
        hit.Roll = Draw(request, attacker.Id, target.Id, hitSalt, 1, 100, enemy);
    });

    /// <summary>
    /// Resolves the damage roll behind the accepted hit in the donor's order: a weapon whose material
    /// cannot cut the target's minimum material rejects the attempt before anything rolls; the attack
    /// then takes its damage through the hand-to-hand or weapon path, a
    /// successful backstab roll triples what those paths produced, and the total is bounded at zero.
    /// </summary>
    /// <remarks>
    /// The donor returns before its hit roll for an insufficient weapon material; here the shared hit
    /// roll is keyed by (generation, step, attacker, target, salt) rather than by draw sequence, so
    /// drawing it before rejecting the attempt cannot change any other roll's value.
    /// </remarks>
    private DamageEvent ResolveDamage(CombatParticipants participants, ExplicitMeleeRequest request, Combatant attacker, Combatant target,
        DaggerfallAttackDefinition attack, int body, bool enemy, int attackDamageMod, int backstabChance) => Rules.Damage(participants, damage =>
    {
        damage.Body = body;
        if (attack.Material is not null && !DaggerfallFormulaPolicy.CanHitMaterial(attack.Material, target.Definition.MinimumMaterial, _weaponMaterialRanks))
        {
            damage.Allowed = false;
            damage.Damage = 0;
            return;
        }

        damage.Allowed = true;
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
    });

    /// <summary>
    /// A monster's natural attacks for one attempt: the classic loop works through up to three
    /// authored attack slots, and each slot must clear the player's reflexes chance and the attack's
    /// own hit chance before it rolls damage, with the career's enemy-type bonus carried only by a
    /// slot that landed positive damage.
    /// </summary>
    /// <remarks>
    /// The donor rolls the reflexes check even for slots whose minimum damage is zero; keyed draws are
    /// addressed per (generation, step, attacker, target, salt) rather than by draw order, so skipping
    /// those slots cannot shift any other roll. Every eligible slot resolves through the shared hit
    /// rules independently. Damage contributions and application see the combined attack once.
    /// </remarks>
    private AttackOutcome MonsterAttackSet(CombatParticipants participants, ExplicitMeleeRequest request,
        Combatant attacker, Combatant target, DaggerfallAttackDefinition attack, int body, bool enemy)
    {
        int enemyType = EnemyTypeBonus(attacker, target);
        int reflexChance = DaggerfallFormulaPolicy.MonsterAttackReflexChance(ReadStat(PlayerCombatant(), DaggerfallMechanicsIds.Reflexes));
        int total = 0;
        bool landed = false;
        TryHitEvent? representative = null;
        int slots = Math.Min(3, attacker.Definition.Attacks.Count);
        for (int slot = 0; slot < slots; slot++)
        {
            int slotMinimum = attacker.Definition.Attacks[slot].MinimumDamage;
            if (slotMinimum <= 0) continue;
            if (Draw(request, attacker.Id, target.Id, CombatRandomKey.MonsterReflexSaltBase + slot, 1, 100, enemy) > reflexChance) continue;
            TryHitEvent hit = ResolveHit(participants, request, attacker, target, attack, body, enemy, 0, 0,
                CombatRandomKey.MonsterHitSaltBase + slot, CombatRandomKey.MonsterCriticalSaltBase + slot);
            // Retain a landed slot for the shared outcome, or the last miss when none landed.
            if (hit.Hit || !landed) representative = hit;
            if (!hit.Hit) continue;
            landed = true;
            int slotDamage = Draw(request, attacker.Id, target.Id, CombatRandomKey.MonsterDamageSaltBase + slot, slotMinimum, attacker.Definition.Attacks[slot].MaximumDamage, enemy);
            total = checked(total + slotDamage);
            if (slotDamage > 0) total = checked(total + enemyType);
        }

        DamageEvent? damage = landed ? Rules.Damage(participants, damage =>
        {
            damage.Body = body;
            damage.Damage = Math.Max(0, total);
        }) : null;
        return new(landed, damage?.Allowed ?? true, body, damage?.Damage ?? 0,
            representative?.Roll ?? 0, representative?.Chance ?? 0);
    }

    private void ApplyDamage(CombatParticipants participants, long attacker, long target, int damage, int body, bool enemy,
        ulong generation, ulong step, FactBuffer<IProductFact> facts)
    {
        Track health = participants.TargetStats.GetTrack(TrackId.Parse(HealthTrack));
        ApplyHitEvent applied = Rules.ApplyToHealth(participants, damage, body, health);
        facts.Append(new AttackHitFact(attacker, target, applied.CalculatedDamage, applied.ActualHealthLost, body, enemy, generation, step));
        facts.Append(new DamageAppliedFact(attacker, target, DaggerfallDamageCause.PhysicalAttack,
            applied.CalculatedDamage, applied.ActualHealthLost, body, generation, step));
        if (applied.ActualHealthLost > 0)
            facts.Append(new ActorDamagedFact(target, attacker, DaggerfallDamageCause.PhysicalAttack,
                applied.CalculatedDamage, applied.ActualHealthLost));
        if (applied.Defeated)
            facts.Append(new ActorDiedFact(target, attacker, DaggerfallDamageCause.PhysicalAttack,
                applied.CalculatedDamage, applied.ActualHealthLost, generation, step));
        if (applied.ActualHealthLost > 0d)
            ApplyFatigueConsequence(attacker, target, applied.Damage, generation, step, facts);
        if (applied.Damage > 0) ApplyPhysicalWear(attacker, target, body, applied.Damage, enemy, generation, step, facts);
    }

    /// <summary>
    /// FormulaHelper applies this after a nymph hit; DFU extends the same donor helper to lamias.
    /// The separate track mutation and fact prevent a stamina consequence from being misreported as health loss.
    /// </summary>
    private void ApplyFatigueConsequence(long attacker, long target, int acceptedHealthDamage, ulong generation, ulong step, FactBuffer<IProductFact> facts)
    {
        if (acceptedHealthDamage <= 0 || !_definitions.TryGetValue(attacker, out DaggerfallActorDefinition? source)
            || source.Id.Value is not ("nymph" or "lamia")) return;
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
    private void ApplyPhysicalWear(long attacker, long target, int body, int damage, bool enemy, ulong generation, ulong step, FactBuffer<IProductFact> facts)
    {
        if (_itemCondition is null) return;
        if (EquippedWeapon(attacker) is not WorldRpg.Kit.Inventory.UniqueInventoryItem weapon) return;
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
            ? _itemCondition!.Damage(item, units)
            : _itemCondition!.Damage(item, DaggerfallItemOwner.Actor(owner), _actorEquipment(owner), units);
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

    /// <summary>Armour worn on the struck body part. Classic never wears the armour a shield already covers.</summary>
    private WorldRpg.Kit.Inventory.UniqueInventoryItem? EquippedArmour(long owner, int body)
    {
        string slot = body switch
        {
            0 => "head", 1 => "right-arm", 2 => "left-arm", 3 => "chest-armor",
            4 => "gloves", 5 => "legs-armor", 6 => "feet", _ => throw new ArgumentOutOfRangeException(nameof(body)),
        };
        EquipmentRead equipment = owner == PlayerId ? _equipment.Read() : _actorEquipment(owner).Read();
        return equipment.TryGet(new WorldRpg.Kit.Inventory.EquipmentSlotId(slot), out WorldRpg.Kit.Inventory.UniqueInventoryItem armor)
            && _catalog.RequireItem(new DaggerfallItemId(armor.Definition.Value)).Armor is not null ? armor : null;
    }

    private static bool ShieldCovers(DaggerfallItemDefinition shield, int body) => shield.Id.Value switch
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
        if (_actors.TryGet(id, out ActorState actor) && _definitions.TryGetValue(id, out DaggerfallActorDefinition? definition)) { combatant = new(id, actor.Stats, definition); return true; }
        combatant = default;
        return false;
    }

    /// <summary>
    /// One enemy attempt's attack: the equipped weapon's authored damage when the enemy wears one, and
    /// the classic hand-to-hand range scaled by the actor's own hand-to-hand skill when it wears none.
    /// A weapon-wielding enemy uses its equipped weapon even where its unarmed numbers would be
    /// higher — the donor's stronger-unarmed substitution is a documented deviation it does not keep.
    /// </summary>
    private DaggerfallAttackDefinition ResolveEquippedEnemyAttack(Combatant attacker, double cooldown)
    {
        DaggerfallEquippedWeapon? weapon = ReadWeapon(_actorEquipment(attacker.Id).Read(), "right-hand")
            ?? ReadWeapon(_actorEquipment(attacker.Id).Read(), "left-hand");
        return weapon is { } selected
            ? new DaggerfallAttackDefinition(selected.Weapon.Skill, selected.Weapon.MinimumDamage, selected.Weapon.MaximumDamage, cooldown, selected.Material, Reach: 2d)
            : new DaggerfallAttackDefinition(DaggerfallMechanicsIds.HandToHand.Value,
                DaggerfallFormulaPolicy.HandToHandMinimumDamage(ReadStat(attacker, DaggerfallMechanicsIds.HandToHand)),
                DaggerfallFormulaPolicy.HandToHandMaximumDamage(ReadStat(attacker, DaggerfallMechanicsIds.HandToHand)),
                cooldown, Reach: 2d);
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
    private int HitChance(ExplicitMeleeRequest request, Combatant attacker, Combatant target, DaggerfallAttackDefinition attack, int body, bool enemy, int attackToHitMod, int backstabChance, int criticalSalt)
    {
        int critical = ReadStat(attacker, new DaggerfallStatId("critical-strike"));
        bool criticalSucceeded = Draw(request, attacker.Id, target.Id, criticalSalt, 1, 100, enemy) <= critical;
        DaggerfallAdrenalineRush attackerRush = _adrenalineRush(attacker.Id), targetRush = _adrenalineRush(target.Id);
        // The donor assembles the skill value, the player's swing/proficiency/racial modifiers and the
        // backstab chance into one chance-to-hit modifier, then adds the weapon material inside its
        // weapon adjustment seam; an unarmed attempt never reaches that seam.
        int chanceToHitMod = checked(ReadStat(attacker, new DaggerfallStatId(attack.Skill)) + attackToHitMod + backstabChance);
        if (attack.Material is not null)
            chanceToHitMod = DaggerfallFormulaPolicy.AdjustWeaponHitChanceMod(checked(
                chanceToHitMod + DaggerfallFormulaPolicy.CalculateWeaponToHit(attack.Material, _weaponMaterialModifiers)));
        return DaggerfallFormulaPolicy.CalculateSuccessfulHitChance(
            chanceToHitMod,
            ArmorToHit(target, body),
            DaggerfallFormulaPolicy.CalculateAdrenalineRushToHit(attackerRush.Enabled, attackerRush.Improved, Health(attacker).Current, Health(attacker).Maximum.Value, targetRush.Enabled, targetRush.Improved, Health(target).Current, Health(target).Maximum.Value),
            DaggerfallFormulaPolicy.CalculateStatsToHit(ReadStat(attacker, DaggerfallMechanicsIds.Luck), ReadStat(target, DaggerfallMechanicsIds.Luck), ReadStat(attacker, DaggerfallMechanicsIds.Agility), ReadStat(target, DaggerfallMechanicsIds.Agility)),
            DaggerfallFormulaPolicy.CalculateSkillsToHit(ReadStat(target, DaggerfallMechanicsIds.Dodging), critical, criticalSucceeded),
            DaggerfallFormulaPolicy.CalculateAdjustmentsToHit(target.Definition.Kind == DaggerfallActorKinds.Monster, target.Id == PlayerId ? _playerBiographyAvoidHit() : 0));
    }

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
        Vector3 targetForward = new(MathF.Sin(targetActor.HeadingYawRadians), 0f, -MathF.Cos(targetActor.HeadingYawRadians));
        float inverseDistance = 1f / MathF.Sqrt(distanceSquared);
        float dot = Math.Clamp(((targetForward.X * dx) + (targetForward.Z * dz)) * inverseDistance, -1f, 1f);
        float facingSector = MathF.Acos(dot) / (MathF.PI / 4f);
        int roundedSector = (int)MathF.Round(facingSector, MidpointRounding.ToEven);
        return roundedSector is >= 3 and <= 5;
    }

    private int ArmorToHit(Combatant target, int body)
    {
        int equipmentBonus = 0;
        string bodySlot = body switch
        {
            0 => "head", 1 => "right-arm", 2 => "left-arm", 3 => "chest-armor",
            4 => "gloves", 5 => "legs-armor", 6 => "feet", _ => throw new ArgumentOutOfRangeException(nameof(body)),
        };
        EquipmentRead equipment = target.Id == PlayerId ? _equipment.Read() : _actorEquipment(target.Id).Read();
        foreach (WorldRpg.Kit.Inventory.EquipmentAssignment assignment in equipment.Assignments)
        {
            DaggerfallItemDefinition item = _catalog.RequireItem(new DaggerfallItemId(assignment.Item.Definition.Value));
            if (item.Armor is { } worn && assignment.Slot.Value == bodySlot)
                equipmentBonus = checked(equipmentBonus + (_catalog.ArmorValuesByMaterial[worn.Material] * 5));
            else if (item.Shield is { } shield && assignment.Slot.Value == "left-hand" && ShieldCovers(item, body))
                equipmentBonus = checked(equipmentBonus + (shield.Armor * 5));
        }

        // A worn StrengthensArmor enchantment shifts the defender's whole armor value, which is why it
        // applies once here rather than to any one piece the way a material's rating does. The donor
        // shifts the value down to make armour stronger, and this ruleset accumulates the value's
        // complement — ArmorToHit below turns the bonus back into the value the hit chance adds — so
        // the shift is subtracted from the bonus rather than added to it.
        if (target.Id == PlayerId) equipmentBonus = checked(equipmentBonus - _armorValueModifier());

        int equipmentArmor = checked(100 - equipmentBonus);
        return target.Definition.Kind switch
        {
            DaggerfallActorKinds.Monster => Math.Min(target.Definition.Armor, equipmentArmor),
            DaggerfallActorKinds.EnemyClass => Math.Min(60, equipmentArmor),
            _ => equipmentArmor,
        };
    }

    private static Track Health(Combatant actor) => actor.Stats.GetTrack(TrackId.Parse(HealthTrack));

    /// <summary>
    /// The to-hit and damage modifiers a player attack carries into both the hit roll and the damage
    /// roll. Swing and racial modifiers require a weapon; expert proficiency also applies to
    /// hand-to-hand under the selected donor policy. An enemy attempt carries none of them.
    /// </summary>
    private (int ToHit, int Damage) PlayerAttackModifiers(DaggerfallAttackDefinition attack)
    {
        int level = _actors.Player.Progression.Level;
        (int swingToHit, int swingDamage) = attack.Material is not null
            ? DaggerfallFormulaPolicy.CalculateSwingModifiers(_playerSwing())
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
    /// monster, the byte its classic enemy configuration record carried. A player target is humanoid
    /// until character vampirism exists to move it, matching the donor's own pending case.
    /// </summary>
    private int EnemyTypeBonus(Combatant attacker, Combatant target)
    {
        int flags = AttackModifierFlagsFor(attacker);
        return flags == 0 ? 0 : DaggerfallFormulaPolicy.BonusOrPenaltyByEnemyType(flags,
            target.Id == PlayerId ? DaggerfallEnemyGroup.Humanoid : DaggerfallFormulaPolicy.EnemyGroupFor(target.Definition),
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

    private Combatant PlayerCombatant() => TryResolve(PlayerId, out Combatant player)
        ? player
        : throw new InvalidOperationException("A monster's natural attacks measure the player's reflexes, and no player combatant is available to measure.");

    /// <summary>
    /// The donor's race number for the player's race: the donor's racial attack modifiers are keyed by
    /// classic race index rather than the product's race identity. A character with no catalogued race
    /// carries no racial modifier.
    /// </summary>
    private int DonorRaceId() => _character() is DaggerfallCharacterState character
        && _catalog.Catalogs.TryGetRace(character.Identity.RaceId, out DaggerfallRaceDefinition? race)
        ? race.DonorRaceId
        : 0;

    internal static int CalculateHitChance(int skill, int struckArmor, int attackerLuck, int targetLuck, int attackerAgility, int targetAgility, int targetDodge, int targetBiographyAvoidHit = 0) => DaggerfallFormulaPolicy.CalculateHitChance(skill, struckArmor, attackerLuck, targetLuck, attackerAgility, targetAgility, targetDodge, targetBiographyAvoidHit);
    private int StrengthModifier(Combatant attacker) => DaggerfallFormulaPolicy.DamageModifier(ReadStat(attacker, DaggerfallMechanicsIds.Strength));
    private static int ReadStat(Combatant actor, DaggerfallStatId stat) =>
        actor.Stats.GetStat(StatId.Parse(stat.Value)).ValueInt;
    private int Draw(ExplicitMeleeRequest request, long attacker, long target, int salt, int minimum, int maximum, bool enemy) => checked((int)_random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, enemy ? CombatRandomKey.EnemyScope : CombatRandomKey.PlayerScope, CombatRandomKey.For(request.Generation, request.SimulationStep, attacker, target, salt), minimum, maximum)).Value);
    private readonly record struct Combatant(long Id, StatsComponent Stats, DaggerfallActorDefinition Definition);
    private readonly record struct DaggerfallEquippedWeapon(DaggerfallWeaponDefinition Weapon, string Material);
}
