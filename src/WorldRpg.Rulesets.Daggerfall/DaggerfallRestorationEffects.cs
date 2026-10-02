using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Recovery and defensive policy over active effects, canonical tracks and cast admission.</summary>
internal static class DaggerfallRestorationEffects
{
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(IRandomService random, Func<long, int> level, DaggerfallVitalityConsequences vitality)
    {
        yield return new("free-action", "free-action", DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
            Apply: effect => ValidateCast(effect, 26), Resume: effect => ValidateCast(effect, 26),
            Spell: new(26, -1, SpellMaker: true, SupportsDuration: true),
            MagicDefense: _ => new(0, 0, [], PreventsParalysis: true), ExtendIncumbentDuration: true,
            IncumbentSettingsMatch: (_, _) => true, SourceScopedIncumbent: true);
        yield return new("spell-absorption", "spell-absorption", DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
            Apply: effect => ValidateCast(effect, 20), Resume: effect => ValidateCast(effect, 20),
            Spell: new(20, -1, SpellMaker: true, SupportsDuration: true),
            MagicDefense: effect => new(Math.Clamp(DaggerfallMagicAdmissionPolicy.CalculateEffectChance(
                ReadCast(effect.State).Settings, level(checked((long)effect.Context.Target.Value))), 0, 100), 0, []),
            ExtendIncumbentDuration: true, IncumbentSettingsMatch: (_, _) => true, SourceScopedIncumbent: true);
        // Different settings coexist; equivalent settings extend one incumbent without an extra heal.
        // Independent Engine provenance permits those distinct incumbents within the compiled family.
        yield return new("regenerate", "regenerate", DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
            Apply: ValidateRegeneration, Resume: ValidateRegeneration,
            MagicRound: effect =>
            {
                var state = ReadRegeneration(effect.State);
                Track health = effect.Target.Get<StatsComponent>().GetTrack(TrackId.Parse("health"));
                if (health.Current <= 0) { effect.ExpireAfterCurrentRound = true; return; }
                int amount = DaggerfallPeriodicCast.RollMagnitude(effect, random, 18, -1, "daggerfall.regenerate.v1", "");
                vitality.RestoreSpellTrack(effect.Target, TrackId.Parse("health"), amount);
            },
            Spell: new(18, -1, SpellMaker: true, SupportsDuration: true, SupportsMagnitude: true,
                CreateState: state => RegenerationState(new(state, 0)), MagnitudePerRound: true),
            ExtendIncumbentDuration: true,
            IncumbentSettingsMatch: (prior, incoming) => SameSettings(ReadRegeneration(prior).Cast, ReadRegeneration(incoming).Cast),
            SourceScopedIncumbent: true);
    }

    private static bool SameSettings(DaggerfallCastEffectState prior, DaggerfallCastEffectState incoming) =>
        prior.Settings with { Key = "settings" } == incoming.Settings with { Key = "settings" };

    internal static JsonElement RegenerationState(DaggerfallPeriodicCastState state) => DaggerfallPeriodicCast.Encode(state);
    internal static DaggerfallPeriodicCastState ReadRegeneration(JsonElement state) => DaggerfallPeriodicCast.Read(state, 18, -1);
    private static IEnumerable<IActiveEffectContribution> ValidateRegeneration(DaggerfallActiveEffect effect)
    { ReadRegeneration(effect.State); return []; }
    private static IEnumerable<IActiveEffectContribution> ValidateCast(DaggerfallActiveEffect effect, int type)
    { Validate(ReadCast(effect.State), type); return []; }
    private static DaggerfallCastEffectState ReadCast(JsonElement state) =>
        state.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)
            ?? throw new ArgumentException("Recovery/defense cast state is missing.");
    private static void Validate(DaggerfallCastEffectState state, int type)
    {
        if (state.Settings is not { SubType: -1 } || state.Settings.Type != type || state.CasterLevel < 1
            || state.SavePercent is < 1 or > 100 || state.Amount < 0)
            throw new ArgumentException("Recovery/defense state does not match its admitted compiled variant.");
    }
}
