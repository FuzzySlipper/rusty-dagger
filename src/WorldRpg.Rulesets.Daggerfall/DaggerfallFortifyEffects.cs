using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Temporary source-scoped attribute mods; equivalent settings keep the incumbent magnitude and add duration.</summary>
internal static class DaggerfallFortifyEffects
{
    private const string SourceDefinition = "daggerfall.attribute-fortify";
    internal static string Key(int subtype) => $"fortify-{DaggerfallMechanicsIds.Attributes[subtype].Value}";
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(Func<DaggerfallCareerDefinition> career)
    {
        for (int subtype = 0; subtype < DaggerfallMechanicsIds.Attributes.Length; subtype++)
        {
            int selected = subtype;
            yield return new(Key(selected), Key(selected), DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
                Apply: effect =>
                {
                    var state = Read(effect.State, selected);
                    Stat stat = StatFor(effect, selected); StatId id = StatId.Parse(DaggerfallMechanicsIds.Attributes[selected].Value);
                    List<StatSource> sources = [.. stat.Sources];
                    if (state.Amount > 0) sources.Add(new(Identity(effect), SourceDefinitionId.Parse(SourceDefinition), 0,
                        [new(id, StackingGroupId.Parse($"daggerfall.fortify.{id.Value}"), MechanicsStackingPolicy.Sum, new StatContribution.Add(state.Amount))]));
                    stat.SetSources(id, sources); Refresh(effect, career);
                    return Cleanup(effect, selected, career);
                },
                Resume: effect =>
                {
                    var state = Read(effect.State, selected);
                    VerifySources(effect, selected, state.Amount);
                    return Cleanup(effect, selected, career);
                },
                Spell: new(9, selected, SpellMaker: true, SupportsDuration: true, SupportsMagnitude: true),
                ExtendIncumbentDuration: true,
                IncumbentSettingsMatch: (prior, incoming) =>
                    Read(prior, selected).Settings with { Key = "settings" } == Read(incoming, selected).Settings with { Key = "settings" });
        }
    }

    private static DaggerfallCastEffectState Read(JsonElement payload, int subtype)
    {
        var state = payload.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        if (state?.Settings is not { Type: 9 } setting || setting.SubType != subtype || state.CasterLevel < 1
            || state.Amount < 0 || state.SavePercent is < 1 or > 100)
            throw new ArgumentException("Fortification state does not match its admitted compiled attribute.");
        return state;
    }
    private static Stat StatFor(DaggerfallActiveEffect effect, int subtype) =>
        effect.Target.Get<StatsComponent>().GetStat(StatId.Parse(DaggerfallMechanicsIds.Attributes[subtype].Value));
    private static EffectSourceIdentity Identity(DaggerfallActiveEffect effect) => new(effect.Target.Entity,
        effect.Context.Instance, 1, SourceDefinitionId.Parse(SourceDefinition));
    private static IEnumerable<IActiveEffectContribution> Cleanup(DaggerfallActiveEffect effect, int subtype, Func<DaggerfallCareerDefinition> career) =>
        [new DelegateActiveEffectContribution(() => { StatFor(effect, subtype).RemoveSource(Identity(effect)); Refresh(effect, career); })];
    private static void Refresh(DaggerfallActiveEffect effect, Func<DaggerfallCareerDefinition> career)
    {
        if (effect.Context.Target.Value == checked((ulong)DaggerfallActorIdentity.PlayerEntityId))
            DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(effect.Target.Get<StatsComponent>(), career());
    }
    private static void VerifySources(DaggerfallActiveEffect effect, int subtype, int amount)
    {
        for (int index = 0; index < DaggerfallMechanicsIds.Attributes.Length; index++)
        {
            var matches = StatFor(effect, index).Sources.Where(source => source.Identity == Identity(effect)).ToArray();
            if (index != subtype || amount == 0)
            { if (matches.Length == 0) continue; }
            else if (matches.Length == 1 && matches[0].Definition == SourceDefinitionId.Parse(SourceDefinition)
                && matches[0].Contributions.Count == 1 && matches[0].Contributions[0].Stat == StatId.Parse(DaggerfallMechanicsIds.Attributes[subtype].Value)
                && matches[0].Contributions[0].Contribution is StatContribution.Add add && add.Amount == amount) continue;
            throw new ArgumentException("Saved fortification sources disagree with their durable magnitude/attribute.");
        }
    }
}
