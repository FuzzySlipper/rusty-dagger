using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

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

    private static readonly TrackId Fatigue = TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value);

    /// <summary>
    /// A spell's drain or transfer of one vital: the health it took as an effect's harm, then a fatigue or spell-point
    /// loss as its own fact, then the transfer itself.
    /// </summary>
    internal static void AppendTransfer(FactBuffer<IProductFact> facts, DaggerfallSpellTransferResult result, ulong generation, ulong step)
    {
        if (result.HealthDamage is { } health) Append(facts, health, DaggerfallDamageCause.Effect, 0, generation, step);
        if (result.TrackDamage is { } loss) AppendTrackLoss(facts, loss, generation, step);
        facts.Append(new VitalTransferredFact(checked((long)result.Caster.Get<DurableEntityIdentity>().Identity.Value),
            checked((long)result.Target.Get<DurableEntityIdentity>().Identity.Value), result.Track.Value, result.Amount,
            result.ActualLoss, result.Restored, result.TargetDefeated, generation, step));
    }

    /// <summary>A spell's loss of a track that is not health: Fatigue's own fact, or spell points' for any other.</summary>
    internal static void AppendTrackLoss(FactBuffer<IProductFact> facts, DaggerfallSpellTrackResult result, ulong generation, ulong step)
    {
        long source = checked((long)result.Source.Get<DurableEntityIdentity>().Identity.Value);
        long target = checked((long)result.Target.Get<DurableEntityIdentity>().Identity.Value);
        facts.Append(result.Track == Fatigue
            ? new FatigueAppliedFact(source, target, result.CalculatedLoss, result.ActualLoss, generation, step)
            : new SpellPointsAppliedFact(source, target, result.CalculatedLoss, result.ActualLoss, generation, step));
    }
}
