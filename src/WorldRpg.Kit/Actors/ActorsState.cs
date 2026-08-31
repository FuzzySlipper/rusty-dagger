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
            ActorTrackRead track = Mechanics.ReadTrack(TrackId.Parse(_defeatTrack));
            return track.Current <= track.Bounds.Minimum;
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
            ActorTrackRead track = Mechanics.ReadTrack(TrackId.Parse(_defeatTrack));
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
public readonly record struct ActorTrackRead(ExactValue Current, ExactTrackBounds Bounds);

public sealed class ActorMechanicsState : IDisposable
{
    private readonly Dictionary<StatId, StatEntry> _stats;
    private readonly Dictionary<TrackId, ExactTrack> _tracks;
    private readonly Dictionary<StatId, ExactStatTrackState> _pairedStats;
    private readonly Dictionary<TrackId, ExactStatTrackState> _pairedTracks;
    private bool _disposed;

    public ActorMechanicsState(
        EntityId entity,
        IEnumerable<(ExactStatDefinition Definition, ExactValue Base)> stats,
        IEnumerable<ExactTrack> tracks,
        IEnumerable<ExactStatTrackState>? statTracks = null)
    {
        if (entity.Value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entity), "Actor entities must be non-zero.");
        }

        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(tracks);
        statTracks ??= Array.Empty<ExactStatTrackState>();
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

        _pairedStats = [];
        _pairedTracks = [];
        foreach (ExactStatTrackState pair in statTracks)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (_stats.ContainsKey(pair.StatDefinition.Id) || !_pairedStats.TryAdd(pair.StatDefinition.Id, pair))
            {
                throw new ArgumentException($"Actor {entity.Value} defines stat {pair.StatDefinition.Id} more than once.", nameof(statTracks));
            }

            if (_tracks.ContainsKey(pair.TrackDefinition.Id) || !_pairedTracks.TryAdd(pair.TrackDefinition.Id, pair))
            {
                throw new ArgumentException($"Actor {entity.Value} defines track {pair.TrackDefinition.Id} more than once.", nameof(statTracks));
            }
        }
    }

    public EntityId Entity { get; }

    public ExactStatEvaluation ReadStat(StatId stat)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(stat);
        if (_pairedStats.TryGetValue(stat, out ExactStatTrackState? pair))
        {
            return pair.Read().Stat;
        }

        if (!_stats.TryGetValue(stat, out StatEntry? entry))
        {
            throw new MechanicsException($"Actor {Entity.Value} does not define stat {stat}.");
        }

        return ExactStatEvaluator.Evaluate(entry.Definition, entry.Base, Array.Empty<ExactSource>());
    }

    public ActorTrackRead ReadTrack(TrackId track)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(track);
        if (_pairedTracks.TryGetValue(track, out ExactStatTrackState? pair))
        {
            ExactStatTrackSnapshot snapshot = pair.Read();
            return new ActorTrackRead(snapshot.TrackCurrent, snapshot.TrackBounds);
        }

        if (_tracks.TryGetValue(track, out ExactTrack? value))
        {
            return new ActorTrackRead(value.Current, value.Bounds);
        }

        throw new MechanicsException($"Actor {Entity.Value} does not define track {track}.");
    }

    /// <summary>Returns the Engine-owned pair for a declared dependent track.</summary>
    public ExactStatTrackState ReadStatTrack(TrackId track)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(track);
        return _pairedTracks.TryGetValue(track, out ExactStatTrackState? pair)
            ? pair
            : throw new MechanicsException($"Actor {Entity.Value} does not define a stat-track pair for {track}.");
    }

    public ExactTrackMutationReceipt SpendTrack(TrackId track, ExactValue amount)
    {
        ThrowIfDisposed();
        if (_pairedTracks.TryGetValue(track, out ExactStatTrackState? pair))
        {
            ExactStatTrackCurrentMutationReceipt receipt = pair.Spend(amount);
            return ToTrackMutationReceipt(receipt, isSpend: true);
        }

        return RequireTrack(track).Spend(amount);
    }

    public ExactTrackMutationReceipt RestoreTrack(TrackId track, ExactValue amount)
    {
        ThrowIfDisposed();
        if (_pairedTracks.TryGetValue(track, out ExactStatTrackState? pair))
        {
            ExactStatTrackCurrentMutationReceipt receipt = pair.Restore(amount);
            return ToTrackMutationReceipt(receipt, isSpend: false);
        }

        return RequireTrack(track).Restore(amount);
    }

    public ExactTrackSetReceipt SetTrack(
        TrackId track,
        ExactValue value,
        ExactTrackSetPolicy policy = ExactTrackSetPolicy.RejectOutOfBounds)
    {
        ThrowIfDisposed();
        if (_pairedTracks.TryGetValue(track, out ExactStatTrackState? pair))
        {
            ExactStatTrackCurrentMutationReceipt receipt = pair.SetCurrent(value, policy);
            ExactStatTrackCurrentMutation.Set set = (ExactStatTrackCurrentMutation.Set)receipt.Mutation;
            return new ExactTrackSetReceipt(
                set.Requested,
                receipt.Before.TrackCurrent,
                receipt.After.TrackCurrent,
                receipt.After.TrackBounds,
                set.Policy);
        }

        return RequireTrack(track).Set(value, policy);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stats.Clear();
        _tracks.Clear();
        _pairedStats.Clear();
        _pairedTracks.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ActorMechanicsState));
    }

    private ExactTrack RequireTrack(TrackId track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return _tracks.TryGetValue(track, out ExactTrack? value)
            ? value
            : throw new MechanicsException($"Actor {Entity.Value} does not define track {track}.");
    }

    private static ExactTrackMutationReceipt ToTrackMutationReceipt(
        ExactStatTrackCurrentMutationReceipt receipt,
        bool isSpend)
    {
        return receipt.Mutation switch
        {
            ExactStatTrackCurrentMutation.Spend spend => new ExactTrackMutationReceipt(
                spend.RequestedAmount,
                spend.AppliedAmount,
                receipt.Before.TrackCurrent,
                receipt.After.TrackCurrent,
                receipt.After.TrackBounds,
                isSpend),
            ExactStatTrackCurrentMutation.Restore restore => new ExactTrackMutationReceipt(
                restore.RequestedAmount,
                restore.AppliedAmount,
                receipt.Before.TrackCurrent,
                receipt.After.TrackCurrent,
                receipt.After.TrackBounds,
                isSpend),
            _ => throw new MechanicsException("Unexpected exact stat-track current mutation."),
        };
    }

    private sealed record StatEntry(ExactStatDefinition Definition, ExactValue Base);
}
