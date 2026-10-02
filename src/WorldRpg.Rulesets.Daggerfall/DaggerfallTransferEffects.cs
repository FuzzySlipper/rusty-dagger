using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal static class DaggerfallTransferEffects
{
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(DaggerfallVitalityConsequences vitality,
        Action<DaggerfallSpellTransferResult> applied, Func<long, bool> hostile, Action<long, long> attacked)
    {
        foreach (int subtype in new[] { 8, 9 })
        {
            int selected = subtype;
            string key = selected == 8 ? "transfer-health" : "transfer-fatigue";
            yield return new(key, key, DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
                Apply: effect => Validate(effect, selected),
                Resume: _ => throw new ArgumentException("Immediate transfers cannot remain active in a current save."),
                MagicRound: effect =>
                {
                    effect.ExpireAfterCurrentRound = true;
                    var state = Read(effect);
                    if (effect.Caster is not { } source || source.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).Current <= 0)
                    { effect.InitialOutcome = DaggerfallEffectAdmissionOutcome.SourceUnavailable; return; }
                    if (effect.Target.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).Current <= 0)
                    { effect.InitialOutcome = DaggerfallEffectAdmissionOutcome.TargetUnavailable; return; }
                    long caster = checked((long)effect.Context.Caster!.Value.Value);
                    long target = checked((long)effect.Context.Target.Value);
                    // The wrapper protects peaceful foes from fatigue loss, but Transfer explicitly restores and causes aggression.
                    applied(vitality.ResolveSpellTransfer(source, effect.Target, state.Amount, selected == 9,
                        target == DaggerfallActorIdentity.PlayerEntityId || hostile(target)));
                    attacked(caster, target);
                },
                Spell: new(11, selected, SpellMaker: true, SupportsMagnitude: true, AllowedTargets: DaggerfallMagicAllowedTargets.Other));
        }
    }

    private static DaggerfallCastEffectState Read(DaggerfallActiveEffect effect) =>
        effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)
            ?? throw new ArgumentException("Transfer cast state is missing.");
    private static IEnumerable<IActiveEffectContribution> Validate(DaggerfallActiveEffect effect, int subtype)
    {
        var state = Read(effect);
        if (state.Settings is not { Type: 11 } settings || settings.SubType != subtype
            || state.CasterLevel < 1 || state.Amount < 0 || state.SavePercent is < 1 or > 100)
            throw new ArgumentException("Transfer state does not match its admitted compiled variant and caster.");
        return [];
    }
}
