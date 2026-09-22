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
    InventoryStore inventoryStore, DaggerfallVariableStore variables, DaggerfallNpcRegistry npcs)
{
    internal GameplayServices<IProductFact> Kit { get; set; } = null!;
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
