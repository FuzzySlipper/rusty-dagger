using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Loot;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Property;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>
    /// Who holds a resident unique item, read from the Engine: the item's container, then the durable
    /// owner that container stands for. Every Daggerfall container is created with its owner's type
    /// and an identity whose value is the owner's id; a corpse's container is the one its actor's
    /// corpse component names. An item no live container holds answers nothing, and its stored owner
    /// stands.
    /// </summary>
    private DaggerfallItemOwner? ResidentItemOwner(ulong itemId)
    {
        EntityDirectory entities = State.Actors.Entities;
        if (!entities.TryResolve(new DurableIdentityReference(DurableIdentityKind.Item, itemId), out EntityId item)
            || !State.InventoryStore.TryGetContainer(item, out EntityId container)
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
        if (type == DaggerfallGroundContainers.GroundContainerType.Value) return DaggerfallItemOwner.Ground(id);
        if (type == DaggerfallWagonStorage.WagonContainerType.Value) return DaggerfallItemOwner.Wagon(id);
        if (type == DaggerfallPropertyStorage.PropertyContainerType.Value) return DaggerfallItemOwner.Property(id);
        if (type == DaggerfallMerchantService.MerchantContainerTypeName) return DaggerfallItemOwner.Merchant(id);
        if (type == DaggerfallMerchantService.RepairContainerTypeName) return DaggerfallItemOwner.RepairCustody(id);
        if (type == DaggerfallQuestItems.CustodyType.Value) return DaggerfallItemOwner.Quest(id);
        foreach (ActorState actor in State.Actors.All)
        {
            if (actor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse) && corpse is not null && corpse.Owner == container)
                return DaggerfallItemOwner.Corpse(actor.DurableId);
        }
        return null;
    }
}
