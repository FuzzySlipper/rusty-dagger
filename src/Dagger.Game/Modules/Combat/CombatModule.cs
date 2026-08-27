using Rusty.Engine;
using RustyDagger.Game.Facts;
using RustyDagger.Game.Modules.Actors;
using RustyDagger.Game.Modules.PlayerControl;

namespace RustyDagger.Game.Modules.Combat;

/// <summary>Local combat policy over shaped combatants; Engine owns their stat and track state.</summary>
internal sealed class CombatModule(IMechanicsService mechanics, CombatTuning tuning)
{
    private const string ReadOperation = "combat_read";
    private const string EvaluateOperation = "combat_evaluate";
    private const string PlayerMeleeOperation = "player_melee";
    private const string EnemyMeleeOperation = "enemy_melee";
    private const string CombatRequestSource = "combat";

    internal void AdvanceCooldowns(ActorsState actors, float deltaSeconds) => actors.AdvanceCooldowns(deltaSeconds);

    internal void TryPlayerMelee(PlayerControlState playerControl, CombatantState player, CombatantState? target, WeaponDefinition weapon, ulong updateSequence, IRandomService rng, ProductFactBuffer facts)
    {
        if (player.Actor.AttackCooldownSeconds > 0f) { facts.Append(new AttackRejectedFact(AttackRejection.WeaponRecovering)); return; }
        if (player.AttackCostTrack is not string attackCost || Track(player, attackCost).Current < tuning.PlayerAttackCost) { facts.Append(new AttackRejectedFact(AttackRejection.TooExhausted)); return; }
        if (playerControl.Position is not WorldPoint playerPosition) { facts.Append(new AttackRejectedFact(AttackRejection.MissingPlayerPosition)); return; }
        if (target is null || target.Actor.IsDefeated || target.Actor is not ActorState actor || playerPosition.HorizontalDistanceTo(actor.Position) > tuning.PlayerMeleeReach) { facts.Append(new AttackRejectedFact(AttackRejection.NoTargetInReach)); return; }

        Spend(player, attackCost, tuning.PlayerAttackCost, PlayerMeleeOperation);
        player.Actor.BeginAttack(tuning.PlayerAttackCooldownSeconds);
        AttackResolution resolution = AttackResolution.ForPlayer(actor.EntityId, HitChance(player, target), updateSequence);
        resolution.Roll = Draw(rng, resolution.HitKey, 1, 100);
        if (resolution.Roll > resolution.Chance) { facts.Append(new AttackMissedFact(actor.EntityId, resolution.Roll, resolution.Chance, EnemyAttack: false)); return; }
        int damage = Damage(player, weapon, rng, updateSequence, actor.EntityId);
        MechanicsTrackMutationReceipt receipt = Spend(target, target.DamageTrack, damage, PlayerMeleeOperation);
        facts.Append(new ActorDamagedFact(actor.EntityId, checked((int)receipt.AppliedAmount)));
        if (receipt.After == receipt.Minimum)
        {
            target.Actor.RecordDefeat();
            facts.Append(new ActorDiedFact(actor.EntityId, checked((int)receipt.AppliedAmount), updateSequence));
        }
        else facts.Append(new AttackHitFact(actor.EntityId, checked((int)receipt.AppliedAmount), EnemyAttack: false));
    }

    internal void TryEnemyMelee(PlayerControlState playerControl, CombatantState player, CombatantState? enemy, ulong updateSequence, IRandomService rng, ProductFactBuffer facts)
    {
        if (playerControl.Position is not WorldPoint playerPosition || player.Actor.IsDefeated || enemy is null || enemy.Actor.IsDefeated || enemy.Attack is not EnemyAttackDefinition attack || enemy.Actor is not ActorState actor || actor.AttackCooldownSeconds > 0f || actor.Position.HorizontalDistanceTo(playerPosition) > attack.Reach) return;
        actor.BeginAttack(attack.CooldownSeconds);
        int chance = HitChance(enemy, player);
        int roll = Draw(rng, CombatKeys.EnemyHit(updateSequence, actor.EntityId), 1, 100);
        if (roll > chance) { facts.Append(new AttackMissedFact(actor.EntityId, roll, chance, EnemyAttack: true)); return; }
        int damage = CombatMath.DamageRange(attack.MinimumDamage, attack.MaximumDamage, rng, CombatKeys.EnemyDamage(updateSequence, actor.EntityId));
        MechanicsTrackMutationReceipt receipt = Spend(player, player.DamageTrack, damage, EnemyMeleeOperation);
        facts.Append(new PlayerDamagedFact(checked((int)receipt.AppliedAmount)));
        if (receipt.After == receipt.Minimum)
        {
            player.Actor.RecordDefeat();
            facts.Append(new PlayerDiedFact(checked((int)receipt.AppliedAmount)));
        }
        facts.Append(new AttackHitFact(actor.EntityId, damage, EnemyAttack: true));
    }

    private MechanicsTrackReadReceipt Track(CombatantState actor, string track) => mechanics.ReadTrack(new MechanicsTrackReadRequest(actor.Actor.Mechanics, track, ReadOperation));
    private long Stat(CombatantState actor, string stat) => mechanics.EvaluateStat(new MechanicsStatOperationRequest(actor.Actor.Mechanics, stat, EvaluateOperation)).Value;
    private MechanicsTrackMutationReceipt Spend(CombatantState actor, string track, int amount, string operation)
    {
        MechanicsTrackReadReceipt current = Track(actor, track);
        return mechanics.SpendTrack(new MechanicsTrackMutationRequest(actor.Actor.Mechanics, operation, CombatRequestSource, track, amount, MechanicsRevisionGuard.Exact, current.Revision));
    }

    private int HitChance(CombatantState attacker, CombatantState target) => CombatMath.HitChance(Stat(attacker, attacker.AttackSkill), Stat(attacker, attacker.Luck), Stat(attacker, attacker.Agility), Stat(target, target.Luck), Stat(target, target.Agility), Stat(target, target.Dodging), target.ArmorValue);
    private int Damage(CombatantState attacker, WeaponDefinition weapon, IRandomService rng, ulong update, long targetId) => CombatMath.Damage(Stat(attacker, attacker.Strength), weapon, rng, update, targetId);
    private static int Draw(IRandomService rng, string key, int minimum, int maximum) => checked((int)rng.DrawKeyed(new KeyedRngRequest(CombatKeys.Seed, CombatKeys.Stream, key, minimum, maximum)).Value);
}

internal sealed class CombatantState(IActorCombatant actor, CombatantProfile profile)
{
    internal IActorCombatant Actor { get; } = actor;
    internal string AttackSkill { get; } = profile.AttackSkill;
    internal string Strength { get; } = profile.Strength;
    internal string Agility { get; } = profile.Agility;
    internal string Luck { get; } = profile.Luck;
    internal string Dodging { get; } = profile.Dodging;
    internal string DamageTrack { get; } = profile.DamageTrack;
    internal string? AttackCostTrack { get; } = profile.AttackCostTrack;
    internal int ArmorValue { get; } = profile.ArmorValue;
    internal EnemyAttackDefinition? Attack { get; } = profile.Attack;
}

internal sealed record CombatState;
internal sealed record CombatTuning(int PlayerAttackCost, float PlayerAttackCooldownSeconds, float PlayerMeleeReach)
{
    internal CombatTuning Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PlayerAttackCost);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PlayerAttackCooldownSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PlayerMeleeReach);
        return this;
    }
}

internal sealed class AttackResolution(long targetId, int chance, ulong updateSequence, string hitKey)
{
    internal long TargetId { get; } = targetId;
    internal int Chance { get; } = chance;
    internal ulong UpdateSequence { get; } = updateSequence;
    internal string HitKey { get; } = hitKey;
    internal int Roll { get; set; }
    internal static AttackResolution ForPlayer(long targetId, int chance, ulong updateSequence) => new(targetId, chance, updateSequence, CombatKeys.PlayerHit(updateSequence, targetId));
}

internal static class CombatKeys
{
    internal const ulong Seed = 0xDA66E2UL;
    internal const string Stream = "dagger.combat";
    internal static string PlayerHit(ulong update, long target) => $"step:{update}:hit:{target}";
    internal static string PlayerDamage(ulong update, long target) => $"step:{update}:damage:{target}";
    internal static string EnemyHit(ulong update, long actor) => $"step:{update}:enemy-hit:{actor}";
    internal static string EnemyDamage(ulong update, long actor) => $"step:{update}:enemy-damage:{actor}";
}

internal static class CombatMath
{
    internal static int HitChance(long attackSkill, long attackerLuck, long attackerAgility, long targetLuck, long targetAgility, long targetDodging, int targetArmorValue) => Math.Clamp(checked((int)(attackSkill + targetArmorValue - 50 + ((attackerLuck - targetLuck) / 10) + ((attackerAgility - targetAgility) / 10) - (targetDodging / 4))), 3, 97);
    internal static int Damage(long attackerStrength, WeaponDefinition weapon, IRandomService rng, ulong update, long targetId) => Math.Max(1, DamageRange(weapon.MinimumDamage, weapon.MaximumDamage, rng, CombatKeys.PlayerDamage(update, targetId)) + checked((int)((attackerStrength - 50) / 5)));
    internal static int DamageRange(int minimum, int maximum, IRandomService rng, string key) => checked((int)rng.DrawKeyed(new KeyedRngRequest(CombatKeys.Seed, CombatKeys.Stream, key, minimum, maximum)).Value);
}
