using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Actors brought in close around the player (world.spawn and a start's opening foes) stand on clear floor ahead of
/// them, facing them, an enemy hostile; without clear floor nothing is brought and the refusal says why. Built without
/// the session.
/// </summary>
public sealed class DaggerfallNearbySpawnTests
{
    private sealed class Stage : IDisposable
    {
        internal Stage(bool placed = true)
        {
            Player = new(placed ? new WorldPoint(0f, 0f, 0f) : null, 0f, 0f);
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), TestSessions.HealthyMechanics(), "health", DaggerfallActorFactory.PlayerCapabilities);
            _ = CombatCollaborators.Targeting(Actors, new Dictionary<long, DaggerfallActorDefinition>(), out _spatial, out SpatialFake fake);
            Spatial = fake;
            DaggerfallActorPlacement placement = new(Actors, Player, _ => true, new Dictionary<long, DaggerfallActorDefinition>(), _ => null, _ => false,
                _ => true, _ => false, _ => false, () => false, _spatial, PerceptionFake.Create().Service, Random.Service, () => default, detectionDistance: 10d, fieldOfViewYDegrees: 60f);
            Spawn = new DaggerfallNearbySpawn(TestPayload.Definitions.Actors, mobile => Presented(mobile), placement, Player,
                (actor, pose) =>
                {
                    Brought.Add((actor, pose));
                    Spawned?.Invoke();
                    return 100 + Brought.Count;
                },
                Hostile.Add, Reports.Add);
        }

        private readonly SpatialMovementSystem _spatial;
        internal ActorsState Actors { get; } = new();
        internal PlayerControlState Player { get; }
        internal KeyedRandomFake Random { get; } = KeyedRandomFake.Create(3);
        internal SpatialFake Spatial { get; }
        internal DaggerfallNearbySpawn Spawn { get; }
        internal Func<int, bool> Presented { get; set; } = _ => true;
        internal Action? Spawned { get; set; }
        internal List<(string Actor, ActorPose Pose)> Brought { get; } = [];
        internal List<long> Hostile { get; } = [];
        internal List<string> Reports { get; } = [];

        /// <summary>Level floor at the player's feet under every downward look.</summary>
        internal void Floor() => Spatial.FloorHit = request => request.Direction.Y < -.5f
            ? new SpatialHit { Present = true, Kind = SpatialHitKind.Entity, Point = request.Origin with { Y = 0f }, Normal = Vector3.UnitY, Distance = request.Origin.Y }
            : default;

        public void Dispose() { _spatial.Dispose(); Actors.Dispose(); }
    }

    [Fact]
    public void Count_actors_stand_on_clear_floor_ahead_of_the_player_facing_them_and_enemies_come_hostile()
    {
        using Stage stage = new();
        stage.Floor();

        DaggerfallNearbySpawnResult result = stage.Spawn.Bring("fire-daedra", 2, 6f);

        Assert.Null(result.Refusal);
        Assert.Equal(new long[] { 101, 102 }, result.Brought);
        Assert.Equal(result.Brought, stage.Hostile);
        Vector3 forward = ActorHeading.Forward(stage.Player.YawRadians);
        foreach ((string actor, ActorPose pose) in stage.Brought)
        {
            Assert.Equal("fire-daedra", actor);
            Vector3 at = pose.Position.ToVector();
            Assert.Equal(0f, at.Y);
            float distance = new Vector2(at.X, at.Z).Length();
            // Never on top of the player: from half METRES (at least 2 m) out to METRES.
            Assert.InRange(distance, 3f, 6f);
            Assert.True(Vector3.Dot(Vector3.Normalize(at with { Y = 0f }), forward) >= MathF.Cos(DaggerfallActorPlacement.AheadDegrees * MathF.PI / 180f) - 1e-4f);
            Assert.Equal(MathF.Atan2(-at.X, at.Z), pose.HeadingYawRadians, 4);
        }
        // Looked for in front, from a step above the feet, under the placement's keyed draws.
        Assert.Contains(stage.Random.Requests, draw => draw.Scope == "daggerfall.nearby-spawn" && draw.Key == "bring:0:0:attempt:0:angle"
            && draw.Minimum == 0 && draw.Maximum == 2 * DaggerfallActorPlacement.AheadDegrees);
        Assert.All(stage.Spatial.FloorProbes.Where(probe => probe.Direction.Y > -.5f),
            probe => Assert.Equal(DaggerfallActorPlacement.SummonSeparation, probe.Origin.Y, 4));
    }

    [Fact]
    public void Without_clear_floor_nothing_is_brought_and_the_refusal_says_how_many_were_left()
    {
        using Stage stage = new();
        DaggerfallNearbySpawnResult none = stage.Spawn.Bring("fire-daedra", 2, 6f);
        Assert.Empty(none.Brought);
        Assert.Equal("No clear floor within 6 m of the player, in sight of them, for 2 more.", none.Refusal);
        Assert.Empty(stage.Brought);
        // Ahead first, then anywhere around.
        Assert.Contains(stage.Random.Requests, draw => draw.Key == "bring:0:0:around:attempt:7:distance");

        // The first finds floor; the body of the second would overlap, so only one is brought.
        stage.Floor();
        stage.Spawned = () => stage.Spatial.OverlapHit = _ => new SpatialHit { Present = true };
        DaggerfallNearbySpawnResult one = stage.Spawn.Bring("fire-daedra", 2, 6f);
        Assert.Equal(new long[] { 101 }, one.Brought);
        Assert.Equal("No clear floor within 6 m of the player, in sight of them, for one more.", one.Refusal);
    }

    [Fact]
    public void A_spawn_refuses_what_it_cannot_bring()
    {
        using Stage stage = new();
        stage.Floor();
        Assert.Equal("A count is 1 to 8.", stage.Spawn.Bring("fire-daedra", 0, 6f).Refusal);
        Assert.Equal("A count is 1 to 8.", stage.Spawn.Bring("fire-daedra", 9, 6f).Refusal);
        Assert.Equal("A distance is 2 to 30 metres.", stage.Spawn.Bring("fire-daedra", 1, 1f).Refusal);
        Assert.Equal("A distance is 2 to 30 metres.", stage.Spawn.Bring("fire-daedra", 1, float.NaN).Refusal);
        Assert.Equal("'no-such-actor' is no actor definition that can be spawned.", stage.Spawn.Bring("no-such-actor", 1, 6f).Refusal);
        Assert.Equal("'player' is no actor definition that can be spawned.", stage.Spawn.Bring("player", 1, 6f).Refusal);
        stage.Presented = _ => false;
        Assert.StartsWith("'fire-daedra' cannot be drawn here", stage.Spawn.Bring("fire-daedra", 1, 6f).Refusal, StringComparison.Ordinal);
        Assert.Empty(stage.Brought);
        using Stage nowhere = new(placed: false);
        Assert.Equal("The player has no position.", nowhere.Spawn.Bring("fire-daedra", 1, 6f).Refusal);
    }

    [Fact]
    public void A_starts_foes_wait_for_floor_then_come_and_are_reported_when_none_is_found_in_time()
    {
        using Stage stage = new();
        stage.Spawn.Expect(new DaggerfallStartFoes(["fire-daedra", "fire-daedra"], 4f));
        for (int step = 1; step < DaggerfallNearbySpawn.ExpectedSteps; step++) stage.Spawn.BringExpected();
        Assert.Empty(stage.Brought);
        Assert.Empty(stage.Reports);

        stage.Floor();
        stage.Spawn.BringExpected();
        Assert.Equal(2, stage.Brought.Count);
        Assert.Equal(2, stage.Hostile.Count);
        stage.Spawn.BringExpected();
        Assert.Equal(2, stage.Brought.Count);

        // Without floor the whole time, the foes left are named on the message line once and no longer looked for.
        stage.Spatial.FloorHit = _ => default;
        stage.Spawn.Expect(new DaggerfallStartFoes(["fire-daedra"], 4f));
        for (int step = 0; step < DaggerfallNearbySpawn.ExpectedSteps + 5; step++) stage.Spawn.BringExpected();
        string report = Assert.Single(stage.Reports);
        Assert.StartsWith("The start's foes fire-daedra were not brought: No clear floor within 4 m", report, StringComparison.Ordinal);
    }

    [Fact]
    public void No_published_actor_is_a_townsperson_so_world_spawn_never_brings_one_with_a_look_of_its_own()
    {
        // Townsfolk come only from a town's source population, dressed by its climate's race; world.spawn brings actor definitions.
        Assert.DoesNotContain(TestPayload.Definitions.Actors.Values, definition => definition.Kind == DaggerfallActorKinds.Civilian);
        using Stage stage = new();
        Assert.Equal("'civilian' is no actor definition that can be spawned.", stage.Spawn.Bring(DaggerfallActorKinds.Civilian, 1, 5F).Refusal);
    }
}
