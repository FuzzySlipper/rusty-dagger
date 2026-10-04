using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Durable meaning captured by one paid temple blessing.</summary>
internal sealed record DaggerfallTempleBlessingState(
    int DeityFactionId,
    DaggerfallTempleBlessingTarget Target,
    int Region,
    int Magnitude,
    int DurationMinutes,
    long ExpiresAtMinute)
{
    internal DaggerfallTempleBlessingState Validate()
    {
        if (!DaggerfallTemplePolicy.TryResolveTarget(DeityFactionId, out DaggerfallTempleBlessingTarget expected)
            || expected != Target
            || Target == DaggerfallTempleBlessingTarget.None
            || Magnitude is < 2 or > 11
            || DurationMinutes is < 1 or > DaggerfallTemplePolicy.MaximumBlessingMinutes
            || ExpiresAtMinute < 0
            || (Target == DaggerfallTempleBlessingTarget.LegalReputation
                ? Region is < 0 or > 61
                : Region != -1))
            throw new ArgumentException("Temple blessing state is malformed.");
        return this;
    }
}

/// <summary>
/// The one shared active-effect implementation for all seven paid temple blessings. Stat and
/// legal contributions are installed through their current readers, so save/restore and expiry
/// remove only this blessing's source while unrelated effects remain active.
/// </summary>
internal static class DaggerfallTempleBlessingEffects
{
    internal const string Key = "temple-blessing";
    private const string SourceDefinition = "daggerfall.temple-blessing";

    internal static DaggerfallEffectDefinition Definition(
        DaggerfallSocialState social,
        Func<DaggerfallCareerDefinition> career,
        long playerId)
    {
        ArgumentNullException.ThrowIfNull(social);
        ArgumentNullException.ThrowIfNull(career);
        if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
        return new(
            Key,
            Key,
            DaggerfallEffectStacking.Replace,
            MaximumInstances: 1,
            MaximumStacks: 1,
            Apply: effect => Apply(effect, social, career, playerId),
            Resume: effect => Apply(effect, social, career, playerId),
            ShowSpellIcon: false);
    }

    internal static JsonElement Encode(DaggerfallTempleBlessingState state) =>
        JsonSerializer.SerializeToElement(state.Validate(), DaggerfallSaveJsonContext.Default.DaggerfallTempleBlessingState);

    internal static DaggerfallTempleBlessingState Read(JsonElement state) =>
        (state.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallTempleBlessingState)
            ?? throw new ArgumentException("Temple blessing effect has no state.")).Validate();

    private static IEnumerable<IActiveEffectContribution> Apply(
        DaggerfallActiveEffect effect,
        DaggerfallSocialState social,
        Func<DaggerfallCareerDefinition> career,
        long playerId)
    {
        DaggerfallTempleBlessingState state = Read(effect.State);
        if (effect.Context.Target.Value != checked((ulong)playerId))
            throw new ArgumentException("Temple blessing effects target only the player.");

        MechanicsSourceIdentity identity = Identity(effect);
        if (state.Target == DaggerfallTempleBlessingTarget.LegalReputation)
        {
            social.SetRegionalReputationSource(state.Region, identity, state.Magnitude);
            return [new DelegateActiveEffectContribution(() => social.RemoveRegionalReputationSource(state.Region, identity))];
        }

        DaggerfallStatId statId = StatFor(state.Target);
        StatsComponent stats = effect.Target.Get<StatsComponent>();
        Stat stat = stats.GetStat(StatId.Parse(statId.Value));
        StatId id = StatId.Parse(statId.Value);
        stat.SetSources(id, [.. stat.Sources.Where(source => source.Identity != identity),
            new StatSource(identity, SourceDefinitionId.Parse(SourceDefinition), 0,
                [new StatContributionDefinition(id,
                    StackingGroupId.Parse($"daggerfall.temple-blessing.{statId.Value}"),
                    MechanicsStackingPolicy.Sum, new StatContribution.Add(state.Magnitude))])]);
        RefreshMaxima(effect, career, playerId);
        return [new DelegateActiveEffectContribution(() =>
        {
            stat.RemoveSource(identity);
            RefreshMaxima(effect, career, playerId);
        })];
    }

    private static void RefreshMaxima(DaggerfallActiveEffect effect, Func<DaggerfallCareerDefinition> career, long playerId)
    {
        if (effect.Context.Target.Value == checked((ulong)playerId))
            DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(effect.Target.Get<StatsComponent>(), career());
    }

    private static DaggerfallStatId StatFor(DaggerfallTempleBlessingTarget target) => target switch
    {
        DaggerfallTempleBlessingTarget.Speed => DaggerfallMechanicsIds.Speed,
        DaggerfallTempleBlessingTarget.Luck => DaggerfallMechanicsIds.Luck,
        DaggerfallTempleBlessingTarget.Intelligence => DaggerfallMechanicsIds.Intelligence,
        DaggerfallTempleBlessingTarget.Endurance => DaggerfallMechanicsIds.Endurance,
        DaggerfallTempleBlessingTarget.Personality => DaggerfallMechanicsIds.Personality,
        DaggerfallTempleBlessingTarget.Mercantile => new DaggerfallStatId("mercantile"),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "This temple target is not an actor stat."),
    };

    private static EffectSourceIdentity Identity(DaggerfallActiveEffect effect) => new(
        effect.Target.Entity,
        effect.Context.Instance,
        1,
        SourceDefinitionId.Parse(SourceDefinition));
}

