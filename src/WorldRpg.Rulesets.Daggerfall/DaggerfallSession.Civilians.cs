using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

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
        DaggerfallSiteProfile profile = _sites.Projection.Inputs;
        bool sourcePopulation = IsSourcePopulation(npc);
        if (State.Actors.TryGet(npcId, out _))
        {
            // Site admission can restore a retained actor before the time-of-day population
            // projection runs. Re-admit the source billboard only when the population owner hid
            // that visual for night; the mechanics actor remains the same durable owner.
            if (sourcePopulation) AdmitSourcePopulationAppearance(npc, profile, _appearance);
            return npcId;
        }

        long actorId = _roster.MaterializeCivilian(npc, pose);
        if (sourcePopulation)
        {
            try { AdmitSourcePopulationAppearance(npc, profile, _appearance); }
            catch
            {
                _roster.UnloadActor(actorId);
                throw;
            }
        }
        // The live actor's Transform now owns the pose; the registry's detached placement is dropped.
        State.Npcs.ReleasePose(npcId);
        return actorId;
    }

    /// <summary>
    /// Restores source-population billboards for actors retained by an adjacent exterior. The
    /// resident projection owns the appearance; the NPC registry and this same source lookup stay
    /// authoritative, so resident re-entry does not create a second civilian visual graph.
    /// </summary>
    internal void AdmitResidentCivilianAppearances(DaggerfallSiteProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        DaggerfallSiteProfile profile = projection.Inputs;
        foreach (DaggerfallNpc npc in State.Npcs.All.Where(npc => IsSourcePopulation(npc)
            && npc.Profile == profile.ProfileKey
            && npc.Presence == DaggerfallNpcPresence.Active
            && State.Actors.TryGet(npc.DurableId, out _)))
        {
            AdmitSourcePopulationAppearance(npc, profile, projection.Appearance);
        }
    }

    /// <summary>Materializes every active civilian, using saved coordinates when present.</summary>
    internal IReadOnlyList<long> MaterializeNpcActors(WorldPoint fallbackPosition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DaggerfallSiteProfile profile = _sites.Projection.Inputs;
        DaggerfallSiteRecord? site = profile.Site is { } siteId ? _site.Require(siteId) : null;
        List<long> materialized = [];
        foreach (DaggerfallNpc npc in State.Npcs.All)
        {
            // Static and quest NPCs have dialogue/service policy but no generic actor
            // definition yet. Hidden/removed civilians belong to an inactive population
            // and must not be recreated by the next site admission.
            if (npc.Kind != DaggerfallNpcKind.Civilian
                || npc.Presence != DaggerfallNpcPresence.Active
                || site is null
                || (npc.Profile is { } physical ? physical != profile.ProfileKey
                    : npc.Site.Region != site.Id.Region || !StringComparer.Ordinal.Equals(npc.Site.Location, site.Name))
                || State.Actors.TryGet(npc.DurableId, out _)) continue;
            WorldPoint position = npc.X is float x && npc.Y is float y && npc.Z is float z
                ? _sites.ProfileToLocal(new WorldPoint(x, y, z))
                : fallbackPosition;
            materialized.Add(MaterializeNpcActor(npc.DurableId, new ActorPose(position, 0F)));
        }
        return materialized;
    }

    private static bool IsSourcePopulation(DaggerfallNpc npc) =>
        npc.Kind == DaggerfallNpcKind.Civilian
        && npc.StableKey.StartsWith("population/", StringComparison.Ordinal);

    private static void AdmitSourcePopulationAppearance(DaggerfallNpc npc, DaggerfallSiteProfile profile,
        DaggerfallSiteAppearance appearance)
    {
        if (!profile.BillboardSprites.TryGetValue((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord),
                out NormalizedBillboardSprite? sourceSprite))
        {
            throw new NotSupportedException($"Population NPC {npc.DurableId} has no published billboard {npc.Appearance.BillboardArchive}/{npc.Appearance.BillboardRecord} at '{profile.ProfileKey.LogicalId}'.");
        }
        if (appearance.HasActor(npc.DurableId)) return;
        appearance.AddActor(npc.DurableId, sourceSprite);
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
        else if (!RetireDetachedActor(npcId, DaggerfallActorKinds.Civilian))
        {
            // Preserve RetireActor's diagnostic for an identity that is not a live
            // dynamic actor and is not retained by an inactive site.
            RetireActor(npcId);
        }

        if (npc.Presence != DaggerfallNpcPresence.Removed)
            State.Npcs.SetPresence(npcId, DaggerfallNpcPresence.Removed);
    }

    /// <summary>
    /// Removes a dynamic actor retained by an inactive site delta. The
    /// detached representation has no Engine entities left to destroy, so retirement
    /// removes every durable relationship directly and keeps the allocator tombstones.
    /// </summary>
    private bool RetireDetachedActor(long npcId, string? requiredDefinition = null)
    {
        DaggerfallWorldProfileKey? owner = null;
        DaggerfallSiteRuntimeDelta? retained = null;
        foreach ((DaggerfallWorldProfileKey profile, DaggerfallSiteRuntimeDelta delta) in _sites.Deltas)
        {
            if (!delta.DynamicActors.Any(actor => actor.EntityId == npcId)) continue;
            if (owner is not null)
                throw new InvalidOperationException($"Actor {npcId} is retained by more than one site delta.");
            owner = profile;
            retained = delta;
        }

        if (retained is null || owner is not DaggerfallWorldProfileKey profileKey) return false;
        DaggerfallDynamicActorSave actor = retained.DynamicActors.Single(value => value.EntityId == npcId);
        if (requiredDefinition is not null && !StringComparer.Ordinal.Equals(actor.Definition, requiredDefinition))
            throw new InvalidOperationException($"Detached NPC actor {npcId} does not carry the required runtime definition.");

        foreach (ulong itemId in retained.ActorInventories
            .Where(inventory => inventory.EntityId == npcId)
            .SelectMany(inventory => inventory.Inventory.UniqueItems.Select(item => item.EntityId))
            .Concat(retained.Corpses.Where(corpse => corpse.ActorId == npcId)
                .SelectMany(corpse => corpse.UniqueItems.Select(item => item.EntityId)))
            .Distinct())
        {
            State.ItemInstances.RemoveUnique(itemId);
            State.Effects.CancelItemReferences(itemId);
            Casting.CancelItemReferences(itemId);
            DurableIdentityReference identity = new(DurableIdentityKind.Item, itemId);
            if (_actorIdentities.Classify(identity) == DurableIdentityClassification.Live)
                _uniqueItems.Remove(identity);
        }

        foreach (var inventory in retained.ActorInventories.Where(value => value.EntityId == npcId))
            foreach (var stack in inventory.Inventory.Stacks)
                State.ItemInstances.RetireRetainedStack(DaggerfallItemOwner.Actor(npcId), Rusty.Engine.Mechanics.InventoryStackId.Parse(stack.StackId));
        foreach (var corpse in retained.Corpses.Where(value => value.ActorId == npcId))
            foreach (var stack in corpse.Stacks)
                State.ItemInstances.RetireRetainedStack(DaggerfallItemOwner.Corpse(npcId), Rusty.Engine.Mechanics.InventoryStackId.Parse(stack.StackId));
        State.Effects.CancelActorReferences(npcId);

        DaggerfallCorpseSave? corpseSave = retained.Corpses.SingleOrDefault(value => value.ActorId == npcId);
        if (corpseSave is not null)
        {
            DurableIdentityReference persisted = new(DurableIdentityKind.Container, corpseSave.ContainerId);
            if (_corpseLoot.TryGetContainerIdentity(npcId, out DurableIdentityReference mapped)
                && mapped != persisted)
                throw new InvalidOperationException($"Detached actor {npcId} has changed its corpse container identity.");
            if (!_corpseLoot.Retire(npcId)
                && _actorIdentities.Classify(persisted) == DurableIdentityClassification.Live)
                _actorIdentities.Remove(persisted);
        }

        DurableIdentityReference actorIdentity = ActorsState.Identity(npcId);
        if (_actorIdentities.Classify(actorIdentity) == DurableIdentityClassification.Live)
            _actorIdentities.Remove(actorIdentity);

        var retiredEffectIds = retained.Effects.Where(value => value.TargetId == npcId || value.CasterId == npcId)
            .Select(value => value.Instance).ToHashSet(StringComparer.Ordinal);
        _sites.ReplaceDelta(profileKey, retained with
        {
            Actors = retained.Actors.Select(value => value with { Stats = DaggerfallStatsSaveBoundary.WithoutEffects(value.Stats, retiredEffectIds) }).ToArray(),
            DynamicActors = retained.DynamicActors.Where(value => value.EntityId != npcId)
                .Select(value => value with { Stats = DaggerfallStatsSaveBoundary.WithoutEffects(value.Stats, retiredEffectIds) }).ToArray(),
            ActorInventories = retained.ActorInventories.Where(value => value.EntityId != npcId).ToArray(),
            Corpses = retained.Corpses.Where(value => value.ActorId != npcId).ToArray(),
            Effects = retained.Effects.Where(value => value.TargetId != npcId && value.CasterId != npcId).ToArray(),
        });
        State.ItemInstances.RemoveOwner(DaggerfallItemOwner.Actor(npcId));
        State.ItemInstances.RemoveOwner(DaggerfallItemOwner.Corpse(npcId));
        foreach (var entry in _sites.Deltas.Where(value => value.Key != profileKey).ToArray())
        {
            var ended = entry.Value.Effects.Where(value => value.TargetId == npcId || value.CasterId == npcId)
                .Select(value => value.Instance).ToHashSet(StringComparer.Ordinal);
            if (ended.Count == 0) continue;
            _sites.ReplaceDelta(entry.Key, entry.Value with
            {
                Effects = entry.Value.Effects.Where(value => !ended.Contains(value.Instance)).ToArray(),
                Actors = entry.Value.Actors.Select(value => value with { Stats = DaggerfallStatsSaveBoundary.WithoutEffects(value.Stats, ended) }).ToArray(),
                DynamicActors = entry.Value.DynamicActors.Select(value => value with { Stats = DaggerfallStatsSaveBoundary.WithoutEffects(value.Stats, ended) }).ToArray(),
            });
        }
        State.Quests.ObserveFoeRemoval(npcId);
        return true;
    }
}
