using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal static class DaggerfallParalysisEffects
{
    internal static DaggerfallEffectDefinition Definition(Action<long, long> attacked) => new(
        "paralyze", "paralyze", DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
        Apply: effect =>
        {
            Validate(effect);
            if (effect.Context.Caster is { } caster) attacked(checked((long)caster.Value), checked((long)effect.Context.Target.Value));
            return [];
        }, Resume: effect =>
        {
            // Restoring the completed attack's control state must not perform another attack reaction.
            Validate(effect);
            return [];
        },
        Feedback: DaggerfallEffectFeedback.MagicSparkle,
        Spell: new(0, -1, SpellMaker: true, SupportsDuration: true, RollChanceOnCast: true, IsParalysis: true,
            AllowedElements: DaggerfallMagicAllowedElements.Fire | DaggerfallMagicAllowedElements.Cold
                | DaggerfallMagicAllowedElements.Poison | DaggerfallMagicAllowedElements.Shock | DaggerfallMagicAllowedElements.Magic,
            AllowedTargets: DaggerfallMagicAllowedTargets.Other),
        ControlRestrictions: new(Movement: true, PhysicalAttacks: true));

    internal static int Cure(DaggerfallEffectLifecycle effects, long targetId) => End(effects, targetId, cure: true);
    internal static int EndOnDeath(DaggerfallEffectLifecycle effects, long targetId) => End(effects, targetId, cure: false);

    private static int End(DaggerfallEffectLifecycle effects, long targetId, bool cure)
    {
        var matches = effects.Active.Where(effect => effect.Definition.Key == "paralyze"
            && checked((long)effect.Context.Target.Value) == targetId).Select(effect => effect.Context.Instance).ToArray();
        foreach (var instance in matches)
            if (cure) effects.Cure(instance); else effects.Cancel(instance);
        return matches.Length;
    }

    private static void Validate(DaggerfallActiveEffect effect)
    {
        var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        if (state?.Settings is not { Type: 0, SubType: -1 } || state.CasterLevel < 1 || state.SavePercent is < 1 or > 100
            || effect.Target.Get<StatsComponent>().GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current <= 0)
            throw new ArgumentException("Paralysis requires an admitted current cast state and a living target.");
    }
}
