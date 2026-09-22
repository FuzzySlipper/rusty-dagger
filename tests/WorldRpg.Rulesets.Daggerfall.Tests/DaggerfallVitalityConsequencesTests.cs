using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallVitalityConsequencesTests
{
    [Fact]
    public void Landing_uses_canonical_health_loss_and_only_the_first_landing_can_defeat()
    {
        using ActorsState actors = new();
        StatsComponent stats = Stats(health: 3.75d, stamina: 100d);
        PlayerActorState player = actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health");
        DaggerfallVitalityConsequences consequences = new(new CombatResolution());

        DamageResult? lethal = consequences.ResolveLanding(player.Actor, new DaggerfallLanding(7f), preventsFallDamage: false);
        DamageResult? repeated = consequences.ResolveLanding(player.Actor, new DaggerfallLanding(7f), preventsFallDamage: false);

        Assert.Equal((10, 3.75d, true), (lethal!.Value.CalculatedDamage, lethal.Value.ActualHealthLost, lethal.Value.Defeated));
        Assert.Equal((0d, false), (repeated!.Value.ActualHealthLost, repeated.Value.Defeated));
        Assert.Equal(0d, stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current);
    }

    [Fact]
    public void Grace_distance_has_no_health_application()
    {
        using ActorsState actors = new();
        StatsComponent stats = Stats(health: 20d, stamina: 100d);
        PlayerActorState player = actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health");

        DamageResult? result = new DaggerfallVitalityConsequences(new CombatResolution())
            .ResolveLanding(player.Actor, new DaggerfallLanding(5f), preventsFallDamage: false);

        Assert.Null(result);
        Assert.Equal(20d, stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current);
    }

    [Fact]
    public void Compiled_movement_protection_rejects_an_otherwise_damaging_landing()
    {
        using ActorsState actors = new();
        StatsComponent stats = Stats(health: 20d, stamina: 100d);
        PlayerActorState player = actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health");

        DamageResult? result = new DaggerfallVitalityConsequences(new CombatResolution())
            .ResolveLanding(player.Actor, new DaggerfallLanding(7f), preventsFallDamage: true);

        Assert.Null(result);
        Assert.Equal(20d, stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current);
    }

    private static StatsComponent Stats(double health, double stamina)
    {
        Stat healthMaximum = new(100d);
        Stat staminaMaximum = new(100d);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value), healthMaximum);
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value), staminaMaximum);
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value), new Track(healthMaximum, health, 0d));
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value), new Track(staminaMaximum, stamina, 0d));
        return stats;
    }
}
