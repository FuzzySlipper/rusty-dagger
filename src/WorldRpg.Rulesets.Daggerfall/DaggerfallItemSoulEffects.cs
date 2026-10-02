using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallConjuredItem([property: JsonRequired] string Source, [property: JsonRequired] long ExpiresAtMinute)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        if (ExpiresAtMinute < 0) throw new ArgumentOutOfRangeException(nameof(ExpiresAtMinute));
    }
}
internal sealed record DaggerfallCreateItemRequest([property: JsonRequired] string Instance, [property: JsonRequired] uint Duration, [property: JsonRequired] DaggerfallCreateItemRequest? Next = null)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Instance);
        if (Duration == 0) throw new ArgumentOutOfRangeException(nameof(Duration));
        Next?.Validate();
    }
}
internal sealed record DaggerfallCreateItemOption(string Id, string Label, string Category, int Template, string? Material);
internal sealed record DaggerfallCreateItemView(string Revision, IReadOnlyList<DaggerfallCreateItemOption> Options);
internal sealed record DaggerfallSoulTrapState([property: JsonRequired] DaggerfallCastEffectState Cast, [property: JsonRequired] long Attempts = 0,
    [property: JsonRequired] bool Captured = false);

internal static class DaggerfallItemSoulEffects
{
    internal static bool WasSoulCaptured(DaggerfallEffectLifecycle effects, long target) =>
        effects.Active.Any(effect => effect.Definition.Key == "soul-trap"
            && checked((long)effect.Context.Target.Value) == target && Read(effect).Captured);

    internal static void EndOnDeath(DaggerfallEffectLifecycle effects, long target)
    {
        foreach (var instance in effects.Active.Where(effect => effect.Definition.Key == "soul-trap"
            && checked((long)effect.Context.Target.Value) == target).Select(effect => effect.Context.Instance).ToArray())
            effects.Cancel(instance);
    }

    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(Action<DaggerfallCreateItemRequest> requestItem,
        Func<long, int?> monsterMobile, Func<int, bool> captureSoul, IRandomService random, Action<long, string, bool> outcome)
    {
        yield return new("create-item", "create-item", DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
            Apply: effect => { DaggerfallMysticismEffects.Read(effect, 2, -1); return []; },
            MagicRound: effect =>
            {
                if (effect.Context.Target.Value != DaggerfallActorIdentity.PlayerEntityId) effect.InitialOutcome = DaggerfallEffectAdmissionOutcome.NoMatch;
                else requestItem(new(effect.Context.Instance.Value, effect.Lifecycle.RemainingRounds ?? 1));
                effect.ExpireAfterCurrentRound = true;
            }, Resume: _ => throw new ArgumentException("Create Item is an immediate paid selection."),
            Spell: new(2, -1, SpellMaker: true, SupportsDuration: true, AllowedTargets: DaggerfallMagicAllowedTargets.CasterOnly), ShowSpellIcon: false);
        yield return new("soul-trap", "soul-trap", DaggerfallEffectStacking.RefreshDuration, 1, 1,
            Apply: effect => Attach(effect, monsterMobile, captureSoul, random, outcome),
            Resume: effect => Attach(effect, monsterMobile, captureSoul, random, outcome, resumed: true),
            Spell: new(12, -1, SpellMaker: true, SupportsDuration: true, AllowedElements: DaggerfallMagicAllowedElements.Fire | DaggerfallMagicAllowedElements.Cold
                | DaggerfallMagicAllowedElements.Poison | DaggerfallMagicAllowedElements.Shock | DaggerfallMagicAllowedElements.Magic,
                AllowedTargets: DaggerfallMagicAllowedTargets.Other, CreateState: state => Serialize(new(state))),
            ExtendIncumbentDuration: true, IncumbentSettingsMatch: (_, _) => true, ShowSpellIcon: false);
    }
    private static JsonElement Serialize(DaggerfallSoulTrapState state) => JsonSerializer.SerializeToElement(state, DaggerfallSaveJsonContext.Default.DaggerfallSoulTrapState);
    private static DaggerfallSoulTrapState Read(DaggerfallActiveEffect effect)
    {
        var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallSoulTrapState);
        if (state?.Cast is not { Settings.Type: 12, Settings.SubType: -1, CasterLevel: >= 1, Amount: 0, SavePercent: >= 1 and <= 100 }
            || state.Attempts < 0) throw new ArgumentException("Soul Trap state does not match its compiled effect.");
        return state;
    }
    private static IEnumerable<IActiveEffectContribution> Attach(DaggerfallActiveEffect effect, Func<long, int?> monster,
        Func<int, bool> capture, IRandomService random, Action<long, string, bool> outcome, bool resumed = false)
    {
        Read(effect);
        long target = checked((long)effect.Context.Target.Value);
        if (monster(target) is not int)
        {
            if (resumed) throw new ArgumentException("Saved Soul Trap target is not an eligible creature.");
            effect.InitialOutcome = DaggerfallEffectAdmissionOutcome.NoMatch;
            effect.ExpireAfterCurrentRound = true;
            return [];
        }
        var rules = effect.Target.Get<CombatContributions>();
        var contribution = new SoulTrapContribution(effect, monster, capture, random, outcome);
        rules.Rules.Add(contribution);
        return [new DelegateActiveEffectContribution(() => rules.Rules.Remove(contribution))];
    }
    private sealed class SoulTrapContribution(DaggerfallActiveEffect effect, Func<long, int?> monster, Func<int, bool> capture,
        IRandomService random, Action<long, string, bool> outcome) : ICombatContribution
    {
        public void Defeating(DefeatEvent interaction)
        {
            if (interaction.Participants.Target.Entity != effect.Target.Entity) return;
            if (monster(checked((long)effect.Context.Target.Value)) is not int mobile) return;
            var state = Read(effect);
            effect.State = Serialize(state with { Attempts = checked(state.Attempts + 1) });
            bool success = random.DrawKeyed(new(CombatRandomKey.Seed, "daggerfall.soul-trap.v1",
                $"{effect.Context.Instance.Value}:attempt:{state.Attempts}", 1, 100)).Value
                <= DaggerfallMagicAdmissionPolicy.CalculateEffectChance(state.Cast.Settings, state.Cast.CasterLevel);
            bool filled = success && capture(mobile);
            effect.State = Serialize(state with { Attempts = checked(state.Attempts + 1), Captured = filled });
            if (success && !filled) interaction.RetainedHealth = Math.Max(interaction.MinimumHealth, 1);
            outcome(checked((long)effect.Context.Target.Value), filled ? "Soul trapped." : success ? "No empty soul gem; the creature remains alive." : "Soul trap failed.", !success || filled);
        }
    }
}
