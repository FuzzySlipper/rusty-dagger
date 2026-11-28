using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Testing;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Targeting;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Sight over uneven ground, under the real Engine's perception: a centaur and the player stand on the floor either
/// side of a rise a hand high, as a centaur band east of Sentinel stood blind beside the player. A ray between their
/// feet strikes the rise; the player's eye sees the centaur's body, and the centaur sees the player along a line
/// lifted evenly off the floor.
/// </summary>
public sealed class DaggerfallActorSightTests
{
    private const long Centaur = 1_000_000_000_009;
    private const float Rise = .1F;

    [Fact]
    public void Sight_over_a_rise_runs_from_the_eye_to_the_body_where_a_ray_between_feet_strikes_the_ground()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition centaur = definitions.RequireActor(new DaggerfallActorId("centaur"));
        Assert.True(definitions.Walks(centaur));
        Dictionary<long, DaggerfallActorDefinition> authored = new() { [Centaur] = centaur };
        DaggerfallActorSight sight = Sight(authored, () => inputs);
        WorldPoint player = new(-1F, Surface(-1F), 0F), foe = new(1.5F, Surface(1.5F), 0F);

        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        host.Call(engine =>
        {
            using SpatialMovementSystem spatial = new(engine.Spatial, engine.Content, null, DaggerfallTuning.Defaults.Spatial);
            _ = engine.Spatial.ApplyCollisionResidency(new CollisionResidencyRequest(spatial.Session,
                new[] { new StaticMeshAsset(1, 0, 6, 0, 4) },
                new Vector3[] { new(-3, 0, -3), new(-3, 0, 3), new(0, Rise, -3), new(0, Rise, 3), new(3, 0, -3), new(3, 0, 3) },
                new Triangle[] { new(0, 1, 2), new(1, 3, 2), new(2, 3, 4), new(3, 5, 4) },
                new[] { new StaticMeshInstance(2, 1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)) },
                ReadOnlyMemory<ulong>.Empty, ReadOnlyMemory<ulong>.Empty));

            // Between feet, the rise is in the way.
            Assert.Equal(PerceptionPairKind.Occluded, Pair(engine, spatial, player.ToVector(), foe.ToVector()));
            // The centaur sees the player along the line pursuit and the senses now look: lifted evenly off the floor, its
            // distance the poses'.
            (Vector3 eye, Vector3 seen) = sight.Line(Centaur, foe, DaggerfallActorIdentity.PlayerEntityId, player);
            Assert.Equal(PerceptionPairKind.Visible, Pair(engine, spatial, eye, seen));
            Assert.Equal(Vector3.Distance(foe.ToVector(), player.ToVector()), Vector3.Distance(eye, seen), 4);

            // The player's attack finds the centaur over the rise: from its eye to the centaur's body.
            using ActorsState actors = new();
            actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), HealthyMechanics(), "health",
                DaggerfallActorFactory.PlayerCapabilities);
            actors.CreateActor(Centaur, new EntityTypeId("centaur"), HealthyMechanics(), new ActorPose(foe, 0F), "health",
                DaggerfallActorFactory.NonPlayerCapabilities);
            TargetingService targeting = new(engine.Perception, spatial, actors,
                new DaggerfallTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting, sight));
            Assert.Equal(Centaur, targeting.Select(player, Vector3.UnitX, 3d));
        });
    }

    [Fact]
    public void A_person_with_no_mobile_sees_a_foe_over_a_rise_from_near_the_top_of_their_sprite()
    {
        // A convoy's guard (#11181) or a hired townsperson (#11105) is a person with no mobile: before, it looked from
        // the floor and the rise hid the centaur, so it stood idle beside the fight.
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        const long Guard = 1_000_000_000_011;
        DaggerfallActorDefinition person = definitions.RequireActor(new DaggerfallActorId("thief")) with { MobileId = null };
        Assert.False(definitions.Walks(person));
        Dictionary<long, DaggerfallActorDefinition> authored = new()
        {
            [Centaur] = definitions.RequireActor(new DaggerfallActorId("centaur")),
            [Guard] = person,
        };
        DaggerfallActorSight sight = new(authored, definitions, () => inputs, (float)DaggerfallTuning.Defaults.Camera.EyeHeight,
            PlayerBodyMiddle, id => id == Guard ? .9d : null);
        WorldPoint guard = new(-1F, Surface(-1F), 0F), foe = new(1.5F, Surface(1.5F), 0F);

        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        host.Call(engine =>
        {
            using SpatialMovementSystem spatial = new(engine.Spatial, engine.Content, null, DaggerfallTuning.Defaults.Spatial);
            _ = engine.Spatial.ApplyCollisionResidency(new CollisionResidencyRequest(spatial.Session,
                new[] { new StaticMeshAsset(1, 0, 6, 0, 4) },
                new Vector3[] { new(-3, 0, -3), new(-3, 0, 3), new(0, Rise, -3), new(0, Rise, 3), new(3, 0, -3), new(3, 0, 3) },
                new Triangle[] { new(0, 1, 2), new(1, 3, 2), new(2, 3, 4), new(3, 5, 4) },
                new[] { new StaticMeshInstance(2, 1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)) },
                ReadOnlyMemory<ulong>.Empty, ReadOnlyMemory<ulong>.Empty));

            Assert.Equal(1.8F * .9F, sight.EyeLift(Guard), 4);
            (Vector3 eye, Vector3 seen) = sight.Line(Guard, guard, Centaur, foe);
            Assert.Equal(PerceptionPairKind.Visible, Pair(engine, spatial, eye, seen));
        });
    }

    [Fact]
    public void Every_mobile_but_a_flyer_a_spectre_or_a_swimmer_walks_and_so_grounds_on_spawn()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ILookup<string, DaggerfallMobileDefinition> byBehaviour = definitions.Mobiles.Mobiles.Values.ToLookup(mobile => mobile.Behaviour);
        foreach (string behaviour in new[] { "Flying", "Spectral", "Aquatic" })
            Assert.All(byBehaviour[behaviour], mobile => Assert.False(mobile.Walks, mobile.DonorName));
        Assert.NotEmpty(byBehaviour["Aquatic"]);
        Assert.NotEmpty(byBehaviour["Flying"]);
        Assert.All(definitions.Mobiles.Mobiles.Values.Where(mobile => mobile.Behaviour is not ("Flying" or "Spectral" or "Aquatic")),
            mobile => Assert.True(mobile.Walks, mobile.DonorName));
        // The actors that carried a flag to ground on spawn walk by their mobile now, and one with no mobile does not.
        foreach (string actor in new[] { "rat", "orc", "skeletal-warrior", "thief", "archer", "centaur", "orc-shaman" })
            Assert.True(definitions.Walks(definitions.RequireActor(new DaggerfallActorId(actor))), actor);
        Assert.False(definitions.Walks(definitions.RequireActor(new DaggerfallActorId("imp"))));
        Assert.False(definitions.Walks(definitions.RequireActor(new DaggerfallActorId("player"))));
    }

    /// <summary>The rise's height at <paramref name="x"/>: its ridge runs along Z at x 0, falling to the plain 3 m either side.</summary>
    private static float Surface(float x) => Rise * (1F - MathF.Abs(x) / 3F);

    private static PerceptionPairKind Pair(IEngineContext engine, SpatialMovementSystem spatial, Vector3 from, Vector3 to)
    {
        Vector3 forward = Vector3.Normalize(to - from);
        PerceptionReadoutResult receipt = engine.Perception.QueryVisibility(new PerceptionQueryRequest(spatial.Session,
            new[] { new PerceptionObserver(1, from, forward, 10d, -1d, 1d) }, new[] { new PerceptionTarget(2, to) },
            ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 8));
        return Assert.Single(receipt.Pairs.ToArray()).Kind;
    }
}
