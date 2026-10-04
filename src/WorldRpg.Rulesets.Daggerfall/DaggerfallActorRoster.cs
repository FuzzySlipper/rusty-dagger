using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Loot;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The session's live non-player actor roster beside <see cref="DaggerActorFactory"/>: which
/// definition every live actor was registered from, which of them were spawned rather than placed,
/// and the lifetime policy for spawning, retiring and unloading them.
/// </summary>
/// <remarks>
/// The factory constructs entities; this owner decides what a spawn grants and what a retirement or
/// site unload releases (effects, owned items, corpse containers, identities and the actor's
/// appearance). It is the one mutable owner of the two actor maps; every other reader holds the same
/// definition dictionary read-only.
/// </remarks>
internal sealed class DaggerfallActorRoster
{
    private readonly DaggerfallState _state;
    private readonly DaggerfallDefinitions _definitions;
    private readonly IRandomService _random;
    private readonly DaggerfallMechanicsState _mechanics;
    private readonly DurableIdentityAllocator _identities;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly IReadOnlySet<ulong> _authoredEntityIds;
    private readonly Dictionary<long, DaggerfallActorDefinition> _definitionsByActor;
    private readonly Dictionary<long, DaggerfallActorId> _dynamicActors;
    private readonly DaggerfallActorGrounding _grounding;
    private readonly Func<DaggerfallSiteProjection> _projection;
    private readonly DaggerfallLootPresentation _lootUi;
    private readonly DaggerfallCorpseLootModule _corpseLoot;

    internal DaggerfallActorRoster(DaggerfallState state, DaggerfallDefinitions definitions, IRandomService random,
        DaggerfallMechanicsState mechanics, DurableIdentityAllocator identities, DaggerfallUniqueItemAllocator uniqueItems,
        IReadOnlySet<ulong> authoredEntityIds, Dictionary<long, DaggerfallActorDefinition> definitionsByActor,
        IEnumerable<DaggerfallDynamicActorSave> restoredDynamicActors, DaggerfallActorGrounding grounding,
        Func<DaggerfallSiteProjection> projection, DaggerfallLootPresentation lootUi, DaggerfallCorpseLootModule corpseLoot)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _mechanics = mechanics ?? throw new ArgumentNullException(nameof(mechanics));
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _uniqueItems = uniqueItems ?? throw new ArgumentNullException(nameof(uniqueItems));
        _authoredEntityIds = authoredEntityIds ?? throw new ArgumentNullException(nameof(authoredEntityIds));
        _definitionsByActor = definitionsByActor ?? throw new ArgumentNullException(nameof(definitionsByActor));
        _grounding = grounding ?? throw new ArgumentNullException(nameof(grounding));
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _lootUi = lootUi ?? throw new ArgumentNullException(nameof(lootUi));
        _corpseLoot = corpseLoot ?? throw new ArgumentNullException(nameof(corpseLoot));
        _dynamicActors = (restoredDynamicActors ?? throw new ArgumentNullException(nameof(restoredDynamicActors)))
            .ToDictionary(spawned => spawned.EntityId, spawned => new DaggerfallActorId(spawned.Definition));
    }

    /// <summary>Every actor definition by durable identity: authored placements and spawned actors alike.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorDefinition> Definitions => _definitionsByActor;

    /// <summary>Spawned actors by durable identity to the definition each was registered from.</summary>
    internal IReadOnlyDictionary<long, DaggerfallActorId> Dynamic => _dynamicActors;

    /// <summary>Returns the live spawned identities that are not owned by an already admitted site.</summary>
    internal IReadOnlySet<long> DynamicActorIdsExcluding(IReadOnlySet<long> excluded)
    {
        ArgumentNullException.ThrowIfNull(excluded);
        return _dynamicActors.Keys.Where(id => !excluded.Contains(id)).ToHashSet();
    }

    private DaggerfallSiteAppearance Appearance => _projection().Appearance;

    /// <summary>Replaces the runtime entity at one durable actor identity; no hidden original remains.</summary>
    internal DaggerfallWabbajackResult Transform(long durableId, int selectedMobile)
    {
        if (!_state.Actors.TryGet(durableId, out var original) || !_definitionsByActor.TryGetValue(durableId, out var oldDefinition)
            || oldDefinition.Kind is not (DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass))
            return new(DaggerfallWabbajackOutcome.InvalidTarget, durableId);
        if (DaggerfallWabbajack.DefinitionOf(original.Actor) is not null)
            return new(DaggerfallWabbajackOutcome.AlreadyTransformed, durableId);
        DaggerfallActorDefinition definition = _definitions.Actors.Values.Single(value => value.MobileId == selectedMobile);
        _ = DaggerfallWabbajack.RequireDefinition(_definitions, definition.Id.Value);
        if (oldDefinition.Team == "player-ally") definition = definition with { Team = "player-ally" };
        if (!_projection().Inputs.MobileSprites.TryGetValue(selectedMobile, out var sprite))
            return new(DaggerfallWabbajackOutcome.UnavailableAppearance, durableId);
        DaggerfallCorruptionOrigin? origin = original.Actor.TryGet<DaggerfallCorruptionOrigin>(out var copiedFrom) ? copiedFrom : null;
        ActorPose pose = original.Pose;
        Track health = original.Stats.GetTrack(TrackId.Parse(oldDefinition.Combat.Health.Value));
        double wounds = health.Maximum.Value - health.Current;
        int level = definition.Level ?? 1;
        StatsComponent stats = _mechanics.CreateStats(definition, SpawnVitals(definition, level, durableId));
        // The replacement starts with its own maximum and retains the original's missing health.
        stats.GetTrack(TrackId.Parse(definition.Combat.Health.Value)).SetCurrent(
            stats.GetTrack(TrackId.Parse(definition.Combat.Health.Value)).Maximum.Value - wounds, clamp: true);
        _lootUi.CloseActor(durableId);
        _ = _state.Effects.CancelActorReferences(durableId);
        DestroyOwnedItems(durableId);
        _corpseLoot.Retire(durableId);
        Appearance.RetireActor(durableId);
        _state.Actors.Entities.Destroy(ActorsState.Identity(durableId));
        ActorState replacement = DaggerActorFactory.CreateNonPlayerActor(_state.Actors, durableId, definition, stats, pose);
        DaggerActorFactory.RegisterActorInventory(replacement, _state.InventoryStore);
        DaggerfallWabbajack.Restore(replacement.Actor, definition.Id.Value);
        if (origin is not null) replacement.Actor.Add(origin);
        GrantSpawnLoadout(replacement, definition);
        _definitionsByActor[durableId] = definition;
        if (_dynamicActors.ContainsKey(durableId)) _dynamicActors[durableId] = definition.Id;
        Appearance.AddActor(durableId, sprite);
        return new(DaggerfallWabbajackOutcome.Transformed, durableId, definition.Id.Value);
    }

    /// <summary>
    /// Registers one actor from a published definition beyond the authored placements, with the
    /// same Mechanics binding an authored actor is constructed with: catalog stats, pursuit
    /// memory, managed inventory and equipment, definition loadout, and floor grounding.
    /// Returns the allocated durable identity, which the save persists and restore reuses.
    /// </summary>
    internal long Spawn(string definitionId, ActorPose pose, int? level = null, bool playerAllied = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        DaggerfallActorDefinition definition = _definitions.RequireActor(new DaggerfallActorId(definitionId));
        if (definition.Kind == DaggerfallActorKinds.Player)
            throw new InvalidOperationException("The player actor is authored once; it cannot be spawned.");
        if (level is < 1) throw new ArgumentOutOfRangeException(nameof(level));
        // Class enemies level to the player the way the donor levels them; monsters carry their
        // own level. Pack skills stay as authored: the donor overwrites every skill from the
        // spawn level, but authored pack values own that meaning here.
        int spawnLevel = level ?? definition.Level ?? (definition.Kind == DaggerfallActorKinds.EnemyClass ? _state.Progression.Level : 1);
        DurableIdentityReference identity = _identities.Allocate(DurableIdentityKind.Actor);
        long durableId = checked((long)identity.Value);
        if (definition.Kind == DaggerfallActorKinds.EnemyClass && definition.MobileId == 146)
            spawnLevel = checked(spawnLevel + (int)_random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, CombatRandomKey.EnemyScope,
                $"class-guard-level:{durableId}", 3, 6)).Value);
        DaggerfallActorDefinition spawnedDefinition = DaggerfallEncounterActors.AtLevel(definition, _definitions.Vocabulary, spawnLevel);
        if (playerAllied) spawnedDefinition = spawnedDefinition with { Team = "player-ally" };
        bool registered = false;
        try
        {
            ActorState actor = DaggerActorFactory.CreateNonPlayerActor(_state.Actors, durableId, spawnedDefinition,
                _mechanics.CreateStats(spawnedDefinition, SpawnVitals(spawnedDefinition, spawnLevel, durableId)),
                pose);
            DaggerActorFactory.RegisterActorInventory(actor, _state.InventoryStore);
            GrantSpawnLoadout(actor, spawnedDefinition);
            GrantStartingEquipment(actor, spawnedDefinition);
            if (spawnedDefinition.MobileId is int mobileId)
            {
                if (!_projection().Inputs.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
                    throw new InvalidOperationException($"Spawned actor '{spawnedDefinition.Id.Value}' has no admitted mobile {mobileId} presentation.");
                Appearance.AddActor(durableId, sprite);
            }
            _definitionsByActor.Add(durableId, spawnedDefinition);
            _dynamicActors.Add(durableId, spawnedDefinition.Id);
            registered = true;
            if (definition.GroundOnSpawn) _grounding.Ground(actor);
            return durableId;
        }
        catch
        {
            if (registered)
            {
                _definitionsByActor.Remove(durableId);
                _dynamicActors.Remove(durableId);
            }

            _state.Actors.Entities.Destroy(identity);
            _identities.Remove(identity);
            throw;
        }
    }

    /// <summary>
    /// Retires one spawned actor: definition and appearance references drop, an open loot
    /// container for the actor closes, owned unique items
    /// are destroyed with their identities tombstoned, the corpse container goes with the actor,
    /// and the identity stays tombstoned so removal remains distinguishable from never-loaded.
    /// Authored placement actors belong to the selected content and cannot retire; the player
    /// can never retire. The actor's inventory and equipment registrations, and its corpse
    /// container's, leave the shared inventory store with it.
    /// </summary>
    internal HashSet<long> BanishedActors { get; } = [];
    internal event Action<long>? ActorRetired;

    internal void Banish(long durableId)
    {
        if (!_state.Actors.TryGet(durableId, out var actor) || actor.IsDefeated) return;
        RemoveQuestActor(durableId);
    }

    /// <summary>Explicit quest removal also removes a defeated actor and its corpse, without inventing a death.</summary>
    internal void RemoveQuestActor(long durableId)
    {
        if (_dynamicActors.ContainsKey(durableId)) { Retire(durableId); return; }
        if (durableId == DaggerfallActorIdentity.PlayerEntityId) throw new InvalidOperationException("The player cannot be removed by a quest foe action.");
        if (!_state.Actors.TryGet(durableId, out _)) throw new InvalidOperationException($"Quest actor {durableId} is not materialized.");
        _definitionsByActor.Remove(durableId);
        Appearance.RetireActor(durableId);
        _lootUi.CloseActor(durableId);
        _state.Effects.CancelActorReferences(durableId);
        DestroyOwnedItems(durableId);
        _corpseLoot.Retire(durableId);
        _state.Actors.Entities.Destroy(ActorsState.Identity(durableId));
        BanishedActors.Add(durableId);
        ActorRetired?.Invoke(durableId);
    }

    internal void Retire(long durableId)
    {
        if (durableId == DaggerfallActorIdentity.PlayerEntityId)
            throw new InvalidOperationException("The player actor cannot be retired.");
        if (durableId <= 0) throw new ArgumentOutOfRangeException(nameof(durableId));
        if (!_dynamicActors.Remove(durableId, out _))
        {
            if (_state.Actors.TryGet(durableId, out _))
                throw new InvalidOperationException($"Authored actor {durableId} belongs to the selected content and cannot be retired; only spawned actors retire.");
            DurableIdentityClassification classification = _identities.Classify(new DurableIdentityReference(DurableIdentityKind.Actor, checked((ulong)durableId)));
            throw new InvalidOperationException($"Actor {durableId} is {classification}; only a live spawned actor can be retired.");
        }

        _definitionsByActor.Remove(durableId);
        Appearance.RetireActor(durableId);
        _lootUi.CloseActor(durableId);
        _ = _state.Effects.CancelActorReferences(durableId);
        DestroyOwnedItems(durableId);
        _corpseLoot.Retire(durableId);
        _state.Actors.Entities.Destroy(ActorsState.Identity(durableId));
        _identities.Remove(new DurableIdentityReference(DurableIdentityKind.Actor, checked((ulong)durableId)));
        if (_state.Npcs.All.Any(npc => npc.DurableId == durableId && npc.Kind is DaggerfallNpcKind.Civilian or DaggerfallNpcKind.Static))
            _state.Npcs.SetPresence(durableId, DaggerfallNpcPresence.Removed);
        ActorRetired?.Invoke(durableId);
    }

    /// <summary>
    /// Materializes one registered civilian at an explicit world pose as a runtime civilian dynamic
    /// actor, so its pose, inventory, corpse, and identity ledger participate in the save boundary.
    /// </summary>
    internal long MaterializeCivilian(DaggerfallNpc npc, ActorPose pose)
    {
        ArgumentNullException.ThrowIfNull(npc);
        long npcId = npc.DurableId;
        DurableIdentityReference identity = ActorsState.Identity(npcId);
        if (_identities.Classify(identity) != DurableIdentityClassification.Live)
            throw new InvalidOperationException($"NPC {npcId} does not own a live actor identity.");

        DaggerfallActorDefinition definition = DaggerActorFactory.CivilianDefinition(npcId, npc.Kind == DaggerfallNpcKind.Static);
        ActorState actor = DaggerActorFactory.CreateCivilianActor(_mechanics, _state.Actors, _state.InventoryStore, npc, pose);
        try
        {
            if (!_definitionsByActor.TryAdd(npcId, definition))
                throw new InvalidOperationException($"NPC {npcId} already has a runtime actor definition.");
            if (!_dynamicActors.TryAdd(npcId, definition.Id))
                throw new InvalidOperationException($"NPC {npcId} already has a runtime actor binding.");
            return actor.DurableId;
        }
        catch
        {
            _definitionsByActor.Remove(npcId);
            _dynamicActors.Remove(npcId);
            _state.Actors.Entities.Destroy(identity);
            throw;
        }
    }

    /// <summary>Admits published source people into the same registry, actors, site delta and renderer.</summary>
    internal void MaterializeStaticNpcs(DaggerfallSiteProfile profile, DaggerfallSiteProjection? projection = null)
    {
        if (profile.StaticNpcs.Count == 0) return;
        DaggerfallSiteAppearance appearance = (projection ?? _projection()).Appearance;
        DaggerfallSiteId site = profile.Site!.Value;
        DaggerfallSiteRecord location = _definitions.Locations.Records.Single(location => location.Id == site);
        DaggerfallInteriorBuilding building = profile.InteriorBuilding!;
        DaggerfallNpcSite binding = new(site.Region, location.Name,
            $"{building.BlockX}/{building.BlockY}/{building.Building.Index}", profile.ProfileKey.LogicalId);
        List<long> batch = [];
        try
        {
            foreach (DaggerfallStaticNpcPlacement placement in profile.StaticNpcs)
            {
                long id = _state.Npcs.RegisterStable(DaggerfallNpcKind.Static, placement.Id, binding,
                    placement.Appearance, placement.Role, placement.Services);
                DaggerfallNpc npc = _state.Npcs.Require(id);
                if (npc.Presence != DaggerfallNpcPresence.Active || BanishedActors.Contains(id)) continue;
                bool created = false;
                try
                {
                    if (!_state.Actors.TryGet(id, out _))
                    {
                        MaterializeCivilian(npc, new ActorPose(placement.Position, 0F));
                        created = true;
                    }
                    // Keep the registry's current-profile placement out of a failed candidate. The
                    // placement becomes durable only after the actor's appearance has been admitted;
                    // this also lets a transition retry reuse the same live stable identity.
                    appearance.AdmitActor(id, placement.Sprite);
                    _state.Npcs.Place(id, profile.ProfileKey, placement.Position);
                    batch.Add(id);
                }
                catch
                {
                    // The outer rollback tears down this candidate together with every earlier
                    // member of the admission batch. Keep the stable registry identity so a
                    // transition retry can register the same source person again.
                    if (created || _state.Actors.TryGet(id, out _) || _dynamicActors.ContainsKey(id))
                        batch.Add(id);
                    throw;
                }
            }
        }
        catch
        {
            foreach (long id in batch.Distinct().Reverse())
            {
                _state.Npcs.Unplace(id);
                UnloadActor(id, projection);
            }
            throw;
        }
    }

    /// <summary>
    /// Releases a departing site's actors after their delta was captured: target-bound effects are
    /// suspended, owned item entities released with their durable identities kept for the delta,
    /// and the actor, its corpse container and its appearance retired.
    /// </summary>
    internal void UnloadSite(DaggerfallSiteProfile source, DaggerfallSiteRuntimeDelta? delta,
        DaggerfallSiteProjection? projection = null, IReadOnlySet<long>? actorIds = null)
    {
        IEnumerable<long> authored = source.Project.Actors.Keys;
        IEnumerable<long> dynamic = delta?.DynamicActors.Select(actor => actor.EntityId) ?? [];
        long[] ids = actorIds is null
            ? [.. authored.Concat(dynamic).Order()]
            : [.. actorIds.Order()];
        // The delta was captured while these actors and their target-bound contributions were live.
        // Detach every target lifecycle before destroying Engine entities; effects on a live player
        // with one of these actors as caster remain active by durable caster identity.
        _ = _state.Effects.SuspendTargets(ids);
        foreach (long id in ids) UnloadActor(id, projection);
    }

    /// <summary>Detaches one canonical actor while its values and identities remain in a site delta.</summary>
    internal void UnloadActor(long id, DaggerfallSiteProjection? projection = null)
    {
        _ = _state.Effects.SuspendTargets([id]);
        if (!_state.Actors.TryGet(id, out ActorState? actor)) return;
        _lootUi.CloseActor(actor.DurableId);
        DestroySiteOwnedUniqueItems(actor);
        _definitionsByActor.Remove(actor.DurableId);
        _dynamicActors.Remove(actor.DurableId);
        // Authored and spawned placements share the same appearance owner. A cell unload must
        // retire both kinds so no sprite/animation wrapper outlives its admitted location.
        (projection?.Appearance ?? Appearance).RetireActor(actor.DurableId);
        _state.ItemInstances.RemoveOwner(DaggerfallItemOwner.Actor(actor.DurableId), retireBindings: false);
        _state.ItemInstances.RemoveOwner(DaggerfallItemOwner.Corpse(actor.DurableId), retireBindings: false);
        _corpseLoot.Unload(actor.DurableId);
        _state.Actors.Entities.Destroy(ActorsState.Identity(actor.DurableId));
    }

    /// <summary>
    /// Creates a destination site's authored placements and its retained spawned actors. The
    /// caller restores the delta's inventories, corpses and effects onto them afterwards.
    /// </summary>
    internal void MaterializeSite(DaggerfallSiteProfile destination, DaggerfallSiteRuntimeDelta? delta,
        bool restoreAuthoredAppearance = false, DaggerfallSiteProjection? projection = null)
    {
        // Banishment is a session-wide durable identity fact. A second resident profile must not
        // clear an earlier profile's tombstones while it is being admitted.
        if (delta is not null) BanishedActors.UnionWith(delta.BanishedActors);
        DaggerfallSiteAppearance appearance = (projection ?? _projection()).Appearance;
        Dictionary<long, DaggerfallActorSave> saved = delta?.Actors.ToDictionary(value => value.EntityId) ?? [];
        foreach (AuthoredActor placement in destination.Project.Actors.Values.OrderBy(value => value.EntityId))
        {
            if (BanishedActors.Contains(placement.EntityId)) continue;
            saved.TryGetValue(placement.EntityId, out DaggerfallActorSave? prior);
            DaggerfallActorDefinition definition = prior?.WabbajackDefinition is { } transformed
                ? DaggerfallWabbajack.RequireDefinition(_definitions, transformed) : _definitions.RequireActor(placement.ActorId);
            ActorState actor = DaggerActorFactory.CreateAuthoredActor(_random, _mechanics, _definitions, _state.Actors, _state.InventoryStore,
                _state.ActorInventories.ItemDefinitions, _state.ItemInstances, placement, prior);
            if (prior is not null) actor.ApplyPose(new ActorPose(new WorldPoint(prior.X, prior.Y, prior.Z), prior.HeadingRadians));
            else if (definition.GroundOnSpawn) _grounding.Ground(actor);
            _definitionsByActor.Add(actor.DurableId, definition);
            if (restoreAuthoredAppearance && definition.MobileId is int authoredMobile)
            {
                if (!destination.MobileSprites.TryGetValue(authoredMobile, out NormalizedActorSprite? sprite))
                    throw new InvalidOperationException($"Restored authored actor '{placement.EntityId}' has no admitted mobile {authoredMobile} presentation.");
                appearance.AddActor(actor.DurableId, sprite);
            }
            if (prior is null) GrantStartingEquipment(actor, definition);
            if (prior?.WabbajackDefinition is not null && definition.MobileId is int changedMobile)
            {
                appearance.RetireActor(actor.DurableId);
                appearance.AddActor(actor.DurableId, destination.MobileSprites[changedMobile]);
            }
        }
        if (delta is null) return;
        foreach (DaggerfallDynamicActorSave savedDynamic in delta.DynamicActors.OrderBy(actor => actor.EntityId))
            MaterializeRetainedActor(savedDynamic, projectAppearance: true, projection: projection);
    }

    /// <summary>Rebuilds a retained actor through the same factory and presentation owner as site re-entry.</summary>
    internal ActorState MaterializeRetainedActor(DaggerfallDynamicActorSave saved, bool projectAppearance = true,
        DaggerfallSiteProjection? projection = null)
    {
        try
        {
            var actor = DaggerActorFactory.CreateDynamicActor(_random, _mechanics, _definitions, _state.Actors, _state.InventoryStore,
                _definitionsByActor, saved);
            _dynamicActors.Add(saved.EntityId, new DaggerfallActorId(saved.Definition));
            if (projectAppearance && _definitionsByActor[saved.EntityId].MobileId is int mobileId)
            {
                DaggerfallSiteProfile presentation = projection?.Inputs ?? _projection().Inputs;
                if (!presentation.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
                    throw new InvalidOperationException($"Restored dynamic actor '{saved.Definition}' has no admitted mobile {mobileId} presentation.");
                (projection?.Appearance ?? Appearance).AddActor(saved.EntityId, sprite);
            }
            return actor;
        }
        catch { UnloadActor(saved.EntityId, projection); throw; }
    }

    /// <summary>
    /// The living creatures a worn enchantment's near-creature condition can see: the group the
    /// ruleset's own enemy-group policy gives them, and where they stand now. The donor flags a
    /// civilian NPC as a humanoid outright, so a civilian definition answers humanoid here too
    /// rather than falling through the enemy-group policy's monster and class arms.
    /// </summary>
    /// <remarks>
    /// Static over the shared definition map because held enchantments first read it while the
    /// session is still composing, before the roster's own presentation dependencies exist.
    /// </remarks>
    internal static IReadOnlyList<DaggerfallNearbyCreature> NearbyCreatures(ActorsState actors,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> definitionsByActor,
        Func<long, bool>? actorGameplayActive = null)
    {
        actorGameplayActive ??= _ => true;
        List<DaggerfallNearbyCreature> nearby = [];
        foreach (ActorState actor in actors.All)
        {
            if (actor.IsDefeated) continue;
            if (!actorGameplayActive(actor.DurableId)) continue;
            if (!definitionsByActor.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition)) continue;
            DaggerfallEnemyGroup group = definition.Kind is DaggerfallActorKinds.Civilian or DaggerfallActorKinds.StaticNpc
                ? DaggerfallEnemyGroup.Humanoid
                : DaggerfallFormulaPolicy.EnemyGroupFor(definition);
            nearby.Add(new DaggerfallNearbyCreature(group, actor.Position));
        }
        return nearby;
    }

    /// <summary>Releases live Engine item entities while preserving their durable identities for an inactive-site restore.</summary>
    private void DestroySiteOwnedUniqueItems(ActorState actor)
    {
        List<ulong> identities = [];
        if (_state.ActorInventories.InventoryFor(actor.DurableId) is { } inventory)
            identities.AddRange(inventory.Read().UniqueItems.Select(item => _state.Actors.Entities.IdentityOf(item.Entity).Value));
        if (_corpseLoot.Corpses.TryGetValue(actor.DurableId, out CorpseContainer? corpse) && corpse.IsRegistered)
            identities.AddRange(_state.Containers.Read(corpse.Owner).UniqueItems.Select(item => _state.Actors.Entities.IdentityOf(item.Entity).Value));
        RetireInventoryOwners(actor);
        foreach (ulong itemId in identities.Distinct())
        {
            _state.ItemInstances.RemoveUnique(itemId);
            _state.Actors.Entities.Destroy(new DurableIdentityReference(DurableIdentityKind.Item, itemId));
        }
    }

    private DaggerfallVitalValues SpawnVitals(DaggerfallActorDefinition definition, int level, long durableId)
    {
        if (definition.Kind == DaggerfallActorKinds.EnemyClass && definition.HitPointsPerLevel is int hitPointsPerLevel)
        {
            // Each level roll draws under its own key: a keyed draw is deterministic per key, so
            // reusing one key would repeat the same roll for every level.
            int rollIndex = 0;
            int health = DaggerfallFormulaPolicy.RollEnemyClassMaxHealth(level, hitPointsPerLevel, (minimum, maximum) =>
                checked((int)_random.DrawKeyed(new KeyedRngRequest(
                    CombatRandomKey.Seed,
                    CombatRandomKey.EnemyScope,
                    CombatRandomKey.ClassHealth(durableId, definition.Id.Value, rollIndex++),
                    minimum,
                    maximum)).Value));
            return new DaggerfallVitalValues(health, 0, 0);
        }

        return DaggerActorFactory.InitialVitals(_random, definition, durableId);
    }

    /// <summary>Initial site construction uses the same starting items as later actor admission.</summary>
    internal void GrantInitialAuthoredEquipment()
    {
        foreach (var entry in _definitionsByActor.OrderBy(entry => entry.Key))
            if (!_dynamicActors.ContainsKey(entry.Key))
                GrantStartingEquipment(_state.Actors.Get(entry.Key), entry.Value);
    }

    private void GrantStartingEquipment(ActorState actor, DaggerfallActorDefinition definition)
    {
        if (definition.Kind != DaggerfallActorKinds.EnemyClass && definition.MobileId is not (7 or 8 or 12)) return;
        if (definition.MobileId is not int mobileId) throw new InvalidOperationException($"Class actor '{definition.Id.Value}' has no equipment mobile id.");
        MechanicsInventoryCoordinator inventory = _state.ActorInventories.InventoryFor(actor.DurableId)
            ?? throw new InvalidOperationException($"Spawned actor {actor.DurableId} has no registered inventory.");
        DaggerfallClassEnemyEquipmentPolicy.Equip(_definitions, _random, _state.ItemInstances, _uniqueItems, inventory, _state.ActorInventories.EquipmentFor(actor.DurableId),
            actor.DurableId, mobileId, _state.Progression.Level,
            _state.Character.Identity.RaceId,
            _state.Character.Identity.Gender == DaggerfallCharacterGender.Female ? "female" : "male",
            fixedVariant: mobileId switch { 7 => 0, 8 or 12 => 1, _ => null });
        DaggerfallClassEnemyEquipmentPolicy.CoatStartingWeapon(_random, _state.ItemInstances,
            _state.ActorInventories.EquipmentFor(actor.DurableId), actor.DurableId, mobileId, _state.Progression.Level);
    }

    private void GrantSpawnLoadout(ActorState actor, DaggerfallActorDefinition definition)
    {
        if (definition.Loadout.Count == 0) return;
        foreach (DaggerfallLoadoutEntry entry in definition.Loadout)
        {
            if (!_definitions.Items.TryGetValue(entry.ItemId, out DaggerfallItemDefinition? item) || !item.IsFungible)
                throw new InvalidOperationException($"Spawned actor '{definition.Id.Value}' loadout carries a unique or missing item, which spawned actors do not equip yet.");
        }

        MechanicsInventoryCoordinator actorInventory = _state.ActorInventories.InventoryFor(actor.DurableId)
            ?? throw new InvalidOperationException($"Spawned actor {actor.DurableId} has no registered inventory.");
        int ordinal = 0;
        foreach (DaggerfallLoadoutEntry entry in definition.Loadout)
        {
            InventoryStackId stackId = DaggerfallInventoryStackIds.ForSpawnLoadout(actor.DurableId, ordinal++);
            actorInventory.Grant(new InventoryGrant(new InventoryItemId(entry.ItemId.Value), stackId, entry.Quantity));
            _state.ItemInstances.RegisterDefaultStack(DaggerfallItemOwner.Actor(actor.DurableId),
                new InventoryStack(stackId, ItemDefinitionId.Parse(entry.ItemId.Value), entry.Quantity), _definitions.Items[entry.ItemId]);
        }
    }

    /// <summary>
    /// Empties the retiring actor's inventory, equipment and corpse container and retires those owners
    /// from the shared inventory store in one edit, so the store keeps no registration for an actor that
    /// no longer exists. The store retires only an empty owner, so every assignment, unique item and
    /// stack goes through its own call first; the item identities and metadata are retired separately.
    /// </summary>
    private void RetireInventoryOwners(ActorState actor)
    {
        InventoryStore store = _state.InventoryStore;
        List<EntityId> owners = [actor.Actor.Entity];
        if (actor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse) && corpse is { HasRegisteredInventory: true })
            owners.Add(corpse.Owner);
        using InventoryEdit edit = store.Prepare();
        foreach (EntityId owner in owners)
        {
            if (!store.TryGetInventory(owner, out _)) continue;
            if (store.TryGetEquipment(owner, out EquipmentState? equipment) && equipment is not null)
                foreach (Rusty.Engine.Mechanics.EquipmentAssignment assignment in equipment.Assignments.DistinctBy(assignment => assignment.Item))
                    edit.Unequip(owner, assignment.Item);
            InventoryView view = store.View(owner);
            foreach (Rusty.Engine.Mechanics.UniqueInventoryItem item in view.UniqueItems) edit.DestroyUnique(item.Entity);
            foreach (InventoryStack stack in view.Stacks) edit.Consume(owner, stack.Id, stack.Quantity);
            edit.RetireOwner(owner);
        }
        edit.Publish();
    }

    /// <summary>
    /// Destroys the unique items one retiring actor owns — carried, equipped, and corpse-seeded —
    /// and tombstones their identities so the save never reissues them. Fungible stacks and their instance metadata retire with the actor.
    /// </summary>
    private void DestroyOwnedItems(long durableId)
    {
        if (!_state.Actors.TryGet(durableId, out ActorState? actor)) return;
        HashSet<ulong> owned = [];
        if (_state.ActorInventories.InventoryFor(durableId) is { } inventory)
            foreach (var item in inventory.Read().UniqueItems)
                owned.Add(_state.Actors.Entities.IdentityOf(item.Entity).Value);
        // Equipment carries runtime item references; metadata and the allocator use durable identities.
        foreach (var assignment in _state.ActorInventories.EquipmentFor(durableId).Read().Assignments)
            owned.Add(_state.Actors.Entities.IdentityOf(new EntityId(assignment.Item.EntityId)).Value);
        if (actor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse) && corpse is not null && corpse.HasRegisteredInventory)
            foreach (var item in _state.Containers.Read(corpse.Owner).UniqueItems)
                owned.Add(_state.Actors.Entities.IdentityOf(item.Entity).Value);
        RetireInventoryOwners(actor);
        _state.ItemInstances.RemoveOwner(DaggerfallItemOwner.Actor(durableId));
        _state.ItemInstances.RemoveOwner(DaggerfallItemOwner.Corpse(durableId));
        foreach (ulong itemId in owned)
        {
            _state.ItemInstances.RemoveUnique(itemId);
            _ = _state.Effects.CancelItemReferences(itemId);
            DurableIdentityReference reference = new(DurableIdentityKind.Item, itemId);
            _state.Actors.Entities.Destroy(reference);
            // Authored reservations stay reserved: the destroyed entity is gone either way, and
            // tombstoning one would misreport content as removed. This is the same rule as
            // RemoveUniqueItemIdentity, minus the throw — retirement must not fail midway.
            if (!_authoredEntityIds.Contains(itemId)) _uniqueItems.Remove(reference);
        }
    }
}

/// <summary>
/// Settles a placed or spawned actor onto the floor below its authored marker through the Engine's
/// own spatial ray query.
/// </summary>
internal sealed class DaggerfallActorGrounding(ISpatialService spatialService, SpatialMovementSystem spatial, float probeLift, double probeDistance)
{
    internal void Ground(ActorState actor)
    {
        // RDB marker heights are probe origins, not floor contacts. Unlike DFU's centered
        // capsule, our navigation pose is the sprite's base.
        actor.ApplyPose(new ActorPose(GroundPosition(actor.Position), actor.HeadingYawRadians));
    }

    internal WorldPoint GroundPosition(WorldPoint position, double? maximumDistance = null)
    {
        SpatialHit floor = spatialService.CastRay(new SpatialRaycastRequest(
            spatial.Session,
            position.ToVector() + Vector3.UnitY * probeLift,
            -Vector3.UnitY,
            maximumDistance ?? probeDistance,
            new SpatialQueryFilter(uint.MaxValue, uint.MaxValue), default, default, default));
        return floor.Present && !floor.StartSolid && floor.Normal.Y > 0f ? WorldPoint.From(floor.Point) : position;
    }
}
