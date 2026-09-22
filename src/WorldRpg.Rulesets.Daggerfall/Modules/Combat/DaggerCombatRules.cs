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
    internal CombatResolution Rules { get; } = new();
    private readonly ActorsState _actors;
    private readonly MechanicsEquipmentCoordinator _equipment;
    private readonly Func<long, MechanicsInventoryCoordinator?> _actorInventories;
    private readonly DaggerfallItemInstances _itemInstances;
    private readonly IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> _items;
    private readonly IReadOnlyDictionary<string, int> _weaponMaterialRanks;
    private readonly IReadOnlyDictionary<string, DaggerfallActionDefinition> _actions;
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _definitions;
    private readonly Action<DaggerfallSkillUse>? _skillUses;
    internal AttackCapabilities<IProductFact> Attacks { get; }
    internal TargetingService Targeting { get; }
    internal AttackExecution<IProductFact> Execution { get; }
    private readonly List<DeferredAttackImpact> _releasedRangedShots = [];
    private readonly Dictionary<RangedShotIdentity, InFlightRangedShot> _inFlightRangedShots = [];
    // The pack's arrow item is the one ammunition the adopted ranged shots draw. A second ranged
    // action with different ammunition would move this name onto the authored action.
    private const string ArrowItemId = "arrow";

    internal DaggerCombatRules(IRandomService random, ActorsState actors, MechanicsEquipmentCoordinator equipment,
        Func<long, MechanicsInventoryCoordinator?> actorInventories, DaggerfallItemInstances itemInstances,
        DaggerfallDefinitions definitions, IReadOnlyDictionary<long, DaggerfallActorDefinition> definitionsByEntity,
        TargetingService targeting, Action<DaggerfallSkillUse>? skillUses = null)
    {
        _random = random;
        Execution = new(actors, this, DeferRangedImpact);
        _actors = actors;
        _equipment = equipment;
        _actorInventories = actorInventories;
        _itemInstances = itemInstances ?? throw new ArgumentNullException(nameof(itemInstances));
        _items = definitions.Items;
        _weaponMaterialRanks = DaggerfallFormulaPolicy.ClassicWeaponMaterialRanks;
        _actions = definitions.Actions;
        _definitions = definitionsByEntity;
        _skillUses = skillUses;
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
        DaggerfallAttackDefinition attack;
        if (!request.Delayed)
        {
            if (!TryAdmitPlayerAttack(request.Generation, request.SimulationStep, request.FixedDeltaSeconds,
                request.Action is string action ? new DaggerfallActionId(action) : null, out attack, facts)) return false;
        }
        else
        {
            if (attacker.Definition.ActionId is not string actionId || !_actions.TryGetValue(actionId, out var action)
                || action.CooldownSeconds is not double cooldown || action.Reach is not > 0d)
            { facts.Append(new AttackRejectedFact(AttackRejection.NoAttackPolicy, request.AttackerId)); return false; }
            attack = ResolveFixedAttack(attacker.Definition, action, cooldown);
            if (action.Interpretation == "fixed-ranged" && !TrySpendArrow(request.AttackerId, facts)) return false;
        }
        if (request.TargetId is not long targetId)
        { prepared = new(attack.CooldownSeconds, default); return true; }
        if (!TryResolve(targetId, out Combatant target)) { Refused(AttackRefusal.UnknownActor, facts); return false; }
        ExplicitMeleeRequest explicitRequest = new(request.AttackerId, targetId, request.Generation, request.SimulationStep, request.FixedDeltaSeconds);
        CombatParticipants participants = Participants(attacker.Id, targetId, request.Action ?? attacker.Definition.ActionId ?? "attack");
        TryHitEvent hit = ResolveHit(participants, explicitRequest, attacker, target, attack, request.Delayed);
        DamageEvent? damage = hit.Hit ? ResolveDamage(participants, explicitRequest, attacker, target, attack, request.Delayed) : null;
        prepared = new(attack.CooldownSeconds, new(hit.Hit, damage?.Allowed ?? true, damage?.Body ?? 0, damage?.Damage ?? 0, hit.Roll, hit.Chance));
        return true;
    }
    public void Started(AttackRequest request, PreparedAttack attack, FactBuffer<IProductFact> facts)
    {
        if (request.Delayed) facts.Append(new EnemyAttackStartedFact(request.AttackerId, request.TargetId!.Value, attack.Outcome.Hit, request.Generation, request.SimulationStep));
    }
    public void Apply(AttackRequest request, PreparedAttack attack, FactBuffer<IProductFact> facts)
    {
        if (request.TargetId is not long target)
        { facts.Append(new AttackRejectedFact(AttackRejection.NoTargetInReach)); return; }
        AttackOutcome outcome = attack.Outcome;
        // This is the one admitted resolution boundary an enemy attempt reaches. The donor tallies
        // Dodging before damage, so a resolved miss contributes too; a rejected or unknown attack
        // never reaches this method and therefore cannot manufacture a use.
        if (request.AttackerId != PlayerId && target == PlayerId)
            _skillUses?.Invoke(new DaggerfallSkillUse(DaggerfallMechanicsIds.Dodging.Value, DaggerfallSkillUseReason.DodgingEnemyAttack, DaggerfallSkillUseOutcome.Attempted));
        if (!outcome.Hit)
        { facts.Append(new AttackMissedFact(request.AttackerId, target, outcome.Roll, outcome.Chance, request.Delayed, request.Generation, request.SimulationStep)); return; }
        if (!outcome.Allowed)
        { facts.Append(new AttackRejectedFact(AttackRejection.InsufficientWeaponMaterial)); return; }
        string action = request.Action ?? _definitions[request.AttackerId].ActionId ?? "attack";
        ApplyDamage(Participants(request.AttackerId, target, action), request.AttackerId, target, outcome.Damage, outcome.Body,
            request.Delayed, request.Generation, request.SimulationStep, facts);
        if (request.AttackerId == PlayerId)
        {
            _skillUses?.Invoke(new DaggerfallSkillUse(PlayerWeaponSkill(), DaggerfallSkillUseReason.WeaponHit, DaggerfallSkillUseOutcome.Succeeded));
            _skillUses?.Invoke(new DaggerfallSkillUse("critical-strike", DaggerfallSkillUseReason.CriticalStrikeHit, DaggerfallSkillUseOutcome.Succeeded));
        }
    }

    /// <summary>
    /// The authored release frame consumes the pending attack, while a fixed-ranged attack stays
    /// in this ruleset-owned transient queue until the session's admitted update advances it.
    /// Daggerfall Unity uses a travelling missile; this approximation has no rendered arrow or
    /// static-cover SphereCast yet, so only the target's current position can dodge the release aim.
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
                    EnemyAttack: true, shot.Release.Request.Generation, shot.Release.Request.SimulationStep));
                continue;
            }
            Execution.ApplyDeferredImpact(shot.Release, facts);
        }
    }

    private bool IsRangedAction(long attackerId) => _definitions.TryGetValue(attackerId, out DaggerfallActorDefinition? actor)
        && actor.ActionId is string actionId && _actions.TryGetValue(actionId, out DaggerfallActionDefinition? action)
        && action.Interpretation == "fixed-ranged";

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
    // target-position dodge radius; its missing static-cover cast and visual arrow are documented above.
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

    private bool TryAdmitPlayerAttack(ulong generation, ulong simulationStep, double fixedDeltaSeconds, DaggerfallActionId? action, out DaggerfallAttackDefinition attack, FactBuffer<IProductFact> facts)
    {
        if (!TryResolve(PlayerId, out Combatant player))
        {
            attack = default!;
            facts.Append(new AttackRejectedFact(AttackRejection.UnknownExplicitCombatant));
            return false;
        }
        string? selectedActionId = action?.Value ?? player.Definition.ActionId;
        if (selectedActionId is null || !_actions.TryGetValue(selectedActionId, out DaggerfallActionDefinition? playerAction) || playerAction.Interpretation != "player-equipped-melee" || playerAction.CooldownSeconds is not double playerCooldown || playerAction.StaminaCost is not int staminaCost)
        {
            attack = default!;
            facts.Append(new AttackRejectedFact(AttackRejection.NoAttackPolicy));
            return false;
        }
        EquipmentRead equipment = _equipment.Read();
        DaggerfallWeaponDefinition? weapon = ReadWeapon(equipment, "right-hand") ?? ReadWeapon(equipment, "left-hand");
        attack = weapon is not null
            ? new DaggerfallAttackDefinition(weapon.Skill, weapon.MinimumDamage, weapon.MaximumDamage, playerCooldown, weapon.Material, playerAction.DamageBonus)
            : new DaggerfallAttackDefinition(
                "hand-to-hand",
                DaggerfallFormulaPolicy.HandToHandMinimumDamage(ReadStat(player, DaggerfallMechanicsIds.HandToHand)),
                DaggerfallFormulaPolicy.HandToHandMaximumDamage(ReadStat(player, DaggerfallMechanicsIds.HandToHand)),
                playerCooldown,
                DamageBonus: playerAction.DamageBonus);
        if (!SpendPlayerStamina(player, staminaCost, facts)) return false;
        facts.Append(new PlayerAttackStartedFact(generation, simulationStep));
        return true;
    }

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
        DaggerfallAttackDefinition attack, bool enemy) => Rules.TryHit(participants, hit =>
    {
        hit.Chance = HitChance(attacker, target, attack.Skill);
        hit.Roll = Draw(request, attacker.Id, target.Id, CombatRandomKey.HitSalt, 1, 100, enemy);
    });

    private DamageEvent ResolveDamage(CombatParticipants participants, ExplicitMeleeRequest request, Combatant attacker, Combatant target,
        DaggerfallAttackDefinition attack, bool enemy) => Rules.Damage(participants, damage =>
    {
        damage.Body = DaggerfallFormulaPolicy.StruckBodyPart(Draw(request, attacker.Id, target.Id, CombatRandomKey.BodySalt, 0, 19, enemy));
        int raw = Draw(request, attacker.Id, target.Id, CombatRandomKey.DamageSalt, attack.MinimumDamage, attack.MaximumDamage, enemy);
        damage.Allowed = enemy || attack.Material is null || DaggerfallFormulaPolicy.CanHitMaterial(attack.Material, target.Definition.MinimumMaterial, _weaponMaterialRanks);
        damage.Damage = Math.Max(1, checked(raw + StrengthModifier(attacker) + attack.DamageBonus));
    });

    private void ApplyDamage(CombatParticipants participants, long attacker, long target, int damage, int body, bool enemy,
        ulong generation, ulong step, FactBuffer<IProductFact> facts)
    {
        ApplyHitEvent applied = Rules.Apply(participants, damage, body, interaction =>
        {
            Track health = participants.TargetStats.GetTrack(TrackId.Parse(HealthTrack));
            int before = health.ValueInt;
            health.SetCurrent(Math.Max(health.Minimum, (double)before - Math.Max(0, interaction.Damage)), clamp: true);
            interaction.AppliedDamage = before - health.ValueInt;
            interaction.Killed = health.Current <= health.Minimum && before > health.Minimum;
        });
        facts.Append(new AttackHitFact(attacker, target, applied.AppliedDamage, body, enemy, generation, step));
        if (applied.AppliedDamage > 0) facts.Append(new ActorDamagedFact(target, applied.AppliedDamage));
        if (applied.Killed) facts.Append(new ActorDiedFact(target, attacker, applied.AppliedDamage, generation, step));
    }

    private bool TryResolve(long id, out Combatant combatant)
    {
        if (id == PlayerId && _definitions.TryGetValue(PlayerId, out DaggerfallActorDefinition? player)) { combatant = new(id, _actors.Player.Stats, player); return true; }
        if (_actors.TryGet(id, out ActorState actor) && _definitions.TryGetValue(id, out DaggerfallActorDefinition? definition)) { combatant = new(id, actor.Stats, definition); return true; }
        combatant = default;
        return false;
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
    public double? ReachOf(long entityId) => _definitions.TryGetValue(entityId, out DaggerfallActorDefinition? attacker)
        && attacker.ActionId is { } actionId
        && _actions.TryGetValue(actionId, out DaggerfallActionDefinition? action)
        && action.Reach is > 0d
        ? action.Reach
        : null;
    private DaggerfallWeaponDefinition? ReadWeapon(EquipmentRead equipment, string slot)
    {
        if (!equipment.TryGet(new WorldRpg.Kit.Inventory.EquipmentSlotId(slot), out WorldRpg.Kit.Inventory.UniqueInventoryItem item)
            || !_items.TryGetValue(new DaggerfallItemId(item.Definition.Value), out DaggerfallItemDefinition? definition)) return null;
        return definition.Weapon;
    }
    private string PlayerWeaponSkill() => ReadWeapon(_equipment.Read(), "right-hand")?.Skill
        ?? ReadWeapon(_equipment.Read(), "left-hand")?.Skill
        ?? DaggerfallMechanicsIds.HandToHand.Value;
    private int HitChance(Combatant attacker, Combatant target, string skill) => DaggerfallFormulaPolicy.CalculateHitChance(ReadStat(attacker, new DaggerfallStatId(skill)), target.Definition.Armor, ReadStat(attacker, DaggerfallMechanicsIds.Luck), ReadStat(target, DaggerfallMechanicsIds.Luck), ReadStat(attacker, DaggerfallMechanicsIds.Agility), ReadStat(target, DaggerfallMechanicsIds.Agility), ReadStat(target, DaggerfallMechanicsIds.Dodging));
    internal static int CalculateHitChance(int skill, int struckArmor, int attackerLuck, int targetLuck, int attackerAgility, int targetAgility, int targetDodge) => DaggerfallFormulaPolicy.CalculateHitChance(skill, struckArmor, attackerLuck, targetLuck, attackerAgility, targetAgility, targetDodge);
    private int StrengthModifier(Combatant attacker) => DaggerfallFormulaPolicy.DamageModifier(ReadStat(attacker, DaggerfallMechanicsIds.Strength));
    private static int ReadStat(Combatant actor, DaggerfallStatId stat) =>
        actor.Stats.GetStat(StatId.Parse(stat.Value)).ValueInt;
    private int Draw(ExplicitMeleeRequest request, long attacker, long target, int salt, int minimum, int maximum, bool enemy) => checked((int)_random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, enemy ? CombatRandomKey.EnemyScope : CombatRandomKey.PlayerScope, CombatRandomKey.For(request.Generation, request.SimulationStep, attacker, target, salt), minimum, maximum)).Value);
    private readonly record struct Combatant(long Id, StatsComponent Stats, DaggerfallActorDefinition Definition);
}
