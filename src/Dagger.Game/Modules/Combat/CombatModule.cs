using Rusty.Engine;
using RustyDagger.Game.Daggerfall;
using RustyDagger.Game.Daggerfall.Content;
using RustyDagger.Game.Facts;
using RustyDagger.Game.Modules.Actors;
using RustyDagger.Game.Modules.Encounters;
using RustyDagger.Game.Modules.PlayerControl;

namespace RustyDagger.Game.Modules.Combat;

internal sealed class CombatModule(CombatTuning tuning)
{
    internal void AdvanceCooldowns(ActorsState actors, float deltaSeconds) => actors.AdvanceCooldowns(deltaSeconds);

    internal void TryPlayerMelee(DaggerfallState state, IRandomService rng, ProductFactBuffer facts)
    {
        PlayerActorState player = state.Actors.Player;
        if (player.AttackCooldownSeconds > 0f) { facts.Append(new AttackRejectedFact(AttackRejection.WeaponRecovering)); return; }
        if (player.Stamina < tuning.PlayerAttackStaminaCost) { facts.Append(new AttackRejectedFact(AttackRejection.TooExhausted)); return; }
        if (state.PlayerControl.Position is not WorldPoint playerPosition) { facts.Append(new AttackRejectedFact(AttackRejection.MissingPlayerPosition)); return; }
        EncounterDefinition? encounter = EncounterSystem.ActiveEncounter(state);
        if (encounter is null || !state.Actors.TryGet(encounter.MemberEntityId, out ActorState target) || playerPosition.HorizontalDistanceTo(target.Position) > tuning.PlayerMeleeReach)
        { facts.Append(new AttackRejectedFact(AttackRejection.NoTargetInReach)); return; }

        player.SpendStamina(tuning.PlayerAttackStaminaCost);
        player.BeginAttack(tuning.PlayerAttackCooldownSeconds);
        AttackResolution resolution = AttackResolution.ForPlayer(target.EntityId, CombatMath.HitChance(player.Definition.Stats.LongBlade, player.Definition.Stats, target.Definition.Stats, target.Definition.ArmorValue), state.UpdateSequence);
        resolution.Roll = Draw(rng, resolution.HitKey, 1, 100);
        if (resolution.Roll > resolution.Chance) { facts.Append(new AttackMissedFact(target.EntityId, resolution.Roll, resolution.Chance, EnemyAttack: false)); return; }
        int damage = CombatMath.Damage(player.Definition.Stats, state.Equipment.RightHand, rng, state.UpdateSequence, target.EntityId);
        DamageReceipt receipt = target.ApplyDamage(damage);
        facts.Append(new ActorDamagedFact(target.EntityId, receipt.AppliedDamage));
        facts.Append(receipt.Died ? new ActorDiedFact(target.EntityId, receipt.AppliedDamage, state.UpdateSequence) : new AttackHitFact(target.EntityId, receipt.AppliedDamage, EnemyAttack: false));
    }

    internal void TryEnemyMelee(DaggerfallState state, IRandomService rng, ProductFactBuffer facts)
    {
        if (state.PlayerControl.Position is not WorldPoint playerPosition || state.Actors.Player.IsDead) return;
        EncounterDefinition? encounter = EncounterSystem.ActiveEncounter(state);
        if (encounter is null || !state.Actors.TryGet(encounter.MemberEntityId, out ActorState actor) || actor.IsDead || actor.Definition.Attack is not EnemyAttackDefinition attack || actor.AttackCooldownSeconds > 0f || actor.Position.HorizontalDistanceTo(playerPosition) > attack.Reach) return;
        actor.BeginAttack(attack.CooldownSeconds);
        int chance = CombatMath.HitChance(actor.Definition.Stats.HandToHand, actor.Definition.Stats, state.Actors.Player.Definition.Stats, state.Actors.Player.Definition.ArmorValue);
        int roll = Draw(rng, CombatKeys.EnemyHit(state.UpdateSequence, actor.EntityId), 1, 100);
        if (roll > chance) { facts.Append(new AttackMissedFact(actor.EntityId, roll, chance, EnemyAttack: true)); return; }
        int damage = CombatMath.DamageRange(attack.MinimumDamage, attack.MaximumDamage, rng, CombatKeys.EnemyDamage(state.UpdateSequence, actor.EntityId));
        DamageReceipt receipt = state.Actors.Player.ApplyDamage(damage);
        facts.Append(new PlayerDamagedFact(receipt.AppliedDamage));
        if (receipt.Died) facts.Append(new PlayerDiedFact(receipt.AppliedDamage));
        facts.Append(new AttackHitFact(actor.EntityId, damage, EnemyAttack: true));
    }

    private static int Draw(IRandomService rng, string key, int minimum, int maximum) => checked((int)rng.DrawKeyed(new KeyedRngRequest(CombatKeys.Seed, CombatKeys.Stream, key, minimum, maximum)).Value);
}

internal sealed record CombatState;

internal sealed record CombatTuning(int PlayerAttackStaminaCost, float PlayerAttackCooldownSeconds, float PlayerMeleeReach)
{
    internal static CombatTuning Defaults { get; } = new(5, .75f, 2.25f);
    internal CombatTuning Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PlayerAttackStaminaCost);
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
    internal static int HitChance(int attackSkill, StatBlock attacker, StatBlock target, int targetArmorValue) => Math.Clamp(attackSkill + targetArmorValue - 50 + ((attacker.Luck - target.Luck) / 10) + ((attacker.Agility - target.Agility) / 10) - (target.Dodging / 4), 3, 97);
    internal static int Damage(StatBlock attacker, WeaponDefinition weapon, IRandomService rng, ulong update, long targetId) => Math.Max(1, DamageRange(weapon.MinimumDamage, weapon.MaximumDamage, rng, CombatKeys.PlayerDamage(update, targetId)) + ((attacker.Strength - 50) / 5));
    internal static int DamageRange(int minimum, int maximum, IRandomService rng, string key) => checked((int)rng.DrawKeyed(new KeyedRngRequest(CombatKeys.Seed, CombatKeys.Stream, key, minimum, maximum)).Value);
}
