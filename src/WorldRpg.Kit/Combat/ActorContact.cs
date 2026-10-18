using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Kit.Combat;

/// <summary>The body a site actor presents to contact queries: an upright box around its canonical feet position.</summary>
public readonly record struct ActorBodyEnvelope
{
    public ActorBodyEnvelope(float halfWidth, float height)
    {
        if (!float.IsFinite(halfWidth) || halfWidth <= 0f) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        if (!float.IsFinite(height) || height <= 0f) throw new ArgumentOutOfRangeException(nameof(height));
        HalfWidth = halfWidth;
        Height = height;
    }
    public float HalfWidth { get; }
    public float Height { get; }
}

/// <summary>What a delivery query touched first.</summary>
public enum ContactKind { None, World, Actor }

/// <summary>
/// The first contact along a delivery segment. <see cref="ContactKind.World"/> includes call-local
/// obstacles and entity bodies that are not live eligible actors; an actor contact names its durable id.
/// </summary>
public readonly record struct SegmentContact(ContactKind Kind, long? ActorId, Vector3 Point, double Distance)
{
    public static SegmentContact None { get; } = new(ContactKind.None, null, default, 0d);
}

/// <summary>
/// Harm delivery's physical contact queries over the admitted Engine scene. Actor bodies are query-local
/// colliders projected from canonical actor state for each call, never a second collision world: site
/// actors from their position and the ruleset's body envelope, the player from the Engine character
/// shape. The Engine answers every query.
/// </summary>
public sealed class ActorContact
{
    private readonly ActorsState _actors;
    private readonly SpatialMovementSystem _spatial;
    private readonly Func<PlayerControlState> _player;
    private readonly ActorBodyEnvelope _body;
    private readonly Func<long, bool> _eligible;

    /// <param name="eligible">Whether a live site actor takes part in contact; the player always does while alive.</param>
    public ActorContact(ActorsState actors, SpatialMovementSystem spatial, Func<PlayerControlState> player,
        ActorBodyEnvelope body, Func<long, bool> eligible)
    {
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _body = body;
        _eligible = eligible ?? throw new ArgumentNullException(nameof(eligible));
    }

    /// <summary>The bodies of every live eligible actor except <paramref name="excluded"/>, the query's own source.</summary>
    public SpatialEntityCollider[] Colliders(long? excluded)
    {
        Vector3 reach = new(_body.HalfWidth, 0f, _body.HalfWidth);
        Vector3 top = new(_body.HalfWidth, _body.Height, _body.HalfWidth);
        IEnumerable<SpatialEntityCollider> colliders = _actors.All
            .Where(actor => actor.DurableId != excluded && !actor.IsDefeated && _eligible(actor.DurableId))
            .Select(actor => new SpatialEntityCollider(actor.Actor.Entity.Value, actor.Position.ToVector() - reach,
                actor.Position.ToVector() + top, 0, 0, true, false, false));
        if (_actors.Player.DurableId != excluded && !_actors.Player.IsDefeated)
            colliders = colliders.Append(_spatial.ProjectCharacterCollider(_player(), _actors.Player.Actor.Entity.Value));
        return colliders.ToArray();
    }

    /// <summary>The live eligible actor an Engine hit names, or none for world geometry and other bodies.</summary>
    public long? ActorFor(SpatialHit hit)
    {
        if (!hit.Present || hit.Kind != SpatialHitKind.Entity) return null;
        if (_actors.Player.Actor.Entity.Value == hit.Entity) return _actors.Player.DurableId;
        return _actors.All.Where(actor => _eligible(actor.DurableId) && actor.Actor.Entity.Value == hit.Entity)
            .Select(actor => (long?)actor.DurableId).SingleOrDefault();
    }

    /// <summary>
    /// The first world surface or actor body a projectile meets moving from <paramref name="from"/> to
    /// <paramref name="to"/> during one admitted step, ignoring its own <paramref name="source"/>. A caller
    /// steps the same projectile segment by segment until it reports contact.
    /// </summary>
    public SegmentContact Segment(long? source, Vector3 from, Vector3 to, CharacterStepEnvironment? environment = null)
    {
        Vector3 delta = to - from;
        float distance = delta.Length();
        if (!float.IsFinite(distance) || distance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(to), "A contact segment needs a finite, positive length.");
        SpatialHit hit = _spatial.CastRay(from, delta / distance, distance, Colliders(source), environment);
        if (!hit.Present) return SegmentContact.None;
        long? actor = ActorFor(hit);
        return new(actor is null ? ContactKind.World : ContactKind.Actor, actor, hit.Point, hit.Distance);
    }
}
