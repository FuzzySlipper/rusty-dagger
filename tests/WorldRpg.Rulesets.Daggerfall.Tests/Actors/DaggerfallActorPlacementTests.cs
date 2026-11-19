using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Spawn poses, the enemy query, the team rule's targets and where everyone stands, built without the session.</summary>
public sealed class DaggerfallActorPlacementTests
{
    private const long Foe = 10, Ally = 11, Hidden = 12, Pacified = 13, Protected = 14, Fallen = 15;

    private sealed class Stage : IDisposable
    {
        internal Stage(long[]? active = null)
        {
            DaggerfallActorDefinition monster = TestPayload.Definitions.Actors.Values.First(actor => actor.Kind == DaggerfallActorKinds.Monster);
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), TestSessions.HealthyMechanics(), "health", DaggerfallActorFactory.PlayerCapabilities);
            foreach (long actor in new[] { Foe, Ally, Hidden, Pacified, Protected, Fallen })
            {
                Actors.CreateActor(actor, new EntityTypeId("test"), TestSessions.HealthyMechanics(), new ActorPose(new WorldPoint(actor, 0f, 4f), 0f), "health", DaggerfallActorFactory.NonPlayerCapabilities);
                Definitions[actor] = monster;
            }
            Actors.Get(Fallen).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).SetCurrent(0);
            _ = CombatCollaborators.Targeting(Actors, Definitions, out _spatial, out SpatialFake fake);
            Spatial = fake;
            Placement = new DaggerfallActorPlacement(Actors, Player, id => id != Hidden, Definitions,
                id => Teams.GetValueOrDefault(id), id => id == Pacified, id => id != Protected, Restrained.Contains, Joins.Contains, () => Infighting,
                _spatial, Perception.Service, Random.Service, () => default, detectionDistance: 10d, fieldOfViewYDegrees: 60f);
        }

        private readonly SpatialMovementSystem _spatial;
        internal ActorsState Actors { get; } = new();
        internal Dictionary<long, DaggerfallActorDefinition> Definitions { get; } = [];
        internal PlayerControlState Player { get; } = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal KeyedRandomFake Random { get; } = KeyedRandomFake.Create(3);
        internal SpatialFake Spatial { get; }
        internal DaggerfallActorPlacement Placement { get; }
        /// <summary>The team each actor fights for now; the ally's is the player's.</summary>
        internal Dictionary<long, string> Teams { get; } = new() { [Ally] = "player-ally" };
        /// <summary>The actors a quest holds back.</summary>
        internal HashSet<long> Restrained { get; } = [];
        /// <summary>The actors a quest opted into infighting.</summary>
        internal HashSet<long> Joins { get; } = [];
        /// <summary>Whether any quest has infighting foes.</summary>
        internal bool Infighting { get; set; }

        /// <summary>Every observer sees every target it asks about, at the distance between them.</summary>
        internal void SeeEveryone() => Perception.Responder = request => TestSessions.Receipt([.. request.Observers.ToArray().SelectMany(observer =>
            request.Targets.ToArray().Select(target => new PerceptionPair(observer.Entity, target.Entity, Vector3.Distance(observer.Origin, target.Center), 1d,
                PerceptionPairKind.Visible, 1d)))]);

        public void Dispose() { _spatial.Dispose(); Actors.Dispose(); }
    }

    [Fact]
    public void The_enemy_query_asks_once_for_the_active_undefeated_foes_the_caller_admits_with_their_current_team()
    {
        using Stage stage = new();
        stage.Perception.Responder = request => TestSessions.Receipt(
            [.. request.Targets.ToArray().Select(target => new PerceptionPair(1, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d))]);

        PerceptionPair[] pairs = [.. stage.Placement.QueryEnemies(DaggerfallActorIdentity.PlayerEntityId, new WorldPoint(0f, 0f, 0f), Vector3.UnitZ, 9d, -1d,
            definition => definition.Team != "player-ally")];

        PerceptionQueryRequest request = Assert.Single(stage.Perception.Requests);
        Assert.Equal((ulong)DaggerfallActorIdentity.PlayerEntityId, Assert.Single(request.Observers.ToArray()).Entity);
        // The hidden actor is not in the world, the fallen one is defeated, and the ally's team is the player's.
        Assert.Equal(new ulong[] { Foe, Pacified, Protected }, request.Targets.ToArray().Select(target => target.Entity));
        Assert.Equal(3, pairs.Length);
    }

    [Fact]
    public void An_ally_targets_the_nearest_visible_foe_it_may_attack()
    {
        using Stage stage = new();
        stage.Perception.Responder = request => TestSessions.Receipt(
            new PerceptionPair(Ally, Pacified, 1d, 1d, PerceptionPairKind.Visible, 1d),
            new PerceptionPair(Ally, Protected, 2d, 1d, PerceptionPairKind.Visible, 2d),
            new PerceptionPair(Ally, Foe, 3d, 1d, PerceptionPairKind.Visible, 3d));

        PursuitTarget target = Assert.IsType<PursuitTarget>(stage.Placement.SelectAllyTarget(Ally));

        // Pacified foes and those a quest protects are passed over, so the farthest is the one attacked.
        Assert.Equal(Foe, target.DurableId);
        Assert.Equal(stage.Actors.Get(Foe).Position, target.Position);
        Assert.Null(stage.Placement.SelectAllyTarget(Fallen));
    }

    [Fact]
    public void An_enemy_pursues_the_player_unless_a_quest_holds_it_back_and_an_ally_its_nearest_foe_where_it_may_fight()
    {
        using Stage stage = new();
        stage.SeeEveryone();

        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, stage.Placement.SelectTarget(Foe)?.DurableId);
        Assert.Equal(stage.Player.Position, stage.Placement.SelectTarget(Foe)?.Position);
        // The ally takes its nearest foe it may attack, passing the pacified and the protected.
        Assert.Equal(Foe, stage.Placement.SelectTarget(Ally)?.DurableId);
        stage.Restrained.Add(Foe);
        Assert.Null(stage.Placement.SelectTarget(Foe));
        Assert.Null(stage.Placement.SelectTarget(Fallen));
        // An ally a quest keeps out of fights fights no one.
        stage.Teams[Protected] = "player-ally";
        Assert.Null(stage.Placement.SelectTarget(Protected));
    }

    [Fact]
    public void Only_a_quests_infighting_turns_an_enemy_on_a_nearer_visible_foe_of_another_team()
    {
        using Stage stage = new();
        stage.SeeEveryone();
        stage.Teams[Foe] = stage.Teams[Pacified] = "orcs";

        stage.Infighting = true;
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, stage.Placement.SelectTarget(Foe)?.DurableId);

        // Opted in, it turns on the nearest visible foe of another team: the ally a metre off, not its fellow orc.
        stage.Joins.Add(Foe);
        Assert.Equal(Ally, stage.Placement.SelectTarget(Foe)?.DurableId);
        stage.Teams[Ally] = "orcs";
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, stage.Placement.SelectTarget(Foe)?.DurableId);
        stage.Teams[Ally] = "player-ally";
        // A player nearer than any foe stays the target, and without an active infighting quest nothing changes.
        stage.Player.MoveTo(new Vector3(Foe, 0f, 4.5f));
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, stage.Placement.SelectTarget(Foe)?.DurableId);
        stage.Player.MoveTo(Vector3.Zero);
        stage.Infighting = false;
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, stage.Placement.SelectTarget(Foe)?.DurableId);
    }

    [Fact]
    public void A_spawn_pose_stands_on_a_clear_floor_facing_the_origin_and_none_is_found_without_one()
    {
        using Stage stage = new();
        Vector3 floor = new(0f, -1f, -3f);
        stage.Spatial.FloorHit = request => request.Direction.Y < -.5f
            ? new SpatialHit { Present = true, Kind = SpatialHitKind.Entity, Point = floor, Normal = Vector3.UnitY, Distance = 1 }
            : default;

        Assert.True(stage.Placement.TrySpawnPose("test", "test.scope", new WorldPoint(0f, 0f, 0f), 2f, 6f, out ActorPose pose));
        Assert.Equal(WorldPoint.From(floor), pose.Position);
        Assert.Equal(MathF.Atan2(0f - floor.X, -(0f - floor.Z)), pose.HeadingYawRadians);
        Assert.All(stage.Random.Requests, draw => Assert.Equal("test.scope", draw.Scope));
        Assert.Contains(stage.Random.Requests, draw => draw.Key == "test:attempt:0:angle" && draw.Minimum == 90 && draw.Maximum == 270);

        stage.Spatial.FloorHit = _ => default;
        Assert.False(stage.Placement.TrySpawnPose("none", "test.scope", new WorldPoint(0f, 0f, 0f), 2f, 6f, out _));
        Assert.Contains(stage.Random.Requests, draw => draw.Key == "none:attempt:7:distance");
    }

    [Fact]
    public void Current_positions_are_the_players_and_every_active_actors()
    {
        using Stage stage = new();
        IReadOnlyDictionary<long, WorldPoint> positions = stage.Placement.CurrentPositions();
        Assert.Equal(new WorldPoint(0f, 0f, 0f), positions[DaggerfallActorIdentity.PlayerEntityId]);
        Assert.Equal(stage.Actors.Get(Foe).Position, positions[Foe]);
        Assert.False(positions.ContainsKey(Hidden));
    }
}
