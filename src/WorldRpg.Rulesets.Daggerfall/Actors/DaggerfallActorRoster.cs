using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Loot;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The session's live non-player actor roster beside <see cref="DaggerfallActorFactory"/>: which
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
    /// <summary>
    /// The first identity a spawned actor takes. Dynamic actors share one numeric base with generated unique
    /// items; durable kinds keep them distinct, the way a corpse container already shares its actor's number
    /// under a different kind.
    /// </summary>
    internal const ulong DynamicFirstIdentity = 1_000_000_000_000UL;

    private readonly ActorsState _actors;
    private readonly DaggerfallActorInventories _actorInventories;
    private readonly DaggerfallCharacterState _character;
    private readonly MechanicsInventoryContainerCoordinator _containers;
    private readonly DaggerfallEffectLifecycle _effects;
    private readonly InventoryStore _inventoryStore;
    private readonly DaggerfallItemInstances _itemInstances;
    private readonly DaggerfallNpcRegistry _npcs;
    private readonly DaggerfallDefinitions _definitions;
    private readonly IRandomService _random;
    private readonly DaggerfallMechanicsState _mechanics;
    private readonly DurableIdentityAllocator _identities;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly Dictionary<long, DaggerfallActorDefinition> _definitionsByActor;
    private readonly Dictionary<long, DaggerfallActorId> _dynamicActors;
    private readonly DaggerfallActorGrounding _grounding;
    private readonly Func<DaggerfallSiteProfile> _profile;
    private readonly Func<DaggerfallSiteAppearance> _appearance;
    private readonly Action<long> _closeLoot;
    private readonly DaggerfallCorpseLootModule _corpseLoot;
    private readonly DaggerfallActorLedger _actorLedger;
    private readonly Action<DaggerfallActorInventorySave?, DaggerfallCorpseSave?, DaggerfallActiveEffectSave[]?>? _restoreDetachedState;

    /// <param name="profile">The active site's profile.</param>
    /// <param name="appearance">The active site's appearance.</param>
    /// <param name="closeLoot">Closes an open loot window on an actor that leaves.</param>
    internal DaggerfallActorRoster(ActorsState actors, DaggerfallActorInventories actorInventories, DaggerfallCharacterState character,
        MechanicsInventoryContainerCoordinator containers, DaggerfallEffectLifecycle effects, InventoryStore inventoryStore,
        DaggerfallItemInstances itemInstances, DaggerfallNpcRegistry npcs, DaggerfallDefinitions definitions, IRandomService random,
        DaggerfallMechanicsState mechanics, DurableIdentityAllocator identities, DaggerfallUniqueItemAllocator uniqueItems,
        Dictionary<long, DaggerfallActorDefinition> definitionsByActor,
        IEnumerable<DaggerfallDynamicActorSave> restoredDynamicActors, DaggerfallActorGrounding grounding,
        Func<DaggerfallSiteProfile> profile, Func<DaggerfallSiteAppearance> appearance, Action<long> closeLoot, DaggerfallCorpseLootModule corpseLoot,
        DaggerfallActorLedger? actorLedger = null,
        Action<DaggerfallActorInventorySave?, DaggerfallCorpseSave?, DaggerfallActiveEffectSave[]?>? restoreDetachedState = null)
    {
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _actorInventories = actorInventories ?? throw new ArgumentNullException(nameof(actorInventories));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _inventoryStore = inventoryStore ?? throw new ArgumentNullException(nameof(inventoryStore));
        _itemInstances = itemInstances ?? throw new ArgumentNullException(nameof(itemInstances));
        _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _mechanics = mechanics ?? throw new ArgumentNullException(nameof(mechanics));
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _uniqueItems = uniqueItems ?? throw new ArgumentNullException(nameof(uniqueItems));
        _definitionsByActor = definitionsByActor ?? throw new ArgumentNullException(nameof(definitionsByActor));
        _grounding = grounding ?? throw new ArgumentNullException(nameof(grounding));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        _closeLoot = closeLoot ?? throw new ArgumentNullException(nameof(closeLoot));
        _corpseLoot = corpseLoot ?? throw new ArgumentNullException(nameof(corpseLoot));
        _actorLedger = actorLedger ?? new DaggerfallActorLedger();
        _restoreDetachedState = restoreDetachedState;
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

    private DaggerfallSiteAppearance Appearance => _appearance();

    /// <summary>Replaces the runtime entity at one durable actor identity; no hidden original remains.</summary>
    internal DaggerfallWabbajackResult Transform(long durableId, int selectedMobile)
    {
        if (!_actors.TryGet(durableId, out var original) || !_definitionsByActor.TryGetValue(durableId, out var oldDefinition)
            || oldDefinition.Kind is not (DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass))
            return new(DaggerfallWabbajackOutcome.InvalidTarget, durableId);
        if (DaggerfallWabbajack.DefinitionOf(original.Actor) is not null)
            return new(DaggerfallWabbajackOutcome.AlreadyTransformed, durableId);
        DaggerfallActorDefinition definition = _definitions.Actors.Values.Single(value => value.MobileId == selectedMobile);
        _ = DaggerfallWabbajack.RequireDefinition(_definitions, definition.Id.Value);
        if (oldDefinition.Team == "player-ally") definition = definition with { Team = "player-ally" };
        if (!_profile().MobileSprites.TryGetValue(selectedMobile, out var sprite))
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
        _closeLoot(durableId);
        _ = _effects.CancelActorReferences(durableId);
        DestroyOwnedItems(durableId);
        _corpseLoot.Retire(durableId);
        Appearance.RetireActor(durableId);
        _actors.Entities.Destroy(ActorsState.Identity(durableId));
        ActorState replacement = DaggerfallActorFactory.CreateNonPlayerActor(_actors, durableId, definition, stats, pose);
        DaggerfallActorFactory.RegisterActorInventory(replacement, _inventoryStore);
        DaggerfallWabbajack.Restore(replacement.Actor, definition.Id.Value);
        if (origin is not null) replacement.Actor.Add(origin);
        GrantSpawnLoadout(replacement, definition);
        _definitionsByActor[durableId] = definition;
        if (_dynamicActors.ContainsKey(durableId)) _dynamicActors[durableId] = definition.Id;
        Appearance.AddActor(durableId, sprite, definition.MobileId);
        return new(DaggerfallWabbajackOutcome.Transformed, durableId, definition.Id.Value);
    }

    /// <summary>
    /// Promotes a live town guard NPC in place to an active city-watch combatant on the same durable entity:
    /// upgrades stats to levelled watch class stats, updates capability policies, faction, overlay archetype,
    /// Tier-2 render spec (<see cref="ActorVisualKind.DirectionalMobile"/> 146), appearance, and watch gear.
    /// </summary>
    internal long? PromoteToCityWatch(long actorId, bool grantEquipment = true, DaggerfallSiteAppearance? siteAppearance = null)
    {
        if (!_actors.TryGet(actorId, out ActorState? actor) || actor.IsDefeated)
            return null;
        return ApplyCityWatchPromotion(actor, grantEquipment, swapAppearance: true, _profile(), siteAppearance);
    }

    private long ApplyCityWatchPromotion(ActorState actor, bool grantEquipment, bool swapAppearance,
        DaggerfallSiteProfile? siteProfile = null, DaggerfallSiteAppearance? siteAppearance = null)
    {
        long actorId = actor.DurableId;
        if (_definitionsByActor.TryGetValue(actorId, out DaggerfallActorDefinition? currentDef)
            && currentDef.MobileId == 146
            && actor.Actor.TryGet<ActorHarmPolicy>(out ActorHarmPolicy? existingHarm)
            && existingHarm.IsCombatant)
        {
            return actorId;
        }

        DaggerfallActorDefinition baseWatch = _definitions.Actors.Values.Single(value => value.MobileId == 146);
        int baseLevel = baseWatch.Level ?? _actors.Player.Progression.Level;
        int spawnLevel = checked(baseLevel + (int)_random.DrawKeyed(new KeyedRngRequest(
            DaggerfallRandomKey.Seed, CombatRandomKey.EnemyScope,
            $"class-guard-level:{actorId}", 3, 6)).Value);
        DaggerfallActorDefinition watchDef = DaggerfallEncounterActors.AtLevel(baseWatch, _definitions.Vocabulary, spawnLevel);

        StatsComponent watchStats = _mechanics.CreateStats(watchDef, SpawnVitals(watchDef, spawnLevel, actorId));
        actor.Actor.Replace(watchStats);
        if (actor.Actor.Has<DaggerfallRestoredStats>())
            actor.Actor.Remove<DaggerfallRestoredStats>();

        string npcGender = _npcs.TryGet(actorId, out DaggerfallNpc? npc) ? npc.Appearance.Gender : "unspecified";
        DaggerfallSiteProfile activeProfile = siteProfile ?? _profile();
        NormalizedActorSprite? mobileSprite = activeProfile.MobileSprites.GetValueOrDefault(146);
        string gender = mobileSprite is not null
            ? DaggerfallActorGender.Resolve(_random, actorId, npcGender, mobileSprite)
            : string.Equals(npcGender, "female", StringComparison.OrdinalIgnoreCase) ? "female" : "male";
        if (actor.Actor.Has<DaggerfallActorGender>())
            actor.Actor.Replace(new DaggerfallActorGender(gender));
        else
            actor.Actor.Add(new DaggerfallActorGender(gender));

        _definitionsByActor[actorId] = watchDef;
        if (_dynamicActors.ContainsKey(actorId))
            _dynamicActors[actorId] = watchDef.Id;

        ArchetypeProfile guardProfile = _definitions.People.RequireArchetype(DaggerfallArchetypeIds.Guard);
        actor.Actor.Get<ActorHarmPolicy>().Set(guardProfile.DefaultHarm);
        actor.Actor.Get<ActorInteractionPolicy>().Set(guardProfile.DefaultInteraction);
        actor.Actor.Get<FactionComponent>().SetCreatureFaction("city-watch");
        actor.Actor.Get<ActorOverlayComponent>().ArchetypeId = guardProfile.Id;
        actor.Actor.Get<ActorRenderComponent>().SetVisual(ActorVisualSpec.DirectionalMobile(146, gender));
        if (actor.Actor.TryGet<WanderMemoryComponent>(out WanderMemoryComponent? wander))
            wander.MarkUnloaded();

        if (swapAppearance && mobileSprite is not null)
        {
            DaggerfallSiteAppearance appearance = siteAppearance ?? Appearance;
            appearance.RetireActor(actorId);
            appearance.AddActor(actorId, mobileSprite.ForGender(gender), 146);
        }

        if (grantEquipment && _actorInventories.EquipmentFor(actorId).Read().Assignments.Count == 0)
        {
            GrantSpawnLoadout(actor, watchDef);
            GrantStartingEquipment(actor, watchDef);
        }

        return actorId;
    }

    private bool TryAdmitDirectionalMobileAppearance(DaggerfallSiteProfile profile, DaggerfallSiteAppearance appearance, long actorId)
    {
        if (!_actors.TryGet(actorId, out ActorState? actor)
            || !actor.Actor.TryGet<ActorRenderComponent>(out ActorRenderComponent? render)
            || render.Spec.Kind != ActorVisualKind.DirectionalMobile
            || render.Spec.MobileId is not int mobileId)
            return false;
        if (profile.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
        {
            string gender = actor.Actor.TryGet<DaggerfallActorGender>(out DaggerfallActorGender? resolvedGender)
                ? resolvedGender.Value
                : render.Spec.Gender ?? "unspecified";
            appearance.AddActor(actorId, sprite.ForGender(gender), mobileId);
        }
        return true;
    }

    /// <summary>
    /// Registers one actor from a published definition beyond the authored placements, with the
    /// same Mechanics binding an authored actor is constructed with: catalog stats, pursuit
    /// memory, managed inventory and equipment, definition loadout, and floor grounding.
    /// Returns the allocated durable identity, which the save persists and restore reuses.
    /// </summary>
    internal long Spawn(string definitionId, ActorPose pose, int? level = null, bool playerAllied = false, int spawnDistance = 0, bool passive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        DaggerfallActorDefinition definition = _definitions.RequireActor(new DaggerfallActorId(definitionId));
        if (definition.Kind == DaggerfallActorKinds.Player)
            throw new InvalidOperationException("The player actor is authored once; it cannot be spawned.");
        if (level is < 1) throw new ArgumentOutOfRangeException(nameof(level));
        if (spawnDistance is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(spawnDistance));
        // Class enemies level to the player the way the donor levels them; monsters carry their
        // own level. Every enemy skill starts from that level before caster school overrides.
        int spawnLevel = level ?? definition.Level ?? (definition.Kind == DaggerfallActorKinds.EnemyClass ? _actors.Player.Progression.Level : 1);
        DurableIdentityReference identity = _identities.Allocate(DurableIdentityKind.Actor);
        long durableId = checked((long)identity.Value);
        if (definition.Kind == DaggerfallActorKinds.EnemyClass && definition.MobileId == 146)
            spawnLevel = checked(spawnLevel + (int)_random.DrawKeyed(new KeyedRngRequest(DaggerfallRandomKey.Seed, CombatRandomKey.EnemyScope,
                $"class-guard-level:{durableId}", 3, 6)).Value);
        DaggerfallActorDefinition spawnedDefinition = DaggerfallEncounterActors.AtLevel(definition, _definitions.Vocabulary, spawnLevel);
        if (playerAllied) spawnedDefinition = spawnedDefinition with { Team = "player-ally" };
        bool registered = false;
        try
        {
            ActorState actor = DaggerfallActorFactory.CreateNonPlayerActor(_actors, durableId, spawnedDefinition,
                _mechanics.CreateStats(spawnedDefinition, SpawnVitals(spawnedDefinition, spawnLevel, durableId)),
                pose);
            actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().SpawnDistance = spawnDistance;
            actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Passive = passive;
            DaggerfallActorFactory.RegisterActorInventory(actor, _inventoryStore);
            GrantSpawnLoadout(actor, spawnedDefinition);
            GrantStartingEquipment(actor, spawnedDefinition);
            string? resolvedGender = null;
            if (spawnedDefinition.MobileId is int mobileId)
            {
                if (!_profile().MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
                    throw new InvalidOperationException($"Spawned actor '{spawnedDefinition.Id.Value}' has no admitted mobile {mobileId} presentation.");
                resolvedGender = DaggerfallActorGender.Resolve(_random, durableId, "unspecified", sprite);
                actor.Actor.Add(new DaggerfallActorGender(resolvedGender));
                Appearance.AddActor(durableId, sprite.ForGender(resolvedGender), definition.MobileId);
            }
            actor.Actor.Add(new ActorOriginComponent(
                ActorOrigin.MintedActor(spawnedDefinition.Id.Value, spawnLevel, gender: resolvedGender),
                pose.Position, pose.HeadingYawRadians));
            _definitionsByActor.Add(durableId, spawnedDefinition);
            _dynamicActors.Add(durableId, spawnedDefinition.Id);
            registered = true;
            if (_definitions.Walks(definition)) _grounding.Ground(actor);
            return durableId;
        }
        catch
        {
            if (registered)
            {
                _definitionsByActor.Remove(durableId);
                _dynamicActors.Remove(durableId);
            }

            _actors.Entities.Destroy(identity);
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
        if (!_actors.TryGet(durableId, out var actor) || actor.IsDefeated) return;
        RemoveQuestActor(durableId);
    }

    /// <summary>Banishes every monster and enemy-class actor, as a vampire's awakening leaves none behind.</summary>
    internal void BanishHostiles()
    {
        foreach (long id in _definitionsByActor.Where(pair => pair.Key != DaggerfallActorIdentity.PlayerEntityId
            && pair.Value.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass).Select(pair => pair.Key).ToArray()) Banish(id);
    }

    /// <summary>Explicit quest removal also removes a defeated actor and its corpse, without inventing a death.</summary>
    internal void RemoveQuestActor(long durableId)
    {
        if (_dynamicActors.ContainsKey(durableId)) { Retire(durableId); return; }
        if (durableId == DaggerfallActorIdentity.PlayerEntityId) throw new InvalidOperationException("The player cannot be removed by a quest foe action.");
        if (!_actors.TryGet(durableId, out _)) throw new InvalidOperationException($"Quest actor {durableId} is not materialized.");
        _definitionsByActor.Remove(durableId);
        Appearance.RetireActor(durableId);
        _closeLoot(durableId);
        _effects.CancelActorReferences(durableId);
        DestroyOwnedItems(durableId);
        _corpseLoot.Retire(durableId);
        _actors.Entities.Destroy(ActorsState.Identity(durableId));
        BanishedActors.Add(durableId);
        _actorLedger.MarkRemoved(durableId, ActorOrigin.FixedPlacement(_profile().ProfileKey, durableId));
        _actorLedger.StripDetachedEffectsOf(durableId);
        ActorRetired?.Invoke(durableId);
    }

    internal void Retire(long durableId)
    {
        if (durableId == DaggerfallActorIdentity.PlayerEntityId)
            throw new InvalidOperationException("The player actor cannot be retired.");
        if (durableId <= 0) throw new ArgumentOutOfRangeException(nameof(durableId));
        bool wasDynamic = _dynamicActors.Remove(durableId, out _);
        ActorOriginComponent? detOriginComp = null;
        bool isDeterministic = !wasDynamic
            && _actors.TryGet(durableId, out ActorState? detActor)
            && detActor.Actor.TryGet<ActorOriginComponent>(out detOriginComp)
            && !detOriginComp.Origin.IsFixedPlacement
            && detOriginComp.Origin.Kind is ActorOriginKind.SeededSlot or ActorOriginKind.CastIndividual;
        if (!wasDynamic && !isDeterministic)
        {
            if (_actors.TryGet(durableId, out _))
                throw new InvalidOperationException($"Authored actor {durableId} belongs to the selected content and cannot be retired; only spawned actors retire.");
            DurableIdentityClassification classification = _identities.Classify(new DurableIdentityReference(DurableIdentityKind.Actor, checked((ulong)durableId)));
            throw new InvalidOperationException($"Actor {durableId} is {classification}; only a live spawned actor can be retired.");
        }

        ActorOrigin? deterministicOrigin = isDeterministic ? detOriginComp?.Origin : null;
        ActorPersonComponent? retiringPerson = _actors.TryGet(durableId, out ActorState? retiringActor)
            && retiringActor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? pc)
            ? pc
            : null;
        ActorOrigin? personOrigin = deterministicOrigin
            ?? (retiringActor is not null && retiringActor.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? anyOc)
                ? anyOc.Origin
                : null);
        _definitionsByActor.Remove(durableId);
        Appearance.RetireActor(durableId);
        _closeLoot(durableId);
        _ = _effects.CancelActorReferences(durableId);
        DestroyOwnedItems(durableId);
        _corpseLoot.Retire(durableId);
        _actors.Entities.Destroy(ActorsState.Identity(durableId));
        if (wasDynamic)
            _identities.Remove(new DurableIdentityReference(DurableIdentityKind.Actor, checked((ulong)durableId)));
        if (retiringPerson is not null)
        {
            _actorLedger.TryGet(durableId, out ActorLedgerEntry? priorPerson);
            ActorOrigin resolvedOrigin = personOrigin
                ?? priorPerson?.Origin
                ?? ActorOrigin.MintedActor(
                    retiringPerson.Snapshot.Kind == DaggerfallNpcKind.Civilian ? DaggerfallActorKinds.Civilian : DaggerfallActorKinds.StaticNpc,
                    gender: retiringPerson.Snapshot.Appearance.Gender);
            ActorSocialDiff removedSocial = ActorSocialDiff.FromNpc(
                retiringPerson.Snapshot with { Presence = DaggerfallNpcPresence.Removed }, priorPerson?.Social);
            _actorLedger.MarkRemoved(durableId, resolvedOrigin, removedSocial);
        }
        else if (isDeterministic && deterministicOrigin is not null)
        {
            _actorLedger.MarkRemoved(durableId, deterministicOrigin);
        }
        else
        {
            _actorLedger.Remove(durableId);
        }
        _actorLedger.StripDetachedEffectsOf(durableId);
        ActorRetired?.Invoke(durableId);
    }

    /// <summary>
    /// Materializes one registered NPC (civilian, static, or questor) at an explicit world pose as a canonical
    /// active-scene actor carrying real stats, faction, capability policies, and Tier-2 render component.
    /// </summary>
    internal long MaterializeCivilian(DaggerfallNpc npc, ActorPose pose)
    {
        ArgumentNullException.ThrowIfNull(npc);
        long npcId = npc.DurableId;
        DurableIdentityReference identity = ActorsState.Identity(npcId);
        if (_identities.Classify(identity) != DurableIdentityClassification.Live)
            throw new InvalidOperationException($"NPC {npcId} does not own a live actor identity.");

        ActorState actor = DaggerfallActorFactory.CreateCivilianActor(_mechanics, _actors, _inventoryStore, npc, pose, _definitions, out DaggerfallActorDefinition definition);
        actor.Actor.Add(new ActorOriginComponent(
            ActorOrigin.MintedActor(definition.Id.Value, gender: npc.Appearance.Gender),
            pose.Position, pose.HeadingYawRadians));
        try
        {
            if (!_definitionsByActor.TryAdd(npcId, definition))
                throw new InvalidOperationException($"NPC {npcId} already has a runtime actor definition.");
            if (!_dynamicActors.TryAdd(npcId, definition.Id))
                throw new InvalidOperationException($"NPC {npcId} already has a runtime actor binding.");
            GrantInitialLoadout(actor, definition);
            return actor.DurableId;
        }
        catch
        {
            _definitionsByActor.Remove(npcId);
            _dynamicActors.Remove(npcId);
            _actors.Entities.Destroy(identity);
            throw;
        }
    }

    /// <summary>
    /// Materializes or moves a questor as a real canonical <see cref="ActorState"/> in <c>EntityStore</c> with its
    /// billboard admitted through <see cref="DaggerfallSiteAppearance"/>, replacing the legacy <c>DaggerfallNpcProjection</c>.
    /// </summary>
    internal long MaterializeQuestor(DaggerfallNpc npc, DaggerfallSiteProfile site, ActorPose pose,
        NormalizedBillboardSprite sprite, DaggerfallSiteAppearance? siteAppearance = null)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(sprite);
        DaggerfallSiteAppearance appearance = siteAppearance ?? _appearance();
        long id = npc.DurableId;
        bool created = false;
        try
        {
            if (_actors.TryGet(id, out ActorState? existing))
            {
                existing.ApplyPose(pose);
            }
            else
            {
                MaterializeCivilian(npc, pose);
                created = true;
            }
            appearance.AdmitActor(id, sprite.Standing((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord)));
            return id;
        }
        catch
        {
            if (created) UnloadActor(id, siteAppearance);
            throw;
        }
    }

    /// <summary>
    /// Reconciles active-scene questors against the active profile and escort party status: materializes active
    /// questors for the loaded site as real <see cref="ActorState"/>s and unloads questors that are hidden, removed,
    /// off-site, or currently <see cref="ActorLocationKind.WithParty"/> (HUD escort).
    /// </summary>
    internal void ReconcileQuestActors(DaggerfallSiteProfile active, DaggerfallSiteAppearance appearance, bool locationLoaded,
        Func<WorldPoint, WorldPoint> profileToLocal, Func<long, bool>? isWithParty = null,
        Func<WorldPoint, WorldPoint>? localToProfile = null)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(profileToLocal);

        Dictionary<long, DaggerfallNpc> shouldBeLive = _npcs.All
            .Where(npc => locationLoaded
                && npc.Kind == DaggerfallNpcKind.Questor
                && npc.Profile == active.ProfileKey
                && _npcs.IsGameplayActive(npc.DurableId)
                && isWithParty?.Invoke(npc.DurableId) != true)
            .ToDictionary(npc => npc.DurableId);

        foreach (ActorState actor in _actors.All.ToArray())
        {
            if (actor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay) && overlay.IsQuestor)
            {
                if (!shouldBeLive.ContainsKey(actor.DurableId))
                {
                    if (_npcs.TryGet(actor.DurableId, out DaggerfallNpc? npc)
                        && npc.Profile is { } npcProfile
                        && npc.Presence != DaggerfallNpcPresence.Removed
                        && isWithParty?.Invoke(actor.DurableId) != true
                        && npc.X is null)
                    {
                        _npcs.Place(npc.DurableId, npcProfile, localToProfile?.Invoke(actor.Position) ?? actor.Position);
                    }
                    UnloadActor(actor.DurableId, appearance);
                }
                continue;
            }
            if (_npcs.TryGet(actor.DurableId, out DaggerfallNpc? worldNpc)
                && (worldNpc.Presence == DaggerfallNpcPresence.Removed || isWithParty?.Invoke(actor.DurableId) == true))
            {
                UnloadActor(actor.DurableId, appearance);
            }
        }

        foreach (DaggerfallNpc npc in shouldBeLive.Values.OrderBy(value => value.DurableId))
        {
            if (_actors.TryGet(npc.DurableId, out _))
            {
                if (!appearance.HasActor(npc.DurableId)
                    && active.BillboardSprites.TryGetValue((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord), out NormalizedBillboardSprite? existingSprite))
                    appearance.AdmitActor(npc.DurableId, existingSprite.Standing((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord)));
                continue;
            }
            if (npc.X is not float x || npc.Y is not float y || npc.Z is not float z)
                throw new InvalidOperationException($"Placed NPC {npc.DurableId} has no profile position.");
            if (!active.BillboardSprites.TryGetValue((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord), out NormalizedBillboardSprite? sprite))
                throw new NotSupportedException($"NPC {npc.DurableId} has no published billboard {npc.Appearance.BillboardArchive}/{npc.Appearance.BillboardRecord} at '{active.ProfileKey.LogicalId}'.");
            MaterializeQuestor(npc, active, new ActorPose(profileToLocal(new(x, y, z)), 0f), sprite, appearance);
            _npcs.ReleasePose(npc.DurableId);
        }
    }

    /// <summary>
    /// Three-step deterministic scene admission for published source people and relocated ledger actors:
    /// Step 1 admits home <see cref="ActorOriginKind.SeededSlot"/> placements unless suppressed in <see cref="DaggerfallActorLedger"/>
    /// (relocated, <c>WithParty</c>, <c>OnOverlandBand</c>, <c>OffStage</c>, or <c>Removed</c>), hydrating any sparse deltas;
    /// Step 2 admits inbound relocated <see cref="ActorOriginKind.SeededSlot"/> / <see cref="ActorOriginKind.CastIndividual"/>
    /// entries whose ledger location names <paramref name="profile"/>.
    /// </summary>
    internal void MaterializeStaticNpcs(DaggerfallSiteProfile profile, DaggerfallSiteAppearance? siteAppearance = null, Vector3 translation = default)
    {
        DaggerfallSiteAppearance appearance = siteAppearance ?? _appearance();
        if (profile.StaticNpcs.Count > 0)
        {
            DaggerfallSiteId site = profile.Site!.Value;
            DaggerfallSiteRecord location = _definitions.Locations.Records.Single(location => location.Id == site);
            DaggerfallNpcSite binding = new(site.Region, location.Name,
                profile.InteriorBuilding is { } building ? $"{building.BlockX}/{building.BlockY}/{building.Building.Index}" : string.Empty,
                profile.ProfileKey.LogicalId);
            List<long> batch = [];
            try
            {
                foreach (DaggerfallStaticNpcPlacement placement in profile.StaticNpcs)
                {
                    if (placement.Appearance.FactionId > 0
                        && _definitions.People.TryGetIndividualByFaction(placement.Appearance.FactionId, out DaggerfallCastIndividualDefinition? cast)
                        && cast is not null)
                    {
                        long? castId = MaterializeCastIndividual(cast.Id, profile, placement.Position, appearance, translation, placement);
                        if (castId is long admittedCastId) batch.Add(admittedCastId);
                        continue;
                    }

                    string slotKey = $"{profile.ProfileKey.LogicalId}/{placement.Id}";
                    long id = DaggerfallPeopleSlots.SeededSlotIdentity(site, slotKey);
                    // Step 1: suppress home slot if ledger relocated, WithParty-attached, off-staged, or removed it.
                    if (_actorLedger.IsHomeSlotSuppressed(id, profile.ProfileKey) || BanishedActors.Contains(id))
                        continue;

                    _npcs.RegisterDeterministic(id, DaggerfallNpcKind.Static, placement.Id, binding,
                        placement.Appearance, placement.Role, placement.Services, homeProfile: profile.ProfileKey);
                    DaggerfallNpc npc = _npcs.Require(id);
                    if (npc.Presence != DaggerfallNpcPresence.Active) continue;
                    bool created = false;
                    try
                    {
                        if (!_actors.TryGet(id, out ActorState? actor))
                        {
                            actor = MaterializeDeterministicNpc(npc,
                                ActorOrigin.Seeded(profile.ProfileKey, placement.Id, placement.SpawnerId, placement.Harm, placement.Interaction),
                                placement.Position, translation, _actorLedger.TryGet(id, out ActorLedgerEntry? delta) ? delta : null,
                                siteProfile: profile, staticPlacement: placement);
                            created = true;
                        }
                        if (!TryAdmitDirectionalMobileAppearance(profile, appearance, id))
                            appearance.AdmitActor(id, placement.Sprite);
                        _npcs.Bind(id, profile.ProfileKey);
                        batch.Add(id);
                    }
                    catch
                    {
                        if (created || _actors.TryGet(id, out _) || _dynamicActors.ContainsKey(id))
                            batch.Add(id);
                        throw;
                    }
                }
            }
            catch
            {
                foreach (long id in batch.Distinct().Reverse())
                {
                    _npcs.Unplace(id);
                    UnloadActor(id, siteAppearance);
                }
                throw;
            }
        }

        // Step 2: materialize any inbound relocated SeededSlot or CastIndividual ledger entries at this profile.
        MaterializeRelocatedLedgerActors(profile, appearance, translation);
    }

    /// <summary>Materializes one unique <c>cast/individuals.json</c> individual at <paramref name="profile"/> under Step 1 suppression.</summary>
    internal long? MaterializeCastIndividual(string castId, DaggerfallSiteProfile profile, WorldPoint position,
        DaggerfallSiteAppearance? siteAppearance = null, Vector3 translation = default,
        DaggerfallStaticNpcPlacement? sourcePlacement = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(castId);
        ArgumentNullException.ThrowIfNull(profile);
        if (!_definitions.People.Individuals.TryGetValue(castId, out DaggerfallCastIndividualDefinition? cast))
            throw new KeyNotFoundException($"Cast individual '{castId}' is not defined in daggerfall.people.");

        long id = DaggerfallPeopleSlots.CastIndividualIdentity(castId);
        if (_actorLedger.IsHomeSlotSuppressed(id, profile.ProfileKey) || BanishedActors.Contains(id))
            return null;

        DaggerfallSiteId site = profile.Site!.Value;
        DaggerfallSiteRecord location = _definitions.Locations.Records.Single(record => record.Id == site);
        DaggerfallNpcSite binding = new(site.Region, location.Name,
            profile.InteriorBuilding is { } building ? $"{building.BlockX}/{building.BlockY}/{building.Building.Index}" : string.Empty,
            profile.ProfileKey.LogicalId);
        DaggerfallNpcAppearance look = sourcePlacement?.Appearance
            ?? new DaggerfallNpcAppearance(cast.Race ?? "breton", cast.Gender, cast.Archive, cast.Record, 0, cast.FactionId);
        string role = sourcePlacement?.Role ?? "person";
        IReadOnlyList<string> services = sourcePlacement?.Services ?? ["talk"];
        _npcs.RegisterDeterministic(id, DaggerfallNpcKind.Static, $"cast:{castId}", binding, look, role, services, cast.Name, profile.ProfileKey);
        DaggerfallNpc npc = _npcs.Require(id);
        if (npc.Presence != DaggerfallNpcPresence.Active) return null;
        if (!_actors.TryGet(id, out _))
        {
            ActorHarmPolicyKind harm = sourcePlacement?.Harm ?? DaggerfallPeopleSlots.ParseHarmPolicy(cast.Harm);
            ActorInteractionPolicyKind interaction = sourcePlacement?.Interaction ?? DaggerfallPeopleSlots.ParseInteractionPolicy(cast.Interaction);
            _ = MaterializeDeterministicNpc(npc, ActorOrigin.Cast(castId, profile.ProfileKey, harm, interaction), position, translation,
                _actorLedger.TryGet(id, out ActorLedgerEntry? delta) ? delta : null, siteProfile: profile, staticPlacement: sourcePlacement);
        }
        DaggerfallSiteAppearance appearance = siteAppearance ?? _appearance();
        if (sourcePlacement is not null)
            appearance.AdmitActor(id, sourcePlacement.Sprite);
        else if (profile.BillboardSprites.TryGetValue((cast.Archive, cast.Record), out NormalizedBillboardSprite? sprite))
            appearance.AdmitActor(id, sprite.Standing((cast.Archive, cast.Record)));
        _npcs.Bind(id, profile.ProfileKey);
        return id;
    }

    /// <summary>
    /// Relocates or materializes a unique <c>cast/individuals.json</c> individual at <paramref name="profile"/> under
    /// its canonical deterministic identity, recording an <see cref="ActorLocationOverride.AtSite"/> relocation in
    /// <see cref="DaggerfallActorLedger"/> whenever <paramref name="profile"/> differs from its home site so Step 1
    /// suppresses its home slot and no duplicate body can exist.
    /// </summary>
    internal long? RelocateCastIndividual(
        string castId,
        DaggerfallSiteProfile profile,
        WorldPoint localPose,
        DaggerfallNpcAppearance appearance,
        string? displayName,
        DaggerfallWorldProfileKey? homeProfile,
        Vector3 translation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(castId);
        ArgumentNullException.ThrowIfNull(profile);
        if (!_definitions.People.Individuals.TryGetValue(castId, out DaggerfallCastIndividualDefinition? cast))
            throw new KeyNotFoundException($"Cast individual '{castId}' is not defined in daggerfall.people.");

        long id = DaggerfallPeopleSlots.CastIndividualIdentity(castId);
        if (BanishedActors.Contains(id))
            return null;
        ActorLedgerEntry? priorEntry = _actorLedger.TryGet(id, out ActorLedgerEntry? found) ? found : null;
        if (priorEntry?.Location?.Kind == ActorLocationKind.Dead)
            return null;
        if (_npcs.TryGet(id, out DaggerfallNpc? existingNpc) && existingNpc.Presence == DaggerfallNpcPresence.Removed)
            return null;

        DaggerfallSiteId site = profile.Site!.Value;
        DaggerfallSiteRecord location = _definitions.Locations.Records.Single(record => record.Id == site);
        DaggerfallNpcSite binding = new(site.Region, location.Name,
            profile.InteriorBuilding is { } building ? $"{building.BlockX}/{building.BlockY}/{building.Building.Index}" : string.Empty,
            profile.ProfileKey.LogicalId);
        DaggerfallWorldProfileKey? originHome = priorEntry?.Origin.SiteProfile?.Require() ?? homeProfile;
        bool isHomeSite = originHome == profile.ProfileKey;

        _npcs.RegisterDeterministic(id, DaggerfallNpcKind.Static, $"cast:{castId}", binding, appearance,
            "person", ["talk"], displayName ?? cast.Name, originHome ?? profile.ProfileKey);
        DaggerfallNpc npc = _npcs.Require(id);
        if (npc.Presence == DaggerfallNpcPresence.Removed)
            return null;
        if (npc.Presence != DaggerfallNpcPresence.Active)
            _npcs.SetPresence(id, DaggerfallNpcPresence.Active);

        WorldPoint profilePose = DaggerfallSceneOrigin.Shift(localPose, -translation);
        ActorOrigin origin = priorEntry?.Origin
            ?? ActorOrigin.Cast(castId, originHome, DaggerfallPeopleSlots.ParseHarmPolicy(cast.Harm), DaggerfallPeopleSlots.ParseInteractionPolicy(cast.Interaction));
        ActorLedgerEntry updatedEntry = priorEntry is not null
            ? priorEntry with
            {
                Origin = origin,
                Pose = new ActorPoseOverride(profilePose.X, profilePose.Y, profilePose.Z, 0f),
                Location = isHomeSite ? null : ActorLocationOverride.AtSite(profile.ProfileKey),
                Social = priorEntry.Social is { } s
                    ? s with { DisplayNameOverride = npc.DisplayName, Presence = (int)DaggerfallNpcPresence.Active }
                    : ActorSocialDiff.FromNpc(npc),
            }
            : new ActorLedgerEntry(
                id,
                origin,
                Pose: new ActorPoseOverride(profilePose.X, profilePose.Y, profilePose.Z, 0f),
                Social: ActorSocialDiff.FromNpc(npc),
                Location: isHomeSite ? null : ActorLocationOverride.AtSite(profile.ProfileKey));
        _actorLedger.Upsert(updatedEntry);

        ActorState? materialized = MaterializeLedgerActorAt(updatedEntry, profile, localPose, 0f, translation: translation);
        if (materialized is null)
            return null;
        if (!string.IsNullOrWhiteSpace(displayName))
            _npcs.SetDisplayName(id, displayName);
        _npcs.Bind(id, profile.ProfileKey);
        _npcs.ReleasePose(id);
        return id;
    }

    /// <summary>
    /// Materializes a deterministic <see cref="ActorOriginKind.SeededSlot"/> exterior population civilian without adding
    /// it to <c>_dynamicActors</c>, hydrating any sparse <see cref="DaggerfallActorLedger"/> delta.
    /// <paramref name="frameOffset"/> is the profile-to-local translation, independent of <paramref name="localPose"/>,
    /// so a saved profile pose delta is shifted into local coordinates exactly once.
    /// </summary>
    internal long MaterializeDeterministicCivilian(DaggerfallNpc npc, ActorOrigin origin, WorldPoint baselineProfilePose,
        ActorPose localPose, Vector3 frameOffset)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (_actors.TryGet(npc.DurableId, out _)) return npc.DurableId;
        ActorLedgerEntry? delta = _actorLedger.TryGet(npc.DurableId, out ActorLedgerEntry? existing) ? existing : null;
        ActorState actor = MaterializeDeterministicNpc(npc, origin, baselineProfilePose, frameOffset, delta);
        if (delta?.Pose is null)
            actor.ApplyPose(localPose);
        return actor.DurableId;
    }

    /// <summary>
    /// Materializes any active-site deterministic population civilians referenced by saved ledger, inventory, corpse,
    /// or active-effect records so restore can attach their saved state before population reconciliation runs.
    /// </summary>
    internal void MaterializeActiveRestoredPopulation(DaggerfallSiteProfile profile, IReadOnlySet<long> requiredIds,
        DaggerfallSiteAppearance? siteAppearance = null)
    {
        if (profile.Population.Count == 0 || profile.Site is not { } siteId || requiredIds.Count == 0) return;
        DaggerfallSiteRecord site = _definitions.Locations.Records.Single(record => record.Id == siteId);
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty);
        DaggerfallSiteAppearance appearance = siteAppearance ?? _appearance();
        string? climateRace = DaggerfallSourcePopulation.TryResolveClimateRace(_definitions, site, out _);

        foreach (DaggerfallPopulationPlacement placement in profile.Population)
        {
            string slotKey = $"{profile.ProfileKey.LogicalId}/{placement.Id}";
            long id = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, slotKey);
            if (!requiredIds.Contains(id) || _actors.TryGet(id, out _)) continue;
            if (_actorLedger.IsHomeSlotSuppressed(id, profile.ProfileKey) || BanishedActors.Contains(id)) continue;

            DaggerfallNpc? npc = _npcs.TryGet(id, out DaggerfallNpc? existing) ? existing : null;
            if (npc is null)
            {
                if (climateRace is null || !DaggerfallSourcePopulation.IsEligiblePlacement(profile, _definitions, placement))
                    continue;
                (DaggerfallNpcAppearance look, string role, IReadOnlyList<string> services) =
                    DaggerfallSourcePopulation.ResolvePlacementIdentity(_definitions, placement, climateRace);
                _npcs.RegisterDeterministic(id, DaggerfallNpcKind.Civilian, placement.Id, npcSite, look, role, services, homeProfile: profile.ProfileKey);
                npc = _npcs.Require(id);
            }
            if (npc.Presence == DaggerfallNpcPresence.Removed) continue;
            ActorLedgerEntry? delta = _actorLedger.TryGet(id, out ActorLedgerEntry? savedDelta) ? savedDelta : null;
            _ = MaterializeDeterministicNpc(npc, ActorOrigin.Seeded(profile.ProfileKey, placement.Id), placement.Position, Vector3.Zero, delta, siteProfile: profile);
            if (npc.Presence == DaggerfallNpcPresence.Active
                && !TryAdmitDirectionalMobileAppearance(profile, appearance, id)
                && profile.BillboardSprites.TryGetValue((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord), out NormalizedBillboardSprite? sprite))
            {
                appearance.AdmitActor(id, sprite.Standing((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord)));
            }
            if (npc.Presence == DaggerfallNpcPresence.Active) _npcs.Bind(id, profile.ProfileKey);
            _npcs.ReleasePose(id);
        }
    }

    private void MaterializeRelocatedLedgerActors(DaggerfallSiteProfile profile, DaggerfallSiteAppearance appearance, Vector3 translation)
    {
        foreach (ActorLedgerEntry entry in _actorLedger.EntriesAtSite(profile.ProfileKey))
        {
            if (entry.Origin.Kind is not (ActorOriginKind.SeededSlot or ActorOriginKind.CastIndividual)) continue;
            if (entry.Origin.IsFixedPlacement) continue;
            if (_actors.TryGet(entry.DurableId, out _)) continue;
            if (entry.Location?.Detached == true && entry.Vitals is null && entry.Pose is null && entry.Social is null && entry.Combat is null) continue;

            DaggerfallNpc? npc = _npcs.TryGet(entry.DurableId, out DaggerfallNpc? existing) ? existing : null;
            if (npc is null && entry.Origin.Kind == ActorOriginKind.CastIndividual
                && entry.Origin.CastId is { } cid && _definitions.People.Individuals.TryGetValue(cid, out DaggerfallCastIndividualDefinition? cast))
            {
                DaggerfallSiteId site = profile.Site!.Value;
                DaggerfallSiteRecord location = _definitions.Locations.Records.Single(record => record.Id == site);
                _npcs.RegisterDeterministic(entry.DurableId, DaggerfallNpcKind.Static, $"cast:{cid}",
                    new DaggerfallNpcSite(site.Region, location.Name, string.Empty, profile.ProfileKey.LogicalId),
                    new DaggerfallNpcAppearance(cast.Race ?? "breton", cast.Gender, cast.Archive, cast.Record, 0, cast.FactionId),
                    "person", ["talk"], cast.Name, profile.ProfileKey);
                npc = _npcs.Require(entry.DurableId);
            }
            if (npc is null || npc.Presence == DaggerfallNpcPresence.Removed) continue;

            bool atHomeSite = entry.Origin.Kind switch
            {
                ActorOriginKind.SeededSlot => entry.Origin.SiteProfile?.Require() == profile.ProfileKey,
                ActorOriginKind.CastIndividual => entry.Origin.SiteProfile is { } castHome
                    ? castHome.Require() == profile.ProfileKey
                    : entry.Origin.CastId is { } castId
                      && _definitions.People.Individuals.TryGetValue(castId, out DaggerfallCastIndividualDefinition? castDef)
                      && profile.StaticNpcs.Any(s => s.Appearance.FactionId == castDef.FactionId),
                _ => true,
            };
            WorldPoint basePose = ResolveBaselineProfilePose(profile, entry, npc);
            DaggerfallStaticNpcPlacement? homeStatic = atHomeSite && entry.Origin.Kind == ActorOriginKind.SeededSlot && entry.Origin.SlotKey is { } sk
                ? profile.StaticNpcs.FirstOrDefault(s => StringComparer.Ordinal.Equals(s.Id, sk))
                : null;
            _ = MaterializeDeterministicNpc(npc, entry.Origin, basePose, translation, entry, isRelocated: !atHomeSite, siteProfile: profile, staticPlacement: homeStatic);
            if (npc.Presence == DaggerfallNpcPresence.Active
                && !TryAdmitDirectionalMobileAppearance(profile, appearance, entry.DurableId)
                && profile.BillboardSprites.TryGetValue((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord), out NormalizedBillboardSprite? sprite))
            {
                appearance.AdmitActor(entry.DurableId, sprite.Standing((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord)));
            }
            if (npc.Presence == DaggerfallNpcPresence.Active) _npcs.Bind(entry.DurableId, profile.ProfileKey);
            _npcs.ReleasePose(entry.DurableId);
        }
    }

    /// <summary>
    /// Materializes one <see cref="ActorLedgerEntry"/> (seeded slot, cast individual, or minted actor) into the active
    /// <c>EntityStore</c> at <paramref name="localPose"/> without creating a duplicate body when already live.
    /// Used when intercepting or arriving with a persistent <see cref="ActorLocationKind.OnOverlandBand"/> party or
    /// staging a <see cref="ActorLocationKind.WithParty"/> driver/companion into a 3D scene.
    /// </summary>
    internal ActorState? MaterializeLedgerActorAt(
        ActorLedgerEntry entry,
        DaggerfallSiteProfile profile,
        WorldPoint localPose,
        float headingRadians = 0f,
        DaggerfallSiteAppearance? siteAppearance = null,
        Vector3 translation = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(profile);
        DaggerfallSiteAppearance appearance = siteAppearance ?? _appearance();

        if (_actors.TryGet(entry.DurableId, out ActorState? existing))
        {
            existing.ApplyPose(new ActorPose(localPose, headingRadians));
            if (_npcs.TryGet(entry.DurableId, out DaggerfallNpc? existingNpc) && existingNpc.Presence == DaggerfallNpcPresence.Active)
                _npcs.Bind(entry.DurableId, profile.ProfileKey);
            return existing;
        }

        DaggerfallNpc? npc = _npcs.TryGet(entry.DurableId, out DaggerfallNpc? registered) ? registered : null;
        if (npc is null && entry.Origin.Kind == ActorOriginKind.Minted
            && entry.Origin.DefinitionId is { Length: > 0 } mintedDefId
            && _definitions.Actors.TryGetValue(new DaggerfallActorId(mintedDefId), out DaggerfallActorDefinition? mintedDef)
            && mintedDef.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass)
        {
            return MaterializeMintedCombatant(entry, mintedDef, profile, localPose, headingRadians, appearance, translation);
        }

        if (npc is null && entry.Origin.Kind == ActorOriginKind.CastIndividual
            && entry.Origin.CastId is { } cid && _definitions.People.Individuals.TryGetValue(cid, out DaggerfallCastIndividualDefinition? cast))
        {
            DaggerfallSiteId site = profile.Site!.Value;
            DaggerfallSiteRecord location = _definitions.Locations.Records.Single(record => record.Id == site);
            _npcs.RegisterDeterministic(entry.DurableId, DaggerfallNpcKind.Static, $"cast:{cid}",
                new DaggerfallNpcSite(site.Region, location.Name, string.Empty, profile.ProfileKey.LogicalId),
                new DaggerfallNpcAppearance(cast.Race ?? "breton", cast.Gender, cast.Archive, cast.Record, 0, cast.FactionId),
                "person", ["talk"], cast.Name, profile.ProfileKey);
            npc = _npcs.Require(entry.DurableId);
        }

        if (npc is null || npc.Presence == DaggerfallNpcPresence.Removed) return null;
        if (npc.Presence != DaggerfallNpcPresence.Active)
            _npcs.SetPresence(entry.DurableId, DaggerfallNpcPresence.Active);

        WorldPoint profilePose = DaggerfallSceneOrigin.Shift(localPose, -translation);
        ActorState materialized;
        if (entry.Origin.Kind is ActorOriginKind.SeededSlot or ActorOriginKind.CastIndividual)
        {
            bool atHomeSite = entry.Origin.SiteProfile?.Require() == profile.ProfileKey;
            WorldPoint baseline = ResolveBaselineProfilePose(profile, entry, npc);
            DaggerfallStaticNpcPlacement? homeStatic = atHomeSite && entry.Origin.Kind == ActorOriginKind.SeededSlot && entry.Origin.SlotKey is { } sk
                ? profile.StaticNpcs.FirstOrDefault(s => StringComparer.Ordinal.Equals(s.Id, sk))
                : null;
            materialized = MaterializeDeterministicNpc(npc, entry.Origin, baseline, translation, entry, isRelocated: !atHomeSite, siteProfile: profile, staticPlacement: homeStatic);
            materialized.ApplyPose(new ActorPose(localPose, headingRadians));
        }
        else
        {
            long id = MaterializeCivilian(npc, new ActorPose(localPose, headingRadians));
            materialized = _actors.Get(id);
            ApplySparseLedgerDelta(materialized, npc, entry with { Pose = new ActorPoseOverride(profilePose.X, profilePose.Y, profilePose.Z, headingRadians) }, translation, profile);
        }

        if (!TryAdmitDirectionalMobileAppearance(profile, appearance, entry.DurableId)
            && profile.BillboardSprites.TryGetValue((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord), out NormalizedBillboardSprite? sprite))
        {
            appearance.AdmitActor(entry.DurableId, sprite.Standing((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord)));
        }
        _npcs.Bind(entry.DurableId, profile.ProfileKey);
        _npcs.ReleasePose(entry.DurableId);
        return materialized;
    }

    private ActorState MaterializeMintedCombatant(
        ActorLedgerEntry entry,
        DaggerfallActorDefinition baseDefinition,
        DaggerfallSiteProfile profile,
        WorldPoint localPose,
        float headingRadians,
        DaggerfallSiteAppearance appearance,
        Vector3 translation)
    {
        long durableId = entry.DurableId;
        int spawnLevel = entry.Origin.Level
            ?? baseDefinition.Level
            ?? (baseDefinition.Kind == DaggerfallActorKinds.EnemyClass ? _actors.Player.Progression.Level : 1);
        DaggerfallActorDefinition spawnedDefinition = DaggerfallEncounterActors.AtLevel(baseDefinition, _definitions.Vocabulary, spawnLevel);
        if (entry.Combat?.PlayerAllied == true)
            spawnedDefinition = spawnedDefinition with { Team = "player-ally" };

        ActorPose pose = new(localPose, headingRadians);
        StatsComponent stats = _mechanics.CreateStats(spawnedDefinition, SpawnVitals(spawnedDefinition, spawnLevel, durableId));
        ActorState actor = DaggerfallActorFactory.CreateNonPlayerActor(_actors, durableId, spawnedDefinition, stats, pose);
        DaggerfallActorFactory.RegisterActorInventory(actor, _inventoryStore);

        if (entry.Vitals is { } vitals)
        {
            DaggerfallActorFactory.RestoreStats(actor.Actor, vitals.Stats);
        }
        if (entry.Location?.Kind == ActorLocationKind.Dead)
        {
            actor.Stats.GetTrack(TrackId.Parse(spawnedDefinition.Combat.Health.Value)).SetCurrent(0, clamp: true);
        }

        DaggerfallEnemyPerceptionMemory memory = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>();
        if (entry.Combat is { } combat)
        {
            memory.SpawnDistance = combat.SpawnDistance;
            memory.Passive = combat.Passive;
            memory.SetForcedHostile(combat.ForcedHostile);
            memory.LanguagePacified = combat.LanguagePacified;
            memory.MagicallyPacified = combat.MagicallyPacified;
            if (combat.TeamOverride is not null && actor.Actor.TryGet<FactionComponent>(out FactionComponent? fc))
                fc.SetTeamOverride(combat.TeamOverride);
            if (combat.DetachedInventory is not null || combat.DetachedCorpse is not null || combat.DetachedEffects is not null)
                _restoreDetachedState?.Invoke(combat.DetachedInventory, combat.DetachedCorpse, combat.DetachedEffects);
        }

        if ((_actorInventories.EquipmentFor(durableId)?.Read().Assignments.Count ?? 0) == 0
            && (_actorInventories.InventoryFor(durableId)?.Read().Stacks.Count ?? 0) == 0
            && (_actorInventories.InventoryFor(durableId)?.Read().UniqueItems.Count ?? 0) == 0)
        {
            GrantSpawnLoadout(actor, spawnedDefinition);
            GrantStartingEquipment(actor, spawnedDefinition);
        }

        if (spawnedDefinition.MobileId is int mobileId
            && profile.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
        {
            string resolvedGender = !string.IsNullOrWhiteSpace(entry.Origin.Gender)
                ? entry.Origin.Gender!
                : DaggerfallActorGender.Resolve(_random, durableId, "unspecified", sprite);
            actor.Actor.Add(new DaggerfallActorGender(resolvedGender));
            appearance.AddActor(durableId, sprite.ForGender(resolvedGender), mobileId);
        }

        WorldPoint profilePose = DaggerfallSceneOrigin.Shift(localPose, -translation);
        actor.Actor.Add(new ActorOriginComponent(entry.Origin, profilePose, headingRadians));
        _definitionsByActor[durableId] = spawnedDefinition;
        _dynamicActors[durableId] = spawnedDefinition.Id;
        if (_definitions.Walks(baseDefinition)) _grounding.Ground(actor);
        return actor;
    }

    private static WorldPoint ResolveBaselineProfilePose(DaggerfallSiteProfile profile, ActorLedgerEntry entry, DaggerfallNpc npc)
    {
        if (entry.Origin.Kind == ActorOriginKind.SeededSlot
            && entry.Origin.SiteProfile?.Require() == profile.ProfileKey
            && entry.Origin.SlotKey is { } slotKey)
        {
            if (profile.Population.FirstOrDefault(p => StringComparer.Ordinal.Equals(p.Id, slotKey)) is { } pop)
                return pop.Position;
            if (profile.StaticNpcs.FirstOrDefault(p => StringComparer.Ordinal.Equals(p.Id, slotKey)) is { } stat)
                return stat.Position;
        }
        return entry.Pose is { } p
            ? new WorldPoint(p.X, p.Y, p.Z)
            : entry.Location?.Pose is { } lp
                ? new WorldPoint(lp.X, lp.Y, lp.Z)
                : profile.Project.PlayerPosition ?? default;
    }

    private ActorState MaterializeDeterministicNpc(DaggerfallNpc npc, ActorOrigin origin, WorldPoint baselineProfilePose,
        Vector3 translation, ActorLedgerEntry? delta, bool isRelocated = false, DaggerfallSiteProfile? siteProfile = null,
        DaggerfallStaticNpcPlacement? staticPlacement = null)
    {
        WorldPoint worldPose = DaggerfallSceneOrigin.Shift(baselineProfilePose, translation);
        string? spawnerId = staticPlacement?.SpawnerId ?? origin.SpawnerId;
        ActorState actor = DaggerfallActorFactory.CreateCivilianActor(_mechanics, _actors, _inventoryStore, npc,
            new ActorPose(worldPose, 0f), _definitions, out DaggerfallActorDefinition definition,
            spawnerId: spawnerId);
        if ((staticPlacement?.Harm ?? origin.HarmPolicy) is { } resolvedHarm)
            actor.Actor.Get<ActorHarmPolicy>().Set(resolvedHarm);
        if ((staticPlacement?.Interaction ?? origin.InteractionPolicy) is { } resolvedInteraction)
            actor.Actor.Get<ActorInteractionPolicy>().Set(resolvedInteraction);
        DaggerfallStatsSave baselineStats = DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity);
        actor.Actor.Add(new ActorOriginComponent(origin, baselineProfilePose, 0f, baselineStats, npc, isRelocated));
        try
        {
            if (!_definitionsByActor.TryAdd(npc.DurableId, definition))
                throw new InvalidOperationException($"Deterministic NPC {npc.DurableId} already has a runtime actor definition.");
            if (delta?.Combat?.DetachedInventory is null)
                GrantInitialLoadout(actor, definition);
            if (delta is not null) ApplySparseLedgerDelta(actor, npc, delta, translation, siteProfile);
            return actor;
        }
        catch
        {
            _definitionsByActor.Remove(npc.DurableId);
            _actors.Entities.Destroy(ActorsState.Identity(npc.DurableId));
            throw;
        }
    }

    private void ApplySparseLedgerDelta(ActorState actor, DaggerfallNpc npc, ActorLedgerEntry delta, Vector3 translation,
        DaggerfallSiteProfile? siteProfile = null)
    {
        if (string.Equals(delta.Combat?.PromotedArchetype, DaggerfallArchetypeIds.Guard, StringComparison.Ordinal))
            _ = ApplyCityWatchPromotion(actor, grantEquipment: false, swapAppearance: false, siteProfile);
        if (delta.Vitals is { } vitals)
            DaggerfallActorFactory.RestoreStats(actor.Actor, vitals.Stats);
        if (delta.Location?.Kind == ActorLocationKind.Dead)
            actor.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).SetCurrent(0, clamp: true);
        if (delta.Pose is { } pose)
            actor.ApplyPose(new ActorPose(DaggerfallSceneOrigin.Shift(new WorldPoint(pose.X, pose.Y, pose.Z), translation), pose.HeadingRadians));
        if (delta.Combat is { } combat)
        {
            DaggerfallEnemyPerceptionMemory memory = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>();
            memory.SetForcedHostile(combat.ForcedHostile);
            memory.LanguagePacified = combat.LanguagePacified;
            memory.MagicallyPacified = combat.MagicallyPacified;
            if (combat.TeamOverride is { } teamOverride)
                actor.Actor.Get<FactionComponent>().SetTeamOverride(teamOverride);
            if (combat.DetachedInventory is not null || combat.DetachedCorpse is not null || combat.DetachedEffects is { Length: > 0 })
            {
                _restoreDetachedState?.Invoke(combat.DetachedInventory, combat.DetachedCorpse, combat.DetachedEffects);
                ActorCombatStateDiff? clearedCombat = combat with
                {
                    DetachedInventory = null,
                    DetachedCorpse = null,
                    DetachedEffects = null,
                };
                if (!clearedCombat.ForcedHostile && !clearedCombat.LanguagePacified && !clearedCombat.MagicallyPacified
                    && clearedCombat.TeamOverride is null && clearedCombat.WabbajackDefinition is null
                    && !clearedCombat.WabbajackActive && !clearedCombat.Passive && clearedCombat.SpawnDistance == 0
                    && !clearedCombat.PlayerAllied && clearedCombat.CorruptionOrigin is null
                    && clearedCombat.EncounterBandId is null && clearedCombat.PromotedArchetype is null
                    && clearedCombat.InfightingOverride is null)
                {
                    clearedCombat = null;
                }
                ActorLocationOverride? clearedLocation = delta.Location switch
                {
                    { Kind: ActorLocationKind.AtSite, Detached: true, Profile: { } locProfile } =>
                        delta.Origin.SiteProfile?.Require() == locProfile.Require() ? null : delta.Location with { Detached = false },
                    { Kind: ActorLocationKind.Dead, Detached: true } => delta.Location with { Detached = false },
                    _ => delta.Location,
                };
                _actorLedger.Upsert(delta with { Combat = clearedCombat, Location = clearedLocation });
            }
            else if (delta.Location is { Kind: ActorLocationKind.AtSite or ActorLocationKind.Dead, Detached: true })
            {
                ActorLocationOverride? clearedLocation = delta.Location is { Kind: ActorLocationKind.AtSite, Profile: { } detProfile }
                    && delta.Origin.SiteProfile?.Require() == detProfile.Require()
                    ? null
                    : delta.Location with { Detached = false };
                _actorLedger.Upsert(delta with { Location = clearedLocation });
            }
        }
        else if (delta.Location is { Kind: ActorLocationKind.AtSite or ActorLocationKind.Dead, Detached: true })
        {
            ActorLocationOverride? clearedLocation = delta.Location is { Kind: ActorLocationKind.AtSite, Profile: { } detProfile }
                && delta.Origin.SiteProfile?.Require() == detProfile.Require()
                ? null
                : delta.Location with { Detached = false };
            _actorLedger.Upsert(delta with { Location = clearedLocation });
        }
        if (delta.Referral is { } referral)
        {
            if (!string.IsNullOrWhiteSpace(referral.DisplayNameOverride))
                _npcs.SetDisplayName(npc.DurableId, referral.DisplayNameOverride);
            if (actor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? refOverlay))
            {
                if (referral.Tags is { Length: > 0 })
                    foreach (string tag in referral.Tags)
                        refOverlay.RecordReferralTag(tag);
                if (referral.Vouches is { Length: > 0 })
                    foreach (ActorVouchRecord vouch in referral.Vouches)
                        refOverlay.RecordVouch(vouch);
            }
        }
        if (delta.Social is { } social)
        {
            if (!string.IsNullOrWhiteSpace(social.DisplayNameOverride))
                _npcs.SetDisplayName(npc.DurableId, social.DisplayNameOverride);
            if (actor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay))
            {
                overlay.FamilyId = social.FamilyId;
                overlay.Met = social.Met;
                overlay.LastFavourDay = social.LastFavourDay;
                overlay.IsMuted = social.Muted;
                overlay.EscortPortraitMedia = social.EscortPortrait;
                overlay.EscortOrder = social.EscortOrder;
                overlay.StagedPartyRole = social.StagedPartyRole?.ToString();
                if (social.WitnessedCrimes is { Length: > 0 })
                    foreach (string operationId in social.WitnessedCrimes)
                        overlay.RecordWitnessedCrime(operationId);
                if (social.ReferralTags is { Length: > 0 })
                    foreach (string tag in social.ReferralTags)
                        overlay.RecordReferralTag(tag);
                if (social.Vouches is { Length: > 0 })
                    foreach (ActorVouchRecord vouch in social.Vouches)
                        overlay.RecordVouch(vouch);
            }
        }
    }

    /// <summary>
    /// Releases a departing site's actors after their delta was captured: target-bound effects are
    /// suspended, owned item entities released with their durable identities kept for the delta,
    /// and the actor, its corpse container and its appearance retired.
    /// </summary>
    internal void UnloadSite(DaggerfallSiteProfile source, IReadOnlyList<DaggerfallDynamicActorSave>? retained,
        DaggerfallSiteAppearance? siteAppearance = null, IReadOnlySet<long>? actorIds = null)
    {
        IEnumerable<long> authored = source.Project.Actors.Keys;
        IEnumerable<long> dynamic = retained?.Select(actor => actor.EntityId) ?? [];
        IEnumerable<long> deterministic = _actors.All
            .Where(actor => actor.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? originComp)
                && originComp.Origin.Kind is ActorOriginKind.SeededSlot or ActorOriginKind.CastIndividual
                && !originComp.Origin.IsFixedPlacement
                && (_npcs.TryGet(actor.DurableId, out DaggerfallNpc? npc) && npc.Profile == source.ProfileKey
                    || originComp.Origin.SiteProfile?.Require() == source.ProfileKey))
            .Select(actor => actor.DurableId);
        long[] ids = actorIds is null
            ? [.. authored.Concat(dynamic).Concat(deterministic).Distinct().Order()]
            : [.. actorIds.Order()];
        // The delta was captured while these actors and their target-bound contributions were live.
        // Detach every target lifecycle before destroying Engine entities; effects on a live player
        // with one of these actors as caster remain active by durable caster identity.
        _ = _effects.SuspendTargets(ids);
        foreach (long id in ids) UnloadActor(id, siteAppearance);
    }

    /// <summary>
    /// Retires a dynamic actor an inactive site keeps, from what its site's delta holds of it. The detached representation
    /// has no Engine entities left to destroy, so retirement removes every durable relationship directly and keeps the
    /// allocator tombstones: the items it and its corpse hold (with Magic's references to them), their stacks, its
    /// effects, its corpse container and its identity. The deltas that held it are the site crossing's to let go, and
    /// the item owners go after the delta that kept it (<see cref="ReleaseDetachedOwners"/>).
    /// </summary>
    /// <param name="inventories">The delta's actor inventories; this actor's are read from them.</param>
    /// <param name="corpses">The delta's corpses; this actor's is read from them.</param>
    /// <param name="requiredDefinition">The definition the caller expects the actor to carry, or null for any.</param>
    /// <param name="cancelCastingItemReferences">Magic's casting lets go of a retired item (it is composed after the roster).</param>
    internal void RetireDetached(DaggerfallDynamicActorSave actor, IReadOnlyList<DaggerfallActorInventorySave> inventories,
        IReadOnlyList<DaggerfallCorpseSave> corpses, string? requiredDefinition, Action<ulong> cancelCastingItemReferences)
    {
        long npcId = actor.EntityId;
        if (requiredDefinition is not null && !StringComparer.Ordinal.Equals(actor.Definition, requiredDefinition))
            throw new InvalidOperationException($"Detached NPC actor {npcId} does not carry the required runtime definition.");

        foreach (ulong itemId in inventories
            .Where(inventory => inventory.EntityId == npcId)
            .SelectMany(inventory => inventory.Inventory.UniqueItems.Select(item => item.EntityId))
            .Concat(corpses.Where(corpse => corpse.ActorId == npcId)
                .SelectMany(corpse => corpse.UniqueItems.Select(item => item.EntityId)))
            .Distinct())
        {
            _itemInstances.RemoveUnique(itemId);
            _effects.CancelItemReferences(itemId);
            cancelCastingItemReferences(itemId);
            DurableIdentityReference identity = new(DurableIdentityKind.Item, itemId);
            if (_identities.Classify(identity) == DurableIdentityClassification.Live)
                _uniqueItems.Remove(identity);
        }

        foreach (var inventory in inventories.Where(value => value.EntityId == npcId))
            foreach (var stack in inventory.Inventory.Stacks)
                _itemInstances.RetireRetainedStack(DaggerfallItemOwner.Actor(npcId), InventoryStackId.Parse(stack.StackId));
        foreach (var corpse in corpses.Where(value => value.ActorId == npcId))
            foreach (var stack in corpse.Stacks)
                _itemInstances.RetireRetainedStack(DaggerfallItemOwner.Corpse(npcId), InventoryStackId.Parse(stack.StackId));
        _effects.CancelActorReferences(npcId);

        DaggerfallCorpseSave? corpseSave = corpses.SingleOrDefault(value => value.ActorId == npcId);
        if (corpseSave is not null)
        {
            DurableIdentityReference persisted = new(DurableIdentityKind.Container, corpseSave.ContainerId);
            if (_corpseLoot.TryGetContainerIdentity(npcId, out DurableIdentityReference mapped)
                && mapped != persisted)
                throw new InvalidOperationException($"Detached actor {npcId} has changed its corpse container identity.");
            if (!_corpseLoot.Retire(npcId)
                && _identities.Classify(persisted) == DurableIdentityClassification.Live)
                _identities.Remove(persisted);
        }

        DurableIdentityReference actorIdentity = ActorsState.Identity(npcId);
        if (_identities.Classify(actorIdentity) == DurableIdentityClassification.Live)
            _identities.Remove(actorIdentity);
        if (_actorLedger.TryGet(npcId, out ActorLedgerEntry? detachedPerson) && detachedPerson is { Social: not null })
            _actorLedger.MarkRemoved(npcId, detachedPerson.Origin, detachedPerson.Social);
        else
            _actorLedger.Remove(npcId);
        _actorLedger.StripDetachedEffectsOf(npcId);
    }

    /// <summary>A retired detached actor's item owners, its own and its corpse's, let go.</summary>
    internal void ReleaseDetachedOwners(long actorId)
    {
        _itemInstances.RemoveOwner(DaggerfallItemOwner.Actor(actorId));
        _itemInstances.RemoveOwner(DaggerfallItemOwner.Corpse(actorId));
    }

    /// <summary>Detaches one canonical actor while its values and identities remain in a site delta.</summary>
    internal void UnloadActor(long id, DaggerfallSiteAppearance? siteAppearance = null)
    {
        _ = _effects.SuspendTargets([id]);
        if (!_actors.TryGet(id, out ActorState? actor)) return;
        _closeLoot(actor.DurableId);
        DestroySiteOwnedUniqueItems(actor);
        _definitionsByActor.Remove(actor.DurableId);
        _dynamicActors.Remove(actor.DurableId);
        // Authored and spawned placements share the same appearance owner. A cell unload must
        // retire both kinds so no sprite/animation wrapper outlives its admitted location.
        (siteAppearance ?? Appearance).RetireActor(actor.DurableId);
        _itemInstances.RemoveOwner(DaggerfallItemOwner.Actor(actor.DurableId), retireBindings: false);
        _itemInstances.RemoveOwner(DaggerfallItemOwner.Corpse(actor.DurableId), retireBindings: false);
        _corpseLoot.Unload(actor.DurableId);
        _actors.Entities.Destroy(ActorsState.Identity(actor.DurableId));
    }

    /// <summary>
    /// Creates a destination site's authored placements and its retained spawned actors. The
    /// caller restores the delta's inventories, corpses and effects onto them afterwards.
    /// </summary>
    internal void MaterializeSite(DaggerfallSiteProfile destination, IReadOnlyList<DaggerfallActorSave>? saves,
        IReadOnlyList<DaggerfallDynamicActorSave>? retained, IEnumerable<long>? banished,
        bool restoreAuthoredAppearance = false, DaggerfallSiteProfile? siteProfile = null, DaggerfallSiteAppearance? siteAppearance = null)
    {
        // Banishment is a session-wide durable identity fact. A second resident profile must not
        // clear an earlier profile's tombstones while it is being admitted.
        if (banished is not null) BanishedActors.UnionWith(banished);
        DaggerfallSiteAppearance appearance = siteAppearance ?? _appearance();
        Dictionary<long, DaggerfallActorSave> saved = saves?.ToDictionary(value => value.EntityId) ?? [];
        foreach (AuthoredActor placement in destination.Project.Actors.Values.OrderBy(value => value.EntityId))
        {
            if (BanishedActors.Contains(placement.EntityId) || _actorLedger.IsHomeSlotSuppressed(placement.EntityId, destination.ProfileKey)) continue;
            saved.TryGetValue(placement.EntityId, out DaggerfallActorSave? prior);
            DaggerfallActorDefinition definition = prior?.WabbajackDefinition is { } transformed
                ? DaggerfallWabbajack.RequireDefinition(_definitions, transformed) : _definitions.RequireActor(placement.ActorId);
            ActorState actor = DaggerfallActorFactory.CreateAuthoredActor(_random, _mechanics, _definitions, _actors, _inventoryStore,
                _actorInventories.ItemDefinitions, _itemInstances, placement, prior, destination.ProfileKey);
            if (prior is not null) actor.ApplyPose(new ActorPose(new WorldPoint(prior.X, prior.Y, prior.Z), prior.HeadingRadians));
            else if (_definitions.Walks(definition)) _grounding.Ground(actor);
            _definitionsByActor.Add(actor.DurableId, definition);
            if (restoreAuthoredAppearance && definition.MobileId is int authoredMobile)
            {
                if (!destination.MobileSprites.TryGetValue(authoredMobile, out NormalizedActorSprite? sprite))
                    throw new InvalidOperationException($"Restored authored actor '{placement.EntityId}' has no admitted mobile {authoredMobile} presentation.");
                appearance.AddActor(actor.DurableId, sprite.ForGender(DaggerfallActorGender.Resolve(_random, placement.EntityId, placement.Gender, sprite)), definition.MobileId);
            }
            if (prior is null) GrantStartingEquipment(actor, definition);
            if (prior?.WabbajackDefinition is not null && definition.MobileId is int changedMobile)
            {
                appearance.RetireActor(actor.DurableId);
                appearance.AddActor(actor.DurableId, destination.MobileSprites[changedMobile], changedMobile);
            }
        }
        if (retained is null) return;
        foreach (DaggerfallDynamicActorSave savedDynamic in retained.OrderBy(actor => actor.EntityId))
            MaterializeRetainedActor(savedDynamic, projectAppearance: true, siteProfile: siteProfile, siteAppearance: siteAppearance);
    }

    /// <summary>Rebuilds a retained actor through the same factory and presentation owner as site re-entry.</summary>
    internal ActorState MaterializeRetainedActor(DaggerfallDynamicActorSave saved, bool projectAppearance = true,
        DaggerfallSiteProfile? siteProfile = null, DaggerfallSiteAppearance? siteAppearance = null)
    {
        try
        {
            var actor = DaggerfallActorFactory.CreateDynamicActor(_random, _mechanics, _definitions, _actors, _inventoryStore,
                _definitionsByActor, saved, _npcs);
            _dynamicActors.Add(saved.EntityId, new DaggerfallActorId(saved.Definition));
            if (_actorLedger.TryGet(saved.EntityId, out ActorLedgerEntry? retainedLedger)
                && retainedLedger?.Social is { } retainedSocial
                && actor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? retainedOverlay))
            {
                retainedOverlay.FamilyId = retainedSocial.FamilyId;
                retainedOverlay.Met = retainedSocial.Met;
                retainedOverlay.LastFavourDay = retainedSocial.LastFavourDay;
                retainedOverlay.IsMuted = retainedSocial.Muted;
                retainedOverlay.EscortPortraitMedia = retainedSocial.EscortPortrait;
                retainedOverlay.EscortOrder = retainedSocial.EscortOrder;
                retainedOverlay.StagedPartyRole = retainedSocial.StagedPartyRole?.ToString();
                if (retainedSocial.WitnessedCrimes is { Length: > 0 })
                    foreach (string operationId in retainedSocial.WitnessedCrimes)
                        retainedOverlay.RecordWitnessedCrime(operationId);
                if (retainedSocial.ReferralTags is { Length: > 0 })
                    foreach (string tag in retainedSocial.ReferralTags)
                        retainedOverlay.RecordReferralTag(tag);
                if (retainedSocial.Vouches is { Length: > 0 })
                    foreach (ActorVouchRecord vouch in retainedSocial.Vouches)
                        retainedOverlay.RecordVouch(vouch);
            }
            if (projectAppearance && _definitionsByActor[saved.EntityId].MobileId is int mobileId)
            {
                DaggerfallSiteProfile presentation = siteProfile ?? _profile();
                if (!presentation.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
                    throw new InvalidOperationException($"Restored dynamic actor '{saved.Definition}' has no admitted mobile {mobileId} presentation.");
                (siteAppearance ?? Appearance).AddActor(saved.EntityId, sprite.ForGender(saved.Gender ?? "unspecified"), mobileId);
            }
            // A retained NPC actor owns its pose again; the registry keeps identity and binding only.
            _npcs.ReleasePose(saved.EntityId);
            return actor;
        }
        catch { UnloadActor(saved.EntityId, siteAppearance); throw; }
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
            DaggerfallEnemyGroup group = actor.Actor.TryGet<ActorHarmPolicy>(out ActorHarmPolicy? harm) && !harm.IsCombatant
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
        if (_actorInventories.InventoryFor(actor.DurableId) is { } inventory)
            identities.AddRange(inventory.Read().UniqueItems.Select(item => _actors.Entities.IdentityOf(item.Entity).Value));
        if (_corpseLoot.Corpses.TryGetValue(actor.DurableId, out CorpseContainer? corpse) && corpse.IsRegistered)
            identities.AddRange(_containers.Read(corpse.Owner).UniqueItems.Select(item => _actors.Entities.IdentityOf(item.Entity).Value));
        RetireInventoryOwners(actor);
        foreach (ulong itemId in identities.Distinct())
        {
            _itemInstances.RemoveUnique(itemId);
            _actors.Entities.Destroy(new DurableIdentityReference(DurableIdentityKind.Item, itemId));
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
                    DaggerfallRandomKey.Seed,
                    CombatRandomKey.EnemyScope,
                    CombatRandomKey.ClassHealth(durableId, definition.Id.Value, rollIndex++),
                    minimum,
                    maximum)).Value));
            return new DaggerfallVitalValues(health, 0, 0);
        }

        return DaggerfallActorFactory.InitialVitals(_random, definition, durableId);
    }

    /// <summary>Initial site construction uses the same starting items as later actor admission.</summary>
    internal void GrantInitialAuthoredEquipment()
    {
        foreach (var entry in _definitionsByActor.OrderBy(entry => entry.Key))
            if (!_dynamicActors.ContainsKey(entry.Key))
                GrantStartingEquipment(_actors.Get(entry.Key), entry.Value);
    }

    private void GrantStartingEquipment(ActorState actor, DaggerfallActorDefinition definition)
    {
        if (definition.Kind != DaggerfallActorKinds.EnemyClass && definition.MobileId is not (7 or 8 or 12 or 21 or 24)) return;
        if (definition.MobileId is not int mobileId) throw new InvalidOperationException($"Class actor '{definition.Id.Value}' has no equipment mobile id.");
        MechanicsInventoryCoordinator inventory = _actorInventories.InventoryFor(actor.DurableId)
            ?? throw new InvalidOperationException($"Spawned actor {actor.DurableId} has no registered inventory.");
        DaggerfallClassEnemyEquipmentPolicy.Equip(_definitions, _random, _itemInstances, _uniqueItems, inventory, _actorInventories.EquipmentFor(actor.DurableId),
            actor.DurableId, mobileId, _actors.Player.Progression.Level,
            _character.Identity.RaceId,
            _character.Identity.Gender == DaggerfallCharacterGender.Female ? "female" : "male",
            fixedVariant: mobileId switch { 7 or 21 => 0, 8 or 12 => 1, 24 => 2, _ => null });
        if (definition.Kind == DaggerfallActorKinds.EnemyClass)
            DaggerfallClassEnemyEquipmentPolicy.GrantRangedLoadout(_definitions, _random, _itemInstances, _uniqueItems,
                inventory, _actorInventories.EquipmentFor(actor.DurableId), actor.DurableId, mobileId, _actors.Player.Progression.Level,
                _character.Identity.RaceId, _character.Identity.Gender == DaggerfallCharacterGender.Female ? "female" : "male");
        DaggerfallClassEnemyEquipmentPolicy.CoatStartingWeapon(_random, _itemInstances,
            _actorInventories.EquipmentFor(actor.DurableId), actor.DurableId, mobileId, _actors.Player.Progression.Level);
    }

    private void GrantSpawnLoadout(ActorState actor, DaggerfallActorDefinition definition)
    {
        if (definition.Loadout.Count == 0) return;
        foreach (DaggerfallLoadoutEntry entry in definition.Loadout)
        {
            if (!_definitions.Items.TryGetValue(entry.ItemId, out DaggerfallItemDefinition? item) || !item.IsFungible)
                throw new InvalidOperationException($"Spawned actor '{definition.Id.Value}' loadout carries a unique or missing item, which spawned actors do not equip yet.");
        }

        MechanicsInventoryCoordinator actorInventory = _actorInventories.InventoryFor(actor.DurableId)
            ?? throw new InvalidOperationException($"Spawned actor {actor.DurableId} has no registered inventory.");
        int ordinal = 0;
        foreach (DaggerfallLoadoutEntry entry in definition.Loadout)
        {
            InventoryStackId stackId = DaggerfallInventoryStackIds.ForSpawnLoadout(actor.DurableId, ordinal++);
            actorInventory.Grant(new InventoryGrant(new InventoryItemId(entry.ItemId.Value), stackId, entry.Quantity));
            _itemInstances.RegisterDefaultStack(DaggerfallItemOwner.Actor(actor.DurableId),
                new InventoryStack(stackId, ItemDefinitionId.Parse(entry.ItemId.Value), entry.Quantity), _definitions.Items[entry.ItemId]);
        }
    }

    private void GrantInitialLoadout(ActorState actor, DaggerfallActorDefinition definition)
    {
        if (definition.Loadout.Count == 0) return;
        MechanicsInventoryCoordinator? actorInventory = _actorInventories.InventoryFor(actor.DurableId);
        if (actorInventory is null) return;
        int ordinal = 0;
        foreach (DaggerfallLoadoutEntry entry in definition.Loadout)
        {
            if (!_definitions.Items.TryGetValue(entry.ItemId, out DaggerfallItemDefinition? item) || !item.IsFungible)
                continue;
            InventoryStackId stackId = DaggerfallInventoryStackIds.ForInitialLoadout(actor.DurableId, ordinal++);
            actorInventory.Grant(new InventoryGrant(new InventoryItemId(entry.ItemId.Value), stackId, entry.Quantity));
            _itemInstances.RegisterDefaultStack(DaggerfallItemOwner.Actor(actor.DurableId),
                new InventoryStack(stackId, ItemDefinitionId.Parse(entry.ItemId.Value), entry.Quantity), item);
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
        InventoryStore store = _inventoryStore;
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
        if (!_actors.TryGet(durableId, out ActorState? actor)) return;
        HashSet<ulong> owned = [];
        if (_actorInventories.InventoryFor(durableId) is { } inventory)
            foreach (var item in inventory.Read().UniqueItems)
                owned.Add(_actors.Entities.IdentityOf(item.Entity).Value);
        // Equipment carries runtime item references; metadata and the allocator use durable identities.
        foreach (var assignment in _actorInventories.EquipmentFor(durableId).Read().Assignments)
            owned.Add(_actors.Entities.IdentityOf(new EntityId(assignment.Item.EntityId)).Value);
        if (actor.Actor.TryGet<CorpseLootComponent>(out CorpseLootComponent? corpse) && corpse is not null && corpse.HasRegisteredInventory)
            foreach (var item in _containers.Read(corpse.Owner).UniqueItems)
                owned.Add(_actors.Entities.IdentityOf(item.Entity).Value);
        RetireInventoryOwners(actor);
        _itemInstances.RemoveOwner(DaggerfallItemOwner.Actor(durableId));
        _itemInstances.RemoveOwner(DaggerfallItemOwner.Corpse(durableId));
        foreach (ulong itemId in owned)
        {
            _itemInstances.RemoveUnique(itemId);
            _ = _effects.CancelItemReferences(itemId);
            DurableIdentityReference reference = new(DurableIdentityKind.Item, itemId);
            _actors.Entities.Destroy(reference);
            _uniqueItems.Remove(reference);
        }
    }
}

/// <summary>
/// Settles a placed or spawned actor onto the floor below its authored marker through the Engine's
/// own spatial ray query. Every actor whose mobile walks is settled (<see cref="DaggerfallDefinitions.Walks"/>);
/// flyers, spectres and swimmers keep the height they were placed at.
/// </summary>
internal sealed class DaggerfallActorGrounding(ISpatialService spatialService, SpatialMovementSystem spatial, float probeLift, double probeDistance)
{
    private readonly List<ActorState> _awaitingFloor = [];

    /// <summary>
    /// Whether the floor under newly placed actors may still be on its way: a staged site change has collision held or
    /// being prepared. Such actors are settled by <see cref="GroundAwaiting"/> once it is live.
    /// </summary>
    internal Func<bool> FloorPending { get; set; } = static () => false;

    internal void Ground(ActorState actor)
    {
        if (FloorPending())
        {
            _awaitingFloor.Add(actor);
            return;
        }
        // RDB marker heights are probe origins, not floor contacts. Unlike DFU's centered
        // capsule, our navigation pose is the sprite's base.
        actor.ApplyPose(new ActorPose(GroundPosition(actor.Position), actor.HeadingYawRadians));
    }

    /// <summary>
    /// Settles the actors placed while their floor was pending, those of them still live: still in the store under their
    /// identity, and as the same entity, not one retired and spawned again under it meanwhile.
    /// </summary>
    internal void GroundAwaiting(ActorsState actors)
    {
        ActorState[] awaiting = [.. _awaitingFloor];
        _awaitingFloor.Clear();
        foreach (ActorState actor in awaiting)
            if (actors.TryGet(actor.DurableId, out ActorState live) && live.Actor.Entity == actor.Actor.Entity) Ground(actor);
    }

    /// <summary>
    /// How far below a falling thing's point the floor is looked for: a corpse of a flyer killed in the air or a
    /// pile dropped from the player's body centre. Beyond it nothing lies, and the thing stays where it was.
    /// </summary>
    internal const double FallDistance = 64d;

    /// <summary>The floor under a thing that falls to rest: a dropped pile, a treasure marker, or a corpse.</summary>
    internal WorldPoint RestingPosition(WorldPoint position) => GroundPosition(position, FallDistance);

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
