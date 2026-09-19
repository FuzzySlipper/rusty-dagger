using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.Inventory;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Named session services; actor-local state lives on the canonical entities.</summary>
internal sealed class DaggerfallState(PlayerControlState playerControl, ActorsState actors,
    MechanicsInventoryCoordinator inventory, MechanicsEquipmentCoordinator equipment,
    MechanicsInventoryContainerCoordinator containers, IReadOnlyDictionary<InventoryItemId, ItemDefinition> items)
{
    internal PlayerControlState PlayerControl { get; } = playerControl;
    internal ActorsState Actors { get; } = actors;
    internal ProgressionState Progression => Actors.Player.Progression;
    internal MechanicsInventoryCoordinator Inventory { get; } = inventory;
    internal MechanicsEquipmentCoordinator Equipment { get; } = equipment;
    internal MechanicsInventoryContainerCoordinator Containers { get; } = containers;
    internal MechanicsInventoryCoordinator? InventoryFor(long durableActorId) =>
        Actors.TryGet(durableActorId, out var actor) ? new(actor.Inventory, Actors.Entities, items) : null;
    internal IEnumerable<KeyValuePair<long, MechanicsInventoryCoordinator>> ActorInventories =>
        Actors.All.Select(actor => new KeyValuePair<long, MechanicsInventoryCoordinator>(actor.DurableId, new(actor.Inventory, Actors.Entities, items)));
}
