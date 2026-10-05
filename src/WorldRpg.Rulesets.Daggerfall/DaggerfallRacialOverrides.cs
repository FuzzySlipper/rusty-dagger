using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallRacialKind { Werewolf, Wereboar }

/// <summary>Meaningful permanent curse values inside the existing source-owned effect save.</summary>
internal sealed record DaggerfallRacialOverrideState(
    [property: JsonRequired] DaggerfallRacialKind Kind,
    [property: JsonRequired] bool BeastForm,
    [property: JsonRequired] long AcquiredMinute,
    [property: JsonRequired] long LastInnocentKilledMinute,
    long? LastMorphMinute = null)
{
    internal DaggerfallRacialOverrideState Validate()
    {
        if (!Enum.IsDefined(Kind) || AcquiredMinute < 0 || LastInnocentKilledMinute < AcquiredMinute
            || LastMorphMinute is long last && last < AcquiredMinute)
            throw new ArgumentException("Racial override has invalid kind or transition times.");
        return this;
    }
}

internal sealed record DaggerfallRacialOverrideView(string Source, DaggerfallRacialOverrideState State)
{
    internal bool SuppressCrime => State.BeastForm;
    internal bool SuppressTalk => State.BeastForm;
    internal bool SuppressInventory => State.BeastForm;
    internal bool SuppressPopulationSpawns => State.BeastForm;
    internal string Name => State.Kind == DaggerfallRacialKind.Werewolf ? "Werewolf" : "Wereboar";
}

/// <summary>
/// The single racial override is an ordinary permanent Kit/Engine effect. This owner supplies
/// Daggerfall selection, compound-race meaning and source removal; it stores no second race graph.
/// </summary>
internal sealed class DaggerfallRacialOverrides(DaggerfallEffectLifecycle effects, DaggerfallCharacterState character)
{
    internal const string EffectKey = "racial-override";
    private DaggerfallActiveEffect? Active => effects.Active.SingleOrDefault(effect => effect.Definition.Key == EffectKey);
    internal DaggerfallRacialOverrideView? Current => Active is { } effect ? new(effect.Context.Instance.Value, Read(effect.State)) : null;

    internal bool Select(DaggerfallRacialKind kind, string source, long minute, bool replace = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var state = new DaggerfallRacialOverrideState(kind, false, minute, minute).Validate();
        if (Active is { } incumbent)
        {
            if (incumbent.Context.Instance.Value == source) return Read(incumbent.State).Kind == kind;
            if (!replace) return false;
            effects.Cancel(incumbent.Context.Instance);
        }
        bool applied = effects.Start(new(source, EffectKey, EffectKey, DaggerfallActorIdentity.PlayerEntityId,
            DaggerfallActorIdentity.PlayerEntityId, EffectKey, null, null, 1, null, Encode(state)))
            == DaggerfallEffectAdmissionOutcome.Started;
        if (applied) character.GrantSpell("spell.085", source, DaggerfallSpellGrantKind.Lycanthropy);
        return applied;
    }

    internal bool Remove(string source) => Active is { } effect && effect.Context.Instance.Value == source
        && effects.Cure(effect.Context.Instance);

    internal void SetBeastForm(bool transformed, long minute)
    {
        var effect = Active ?? throw new InvalidOperationException("No racial override is active.");
        var state = Read(effect.State);
        effect.State = Encode((state with { BeastForm = transformed, LastMorphMinute = minute }).Validate());
    }

    internal void Satiate(long minute)
    {
        var effect = Active ?? throw new InvalidOperationException("No racial override is active.");
        effect.State = Encode((Read(effect.State) with { LastInnocentKilledMinute = minute }).Validate());
    }

    internal DaggerfallRaceDefinition ApplyToBirthRace(DaggerfallRaceDefinition birthRace) => Current is null ? birthRace
        : birthRace with
        {
            ImmunityFlags = birthRace.ImmunityFlags | (int)DaggerfallMagicEffectFlags.Disease,
            ResistanceFlags = birthRace.ResistanceFlags & ~(int)DaggerfallMagicEffectFlags.Disease,
            LowToleranceFlags = birthRace.LowToleranceFlags & ~(int)DaggerfallMagicEffectFlags.Disease,
            CriticalWeaknessFlags = birthRace.CriticalWeaknessFlags & ~(int)DaggerfallMagicEffectFlags.Disease,
        };

    internal static DaggerfallEffectDefinition Definition(DaggerfallCharacterState character, DaggerfallLycanthropyTuning? tuning = null) => new(EffectKey, EffectKey, DaggerfallEffectStacking.Reject, 1, 1,
        Apply: effect => Attach(effect, character, tuning ?? DaggerfallLycanthropyTuning.Classic), Resume: effect => Attach(effect, character, tuning ?? DaggerfallLycanthropyTuning.Classic), ShowSpellIcon: false);

    private static IEnumerable<IActiveEffectContribution> Attach(DaggerfallActiveEffect effect, DaggerfallCharacterState character, DaggerfallLycanthropyTuning tuning)
    {
        if (effect.Context.Target.Value != (ulong)DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentException("Racial override is only defined for the player.");
        _ = Read(effect.State);
        var stats = effect.Target.Get<StatsComponent>();
        var identity = new EffectSourceIdentity(effect.Target.Entity, effect.Context.Instance, 1, SourceDefinitionId.Parse(EffectKey));
        string[] attributes = ["strength", "agility", "endurance", "speed"];
        string[] skills = ["swimming", "running", "stealth", "critical-strike", "climbing", "hand-to-hand", "jumping"];
        foreach (string key in attributes.Concat(skills))
        {
            var id = StatId.Parse(key);
            var stat = stats.GetStat(id);
            int amount = attributes.Contains(key) ? tuning.AttributeBonus : tuning.SkillBonus;
            stat.SetSources(id, [.. stat.Sources.Where(source => source.Identity != identity),
                new StatSource(identity, SourceDefinitionId.Parse(EffectKey), 0,
                    [new(id, StackingGroupId.Parse($"daggerfall.lycanthropy.{key}"), MechanicsStackingPolicy.Sum, new StatContribution.Add(amount))])]);
        }
        DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(stats, character.Career);
        return [new DelegateActiveEffectContribution(() =>
        {
            foreach (string key in attributes.Concat(skills).Append("health-maximum")) stats.GetStat(StatId.Parse(key)).RemoveSource(identity);
            DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(stats, character.Career);
            character.RemoveSpellGrants(effect.Context.Instance.Value);
        })];
    }
    private static DaggerfallRacialOverrideState Read(JsonElement state) =>
        (state.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallRacialOverrideState)
            ?? throw new ArgumentException("Racial override state is missing.")).Validate();
    private static JsonElement Encode(DaggerfallRacialOverrideState state) =>
        JsonSerializer.SerializeToElement(state, DaggerfallSaveJsonContext.Default.DaggerfallRacialOverrideState);
}
