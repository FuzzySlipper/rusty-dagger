using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Ai;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The actor commands over a started session: a hostile hold enemy two metres in front of the player, seen, its motor
/// rolling a strafe (<see cref="MotorRandom"/>), so with no override it strafes.
/// </summary>
public sealed class DaggerfallActorInspectionTests : IDisposable
{
    private readonly DaggerfallSession _session;
    private readonly PerceptionFake _perception = PerceptionFake.Create();
    private readonly SpatialFake _spatial;
    private readonly IDeveloperActorCommands _commands;
    private readonly long _enemy = HoldActor(40);
    private ulong _step;

    public DaggerfallActorInspectionTests()
    {
        var profile = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        var content = new ContentFake(releases); PopulateContent(content, profile);
        var spatial = _spatial = SpatialFake.Create(profile.Geometry.Spatial[0].Sha256, releases); spatial.KeepPosition = true;
        // Every step's route is found, so a maneuver never turns into Blocked.
        PlanarNavCell[] path = [new(0, 0, 0), new(1, 0, 0)];
        spatial.NavigationResult = request => new(path, path.Select(_ => new NavigationPathEdge(NavigationEdgeKind.Walk)).ToArray(),
            NavigationPathOutcome.Reached, request.From, path[1], NavigationEdgeKind.Walk, 0, 1, 2, 1, 1, 1, false, default, default);
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), _perception.Service,
            random: MotorRandom.Create(strafe: true));
        _session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, profile, DaggerfallTuning.Defaults));
        _commands = _session.Commands<IDeveloperActorCommands>();
        _session.State.Actors.Get(_enemy).ApplyPose(new(new(0, 0, -2), 0));
        _session.State.PlayerControl.MoveTo(Vector3.Zero);
        _session.State.PlayerControl.YawRadians = 0;
        _session.State.WeaponDrawn = true;
        Memory.SetForcedHostile(true);
        _perception.Receipt = Receipt(new PerceptionPair((ulong)_enemy, 1, 2, 1, PerceptionPairKind.Visible, 1));
    }

    public void Dispose() => _session.Dispose();

    private DaggerfallEnemyPerceptionMemory Memory => _session.State.Actors.Get(_enemy).Actor.Get<DaggerfallEnemyPerceptionMemory>();

    private EnemyBehaviorEvidence Step()
    {
        _session.Update(new ProductUpdate(OuterUpdate(++_step), []));
        return _session.EnemyBehavior.LastEvidence[_enemy];
    }

    private static JsonElement Json(DebugCommandResult result)
    {
        Assert.True(result.Succeeded, result.Message);
        return JsonDocument.Parse(result.Message).RootElement;
    }

    private void Run(DebugCommandResult result) => Assert.True(result.Succeeded, result.Message);

    [Fact]
    public void Inspect_reads_the_live_components_and_the_behaviour_owners_evidence()
    {
        EnemyBehaviorEvidence decided = Step();
        JsonElement actor = Json(_commands.Inspect(_enemy.ToString()));

        Assert.Equal(_enemy, actor.GetProperty("durableId").GetInt64());
        Assert.Equal(_session.State.Actors.Get(_enemy).Actor.Entity.Value, actor.GetProperty("entityId").GetUInt64());
        Assert.Equal(_session.DefinitionsByActor[_enemy].Id.Value, actor.GetProperty("definition").GetProperty("id").GetString());
        Assert.Equal(_session.State.Actors.Get(_enemy).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current,
            actor.GetProperty("tracks").GetProperty("health").GetProperty("current").GetDouble());
        JsonElement behavior = actor.GetProperty("behavior");
        Assert.Equal(decided.State.ToString(), behavior.GetProperty("decided").GetString());
        Assert.Equal(_session.State.Actors.Get(_enemy).Pursuit.State.ToString(), behavior.GetProperty("pursuit").GetString());
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, behavior.GetProperty("target").GetInt64());
        Assert.True(behavior.GetProperty("perception").GetProperty("detected").GetBoolean());
        Assert.Equal(Memory.Motor.Strafe, behavior.GetProperty("motor").GetProperty("strafe").GetBoolean());
        Assert.True(actor.GetProperty("disposition").GetProperty("hostile").GetBoolean());
        Assert.Equal(_session.State.Kit.Attacks.ReachOf(_enemy)!.Value, actor.GetProperty("attack").GetProperty("reach").GetDouble());
        Assert.False(actor.GetProperty("spells").GetProperty("casting").GetBoolean());
        Assert.Equal(0, actor.GetProperty("overrides").GetArrayLength());

        // nearest is the live actor closest to the player's feet; an unknown or the player's own id refuses.
        long nearest = _session.State.Actors.All.Where(value => value.DurableId != _session.State.Actors.Player.DurableId && !value.IsDefeated)
            .MinBy(value => value.Position.ToVector().LengthSquared())!.DurableId;
        Assert.Equal(nearest, Json(_commands.Inspect("nearest")).GetProperty("durableId").GetInt64());
        // target is the live actor the player faces; facing away, nobody is.
        Assert.Equal(_enemy, Json(_commands.Inspect("target")).GetProperty("durableId").GetInt64());
        _session.State.PlayerControl.YawRadians = MathF.PI;
        _session.State.Actors.All.Where(value => value.DurableId != _enemy && value.DurableId != _session.State.Actors.Player.DurableId).ToList()
            .ForEach(value => value.ApplyPose(new(new(0, 0, -50), 0)));
        Assert.False(_commands.Inspect("target").Succeeded);
        Assert.False(_commands.Inspect(DaggerfallActorIdentity.PlayerEntityId.ToString()).Succeeded);
        Assert.False(_commands.Inspect("999999").Succeeded);
    }

    [Fact]
    public void The_trace_records_each_gate_for_the_traced_actor_only_and_reads_back_the_ring()
    {
        Step();
        Assert.Null(_session.EnemyBehavior.Trace.ActorId);
        Assert.Empty(_session.EnemyBehavior.Trace.Recent);

        Assert.Empty(Json(_commands.Trace(_enemy.ToString())).GetProperty("decisions").EnumerateArray());
        Step();
        JsonElement decisions = Json(_commands.Trace(_enemy.ToString())).GetProperty("decisions");
        JsonElement decision = Assert.Single(decisions.EnumerateArray());
        string[] gates = [.. decision.GetProperty("gates").EnumerateArray().Select(gate => $"{gate.GetProperty("policy").GetString()}.{gate.GetProperty("gate").GetString()}")];
        Assert.Equal("target.select", gates[0]);
        Assert.Contains("perception.senses", gates);
        Assert.Contains("motor.maneuver", gates);
        Assert.Contains("attack.physical", gates);
        Assert.Contains(gates, gate => gate.StartsWith("spell.", StringComparison.Ordinal));
        JsonElement senses = decision.GetProperty("gates").EnumerateArray().First(gate => gate.GetProperty("policy").GetString() == "perception");
        Assert.Equal("Passed", senses.GetProperty("outcome").GetString());
        Assert.Equal(2, senses.GetProperty("values").GetProperty("distance").GetDouble());
        JsonElement maneuver = decision.GetProperty("gates").EnumerateArray().Last(gate => gate.GetProperty("gate").GetString() == "maneuver");
        Assert.StartsWith("strafe:", maneuver.GetProperty("reason").GetString());
        Assert.Equal("Strafe", decision.GetProperty("state").GetString());

        // A decision that says the same again folds into the one before it.
        Step();
        DaggerfallDecisionRecord latest = _session.EnemyBehavior.Trace.Latest!;
        Assert.True(latest.Repeats >= 1);
        Assert.InRange(_session.EnemyBehavior.Trace.Recent.Count, 1, 2);
        Assert.All(_session.EnemyBehavior.Trace.Recent, record => Assert.NotEmpty(record.Gates));

        Run(_commands.Trace("off"));
        Step();
        Assert.Null(_session.EnemyBehavior.Trace.ActorId);
        Assert.Empty(_session.EnemyBehavior.Trace.Recent);
    }

    [Fact]
    public void A_freeze_holds_every_enemy_through_the_movement_restriction_and_clears()
    {
        Assert.Equal(EnemyBehaviorState.Strafe, Step().State);
        Run(_commands.Trace(_enemy.ToString()));
        Run(_commands.Freeze("on"));
        Assert.Equal(EnemyBehaviorState.Idle, Step().State);
        DaggerfallDecisionGate held = Assert.Single(_session.EnemyBehavior.Trace.Latest!.Gates);
        Assert.Equal(("control", "movement", DaggerfallGateOutcome.Refused, "ai.freeze"), (held.Policy, held.Gate, held.Outcome, held.Reason));
        Assert.All(_session.EnemyBehavior.LastEvidence.Values, evidence => Assert.Contains(evidence.State, new[] { EnemyBehaviorState.Idle, EnemyBehaviorState.Dead }));

        Run(_commands.Freeze("off"));
        Assert.Equal(EnemyBehaviorState.Strafe, Step().State);
        Assert.False(_commands.Freeze("maybe").Succeeded);
    }

    [Fact]
    public void Passive_takes_the_target_away_and_clears()
    {
        Step();
        Run(_commands.Passive(_enemy.ToString(), "on"));
        EnemyBehaviorEvidence passive = Step();
        Assert.Equal(EnemyBehaviorState.Idle, passive.State);
        Assert.Null(passive.Target);
        Assert.Equal("passive", Json(_commands.Inspect(_enemy.ToString())).GetProperty("overrides")[0].GetString());

        Run(_commands.Passive(_enemy.ToString(), "off"));
        Assert.Equal(EnemyBehaviorState.Strafe, Step().State);
    }

    [Theory]
    [InlineData("Chase", "Chase")]
    [InlineData("retreat", "Retreat")]
    [InlineData("Idle", "Idle")]
    public void A_forced_state_replaces_the_motors_maneuver_and_clears(string state, string expected)
    {
        Assert.Equal(EnemyBehaviorState.Strafe, Step().State);
        Run(_commands.Trace(_enemy.ToString()));
        Run(_commands.ForceState(_enemy.ToString(), state));
        Assert.Equal(expected, Step().State.ToString());
        DaggerfallDecisionGate forced = Assert.Single(_session.EnemyBehavior.Trace.Latest!.Gates, gate => gate.Policy == "override");
        Assert.Equal(DaggerfallGateOutcome.Chose, forced.Outcome);
        Assert.Equal(Enum.Parse<PursuitState>(state, ignoreCase: true).ToString(), forced.Reason);
        // The forced maneuver holds the swing back except where the state is to attack.
        Assert.Contains(_session.EnemyBehavior.Trace.Latest.Gates, gate => gate.Policy == "attack" && gate.Outcome == DaggerfallGateOutcome.Refused);

        Run(_commands.ForceState(_enemy.ToString(), "off"));
        Assert.Equal(EnemyBehaviorState.Strafe, Step().State);
        Assert.False(_commands.ForceState(_enemy.ToString(), "Blocked").Succeeded);
    }

    [Fact]
    public void A_forced_moving_state_holds_where_the_enemy_cannot_see_its_target()
    {
        // Behind cover the enemy knows nothing of where the player is, so on its own it doesn't close.
        _perception.Receipt = Receipt(new PerceptionPair((ulong)_enemy, 1, 2, 1, PerceptionPairKind.Occluded, 1));
        Assert.NotEqual(EnemyBehaviorState.Chase, Step().State);
        Run(_commands.ForceState(_enemy.ToString(), "Chase"));
        Assert.Equal(EnemyBehaviorState.Chase, Step().State);
        Run(_commands.ForceState(_enemy.ToString(), "Retreat"));
        Assert.Equal(EnemyBehaviorState.Retreat, Step().State);
    }

    [Fact]
    public void A_forced_idle_does_not_search_for_a_target_that_left_its_sight_and_clears()
    {
        // Seen far beyond reach, so the enemy remembers where the player was.
        _perception.Receipt = Receipt(new PerceptionPair((ulong)_enemy, 1, 12, 1, PerceptionPairKind.Visible, 1));
        _session.State.Actors.Get(_enemy).ApplyPose(new(new(0, 0, -12), 0));
        Step();
        Assert.NotNull(_session.State.Actors.Get(_enemy).Pursuit.LastKnownPosition);

        Run(_commands.ForceState(_enemy.ToString(), "Idle"));
        _perception.Receipt = Receipt(new PerceptionPair((ulong)_enemy, 1, 12, 1, PerceptionPairKind.Occluded, 1));
        _spatial.NavigationRequests.Clear();
        // Pursuit says once that the target is lost, then idles; it never searches or moves.
        Assert.Equal(EnemyBehaviorState.TargetLost, Step().State);
        Assert.Equal(EnemyBehaviorState.Idle, Step().State);
        Assert.Empty(_spatial.NavigationRequests);

        Run(_commands.ForceState(_enemy.ToString(), "off"));
        _perception.Receipt = Receipt(new PerceptionPair((ulong)_enemy, 1, 12, 1, PerceptionPairKind.Visible, 1));
        Assert.Equal(EnemyBehaviorState.Chase, Step().State);
        Assert.NotEmpty(_spatial.NavigationRequests);
    }

    [Fact]
    public void A_chosen_target_replaces_target_selection_and_clears()
    {
        long other = _session.SpawnActor("thief", new(new(4, 0, -4), 0));
        Step();
        Run(_commands.Target(_enemy.ToString(), other.ToString()));
        Assert.Equal(other, Step().Target!.Value.DurableId);
        Assert.False(_commands.Target(_enemy.ToString(), _enemy.ToString()).Succeeded);

        Run(_commands.Target(_enemy.ToString(), "off"));
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, Step().Target!.Value.DurableId);
    }

    [Fact]
    public void Forced_hostility_overrides_a_pacification_without_touching_the_saved_disposition_and_clears()
    {
        Memory.SetForcedHostile(false);
        Memory.MagicallyPacified = true;
        Assert.NotEqual(EnemyBehaviorState.Strafe, Step().State);
        Assert.True(_session.EnemyBehavior.IsPacified(_enemy));

        Run(_commands.Hostile(_enemy.ToString(), "on"));
        Assert.False(_session.EnemyBehavior.IsPacified(_enemy));
        Assert.Equal(EnemyBehaviorState.Strafe, Step().State);
        Assert.True(Memory.MagicallyPacified);
        Assert.False(Memory.ForcedHostile);

        Run(_commands.Hostile(_enemy.ToString(), "off"));
        Assert.True(_session.EnemyBehavior.IsPacified(_enemy));
        Assert.NotEqual(EnemyBehaviorState.Strafe, Step().State);
    }
}
