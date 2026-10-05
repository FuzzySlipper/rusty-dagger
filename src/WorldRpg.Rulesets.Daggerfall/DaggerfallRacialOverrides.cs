using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallRacialKind { Werewolf, Wereboar, Vampire }

/// <summary>Meaningful permanent curse values inside the existing source-owned effect save.</summary>
internal sealed record DaggerfallRacialOverrideState(
    [property: JsonRequired] DaggerfallRacialKind Kind,
    [property: JsonRequired] bool BeastForm,
    [property: JsonRequired] long AcquiredMinute,
    [property: JsonRequired] long LastInnocentKilledMinute,
    long? LastMorphMinute = null,
    DaggerfallVampireState? Vampire = null)
{
    internal DaggerfallRacialOverrideState Validate()
    {
        if (!Enum.IsDefined(Kind) || AcquiredMinute < 0 || LastInnocentKilledMinute < AcquiredMinute
            || LastMorphMinute is long last && last < AcquiredMinute)
            throw new ArgumentException("Racial override has invalid kind or transition times.");
        if ((Kind == DaggerfallRacialKind.Vampire) != (Vampire is not null)
            || Vampire is { } vampire && (BeastForm || LastMorphMinute is not null || vampire.Clan is < 150 or > 158 || vampire.LastFedMinute < AcquiredMinute))
            throw new ArgumentException("Racial override has incompatible vampire state.");
        return this;
    }
}

internal sealed record DaggerfallVampireState([property: JsonRequired] int Clan, [property: JsonRequired] long LastFedMinute,
    [property: JsonRequired] bool InitialQuestStarted = false);

internal sealed record DaggerfallRacialOverrideView(string Source, DaggerfallRacialOverrideState State)
{
    internal bool SuppressCrime => State.BeastForm;
    internal bool SuppressTalk => State.BeastForm;
    internal bool SuppressInventory => State.BeastForm;
    internal bool SuppressPopulationSpawns => State.BeastForm;
    internal bool IsVampire => State.Kind == DaggerfallRacialKind.Vampire;
    internal bool RequiresSilver => State.BeastForm || IsVampire;
    internal string Name => State.Kind switch { DaggerfallRacialKind.Werewolf => "Werewolf", DaggerfallRacialKind.Wereboar => "Wereboar", _ => "Vampire" };
}

/// <summary>
/// The single racial override is an ordinary permanent Kit/Engine effect. This owner supplies
/// Daggerfall selection, compound-race meaning and source removal; it stores no second race graph.
/// </summary>
internal sealed class DaggerfallRacialOverrides(DaggerfallEffectLifecycle effects, DaggerfallCharacterState character, DaggerfallDefinitions definitions, Action<DaggerfallRacialOverrideView>? onRemoved = null)
{
    internal const string EffectKey = "racial-override";
    private DaggerfallActiveEffect? Active => effects.Active.SingleOrDefault(effect => effect.Definition.Key == EffectKey);
    internal DaggerfallRacialOverrideView? Current => Active is { } effect ? new(effect.Context.Instance.Value, Read(effect.State)) : null;

    internal bool Select(DaggerfallRacialKind kind, string source, long minute, bool replace = false, int? vampireClan = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var state = new DaggerfallRacialOverrideState(kind, false, minute, minute,
            Vampire: vampireClan is int clan ? new(clan, minute) : null).Validate();
        string[] spells = state.Vampire is { } vampire ? DaggerfallVampirismPolicy.GrantedSpells(definitions.Magic, vampire.Clan) : ["spell.085"];
        if (Active is { } incumbent)
        {
            if (incumbent.Context.Instance.Value == source) return Read(incumbent.State).Kind == kind && Read(incumbent.State).Vampire?.Clan == vampireClan;
            if (!replace) return false;
            var previous = new DaggerfallRacialOverrideView(incumbent.Context.Instance.Value, Read(incumbent.State));
            effects.Cancel(incumbent.Context.Instance);
            onRemoved?.Invoke(previous);
        }
        bool applied = effects.Start(new(source, EffectKey, EffectKey, DaggerfallActorIdentity.PlayerEntityId,
            DaggerfallActorIdentity.PlayerEntityId, EffectKey, null, null, 1, null, Encode(state)))
            == DaggerfallEffectAdmissionOutcome.Started;
        if (applied)
            foreach (string spell in spells) character.GrantSpell(spell, source,
                kind == DaggerfallRacialKind.Vampire ? DaggerfallSpellGrantKind.Vampirism : DaggerfallSpellGrantKind.Lycanthropy);
        return applied;
    }

    internal bool Remove(string source)
    {
        if (Active is not { } effect || effect.Context.Instance.Value != source) return false;
        var previous = new DaggerfallRacialOverrideView(source, Read(effect.State));
        if (!effects.Cure(effect.Context.Instance)) return false;
        onRemoved?.Invoke(previous);
        return true;
    }

    internal void SetBeastForm(bool transformed, long minute)
    {
        var effect = Active ?? throw new InvalidOperationException("No racial override is active.");
        var state = Read(effect.State);
        if (state.Kind == DaggerfallRacialKind.Vampire) throw new InvalidOperationException("A vampire has no beast form.");
        effect.State = Encode((state with { BeastForm = transformed, LastMorphMinute = minute }).Validate());
    }

    internal void Satiate(long minute)
    {
        var effect = Active ?? throw new InvalidOperationException("No racial override is active.");
        effect.State = Encode((Read(effect.State) with { LastInnocentKilledMinute = minute }).Validate());
    }

    internal void Feed(long minute)
    {
        var effect = Active ?? throw new InvalidOperationException("No racial override is active.");
        var state = Read(effect.State);
        if (state.Vampire is not { } vampire) throw new InvalidOperationException("Only a vampire feeds on a weapon hit.");
        effect.State = Encode((state with { Vampire = vampire with { LastFedMinute = minute } }).Validate());
    }

    internal void MarkInitialVampireQuestStarted()
    {
        var effect = Active ?? throw new InvalidOperationException("No racial override is active.");
        var state = Read(effect.State);
        var vampire = state.Vampire ?? throw new InvalidOperationException("Only a vampire has clan quests.");
        effect.State = Encode(state with { Vampire = vampire with { InitialQuestStarted = true } });
    }

    private int Immunities => (int)(DaggerfallMagicEffectFlags.Disease | (Current?.IsVampire == true ? DaggerfallMagicEffectFlags.Paralysis : 0));
    internal DaggerfallRaceDefinition ApplyToBirthRace(DaggerfallRaceDefinition birthRace) => Current is null ? birthRace
        : birthRace with
        {
            ImmunityFlags = birthRace.ImmunityFlags | Immunities,
            ResistanceFlags = birthRace.ResistanceFlags & ~Immunities,
            LowToleranceFlags = birthRace.LowToleranceFlags & ~Immunities,
            CriticalWeaknessFlags = birthRace.CriticalWeaknessFlags & ~Immunities,
        };

    internal static DaggerfallEffectDefinition Definition(DaggerfallCharacterState character, DaggerfallLycanthropyTuning? tuning = null, DaggerfallVampirismTuning? vampirism = null) => new(EffectKey, EffectKey, DaggerfallEffectStacking.Reject, 1, 1,
        Apply: effect => Attach(effect, character, tuning ?? DaggerfallLycanthropyTuning.Classic, vampirism ?? DaggerfallVampirismTuning.Classic), Resume: effect => Attach(effect, character, tuning ?? DaggerfallLycanthropyTuning.Classic, vampirism ?? DaggerfallVampirismTuning.Classic), ShowSpellIcon: false);

    private static IEnumerable<IActiveEffectContribution> Attach(DaggerfallActiveEffect effect, DaggerfallCharacterState character, DaggerfallLycanthropyTuning tuning, DaggerfallVampirismTuning vampirism)
    {
        if (effect.Context.Target.Value != (ulong)DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentException("Racial override is only defined for the player.");
        var state = Read(effect.State);
        var stats = effect.Target.Get<StatsComponent>();
        var identity = new EffectSourceIdentity(effect.Target.Entity, effect.Context.Instance, 1, SourceDefinitionId.Parse(EffectKey));
        string[] attributes = state.Vampire is { } vampire
            ? [.. new[] { "strength", "intelligence", "willpower", "agility", "endurance", "personality", "speed", "luck" }.Where(key => key != "intelligence" || vampire.Clan == 157)]
            : ["strength", "agility", "endurance", "speed"];
        string[] skills = state.Vampire is not null
            ? ["running", "stealth", "critical-strike", "climbing", "hand-to-hand", "jumping"]
            : ["swimming", "running", "stealth", "critical-strike", "climbing", "hand-to-hand", "jumping"];
        foreach (string key in attributes.Concat(skills))
        {
            var id = StatId.Parse(key);
            var stat = stats.GetStat(id);
            int amount = state.Vampire is not null
                ? attributes.Contains(key) ? vampirism.AttributeBonus : vampirism.SkillBonus
                : attributes.Contains(key) ? tuning.AttributeBonus : tuning.SkillBonus;
            stat.SetSources(id, [.. stat.Sources.Where(source => source.Identity != identity),
                new StatSource(identity, SourceDefinitionId.Parse(EffectKey), 0,
                    [new(id, StackingGroupId.Parse($"daggerfall.racial-override.{key}"), MechanicsStackingPolicy.Sum, new StatContribution.Add(amount))])]);
        }
        DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(stats, character.Career);
        return [new DelegateActiveEffectContribution(() =>
        {
            foreach (string key in attributes.Concat(skills).Append("health-maximum")) stats.GetStat(StatId.Parse(key)).RemoveSource(identity);
            DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(stats, character.Career);
            character.RemoveSpellGrants(effect.Context.Instance.Value);
        })];
    }
    internal static DaggerfallRacialOverrideState Read(JsonElement state) =>
        (state.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallRacialOverrideState)
            ?? throw new ArgumentException("Racial override state is missing.")).Validate();
    private static JsonElement Encode(DaggerfallRacialOverrideState state) =>
        JsonSerializer.SerializeToElement(state, DaggerfallSaveJsonContext.Default.DaggerfallRacialOverrideState);
}
