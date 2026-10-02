using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private EntityId ItemOwnerEntity(DaggerfallItemOwner owner) => owner.Scope switch
    {
        "player" => State.Actors.Player.Actor.Entity,
        "actor" => State.Actors.Get(owner.Id).Actor.Entity,
        "corpse" => _corpseLoot.Corpses[owner.Id].Owner,
        "ground" or "wagon" or "property" => State.Actors.Entities.Resolve(new(DurableIdentityKind.Container, checked((ulong)owner.Id))),
        _ => throw new InvalidOperationException($"No live inventory owner for {owner.Scope}."),
    };

    /// <summary>Expiry is item policy on the one admitted calendar and the canonical inventories.</summary>
    private void ExpireConjuredItems()
    {
        long minute = MinuteIndex(_time.Calendar);
        var entities = State.Actors.Entities;
        var store = State.InventoryStore;
        foreach (var entry in State.ItemInstances.UniqueItems.Where(value => value.Value.Conjuration?.ExpiresAtMinute <= minute).ToArray())
        {
            var identity = new DurableIdentityReference(DurableIdentityKind.Item, entry.Key);
            EntityId item = entities.Resolve(identity);
            using (var edit = store.Prepare())
            {
                foreach (EntityId owner in store.EquipmentOwners.Where(entities.Store.IsAlive))
                    if (store.TryGetEquipment(owner, out var equipment) && equipment!.Assignments.Any(value => value.Item == item))
                        edit.Unequip(owner, item);
                edit.DestroyUnique(item);
                edit.Publish();
            }
            State.ItemInstances.RemoveUnique(entry.Key);
            entities.Destroy(identity);
            _uniqueItems.Remove(identity);
        }
        foreach (var entry in State.ItemInstances.StackItems.Where(value => value.Metadata.Conjuration?.ExpiresAtMinute <= minute).ToArray())
        {
            EntityId owner = ItemOwnerEntity(entry.Owner);
            var stack = store.View(owner).Stacks.Single(value => value.Id == entry.Stack);
            store.Consume(owner, stack.Id, stack.Quantity);
            State.ItemInstances.RemoveStack(entry.Owner, entry.Stack);
        }
        State.HeldEnchantments.Refresh();
        // Inactive deltas stay coherent while suspended. The site's existing materialization
        // restores their effects and stat sources before this same live sweep releases them.
    }
}
