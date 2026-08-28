using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Modules.PlayerControl;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Actors;

/// <summary>Generic actor lifecycle and mechanics bindings. Product catalogs choose identities, stats, tracks, and combat policy.</summary>
internal sealed class ActorsState : IDisposable
{
    private readonly Dictionary<long, ActorState> _actors;

    internal ActorsState(PlayerActorState player, IEnumerable<ActorState> actors)
    {
        Player = player;
        _actors = actors.ToDictionary(actor => actor.EntityId);
    }

    internal PlayerActorState Player { get; }
    internal IReadOnlyDictionary<long, ActorState> All => _actors;
    internal bool TryGet(long entityId, out ActorState actor) => _actors.TryGetValue(entityId, out actor!);
    internal void AdvanceCooldowns(float deltaSeconds)
    {
        Player.AdvanceCooldown(deltaSeconds);
        foreach (ActorState actor in _actors.Values) actor.AdvanceCooldown(deltaSeconds);
    }

    public void Dispose()
    {
        Player.Dispose();
        foreach (ActorState actor in _actors.Values) actor.Dispose();
    }
}

internal interface IActorCombatant
{
    MechanicsEntity Mechanics { get; }
    float AttackCooldownSeconds { get; }
    bool IsDefeated { get; }
    void BeginAttack(float cooldownSeconds);
}

internal sealed class PlayerActorState(MechanicsEntity mechanics, string defeatTrack, IMechanicsService mechanicsService) : IActorCombatant, IDisposable
{
    private const string LifecycleReadOperation = "actor_lifecycle";
    private readonly IMechanicsService _mechanicsService = mechanicsService;
    private readonly string _defeatTrack = defeatTrack;

    internal MechanicsEntity Mechanics { get; } = mechanics;
    internal float AttackCooldownSeconds { get; private set; }
    internal bool IsDefeated
    {
        get
        {
            MechanicsTrackReadLeaseReceipt track = _mechanicsService.ReadTrack(new MechanicsTrackReadRequest(Mechanics, _defeatTrack, LifecycleReadOperation));
            return track.Current <= track.Minimum;
        }
    }
    internal void BeginAttack(float cooldownSeconds) => AttackCooldownSeconds = cooldownSeconds;
    internal void AdvanceCooldown(float deltaSeconds) => AttackCooldownSeconds = Math.Max(0f, AttackCooldownSeconds - deltaSeconds);
    MechanicsEntity IActorCombatant.Mechanics => Mechanics;
    float IActorCombatant.AttackCooldownSeconds => AttackCooldownSeconds;
    bool IActorCombatant.IsDefeated => IsDefeated;
    void IActorCombatant.BeginAttack(float cooldownSeconds) => BeginAttack(cooldownSeconds);
    public void Dispose() => Mechanics.Dispose();
}

internal sealed class ActorState(long entityId, MechanicsEntity mechanics, WorldPoint position, string defeatTrack, IMechanicsService mechanicsService) : IActorCombatant, IDisposable
{
    private const string LifecycleReadOperation = "actor_lifecycle";
    private readonly string _defeatTrack = defeatTrack;
    private readonly IMechanicsService _mechanicsService = mechanicsService;

    internal long EntityId { get; } = entityId;
    internal MechanicsEntity Mechanics { get; } = mechanics;
    internal WorldPoint Position { get; } = position;
    internal float AttackCooldownSeconds { get; private set; }
    internal bool IsDefeated
    {
        get
        {
            MechanicsTrackReadLeaseReceipt track = _mechanicsService.ReadTrack(new MechanicsTrackReadRequest(Mechanics, _defeatTrack, LifecycleReadOperation));
            return track.Current <= track.Minimum;
        }
    }
    internal void BeginAttack(float cooldownSeconds) => AttackCooldownSeconds = cooldownSeconds;
    internal void AdvanceCooldown(float deltaSeconds) => AttackCooldownSeconds = Math.Max(0f, AttackCooldownSeconds - deltaSeconds);
    MechanicsEntity IActorCombatant.Mechanics => Mechanics;
    float IActorCombatant.AttackCooldownSeconds => AttackCooldownSeconds;
    bool IActorCombatant.IsDefeated => IsDefeated;
    void IActorCombatant.BeginAttack(float cooldownSeconds) => BeginAttack(cooldownSeconds);
    public void Dispose() => Mechanics.Dispose();
}
