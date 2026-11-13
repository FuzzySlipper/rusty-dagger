using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Loot;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The Engine type of every Daggerfall item container that is not an actor's own inventory. A container's type
/// says which <see cref="DaggerfallItemOwner"/> scope its items belong to, so the types sit beside the owner.
/// </summary>
internal static class DaggerfallItemContainers
{
    internal static readonly EntityTypeId Ground = new("daggerfall.ground-container");
    internal static readonly EntityTypeId Wagon = new("daggerfall.wagon-container");
    internal static readonly EntityTypeId Property = new("daggerfall.property-container");
    internal static readonly EntityTypeId Merchant = new("daggerfall.merchant-container");
    internal static readonly EntityTypeId RepairCustody = new("daggerfall.repair-custody");
    internal static readonly EntityTypeId QuestCustody = new("daggerfall.quest-custody");
}

/// <summary>
/// Who holds an item, both ways: the live inventory a durable owner stands for, and the durable owner a resident
/// unique item's container stands for. Every Daggerfall container is created with its owner's type and an
/// identity whose value is the owner's id; a corpse's container is the one its actor's corpse component names,
/// which <paramref name="corpseContainer"/> reads for a corpse's actor id.
/// </summary>
internal sealed class DaggerfallItemOwnership(ActorsState actors, InventoryStore store, Func<long, EntityId> corpseContainer)
{
    /// <summary>The live inventory entity that holds <paramref name="owner"/>'s items.</summary>
    internal EntityId Entity(DaggerfallItemOwner owner) => owner.Scope switch
    {
        "player" => actors.Player.Actor.Entity,
        "actor" => actors.Get(owner.Id).Actor.Entity,
        "corpse" => corpseContainer(owner.Id),
        "ground" or "wagon" or "property" or "quest" => actors.Entities.Resolve(new(DurableIdentityKind.Container, checked((ulong)owner.Id))),
        _ => throw new InvalidOperationException($"No live inventory owner for {owner.Scope}."),
    };

    /// <summary>
    /// Who holds a resident unique item, read from the Engine: the item's container, then the durable owner that
    /// container stands for. An item no live container holds answers nothing, and its stored owner stands.
    /// </summary>
    internal DaggerfallItemOwner? ResidentOwner(ulong itemId)
    {
        EntityDirectory entities = actors.Entities;
        if (!entities.TryResolve(new DurableIdentityReference(DurableIdentityKind.Item, itemId), out EntityId item)
            || !store.TryGetContainer(item, out EntityId container)
            || !entities.Store.TryGet(container, out DurableEntityIdentity? held))
            return null;
        DurableIdentityReference identity = held.Identity;
        if (identity.Kind == DurableIdentityKind.Actor)
        {
            long actorId = checked((long)identity.Value);
            return actorId == DaggerfallActorIdentity.PlayerEntityId ? DaggerfallItemOwner.Player : DaggerfallItemOwner.Actor(actorId);
        }
        if (identity.Kind != DurableIdentityKind.Container) return null;
        long id = checked((long)identity.Value);
        string? type = entities.Store.GetTypeId(container).Value;
        if (type == DaggerfallItemContainers.Ground.Value) return DaggerfallItemOwner.Ground(id);
        if (type == DaggerfallItemContainers.Wagon.Value) return DaggerfallItemOwner.Wagon(id);
        if (type == DaggerfallItemContainers.Property.Value) return DaggerfallItemOwner.Property(id);
        if (type == DaggerfallItemContainers.Merchant.Value) return DaggerfallItemOwner.Merchant(id);
        if (type == DaggerfallItemContainers.RepairCustody.Value) return DaggerfallItemOwner.RepairCustody(id);
        if (type == DaggerfallItemContainers.QuestCustody.Value) return DaggerfallItemOwner.Quest(id);
        foreach (ActorState actor in actors.All)
        {
            if (actor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse) && corpse is not null && corpse.Owner == container)
                return DaggerfallItemOwner.Corpse(actor.DurableId);
        }
        return null;
    }
}
