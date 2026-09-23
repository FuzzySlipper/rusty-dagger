using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallRestRecoveryModuleTests
{
    private static readonly TrackId Health = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private static readonly TrackId Stamina = TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value);
    private static readonly TrackId Magicka = TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value);

    [Fact]
    public void Timed_rest_uses_the_shared_ten_minute_interval_and_recovers_once_per_hour()
    {
        StatsComponent player = PlayerStats(health: 40, stamina: 400, magicka: 20);
        DaggerfallRestRecoveryModule module = new();
        List<long> intervals = [];
        int medicalUses = 0;

        DaggerfallRestResult result = module.Apply(
            player,
            new(DaggerfallRestMode.Timed, 2),
            new(true),
            endurance: 50,
            medical: 30,
            rapidHealing: false,
            noRegeneration: false,
            advanceTime: seconds =>
            {
                intervals.Add(seconds);
                return new(seconds, seconds);
            },
            recordMedicalRest: () => medicalUses++);

        Assert.True(result.Accepted);
        Assert.Equal(7_200, result.RequestedSeconds);
        Assert.Equal(7_200, result.ElapsedSeconds);
        Assert.Equal(2, result.RecoveryHours);
        Assert.Equal(18, result.HealthRecovered);
        Assert.Equal(200, result.FatigueRecovered);
        Assert.Equal(20, result.SpellPointsRecovered);
        Assert.Equal(DaggerfallRestInterruption.None, result.Interruption);
        Assert.Equal(Enumerable.Repeat(600L, 12), intervals);
        Assert.Equal(2, medicalUses);
        Assert.Equal(58, player.GetTrack(Health).Current);
        Assert.Equal(600, player.GetTrack(Stamina).Current);
        Assert.Equal(40, player.GetTrack(Magicka).Current);
    }

    [Fact]
    public void Later_recovery_hours_use_live_inputs_and_skills_advance_once_at_completion()
    {
        StatsComponent player = PlayerStats(health: 1, stamina: 400, magicka: 20);
        int medical = 0;
        int hours = 0;
        int elapsedChecks = 0;
        int advancementChecks = 0;
        int firstRate = DaggerfallRestPolicy.RecoveryRates(50, 0, 100, 800, 80, false, false).Health;
        int secondRate = DaggerfallRestPolicy.RecoveryRates(50, 100, 100, 800, 80, false, false).Health;

        DaggerfallRestResult result = new DaggerfallRestRecoveryModule().Apply(
            player, new(DaggerfallRestMode.Timed, 2), new(true),
            50, 0, false, false,
            advanceTime: seconds =>
            {
                elapsedChecks++;
                if (elapsedChecks == 7) medical = 100;
                return new(seconds, seconds);
            },
            recordMedicalRest: () => hours++,
            advanceSkills: () => advancementChecks++,
            currentRecoveryInputs: () => (50, medical, false, false));

        Assert.Equal(2, hours);
        Assert.Equal(1, advancementChecks);
        Assert.True(secondRate > firstRate);
        Assert.Equal(firstRate + secondRate, result.HealthRecovered);
    }

    [Fact]
    public void Encounter_stops_rest_after_applied_time_and_keeps_the_completed_hour()
    {
        StatsComponent player = PlayerStats(health: 40, stamina: 400, magicka: 20);
        DaggerfallRestRecoveryModule module = new();
        int calls = 0;
        int medicalUses = 0;

        DaggerfallRestResult result = module.Apply(
            player,
            new(DaggerfallRestMode.Timed, 2),
            new(true),
            50, 30, false, false,
            advanceTime: seconds =>
            {
                calls++;
                return calls == 7
                    ? new(seconds, seconds, DaggerfallRestInterruption.Encounter)
                    : new(seconds, seconds);
            },
            recordMedicalRest: () => medicalUses++);

        Assert.Equal(7, calls);
        Assert.Equal(4_200, result.ElapsedSeconds);
        Assert.Equal(1, result.RecoveryHours);
        Assert.Equal(9, result.HealthRecovered);
        Assert.Equal(DaggerfallRestInterruption.Encounter, result.Interruption);
        Assert.Equal(1, medicalUses);
    }

    [Fact]
    public void Encounter_at_the_hour_boundary_interrupts_before_that_hour_recovers()
    {
        StatsComponent player = PlayerStats(health: 40, stamina: 400, magicka: 20);
        int calls = 0;
        int medicalUses = 0;

        DaggerfallRestResult result = new DaggerfallRestRecoveryModule().Apply(
            player,
            new(DaggerfallRestMode.Timed, 2),
            new(true),
            50, 30, false, false,
            advanceTime: seconds =>
            {
                calls++;
                return calls == 6
                    ? new(seconds, seconds, DaggerfallRestInterruption.Encounter)
                    : new(seconds, seconds);
            },
            recordMedicalRest: () => medicalUses++);

        Assert.Equal(6, calls);
        Assert.Equal(3_600, result.ElapsedSeconds);
        Assert.Equal(0, result.RecoveryHours);
        Assert.Equal((0, 0, 0), (result.HealthRecovered, result.FatigueRecovered, result.SpellPointsRecovered));
        Assert.Equal(0, medicalUses);
    }

    [Fact]
    public void Lethal_elapsed_effect_interrupts_until_healed_without_reviving_the_player()
    {
        StatsComponent player = PlayerStats(health: 4, stamina: 400, magicka: 20);
        int calls = 0;
        int medicalUses = 0;

        DaggerfallRestResult result = new DaggerfallRestRecoveryModule().Apply(
            player, new(DaggerfallRestMode.UntilHealed), new(true),
            50, 30, false, false,
            advanceTime: seconds =>
            {
                calls++;
                player.GetTrack(Health).SetCurrent(0, clamp: true);
                return new(seconds, seconds, DaggerfallRestInterruption.Defeated);
            },
            recordMedicalRest: () => medicalUses++);

        Assert.Equal(1, calls);
        Assert.Equal(DaggerfallRestInterruption.Defeated, result.Interruption);
        Assert.Equal(0, result.RecoveryHours);
        Assert.Equal(0, player.GetTrack(Health).Current);
        Assert.Equal(0, medicalUses);
    }

    [Fact]
    public void Loiter_advances_time_without_vitals_or_medical_skill_use()
    {
        StatsComponent player = PlayerStats(health: 40, stamina: 400, magicka: 20);
        DaggerfallRestRecoveryModule module = new();
        int calls = 0;
        int medicalUses = 0;

        DaggerfallRestResult result = module.Apply(
            player,
            new(DaggerfallRestMode.Loiter, 2),
            new(true),
            50, 30, false, false,
            advanceTime: seconds =>
            {
                calls++;
                return new(seconds, seconds);
            },
            recordMedicalRest: () => medicalUses++);

        Assert.True(result.Accepted);
        Assert.Equal(12, calls);
        Assert.Equal(2, result.RecoveryHours);
        Assert.Equal((0, 0, 0), (result.HealthRecovered, result.FatigueRecovered, result.SpellPointsRecovered));
        Assert.Equal(0, medicalUses);
        Assert.Equal(40, player.GetTrack(Health).Current);
    }

    [Fact]
    public void Until_healed_stops_at_the_vital_caps_and_records_medical_once()
    {
        StatsComponent player = PlayerStats(health: 91, stamina: 700, magicka: 70);
        DaggerfallRestRecoveryModule module = new();
        int calls = 0;
        int medicalUses = 0;

        DaggerfallRestResult result = module.Apply(
            player,
            new(DaggerfallRestMode.UntilHealed),
            new(true),
            50, 30, false, false,
            advanceTime: seconds =>
            {
                calls++;
                return new(seconds, seconds);
            },
            recordMedicalRest: () => medicalUses++);

        Assert.Equal(6, calls);
        Assert.Equal(3_600, result.ElapsedSeconds);
        Assert.Equal(1, result.RecoveryHours);
        Assert.Equal((9, 100, 10), (result.HealthRecovered, result.FatigueRecovered, result.SpellPointsRecovered));
        Assert.Equal(1, medicalUses);
        Assert.True(DaggerfallRestPolicy.IsFullyRecovered(player, noRegeneration: false));
    }

    [Fact]
    public void Ineligible_rest_does_not_advance_time_or_record_skill_use()
    {
        StatsComponent player = PlayerStats(health: 40, stamina: 400, magicka: 20);
        int calls = 0;
        int medicalUses = 0;
        DaggerfallRestResult result = new DaggerfallRestRecoveryModule().Apply(
            player,
            new(DaggerfallRestMode.Timed, 1),
            new(false, Message: "You cannot rest here."),
            50, 30, false, false,
            advanceTime: seconds =>
            {
                calls++;
                return new(seconds, seconds);
            },
            recordMedicalRest: () => medicalUses++);

        Assert.False(result.Accepted);
        Assert.Equal("You cannot rest here.", result.Message);
        Assert.Equal(0, calls);
        Assert.Equal(0, medicalUses);
        Assert.Equal(40, player.GetTrack(Health).Current);
    }

    private static StatsComponent PlayerStats(int health, int stamina, int magicka)
    {
        Stat healthMaximum = new(100, 0, 100);
        Stat staminaMaximum = new(800, 0, 800);
        Stat magickaMaximum = new(80, 0, 80);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value), healthMaximum);
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value), staminaMaximum);
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value), magickaMaximum);
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value), new Stat(50, 0, 100));
        stats.AddTrack(Health, new Track(healthMaximum, health, 0, quantum: 1));
        stats.AddTrack(Stamina, new Track(staminaMaximum, stamina, 0, quantum: 1));
        stats.AddTrack(Magicka, new Track(magickaMaximum, magicka, 0, quantum: 1));
        return stats;
    }
}
