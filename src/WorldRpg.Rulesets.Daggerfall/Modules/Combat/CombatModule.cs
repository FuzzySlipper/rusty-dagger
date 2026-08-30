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
    private readonly Dictionary<(ulong Generation, long Attacker), ulong> _readyAtStep = [];

    internal CombatModule(IRandomService random, ActorsState actors, MechanicsEquipmentCoordinator equipment, DaggerfallDefinitions definitions, IReadOnlyDictionary<long, DaggerfallActorDefinition> definitionsByEntity)
    {
        _random = random;
        _actors = actors;
        _equipment = equipment;
        _items = definitions.Items;
        _weaponMaterialRanks = DaggerfallFormulaPolicy.ClassicWeaponMaterialRanks;
        _actions = definitions.Actions;
        _definitions = definitionsByEntity;
    }

    internal void TryPlayerMelee(PlayerControlState playerControl, FactBuffer<IProductFact> facts)
    {
        facts.Append(new AttackRejectedFact(playerControl.Position is null ? AttackRejection.MissingPlayerPosition : AttackRejection.NoTargetInReach));
    }

    internal void ResolveExplicit(ExplicitMeleeRequest request, FactBuffer<IProductFact> facts)
    {
        request.Validate();
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

        DaggerfallAttackDefinition attack;
        if (attacker.Id == PlayerId)
        {
            string? selectedActionId = request.Action?.Value ?? attacker.Definition.ActionId;
            if (selectedActionId is null || !_actions.TryGetValue(selectedActionId, out DaggerfallActionDefinition? playerAction) || playerAction.Interpretation != "player-equipped-melee" || playerAction.CooldownSeconds is not double playerCooldown || playerAction.StaminaCost is not int staminaCost)
            {
                facts.Append(new AttackRejectedFact(AttackRejection.NoAttackPolicy));
                return;
            }
            EquipmentRead equipment = _equipment.Read();
            DaggerfallWeaponDefinition? weapon = ReadWeapon(equipment, "right-hand") ?? ReadWeapon(equipment, "left-hand");
            attack = weapon is not null
                ? new DaggerfallAttackDefinition(weapon.Skill, weapon.MinimumDamage, weapon.MaximumDamage, playerCooldown, weapon.Material, playerAction.DamageBonus)
                : new DaggerfallAttackDefinition(
                    "hand-to-hand",
                    DaggerfallFormulaPolicy.HandToHandMinimumDamage(ReadStat(attacker, DaggerfallMechanicsIds.HandToHand)),
                    DaggerfallFormulaPolicy.HandToHandMaximumDamage(ReadStat(attacker, DaggerfallMechanicsIds.HandToHand)),
                    playerCooldown,
                    DamageBonus: playerAction.DamageBonus);
            if (!SpendPlayerStamina(attacker, staminaCost, facts)) return;
        }
        else if (attacker.Definition.ActionId is { } actionId && _actions.TryGetValue(actionId, out DaggerfallActionDefinition? authoredAction) && authoredAction.CooldownSeconds is double authoredCooldown) attack = ResolveFixedAttack(attacker.Definition, authoredAction, authoredCooldown);
        else
        {
            facts.Append(new AttackRejectedFact(AttackRejection.NoAttackPolicy));
            return;
        }

        bool enemyAttack = attacker.Id != PlayerId;
        int chance = HitChance(attacker, target, attack.Skill);
        int roll = Draw(request, attacker.Id, target.Id, CombatRandomKey.HitSalt, 1, 100, enemyAttack);
        if (roll > chance)
        {
            LatchCooldown(request, attack);
            facts.Append(new AttackMissedFact(attacker.Id, target.Id, roll, chance, enemyAttack));
            return;
        }

        int body = DaggerfallFormulaPolicy.StruckBodyPart(Draw(request, attacker.Id, target.Id, CombatRandomKey.BodySalt, 0, 19, enemyAttack));
        int rawDamage = Draw(request, attacker.Id, target.Id, CombatRandomKey.DamageSalt, attack.MinimumDamage, attack.MaximumDamage, enemyAttack);
        // Unarmed hand-to-hand is a natural attack and deliberately bypasses
        // the metal gate; only a weapon with authored material is gated.
        if (attacker.Id == PlayerId && attack.Material is not null && !DaggerfallFormulaPolicy.CanHitMaterial(attack.Material, target.Definition.MinimumMaterial, _weaponMaterialRanks))
        {
            LatchCooldown(request, attack);
            facts.Append(new AttackRejectedFact(AttackRejection.InsufficientWeaponMaterial));
            return;
        }
        int damage = Math.Max(1, checked(rawDamage + StrengthModifier(attacker) + attack.DamageBonus));
        ExactTrack targetHealth = target.Mechanics.ReadTrack(TrackId.Parse(HealthTrack));
        ExactTrackSetReceipt change = target.Mechanics.SetTrack(
            TrackId.Parse(HealthTrack),
            new ExactValue(checked(targetHealth.Current.Raw - damage)),
            ExactTrackSetPolicy.ClampToBounds);
        // A failed managed mutation reaches here with no optimistic hit fact or cooldown.
        // Daggerfall owns the policy; the managed track owns its bounds and mutation invariants.
        LatchCooldown(request, attack);
        int applied = checked((int)(change.Before.Raw - change.After.Raw));
        facts.Append(new AttackHitFact(attacker.Id, target.Id, applied, body, enemyAttack));
        if (applied > 0) facts.Append(new ActorDamagedFact(target.Id, applied));
        bool defeated = change.After <= change.Bounds.Minimum;
        if (defeated && change.Before > change.Bounds.Minimum) facts.Append(new ActorDiedFact(target.Id, attacker.Id, applied, request.Generation, request.SimulationStep));
    }

    private bool SpendPlayerStamina(Combatant player, int staminaCost, FactBuffer<IProductFact> facts)
    {
        ExactTrack stamina = player.Mechanics.ReadTrack(TrackId.Parse(StaminaTrack));
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
    private void LatchCooldown(ExplicitMeleeRequest request, DaggerfallAttackDefinition attack) => _readyAtStep[(request.Generation, request.AttackerId)] = checked(request.SimulationStep + RequiredSteps(attack.CooldownSeconds, request.FixedDeltaSeconds));
    private readonly record struct Combatant(long Id, ActorMechanicsState Mechanics, DaggerfallActorDefinition Definition);
}
