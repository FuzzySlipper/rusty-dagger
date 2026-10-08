using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Interaction;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Kit;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class PlaytestInspectionTests
{
    [Fact]
    public void Enemy_inspection_distinguishes_pursuit_from_player_weapon_query_without_advancing()
    {
        var (created, _, perception) = VisibleEnemySession([], distance: 5);
        using var session = created;
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        var behavior = session.LastEnemyBehavior[2000];
        perception.Receipt = Receipt(); // The separate player query has no pair outside weapon reach.
        using var readout = JsonDocument.Parse(session.ReadPlaytestTargets().Message);
        var actor = readout.RootElement.GetProperty("actors").EnumerateArray().Single(a => a.GetProperty("id").GetString() == "actor:2000");
        Assert.Equal("unavailable", actor.GetProperty("currentAttackVisibility").GetString());
        var enemy = actor.GetProperty("enemyBehavior");
        Assert.Equal("Chase", enemy.GetProperty("state").GetString());
        Assert.True(enemy.GetProperty("detected").GetBoolean());
        Assert.Equal("NoPath", enemy.GetProperty("navigation").GetString());
        Assert.Same(behavior, session.LastEnemyBehavior[2000]);
    }

    [Fact]
    public void Standard_world_inspection_uses_live_owners_without_activation_or_resource_changes()
    {
        using ConditionSessionFixture fixture = new();
        var session = fixture.Session;
        var position = session.State.PlayerControl.Position;
        var lastActivation = session.LastActivationTargeting;
        var stamina = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina"));
        double before = stamina.Current;
        var module = new InteractionDebugModule(new WorldInteraction(new CurrentInteractionInspectionScene(session.CreateInteractionInspection), targetedUseEnabled: false));
        using var readout = JsonDocument.Parse(module.Inspect().Message);
        Assert.Equal("observation-only", readout.RootElement.GetProperty("assistance").GetString());
        Assert.False(readout.RootElement.GetProperty("targetedUseEnabled").GetBoolean());
        using var probe = JsonDocument.Parse(session.ReadSpatialProbe(1).Message);
        Assert.Equal(32, probe.RootElement.GetProperty("samples").GetArrayLength());
        Assert.Equal(DebugCommandStatus.InvalidArguments, session.ReadSpatialProbe(0).Status);
        Assert.Equal(DebugCommandStatus.InvalidArguments, session.ReadSpatialGrid(16, 0, 1).Status);
        Assert.Equal(DebugCommandStatus.InvalidArguments, session.ReadJumpPlan(double.NaN, 0, 0).Status);
        Assert.Equal(position, session.State.PlayerControl.Position);
        Assert.Equal(before, stamina.Current);
        Assert.Same(lastActivation, session.LastActivationTargeting);
    }

    [Fact]
    public void Playtest_attack_stays_available_at_empty_fatigue_without_spending_or_advancing()
    {
        using ConditionSessionFixture fixture = new();
        var session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);
        var stamina = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina"));
        stamina.SetCurrent(0);
        // The donor never refuses a swing for fatigue; an empty pool is the exhaustion collapse's.
        var action = session.InspectPlaytestAction("attack");
        Assert.True(action.Available);
        Assert.Null(action.Reason);
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
