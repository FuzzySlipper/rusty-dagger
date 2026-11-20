using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Targeting;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

internal sealed class DaggerfallTargetingPolicy(IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions,
    DaggerfallMeleeTargetingTuning tuning, DaggerfallActorSight sight, Func<long, bool>? actorGameplayActive = null) : ITargetingPolicy
{
    private readonly Func<long, bool> _actorGameplayActive = actorGameplayActive ?? (_ => true);

    public bool IsValidTarget(ActorState actor) => actor.DurableId != DaggerfallActorIdentity.PlayerEntityId
        && actor.DurableId > 0 && definitions.TryGetValue(actor.DurableId, out var definition)
        && definition.Kind != DaggerfallActorKinds.StaticNpc
        && _actorGameplayActive(actor.DurableId);
    public double MinimumFacingCosine => tuning.MinimumFacingCosine;

    // A visibility ray to a walker's floor contact can hit the floor itself, so the player looks from its eye at the
    // body's middle, while the Engine's normal cover query stands.
    public Vector3 AimPoint(ActorState actor) => sight.Body(actor.DurableId, actor.Position);
    public Vector3 EyePoint(long observer, WorldPoint origin) => sight.Eye(observer, origin);
    public double MaximumDistance(double? actionReach)
    {
        if (actionReach is not double reach) return tuning.MaximumDistance;
        if (!double.IsFinite(reach) || reach <= 0d) throw new InvalidOperationException("Player action reach must be a positive finite authored value.");
        return reach;
    }
}

/// <summary>Ruleset-tunable query bounds for ordinary Daggerfall player melee.</summary>
internal sealed record DaggerfallMeleeTargetingTuning(double MaximumDistance, double MinimumFacingCosine)
{
    internal DaggerfallMeleeTargetingTuning Validate()
    {
        if (!double.IsFinite(MaximumDistance) || MaximumDistance <= 0d) throw new ArgumentOutOfRangeException(nameof(MaximumDistance));
        if (!double.IsFinite(MinimumFacingCosine) || MinimumFacingCosine is < -1d or > 1d) throw new ArgumentOutOfRangeException(nameof(MinimumFacingCosine));
        return this;
    }

    internal static DaggerfallMeleeTargetingTuning Read(JsonElement section) => new DaggerfallMeleeTargetingTuning(
        section.GetProperty("maximumDistance").GetDouble(),
        section.GetProperty("minimumFacingCosine").GetDouble()).Validate();
}
