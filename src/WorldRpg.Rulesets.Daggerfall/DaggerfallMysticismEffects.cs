using WorldRpg.Rulesets.Daggerfall.Content;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The already-paid player choice; cancelling does not undo casting.</summary>
internal sealed record DaggerfallDispelRequest([property: JsonRequired] string Instance, [property: JsonRequired] int Chance)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Instance);
        if (Chance < 0) throw new ArgumentOutOfRangeException(nameof(Chance));
    }
}
internal sealed record DaggerfallDispelOption(string Id, string Label);
internal sealed record DaggerfallDispelView(string Revision, IReadOnlyList<DaggerfallDispelOption> Options);

internal static class DaggerfallMysticismEffects
{
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(Func<long, int> level,
        Action<DaggerfallDispelRequest> requestDispel, Action<DaggerfallActiveEffect, bool> banish,
        Action<long, long, string>? attacked = null, Action<string>? requestTeleport = null)
    {
        yield return new("silence", "silence", DaggerfallEffectStacking.RefreshDuration, 1, 1,
            Apply: effect =>
            {
                Read(effect, 19, -1);
                if (effect.Context.Caster is { } caster) attacked?.Invoke(checked((long)caster.Value), checked((long)effect.Context.Target.Value), effect.BundleId ?? effect.Context.Instance.Value);
                return [];
            }, Resume: effect => Validate(effect, 19, -1),
            Spell: new(19, -1, SpellMaker: true, SupportsDuration: true, RollChanceOnCast: true,
                AllowedElements: DaggerfallMagicAllowedElements.Fire | DaggerfallMagicAllowedElements.Cold
                    | DaggerfallMagicAllowedElements.Poison | DaggerfallMagicAllowedElements.Shock | DaggerfallMagicAllowedElements.Magic,
                AllowedTargets: DaggerfallMagicAllowedTargets.All),
            MagicDefense: _ => new(0, 0, [], BlocksCasting: true),
            ExtendIncumbentDuration: true, IncumbentSettingsMatch: (_, _) => true);
        yield return new("teleport", "teleport", DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
            Apply: effect =>
            {
                Read(effect, 43, -1);
                if (effect.Context.Target.Value != DaggerfallActorIdentity.PlayerEntityId || requestTeleport is null)
                    effect.InitialOutcome = DaggerfallEffectAdmissionOutcome.NoMatch;
                else requestTeleport(effect.Context.Instance.Value);
                return [];
            }, Resume: _ => throw new ArgumentException("Teleport is an immediate paid choice, not an ongoing effect."),
            Spell: new(43, -1, SpellMaker: true, AllowedTargets: DaggerfallMagicAllowedTargets.CasterOnly), ShowSpellIcon: false);
        yield return new("comprehend-languages", "comprehend-languages", DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
            Apply: effect => Validate(effect, 44, -1), Resume: effect => Validate(effect, 44, -1),
            Spell: new(44, -1, SpellMaker: true, SupportsDuration: true, AllowedTargets: DaggerfallMagicAllowedTargets.CasterOnly),
            ExtendIncumbentDuration: true, IncumbentSettingsMatch: (_, _) => true,
            LivePerception: effect => new(ComprehendLanguagesBonus: DaggerfallMagicAdmissionPolicy.CalculateEffectChance(
                Read(effect, 44, -1).Settings, level(checked((long)effect.Context.Target.Value)))));
        yield return new("dispel-magic", "dispel-magic", DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
            Apply: effect =>
            {
                var state = Read(effect, 6, 0);
                if (effect.Context.Target.Value != DaggerfallActorIdentity.PlayerEntityId) effect.InitialOutcome = DaggerfallEffectAdmissionOutcome.NoMatch;
                else requestDispel(new(effect.Context.Instance.Value, DaggerfallMagicAdmissionPolicy.CalculateEffectChance(state.Settings, state.CasterLevel)));
                return [];
            }, Resume: _ => throw new ArgumentException("Dispel Magic is an immediate request, not an ongoing effect."),
            Spell: new(6, 0, SpellMaker: true, AllowedTargets: DaggerfallMagicAllowedTargets.CasterOnly), ShowSpellIcon: false);
        foreach (int subtype in new[] { 1, 2 })
        {
            int variant = subtype;
            yield return new(variant == 1 ? "dispel-undead" : "dispel-daedra", variant == 1 ? "dispel-undead" : "dispel-daedra",
                DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
                Apply: effect => { Read(effect, 6, variant); banish(effect, variant == 2); return []; },
                Resume: _ => throw new ArgumentException("Creature dispel is an immediate action, not an ongoing effect."),
                Spell: new(6, variant, SpellMaker: true, AllowedTargets: DaggerfallMagicAllowedTargets.CasterOnly));
        }
    }
    internal static DaggerfallCastEffectState Read(DaggerfallActiveEffect effect, int type, int subtype)
    {
        var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        if (state?.Settings is not { } settings || settings.Type != type || settings.SubType != subtype || state.CasterLevel < 1
            || state.Amount != 0 || state.SavePercent is < 1 or > 100)
            throw new ArgumentException("Mysticism state does not match its admitted compiled variant.");
        return state;
    }
    private static IEnumerable<IActiveEffectContribution> Validate(DaggerfallActiveEffect effect, int type, int subtype)
    { Read(effect, type, subtype); return []; }
    internal static string Bundle(DaggerfallActiveEffect effect) => effect.BundleId ?? effect.Context.Source.Key;
    internal static DaggerfallActiveEffect[] Dispellable(DaggerfallEffectLifecycle effects) => effects.Active.Where(effect =>
        effect.Context.Target.Value == DaggerfallActorIdentity.PlayerEntityId
            && effect.BundleKind is DaggerfallEffectBundleKind.Spell or DaggerfallEffectBundleKind.HeldMagicItem)
        .GroupBy(Bundle).Where(bundle => bundle.Any(effect => effect.Definition.ShowSpellIcon
            && (effect.Lifecycle.RemainingRounds is not null || effect.Context.Item is not null)))
        .SelectMany(bundle => bundle).ToArray();
}
