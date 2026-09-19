using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Targeting;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

internal sealed class DaggerTargetingPolicy(IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions,
    DaggerfallMeleeTargetingTuning tuning) : ITargetingPolicy
{
    public bool IsValidTarget(ActorState actor) => actor.DurableId != DaggerfallActorIdentity.PlayerEntityId
        && actor.DurableId > 0 && definitions.ContainsKey(actor.DurableId);
    public double MinimumFacingCosine => tuning.MinimumFacingCosine;
    public double MaximumDistance(double? actionReach)
    {
        if (actionReach is not double reach) return tuning.MaximumDistance;
        if (!double.IsFinite(reach) || reach <= 0d) throw new InvalidOperationException("Player melee action reach must be a positive finite authored value.");
        return Math.Min(tuning.MaximumDistance, reach);
    }
}
