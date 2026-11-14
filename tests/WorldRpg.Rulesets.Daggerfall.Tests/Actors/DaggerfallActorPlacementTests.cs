using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Spawn poses, the enemy query, an ally's target and where everyone stands, built without the session.</summary>
public sealed class DaggerfallActorPlacementTests
{
    private const long Foe = 10, Ally = 11, Hidden = 12, Pacified = 13, Protected = 14, Fallen = 15;

    private sealed class Stage : IDisposable
    {
        internal Stage(long[]? active = null)
        {
            DaggerfallActorDefinition monster = TestPayload.Definitions.Actors.Values.First(actor => actor.Kind == DaggerfallActorKinds.Monster);
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), TestSessions.HealthyMechanics(), "health", DaggerActorFactory.PlayerCapabilities);
            foreach (long actor in new[] { Foe, Ally, Hidden, Pacified, Protected, Fallen })
            {
                Actors.CreateActor(actor, new EntityTypeId("test"), TestSessions.HealthyMechanics(), new ActorPose(new WorldPoint(actor, 0f, 4f), 0f), "health", DaggerActorFactory.NonPlayerCapabilities);
                Definitions[actor] = monster;
            }
            Actors.Get(Fallen).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).SetCurrent(0);
            _ = CombatCollaborators.Targeting(Actors, Definitions, out _spatial, out SpatialFake fake);
            Spatial = fake;
            Placement = new DaggerfallActorPlacement(Actors, Player, id => id != Hidden, Definitions,
                id => id == Ally ? "player-ally" : null, id => id == Pacified, id => id != Protected, _spatial, Perception.Service,
                Random.Service, () => default, allyDetectionDistance: 10d, fieldOfViewYDegrees: 60f);
        }

        private readonly SpatialMovementSystem _spatial;
        internal ActorsState Actors { get; } = new();
        internal Dictionary<long, DaggerfallActorDefinition> Definitions { get; } = [];
        internal PlayerControlState Player { get; } = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal KeyedRandomFake Random { get; } = KeyedRandomFake.Create(3);
        internal SpatialFake Spatial { get; }
        internal DaggerfallActorPlacement Placement { get; }

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
