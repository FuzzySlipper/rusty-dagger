using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallAttributeDrainState(DaggerfallCastEffectState Cast, int Magnitude);

/// <summary>One target-owned incumbent per attribute; damage changes live sources, never permanent bases.</summary>
internal static class DaggerfallAttributeDrainEffects
{
    private const string SourceDefinition = "daggerfall.attribute-drain";
    internal static readonly DaggerfallStatId[] Attributes =
    [DaggerfallMechanicsIds.Strength, DaggerfallMechanicsIds.Intelligence, DaggerfallMechanicsIds.Willpower,
     DaggerfallMechanicsIds.Agility, DaggerfallMechanicsIds.Endurance, DaggerfallMechanicsIds.Personality,
     DaggerfallMechanicsIds.Speed, DaggerfallMechanicsIds.Luck];

    internal static string Key(int subtype) => $"drain-{Attributes[subtype].Value}";
    internal static JsonElement Encode(DaggerfallAttributeDrainState state) =>
        JsonSerializer.SerializeToElement(state, DaggerfallSaveJsonContext.Default.DaggerfallAttributeDrainState);

    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(Func<DaggerfallCareerDefinition> career, Action<long, long> attacked)
    {
        for (int subtype = 0; subtype < Attributes.Length; subtype++)
        {
            int selected = subtype;
            yield return new(Key(selected), Key(selected), DaggerfallEffectStacking.RefreshDuration, 1, 1,
                Apply: effect =>
                {
                    var incoming = Read(effect.State, selected);
                    Update(effect, incoming with { Magnitude = Bounded(effect, selected, incoming.Magnitude) }, selected, career);
                    attacked(incoming.Cast.Origin!.CasterId, checked((long)effect.Context.Target.Value));
                    return Cleanup(effect, selected, career);
                },
                Resume: effect =>
                {
                    var state = Read(effect.State, selected);
                    VerifySources(effect, selected, state.Magnitude);
                    return Cleanup(effect, selected, career);
                },
                RefreshState: (effect, payload) =>
                {
                    var incoming = Read(payload, selected);
                    var incumbent = Read(effect.State, selected);
                    Update(effect, incumbent with { Magnitude = Bounded(effect, selected, (long)incumbent.Magnitude + incoming.Magnitude) }, selected, career);
                    attacked(incoming.Cast.Origin!.CasterId, checked((long)effect.Context.Target.Value));
                },
                Spell: new(7, selected, SupportsMagnitude: true, UntilHealed: true,
                    AllowedElements: DaggerfallMagicAllowedElements.Fire | DaggerfallMagicAllowedElements.Cold
                        | DaggerfallMagicAllowedElements.Poison | DaggerfallMagicAllowedElements.Shock | DaggerfallMagicAllowedElements.Magic,
                    AllowedTargets: DaggerfallMagicAllowedTargets.Other,
                    CreateState: cast => Encode(new(cast, cast.Amount))));
        }
    }

    /// <summary>Matching healing consumes only this drain, leaving disease/poison and other attributes untouched.</summary>
    internal static int Heal(DaggerfallEffectLifecycle effects, long targetId, DaggerfallStatId attribute, int amount,
        Func<DaggerfallCareerDefinition> career)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        int subtype = Array.IndexOf(Attributes, attribute);
        if (subtype < 0) throw new ArgumentException("Drain healing requires an attribute.", nameof(attribute));
        var effect = effects.Active.SingleOrDefault(effect => effect.Context.Target.Value == checked((ulong)targetId)
            && effect.Definition.Spell is { Type: 7 } binding && binding.SubType == subtype);
        if (effect is null || amount == 0) return 0;
        var state = Read(effect.State, subtype);
        int healed = Math.Min(amount, state.Magnitude);
        if (state.Magnitude == healed) effects.Cure(effect.Context.Instance);
        else Update(effect, state with { Magnitude = state.Magnitude - healed }, subtype, career);
        return healed;
    }

    internal static DaggerfallAttributeDrainState Read(JsonElement payload, int subtype)
    {
        var state = payload.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallAttributeDrainState);
        if (state?.Cast is not { } cast || cast.Settings is not { Type: 7 } setting || setting.SubType != subtype
            || cast.CasterLevel < 1 || cast.Amount < 0 || cast.SavePercent is < 1 or > 100 || state.Magnitude < 0
            || cast.Origin is not { CasterId: > 0 } origin || origin.ItemId == 0 || !Enum.IsDefined(origin.Source)
            || (origin.Source == DaggerfallCastSource.Spell) != (origin.ItemId is null))
            throw new ArgumentException("Attribute drain state does not match its admitted variant and origin.");
        return state;
    }

    private static int Bounded(DaggerfallActiveEffect effect, int subtype, long magnitude) =>
        checked((int)Math.Min(magnitude, Math.Max(0, StatFor(effect, subtype).BaseValue - 1)));
    private static Stat StatFor(DaggerfallActiveEffect effect, int subtype) => effect.Target.Get<StatsComponent>().GetStat(StatId.Parse(Attributes[subtype].Value));
    private static EffectSourceIdentity Identity(DaggerfallActiveEffect effect) => new(effect.Target.Entity,
        effect.Context.Instance, 1, SourceDefinitionId.Parse(SourceDefinition));

    private static void Update(DaggerfallActiveEffect effect, DaggerfallAttributeDrainState state, int subtype, Func<DaggerfallCareerDefinition> career)
    {
        Stat stat = StatFor(effect, subtype); StatId id = StatId.Parse(Attributes[subtype].Value);
        var identity = Identity(effect);
        List<StatSource> sources = [.. stat.Sources.Where(source => source.Identity != identity)];
        if (state.Magnitude > 0) sources.Add(new(identity, SourceDefinitionId.Parse(SourceDefinition), 0,
            [new(id, StackingGroupId.Parse($"daggerfall.drain.{id.Value}"), MechanicsStackingPolicy.Sum, new StatContribution.Add(-state.Magnitude))]));
        stat.SetSources(id, sources);
        effect.State = Encode(state);
        Refresh(effect, career);
    }

    private static IEnumerable<IActiveEffectContribution> Cleanup(DaggerfallActiveEffect effect, int subtype, Func<DaggerfallCareerDefinition> career) =>
        [new DelegateActiveEffectContribution(() => { StatFor(effect, subtype).RemoveSource(Identity(effect)); Refresh(effect, career); })];
    private static void Refresh(DaggerfallActiveEffect effect, Func<DaggerfallCareerDefinition> career)
    {
        if (effect.Context.Target.Value == checked((ulong)DaggerfallActorIdentity.PlayerEntityId))
            DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(effect.Target.Get<StatsComponent>(), career());
    }

    private static void VerifySources(DaggerfallActiveEffect effect, int subtype, int magnitude)
    {
        for (int index = 0; index < Attributes.Length; index++)
        {
            var matches = StatFor(effect, index).Sources.Where(source => source.Identity == Identity(effect)).ToArray();
            if (index != subtype || magnitude == 0)
            { if (matches.Length == 0) continue; }
            else if (matches.Length == 1 && matches[0].Definition == SourceDefinitionId.Parse(SourceDefinition)
                && matches[0].Contributions.Count == 1 && matches[0].Contributions[0].Stat == StatId.Parse(Attributes[subtype].Value)
                && matches[0].Contributions[0].Contribution is StatContribution.Add add && add.Amount == -magnitude) continue;
            throw new ArgumentException("Saved attribute drain sources disagree with their durable magnitude/attribute.");
        }
    }
}
