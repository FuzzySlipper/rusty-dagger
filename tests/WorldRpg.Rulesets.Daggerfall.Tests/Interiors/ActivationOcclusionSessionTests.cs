using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// #11203: activation passes live actor body colliders into Engine visibility and checks the look ray so a nearer
/// hostile body standing between the player and an NPC blocks talking to the person behind it.
/// </summary>
public sealed class ActivationOcclusionSessionTests
{
    [Fact]
    public void A_hostile_standing_between_the_player_and_an_npc_blocks_talking_until_it_moves_or_falls()
    {
        List<string> releases = [];
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();

        // Mirror Engine's SpatialPerceptionService::evaluate_page and CastRay over caller-supplied SpatialEntityCollider boxes.
        perception.Responder = request =>
        {
            SpatialEntityCollider[] occluders = request.Occluders.ToArray();
            List<PerceptionPair> pairs = [];
            foreach (PerceptionObserver observer in request.Observers.Span)
            {
                foreach (PerceptionTarget target in request.Targets.Span)
                {
                    Vector3 delta = target.Center - observer.Origin;
                    float distance = delta.Length();
                    if (distance <= 0f || distance > observer.MaximumDistance) continue;
                    Vector3 dir = delta / distance;
                    double facing = Vector3.Dot(Vector3.Normalize(observer.Forward), dir);
                    if (facing < observer.MinimumFacingCosine)
                    {
                        pairs.Add(new(observer.Entity, target.Entity, distance, facing, PerceptionPairKind.FacingRejected, 0d));
                        continue;
                    }
                    bool occluded = false;
                    foreach (SpatialEntityCollider box in occluders)
                    {
                        if (box.Entity == observer.Entity || box.Entity == target.Entity) continue;
                        if (Enter(observer.Origin, dir, box.Min, box.Max) is double t && t <= distance)
                        {
                            occluded = true;
                            break;
                        }
                    }
                    pairs.Add(new(observer.Entity, target.Entity, distance, facing,
                        occluded ? PerceptionPairKind.Occluded : PerceptionPairKind.Visible, occluded ? 0d : 1d));
                }
            }
            return Receipt([.. pairs]);
        };

        spatial.FloorHit = request =>
        {
            if (MathF.Abs(request.Direction.Y) >= .9f) return default;
            double best = double.PositiveInfinity;
            ulong entity = 0;
            foreach (SpatialEntityCollider box in request.Entities.Span)
                if (Enter(request.Origin, request.Direction, box.Min, box.Max) is double t && t <= request.MaxDistance && t < best)
                {
                    best = t;
                    entity = box.Entity;
                }
            return double.IsFinite(best)
                ? new SpatialHit(true, SpatialHitKind.Entity, entity, 0, 0, 0, 0, 0, 0, default,
                    request.Origin + request.Direction * (float)best, -request.Direction, best, 0d, 0d, false, true)
                : default;
        };

        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));

        Vector3 playerPos = session.State.PlayerControl.Position!.Value.ToVector();
        Vector3 forward = -Vector3.UnitZ;
        LookReceipt look = new(default, default, Quaternion.Identity, forward, Vector3.UnitX, Vector3.UnitY);

        // Register a civilian NPC 2.1m directly ahead of the player.
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        long npcId = session.State.Npcs.RegisterCivilian(
            new(site.Id.Region, site.Name, string.Empty),
            new("breton", "Male", 0, 0, 0, 0),
            "civilian",
            ["talk"]);
        session.Civilians.Materialize(npcId, new ActorPose(WorldPoint.From(playerPos + forward * 2.1f), 0f));
        session.State.Npcs.Bind(npcId, session.Sites.ActiveProfile);

        // With nobody in between, activation focuses Theodore Copperwing ("Talk to").
        DaggerfallActivationTarget? clearTarget = session.Activation.Module!.FocusedTarget(
            session.State.Actors.Player.Actor.Entity, session.State.PlayerControl, look);
        Assert.NotNull(clearTarget);
        Assert.Equal(checked((ulong)npcId), clearTarget!.Value.QueryIdentity);
        Assert.Equal("Talk to", session.Activation.Module.PromptAction(clearTarget.Value));

        // Place a hostile centaur 0.7m directly ahead of the player, between the player and Theodore.
        long centaurId = session.SpawnActor("centaur", new ActorPose(WorldPoint.From(playerPos + forward * 0.7f), 0f), level: 5);
        DaggerfallActivationTarget? blockedTarget = session.Activation.Module.FocusedTarget(
            session.State.Actors.Player.Actor.Entity, session.State.PlayerControl, look);
        Assert.Null(blockedTarget);
        Assert.Contains(perception.Requests[^1].Occluders.ToArray(), box => box.Entity == checked((ulong)centaurId));

        DaggerfallActivationOutcome? blockedUse = session.Activation.Activate(look, out _);
        Assert.NotNull(blockedUse);
        Assert.False(blockedUse!.Applied);

        // Even if Theodore stands slightly off-axis (within the 60-degree cone) while the crosshair sits squarely on the centaur,
        // the nearer centaur on the look ray still blocks focusing Theodore.
        session.State.Actors.Get(npcId).ApplyPose(new ActorPose(WorldPoint.From(playerPos + forward * 2.0f + Vector3.UnitX * 0.85f), 0f));
        Assert.Null(session.Activation.Module.FocusedTarget(
            session.State.Actors.Player.Actor.Entity, session.State.PlayerControl, look));

        // Turning away from the centaur toward a clear NPC behind the player still focuses that NPC cleanly.
        long behindNpcId = session.State.Npcs.RegisterCivilian(
            new(site.Id.Region, site.Name, string.Empty),
            new("breton", "Female", 0, 0, 0, 0),
            "civilian",
            ["talk"]);
        session.Civilians.Materialize(behindNpcId, new ActorPose(WorldPoint.From(playerPos - forward * 1.5f), 0f));
        session.State.Npcs.Bind(behindNpcId, session.Sites.ActiveProfile);
        LookReceipt turnAround = new(default, default, Quaternion.Identity, -forward, -Vector3.UnitX, Vector3.UnitY);
        DaggerfallActivationTarget? behindTarget = session.Activation.Module.FocusedTarget(
            session.State.Actors.Player.Actor.Entity, session.State.PlayerControl, turnAround);
        Assert.NotNull(behindTarget);
        Assert.Equal(checked((ulong)behindNpcId), behindTarget!.Value.QueryIdentity);

        // Return Theodore directly behind the centaur and defeat the centaur -> his standing body no longer blocks talking.
        session.State.Actors.Get(npcId).ApplyPose(new ActorPose(WorldPoint.From(playerPos + forward * 2.1f), 0f));
        session.State.Actors.Get(centaurId).Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).SetCurrent(0, clamp: true);

        DaggerfallActivationTarget? afterFall = session.Activation.Module.FocusedTarget(
            session.State.Actors.Player.Actor.Entity, session.State.PlayerControl, look);
        Assert.NotNull(afterFall);
        Assert.Equal(checked((ulong)npcId), afterFall!.Value.QueryIdentity);
        Assert.DoesNotContain(perception.Requests[^1].Occluders.ToArray(), box => box.Entity == checked((ulong)centaurId));
    }

    private static double? Enter(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max)
    {
        double near = 0d, far = double.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            double o = origin[axis], d = direction[axis], lo = min[axis], hi = max[axis];
            if (Math.Abs(d) < 1e-9) { if (o < lo || o > hi) return null; continue; }
            double a = (lo - o) / d, b = (hi - o) / d;
            near = Math.Max(near, Math.Min(a, b));
            far = Math.Min(far, Math.Max(a, b));
        }
        return near <= far ? near : null;
    }
}
