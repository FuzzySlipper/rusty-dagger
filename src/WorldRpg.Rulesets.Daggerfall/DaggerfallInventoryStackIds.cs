using Rusty.Engine.Mechanics;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Daggerfall names every newly authored fungible stack before it enters the
/// Engine inventory. The name derives from the owning placement and grant
/// occurrence, never from the item definition, so same-definition stacks can
/// retain distinct instance meaning.
/// </summary>
internal static class DaggerfallInventoryStackIds
{
    internal static InventoryStackId ForInitialLoadout(long ownerId, int ordinal) =>
        InventoryStackId.Parse($"daggerfall.loadout.{ownerId}.{ordinal}");

    internal static InventoryStackId ForSpawnLoadout(long ownerId, int ordinal) =>
        InventoryStackId.Parse($"daggerfall.spawn.{ownerId}.{ordinal}");

    internal static InventoryStackId ForLoot(long actorId, ulong sequence, int ordinal) =>
        InventoryStackId.Parse($"daggerfall.loot.{actorId}.{sequence}.{ordinal}");
}
