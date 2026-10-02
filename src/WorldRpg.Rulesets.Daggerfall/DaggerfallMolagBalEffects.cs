using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallMolagBalState([property: JsonRequired] long LastStrikeMinute,
    [property: JsonRequired] double MagickaMaximumIncrease, [property: JsonRequired] int StrengthIncrease);

/// <summary>Artifact policy over the ordinary effect lifetime, stat sources, and world calendar.</summary>
internal static class DaggerfallMolagBalEffects
{
    internal const string Key = "artifact-molag-bal";
    private const string SourceDefinition = "daggerfall.artifact-molag-bal";
    private const int BonusMinutes = 12;
    private static readonly StatId Strength = StatId.Parse(DaggerfallMechanicsIds.Strength.Value);
    private static readonly StatId MagickaMaximum = StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value);

    internal static long Minute(DaggerfallCalendar calendar) => calendar.ToAbsoluteSeconds() / 60;
    internal static JsonElement Encode(DaggerfallMolagBalState state) =>
        JsonSerializer.SerializeToElement(state, DaggerfallSaveJsonContext.Default.DaggerfallMolagBalState);

    internal static DaggerfallEffectDefinition Definition(Func<DaggerfallCalendar> calendar,
        Func<DaggerfallCareerDefinition> career, Func<long, ulong, bool> equipped) =>
        new(Key, Key, DaggerfallEffectStacking.RefreshDuration, 1, 1,
            Apply: effect =>
            {
                ValidateOwner(effect, equipped); Update(effect, Read(effect.State), career);
                return Cleanup(effect, career);
            },
            Resume: effect =>
            {
                ValidateOwner(effect, equipped);
                var state = Read(effect.State);
                if (state.LastStrikeMinute > Minute(calendar()))
                    throw new ArgumentException("Saved Mace strike time is after the current world calendar.");
                foreach (var (id, stat) in Stats(effect).Stats)
                    if (id != Strength && id != MagickaMaximum && stat.Sources.Any(source => source.Identity == Identity(effect)))
                        throw new ArgumentException("Saved Mace bonus contributes to an unsupported stat.");
                VerifySource(effect, Strength, state.StrengthIncrease);
                VerifySource(effect, MagickaMaximum, state.MagickaMaximumIncrease);
                if (Stats(effect).GetStat(Strength).Maximum != StrengthMaximum(Stats(effect).GetStat(Strength)))
                    throw new ArgumentException("Saved Mace strength bounds disagree with its active bonus.");
                return Cleanup(effect, career);
            },
            RefreshState: (effect, incoming) =>
            {
                ValidateOwner(effect, equipped);
                var prior = Read(effect.State); var added = Read(incoming);
                Update(effect, new(added.LastStrikeMinute, prior.MagickaMaximumIncrease + added.MagickaMaximumIncrease,
                    checked(prior.StrengthIncrease + added.StrengthIncrease)), career);
            },
            MagicRound: effect =>
            {
                // The donor expires on the first round strictly after last strike + 12 minutes.
                if (!equipped(checked((long)effect.Context.Target.Value), effect.Context.Item!.Value.Value)
                    || Stats(effect).GetTrack(TrackId.Parse("health")).Current <= 0
                    || Minute(calendar()) > Read(effect.State).LastStrikeMinute + BonusMinutes)
                    effect.ExpireAfterCurrentRound = true;
            }, SourceScopedIncumbent: true, ShowSpellIcon: false);

    internal static void Reconcile(DaggerfallEffectLifecycle effects, Func<long, ulong, bool> equipped)
    {
        foreach (var effect in effects.Active.Where(effect => effect.Definition.Key == Key).ToArray())
            if (!equipped(checked((long)effect.Context.Target.Value), effect.Context.Item!.Value.Value)
                || Stats(effect).GetTrack(TrackId.Parse("health")).Current <= 0)
                effects.Cancel(effect.Context.Instance);
    }

    internal static void EndOnDeath(DaggerfallEffectLifecycle effects, long actor) =>
        Reconcile(effects, (owner, _) => owner != actor);

    internal static DaggerfallMolagBalState Read(JsonElement payload)
    {
        var state = payload.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallMolagBalState);
        if (state is null || state.LastStrikeMinute < 0 || !double.IsFinite(state.MagickaMaximumIncrease)
            || state.MagickaMaximumIncrease < 0 || state.StrengthIncrease < 0)
            throw new ArgumentException("Mace state requires a valid strike time and bounded positive bonuses.");
        return state;
    }

    internal static double SavedStrengthMaximum(IEnumerable<DaggerfallStatSourceSave> sources) =>
        DaggerfallFormulaPolicy.MaxStatValue() + sources.Where(source => source.StatId == Strength.Value
            && source.DefinitionId == SourceDefinition).Sum(source => source.Contributions
                .Where(contribution => contribution.StatId == Strength.Value && contribution.Kind == StatModifierKind.Add)
                .Sum(contribution => contribution.Value));

    private static void ValidateOwner(DaggerfallActiveEffect effect, Func<long, ulong, bool> equipped)
    {
        if (effect.Context.Item is not { } item || effect.Context.Caster?.Value != effect.Context.Target.Value
            || !equipped(checked((long)effect.Context.Target.Value), item.Value))
            throw new ArgumentException("A Mace bonus requires its living wielder and equipped source item.");
    }
    private static StatsComponent Stats(DaggerfallActiveEffect effect) => effect.Target.Get<StatsComponent>();
    private static EffectSourceIdentity Identity(DaggerfallActiveEffect effect) =>
        new(effect.Target.Entity, effect.Context.Instance, 1, SourceDefinitionId.Parse(SourceDefinition));

    private static void Update(DaggerfallActiveEffect effect, DaggerfallMolagBalState state,
        Func<DaggerfallCareerDefinition> career)
    {
        var stats = Stats(effect); var identity = Identity(effect);
        foreach (var (id, amount) in new[] { (Strength, (double)state.StrengthIncrease), (MagickaMaximum, state.MagickaMaximumIncrease) })
        {
            var stat = stats.GetStat(id);
            List<StatSource> sources = [.. stat.Sources.Where(source => source.Identity != identity)];
            if (amount > 0) sources.Add(new(identity, SourceDefinitionId.Parse(SourceDefinition), 0,
                [new(id, StackingGroupId.Parse(SourceDefinition), MechanicsStackingPolicy.Sum, new StatContribution.Add(amount))]));
            if (id == Strength) stat.Maximum = DaggerfallFormulaPolicy.MaxStatValue()
                + sources.Where(source => source.Definition.Value == SourceDefinition)
                    .Sum(source => source.Contributions.Sum(contribution => ((StatContribution.Add)contribution.Contribution).Amount));
            stat.SetSources(id, sources);
        }
        effect.State = Encode(state); Refresh(effect, career);
    }
    private static double StrengthMaximum(Stat stat) => DaggerfallFormulaPolicy.MaxStatValue()
        + stat.Sources.Where(source => source.Definition.Value == SourceDefinition)
            .Sum(source => source.Contributions.Sum(contribution => ((StatContribution.Add)contribution.Contribution).Amount));

    private static IEnumerable<IActiveEffectContribution> Cleanup(DaggerfallActiveEffect effect, Func<DaggerfallCareerDefinition> career) =>
        [new DelegateActiveEffectContribution(() =>
        {
            var stats = Stats(effect); var strength = stats.GetStat(Strength);
            strength.RemoveSource(Identity(effect)); strength.Maximum = StrengthMaximum(strength);
            stats.GetStat(MagickaMaximum).RemoveSource(Identity(effect)); Refresh(effect, career);
        })];
    private static void Refresh(DaggerfallActiveEffect effect, Func<DaggerfallCareerDefinition> career)
    {
        if (effect.Context.Target.Value == checked((ulong)DaggerfallActorIdentity.PlayerEntityId))
            DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(Stats(effect), career());
    }
    private static void VerifySource(DaggerfallActiveEffect effect, StatId id, double amount)
    {
        var sources = Stats(effect).GetStat(id).Sources.Where(source => source.Identity == Identity(effect)).ToArray();
        if (amount == 0 && sources.Length == 0) return;
        if (amount > 0 && sources.Length == 1 && sources[0].Definition.Value == SourceDefinition
            && sources[0].Contributions.Count == 1 && sources[0].Contributions[0].Stat == id
            && sources[0].Contributions[0].Contribution is StatContribution.Add added && added.Amount == amount) return;
        throw new ArgumentException("Saved Mace stat sources disagree with the artifact's retained bonuses.");
    }
}
