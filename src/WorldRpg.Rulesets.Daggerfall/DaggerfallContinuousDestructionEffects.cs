using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal static class DaggerfallContinuousDestructionEffects
{
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(IRandomService random, DaggerfallVitalityConsequences vitality,
        Action<DaggerfallEffectDamage> healthApplied, Action<DaggerfallSpellTrackResult> trackApplied,
        Func<long, bool> hostile, Action<long, long, string> attacked)
    {
        foreach (var (key, subtype) in new[] { ("continuous-damage-health", 0), ("continuous-damage-fatigue", 1), ("continuous-damage-spell-points", 2) })
        {
            int selected = subtype;
            yield return new(key, key, DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
                Apply: effect => Validate(effect, selected), Resume: effect => Validate(effect, selected),
                MagicRound: effect =>
                {
                    Track health = effect.Target.Get<StatsComponent>().GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
                    if (health.Current <= 0) { effect.ExpireAfterCurrentRound = true; return; }
                    long target = checked((long)effect.Context.Target.Value);
                    int amount = DaggerfallPeriodicCast.RollMagnitude(effect, random, 1, selected, "daggerfall.continuous-damage.v1", $"{key}:");
                    // The donor protects peaceful non-player actors from Sleep/fatigue damage and aggression.
                    if (selected == 1 && target != DaggerfallActorIdentity.PlayerEntityId && !hostile(target)) return;
                    if (selected == 0) healthApplied(new(vitality.ResolveSpellHealth(effect.Source, effect.Target, amount, terminal:false)));
                    else trackApplied(vitality.ResolveSpellTrack(effect.Source, effect.Target,
                        TrackId.Parse(selected == 1 ? DaggerfallMechanicsIds.Stamina.Value : DaggerfallMechanicsIds.Magicka.Value),
                        selected == 1 ? DaggerfallFormulaPolicy.SpellFatigueDamage(amount) : amount));
                    if (effect.Context.Caster is { } caster) attacked(checked((long)caster.Value), target, effect.BundleId ?? effect.Context.Instance.Value);
                    if (health.Current <= 0) effect.ExpireAfterCurrentRound = true;
                },
                Spell: new(1, selected, SpellMaker: true, SupportsDuration:true, SupportsMagnitude:true,
                    AllowedElements:DaggerfallMagicAllowedElements.Fire | DaggerfallMagicAllowedElements.Cold
                        | DaggerfallMagicAllowedElements.Poison | DaggerfallMagicAllowedElements.Shock | DaggerfallMagicAllowedElements.Magic,
                    AllowedTargets:DaggerfallMagicAllowedTargets.Other,
                    CreateState: state => DaggerfallPeriodicCast.Encode(new(state,0)), MagnitudePerRound:true),
                ExtendIncumbentDuration:true, IncumbentSettingsMatch:(_,_) => true, SourceScopedIncumbent:true);
        }
    }

    private static IEnumerable<IActiveEffectContribution> Validate(DaggerfallActiveEffect effect, int subtype)
    { DaggerfallPeriodicCast.Read(effect.State,1,subtype); return []; }

    internal static void EndOnDeath(DaggerfallEffectLifecycle effects, long target)
    {
        foreach (var instance in effects.Active.Where(effect => effect.Definition.Spell is { Type:1, SubType:>=0 and <=2 }
            && checked((long)effect.Context.Target.Value) == target).Select(effect => effect.Context.Instance).ToArray())
            effects.Cancel(instance);
    }
}
