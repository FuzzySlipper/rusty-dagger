using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Facts;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.Inventory;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Named session services; actor-local state lives on the canonical entities.</summary>
internal sealed class DaggerfallState(PlayerControlState playerControl, ActorsState actors,
    MechanicsInventoryCoordinator inventory, MechanicsEquipmentCoordinator equipment,
    MechanicsInventoryContainerCoordinator containers, IReadOnlyDictionary<InventoryItemId, ItemDefinition> items,
    IReadOnlyDictionary<WorldRpg.Kit.Inventory.EquipmentSlotId, EquipmentSlotDefinition> slots,
    InventoryStore inventoryStore, DaggerfallVariableStore variables, DaggerfallNpcRegistry npcs, DaggerfallSocialState social,
    DaggerfallItemInstances itemInstances, DaggerfallCharacterState character, DaggerfallQuestInstances quests)
{
    internal GameplayServices<IProductFact> Kit { get; set; } = null!;
    /// <summary>Compiled Daggerfall effect policy over the attached per-actor Engine effect components.</summary>
    internal DaggerfallEffectLifecycle Effects { get; set; } = null!;
    /// <summary>Classic skill-use attribution and counters over the player's Kit progression state.</summary>
    internal DaggerfallSkillUseReactions SkillUses { get; set; } = null!;
    /// <summary>One durable, staged classic level-up allocation opened by ordinary rest eligibility.</summary>
    internal DaggerfallLevelUpState LevelUps { get; set; } = null!;
    internal PlayerControlState PlayerControl { get; } = playerControl;
    internal ActorsState Actors { get; } = actors;
    internal ProgressionState Progression => Actors.Player.Progression;
    internal MechanicsInventoryCoordinator Inventory { get; } = inventory;
    internal MechanicsEquipmentCoordinator Equipment { get; } = equipment;
    internal MechanicsInventoryContainerCoordinator Containers { get; } = containers;
    /// <summary>The one managed inventory store every actor inventory and equipment registers in.</summary>
    internal InventoryStore InventoryStore { get; } = inventoryStore;
    /// <summary>The session's scoped quest and world variables, handed explicitly to readers.</summary>
    internal DaggerfallVariableStore Variables { get; } = variables;
    /// <summary>The session's NPC identities, handed explicitly to talk, damage and quest readers.</summary>
    internal DaggerfallNpcRegistry Npcs { get; } = npcs;
    /// <summary>Persistent Daggerfall reputation, reaction, and guild-membership policy for talk, services, and quests.</summary>
    internal DaggerfallSocialState Social { get; } = social;
    /// <summary>Daggerfall instance meaning paired with Engine-backed stacks and unique items.</summary>
    internal DaggerfallItemInstances ItemInstances { get; } = itemInstances;
    /// <summary>The committed player identity and its cancellable creation draft.</summary>
    internal DaggerfallCharacterState Character { get; } = character;
    /// <summary>Current Daggerfall quest instances over admitted definitions and durable product bindings.</summary>
    internal DaggerfallQuestInstances Quests { get; } = quests;
    /// <summary>Live carried-weight policy over the player's canonical Engine inventory.</summary>
    internal DaggerfallEncumbrancePolicy Encumbrance { get; set; } = null!;
    /// <summary>Inventory-backed coins, letters of credit, and the one persistent bank balance.</summary>
    internal DaggerfallCurrencyService Currency { get; set; } = null!;
    /// <summary>Typed Daggerfall service admission, quotes, outcomes, and pending concrete work.</summary>
    internal DaggerfallServiceTransactions Services { get; set; } = null!;
    internal MechanicsInventoryCoordinator? InventoryFor(long durableActorId) =>
        Actors.TryGet(durableActorId, out var actor) ? new(actor.Inventory, Actors.Entities, items) : null;
    internal MechanicsEquipmentCoordinator EquipmentFor(long durableActorId)
    {
        ActorState actor = Actors.Get(durableActorId);
        return new(actor.Inventory, actor.Equipment, Actors.Entities, items, slots);
    }
    internal IEnumerable<KeyValuePair<long, MechanicsInventoryCoordinator>> ActorInventories =>
        Actors.All.Select(actor => new KeyValuePair<long, MechanicsInventoryCoordinator>(actor.DurableId, new(actor.Inventory, Actors.Entities, items)));
}
