using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Interaction;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
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
        var behavior = session.EnemyBehavior.LastEvidence[HoldActor(1)];
        perception.Receipt = Receipt(); // The separate player query has no pair outside weapon reach.
        using var readout = JsonDocument.Parse(session.Commands<PlaytestTargetsDebugModule>().Targets().Message);
        var actor = readout.RootElement.GetProperty("actors").EnumerateArray().Single(a => a.GetProperty("id").GetString() == $"actor:{HoldActor(1)}");
        Assert.Equal("unavailable", actor.GetProperty("currentAttackVisibility").GetString());
        var enemy = actor.GetProperty("enemyBehavior");
        Assert.Equal("Chase", enemy.GetProperty("state").GetString());
        Assert.True(enemy.GetProperty("detected").GetBoolean());
        Assert.Equal("NoPath", enemy.GetProperty("navigation").GetString());
        Assert.Same(behavior, session.EnemyBehavior.LastEvidence[HoldActor(1)]);
    }

    [Fact]
    public void Standard_world_inspection_uses_live_owners_without_activation_or_resource_changes()
    {
        using ConditionSessionFixture fixture = new();
        var session = fixture.Session;
        var position = session.State.PlayerControl.Position;
        var lastActivation = session.Activation.LastEvidence;
        var fatigue = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("fatigue"));
        double before = fatigue.Current;
        var module = session.Commands<InteractionDebugModule>();
        using var readout = JsonDocument.Parse(module.Inspect().Message);
        Assert.Equal("observation-only", readout.RootElement.GetProperty("assistance").GetString());
        Assert.False(readout.RootElement.GetProperty("targetedUseEnabled").GetBoolean());
        using var probe = JsonDocument.Parse(session.Commands<SpatialInspectionDebugModule>().Probe(1).Message);
        Assert.Equal(32, probe.RootElement.GetProperty("samples").GetArrayLength());
        Assert.Equal(DebugCommandStatus.InvalidArguments, session.Commands<SpatialInspectionDebugModule>().Probe(0).Status);
        Assert.Equal(DebugCommandStatus.InvalidArguments, session.Commands<SpatialInspectionDebugModule>().Grid(16, 0, 1).Status);
        Assert.Equal(DebugCommandStatus.InvalidArguments, session.Commands<SpatialInspectionDebugModule>().Jump(double.NaN, 0, 0).Status);
        Assert.Equal(position, session.State.PlayerControl.Position);
        Assert.Equal(before, fatigue.Current);
        Assert.Same(lastActivation, session.Activation.LastEvidence);
    }

    [Fact]
    public void A_streamed_exterior_floor_is_read_only_where_its_collision_is_loaded_and_a_route_is_the_engine_step()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile charing = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        List<string> releases = [];
        ContentFake content = new(releases); PopulateContent(content, charing);
        SpatialFake spatial = SpatialFake.Create(charing.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, charing, DaggerfallTuning.Defaults));
        Assert.True(session.AwaitArrivalGround().Done);
        spatial.FloorHit = request => new(true, SpatialHitKind.StaticMesh, 0, 0, 0, 0, 0, 0, 0, default,
            request.Origin with { Y = 10 }, Vector3.UnitY, request.Origin.Y - 10, 0, 0, false, true);
        IPlaytestNavigationCommands navigation = session.Commands<IPlaytestNavigationCommands>();
        WorldPoint player = session.State.PlayerControl.Position!.Value;
        WorldPoint position = player;

        using (JsonDocument floor = JsonDocument.Parse(navigation.ReadFloor(position.X, 10, position.Z).Message))
        {
            Assert.Equal("floor", floor.RootElement.GetProperty("status").GetString());
            Assert.Equal(10, floor.RootElement.GetProperty("floorY").GetDouble());
            Assert.True(floor.RootElement.GetProperty("standable").GetBoolean());
        }
        using (JsonDocument observed = JsonDocument.Parse(session.Commands<PlaytestDebugModule>().Observe().Message))
            Assert.Equal(10, observed.RootElement.GetProperty("axes").GetProperty("floorY").GetDouble());

        // Ground still arriving is not known: nothing is cast, so nothing is guessed.
        int casts = spatial.FloorProbes.Count;
        spatial.HeightfieldColliderReady = false;
        using (JsonDocument arriving = JsonDocument.Parse(navigation.ReadFloor(position.X, 10, position.Z).Message))
        {
            Assert.Equal("not-loaded", arriving.RootElement.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, arriving.RootElement.GetProperty("floorY").ValueKind);
            Assert.Contains("streamed ground", arriving.RootElement.GetProperty("detail").GetString());
        }
        Assert.Equal(casts, spatial.FloorProbes.Count);
        spatial.HeightfieldColliderReady = true;
        float beyond = position.X + (DaggerfallExteriorCellResidency.StreamingRadius + 2) * DaggerfallExteriorCellResidency.CellSize;
        using (JsonDocument far = JsonDocument.Parse(navigation.ReadFloor(beyond, 10, position.Z).Message))
        {
            Assert.Equal("not-loaded", far.RootElement.GetProperty("status").GetString());
            Assert.Contains("outside the resident exterior window", far.RootElement.GetProperty("detail").GetString());
        }

        spatial.NavigationGrids = [new(7, NavigationProjectionKind.HostWalkableCells, true, 100, 1, 1, .5, .3f, 1.8f, -100, 100, -100, 100)];
        PlanarNavCell[] path = [new(0, 20, 0), new(1, 20, 0), new(2, 20, 0), new(2, 20, 1)];
        spatial.NavigationResult = request => new(path, path.Select(_ => new NavigationPathEdge(NavigationEdgeKind.Walk)).ToArray(),
            NavigationPathOutcome.Reached, new Vector3(.75f, 10, .25f), path[1], NavigationEdgeKind.Walk, 0, 1, 9, 1, 1, 1, false, default, default);
        using JsonDocument route = JsonDocument.Parse(navigation.ReadRoute(1.25, 10, .75).Message);
        NavigationStepRequest asked = spatial.NavigationRequests[^1];
        Assert.Equal(new Vector3(1.25f, 10, .75f), asked.Target);
        Assert.Equal(player.X, asked.From.X);
        Assert.True(asked.From.Y < player.Y, "A route starts from the feet, below the character centre.");
        JsonElement reply = route.RootElement;
        Assert.Equal("Reached", reply.GetProperty("outcome").GetString());
        Assert.Equal(7UL, reply.GetProperty("gridId").GetUInt64());
        Assert.Equal(4, reply.GetProperty("pathCells").GetInt32());
        Assert.Equal("floor", reply.GetProperty("goalFloor").GetProperty("status").GetString());
        JsonElement[] waypoints = [.. reply.GetProperty("waypoints").EnumerateArray()];
        Assert.Equal([0, 2, 3], waypoints.Select(waypoint => waypoint.GetProperty("index").GetInt32()));
        Assert.Equal(1.25, waypoints[1].GetProperty("x").GetDouble());
        Assert.Equal(20, waypoints[1].GetProperty("level").GetInt64());
        Assert.Equal(player, session.State.PlayerControl.Position);
    }

    [Fact]
    public void Playtest_attack_stays_available_at_empty_fatigue_without_spending_or_advancing()
    {
        using ConditionSessionFixture fixture = new();
        var session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);
        var fatigue = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("fatigue"));
        fatigue.SetCurrent(0);
        // The donor never refuses a swing for fatigue; an empty pool is the exhaustion collapse's.
        var action = session.PlaytestAction("attack");
        Assert.True(action.Available);
        Assert.Null(action.Reason);
        Assert.Equal(0, fatigue.Current);
        Assert.Null(session.State.Actors.Player.Attack.Pending);
        session.ApplyProductMode(ProductMode.Modal);
        Assert.Contains("mode-Modal", session.PlaytestAction("forward").Reason);
    }

    [Fact]
    public void Playtest_actions_follow_saved_binding_and_hold_an_attack_for_a_tap_whatever_the_speed_without_spending_resources()
    {
        var (created, _, perception) = VisibleEnemySession([]);
        perception.Receipt = Receipt(new PerceptionPair(1, (ulong)HoldActor(1), 1, 1, PerceptionPairKind.Visible, 1));
        using var session = created;
        var fatigue = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("fatigue"));
        double before = fatigue.Current;
        Assert.Equal("Primary", session.PlaytestAction("attack").Key);
        DaggerfallControlSettings controls = new();
        controls.Rebind("attack", ["KeyB"]);
        session.ApplyPlayerPreferences(controls.Serialize());
        Assert.Equal("KeyB", session.PlaytestAction("attack").Key);
        var speed = session.State.Actors.Player.Stats.GetStat(StatId.Parse("speed"));
        speed.BaseValue = 20;
        double slow = session.PlaytestAction("attack").DurationMs;
        speed.BaseValue = 90;
        double fast = session.PlaytestAction("attack").DurationMs;
        // The hold stays under the power threshold, so the action is a tap and the motion runs on from it.
        Assert.Equal(slow, fast);
        Assert.True(fast / 1000d < DaggerfallTuning.Defaults.Attacks.PowerThresholdSeconds);
        Assert.Equal(before, fatigue.Current);
        Assert.Null(session.State.Actors.Player.Attack.Pending);
    }

    [Fact]
    public void Playtest_look_and_observation_keep_simulation_and_target_selection_unchanged()
    {
        using ConditionSessionFixture fixture = new();
        var session = fixture.Session;
        var position = session.State.PlayerControl.Position;
        var target = session.State.Actors.Player.Targeting.Current;
        var evidence = session.State.Kit.Targeting.LastEvidence;
        var interaction = session.Activation.LastEvidence;
        using var before = JsonDocument.Parse(session.Commands<PlaytestDebugModule>().Observe().Message);
        double yaw = session.State.PlayerControl.YawRadians;
        Assert.Equal(DebugCommandStatus.Success, session.Commands<PlaytestDebugModule>().Look(45, 10).Status);
        using var after = JsonDocument.Parse(session.Commands<PlaytestDebugModule>().Observe().Message);
        Assert.Equal(before.RootElement.GetProperty("simulationStep").GetString(), after.RootElement.GetProperty("simulationStep").GetString());
        Assert.Equal(position, session.State.PlayerControl.Position);
        Assert.NotEqual(yaw, session.State.PlayerControl.YawRadians);
        Assert.Equal(target, session.State.Actors.Player.Targeting.Current);
        Assert.Same(evidence, session.State.Kit.Targeting.LastEvidence);
        Assert.Same(interaction, session.Activation.LastEvidence);
        Assert.Equal(DebugCommandStatus.Success, session.Commands<PlaytestTargetsDebugModule>().Targets().Status);
    }
}
