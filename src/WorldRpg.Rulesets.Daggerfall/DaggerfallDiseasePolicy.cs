using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The retained classic disease identities, each resolved through the compiled donor matrix below.</summary>
internal enum DaggerfallClassicDisease
{
    BloodRot,
    BrainFever,
    CalironsCurse,
    Cholera,
    Chrondiasis,
    Consumption,
    Dementia,
    Leprosy,
    Plague,
    RedDeath,
    StomachRot,
    SwampRot,
    TyphoidFever,
    WitchesPox,
    WizardFever,
    WoundRot,
    YellowFever,
}

/// <summary>The externally visible result of FORM-06 disease admission.</summary>
internal enum DaggerfallDiseaseAdmission
{
    Started,
    TargetIsNotPlayer,
    LevelOneImmune,
    Immune,
    Resisted,
}

/// <summary>The published career's one classic disease tolerance, resolved using the donor's raw-byte priority.</summary>
internal enum DaggerfallDiseaseCareerTolerance
{
    Normal,
    Resistant,
    Immune,
    LowTolerance,
    CriticalWeakness,
}

/// <summary>Stable source values for a disease exposure. The caller supplies the actual hit, item, or quest provenance.</summary>
internal sealed record DaggerfallDiseaseExposure(
    string Instance,
    string Source,
    long? CasterId,
    long TargetId,
    IReadOnlyList<DaggerfallClassicDisease> Candidates,
    string Settings = "classic",
    ulong? ItemId = null,
    int BiographyModifier = 0,
    int? ActiveResistanceChance = null)
{
    internal void ValidateSavingThrowInputs()
    {
        if (ActiveResistanceChance is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(ActiveResistanceChance), "An active disease-resistance chance must be within 0 through 100.");
    }
}

/// <summary>
/// Classic disease data and FORM-06 admission over the existing Daggerfall active-effect lifecycle.
/// The donor matrix is FALL.EXE 1.07.213 as recorded by DFU's DiseaseEffect; the policy keeps its
/// calendar cursor and accumulated attribute losses in effect state. Attribute losses are canonical
/// Engine stat sources owned by that effect; health loss uses the same accepted application path as attacks.
/// </summary>
internal sealed record DaggerfallEffectDamage(DamageResult Result);

internal static class DaggerfallDiseasePolicy
{
    private const string DiseaseElement = "disease";
    private const string RandomScope = "dagger.disease.v1";
    private const ulong RandomSeed = 0;

    private static readonly DaggerfallStatId[] Attributes =
    [
        DaggerfallMechanicsIds.Strength,
        DaggerfallMechanicsIds.Intelligence,
        DaggerfallMechanicsIds.Willpower,
        DaggerfallMechanicsIds.Agility,
        DaggerfallMechanicsIds.Endurance,
        DaggerfallMechanicsIds.Personality,
        DaggerfallMechanicsIds.Speed,
        DaggerfallMechanicsIds.Luck,
    ];

    private static readonly IReadOnlyDictionary<DaggerfallClassicDisease, DiseaseData> Data =
        new Dictionary<DaggerfallClassicDisease, DiseaseData>
        {
            // STR INT WIL AGI END PER SPD LUC HEA FAT SPL, minDamage, maxDamage, symptoms
            [DaggerfallClassicDisease.BloodRot] = new("disease-blood-rot", 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 5, 10, 3, 18),
            [DaggerfallClassicDisease.BrainFever] = new("disease-brain-fever", 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 5, null, null),
            [DaggerfallClassicDisease.CalironsCurse] = new("disease-calirons-curse", 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 5, 10, 3, 18),
            [DaggerfallClassicDisease.Cholera] = new("disease-cholera", 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 5, 30, null, null),
            [DaggerfallClassicDisease.Chrondiasis] = new("disease-chrondiasis", 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 5, 10, null, null),
            [DaggerfallClassicDisease.Consumption] = new("disease-consumption", 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 2, 10, null, null),
            [DaggerfallClassicDisease.Dementia] = new("disease-dementia", 0, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 2, 10, null, null),
            [DaggerfallClassicDisease.Leprosy] = new("disease-leprosy", 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 5, 30, null, null),
            [DaggerfallClassicDisease.Plague] = new("disease-plague", 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 3, 30, null, null),
            [DaggerfallClassicDisease.RedDeath] = new("disease-red-death", 0, 0, 0, 0, 1, 1, 0, 0, 0, 1, 0, 2, 10, null, null),
            [DaggerfallClassicDisease.StomachRot] = new("disease-stomach-rot", 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 5, null, null),
            [DaggerfallClassicDisease.SwampRot] = new("disease-swamp-rot", 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 2, 10, null, null),
            [DaggerfallClassicDisease.TyphoidFever] = new("disease-typhoid-fever", 0, 1, 0, 0, 1, 0, 0, 0, 1, 0, 0, 2, 10, null, null),
            [DaggerfallClassicDisease.WitchesPox] = new("disease-witches-pox", 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 2, 10, null, null),
            [DaggerfallClassicDisease.WizardFever] = new("disease-wizard-fever", 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 4, 3, 18),
            [DaggerfallClassicDisease.WoundRot] = new("disease-wound-rot", 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 2, 4, null, null),
            [DaggerfallClassicDisease.YellowFever] = new("disease-yellow-fever", 0, 0, 1, 0, 1, 0, 0, 0, 1, 0, 0, 5, 10, null, null),
        };

    /// <summary>Builds the compiled disease policy for this session's one admitted calendar and RNG service.</summary>
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(
        IRandomService random,
        Func<long> currentDay,
        Func<DaggerfallCareerDefinition> playerCareer,
        CombatResolution? combat = null,
        Action<DaggerfallEffectDamage>? damageApplied = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(currentDay);
        ArgumentNullException.ThrowIfNull(playerCareer);
        CombatResolution selectedCombat = combat ?? new CombatResolution();
        return Data.Values.Select(data => new DaggerfallEffectDefinition(
            data.Key,
            data.Key,
            DaggerfallEffectStacking.Stack,
            ushort.MaxValue,
            1,
            Apply: effect => [Removal(effect, playerCareer)],
            MagicRound: effect => AdvanceDisease(effect, data, random, currentDay, playerCareer, selectedCombat, damageApplied),
            Resume: effect =>
            {
                VerifyRestoredAttributeContributions(effect, ReadState(effect.State));
                return [Removal(effect, playerCareer)];
            }));
    }

    /// <summary>
    /// FORM-06: validates player-only classic infection, performs the existing disease resistance
    /// check, picks one donor candidate, and starts its compiled effect. A successful admission
    /// deliberately permits another instance of the same disease, matching classic rather than
    /// DFU's later same-disease suppression.
    /// </summary>
    internal static DaggerfallDiseaseAdmission InflictDisease(
        DaggerfallEffectLifecycle effects,
        ActorsState actors,
        IRandomService random,
        Func<long> currentDay,
        DaggerfallDiseaseExposure exposure)
        => InflictDisease(effects, actors, random, currentDay, exposure, null);

    /// <summary>Applies FORM-06 using the selected player's published career tolerance.</summary>
    internal static DaggerfallDiseaseAdmission InflictDisease(
        DaggerfallEffectLifecycle effects,
        ActorsState actors,
        IRandomService random,
        Func<long> currentDay,
        DaggerfallDiseaseExposure exposure,
        DaggerfallCareerDefinition? career)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(currentDay);
        ArgumentNullException.ThrowIfNull(exposure);
        exposure.ValidateSavingThrowInputs();
        if (exposure.TargetId != actors.Player.DurableId) return DaggerfallDiseaseAdmission.TargetIsNotPlayer;
        if (actors.Player.Progression.Level <= 1) return DaggerfallDiseaseAdmission.LevelOneImmune;

        StatsComponent stats = actors.Player.Stats;
        if (ReadStat(stats, DaggerfallMechanicsIds.ImmunityDisease) != 0) return DaggerfallDiseaseAdmission.Immune;
        if (exposure.ActiveResistanceChance is int activeResistance
            && Draw(random, $"active-resist:{exposure.Instance}", 1, 100) <= activeResistance)
            return DaggerfallDiseaseAdmission.Resisted;
        int chance = DiseaseSavingThrowChance(
            ReadStat(stats, DaggerfallMechanicsIds.Willpower),
            career is null ? DaggerfallDiseaseCareerTolerance.Normal : CareerTolerance(career),
            exposure.BiographyModifier);
        int resistanceRoll = Draw(random, $"resist:{exposure.Instance}", 1, 100);
        if (DiseaseSavingThrowAmount(chance, resistanceRoll) == 0) return DaggerfallDiseaseAdmission.Resisted;

        DaggerfallClassicDisease[] candidates = exposure.Candidates?.ToArray()
            ?? throw new ArgumentNullException(nameof(exposure), "Disease candidates cannot be null.");
        if (candidates.Length == 0 || candidates.Any(candidate => !Data.ContainsKey(candidate)))
            throw new ArgumentException("A disease exposure must name one or more diseases implemented by this catalog.", nameof(exposure));
        DaggerfallClassicDisease disease = candidates[Draw(random, $"select:{exposure.Instance}", 0, candidates.Length - 1)];
        DiseaseData data = Data[disease];
        int? symptoms = data.SymptomDaysMinimum is int minimum
            ? Draw(random, $"duration:{exposure.Instance}", minimum, data.SymptomDaysMaximum!.Value)
            : null;
        JsonElement state = State(new DiseaseState(currentDay(), false, symptoms, new Dictionary<string, int>(StringComparer.Ordinal)));
        DaggerfallEffectAdmissionOutcome started = effects.Start(new DaggerfallEffectRequest(
            exposure.Instance,
            data.Key,
            exposure.Source,
            exposure.CasterId,
            exposure.TargetId,
            exposure.Settings,
            DiseaseElement,
            exposure.ItemId,
            1,
            null,
            state));
        if (started != DaggerfallEffectAdmissionOutcome.Started)
            throw new InvalidOperationException($"Classic disease admission unexpectedly returned {started}.");
        return DaggerfallDiseaseAdmission.Started;
    }

    /// <summary>The current actor baseline of the donor saving throw, before its single 1–100 roll.</summary>
    internal static int DiseaseSavingThrowChance(int willpower, DaggerfallDiseaseCareerTolerance tolerance = DaggerfallDiseaseCareerTolerance.Normal, int biographyModifier = 0)
    {
        int chance = checked(50 + CareerToleranceModifier(tolerance) + biographyModifier);
        // The donor makes career immunity complete before adding magic resistance and applying the
        // ordinary 5–95 window.
        if (chance >= 100) return 100;
        return Math.Clamp(checked(chance + DaggerfallFormulaPolicy.MagicResist(willpower)), 5, 95);
    }

    /// <summary>
    /// The donor saving-throw result. Zero cancels an incoming disease; a near successful throw
    /// produces a reduced nonzero payload and therefore still admits a disease, as FORM-06 does.
    /// </summary>
    internal static int DiseaseSavingThrowAmount(int chance, int roll)
    {
        if (chance is < 5 or > 100) throw new ArgumentOutOfRangeException(nameof(chance));
        if (roll is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(roll));
        // FormulaHelper returns immediately for immunity instead of treating a natural 100 as a
        // near miss through the ordinary prorating branch.
        if (chance == 100) return 0;
        if (roll > chance) return 100;
        return Math.Clamp(checked(100 - (5 * (chance - roll))), 0, 100);
    }

    internal static DaggerfallDiseaseCareerTolerance CareerTolerance(DaggerfallCareerDefinition career) =>
        DaggerfallCareerTolerances.Tolerance(career, DaggerfallCareerTolerances.Disease);

    private static int CareerToleranceModifier(DaggerfallDiseaseCareerTolerance tolerance) => tolerance switch
    {
        DaggerfallDiseaseCareerTolerance.Normal => 0,
        DaggerfallDiseaseCareerTolerance.Immune => 50,
        DaggerfallDiseaseCareerTolerance.Resistant => 25,
        DaggerfallDiseaseCareerTolerance.LowTolerance => -25,
        DaggerfallDiseaseCareerTolerance.CriticalWeakness => -50,
        _ => throw new ArgumentOutOfRangeException(nameof(tolerance)),
    };

    /// <summary>Cures every live instance of one classic disease on the named target; direct vital loss remains while attribute sources are removed.</summary>
    internal static int CureDisease(DaggerfallEffectLifecycle effects, long targetId, DaggerfallClassicDisease disease)
    {
        ArgumentNullException.ThrowIfNull(effects);
        string key = Data[disease].Key;
        EffectInstanceId[] instances = effects.Active
            .Where(effect => effect.Context.Target.Value == checked((ulong)targetId) && effect.Definition.Key == key)
            .Select(effect => effect.Context.Instance)
            .ToArray();
        foreach (EffectInstanceId instance in instances) _ = effects.Cure(instance);
        return instances.Length;
    }

    /// <summary>Cures all currently compiled classic diseases on the named target.</summary>
    internal static int CureAllDiseases(DaggerfallEffectLifecycle effects, long targetId)
    {
        ArgumentNullException.ThrowIfNull(effects);
        HashSet<string> keys = Data.Values.Select(value => value.Key).ToHashSet(StringComparer.Ordinal);
        EffectInstanceId[] instances = effects.Active
            .Where(effect => effect.Context.Target.Value == checked((ulong)targetId) && keys.Contains(effect.Definition.Key))
            .Select(effect => effect.Context.Instance)
            .ToArray();
        foreach (EffectInstanceId instance in instances) _ = effects.Cure(instance);
        return instances.Length;
    }

    private static void AdvanceDisease(DaggerfallActiveEffect effect, DiseaseData data, IRandomService random, Func<long> currentDay,
        Func<DaggerfallCareerDefinition> playerCareer, CombatResolution combat, Action<DaggerfallEffectDamage>? damageApplied)
    {
        DiseaseState state = ReadState(effect.State);
        long today = currentDay();
        if (today < state.LastDay)
            throw new InvalidOperationException($"Disease '{data.Key}' cannot advance backward from day {state.LastDay} to {today}.");
        long daysPast = today - state.LastDay;
        if (daysPast == 0 || state.DaysOfSymptomsLeft == 0) return;

        for (long day = checked(state.LastDay + 1); day <= today; day++)
            state = ApplyDailyDamage(effect, data, state,
                playerCareer, combat, damageApplied,
                Draw(random, $"daily:{effect.Context.Instance.Value}:{day}", data.MinimumDamage, data.MaximumDamage));

        int? symptoms = state.DaysOfSymptomsLeft is int remaining
            ? daysPast >= remaining ? 0 : checked(remaining - (int)daysPast)
            : null;
        effect.State = State(state with { LastDay = today, IncubationOver = true, DaysOfSymptomsLeft = symptoms });
        // A finite disease ends through the same Engine-backed expiry and cleanup path on the
        // round that consumed its final symptoms. Permanent diseases retain no common timer.
        if (symptoms == 0) effect.ExpireAfterCurrentRound = true;
    }

    private static DiseaseState ApplyDailyDamage(DaggerfallActiveEffect effect, DiseaseData data, DiseaseState state,
        Func<DaggerfallCareerDefinition> playerCareer, CombatResolution combat, Action<DaggerfallEffectDamage>? damageApplied, int amount)
    {
        StatsComponent stats = effect.Target.Get<StatsComponent>();
        Dictionary<string, int> losses = new(state.AttributeLosses, StringComparer.Ordinal);
        AddAttributeLoss(losses, DaggerfallMechanicsIds.Strength, data.Strength, amount);
        AddAttributeLoss(losses, DaggerfallMechanicsIds.Intelligence, data.Intelligence, amount);
        AddAttributeLoss(losses, DaggerfallMechanicsIds.Willpower, data.Willpower, amount);
        AddAttributeLoss(losses, DaggerfallMechanicsIds.Agility, data.Agility, amount);
        AddAttributeLoss(losses, DaggerfallMechanicsIds.Endurance, data.Endurance, amount);
        AddAttributeLoss(losses, DaggerfallMechanicsIds.Personality, data.Personality, amount);
        AddAttributeLoss(losses, DaggerfallMechanicsIds.Speed, data.Speed, amount);
        AddAttributeLoss(losses, DaggerfallMechanicsIds.Luck, data.Luck, amount);
        ApplyAttributeContributions(effect, stats, losses, playerCareer);
        ApplyHealth(combat, effect, stats, data, amount, damageApplied);
        ApplyTrack(stats, DaggerfallMechanicsIds.Stamina, data.Fatigue, amount);
        ApplyTrack(stats, DaggerfallMechanicsIds.Magicka, data.SpellPoints, amount);
        return state with { AttributeLosses = losses };
    }

    private static void AddAttributeLoss(Dictionary<string, int> losses, DaggerfallStatId id, int multiplier, int amount)
    {
        if (multiplier == 0) return;
        losses[id.Value] = checked(losses.GetValueOrDefault(id.Value) + checked(multiplier * amount));
    }

    private static IActiveEffectContribution Removal(DaggerfallActiveEffect effect, Func<DaggerfallCareerDefinition> playerCareer) =>
        new DelegateActiveEffectContribution(() => RemoveAttributeContributions(effect, playerCareer));

    private static void ApplyAttributeContributions(DaggerfallActiveEffect effect, StatsComponent stats, IReadOnlyDictionary<string, int> losses,
        Func<DaggerfallCareerDefinition> playerCareer)
    {
        EffectSourceIdentity identity = AttributeSourceIdentity(effect);
        foreach (DaggerfallStatId attribute in Attributes)
        {
            Stat stat = stats.GetStat(StatId.Parse(attribute.Value));
            int loss = losses.GetValueOrDefault(attribute.Value);
            List<StatSource> sources = stat.Sources.Where(source => source.Identity != identity).ToList();
            if (loss != 0)
            {
                sources.Add(new StatSource(
                    identity,
                    AttributeSourceDefinition(effect, attribute),
                    priority: 0,
                    [new StatContributionDefinition(
                        StatId.Parse(attribute.Value),
                        StackingGroupId.Parse($"daggerfall.{effect.Definition.Key}.{attribute.Value}"),
                        MechanicsStackingPolicy.Sum,
                        new StatContribution.Add(-loss))]));
            }

            stat.SetSources(StatId.Parse(attribute.Value), sources);
        }

        RefreshPlayerDerivedMaxima(effect, stats, playerCareer());
    }

    private static void RemoveAttributeContributions(DaggerfallActiveEffect effect, Func<DaggerfallCareerDefinition> playerCareer)
    {
        StatsComponent stats = effect.Target.Get<StatsComponent>();
        EffectSourceIdentity identity = AttributeSourceIdentity(effect);
        foreach (DaggerfallStatId attribute in Attributes)
            _ = stats.GetStat(StatId.Parse(attribute.Value)).RemoveSource(identity);
        RefreshPlayerDerivedMaxima(effect, stats, playerCareer());
    }

    private static void VerifyRestoredAttributeContributions(DaggerfallActiveEffect effect, DiseaseState state)
    {
        StatsComponent stats = effect.Target.Get<StatsComponent>();
        EffectSourceIdentity identity = AttributeSourceIdentity(effect);
        foreach (DaggerfallStatId attribute in Attributes)
        {
            int loss = state.AttributeLosses.GetValueOrDefault(attribute.Value);
            StatSource[] sources = stats.GetStat(StatId.Parse(attribute.Value)).Sources
                .Where(source => source.Identity == identity)
                .ToArray();
            if (loss == 0 && sources.Length == 0) continue;
            if (loss == 0 || sources.Length != 1
                || sources[0].Definition != AttributeSourceDefinition(effect, attribute)
                || sources[0].Contributions.Count != 1
                || sources[0].Contributions[0].Contribution is not StatContribution.Add add
                || sources[0].Contributions[0].Stat != StatId.Parse(attribute.Value)
                || add.Amount != -loss)
            {
                throw new ArgumentException($"Restored disease '{effect.Context.Instance.Value}' has attribute contributions that do not match its durable disease state.");
            }
        }
    }

    private static EffectSourceIdentity AttributeSourceIdentity(DaggerfallActiveEffect effect) => new(
        effect.Target.Entity,
        effect.Context.Instance,
        1,
        SourceDefinitionId.Parse($"daggerfall.{effect.Definition.Key}.attributes"));

    private static SourceDefinitionId AttributeSourceDefinition(DaggerfallActiveEffect effect, DaggerfallStatId attribute) =>
        SourceDefinitionId.Parse($"daggerfall.{effect.Definition.Key}.{attribute.Value}");

    private static void RefreshPlayerDerivedMaxima(DaggerfallActiveEffect effect, StatsComponent stats, DaggerfallCareerDefinition career)
    {
        if (effect.Context.Target.Value == checked((ulong)DaggerfallActorIdentity.PlayerEntityId))
            DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(stats, career);
    }

    private static void ApplyHealth(CombatResolution combat, DaggerfallActiveEffect effect, StatsComponent stats, DiseaseData data, int amount,
        Action<DaggerfallEffectDamage>? damageApplied)
    {
        int damage = checked(data.Health * amount);
        if (damage == 0) return;
        Track health = stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        ApplyHitEvent applied = combat.ApplyToHealth(new CombatParticipants(effect.Source, effect.Target, $"effect:{data.Key}"), damage, 0, health);
        damageApplied?.Invoke(new DaggerfallEffectDamage(applied.Result));
    }

    private static void ApplyTrack(StatsComponent stats, DaggerfallTrackId id, int multiplier, int amount)
    {
        if (multiplier == 0) return;
        Track track = stats.GetTrack(TrackId.Parse(id.Value));
        track.SetCurrent(Math.Max(track.Minimum, checked(track.Current - checked((long)multiplier * amount))), clamp: true);
    }

    private static int ReadStat(StatsComponent stats, DaggerfallStatId id) =>
        stats.GetStat(StatId.Parse(id.Value)).ValueInt;

    private static int Draw(IRandomService random, string key, int minimum, int maximum) =>
        checked((int)random.DrawKeyed(new KeyedRngRequest(RandomSeed, RandomScope, key, minimum, maximum)).Value);

    private static JsonElement State(DiseaseState state)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("lastDay", state.LastDay);
            writer.WriteBoolean("incubationOver", state.IncubationOver);
            writer.WritePropertyName("daysOfSymptomsLeft");
            if (state.DaysOfSymptomsLeft is int symptoms) writer.WriteNumberValue(symptoms);
            else writer.WriteNullValue();
            writer.WritePropertyName("attributeLosses");
            writer.WriteStartObject();
            foreach ((string stat, int loss) in state.AttributeLosses.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                writer.WriteNumber(stat, loss);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        using JsonDocument document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    private static DiseaseState ReadState(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Object
            || !state.TryGetProperty("lastDay", out JsonElement lastDay)
            || !lastDay.TryGetInt64(out long day)
            || !state.TryGetProperty("incubationOver", out JsonElement incubation)
            || incubation.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !state.TryGetProperty("daysOfSymptomsLeft", out JsonElement symptoms)
            || !state.TryGetProperty("attributeLosses", out JsonElement losses)
            || losses.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Disease effect state is malformed.", nameof(state));
        }

        int? remaining = symptoms.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when symptoms.TryGetInt32(out int value) && value >= 0 => value,
            _ => throw new ArgumentException("Disease symptom state is malformed.", nameof(state)),
        };
        Dictionary<string, int> attributeLosses = new(StringComparer.Ordinal);
        foreach (JsonProperty property in losses.EnumerateObject())
        {
            if (!Attributes.Any(attribute => attribute.Value == property.Name)
                || !property.Value.TryGetInt32(out int value)
                || value <= 0
                || !attributeLosses.TryAdd(property.Name, value))
            {
                throw new ArgumentException("Disease attribute-loss state is malformed.", nameof(state));
            }
        }
        return new DiseaseState(day, incubation.GetBoolean(), remaining, attributeLosses);
    }

    private sealed record DiseaseData(
        string Key,
        int Strength,
        int Intelligence,
        int Willpower,
        int Agility,
        int Endurance,
        int Personality,
        int Speed,
        int Luck,
        int Health,
        int Fatigue,
        int SpellPoints,
        int MinimumDamage,
        int MaximumDamage,
        int? SymptomDaysMinimum,
        int? SymptomDaysMaximum);

    private sealed record DiseaseState(long LastDay, bool IncubationOver, int? DaysOfSymptomsLeft, IReadOnlyDictionary<string, int> AttributeLosses);
}
