using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Targeting;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

internal sealed class DaggerTargetingPolicy(IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions,
    DaggerfallMeleeTargetingTuning tuning, Func<DaggerfallSiteProfile> currentInputs) : ITargetingPolicy
{
    public bool IsValidTarget(ActorState actor) => actor.DurableId != DaggerfallActorIdentity.PlayerEntityId
        && actor.DurableId > 0 && definitions.TryGetValue(actor.DurableId, out var definition)
        && definition.Kind != DaggerfallActorKinds.StaticNpc;
    public double MinimumFacingCosine => tuning.MinimumFacingCosine;
    public Vector3 AimPoint(ActorState actor)
    {
        Vector3 basePosition = actor.Position.ToVector();
        DaggerfallActorDefinition definition = definitions[actor.DurableId];
        if (!definition.GroundOnSpawn) return basePosition;

        // Grounded actor poses name the sprite base at the floor contact. A visibility ray to that
        // contact can hit the floor itself, so use the visible body's midpoint while retaining the
        // Engine's normal cover query.
        DaggerfallSiteProfile inputs = currentInputs();
        NormalizedActorSprite sprite = inputs.ActorSprites.TryGetValue(actor.DurableId, out NormalizedActorSprite? placed)
            ? placed
            : definition.MobileId is int mobileId && inputs.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? mobile)
                ? mobile : throw new InvalidOperationException($"Grounded actor {actor.DurableId} has no admitted sprite size.");
        return basePosition + Vector3.UnitY * (sprite.Size.Y * .5f);
    }
    public double MaximumDistance(double? actionReach)
    {
        if (actionReach is not double reach) return tuning.MaximumDistance;
        if (!double.IsFinite(reach) || reach <= 0d) throw new InvalidOperationException("Player action reach must be a positive finite authored value.");
        return reach;
    }
}
