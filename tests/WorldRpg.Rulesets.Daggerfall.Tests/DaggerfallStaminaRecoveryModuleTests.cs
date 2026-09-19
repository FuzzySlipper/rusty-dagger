using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallStaminaRecoveryModuleTests
{
    private static readonly TrackId Health = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private static readonly TrackId Stamina = TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value);

    [Fact]
    public void Admitted_player_swing_delays_recovery_and_health_gates_it()
    {
        StatsComponent player = PlayerStats();
        DaggerfallStaminaRecoveryModule recovery = new(new DaggerfallStaminaRecoveryTuning(5d, 2d));
        player.GetTrack(Stamina).Spend(10);
        recovery.React(new PlayerAttackStartedFact(7, 13));

        recovery.Update(player, 1.5d);
        Assert.Equal(80d, player.GetTrack(Stamina).Current);
        recovery.React(new AttackRejectedFact(AttackRejection.Cooldown));
        recovery.Update(player, .5d);
        Assert.Equal(80d, player.GetTrack(Stamina).Current);
        recovery.Update(player, .2d);
        Assert.Equal(81d, player.GetTrack(Stamina).Current);
        recovery.Update(player, .8d);
        Assert.Equal(85d, player.GetTrack(Stamina).Current);

        player.GetTrack(Stamina).SetCurrent(0);
        recovery.React(new PlayerAttackStartedFact(7, 14));
        recovery.Update(player, 2d);
        Assert.Equal(0d, player.GetTrack(Stamina).Current);
        recovery.React(new AttackRejectedFact(AttackRejection.Cooldown));
        recovery.Update(player, 1d);
        Assert.Equal(5d, player.GetTrack(Stamina).Current);

        player.GetTrack(Health).SetCurrent(0);
        recovery.Update(player, 20d);
        Assert.Equal(5d, player.GetTrack(Stamina).Current);
    }

    private static StatsComponent PlayerStats()
    {
        Stat healthMaximum = new(100, 0, 100);
        Stat staminaMaximum = new(100, 0, 100);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value), healthMaximum);
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value), staminaMaximum);
        stats.AddTrack(Health, new Track(healthMaximum, 100, 0, quantum: 1));
        stats.AddTrack(Stamina, new Track(staminaMaximum, 90, 0, quantum: 1));
        return stats;
    }
}
