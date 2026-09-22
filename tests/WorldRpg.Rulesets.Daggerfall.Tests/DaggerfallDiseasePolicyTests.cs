using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDiseasePolicyTests
{
    [Fact]
    public void Blood_rot_incubates_until_the_next_day_then_applies_its_classic_matrix()
    {
        using ActorsState actors = Actors(level: 2);
        long day = 100;
        IRandomService random = Random(100, 0, 18, 10);
        DaggerfallEffectLifecycle effects = new(actors, DaggerfallDiseasePolicy.CreateCatalog(random, () => day));

        Assert.Equal(DaggerfallDiseaseAdmission.Started, DaggerfallDiseasePolicy.InflictDisease(
            effects, actors, random, () => day, Exposure("blood", DaggerfallClassicDisease.BloodRot)));
        Assert.Equal(50, Attribute(actors, DaggerfallMechanicsIds.Willpower));
        Assert.Equal(100, Track(actors, DaggerfallMechanicsIds.Health));
        Assert.False(Assert.Single(effects.Active).State.GetProperty("incubationOver").GetBoolean());

        day++;
        effects.AdvanceOrdinaryRound();

        Assert.Equal(40, Attribute(actors, DaggerfallMechanicsIds.Willpower));
        Assert.Equal(40, Attribute(actors, DaggerfallMechanicsIds.Personality));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Willpower));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Personality));
        Assert.Equal(90, Track(actors, DaggerfallMechanicsIds.Health));
        DaggerfallActiveEffect active = Assert.Single(effects.Active);
        Assert.True(active.State.GetProperty("incubationOver").GetBoolean());
        Assert.Equal(17, active.State.GetProperty("daysOfSymptomsLeft").GetInt32());
    }

    [Fact]
    public void Overlapping_diseases_keep_independent_attribute_sources_and_curing_one_removes_only_its_loss()
    {
        using ActorsState actors = Actors(level: 2);
        long day = 30;
        IRandomService random = Random(100, 0, 3, 100, 0, 3, 5, 5);
        DaggerfallEffectLifecycle effects = new(actors, DaggerfallDiseasePolicy.CreateCatalog(random, () => day));

        Assert.Equal(DaggerfallDiseaseAdmission.Started, DaggerfallDiseasePolicy.InflictDisease(
            effects, actors, random, () => day, Exposure("caliron-one", DaggerfallClassicDisease.CalironsCurse)));
        Assert.Equal(DaggerfallDiseaseAdmission.Started, DaggerfallDiseasePolicy.InflictDisease(
            effects, actors, random, () => day, Exposure("caliron-two", DaggerfallClassicDisease.CalironsCurse)));
        Assert.Equal(2, effects.Active.Count);

        day++;
        effects.AdvanceOrdinaryRound();
        Assert.Equal(40, Attribute(actors, DaggerfallMechanicsIds.Strength));
        Assert.Equal(40, Attribute(actors, DaggerfallMechanicsIds.Agility));
        Assert.Equal(40, Attribute(actors, DaggerfallMechanicsIds.Speed));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Strength));

        Assert.True(effects.Cure(EffectInstanceId.Parse("caliron-one")));
        Assert.Single(effects.Active);
        Assert.Equal(45, Attribute(actors, DaggerfallMechanicsIds.Strength));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Strength));

        Assert.Equal(1, DaggerfallDiseasePolicy.CureDisease(effects, 1, DaggerfallClassicDisease.CalironsCurse));
        Assert.Empty(effects.Active);
        Assert.Equal(50, Attribute(actors, DaggerfallMechanicsIds.Strength));
    }

    [Fact]
    public void Finite_disease_expires_after_its_last_daily_symptom_through_the_effect_lifecycle()
    {
        using ActorsState actors = Actors(level: 2);
        long day = 10;
        IRandomService random = Random(100, 0, 3, 5, 5, 5);
        DaggerfallEffectLifecycle effects = new(actors, DaggerfallDiseasePolicy.CreateCatalog(random, () => day));
        _ = DaggerfallDiseasePolicy.InflictDisease(effects, actors, random, () => day, Exposure("finite", DaggerfallClassicDisease.CalironsCurse));

        for (int symptom = 0; symptom < 3; symptom++)
        {
            day++;
            effects.AdvanceOrdinaryRound();
        }

        Assert.Empty(effects.Active);
        Assert.Equal(50, Attribute(actors, DaggerfallMechanicsIds.Strength));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Strength));
    }

    [Fact]
    public void Permanent_cholera_applies_once_per_elapsed_calendar_day_and_damages_all_vitals()
    {
        using ActorsState actors = Actors(level: 2);
        long day = 9;
        IRandomService random = Random(100, 0, 5, 5);
        DaggerfallEffectLifecycle effects = new(actors, DaggerfallDiseasePolicy.CreateCatalog(random, () => day));
        _ = DaggerfallDiseasePolicy.InflictDisease(effects, actors, random, () => day, Exposure("cholera", DaggerfallClassicDisease.Cholera));

        day += 2;
        Assert.Equal(2_880u, effects.AdvanceElapsedRounds(2_880));

        Assert.Equal(40, Attribute(actors, DaggerfallMechanicsIds.Strength));
        Assert.Equal(40, Attribute(actors, DaggerfallMechanicsIds.Intelligence));
        Assert.Equal(40, Attribute(actors, DaggerfallMechanicsIds.Luck));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Strength));
        Assert.Equal(80, Track(actors, DaggerfallMechanicsIds.Health));
        Assert.Equal(75, Track(actors, DaggerfallMechanicsIds.Stamina));
        Assert.Equal(35, Track(actors, DaggerfallMechanicsIds.Magicka));
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            Assert.Single(effects.Active).State.GetProperty("daysOfSymptomsLeft").ValueKind);
    }

    [Fact]
    public void Formula_admission_rejects_level_one_immune_resisted_and_non_player_targets_before_effect_mutation()
    {
        Assert.Equal(55, DaggerfallDiseasePolicy.DiseaseSavingThrowChance(50));
        Assert.Equal(95, DaggerfallDiseasePolicy.DiseaseSavingThrowChance(500));
        Assert.Equal(5, DaggerfallDiseasePolicy.DiseaseSavingThrowChance(-500));
        Assert.Equal(100, DaggerfallDiseasePolicy.DiseaseSavingThrowChance(50, DaggerfallDiseaseCareerTolerance.Immune));
        Assert.Equal(80, DaggerfallDiseasePolicy.DiseaseSavingThrowChance(50, DaggerfallDiseaseCareerTolerance.Resistant));
        Assert.Equal(30, DaggerfallDiseasePolicy.DiseaseSavingThrowChance(50, DaggerfallDiseaseCareerTolerance.LowTolerance));
        Assert.Equal(5, DaggerfallDiseasePolicy.DiseaseSavingThrowChance(50, DaggerfallDiseaseCareerTolerance.CriticalWeakness));
        Assert.Equal(0, DaggerfallDiseasePolicy.DiseaseSavingThrowAmount(55, 35));
        Assert.Equal(5, DaggerfallDiseasePolicy.DiseaseSavingThrowAmount(55, 36));
        Assert.Equal(100, DaggerfallDiseasePolicy.DiseaseSavingThrowAmount(55, 55));
        Assert.Equal(100, DaggerfallDiseasePolicy.DiseaseSavingThrowAmount(55, 56));
        Assert.Equal(0, DaggerfallDiseasePolicy.DiseaseSavingThrowAmount(100, 100));
        long day = 1;
        IRandomService random = Random(100, 0);
        using (ActorsState levelOne = Actors(level: 1))
        {
            DaggerfallEffectLifecycle effects = new(levelOne, DaggerfallDiseasePolicy.CreateCatalog(random, () => day));
            Assert.Equal(DaggerfallDiseaseAdmission.LevelOneImmune, DaggerfallDiseasePolicy.InflictDisease(
                effects, levelOne, random, () => day, Exposure("level-one", DaggerfallClassicDisease.BrainFever)));
            Assert.Empty(effects.Active);
        }

        using (ActorsState immune = Actors(level: 2))
        {
            immune.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.ImmunityDisease.Value)).BaseValue = 1;
            DaggerfallEffectLifecycle effects = new(immune, DaggerfallDiseasePolicy.CreateCatalog(random, () => day));
            Assert.Equal(DaggerfallDiseaseAdmission.Immune, DaggerfallDiseasePolicy.InflictDisease(
                effects, immune, random, () => day, Exposure("immune", DaggerfallClassicDisease.BrainFever)));
            Assert.Empty(effects.Active);
        }

        using (ActorsState resisted = Actors(level: 2))
        {
            DaggerfallEffectLifecycle effects = new(resisted, DaggerfallDiseasePolicy.CreateCatalog(Random(1), () => day));
            Assert.Equal(DaggerfallDiseaseAdmission.Resisted, DaggerfallDiseasePolicy.InflictDisease(
                effects, resisted, Random(1), () => day, Exposure("resisted", DaggerfallClassicDisease.BrainFever)));
            Assert.Empty(effects.Active);
        }

        using (ActorsState nonPlayer = Actors(level: 2, secondTarget: true))
        {
            DaggerfallEffectLifecycle effects = new(nonPlayer, DaggerfallDiseasePolicy.CreateCatalog(random, () => day));
            Assert.Equal(DaggerfallDiseaseAdmission.TargetIsNotPlayer, DaggerfallDiseasePolicy.InflictDisease(
                effects, nonPlayer, random, () => day, Exposure("other", DaggerfallClassicDisease.BrainFever) with { TargetId = 2 }));
            Assert.Empty(effects.Active);
        }
    }

    [Fact]
    public void Career_disease_tolerance_uses_the_donor_raw_flag_priority()
    {
        Assert.Equal(DaggerfallDiseaseCareerTolerance.Resistant, DaggerfallDiseasePolicy.CareerTolerance(Career(resistance: 64, immunity: 64, low: 64, critical: 64)));
        Assert.Equal(DaggerfallDiseaseCareerTolerance.Immune, DaggerfallDiseasePolicy.CareerTolerance(Career(immunity: 64, low: 64, critical: 64)));
        Assert.Equal(DaggerfallDiseaseCareerTolerance.LowTolerance, DaggerfallDiseasePolicy.CareerTolerance(Career(low: 64, critical: 64)));
        Assert.Equal(DaggerfallDiseaseCareerTolerance.CriticalWeakness, DaggerfallDiseasePolicy.CareerTolerance(Career(critical: 64)));
        Assert.Equal(DaggerfallDiseaseCareerTolerance.Normal, DaggerfallDiseasePolicy.CareerTolerance(Career()));
    }

    [Fact]
    public void Saved_disease_cursor_and_persistent_damage_resume_after_fresh_actor_reconstruction()
    {
        DaggerfallActiveEffectSave[] active;
        DaggerfallStatsSave stats;
        long day = 55;
        using (ActorsState original = Actors(level: 2))
        {
            IRandomService random = Random(100, 0, 5);
            DaggerfallEffectLifecycle effects = new(original, DaggerfallDiseasePolicy.CreateCatalog(random, () => day));
            _ = DaggerfallDiseasePolicy.InflictDisease(effects, original, random, () => day, Exposure("brain", DaggerfallClassicDisease.BrainFever));
            day++;
            effects.AdvanceOrdinaryRound();
            Assert.Equal(45, Attribute(original, DaggerfallMechanicsIds.Willpower));
            Assert.Equal(50, BaseAttribute(original, DaggerfallMechanicsIds.Willpower));
            stats = DaggerfallStatsSaveBoundary.Capture(original.Player.Stats, original.Player.Actor.Entity);
            active = effects.Capture();
        }

        using ActorsState restored = Actors(level: 2);
        DaggerfallRestoredStats rebuilt = DaggerfallStatsSaveBoundary.Restore(stats, restored.Player.Actor.Entity);
        restored.Player.Actor.Replace(rebuilt.Component);
        IRandomService resumedRandom = Random(5);
        DaggerfallEffectLifecycle resumed = new(restored, DaggerfallDiseasePolicy.CreateCatalog(resumedRandom, () => day));
        resumed.Restore(active);

        Assert.Equal(45, Attribute(restored, DaggerfallMechanicsIds.Willpower));
        Assert.Equal(50, BaseAttribute(restored, DaggerfallMechanicsIds.Willpower));
        Assert.Single(restored.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Willpower.Value)).Sources);
        Assert.True(Assert.Single(resumed.Active).State.GetProperty("incubationOver").GetBoolean());
        day++;
        resumed.AdvanceOrdinaryRound();
        Assert.Equal(40, Attribute(restored, DaggerfallMechanicsIds.Willpower));
        Assert.Equal(1, DaggerfallDiseasePolicy.CureAllDiseases(resumed, 1));
        Assert.Equal(50, Attribute(restored, DaggerfallMechanicsIds.Willpower));
        Assert.Equal(50, BaseAttribute(restored, DaggerfallMechanicsIds.Willpower));
    }

    [Fact]
    public void Restore_rejects_malformed_disease_state_before_it_can_schedule_a_later_round()
    {
        using ActorsState actors = Actors(level: 2);
        long day = 1;
        IRandomService random = Random(100, 0);
        DaggerfallEffectLifecycle source = new(actors, DaggerfallDiseasePolicy.CreateCatalog(random, () => day));
        _ = DaggerfallDiseasePolicy.InflictDisease(source, actors, random, () => day, Exposure("malformed", DaggerfallClassicDisease.BrainFever));
        DaggerfallActiveEffectSave saved = Assert.Single(source.Capture()) with { State = Json("{}") };

        using ActorsState restoredActors = Actors(level: 2);
        DaggerfallEffectLifecycle restored = new(restoredActors, DaggerfallDiseasePolicy.CreateCatalog(Random(), () => day));
        Assert.Throws<ArgumentException>(() => restored.Restore([saved]));
        Assert.Empty(restored.Active);
    }

    [Theory]
    [InlineData("BloodRot", 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 5, 10, 3, 18)]
    [InlineData("BrainFever", 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 5, 0, 0)]
    [InlineData("CalironsCurse", 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 5, 10, 3, 18)]
    [InlineData("Cholera", 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 5, 30, 0, 0)]
    [InlineData("Chrondiasis", 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 5, 10, 0, 0)]
    [InlineData("Consumption", 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 2, 10, 0, 0)]
    public void Every_retained_disease_uses_its_donor_daily_targets(string diseaseName,
        int strength, int intelligence, int willpower, int agility, int endurance, int personality, int speed, int luck, int health, int fatigue, int magicka,
        int minimumDamage, int maximumDamage, int symptomsMinimum, int symptomsMaximum)
    {
        DaggerfallClassicDisease disease = Enum.Parse<DaggerfallClassicDisease>(diseaseName);
        AssertDailyMatrix(disease, minimumDamage, symptomsMinimum, strength, intelligence, willpower, agility, endurance, personality, speed, luck, health, fatigue, magicka);
        AssertDailyMatrix(disease, maximumDamage, symptomsMaximum, strength, intelligence, willpower, agility, endurance, personality, speed, luck, health, fatigue, magicka);
    }

    private static void AssertDailyMatrix(DaggerfallClassicDisease disease, int damage, int symptoms,
        int strength, int intelligence, int willpower, int agility, int endurance, int personality, int speed, int luck, int health, int fatigue, int magicka)
    {
        using ActorsState actors = Actors(level: 2);
        long day = 4;
        IRandomService random = symptoms == 0 ? Random(100, 0, damage) : Random(100, 0, symptoms, damage);
        DaggerfallEffectLifecycle effects = new(actors, DaggerfallDiseasePolicy.CreateCatalog(random, () => day));
        _ = DaggerfallDiseasePolicy.InflictDisease(effects, actors, random, () => day, Exposure($"matrix-{disease}-{damage}", disease));
        day++;
        effects.AdvanceOrdinaryRound();

        Assert.Equal(50 - (strength * damage), Attribute(actors, DaggerfallMechanicsIds.Strength));
        Assert.Equal(50 - (intelligence * damage), Attribute(actors, DaggerfallMechanicsIds.Intelligence));
        Assert.Equal(50 - (willpower * damage), Attribute(actors, DaggerfallMechanicsIds.Willpower));
        Assert.Equal(50 - (agility * damage), Attribute(actors, DaggerfallMechanicsIds.Agility));
        Assert.Equal(50 - (endurance * damage), Attribute(actors, DaggerfallMechanicsIds.Endurance));
        Assert.Equal(50 - (personality * damage), Attribute(actors, DaggerfallMechanicsIds.Personality));
        Assert.Equal(50 - (speed * damage), Attribute(actors, DaggerfallMechanicsIds.Speed));
        Assert.Equal(50 - (luck * damage), Attribute(actors, DaggerfallMechanicsIds.Luck));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Strength));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Intelligence));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Willpower));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Agility));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Endurance));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Personality));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Speed));
        Assert.Equal(50, BaseAttribute(actors, DaggerfallMechanicsIds.Luck));
        int healthMaximum = 25 + (((50 - (endurance * damage)) * 3) / 2);
        int staminaMaximum = 100 - ((strength + endurance) * damage);
        int magickaMaximum = 50 - (intelligence * damage);
        Assert.Equal(TrackAfterMaximumRefresh(100, healthMaximum, health * damage), Track(actors, DaggerfallMechanicsIds.Health));
        Assert.Equal(TrackAfterMaximumRefresh(100, staminaMaximum, fatigue * damage), Track(actors, DaggerfallMechanicsIds.Stamina));
        Assert.Equal(TrackAfterMaximumRefresh(100, magickaMaximum, magicka * damage), Track(actors, DaggerfallMechanicsIds.Magicka));
        if (symptoms == 0)
            Assert.Equal(System.Text.Json.JsonValueKind.Null, Assert.Single(effects.Active).State.GetProperty("daysOfSymptomsLeft").ValueKind);
        else
            Assert.Equal(symptoms - 1, Assert.Single(effects.Active).State.GetProperty("daysOfSymptomsLeft").GetInt32());
    }

    private static DaggerfallDiseaseExposure Exposure(string instance, DaggerfallClassicDisease disease) =>
        new(instance, "classic-exposure", null, 1, [disease]);

    private static DaggerfallCareerDefinition Career(int resistance = 0, int immunity = 0, int low = 0, int critical = 0) =>
        new("disease-career", "Disease career", [], [], [], [], [], 1, 1f, [], [], resistance, immunity, low, critical,
            new DaggerfallCatalogCitation("test", "test"));

    private static System.Text.Json.JsonElement Json(string value)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static int Attribute(ActorsState actors, DaggerfallStatId id) => actors.Player.Stats.GetStat(StatId.Parse(id.Value)).ValueInt;
    private static int BaseAttribute(ActorsState actors, DaggerfallStatId id) => checked((int)actors.Player.Stats.GetStat(StatId.Parse(id.Value)).BaseValue);
    private static int Track(ActorsState actors, DaggerfallTrackId id) => actors.Player.Stats.GetTrack(TrackId.Parse(id.Value)).ValueInt;
    private static int TrackAfterMaximumRefresh(int current, int maximum, int damage) => Math.Max(0, Math.Min(current, maximum) - damage);

    private static ActorsState Actors(int level, bool secondTarget = false)
    {
        ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), DaggerfallMechanicsIds.Health.Value);
        if (level > 1) actors.Player.Progression.AdvanceTo(500, level);
        if (secondTarget)
            actors.CreateActor(2, new EntityTypeId("target"), Stats(), new ActorPose(new WorldPoint(1, 0, 0), 0), DaggerfallMechanicsIds.Health.Value);
        return actors;
    }

    private static StatsComponent Stats()
    {
        StatsComponent stats = new();
        foreach (DaggerfallStatId id in new[]
        {
            DaggerfallMechanicsIds.Strength, DaggerfallMechanicsIds.Intelligence, DaggerfallMechanicsIds.Willpower,
            DaggerfallMechanicsIds.Agility, DaggerfallMechanicsIds.Endurance, DaggerfallMechanicsIds.Personality,
            DaggerfallMechanicsIds.Speed, DaggerfallMechanicsIds.Luck,
        })
        {
            stats.AddStat(StatId.Parse(id.Value), new Stat(50, 0, 10_000, quantum: 1, rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero));
        }
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.ResistanceDiseaseOrPoison.Value), new Stat(0, -10_000, 10_000, quantum: 1, rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero));
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.ImmunityDisease.Value), new Stat(0, 0, 1, quantum: 1, rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero));
        AddTrack(stats, DaggerfallMechanicsIds.HealthMaximum, DaggerfallMechanicsIds.Health);
        AddTrack(stats, DaggerfallMechanicsIds.StaminaMaximum, DaggerfallMechanicsIds.Stamina);
        AddTrack(stats, DaggerfallMechanicsIds.MagickaMaximum, DaggerfallMechanicsIds.Magicka);
        return stats;
    }

    private static void AddTrack(StatsComponent stats, DaggerfallStatId maximum, DaggerfallTrackId track)
    {
        Stat value = new(100, 0, 10_000, quantum: 1, rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero);
        stats.AddStat(StatId.Parse(maximum.Value), value);
        stats.AddTrack(TrackId.Parse(track.Value), new Track(value, 100, 0, TrackMaximumChangePolicy.PreserveCurrent, 1, MidpointRounding.ToZero, MidpointRounding.ToZero));
    }

    private static IRandomService Random(params int[] values)
    {
        IRandomService service = DispatchProxy.Create<IRandomService, ScriptedRandom>();
        ScriptedRandom proxy = (ScriptedRandom)(object)service;
        proxy.Values.AddRange(values);
        return service;
    }

    private class ScriptedRandom : DispatchProxy
    {
        internal List<int> Values { get; } = [];
        private int next;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            if (next >= Values.Count) throw new InvalidOperationException($"No scripted random value for {request.Key}.");
            int value = Values[next++];
            if (value < request.Minimum || value > request.Maximum)
                throw new InvalidOperationException($"Scripted random value {value} is outside [{request.Minimum}, {request.Maximum}] for {request.Key}.");
            return new KeyedRngReceipt(value);
        }
    }
}
