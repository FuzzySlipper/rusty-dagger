using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private EntityId ItemOwnerEntity(DaggerfallItemOwner owner) => owner.Scope switch
    {
        "player" => State.Actors.Player.Actor.Entity,
        "actor" => State.Actors.Get(owner.Id).Actor.Entity,
        "corpse" => _corpseLoot.Corpses[owner.Id].Owner,
        "ground" or "wagon" or "property" or "quest" => State.Actors.Entities.Resolve(new(DurableIdentityKind.Container, checked((ulong)owner.Id))),
        _ => throw new InvalidOperationException($"No live inventory owner for {owner.Scope}."),
    };

    /// <summary>Completes unique item retirement through the Engine inventory and current identity owners.</summary>
    private void DestroyUniqueItem(ulong durableId)
    {
        var entities = State.Actors.Entities;
        var store = State.InventoryStore;
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
        State.ItemInstances.RemoveUnique(durableId);
        entities.Destroy(identity);
        _uniqueItems.Remove(identity);
    }

    private void ConsumeItemStack(DaggerfallItemOwner owner, InventoryStackId id)
    {
        EntityId entity = ItemOwnerEntity(owner);
        var stack = State.InventoryStore.View(entity).Stacks.Single(value => value.Id == id);
        State.InventoryStore.Consume(entity, stack.Id, stack.Quantity);
        State.ItemInstances.RemoveStack(owner, id);
    }

    /// <summary>Daggerfall removes only still-quest items carried by the player when their quest ends.</summary>
    private void RemoveCarriedQuestItems(string instanceId)
    {
        foreach (var item in State.ItemInstances.UniqueItems.Where(value => value.Value.QuestId == instanceId
            && value.Value.Owner == DaggerfallItemOwner.Player).ToArray())
            DestroyUniqueItem(item.Key);
        foreach (var stack in State.ItemInstances.StackItems.Where(value => value.Metadata.QuestId == instanceId
            && value.Owner == DaggerfallItemOwner.Player).ToArray())
            ConsumeItemStack(stack.Owner, stack.Stack);
        State.QuestItems.RemoveCustody(instanceId);
        State.HeldEnchantments.Refresh();
    }
}
