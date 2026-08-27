using RustyDagger.Game.Content;
using RustyDagger.Game.Daggerfall.Content;
using RustyDagger.Game.Modules.PlayerControl;

namespace RustyDagger.Game.Modules.Actors;

internal sealed class ActorsState
{
    private readonly Dictionary<long, ActorState> _actors;

    internal ActorsState(ActorDefinition playerDefinition, IEnumerable<AuthoredActor> authoredActors)
    {
        Player = new PlayerActorState(playerDefinition);
        _actors = new Dictionary<long, ActorState>();
        foreach (AuthoredActor authored in authoredActors)
        {
            ActorDefinition? definition = DaggerfallDefinitions.ForAuthoredName(authored.Name);
            if (definition is not null) _actors[authored.EntityId] = new ActorState(authored.EntityId, definition, authored.Position, authored.Sprite);
        }
    }

    internal PlayerActorState Player { get; }
    internal IReadOnlyDictionary<long, ActorState> All => _actors;
    internal bool TryGet(long entityId, out ActorState actor) => _actors.TryGetValue(entityId, out actor!);
    internal void AdvanceCooldowns(float deltaSeconds)
    {
        Player.AdvanceCooldown(deltaSeconds);
        foreach (ActorState actor in _actors.Values) actor.AdvanceCooldown(deltaSeconds);
    }
}

internal sealed class PlayerActorState(ActorDefinition definition)
{
    internal ActorDefinition Definition { get; } = definition;
    internal int Health { get; private set; } = definition.MaximumHealth;
    internal int Stamina { get; private set; } = definition.MaximumStamina;
    internal int Magicka { get; } = definition.MaximumMagicka;
    internal float AttackCooldownSeconds { get; private set; }
    internal bool IsDead => Health == 0;
    internal void SpendStamina(int amount) => Stamina = Math.Max(0, Stamina - Math.Max(0, amount));
    internal void BeginAttack(float cooldownSeconds) => AttackCooldownSeconds = cooldownSeconds;
    internal DamageReceipt ApplyDamage(int amount)
    {
        if (IsDead) return new(0, false);
        int applied = Math.Min(Health, Math.Max(0, amount));
        Health -= applied;
        return new(applied, Health == 0);
    }
    internal void AdvanceCooldown(float deltaSeconds) => AttackCooldownSeconds = Math.Max(0f, AttackCooldownSeconds - deltaSeconds);
}

internal sealed class ActorState(long entityId, ActorDefinition definition, WorldPoint position, AuthoredSprite? sprite)
{
    internal long EntityId { get; } = entityId;
    internal ActorDefinition Definition { get; } = definition;
    internal WorldPoint Position { get; } = position;
    internal AuthoredSprite? Sprite { get; } = sprite;
    internal int Health { get; private set; } = definition.MaximumHealth;
    internal float AttackCooldownSeconds { get; private set; }
    internal bool IsDead => Health == 0;
    internal void BeginAttack(float cooldownSeconds) => AttackCooldownSeconds = cooldownSeconds;
    internal void AdvanceCooldown(float deltaSeconds) => AttackCooldownSeconds = Math.Max(0f, AttackCooldownSeconds - deltaSeconds);
    internal DamageReceipt ApplyDamage(int amount)
    {
        if (IsDead) return new(0, false);
        int applied = Math.Min(Health, Math.Max(0, amount));
        Health -= applied;
        return new(applied, Health == 0);
    }
}

internal readonly record struct DamageReceipt(int AppliedDamage, bool Died);
