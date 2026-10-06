using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>A computed potion payload, with no invented classic spell identity.</summary>
internal sealed record DaggerfallSpellPointHealingState(int Amount);

internal static class DaggerfallHealingEffects
{
    internal static JsonElement SpellPointState(int amount) => JsonSerializer.SerializeToElement(
        new DaggerfallSpellPointHealingState(amount), DaggerfallSaveJsonContext.Default.DaggerfallSpellPointHealingState);

    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(Func<DaggerfallEffectLifecycle> effects,
        Func<DaggerfallCareerDefinition> career, DaggerfallVitalityConsequences vitality)
    {
        for (int subtype = 0; subtype < 10; subtype++)
        {
            int selected = subtype;
            string key = subtype < 8 ? $"heal-{DaggerfallAttributeDrainEffects.Attributes[subtype].Value}"
                : subtype == 8 ? "heal-health" : "heal-fatigue";
            yield return new(key, key, DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
                Apply: effect => Validate(effect, selected), Resume: CannotResume,
                MagicRound: effect =>
                {
                    effect.ExpireAfterCurrentRound = true;
                    if (!Alive(effect)) return;
                    var state = Read(effect);
                    if (selected < 8)
                    {
                        long target = checked((long)effect.Context.Target.Value);
                        bool matching = effects().Active.Any(active => active.Context.Target == effect.Context.Target
                            && DaggerfallAttributeDrainEffects.IsAttributeDamage(active.Definition, selected));
                        DaggerfallAttributeDrainEffects.Heal(effects(), target, DaggerfallAttributeDrainEffects.Attributes[selected], state.Amount, career);
                        if (!matching) effect.InitialOutcome = DaggerfallEffectAdmissionOutcome.NoMatch;
                    }
                    else vitality.RestoreSpellTrack(effect.Target,
                        TrackId.Parse(selected == 8 ? DaggerfallMechanicsIds.Health.Value : DaggerfallMechanicsIds.Stamina.Value),
                        selected == 9 ? DaggerfallFormulaPolicy.SpellFatigueDamage(state.Amount) : state.Amount);
                }, Spell: new(10, selected, SpellMaker: true, SupportsMagnitude: true));
        }
        // HealSpellPoints is PotionMaker-only in the donor and has no ClassicKey. Recipe/drinking owners
        // supply the computed magnitude to this named self-targeted compiled definition.
        yield return new("heal-spell-points", "heal-spell-points", DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
            Apply: effect =>
            {
                var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallSpellPointHealingState);
                if (state is null || state.Amount < 0 || effect.Context.Caster != effect.Context.Target || effect.Context.Element != "Magic")
                    throw new ArgumentException("Spell-point healing requires a non-negative Magic self-targeted potion payload.");
                return [];
            }, Resume: CannotResume, MagicRound: effect =>
            {
                effect.ExpireAfterCurrentRound = true;
                if (Alive(effect)) vitality.RestoreSpellTrack(effect.Target, TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value),
                    effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallSpellPointHealingState)!.Amount);
            },
            // Restore Power sparkles on its drinker like every other potion effect.
            Feedback: DaggerfallEffectFeedback.MagicSparkle);
    }

    private static bool Alive(DaggerfallActiveEffect effect)
    {
        if (effect.Target.Get<StatsComponent>().GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current > 0) return true;
        effect.InitialOutcome = DaggerfallEffectAdmissionOutcome.TargetUnavailable;
        return false;
    }
    private static IEnumerable<IActiveEffectContribution> CannotResume(DaggerfallActiveEffect _) =>
        throw new ArgumentException("Immediate healing cannot remain active in a current save.");
    private static DaggerfallCastEffectState Read(DaggerfallActiveEffect effect) =>
        effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)
            ?? throw new ArgumentException("Healing cast state is missing.");
    private static IEnumerable<IActiveEffectContribution> Validate(DaggerfallActiveEffect effect, int subtype)
    {
        var state = Read(effect);
        if (state.Settings is not { Type: 10 } setting || setting.SubType != subtype || state.CasterLevel < 1
            || state.Amount < 0 || state.SavePercent is < 1 or > 100)
            throw new ArgumentException("Healing state does not match its admitted compiled variant.");
        return [];
    }
}
