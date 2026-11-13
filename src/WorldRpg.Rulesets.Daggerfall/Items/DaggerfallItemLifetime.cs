using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The end of an item's life: a unique item retired through the Engine inventory and every identity owner, a
/// whole stack consumed, and the still-quest items a quest's end takes back from the player.
/// </summary>
internal sealed class DaggerfallItemLifetime(EntityDirectory entities, InventoryStore store, DaggerfallItemInstances instances,
    DaggerfallUniqueItemAllocator uniqueItems, DaggerfallItemOwnership ownership)
{
    /// <summary>Completes unique item retirement through the Engine inventory and current identity owners.</summary>
    internal void DestroyUnique(ulong durableId)
    {
        var identity = new DurableIdentityReference(DurableIdentityKind.Item, durableId);
        EntityId item = entities.Resolve(identity);
        using (var edit = store.Prepare())
        {
            foreach (EntityId owner in store.EquipmentOwners.Where(entities.Store.IsAlive))
                if (store.TryGetEquipment(owner, out var equipment) && equipment!.Assignments.Any(value => value.Item == item))
                    edit.Unequip(owner, item);
            edit.DestroyUnique(item);
            edit.Publish();
        }
        instances.RemoveUnique(durableId);
        entities.Destroy(identity);
        uniqueItems.Remove(identity);
    }

    /// <summary>Consumes the whole of one owner's stack and forgets its meaning.</summary>
    /// <summary>Every conjured item whose minute has come is retired: unique ones destroyed, stacks consumed.</summary>
    internal void ExpireConjured(long minute)
    {
        foreach (var entry in instances.UniqueItems.Where(value => value.Value.Conjuration?.ExpiresAtMinute <= minute).ToArray())
            DestroyUnique(entry.Key);
        foreach (var entry in instances.StackItems.Where(value => value.Metadata.Conjuration?.ExpiresAtMinute <= minute).ToArray())
            ConsumeStack(entry.Owner, entry.Stack);
    }

    internal void ConsumeStack(DaggerfallItemOwner owner, InventoryStackId id)
    {
        EntityId entity = ownership.Entity(owner);
        var stack = store.View(entity).Stacks.Single(value => value.Id == id);
        store.Consume(entity, stack.Id, stack.Quantity);
        instances.RemoveStack(owner, id);
    }

    /// <summary>
    /// Daggerfall removes only still-quest items carried by the player when their quest ends. The quest's
    /// custody and the held powers follow in the session's list.
    /// </summary>
    internal void RemoveCarriedQuestItems(string instanceId)
    {
        foreach (var item in instances.UniqueItems.Where(value => value.Value.QuestId == instanceId
            && value.Value.Owner == DaggerfallItemOwner.Player).ToArray())
            DestroyUnique(item.Key);
        foreach (var stack in instances.StackItems.Where(value => value.Metadata.QuestId == instanceId
            && value.Owner == DaggerfallItemOwner.Player).ToArray())
            ConsumeStack(stack.Owner, stack.Stack);
    }
}
