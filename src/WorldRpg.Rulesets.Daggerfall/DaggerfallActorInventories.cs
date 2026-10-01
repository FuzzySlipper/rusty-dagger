using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Inventory and equipment coordinators over any live actor's canonical Engine components, against the
/// session's one admitted item and slot catalog.
/// </summary>
internal sealed class DaggerfallActorInventories(ActorsState actors, IReadOnlyDictionary<InventoryItemId, ItemDefinition> items,
    IReadOnlyDictionary<WorldRpg.Kit.Inventory.EquipmentSlotId, EquipmentSlotDefinition> slots)
{
    internal IReadOnlyDictionary<InventoryItemId, ItemDefinition> ItemDefinitions => items;

    /// <summary>The actor's inventory, or null when no live actor has that durable identity.</summary>
    internal MechanicsInventoryCoordinator? InventoryFor(long durableActorId) =>
        actors.TryGet(durableActorId, out var actor) ? new(actor.Inventory, actors.Entities, items) : null;

    internal MechanicsEquipmentCoordinator EquipmentFor(long durableActorId)
    {
        ActorState actor = actors.Get(durableActorId);
        return new(actor.Inventory, actor.Equipment, actors.Entities, items, slots);
    }

    /// <summary>Every live actor's inventory, keyed by durable identity.</summary>
    internal IEnumerable<KeyValuePair<long, MechanicsInventoryCoordinator>> All =>
        actors.All.Select(actor => new KeyValuePair<long, MechanicsInventoryCoordinator>(actor.DurableId, new(actor.Inventory, actors.Entities, items)));
}
