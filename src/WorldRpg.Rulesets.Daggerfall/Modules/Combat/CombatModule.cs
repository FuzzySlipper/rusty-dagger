using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>Direct Daggerfall attack policy over Engine-authoritative Mechanics state.</summary>
internal sealed class CombatModule
{
    private const long PlayerId = DaggerfallActorIdentity.PlayerEntityId;
    private const string HealthTrack = "health";
    private const string StaminaTrack = "stamina";
    private readonly IRandomService _random;
    private readonly ActorsState _actors;
    private readonly MechanicsEquipmentCoordinator _equipment;
    private readonly IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> _items;
    private readonly IReadOnlyDictionary<string, int> _weaponMaterialRanks;
    private readonly IReadOnlyDictionary<string, DaggerfallActionDefinition> _actions;
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _definitions;
    private readonly DaggerfallMeleeTargetingModule _targeting;
    private readonly Dictionary<(ulong Generation, long Attacker), ulong> _readyAtStep = [];
    private readonly Dictionary<(ulong Generation, long Attacker), PendingEnemyImpact> _pendingImpacts = [];
    private readonly Dictionary<long, ulong> _restoredRemainingCooldowns = [];

    internal CombatModule(IRandomService random, ActorsState actors, MechanicsEquipmentCoordinator equipment, DaggerfallDefinitions definitions, IReadOnlyDictionary<long, DaggerfallActorDefinition> definitionsByEntity, DaggerfallMeleeTargetingModule targeting)
    {
        _random = random;
        _actors = actors;
        _equipment = equipment;
        _items = definitions.Items;
        _weaponMaterialRanks = DaggerfallFormulaPolicy.ClassicWeaponMaterialRanks;
        _actions = definitions.Actions;
        _definitions = definitionsByEntity;
        _targeting = targeting ?? throw new ArgumentNullException(nameof(targeting));
    }

    internal DaggerfallMeleeTargetingEvidence? LastMeleeTargeting => _targeting.LastEvidence;
    /// <summary>Converts active generation-bound cooldowns to remaining admitted steps for durable save data.</summary>
    internal IReadOnlyList<CombatCooldown> CaptureCooldowns(ulong? generation, ulong? simulationStep)
    {
        Dictionary<long, ulong> values = new(_restoredRemainingCooldowns);
        if (generation is ulong currentGeneration && simulationStep is ulong currentStep)
        {
            foreach ((ulong readyGeneration, long attacker) in _readyAtStep.Keys)
            {
                if (readyGeneration != currentGeneration) continue;
                ulong readyAt = _readyAtStep[(readyGeneration, attacker)];
                if (readyAt > currentStep) values[attacker] = readyAt - currentStep;
            }
        }
        return values.OrderBy(value => value.Key).Select(value => new CombatCooldown(value.Key, value.Value)).ToArray();
    }

    /// <summary>Installs durable cooldown policy state into a freshly composed combat module.</summary>
    internal void RestoreCooldowns(IEnumerable<CombatCooldown> cooldowns)
    {
        ArgumentNullException.ThrowIfNull(cooldowns);
        if (_readyAtStep.Count != 0 || _restoredRemainingCooldowns.Count != 0) throw new InvalidOperationException("Combat readiness can only be restored into a fresh session.");
        foreach (CombatCooldown value in cooldowns)
        {
            if (value.AttackerId <= 0 || value.RemainingSteps == 0)
                throw new ArgumentException("Saved combat readiness is invalid.", nameof(cooldowns));
            if (!_definitions.ContainsKey(value.AttackerId) || !_restoredRemainingCooldowns.TryAdd(value.AttackerId, value.RemainingSteps))
                throw new ArgumentException("Saved combat readiness is duplicated or refers to an unknown attacker.", nameof(cooldowns));
        }
    }

    /// <summary>Anchors restored relative cooldowns to the first resumed Engine-admitted simulation step.</summary>
    internal void ObserveTimeline(ulong generation, ulong simulationStep)
    {
        if (_restoredRemainingCooldowns.Count == 0) return;
        foreach ((long attacker, ulong remaining) in _restoredRemainingCooldowns)
            _readyAtStep[(generation, attacker)] = checked(simulationStep + remaining);
        _restoredRemainingCooldowns.Clear();
    }

    internal void TryPlayerMelee(PlayerControlState playerControl, LookReceipt look, ulong generation, ulong simulationStep, double fixedDeltaSeconds, FactBuffer<IProductFact> facts)
    {
        ArgumentNullException.ThrowIfNull(playerControl);
        ArgumentNullException.ThrowIfNull(facts);
        if (playerControl.Position is null)
        {
            _targeting.ClearEvidence();
            facts.Append(new AttackRejectedFact(AttackRejection.MissingPlayerPosition));
            return;
        }
        double? actionReach = _definitions.TryGetValue(PlayerId, out DaggerfallActorDefinition? player)
            && player.ActionId is { } actionId
            && _actions.TryGetValue(actionId, out DaggerfallActionDefinition? action)
            ? action.Reach
            : null;
        long? target = _targeting.Select(playerControl, look, actionReach);
        if (target is not long targetId)
        {
            if (!TryAdmitPlayerAttack(generation, simulationStep, fixedDeltaSeconds, action: null, out DaggerfallAttackDefinition attack, facts)) return;
            LatchPlayerCooldown(generation, simulationStep, fixedDeltaSeconds, attack);
            facts.Append(new AttackRejectedFact(AttackRejection.NoTargetInReach));
            return;
        }
        ResolveExplicit(new ExplicitMeleeRequest(PlayerId, targetId, generation, simulationStep, fixedDeltaSeconds), facts);
    }

    /// <summary>
    /// Resolves the player's own melee swing against current state in the same admitted
    /// step, because input admission, stamina spend and outcome belong together. There
    /// is deliberately no enemy path here: an enemy swing goes through
    /// <see cref="TryBeginEnemyAttack"/> and lands on its authored damage frame, so
    /// resolving one in-step would quietly restore the timing this owner just fixed.
    /// </summary>
    internal void ResolveExplicit(ExplicitMeleeRequest request, FactBuffer<IProductFact> facts)
    {
        request.Validate();
        if (request.AttackerId != PlayerId)
        {
            throw new ArgumentException(
                "An enemy swing must be resolved through TryBeginEnemyAttack so it lands on its authored damage frame.",
                nameof(request));
        }
        if (!TryResolve(request.AttackerId, out Combatant attacker) || !TryResolve(request.TargetId, out Combatant target))
        {
            facts.Append(new AttackRejectedFact(AttackRejection.UnknownExplicitCombatant));
            return;
        }
        if (IsDefeated(target))
        {
            facts.Append(new AttackRejectedFact(AttackRejection.TargetDefeated));
            return;
        }
        if (_readyAtStep.TryGetValue((request.Generation, request.AttackerId), out ulong readyAt) && request.SimulationStep < readyAt)
        {
            facts.Append(new AttackRejectedFact(AttackRejection.Cooldown));
            return;
        }

        if (!TryAdmitPlayerAttack(request.Generation, request.SimulationStep, request.FixedDeltaSeconds, request.Action, out DaggerfallAttackDefinition attack, facts)) return;

        int chance = HitChance(attacker, target, attack.Skill);
        int roll = Draw(request, attacker.Id, target.Id, CombatRandomKey.HitSalt, 1, 100, enemy: false);
        if (roll > chance)
        {
            LatchCooldown(request, attack);
            facts.Append(new AttackMissedFact(attacker.Id, target.Id, roll, chance, false, request.Generation, request.SimulationStep));
            return;
        }

        int body = DaggerfallFormulaPolicy.StruckBodyPart(Draw(request, attacker.Id, target.Id, CombatRandomKey.BodySalt, 0, 19, enemy: false));
        int rawDamage = Draw(request, attacker.Id, target.Id, CombatRandomKey.DamageSalt, attack.MinimumDamage, attack.MaximumDamage, enemy: false);
        // Unarmed hand-to-hand is a natural attack and deliberately bypasses
        // the metal gate; only a weapon with authored material is gated.
        if (attacker.Id == PlayerId && attack.Material is not null && !DaggerfallFormulaPolicy.CanHitMaterial(attack.Material, target.Definition.MinimumMaterial, _weaponMaterialRanks))
        {
            LatchCooldown(request, attack);
            facts.Append(new AttackRejectedFact(AttackRejection.InsufficientWeaponMaterial));
            return;
        }
        int damage = Math.Max(1, checked(rawDamage + StrengthModifier(attacker) + attack.DamageBonus));
        ActorTrackRead targetHealth = target.Mechanics.ReadTrack(TrackId.Parse(HealthTrack));
        ExactTrackSetReceipt change = target.Mechanics.SetTrack(
            TrackId.Parse(HealthTrack),
            new ExactValue(checked(targetHealth.Current.Raw - damage)),
            ExactTrackSetPolicy.ClampToBounds);
        // A failed managed mutation reaches here with no optimistic hit fact or cooldown.
        // Daggerfall owns the policy; the managed track owns its bounds and mutation invariants.
        LatchCooldown(request, attack);
        int applied = checked((int)(change.Before.Raw - change.After.Raw));
        facts.Append(new AttackHitFact(attacker.Id, target.Id, applied, body, false, request.Generation, request.SimulationStep));
        if (applied > 0) facts.Append(new ActorDamagedFact(target.Id, applied));
        bool defeated = change.After <= change.Bounds.Minimum;
        if (defeated && change.Before > change.Bounds.Minimum) facts.Append(new ActorDiedFact(target.Id, attacker.Id, applied, request.Generation, request.SimulationStep));
    }

    /// <summary>
    /// Decides one enemy melee attack without touching health. The roll, struck body
    /// and damage are fixed here so the outcome is deterministic from the admitted
    /// step, but they are applied only when the authored damage frame is reached.
    /// A swing already in flight refuses a second one from the same attacker.
    /// </summary>
    internal bool TryBeginEnemyAttack(long attackerId, long targetId, ulong generation, ulong simulationStep, double fixedDeltaSeconds, FactBuffer<IProductFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (_pendingImpacts.ContainsKey((generation, attackerId))) return false;
        if (!TryResolve(attackerId, out Combatant attacker) || !TryResolve(targetId, out Combatant target))
        {
            facts.Append(new AttackRejectedFact(AttackRejection.UnknownExplicitCombatant));
            return false;
        }
        if (IsDefeated(target))
        {
            facts.Append(new AttackRejectedFact(AttackRejection.TargetDefeated));
            return false;
        }
        if (_readyAtStep.TryGetValue((generation, attackerId), out ulong readyAt) && simulationStep < readyAt)
        {
            facts.Append(new AttackRejectedFact(AttackRejection.Cooldown));
            return false;
        }
        if (attacker.Definition.ActionId is not { } actionId
            || !_actions.TryGetValue(actionId, out DaggerfallActionDefinition? authoredAction)
            || authoredAction.CooldownSeconds is not double authoredCooldown)
        {
            facts.Append(new AttackRejectedFact(AttackRejection.NoAttackPolicy));
            return false;
        }

        DaggerfallAttackDefinition attack = ResolveFixedAttack(attacker.Definition, authoredAction, authoredCooldown);
        ExplicitMeleeRequest request = new(attackerId, targetId, generation, simulationStep, fixedDeltaSeconds);
        int chance = HitChance(attacker, target, attack.Skill);
        int roll = Draw(request, attackerId, targetId, CombatRandomKey.HitSalt, 1, 100, enemy: true);
        bool willHit = roll <= chance;
        // A miss draws nothing further, matching the resolved player path.
        int body = willHit ? DaggerfallFormulaPolicy.StruckBodyPart(Draw(request, attackerId, targetId, CombatRandomKey.BodySalt, 0, 19, enemy: true)) : 0;
        int damage = willHit
            ? Math.Max(1, checked(Draw(request, attackerId, targetId, CombatRandomKey.DamageSalt, attack.MinimumDamage, attack.MaximumDamage, enemy: true) + StrengthModifier(attacker) + attack.DamageBonus))
            : 0;
        LatchCooldown(request, attack);
        _pendingImpacts[(generation, attackerId)] = new PendingEnemyImpact(attackerId, targetId, willHit, body, damage, roll, chance);
        facts.Append(new EnemyAttackStartedFact(attackerId, targetId, willHit, generation, simulationStep));
        return true;
    }

    /// <summary>
    /// Applies the swings whose authored damage frame was reached inside the current
    /// admitted update. A swing whose attacker died mid-strike, whose target is gone
    /// or defeated, or whose target left reach is resolved as a miss or dropped
    /// rather than applied late; an expired swing applies nothing.
    /// </summary>
    internal void ApplyImpacts(IReadOnlyList<AttackImpactNotice> notices, ulong generation, FactBuffer<IProductFact> facts)
    {
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(facts);
        foreach (AttackImpactNotice notice in notices)
        {
            if (!_pendingImpacts.Remove((notice.Generation, notice.AttackerId), out PendingEnemyImpact pending)) continue;
            if (notice.Expired || notice.Generation != generation) continue;
            if (!TryResolve(pending.AttackerId, out Combatant attacker) || IsDefeated(attacker)) continue;
            if (!TryResolve(pending.TargetId, out Combatant target) || IsDefeated(target)) continue;
            if (!pending.WillHit)
            {
                facts.Append(new AttackMissedFact(pending.AttackerId, pending.TargetId, pending.Roll, pending.Chance, true, notice.Generation, notice.SimulationStep));
                continue;
            }

            ActorTrackRead targetHealth = target.Mechanics.ReadTrack(TrackId.Parse(HealthTrack));
            ExactTrackSetReceipt change = target.Mechanics.SetTrack(
                TrackId.Parse(HealthTrack),
                new ExactValue(checked(targetHealth.Current.Raw - pending.Damage)),
                ExactTrackSetPolicy.ClampToBounds);
            int applied = checked((int)(change.Before.Raw - change.After.Raw));
            facts.Append(new AttackHitFact(pending.AttackerId, pending.TargetId, applied, pending.Body, true, notice.Generation, notice.SimulationStep));
            if (applied > 0) facts.Append(new ActorDamagedFact(pending.TargetId, applied));
            bool defeated = change.After <= change.Bounds.Minimum;
            if (defeated && change.Before > change.Bounds.Minimum) facts.Append(new ActorDiedFact(pending.TargetId, pending.AttackerId, applied, notice.Generation, notice.SimulationStep));
        }
    }

    /// <summary>
    /// Drops a swing that is no longer being made, so a target that left reach or an
    /// attacker that changed its mind is never struck late. Range and visibility belong
    /// to the behaviour that admitted the attack, not to this module.
    /// </summary>
    internal void InterruptPendingAttack(long attackerId, ulong generation) => _pendingImpacts.Remove((generation, attackerId));

    private bool TryAdmitPlayerAttack(ulong generation, ulong simulationStep, double fixedDeltaSeconds, DaggerfallActionId? action, out DaggerfallAttackDefinition attack, FactBuffer<IProductFact> facts)
    {
        if (!TryResolve(PlayerId, out Combatant player))
        {
            attack = default!;
            facts.Append(new AttackRejectedFact(AttackRejection.UnknownExplicitCombatant));
            return false;
        }
        if (_readyAtStep.TryGetValue((generation, PlayerId), out ulong readyAt) && simulationStep < readyAt)
        {
            attack = default!;
            facts.Append(new AttackRejectedFact(AttackRejection.Cooldown));
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
        ActorTrackRead stamina = player.Mechanics.ReadTrack(TrackId.Parse(StaminaTrack));
        if (stamina.Current.Raw < staminaCost)
        {
            facts.Append(new AttackRejectedFact(AttackRejection.InsufficientStamina));
            return false;
        }
        ExactTrackMutationReceipt spent = player.Mechanics.SpendTrack(TrackId.Parse(StaminaTrack), new ExactValue(staminaCost));
        if (spent.AppliedAmount.Raw == staminaCost) return true;
        // A surprising partial receipt has no fake local rollback.
        facts.Append(new AttackRejectedFact(AttackRejection.StaminaSpendNotAccepted));
        return false;
    }

    private bool TryResolve(long id, out Combatant combatant)
    {
        if (id == PlayerId && _definitions.TryGetValue(PlayerId, out DaggerfallActorDefinition? player)) { combatant = new(id, _actors.Player.Mechanics, player); return true; }
        if (_actors.TryGet(id, out ActorState actor) && _definitions.TryGetValue(id, out DaggerfallActorDefinition? definition)) { combatant = new(id, actor.Mechanics, definition); return true; }
        combatant = default;
        return false;
    }

    private static bool IsDefeated(Combatant combatant) => combatant.Mechanics.ReadTrack(TrackId.Parse(HealthTrack)).Current.Raw <= 0;
    private static DaggerfallAttackDefinition ResolveFixedAttack(DaggerfallActorDefinition actor, DaggerfallActionDefinition action, double cooldown) => action.AttackRangeIndex is int index
        ? new DaggerfallAttackDefinition(action.Skill, actor.Attacks[index].MinimumDamage, actor.Attacks[index].MaximumDamage, cooldown)
        : new DaggerfallAttackDefinition(action.Skill, action.MinimumDamage!.Value, action.MaximumDamage!.Value, cooldown);
    private DaggerfallWeaponDefinition? ReadWeapon(EquipmentRead equipment, string slot)
    {
        if (!equipment.TryGet(new WorldRpg.Kit.Inventory.EquipmentSlotId(slot), out WorldRpg.Kit.Inventory.UniqueInventoryItem item)
            || !_items.TryGetValue(new DaggerfallItemId(item.Definition.Value), out DaggerfallItemDefinition? definition)) return null;
        return definition.Weapon;
    }
    private int HitChance(Combatant attacker, Combatant target, string skill) => DaggerfallFormulaPolicy.CalculateHitChance(ReadStat(attacker, new DaggerfallStatId(skill)), target.Definition.Armor, ReadStat(attacker, DaggerfallMechanicsIds.Luck), ReadStat(target, DaggerfallMechanicsIds.Luck), ReadStat(attacker, DaggerfallMechanicsIds.Agility), ReadStat(target, DaggerfallMechanicsIds.Agility), ReadStat(target, DaggerfallMechanicsIds.Dodging));
    internal static int CalculateHitChance(int skill, int struckArmor, int attackerLuck, int targetLuck, int attackerAgility, int targetAgility, int targetDodge) => DaggerfallFormulaPolicy.CalculateHitChance(skill, struckArmor, attackerLuck, targetLuck, attackerAgility, targetAgility, targetDodge);
    private int StrengthModifier(Combatant attacker) => DaggerfallFormulaPolicy.DamageModifier(ReadStat(attacker, DaggerfallMechanicsIds.Strength));
    private static int ReadStat(Combatant actor, DaggerfallStatId stat) => checked((int)actor.Mechanics.ReadStat(StatId.Parse(stat.Value)).Base.Raw);
    private int Draw(ExplicitMeleeRequest request, long attacker, long target, int salt, int minimum, int maximum, bool enemy) => checked((int)_random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, enemy ? CombatRandomKey.EnemyScope : CombatRandomKey.PlayerScope, CombatRandomKey.For(request.Generation, request.SimulationStep, attacker, target, salt), minimum, maximum)).Value);
    private static ulong RequiredSteps(double cooldown, double fixedDelta) => checked((ulong)Math.Max(1d, Math.Ceiling(cooldown / fixedDelta)));
    private void LatchPlayerCooldown(ulong generation, ulong simulationStep, double fixedDeltaSeconds, DaggerfallAttackDefinition attack) => _readyAtStep[(generation, PlayerId)] = checked(simulationStep + RequiredSteps(attack.CooldownSeconds, fixedDeltaSeconds));
    private void LatchCooldown(ExplicitMeleeRequest request, DaggerfallAttackDefinition attack) => _readyAtStep[(request.Generation, request.AttackerId)] = checked(request.SimulationStep + RequiredSteps(attack.CooldownSeconds, request.FixedDeltaSeconds));
    private readonly record struct Combatant(long Id, ActorMechanicsState Mechanics, DaggerfallActorDefinition Definition);
}

internal readonly record struct CombatCooldown(long AttackerId, ulong RemainingSteps);

/// <summary>A decided enemy swing waiting for its authored damage frame.</summary>
internal readonly record struct PendingEnemyImpact(long AttackerId, long TargetId, bool WillHit, int Body, int Damage, int Roll, int Chance);

/// <summary>One swing's authored damage frame: reached, or passed without being reached.</summary>
internal readonly record struct AttackImpactNotice(long AttackerId, long TargetId, ulong Generation, ulong SimulationStep, bool Expired);
