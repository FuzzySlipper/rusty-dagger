using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// NPC-to-actor materialization owned by the Daggerfall session. The registry
/// remains the durable social/appearance record; this partial creates the one
/// canonical Mechanics actor that combat, dialogue, save, and corpse policy
/// can address by the same durable identity.
/// </summary>
internal sealed partial class DaggerfallSession
{
    /// <summary>
    /// Materializes one registered NPC at an explicit world pose. The actor is
    /// recorded as a runtime civilian dynamic actor, so its pose, inventory,
    /// corpse, and identity ledger participate in the existing save boundary.
    /// </summary>
    internal long MaterializeNpcActor(long npcId, ActorPose pose)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DaggerfallNpc npc = State.Npcs.Require(npcId);
        if (npc.Kind != DaggerfallNpcKind.Civilian)
            throw new InvalidOperationException($"NPC {npcId} is {npc.Kind}, not a materializable civilian.");
        if (npc.Presence == DaggerfallNpcPresence.Removed)
            throw new InvalidOperationException($"NPC {npcId} has been removed and cannot be materialized.");
        if (State.Actors.TryGet(npcId, out _)) return npcId;

        DurableIdentityReference identity = ActorsState.Identity(npcId);
        if (_actorIdentities.Classify(identity) != DurableIdentityClassification.Live)
            throw new InvalidOperationException($"NPC {npcId} does not own a live actor identity.");

        DaggerfallActorDefinition definition = DaggerActorFactory.CivilianDefinition(npcId);
        ActorState actor = DaggerActorFactory.CreateCivilianActor(_mechanics, State.Actors, State.InventoryStore, npc, pose);
        try
        {
            if (!_definitionsByActor.TryAdd(npcId, definition))
                throw new InvalidOperationException($"NPC {npcId} already has a runtime actor definition.");
            if (!_dynamicActors.TryAdd(npcId, new DaggerfallActorId(DaggerfallActorKinds.Civilian)))
                throw new InvalidOperationException($"NPC {npcId} already has a runtime actor binding.");
            return actor.DurableId;
        }
        catch
        {
            _definitionsByActor.Remove(npcId);
            _dynamicActors.Remove(npcId);
            State.Actors.Entities.Destroy(identity);
            throw;
        }
    }

    /// <summary>Materializes every active civilian, using saved coordinates when present.</summary>
    internal IReadOnlyList<long> MaterializeNpcActors(WorldPoint fallbackPosition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DaggerfallSiteRecord? site = _site.ActiveSite;
        List<long> materialized = [];
        foreach (DaggerfallNpc npc in State.Npcs.All)
        {
            // Static and quest NPCs have dialogue/service policy but no generic actor
            // definition yet. Hidden/removed civilians belong to an inactive population
            // and must not be recreated by the next site admission.
            if (npc.Kind != DaggerfallNpcKind.Civilian
                || npc.Presence != DaggerfallNpcPresence.Active
                || site is null
                || npc.Site.Region != site.Id.Region
                || !StringComparer.Ordinal.Equals(npc.Site.Location, site.Name)
                || State.Actors.TryGet(npc.DurableId, out _)) continue;
            WorldPoint position = npc.X is int x && npc.Y is int y && npc.Z is int z
                ? new WorldPoint(x, y, z)
                : fallbackPosition;
            materialized.Add(MaterializeNpcActor(npc.DurableId, new ActorPose(position, 0F)));
        }
        return materialized;
    }

    /// <summary>Retires a civilian and records the social identity as removed.</summary>
    internal void RetireNpcActor(long npcId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DaggerfallNpc npc = State.Npcs.Require(npcId);
        if (npc.Kind != DaggerfallNpcKind.Civilian)
            throw new InvalidOperationException($"NPC {npcId} is {npc.Kind}, not a materializable civilian.");

        if (State.Actors.TryGet(npcId, out _))
        {
            RetireActor(npcId);
        }
        else if (!RetireDetachedCivilian(npcId))
        {
            // Preserve RetireActor's diagnostic for an identity that is not a live
            // dynamic actor and is not retained by an inactive site.
            RetireActor(npcId);
        }

        if (npc.Presence != DaggerfallNpcPresence.Removed)
            State.Npcs.SetPresence(npcId, DaggerfallNpcPresence.Removed);
    }

    /// <summary>
    /// Removes a civilian whose actor is retained by an inactive site delta. The
    /// detached representation has no Engine entities left to destroy, so retirement
    /// removes every durable relationship directly and keeps the allocator tombstones.
    /// </summary>
    private bool RetireDetachedCivilian(long npcId)
    {
        DaggerfallWorldProfileKey? owner = null;
        DaggerfallSiteRuntimeDelta? retained = null;
        foreach ((DaggerfallWorldProfileKey profile, DaggerfallSiteRuntimeDelta delta) in _siteDeltas)
        {
            if (!delta.DynamicActors.Any(actor => actor.EntityId == npcId)) continue;
            if (owner is not null)
                throw new InvalidOperationException($"Civilian actor {npcId} is retained by more than one site delta.");
            owner = profile;
            retained = delta;
        }

        if (retained is null || owner is not DaggerfallWorldProfileKey profileKey) return false;
        DaggerfallDynamicActorSave actor = retained.DynamicActors.Single(value => value.EntityId == npcId);
        if (!StringComparer.Ordinal.Equals(actor.Definition, DaggerfallActorKinds.Civilian))
            throw new InvalidOperationException($"Detached NPC actor {npcId} does not carry the civilian runtime definition.");

        foreach (ulong itemId in retained.ActorInventories
            .Where(inventory => inventory.EntityId == npcId)
            .SelectMany(inventory => inventory.Inventory.UniqueItems.Select(item => item.EntityId))
            .Concat(retained.Corpses.Where(corpse => corpse.ActorId == npcId)
                .SelectMany(corpse => corpse.UniqueItems.Select(item => item.EntityId)))
            .Distinct())
        {
            State.ItemInstances.RemoveUnique(itemId);
            DurableIdentityReference identity = new(DurableIdentityKind.Item, itemId);
            if (_actorIdentities.Classify(identity) == DurableIdentityClassification.Live)
                _uniqueItems.Remove(identity);
        }

        DaggerfallCorpseSave? corpseSave = retained.Corpses.SingleOrDefault(value => value.ActorId == npcId);
        if (corpseSave is not null)
        {
            DurableIdentityReference persisted = new(DurableIdentityKind.Container, corpseSave.ContainerId);
            if (_corpseLoot.TryGetContainerIdentity(npcId, out DurableIdentityReference mapped)
                && mapped != persisted)
                throw new InvalidOperationException($"Detached civilian {npcId} has changed its corpse container identity.");
            if (!_corpseLoot.Retire(npcId)
                && _actorIdentities.Classify(persisted) == DurableIdentityClassification.Live)
                _actorIdentities.Remove(persisted);
        }

        DurableIdentityReference actorIdentity = ActorsState.Identity(npcId);
        if (_actorIdentities.Classify(actorIdentity) == DurableIdentityClassification.Live)
            _actorIdentities.Remove(actorIdentity);

        _siteDeltas[profileKey] = retained with
        {
            DynamicActors = retained.DynamicActors.Where(value => value.EntityId != npcId).ToArray(),
            ActorInventories = retained.ActorInventories.Where(value => value.EntityId != npcId).ToArray(),
            Corpses = retained.Corpses.Where(value => value.ActorId != npcId).ToArray(),
            Effects = retained.Effects.Where(value => value.TargetId != npcId && value.CasterId != npcId).ToArray(),
        };
        State.ItemInstances.RemoveOwner(DaggerfallItemOwner.Actor(npcId));
        State.ItemInstances.RemoveOwner(DaggerfallItemOwner.Corpse(npcId));
        return true;
    }
}
