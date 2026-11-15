using Rusty.Engine.Entities;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What a quest says about one of its items leaving the player's pack. The quests implement it.</summary>
internal interface IDaggerfallQuestItemDrops
{
    /// <summary>Whether a running drop-at-place task for the item has the player at its place.</summary>
    bool MayLeave(DaggerfallItemInstanceMetadata item);

    /// <summary>The item left the player's pack for somewhere a drop counts.</summary>
    void Left(DaggerfallItemInstanceMetadata item);

    /// <summary>The item came back into the player's pack.</summary>
    void Returned(DaggerfallItemInstanceMetadata item);
}

/// <summary>A way out of the pack refused an item; its message is the reason the player is shown.</summary>
internal sealed class DaggerfallItemExitRefusedException(string reason) : InvalidOperationException(reason);

/// <summary>
/// Whether an item may leave the player's pack, and telling its quest when it does and when it comes back. Daggerfall
/// lets a quest item leave only while its quest allows the drop; every way out (dropping, selling, the repair counter,
/// the wagon, house storage) asks before it moves anything. An item made permanent is no longer a quest item and moves
/// freely. A move into the wagon is not a drop, as in the original.
/// </summary>
internal sealed class DaggerfallQuestItemExits(DaggerfallItemInstances instances, IDaggerfallQuestItemDrops quests)
{
    internal const string Refused = "You cannot remove this item.";

    /// <summary>Why the item may not leave the pack, or null when it may.</summary>
    internal string? Refusal(DaggerfallItemInstanceMetadata item) =>
        item.QuestId is null || quests.MayLeave(item) ? null : Refused;

    /// <summary>Refuses a selection from the player's pack that may not leave it, before anything moves.</summary>
    internal void RequireMayLeave(InventoryContainerSelection selection, EntityDirectory entities)
    {
        DaggerfallItemInstanceMetadata item = selection.UniqueEntityId is ulong unique
            ? instances.RequireUnique(entities.IdentityOf(new EntityId(unique)).Value)
            : instances.RequireStack(DaggerfallItemOwner.Player, selection.Stack!);
        if (Refusal(item) is { } reason) throw new DaggerfallItemExitRefusedException(reason);
    }

    /// <summary>Reports a completed move of a quest item out of or back into the player's pack.</summary>
    internal void Moved(DaggerfallItemInstanceMetadata item, DaggerfallItemOwner source, DaggerfallItemOwner destination)
    {
        if (item.QuestId is null || source == destination) return;
        if (source == DaggerfallItemOwner.Player && destination.Scope != "wagon") quests.Left(item);
        else if (destination == DaggerfallItemOwner.Player) quests.Returned(item);
    }
}
