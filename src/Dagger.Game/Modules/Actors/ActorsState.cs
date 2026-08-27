using Rusty.Engine;
using RustyDagger.Game.Modules.PlayerControl;

namespace RustyDagger.Game.Modules.Actors;

/// <summary>Generic actor lifecycle and mechanics bindings. Product catalogs choose identities, stats, tracks, and combat policy.</summary>
internal sealed class ActorsState : IDisposable
{
    private readonly Dictionary<long, ActorState> _actors;

    internal ActorsState(PlayerActorState player, IEnumerable<ActorSpawn> actors)
    {
        Player = player;
        _actors = actors.ToDictionary(actor => actor.EntityId, actor => new ActorState(actor.EntityId, actor.Mechanics, actor.Position));
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

internal sealed record ActorMechanicsBinding(MechanicsEntity Entity);
internal sealed record ActorSpawn(long EntityId, MechanicsEntity Mechanics, WorldPoint Position);

internal interface IActorCombatant
{
    MechanicsEntity Mechanics { get; }
    float AttackCooldownSeconds { get; }
    bool IsDefeated { get; }
    void BeginAttack(float cooldownSeconds);
    void RecordDefeat();
}

internal sealed class PlayerActorState(ActorMechanicsBinding mechanics) : IActorCombatant, IDisposable
{
    internal MechanicsEntity Mechanics { get; } = mechanics.Entity;
    internal float AttackCooldownSeconds { get; private set; }
    internal bool IsDefeated { get; private set; }
    internal void BeginAttack(float cooldownSeconds) => AttackCooldownSeconds = cooldownSeconds;
    internal void AdvanceCooldown(float deltaSeconds) => AttackCooldownSeconds = Math.Max(0f, AttackCooldownSeconds - deltaSeconds);
    internal void RecordDefeat() => IsDefeated = true;
    MechanicsEntity IActorCombatant.Mechanics => Mechanics;
    float IActorCombatant.AttackCooldownSeconds => AttackCooldownSeconds;
    bool IActorCombatant.IsDefeated => IsDefeated;
    void IActorCombatant.BeginAttack(float cooldownSeconds) => BeginAttack(cooldownSeconds);
    void IActorCombatant.RecordDefeat() => RecordDefeat();
    public void Dispose() => Mechanics.Dispose();
}

internal sealed class ActorState(long entityId, MechanicsEntity mechanics, WorldPoint position) : IActorCombatant, IDisposable
{
    internal long EntityId { get; } = entityId;
    internal MechanicsEntity Mechanics { get; } = mechanics;
    internal WorldPoint Position { get; } = position;
    internal float AttackCooldownSeconds { get; private set; }
    internal bool IsDefeated { get; private set; }
    internal void BeginAttack(float cooldownSeconds) => AttackCooldownSeconds = cooldownSeconds;
    internal void AdvanceCooldown(float deltaSeconds) => AttackCooldownSeconds = Math.Max(0f, AttackCooldownSeconds - deltaSeconds);
    internal void RecordDefeat() => IsDefeated = true;
    MechanicsEntity IActorCombatant.Mechanics => Mechanics;
    float IActorCombatant.AttackCooldownSeconds => AttackCooldownSeconds;
    bool IActorCombatant.IsDefeated => IsDefeated;
    void IActorCombatant.BeginAttack(float cooldownSeconds) => BeginAttack(cooldownSeconds);
    void IActorCombatant.RecordDefeat() => RecordDefeat();
    public void Dispose() => Mechanics.Dispose();
}
