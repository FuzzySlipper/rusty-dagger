using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed partial class NormalizedRuntimeSeamTests
{
    [Fact]
    public void Playtest_attack_reports_resource_refusal_without_spending_or_advancing()
    {
        using ConditionSessionFixture fixture = new();
        var session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);
        var stamina = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina"));
        stamina.SetCurrent(0);
        var action = session.InspectPlaytestAction("attack");
        Assert.False(action.Available);
        Assert.Equal("InsufficientStamina", action.Reason);
        Assert.Equal(0, stamina.Current);
        Assert.Null(session.State.Actors.Player.Attack.Pending);
        session.ApplyProductMode(ProductMode.Modal);
        Assert.Contains("mode-Modal", session.InspectPlaytestAction("forward").Reason);
    }

    [Fact]
    public void Playtest_actions_follow_saved_binding_and_live_attack_timing_without_spending_resources()
    {
        var (created, _, perception) = VisibleEnemySession([]);
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1, 1, PerceptionPairKind.Visible, 1));
        using var session = created;
        var stamina = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina"));
        double before = stamina.Current;
        Assert.Equal("Primary", session.InspectPlaytestAction("attack").Key);
        DaggerfallControlSettings controls = new();
        controls.Rebind("attack", ["KeyQ"]);
        session.ApplyPlayerPreferences(controls.Serialize());
        Assert.Equal("KeyQ", session.InspectPlaytestAction("attack").Key);
        var speed = session.State.Actors.Player.Stats.GetStat(StatId.Parse("speed"));
        speed.BaseValue = 20;
        double slow = session.InspectPlaytestAction("attack").DurationMs;
        speed.BaseValue = 90;
        double fast = session.InspectPlaytestAction("attack").DurationMs;
        Assert.True(fast < slow, $"Expected live speed to shorten observation window: slow={slow}, fast={fast}");
        Assert.InRange(fast, 1, 2000);
        Assert.Equal(before, stamina.Current);
        Assert.Null(session.State.Actors.Player.Attack.Pending);
    }

    [Fact]
    public void Playtest_look_and_observation_keep_simulation_and_target_selection_unchanged()
    {
        using ConditionSessionFixture fixture = new();
        var session = fixture.Session;
        var position = session.State.PlayerControl.Position;
        var target = session.State.Actors.Player.Targeting.Current;
        var evidence = session.LastMeleeTargeting;
        var interaction = session.LastActivationTargeting;
        using var before = JsonDocument.Parse(session.ReadPlaytestObservation().Message);
        double yaw = session.State.PlayerControl.YawRadians;
        Assert.Equal(DebugCommandStatus.Success, session.InspectPlaytestLook(45, 10).Status);
        using var after = JsonDocument.Parse(session.ReadPlaytestObservation().Message);
        Assert.Equal(before.RootElement.GetProperty("simulationStep").GetString(), after.RootElement.GetProperty("simulationStep").GetString());
        Assert.Equal(position, session.State.PlayerControl.Position);
        Assert.NotEqual(yaw, session.State.PlayerControl.YawRadians);
        Assert.Equal(target, session.State.Actors.Player.Targeting.Current);
        Assert.Same(evidence, session.LastMeleeTargeting);
        Assert.Same(interaction, session.LastActivationTargeting);
        Assert.Equal(DebugCommandStatus.Success, session.ReadPlaytestTargets().Status);
    }
}
