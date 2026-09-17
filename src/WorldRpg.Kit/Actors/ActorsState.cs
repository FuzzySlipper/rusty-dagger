using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
namespace WorldRpg.Kit.Actors;

/// <summary>Generic actor lifecycle and mechanics bindings. Product catalogs choose identities, stats, tracks, and combat policy.</summary>
public sealed class ActorsState : IDisposable
{
    private readonly Dictionary<long, ActorState> _actors;

    public ActorsState(PlayerActorState player, IEnumerable<ActorState> actors)
    {
        Player = player;
        _actors = actors.ToDictionary(actor => actor.EntityId);
    }

    public PlayerActorState Player { get; }
    public IReadOnlyDictionary<long, ActorState> All => _actors;
    public bool TryGet(long entityId, out ActorState actor) => _actors.TryGetValue(entityId, out actor!);
    public void Dispose()
    {
        Player.Dispose();
        foreach (ActorState actor in _actors.Values) actor.Dispose();
    }
}

public sealed class PlayerActorState(ActorMechanicsState mechanics, string defeatTrack) : IDisposable
{
    private readonly string _defeatTrack = defeatTrack;

    public ActorMechanicsState Mechanics { get; } = mechanics;
    public long EntityId => checked((long)Mechanics.Entity.Value);
    public bool IsDefeated
    {
        get
        {
            Track track = Mechanics.ReadTrack(TrackId.Parse(_defeatTrack));
            return track.Current <= track.Minimum;
        }
    }
    public void Dispose() => Mechanics.Dispose();
}

/// <summary>
/// Authoritative actor placement. Heading follows the Engine world convention:
/// zero faces negative Z and positive yaw turns toward positive X.
/// </summary>
public readonly record struct ActorPose
{
    public ActorPose(WorldPoint position, float headingYawRadians)
    {
        position.Validate();
        if (!float.IsFinite(headingYawRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(headingYawRadians));
        }

        Position = position;
        HeadingYawRadians = headingYawRadians;
    }

    public WorldPoint Position { get; }
    public float HeadingYawRadians { get; }
}

public sealed class ActorState : IDisposable
{
    private readonly string _defeatTrack;

    public ActorState(long entityId, ActorMechanicsState mechanics, WorldPoint position, string defeatTrack)
        : this(entityId, mechanics, new ActorPose(position, 0f), defeatTrack)
    {
    }

    public ActorState(long entityId, ActorMechanicsState mechanics, ActorPose pose, string defeatTrack)
    {
        ArgumentNullException.ThrowIfNull(mechanics);
        ArgumentException.ThrowIfNullOrWhiteSpace(defeatTrack);
        EntityId = entityId;
        Mechanics = mechanics;
        Pose = pose;
        _defeatTrack = defeatTrack;
    }

    public long EntityId { get; }
    public ActorMechanicsState Mechanics { get; }
    public ActorPose Pose { get; private set; }
    /// <summary>Compatibility accessor for callers which only require placement.</summary>
    public WorldPoint Position => Pose.Position;
    /// <summary>Compatibility accessor for callers which use the actor's yaw heading.</summary>
    public float Heading => Pose.HeadingYawRadians;
    public float HeadingYawRadians => Pose.HeadingYawRadians;

    /// <summary>Applies an already Engine-admitted pose; navigation and policy stay outside actor lifetime ownership.</summary>
    public void ApplyPose(ActorPose pose) => Pose = pose;

    public bool IsDefeated
    {
        get
        {
            Track track = Mechanics.ReadTrack(TrackId.Parse(_defeatTrack));
            return track.Current <= track.Minimum;
        }
    }
    public void Dispose() => Mechanics.Dispose();
}

/// <summary>
/// Product-owned actor mechanics identities. The stored values are the Engine
/// Stat and Track instances themselves, so each value has one authority.
/// </summary>
public sealed class ActorMechanicsState : IDisposable
{
    private readonly Dictionary<StatId, Stat> _stats;
    private readonly Dictionary<TrackId, Track> _tracks;
    private bool _disposed;

    public ActorMechanicsState(
        EntityId entity,
        IEnumerable<(StatId Id, Stat Value)> stats,
        IEnumerable<(TrackId Id, Track Value)> tracks)
    {
        if (entity.Value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entity), "Actor entities must be non-zero.");
        }

        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(tracks);
        Entity = entity;
        _stats = [];
        foreach ((StatId id, Stat value) in stats)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(value);
            if (!_stats.TryAdd(id, value))
            {
                throw new ArgumentException($"Actor {entity.Value} defines stat {id} more than once.", nameof(stats));
            }
        }

        _tracks = [];
        foreach ((TrackId id, Track value) in tracks)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(value);
            if (!_tracks.TryAdd(id, value))
            {
                throw new ArgumentException($"Actor {entity.Value} defines track {id} more than once.", nameof(tracks));
            }
        }
    }

    public EntityId Entity { get; }

    public Stat ReadStat(StatId stat)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(stat);
        return _stats.TryGetValue(stat, out Stat? value)
            ? value
            : throw new MechanicsException($"Actor {Entity.Value} does not define stat {stat}.");
    }

    public Track ReadTrack(TrackId track)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(track);
        return _tracks.TryGetValue(track, out Track? value)
            ? value
            : throw new MechanicsException($"Actor {Entity.Value} does not define track {track}.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stats.Clear();
        _tracks.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ActorMechanicsState));
    }

}
