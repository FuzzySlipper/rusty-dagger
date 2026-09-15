using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.Inventory;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Composition and inspection aggregate; each mutable family remains owned by its module.</summary>
internal sealed class DaggerfallState(PlayerControlState playerControl, ActorsState actors, ProgressionState progression, MechanicsInventoryCoordinator inventory, MechanicsEquipmentCoordinator equipment, MechanicsInventoryContainerCoordinator containers, IReadOnlyDictionary<long, MechanicsInventoryCoordinator> actorInventories)
{
    internal PlayerControlState PlayerControl { get; } = playerControl;
    internal ActorsState Actors { get; } = actors;
    internal ProgressionState Progression { get; } = progression;
    internal MechanicsInventoryCoordinator Inventory { get; } = inventory;
    internal MechanicsEquipmentCoordinator Equipment { get; } = equipment;
    internal MechanicsInventoryContainerCoordinator Containers { get; } = containers;
    /// <summary>
    /// One managed inventory per placed actor whose definition declares a loadout, over the
    /// session's single InventoryWorld. Today this carries the ranged actors' quivers: a shot
    /// draws from it, and its contents persist with the save.
    /// </summary>
    internal IReadOnlyDictionary<long, MechanicsInventoryCoordinator> ActorInventories { get; } = actorInventories;
}
