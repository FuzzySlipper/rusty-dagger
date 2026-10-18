using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class ActorContactTests
{
    private static readonly ContentSha256 Hash = new(1, 2, 3, 4);
    private static readonly ActorBodyEnvelope Body = new(.3f, 1.8f);
    private static readonly Vector3 Chest = new(0f, 1f, 0f);

    [Fact]
    public void A_segment_contacts_the_nearest_actor_body_first_and_passes_defeated_bodies()
    {
        using Scene scene = new();
        SegmentContact first = scene.Contact.Segment(1, Chest, Chest + new Vector3(10f, 0f, 0f));
        Assert.Equal((ContactKind.Actor, (long?)2), (first.Kind, first.ActorId));
        Assert.Equal(1.7d, first.Distance, 3);

        scene.Actors.Get(2).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        SegmentContact past = scene.Contact.Segment(1, Chest, Chest + new Vector3(10f, 0f, 0f));
        Assert.Equal((ContactKind.Actor, (long?)3), (past.Kind, past.ActorId));
    }

    [Fact]
    public void A_segment_ignores_its_own_source_body()
    {
        using Scene scene = new();
        Vector3 inside = new(2f, 1f, 0f);
        SegmentContact contact = scene.Contact.Segment(2, inside, inside + new Vector3(10f, 0f, 0f));
        Assert.Equal((ContactKind.Actor, (long?)3), (contact.Kind, contact.ActorId));
        Assert.DoesNotContain(scene.Spatial.Requests[^1].Entities.ToArray(), collider => collider.Entity == scene.Entity(2));
    }

    [Fact]
    public void World_geometry_before_a_body_occludes_it()
    {
        using Scene scene = new() { WallAt = 1f };
        SegmentContact contact = scene.Contact.Segment(1, Chest, Chest + new Vector3(10f, 0f, 0f));
        Assert.Equal((ContactKind.World, (long?)null), (contact.Kind, contact.ActorId));
        Assert.Equal(1d, contact.Distance, 3);
    }

    [Fact]
    public void A_step_that_reaches_nothing_reports_no_contact_so_its_projectile_keeps_flying()
    {
        using Scene scene = new();
        Assert.Equal(SegmentContact.None, scene.Contact.Segment(1, Chest, Chest + new Vector3(1f, 0f, 0f)));
        Assert.Equal((ContactKind.Actor, (long?)2), (scene.Contact.Segment(1, Chest + new Vector3(1f, 0f, 0f), Chest + new Vector3(2f, 0f, 0f)) is var next ? (next.Kind, next.ActorId) : default));
    }

    [Fact]
    public void An_ineligible_actor_presents_no_body_and_an_unknown_entity_is_world()
    {
        using Scene scene = new(eligible: id => id != 2);
        SegmentContact contact = scene.Contact.Segment(1, Chest, Chest + new Vector3(10f, 0f, 0f));
        Assert.Equal((ContactKind.Actor, (long?)3), (contact.Kind, contact.ActorId));
        Assert.Null(scene.Contact.ActorFor(new SpatialHit(true, SpatialHitKind.Entity, 999, 0, 0, 0, 0, 0, 0, default,
            Vector3.Zero, Vector3.UnitY, 1d, 0d, 0d, false, true)));
    }

    [Fact]
    public void An_enemy_segment_contacts_the_player_through_the_engine_character_shape()
    {
        using Scene scene = new();
        Vector3 from = new(4f, 1f, 0f);
        SegmentContact contact = scene.Contact.Segment(3, from, new Vector3(-1f, 1f, 0f));
        Assert.Equal((ContactKind.Actor, (long?)2), (contact.Kind, contact.ActorId)); // Actor 2 stands between.
        scene.Actors.Get(2).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        Assert.Equal((ContactKind.Actor, (long?)1), scene.Contact.Segment(3, from, new Vector3(-1f, 1f, 0f)) is var hit ? (hit.Kind, hit.ActorId) : default);
    }

    [Fact]
    public void A_contact_segment_needs_a_positive_length_and_the_body_needs_positive_dimensions()
    {
        using Scene scene = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.Contact.Segment(1, Chest, Chest));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActorBodyEnvelope(0f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActorBodyEnvelope(.3f, float.NaN));
    }

    private sealed class Scene : IDisposable
    {
        internal ActorsState Actors { get; } = new();
        internal SpatialDouble Spatial { get; } = SpatialDouble.Create();
        internal ActorContact Contact { get; }
        internal float? WallAt { get => Spatial.WallAt; init => Spatial.WallAt = value; }
        private readonly SpatialMovementSystem _movement;
        private readonly PlayerControlState _player = new(new WorldPoint(0f, 1f, 0f), 0f, 0f);

        internal Scene(Func<long, bool>? eligible = null)
        {
            Actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health");
            Actors.CreateActor(2, new EntityTypeId("near"), Stats(), new ActorPose(new WorldPoint(2, 0, 0), 0), "health");
            Actors.CreateActor(3, new EntityTypeId("far"), Stats(), new ActorPose(new WorldPoint(4, 0, 0), 0), "health");
            _movement = new SpatialMovementSystem(Spatial.Service, ContentDouble.Create().Service,
                new SpatialContentArtifact("spatial/test", Hash, 1), new SpatialTuning(.5, 8, 8, 1));
            Contact = new ActorContact(Actors, _movement, () => _player, Body, eligible ?? (_ => true));
        }
        internal ulong Entity(long id) => Actors.Get(id).Actor.Entity.Value;
        public void Dispose() { _movement.Dispose(); Actors.Dispose(); }
    }

    private static StatsComponent Stats()
    {
        Stat maximum = new(100);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse("health-maximum"), maximum);
        stats.AddTrack(TrackId.Parse("health"), new Track(maximum, 100));
        return stats;
    }

    private class ContentDouble : DispatchProxy
    {
        public IContentService Service { get; private set; } = null!;
        public static ContentDouble Create()
        {
            IContentService service = DispatchProxy.Create<IContentService, ContentDouble>();
            ContentDouble result = (ContentDouble)(object)service;
            result.Service = service;
            return result;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IContentService.ResolveReference)
            ? new ContentReference(new ContentReferenceHandle(1), static () => { })
            : throw new NotSupportedException(method?.Name);
    }

    /// <summary>Answers rays with the nearest caller-supplied box along them, or a wall at a fixed distance.</summary>
    internal class SpatialDouble : DispatchProxy
    {
        public ISpatialService Service { get; private set; } = null!;
        public List<SpatialRaycastRequest> Requests { get; } = [];
        public float? WallAt { get; set; }
        public static SpatialDouble Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialDouble>();
            SpatialDouble result = (SpatialDouble)(object)service;
            result.Service = service;
            return result;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(ISpatialService.DefaultCharacterControllerConfig) => default(CharacterControllerConfig),
            nameof(ISpatialService.ValidateCharacterControllerConfig) => null,
            nameof(ISpatialService.CreateSession) => new SpatialSession(new SpatialSessionHandle(1), static () => { }),
            nameof(ISpatialService.ReplaceContentArtifact) => new SpatialContentArtifactReplaceReceipt(),
            nameof(ISpatialService.CastRay) => Cast((SpatialRaycastRequest)arguments![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private SpatialHit Cast(SpatialRaycastRequest request)
        {
            Requests.Add(request);
            double best = WallAt is float wall && wall <= request.MaxDistance ? wall : double.PositiveInfinity;
            SpatialHitKind kind = double.IsFinite(best) ? SpatialHitKind.StaticMesh : SpatialHitKind.None;
            ulong entity = 0;
            foreach (SpatialEntityCollider collider in request.Entities.Span)
                if (Enter(request.Origin, request.Direction, collider.Min, collider.Max) is double t && t <= request.MaxDistance && t < best)
                { best = t; kind = SpatialHitKind.Entity; entity = collider.Entity; }
            if (kind == SpatialHitKind.None) return default;
            return new SpatialHit(true, kind, entity, 0, 0, 0, 0, 0, 0, default,
                request.Origin + request.Direction * (float)best, -request.Direction, best, 0d, 0d, false, true);
        }

        /// <summary>Where a ray first enters a box, ignoring a box it starts inside the way a cast from a body leaves it.</summary>
        private static double? Enter(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max)
        {
            double near = double.NegativeInfinity, far = double.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                double o = origin[axis], d = direction[axis], lo = min[axis], hi = max[axis];
                if (Math.Abs(d) < 1e-9) { if (o < lo || o > hi) return null; continue; }
                double a = (lo - o) / d, b = (hi - o) / d;
                near = Math.Max(near, Math.Min(a, b));
                far = Math.Min(far, Math.Max(a, b));
            }
            return near <= far && near > 0d ? near : null;
        }
    }
}
