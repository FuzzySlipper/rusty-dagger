using Rusty.Engine;
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

public sealed class PlayerActorState(MechanicsEntity mechanics, string defeatTrack, IMechanicsService mechanicsService) : IDisposable
{
    private const string LifecycleReadOperation = "actor_lifecycle";
    private readonly IMechanicsService _mechanicsService = mechanicsService;
    private readonly string _defeatTrack = defeatTrack;

    public MechanicsEntity Mechanics { get; } = mechanics;
    public bool IsDefeated
    {
        get
        {
            MechanicsTrackReadLeaseReceipt track = _mechanicsService.ReadTrack(new MechanicsTrackReadRequest(Mechanics, _defeatTrack, LifecycleReadOperation));
            return track.Current <= track.Minimum;
        }
    }
    public void Dispose() => Mechanics.Dispose();
}

public sealed class ActorState(long entityId, MechanicsEntity mechanics, WorldPoint position, string defeatTrack, IMechanicsService mechanicsService) : IDisposable
{
    private const string LifecycleReadOperation = "actor_lifecycle";
    private readonly string _defeatTrack = defeatTrack;
    private readonly IMechanicsService _mechanicsService = mechanicsService;

    public long EntityId { get; } = entityId;
    public MechanicsEntity Mechanics { get; } = mechanics;
    public WorldPoint Position { get; } = position;
    public bool IsDefeated
    {
        get
        {
            MechanicsTrackReadLeaseReceipt track = _mechanicsService.ReadTrack(new MechanicsTrackReadRequest(Mechanics, _defeatTrack, LifecycleReadOperation));
            return track.Current <= track.Minimum;
        }
    }
    public void Dispose() => Mechanics.Dispose();
}
