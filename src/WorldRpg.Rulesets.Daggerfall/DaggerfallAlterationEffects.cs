using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallShieldState([property: JsonRequired] int Starting, [property: JsonRequired] int Remaining);

/// <summary>Compiled alteration policy over the canonical effect and accepted-damage owners.</summary>
internal static class DaggerfallAlterationEffects
{
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(Action<DaggerfallActiveEffect> depleted)
    {
        foreach (var element in new[] { DaggerfallMagicResistanceElement.Fire, DaggerfallMagicResistanceElement.Frost,
            DaggerfallMagicResistanceElement.DiseaseOrPoison, DaggerfallMagicResistanceElement.Shock, DaggerfallMagicResistanceElement.Magic })
        {
            var selected = element;
            string key = $"resist-{element.ToString().ToLowerInvariant()}";
            yield return new(key, key, DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
                Apply: effect => ValidateResistance(effect, selected), Resume: effect => ValidateResistance(effect, selected),
                Feedback: DaggerfallEffectFeedback.MagicSparkle,
                Spell: new(8, (int)element, SpellMaker: true, SupportsDuration: true),
                MagicDefense: effect =>
                {
                    var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)
                        ?? throw new ArgumentException("Resistance effect state is missing.");
                    return new(0, 0, [new(selected,
                        DaggerfallMagicAdmissionPolicy.CalculateEffectChance(state.Settings, state.CasterLevel))]);
                });
        }
        yield return new("shield", "shield", DaggerfallEffectStacking.RefreshDuration, 1, 1,
            Apply: effect => AttachShield(effect, depleted), Resume: effect => AttachShield(effect, depleted, resumed: true),
            Feedback: DaggerfallEffectFeedback.MagicSparkle,
            Spell: new(35, -1, SpellMaker: true, SupportsDuration: true, SupportsMagnitude: true,
                CreateState: state => ShieldState(new(state.Amount, state.Amount))),
            RefreshState: (incumbent, incoming) =>
            {
                var prior = ReadShield(incumbent.State);
                var added = ReadShield(incoming);
                incumbent.State = ShieldState(prior with { Remaining = (int)Math.Min(prior.Starting, (long)prior.Remaining + added.Starting) });
            }, ExtendIncumbentDuration: true);
    }

    internal static DaggerfallShieldState ReadShield(JsonElement state)
    {
        var value = state.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallShieldState)
            ?? throw new ArgumentException("Shield effect state is missing.");
        if (value.Starting < 0 || value.Remaining < 0 || value.Remaining > value.Starting)
            throw new ArgumentException("Shield effect state contains an invalid remaining pool.");
        return value;
    }

    internal static JsonElement ShieldState(DaggerfallShieldState state) =>
        JsonSerializer.SerializeToElement(state, DaggerfallSaveJsonContext.Default.DaggerfallShieldState);

    private static IEnumerable<IActiveEffectContribution> ValidateResistance(DaggerfallActiveEffect effect, DaggerfallMagicResistanceElement element)
    {
        var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        if (state?.Settings is not { Type: 8 } settings || settings.SubType != (int)element || state.CasterLevel < 1)
            throw new ArgumentException("Resistance effect state does not match its compiled variant.");
        return [];
    }

    private static IEnumerable<IActiveEffectContribution> AttachShield(DaggerfallActiveEffect effect, Action<DaggerfallActiveEffect> depleted, bool resumed = false)
    {
        var state = ReadShield(effect.State);
        if (resumed && state.Remaining == 0) throw new ArgumentException("A saved active shield cannot have a depleted pool.");
        if (state.Remaining == 0) effect.ExpireAfterCurrentRound = true;
        var rules = effect.Target.Get<CombatContributions>();
        var contribution = new ShieldContribution(effect, depleted);
        rules.Rules.Add(contribution);
        return [new DelegateActiveEffectContribution(() => rules.Rules.Remove(contribution))];
    }

    private sealed class ShieldContribution(DaggerfallActiveEffect effect, Action<DaggerfallActiveEffect> depleted) : ICombatContribution
    {
        public void Applying(ApplyHitEvent interaction)
        {
            if (interaction.Mode == HealthApplicationMode.Terminal
                || interaction.Participants.Target.Entity != effect.Target.Entity || interaction.Damage <= 0
                || effect.Target.Get<StatsComponent>().GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current <= 0)
                return;
            var prior = ReadShield(effect.State);
            int absorbed = Math.Min(prior.Remaining, interaction.Damage);
            interaction.Damage -= absorbed;
            effect.State = ShieldState(prior with { Remaining = prior.Remaining - absorbed });
            if (prior.Remaining - absorbed == 0) depleted(effect);
        }
    }
}
