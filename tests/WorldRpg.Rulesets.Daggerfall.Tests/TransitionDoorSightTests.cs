using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Interaction;
using Rusty.Engine.Testing;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Targeting;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A transition door is a plane set into the static wall it leads through, so its portal's position lies on
/// collision Engine line of sight stops at. These run the real Engine perception over the shipped collision:
/// each portal is sighted off its plane from the side it is used from, and hidden from behind.
/// </summary>
public sealed class TransitionDoorSightTests
{
    /// <summary>How far in front of (or behind) a door the player stands, at the door's centre height.</summary>
    private const float Standoff = .75F;

    [Fact]
    public void A_new_game_can_target_the_privateers_hold_exit_from_the_room_and_not_through_its_wall()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile hold = ReadInputs(root);
        Dictionary<string, ReadOnlyMemory<byte>> files = PublishedUiArt(root)
            .ToDictionary(entry => entry.Path, entry => (ReadOnlyMemory<byte>)entry.Bytes);
        files[hold.SpatialArtifact.Path] = File.ReadAllBytes(Path.Combine(root, "content", hold.SpatialArtifact.Path));
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = files });
        host.Call(engine =>
        {
            EngineContextFake context = EngineContextFake.Create(engine.Content, engine.Spatial, new AppearanceFake([]), engine.Perception,
                worldOrigin: engine.WorldOrigin);
            using DaggerfallSession session = DaggerfallSession.StartNew(context.Context, new(TestPayload.Definitions, hold, DaggerfallTuning.Defaults));
            (DaggerfallSitePortal exit, DurableIdentityReference _, EntityId entity) = Assert.Single(session.Sites.Projection.Portals.All);
            Vector3 normal = Assert.NotNull(exit.Normal);

            // Its point lies on the wall (the theory below holds Engine sight to that point stopped there), yet the
            // ordinary activation inspection finds it ready from the room and hidden from behind the wall.
            Vector3 front = exit.Position.ToVector() + (normal * Standoff);
            Assert.Equal((InteractionVisibility.Visible, InteractionReason.Ready), Inspect(session, front, -normal, entity));
            Vector3 behind = exit.Position.ToVector() - (normal * Standoff);
            Assert.Equal(InteractionVisibility.Occluded, Inspect(session, behind, normal, entity).Visibility);
        });
    }

    /// <summary>
    /// Every authored source exit (and so each assembled portal on the same door) is in sight from its room.
    /// <paramref name="wallThroughDoor"/> marks a closure whose collision has a wall in the door's own plane, which
    /// stops Engine sight at the door's centre: the playtested case of Privateer's Hold.
    /// </summary>
    [Theory]
    [InlineData("daggerfall.privateers-hold.json", true)]
    [InlineData("daggerfall.castle-necromoghan.json", false)]
    [InlineData("daggerfall.charing-interior-1-5-17.json", false)]
    [InlineData("daggerfall.charing-interior-2-1-0.json", false)]
    [InlineData("daggerfall.charing-interior-3-1-13.json", false)]
    [InlineData("daggerfall.charing-interior-3-2-14.json", false)]
    [InlineData("daggerfall.charing-interior-3-4-0.json", false)]
    [InlineData("daggerfall.charing-interior-4-2-0.json", false)]
    public void Each_authored_source_exit_is_sighted_from_its_open_side(string payload, bool wallThroughDoor)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile profile = ReadProfile(root, FullContent(root), TestPayload.Definitions, payload);
        Assert.NotEmpty(profile.Portals);
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = new Dictionary<string, ReadOnlyMemory<byte>>
        {
            [profile.SpatialArtifact.Path] = File.ReadAllBytes(Path.Combine(root, "content", profile.SpatialArtifact.Path)),
        } });
        host.Call(engine =>
        {
            using SpatialMovementSystem spatial = new(engine.Spatial, engine.Content, profile.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
            foreach (DaggerfallSitePortal portal in profile.Portals)
            {
                Vector3 normal = Assert.NotNull(portal.Normal);
                Vector3 sight = new InteractionTargetCandidate(new EntityId(1), new DurableIdentityReference(DurableIdentityKind.Resource, 1), 1,
                    portal.Position, 0, SurfaceNormal: normal).SightPoint;
                Vector3 front = portal.Position.ToVector() + (normal * Standoff);
                PerceptionReadoutResult result = engine.Perception.QueryVisibility(new(spatial.Session,
                    new[] { new PerceptionObserver(1, front, -normal, 4, -1, 1) },
                    new[] { new PerceptionTarget(2, sight), new PerceptionTarget(3, portal.Position.ToVector()) },
                    ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 0, 64));
                PerceptionPair[] pairs = result.Pairs.ToArray();
                Assert.True(pairs.Single(pair => pair.Target == 2).Kind == PerceptionPairKind.Visible, $"{payload} {portal.Id} is hidden from its room.");
                if (wallThroughDoor) Assert.Equal(PerceptionPairKind.Occluded, pairs.Single(pair => pair.Target == 3).Kind);
            }
        });
    }

    private static (InteractionVisibility Visibility, InteractionReason Reason) Inspect(DaggerfallSession session, Vector3 at, Vector3 facing, EntityId entity)
    {
        session.State.PlayerControl.MoveTo(at);
        session.State.PlayerControl.YawRadians = ActorHeading.Yaw(facing);
        session.State.PlayerControl.PitchRadians = 0F;
        WorldInteractionReadout readout = session.CreateInteractionInspection().Inspect();
        InteractionObservation row = readout.Focus.Candidates.Single(candidate => candidate.Candidate.Target.Id == entity.Value);
        return (row.Candidate.Visibility, row.Reason);
    }
}
