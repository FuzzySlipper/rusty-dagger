using System.Text.Json;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Rusty.Engine.Mechanics;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Immediate destruction consumes the casting owner's admitted magnitude/chance exactly once.</summary>
internal static class DaggerfallDestructionEffects
{
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(DaggerfallVitalityConsequences vitality,
        Action<DaggerfallEffectDamage> healthApplied, Action<DaggerfallSpellTrackResult> trackApplied,
        Func<long, bool> hostile, Action<long, long> attacked)
    {
        foreach (var (key, type, subtype) in new[] { ("damage-health", 4, 0), ("damage-fatigue", 4, 1),
            ("damage-spell-points", 4, 2), ("disintegrate", 5, -1) })
        {
            string selected = key;
            yield return new(key, key, DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
                Apply: effect => Validate(effect, type, subtype),
                Resume: _ => throw new ArgumentException("Immediate destruction cannot be saved as an active effect."),
                MagicRound: effect =>
                {
                    effect.ExpireAfterCurrentRound = true;
                    var state = Read(effect);
                    long target = checked((long)effect.Context.Target.Value);
                    // The donor protects peaceful non-player quest actors from fatigue drain.
                    if (selected == "damage-fatigue" && target != DaggerfallActorIdentity.PlayerEntityId && !hostile(target)) return;
                    if (selected is "damage-health" or "disintegrate")
                        healthApplied(new(vitality.ResolveSpellHealth(effect.Source, effect.Target, state.Amount, selected == "disintegrate")));
                    else
                    {
                        bool fatigue = selected == "damage-fatigue";
                        int amount = fatigue ? DaggerfallFormulaPolicy.SpellFatigueDamage(state.Amount) : state.Amount;
                        trackApplied(vitality.ResolveSpellTrack(effect.Source, effect.Target,
                            TrackId.Parse(fatigue ? DaggerfallMechanicsIds.Stamina.Value : DaggerfallMechanicsIds.Magicka.Value), amount));
                    }
                    // A DFU dungeon CastSpell bundle has no entity caster. Its admitted action
                    // source supplies the player-level power, while the target fallback above
                    // supplies the ordinary consequence owner; there is no actor to notify as an
                    // attacker or to use as an aggro identity.
                    if (effect.Context.Caster is { } liveCaster)
                        attacked(checked((long)liveCaster.Value), target);
                },
                Spell: new(type, subtype, SpellMaker: true, SupportsMagnitude: type == 4, RollChanceOnCast: type == 5,
                    AllowedElements: DaggerfallMagicAllowedElements.Fire | DaggerfallMagicAllowedElements.Cold
                        | DaggerfallMagicAllowedElements.Poison | DaggerfallMagicAllowedElements.Shock | DaggerfallMagicAllowedElements.Magic));
        }
    }

    private static DaggerfallCastEffectState Read(DaggerfallActiveEffect effect) =>
        effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)
            ?? throw new ArgumentException("Immediate destruction cast state is missing.");

    private static IEnumerable<IActiveEffectContribution> Validate(DaggerfallActiveEffect effect, int type, int subtype)
    {
        var state = Read(effect);
        bool actorlessDungeonAction = effect.Context.Caster is null
            && state.Origin is { Source: DaggerfallCastSource.DungeonAction, ActionSource: { IsValid: true } };
        if ((!actorlessDungeonAction && effect.Context.Caster is null) || state.Settings.Type != type || state.Settings.SubType != subtype
            || state.CasterLevel < 1 || state.Amount < 0 || state.SavePercent is < 1 or > 100)
            throw new ArgumentException("Immediate destruction state does not match its admitted compiled variant.");
        return [];
    }
}
