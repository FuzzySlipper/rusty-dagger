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
            ExactTrack track = Mechanics.ReadTrack(TrackId.Parse(_defeatTrack));
            return track.Current <= track.Bounds.Minimum;
        }
    }
    public void Dispose() => Mechanics.Dispose();
}

public sealed class ActorState(long entityId, ActorMechanicsState mechanics, WorldPoint position, string defeatTrack) : IDisposable
{
    private readonly string _defeatTrack = defeatTrack;

    public long EntityId { get; } = entityId;
    public ActorMechanicsState Mechanics { get; } = mechanics;
    public WorldPoint Position { get; } = position;
    public bool IsDefeated
    {
        get
        {
            ExactTrack track = Mechanics.ReadTrack(TrackId.Parse(_defeatTrack));
            return track.Current <= track.Bounds.Minimum;
        }
    }
    public void Dispose() => Mechanics.Dispose();
}

/// <summary>
/// Product-owned exact stat and track state for one actor. The Engine SDK
/// supplies the checked value/evaluation primitives; this class owns which
/// identities and bases make up an actor and when tracks are changed.
/// </summary>
public sealed class ActorMechanicsState : IDisposable
{
    private readonly Dictionary<StatId, StatEntry> _stats;
    private readonly Dictionary<TrackId, ExactTrack> _tracks;
    private bool _disposed;

    public ActorMechanicsState(
        EntityId entity,
        IEnumerable<(ExactStatDefinition Definition, ExactValue Base)> stats,
        IEnumerable<ExactTrack> tracks)
    {
        if (entity.Value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entity), "Actor entities must be non-zero.");
        }

        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(tracks);
        Entity = entity;
        _stats = [];
        foreach ((ExactStatDefinition definition, ExactValue baseValue) in stats)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!_stats.TryAdd(definition.Id, new StatEntry(definition, baseValue)))
            {
                throw new ArgumentException($"Actor {entity.Value} defines stat {definition.Id} more than once.", nameof(stats));
            }
        }

        _tracks = [];
        foreach (ExactTrack track in tracks)
        {
            ArgumentNullException.ThrowIfNull(track);
            if (!_tracks.TryAdd(track.Definition.Id, track))
            {
                throw new ArgumentException($"Actor {entity.Value} defines track {track.Definition.Id} more than once.", nameof(tracks));
            }
        }
    }

    public EntityId Entity { get; }

    public ExactStatEvaluation ReadStat(StatId stat)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(stat);
        if (!_stats.TryGetValue(stat, out StatEntry? entry))
        {
            throw new MechanicsException($"Actor {Entity.Value} does not define stat {stat}.");
        }

        return ExactStatEvaluator.Evaluate(entry.Definition, entry.Base, Array.Empty<ExactSource>());
    }

    public ExactTrack ReadTrack(TrackId track)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(track);
        return _tracks.TryGetValue(track, out ExactTrack? value)
            ? value
            : throw new MechanicsException($"Actor {Entity.Value} does not define track {track}.");
    }

    public ExactTrackMutationReceipt SpendTrack(TrackId track, ExactValue amount) => ReadTrack(track).Spend(amount);

    public ExactTrackMutationReceipt RestoreTrack(TrackId track, ExactValue amount) => ReadTrack(track).Restore(amount);

    public ExactTrackSetReceipt SetTrack(
        TrackId track,
        ExactValue value,
        ExactTrackSetPolicy policy = ExactTrackSetPolicy.RejectOutOfBounds) => ReadTrack(track).Set(value, policy);

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

    private sealed record StatEntry(ExactStatDefinition Definition, ExactValue Base);
}
