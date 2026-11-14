using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What an actor's harm tells the rest of the game, whatever dealt it: every application, the health actually lost, and
/// the death. Condition's vitality owner reports the harm as a <see cref="DamageResult"/>; these are the facts delivered
/// for it, in this order, and what follows a death (effects ended, the corpse, rewards, the quests' foes) reacts to
/// <see cref="ActorDiedFact"/>.
/// </summary>
internal static class DaggerfallDamageFacts
{
    internal static void Append(FactBuffer<IProductFact> facts, DamageResult result, DaggerfallDamageCause cause, int struckBody,
        ulong generation, ulong step)
    {
        long source = checked((long)result.Source.Get<DurableEntityIdentity>().Identity.Value);
        long target = checked((long)result.Target.Get<DurableEntityIdentity>().Identity.Value);
        facts.Append(new DamageAppliedFact(source, target, cause,
            result.CalculatedDamage, result.ActualHealthLost, struckBody, generation, step));
        if (result.ActualHealthLost > 0)
            facts.Append(new ActorDamagedFact(target, source, cause,
                result.CalculatedDamage, result.ActualHealthLost) { TargetDefeated = result.Defeated });
        if (result.Defeated)
            facts.Append(new ActorDiedFact(target, source, cause,
                result.CalculatedDamage, result.ActualHealthLost, generation, step));
    }
}
