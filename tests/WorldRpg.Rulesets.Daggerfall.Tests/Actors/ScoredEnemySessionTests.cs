using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The opted-in fights through the live session: the Ranger and the Battlemage on the intelligent tactic, the Grizzly
/// Bear on the beast preset. Each enemy is traced, so its decision reads as the options it weighed.
/// </summary>
public sealed class ScoredEnemySessionTests
{
    [Fact]
    public void The_ranger_shoots_while_it_has_arrows_and_closes_with_its_blade_once_they_are_gone()
    {
        using Fixture fixture = new();
        long ranger = fixture.Spawn("encounter-ranger", 15f);

        MechanicsInventoryCoordinator quiver = Assert.IsType<MechanicsInventoryCoordinator>(fixture.Session.State.ActorInventories.InventoryFor(ranger));
        ulong Arrows() => (ulong)quiver.Read().Stacks.Where(stack => stack.Definition.Value == "arrow").Sum(stack => (long)stack.Quantity);
        ulong carried = Arrows();

        DaggerfallDecisionRecord shooting = fixture.Decide(ranger);
        Assert.Equal("strike:archer-shot", Chosen(shooting));
        Assert.Equal(carried - 1, Arrows());

        foreach (var stack in quiver.Read().Stacks.Where(stack => stack.Definition.Value == "arrow").ToArray())
            quiver.Consume(new InventoryConsume(stack.Id, stack.Quantity));
        DaggerfallDecisionRecord closing = fixture.Decide(ranger);

        Assert.Equal("no-arrows", Gate(closing, "strike:archer-shot").Reason);
        Assert.Equal("strike:enemy-class-equipped-melee", Chosen(closing));
        Assert.Equal(EnemyBehaviorState.Chase, closing.State);
    }

    [Fact]
    public void The_battlemage_casts_at_range_and_moves_for_a_lane_when_cover_blocks_it()
    {
        using Fixture fixture = new();
        // A battlemage of level 6 to 8 casts from the class tier with bolts in it.
        long battlemage = fixture.Spawn("encounter-battlemage", 15f, level: 6);

        DaggerfallDecisionRecord casting = fixture.Decide(battlemage);
        Assert.StartsWith("spell:", Chosen(casting), StringComparison.Ordinal);
        Assert.True(fixture.Session.LastEnemySpell.ContainsKey(battlemage), "the battlemage released no spell");

        // Cover across the lane: every shot it carries waits, and its discipline moves it round for a clear one.
        fixture.Spatial.CapsuleCastHit = _ => new SpatialHit { Present = true, Kind = SpatialHitKind.StaticMesh };
        fixture.Spatial.FloorHit = request => MathF.Abs(request.Direction.Y) > .9f ? default
            : new SpatialHit { Present = true, Kind = SpatialHitKind.StaticMesh, Point = request.Origin + request.Direction, Distance = 1f };
        DaggerfallDecisionRecord held = fixture.Decide(battlemage);

        Assert.Equal("reposition", Chosen(held));
        Assert.Equal(EnemyBehaviorState.Strafe, held.State);
        // Its bolt still in flight keeps its spells off the menu; the bow waits on the lane.
        Assert.Equal("lane-blocked", Gate(held, "strike:archer-shot").Reason);
    }

    [Fact]
    public void The_bear_bites_then_flees_wounded_leaves_the_player_alone_and_turns_on_them_again_when_struck()
    {
        using Fixture fixture = new();
        long bear = fixture.Spawn("grizzly-bear", 1.5f);

        Assert.StartsWith("strike:", Chosen(fixture.Decide(bear)), StringComparison.Ordinal);

        Track health = fixture.Session.State.Actors.Get(bear).Stats.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(health.Maximum.Value * .1d, clamp: true);
        Vector3 from = fixture.Session.State.Actors.Get(bear).Position.ToVector();
        DaggerfallDecisionRecord fleeing = fixture.Decide(bear);
        Assert.Equal("flee", Chosen(fleeing));
        Assert.Equal(EnemyBehaviorState.Flee, fleeing.State);
        // Away from the player by the Engine's navigation, at the tuned distance.
        Vector3 player = fixture.Session.State.PlayerControl.Position!.Value.ToVector();
        Vector3 goal = fixture.Spatial.NavigationRequests[^1].Target;
        Assert.Equal(DaggerfallTuning.Defaults.EnemyBehavior.Tactics.Flee.Distance, Vector2.Distance(new(goal.X, goal.Z), new(from.X, from.Z)), 1);
        Assert.True(Vector2.Distance(new(goal.X, goal.Z), new(player.X, player.Z)) > 15f);

        // The fake's navigation walks it there in a step; arrived, it leaves the player alone though it still sees them.
        DaggerfallDecisionRecord arrived = fixture.Decide(bear);
        Assert.Contains(arrived.Gates, gate => gate is { Gate: "intent", Reason: "flee:arrived" });
        for (int step = 0; step < 30; step++)
        {
            DaggerfallDecisionRecord alone = fixture.Decide(bear);
            Assert.Contains(alone.Gates, gate => gate is { Gate: "intent", Reason: "leave-alone" });
            Assert.Equal(EnemyBehaviorState.Idle, alone.State);
        }

        fixture.Session.EnemyBehavior.Provoke(bear, 0);
        Assert.DoesNotContain(fixture.Decide(bear).Gates, gate => gate is { Gate: "intent", Reason: "leave-alone" });
    }

    private static DaggerfallDecisionGate Gate(DaggerfallDecisionRecord record, string name) =>
        Assert.Single(record.Gates, gate => gate.Policy == DaggerfallScoredEnemyTactic.Policy && gate.Gate == name);

    private static string Chosen(DaggerfallDecisionRecord record) =>
        Assert.Single(record.Gates, gate => gate.Policy == DaggerfallScoredEnemyTactic.Policy && gate.Gate != "intent"
            && gate.Outcome == DaggerfallGateOutcome.Chose).Gate;

    private sealed class Fixture : IDisposable
    {
        private readonly List<string> releases = [];
        private ulong _step;
        internal DaggerfallSession Session { get; }
        internal SpatialFake Spatial { get; }

        internal Fixture()
        {
            string root = TestData.RepositoryRoot;
            DaggerfallSiteProfile inputs = ReadInputs(root);
            ContentFake content = new(releases);
            PopulateContent(content, inputs);
            Spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
            PerceptionFake perception = PerceptionFake.Create();
            // Every playback completes as it advances, so a swing or shot clears and the next can begin.
            AppearanceFake appearance = new(releases) { AdvanceReceiptForAll = CompletedMarker(1) };
            EngineContextFake engine = EngineContextFake.Create(content, Spatial.Service, appearance, perception.Service);
            Session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));
            // The Engine sees every pair at its distance, and every walk arrives where it was going in one step.
            perception.Responder = request => Receipt([.. request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray().Select(target =>
                new PerceptionPair(observer.Entity, target.Entity, Vector3.Distance(observer.Origin, target.Center), 1d, PerceptionPairKind.Visible, 1d)))]);
            PlanarNavCell[] path = [new(0, 0, 0), new(1, 0, 0)];
            Spatial.NavigationResult = request => new(path, path.Select(_ => new NavigationPathEdge(NavigationEdgeKind.Walk)).ToArray(),
                NavigationPathOutcome.Reached, request.Target, path[1], NavigationEdgeKind.Walk, 0, 1, 2, 1, 1, 1, false, default, default);
            // The population authored for the dungeon waits far off, so only the spawned enemy decides near the player.
            Vector3 player = Session.State.PlayerControl.Position!.Value.ToVector();
            foreach (ActorState other in Session.State.Actors.All.Where(actor => actor.DurableId != Session.State.Actors.Player.DurableId))
                other.ApplyPose(new(WorldPoint.From(player + new Vector3(0, 0, 400)), 0));
        }

        /// <summary>A hostile enemy <paramref name="metres"/> in front of the player, facing them, traced.</summary>
        internal long Spawn(string actor, float metres, int level = 5)
        {
            Vector3 player = Session.State.PlayerControl.Position!.Value.ToVector();
            long id = Session.SpawnActor(actor, new ActorPose(WorldPoint.From(player - new Vector3(0, 0, metres)), 0f), level);
            Session.State.Actors.Get(id).Actor.Get<DaggerfallEnemyPerceptionMemory>().SetForcedHostile(true);
            Session.EnemyBehavior.Trace.Follow(id);
            return id;
        }

        /// <summary>One more update, and the traced enemy's decision in it.</summary>
        internal DaggerfallDecisionRecord Decide(long actor)
        {
            Session.Update(new ProductUpdate(OuterUpdate(++_step), []));
            return Session.EnemyBehavior.Trace.Recent.Last(record => record.Gates[0].Policy != "delivery");
        }

        public void Dispose() => Session.Dispose();
    }
}
