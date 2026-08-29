using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>Direct Daggerfall attack policy over Engine-authoritative Mechanics state.</summary>
internal sealed class CombatModule
{
    private const long PlayerId = 1;
    private const string Operation = "daggerfall.melee";
    private readonly IMechanicsService _mechanics;
    private readonly IRandomService _random;
    private readonly ActorsState _actors;
    private readonly MechanicsEquipmentCoordinator _equipment;
    private readonly IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> _items;
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _definitions;
    private readonly DaggerfallCombatTuning _tuning;
    private readonly Dictionary<(ulong Generation, long Attacker), ulong> _readyAtStep = [];

    internal CombatModule(IMechanicsService mechanics, IRandomService random, ActorsState actors, MechanicsEquipmentCoordinator equipment, DaggerfallDefinitions definitions, IReadOnlyDictionary<long, DaggerfallActorDefinition> definitionsByEntity, DaggerfallCombatTuning tuning)
    {
        _mechanics = mechanics;
        _random = random;
        _actors = actors;
        _equipment = equipment;
        _items = definitions.Items;
        _definitions = definitionsByEntity;
        _tuning = tuning.Validate();
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
            EquipmentRead equipment = _equipment.Read();
            if (!equipment.TryGet(new EquipmentSlotId("right-hand"), out UniqueInventoryItem item)
                || !_items.TryGetValue(new DaggerfallItemId(item.Definition.Value), out DaggerfallItemDefinition? definition)
                || definition.Weapon is not DaggerfallWeaponDefinition weapon)
            {
                facts.Append(new AttackRejectedFact(AttackRejection.RightHandWeaponRequired));
                return;
            }
            attack = new DaggerfallAttackDefinition(weapon.Skill, weapon.MinimumDamage, weapon.MaximumDamage, _tuning.PlayerMeleeCooldownSeconds);
            if (!SpendPlayerStamina(attacker, facts)) return;
        }
        else if (attacker.Definition.Attack is { } authoredAttack) attack = authoredAttack;
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

        int body = StruckBodyTable[Draw(request, attacker.Id, target.Id, CombatRandomKey.BodySalt, 0, StruckBodyTable.Length - 1, enemyAttack)];
        int rawDamage = Draw(request, attacker.Id, target.Id, CombatRandomKey.DamageSalt, attack.MinimumDamage, attack.MaximumDamage, enemyAttack);
        int damage = Math.Max(1, checked(rawDamage + StrengthModifier(attacker)));
        MechanicsTrackReadLeaseReceipt before = _mechanics.ReadTrack(new MechanicsTrackReadRequest(target.Mechanics, DaggerfallMechanicsIds.Health.Value, Operation));
        MechanicsDamageLeaseReceipt receipt = _mechanics.ApplyDamage(new MechanicsDamageRequest(
            Operation, MechanicsActiveEffectProvenanceKind.Request, 0, string.Empty, 0, string.Empty, 0, string.Empty, 0, 0, string.Empty,
            Operation, SourceInstance(request), true, checked((ulong)attacker.Id), target.Mechanics,
            DaggerfallMechanicsIds.Health.Value, new MechanicsDamagePart[] { new(DaggerfallMechanicsIds.PhysicalDamage.Value, damage) }, ReadOnlyMemory<MechanicsRequestSource>.Empty,
            true, before.Revision));
        MechanicsTrackDamageChangeRow change = TrackChange(receipt.TrackChanges.Span, DaggerfallMechanicsIds.Health.Value);
        // An apply exception or malformed receipt reaches here with no optimistic hit fact or
        // cooldown. A previously accepted stamina spend remains the Engine's authoritative fact.
        LatchCooldown(request, attack);
        facts.Append(new AttackHitFact(attacker.Id, target.Id, damage, body, enemyAttack));
        int applied = checked((int)(change.Before - change.After));
        if (applied > 0) facts.Append(new ActorDamagedFact(target.Id, applied));
        bool defeated = HasDepletion(receipt.TargetTrackDepletions.Span, DaggerfallMechanicsIds.Health.Value) || change.After <= before.Minimum;
        if (defeated && change.Before > before.Minimum) facts.Append(new ActorDiedFact(target.Id, applied, request.Generation, request.SimulationStep));
    }

    private bool SpendPlayerStamina(Combatant player, FactBuffer<IProductFact> facts)
    {
        MechanicsTrackReadLeaseReceipt stamina = _mechanics.ReadTrack(new MechanicsTrackReadRequest(player.Mechanics, DaggerfallMechanicsIds.Stamina.Value, Operation));
        if (stamina.Current < _tuning.PlayerMeleeStaminaCost)
        {
            facts.Append(new AttackRejectedFact(AttackRejection.InsufficientStamina));
            return false;
        }
        MechanicsTrackMutationLeaseReceipt spent = _mechanics.SpendTrack(new MechanicsTrackMutationRequest(player.Mechanics, Operation, "player_melee", DaggerfallMechanicsIds.Stamina.Value, _tuning.PlayerMeleeStaminaCost, MechanicsRevisionGuard.Exact, stamina.Revision));
        if (spent.AppliedAmount == _tuning.PlayerMeleeStaminaCost) return true;
        // The Engine mutation is authoritative. A surprising partial receipt has no fake local rollback.
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

    private bool IsDefeated(Combatant combatant) => _mechanics.ReadTrack(new MechanicsTrackReadRequest(combatant.Mechanics, DaggerfallMechanicsIds.Health.Value, Operation)).Current <= 0;
    private int HitChance(Combatant attacker, Combatant target, string skill) => CalculateHitChance(ReadStat(attacker, Skill(skill)), target.Definition.Armor, ReadStat(attacker, DaggerfallMechanicsIds.Luck), ReadStat(target, DaggerfallMechanicsIds.Luck), ReadStat(attacker, DaggerfallMechanicsIds.Agility), ReadStat(target, DaggerfallMechanicsIds.Agility), ReadStat(target, DaggerfallMechanicsIds.Dodging));
    internal static int CalculateHitChance(int skill, int struckArmor, int attackerLuck, int targetLuck, int attackerAgility, int targetAgility, int targetDodge) => Math.Clamp(skill + struckArmor - 50 + ((attackerLuck - targetLuck) / 10) + ((attackerAgility - targetAgility) / 10) - (targetDodge / 4), 3, 97);
    private int StrengthModifier(Combatant attacker) => FloorDivision(ReadStat(attacker, DaggerfallMechanicsIds.Strength) - 50, 5);
    private int ReadStat(Combatant actor, DaggerfallStatId stat) => checked((int)_mechanics.ReadStat(new MechanicsStatReadRequest(actor.Mechanics, stat.Value)).Base);
    private static int FloorDivision(int value, int divisor) => value >= 0 ? value / divisor : -(((-value) + divisor - 1) / divisor);
    private int Draw(ExplicitMeleeRequest request, long attacker, long target, int salt, int minimum, int maximum, bool enemy) => checked((int)_random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, enemy ? CombatRandomKey.EnemyScope : CombatRandomKey.PlayerScope, CombatRandomKey.For(request.Generation, request.SimulationStep, attacker, target, salt), minimum, maximum)).Value);
    private static ulong RequiredSteps(double cooldown, double fixedDelta) => checked((ulong)Math.Max(1d, Math.Ceiling(cooldown / fixedDelta)));
    private void LatchCooldown(ExplicitMeleeRequest request, DaggerfallAttackDefinition attack) => _readyAtStep[(request.Generation, request.AttackerId)] = checked(request.SimulationStep + RequiredSteps(attack.CooldownSeconds, request.FixedDeltaSeconds));
    private static string SourceInstance(ExplicitMeleeRequest request) => $"daggerfall.melee.g{request.Generation}.s{request.SimulationStep}.a{request.AttackerId}.t{request.TargetId}";
    private static MechanicsTrackDamageChangeRow TrackChange(ReadOnlySpan<MechanicsTrackDamageChangeRow> changes, string track)
    {
        foreach (MechanicsTrackDamageChangeRow change in changes) if (change.Track == track) return change;
        throw new InvalidOperationException($"Engine damage receipt omitted target track '{track}'.");
    }
    private static bool HasDepletion(ReadOnlySpan<MechanicsTrackDepletionRow> rows, string track)
    {
        foreach (MechanicsTrackDepletionRow row in rows) if (row.Track == track) return true;
        return false;
    }
    private static readonly int[] StruckBodyTable = [0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 6];
    private readonly record struct Combatant(long Id, MechanicsEntity Mechanics, DaggerfallActorDefinition Definition);
    private static DaggerfallStatId Skill(string skill) => skill switch { "long-blade" => DaggerfallMechanicsIds.LongBlade, "hand-to-hand" => DaggerfallMechanicsIds.HandToHand, _ => throw new InvalidOperationException($"Unsupported Daggerfall skill '{skill}'.") };
}
