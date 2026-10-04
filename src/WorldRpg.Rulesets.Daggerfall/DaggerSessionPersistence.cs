using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using Rusty.Engine;
using System.Numerics;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Property;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Explicit current-state capture and restoration; native presentation and input are reconstructed by composition.</summary>
internal sealed class DaggerSessionPersistence
{
    private readonly DaggerfallState State;
    private readonly DaggerfallCorpseLootModule _corpseLoot;
    private readonly DaggerfallGroundContainers _groundContainers;
    private readonly DaggerfallBookNotebook _notebook;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly FirstPersonCameraSystem _camera;
    private readonly DaggerfallWorldTime _time;
    private readonly DaggerfallSiteContext _site;
    private readonly DaggerfallEffectLifecycle _effects;
    private readonly Func<DaggerfallDoorRuntime> _doors;
    private readonly DaggerfallLocomotionPolicy _locomotion;
    private readonly DaggerfallClimbingPolicy _climbing;
    private readonly DaggerfallSwimmingPolicy _swimming;
    private readonly DaggerfallDungeonTextActions _dungeonText;
    private readonly Func<DaggerfallPropertyStorageKey, DaggerfallInventorySave> _capturePropertyStorage;
    private readonly Func<DaggerfallTravelMapPixel> _travelPosition;
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _actorDefinitions;
    private readonly Func<long> _nextCastSequence;
    internal Func<DaggerfallInfectionsSave> Infections { get; set; } = () => DaggerfallInfectionsSave.Empty;
    internal Func<DaggerfallWeatherSave> Weather { get; set; } = null!;
    internal Func<DaggerfallReadySpell?> ReadySpell {get;set;}=()=>null;
    internal Func<DaggerfallCreateItemRequest?> PendingCreateItem { get; set; } = () => null;
    internal Func<DaggerfallIdentifyRequest?> PendingIdentify { get; set; } = () => null;
    internal Func<DaggerfallDispelRequest?> PendingDispel { get; set; } = () => null;
    internal Func<string?> PendingTeleport { get; set; } = () => null;
    internal Func<DaggerfallTeleportAnchor?> TeleportAnchor { get; set; } = () => null;
    internal Func<IReadOnlySet<long>> BanishedActors { get; set; } = () => new HashSet<long>();
    internal DaggerSessionPersistence(DaggerfallState state, DaggerfallCorpseLootModule corpses, DaggerfallGroundContainers groundContainers, DaggerfallBookNotebook notebook,
        DaggerfallUniqueItemAllocator uniqueItems, FirstPersonCameraSystem camera, DaggerfallWorldTime time, DaggerfallSiteContext site,
        DaggerfallEffectLifecycle effects, Func<DaggerfallDoorRuntime> doors, DaggerfallLocomotionPolicy locomotion,
        DaggerfallClimbingPolicy climbing, DaggerfallSwimmingPolicy swimming, DaggerfallDungeonTextActions dungeonText,
        Func<DaggerfallPropertyStorageKey, DaggerfallInventorySave> capturePropertyStorage, Func<DaggerfallTravelMapPixel> travelPosition, IReadOnlyDictionary<long, DaggerfallActorDefinition> actorDefinitions, Func<long> nextCastSequence)
    {
        _nextCastSequence = nextCastSequence;
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(locomotion);
        ArgumentNullException.ThrowIfNull(climbing);
        ArgumentNullException.ThrowIfNull(swimming);
        ArgumentNullException.ThrowIfNull(dungeonText);
        ArgumentNullException.ThrowIfNull(capturePropertyStorage);
        _actorDefinitions = actorDefinitions;
        State = state; _corpseLoot = corpses; _groundContainers = groundContainers ?? throw new ArgumentNullException(nameof(groundContainers)); _notebook = notebook ?? throw new ArgumentNullException(nameof(notebook)); _uniqueItems = uniqueItems; _camera = camera; _time = time; _site = site; _effects = effects; _doors = doors; _locomotion = locomotion; _climbing = climbing; _swimming = swimming; _dungeonText = dungeonText;
        _capturePropertyStorage = capturePropertyStorage;
        _travelPosition = travelPosition ?? throw new ArgumentNullException(nameof(travelPosition));
    }
    internal RulesetSavePayload Capture(ulong? generation, ulong? step, IReadOnlyDictionary<long, DaggerfallActorId> dynamicActors, DaggerfallEncounterRuntime encounters, IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> siteDeltas, DaggerfallWorldProfileKey activeProfile, DaggerfallWorldProfileKey? returnProfile, IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallDungeonDiscovery> dungeonDiscoveries, IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallDungeonActionGraph> dungeonActions, DaggerfallDungeonMotionSnapshot dungeonMotion, DaggerfallExteriorCellResidencySave? exteriorResidency)
    {
        ArgumentNullException.ThrowIfNull(dynamicActors);
        ArgumentNullException.ThrowIfNull(encounters);
        ArgumentNullException.ThrowIfNull(siteDeltas);
        ArgumentNullException.ThrowIfNull(dungeonDiscoveries);
        ArgumentNullException.ThrowIfNull(dungeonActions);
        ArgumentNullException.ThrowIfNull(dungeonMotion);
        PlayerControlState control = State.PlayerControl;
        WorldPoint playerPosition = control.Position
            ?? throw new InvalidOperationException("Daggerfall cannot save without a player position.");
        DaggerfallPlayerSave player = new(
            playerPosition.X, playerPosition.Y, playerPosition.Z,
            control.YawRadians, control.PitchRadians,
            DaggerfallStatsSaveBoundary.Capture(State.Actors.Player.Stats, State.Actors.Player.Actor.Entity));
        DaggerfallActorSave[] actors = State.Actors.All
            .Where(actor => !dynamicActors.ContainsKey(actor.DurableId))
            .OrderBy(actor => actor.DurableId)
            .Select(actor => new DaggerfallActorSave(
                actor.DurableId,
                actor.Position.X, actor.Position.Y, actor.Position.Z, actor.HeadingYawRadians,
                DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity)) { WabbajackDefinition = DaggerfallWabbajack.DefinitionOf(actor.Actor), ForcedHostile = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile, MagicallyPacified=actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().MagicallyPacified })
            .ToArray();
        DaggerfallDynamicActorSave[] spawned = dynamicActors.OrderBy(entry => entry.Key)
            .Select(entry => CaptureDynamicActor(entry.Key, entry.Value.Value)).ToArray();
        DaggerfallInventorySave inventorySave = CaptureInventory(State.Inventory, State.Equipment, DaggerfallItemOwner.Player);
        DaggerfallCorpseSave[] corpses = _corpseLoot.Corpses.Values.OrderBy(corpse => corpse.ActorId).Select(corpse =>
        {
            // Corpses never carry equipment: anything worn stays on the actor's own inventory
            // section, so the corpse save shares the stack/unique contents mapping only.
            (DaggerfallStackSave[] stacks, DaggerfallUniqueSave[] uniques) = corpse.IsRegistered
                ? CaptureContents(State.Containers.Read(corpse.Owner), DaggerfallItemOwner.Corpse(corpse.ActorId))
                : ([], []);
            return new DaggerfallCorpseSave(
                corpse.ActorId,
                corpse.ContainerIdentity.Value,
                corpse.OriginatingSequence,
                corpse.IsRegistered,
                corpse.IsInteractable,
                stacks,
                uniques);
        }).ToArray();
        DaggerfallActorInventorySave[] actorInventories = State.ActorInventories.All.OrderBy(entry => entry.Key)
            .Select(entry => new DaggerfallActorInventorySave(entry.Key, CaptureInventory(entry.Value, State.ActorInventories.EquipmentFor(entry.Key), DaggerfallItemOwner.Actor(entry.Key)))).ToArray();
        return DaggerfallSavePayload.Encode(new DaggerfallSavePayload(
            player,
            actors,
            spawned,
            State.Progression.Experience,
            State.Progression.Level,
            inventorySave,
            corpses,
            _uniqueItems.CaptureState(),
            State.Kit.AttackExecution.CaptureCooldowns(generation, step)
                .Select(value => new DaggerfallCombatCooldownSave(value.AttackerId, value.RemainingSteps)).ToArray(),
            new DaggerfallCalendarSave(_time.Calendar.Year, _time.Calendar.Month, _time.Calendar.Day, _time.Calendar.Hour, _time.Calendar.Minute, _time.Calendar.Second, _time.RemainderSeconds),
            _site.Capture() with { ActiveProfile = DaggerfallWorldProfileKeySave.Capture(activeProfile), ReturnProfile = returnProfile is { } returned ? DaggerfallWorldProfileKeySave.Capture(returned) : null },
            actorInventories,
            new DaggerfallVariablesSave([.. State.Variables.Capture().Select(entry => new DaggerfallVariableSave((int)entry.Address.Scope, entry.Address.Owner, entry.Address.Key, entry.Value))]),
            new DaggerfallNpcSave([.. State.Npcs.Capture().Select(npc => new DaggerfallNpcEntry(
                npc.DurableId, (int)npc.Kind, npc.StableKey, npc.Site.Region, npc.Site.Location, npc.Site.Building,
                npc.Appearance.Race, npc.Appearance.Gender, npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord,
                npc.Appearance.NameSeed, npc.Appearance.FactionId, npc.Role, [.. npc.Services],
                (int)npc.Presence, npc.X, npc.Y, npc.Z)
                { Profile = npc.Profile is { } profile ? DaggerfallWorldProfileKeySave.Capture(profile) : null, DisplayName = npc.DisplayName, ProfileId = npc.Site.ProfileId })]),
            _effects.Capture(),
            State.SkillUses.Capture(),
            State.Social.Capture(),
            Character: State.Character.Capture(),
            LevelUp: State.LevelUps.Capture())
        {
            CustomSpells = State.Character.CaptureConstructedSpells(),
            MagicRounds = State.Effects.MagicRounds,
            Infections = Infections(),
            NextCastSequence = _nextCastSequence(),
            ReadySpell=ReadySpell(),
            PendingCreateItem = PendingCreateItem(),
            PendingDispel = PendingDispel(),
            PendingTeleport = PendingTeleport(),
            TeleportAnchor = TeleportAnchor(),
            PendingIdentify = PendingIdentify(),
            BanishedActors = [.. BanishedActors().Order()],
            Quests = State.Quests.Capture(),
            Doors = _doors().Capture(),
            SiteDeltas = [.. siteDeltas.OrderBy(entry => entry.Key.Site.Region).ThenBy(entry => entry.Key.Site.Index).ThenBy(entry => entry.Key.LogicalId, StringComparer.Ordinal).Select(entry => new DaggerfallSiteDeltaSave(
                DaggerfallWorldProfileKeySave.Capture(entry.Key), entry.Value.Actors, entry.Value.DynamicActors, entry.Value.ActorInventories, entry.Value.Corpses, entry.Value.Doors, entry.Value.Effects)
            {
                BanishedActors = entry.Value.BanishedActors,
                Motion = entry.Value.Motion
                    ?? throw new InvalidOperationException($"Inactive site '{entry.Key.LogicalId}' has no captured dungeon motion snapshot."),
            })],
            DungeonMotion = dungeonMotion,
            ExteriorResidency = exteriorResidency,
            Currency = State.Currency.Capture(),
            Bank = State.Bank.Capture(),
            Loans = State.Loans.Capture(),
            Property = State.Property.Capture(_capturePropertyStorage),
            Lodging = State.Lodging.Capture(_time.Calendar.ToAbsoluteSeconds()),
            Travel = State.Travel.Capture(_time.Calendar.ToAbsoluteSeconds(), _site.Active, _travelPosition),
            Crime = State.Crime.Capture(),
            KnightlyClaims = State.KnightlyClaims.Capture(),
            Services = State.Services.Capture(),
            QuestTraining = State.QuestTraining.Capture(),
            RegionalPrices = State.RegionalPrices.Capture(),
            Weather = Weather(),
            Transport = State.Transport.Capture(),
            Swimming = State.Swimming.Capture(),
            Wagon = State.Wagon.Capture(),
            Locomotion = _locomotion.Capture(),
            Climbing = _climbing.Capture(),
            DungeonDiscovery = dungeonDiscoveries.Values.OrderBy(value => value.Profile.Site.Region)
                .ThenBy(value => value.Profile.Site.Index).ThenBy(value => value.Profile.LogicalId, StringComparer.Ordinal)
                .Select(value => value.Capture()).ToArray(),
            DungeonActions = dungeonActions.Values.OrderBy(value => value.ProfileId, StringComparer.Ordinal)
                .Select(value => value.Capture()).ToArray(),
            DungeonText = _dungeonText.Capture(),
            Encounters = encounters.Capture(),
            Notebook = _notebook.Capture(),
            QuestCustody = State.QuestItems.Custody.OrderBy(value => value.Id).Select(value =>
            {
                var (stacks, uniques) = CaptureContents(State.Containers.Read(value.Owner), DaggerfallItemOwner.Quest(value.Id));
                return new DaggerfallQuestCustodySave(value.InstanceId, value.Id, new(stacks, uniques, []));
            }).ToArray(),
            GroundContainers = _groundContainers.Capture(),
        });
    }

    /// <summary>Captures one unloading site's actor-owned state without retaining runtime entities.</summary>
    internal DaggerfallSiteRuntimeDelta CaptureSiteDelta(DaggerfallSiteProfile inputs, DaggerfallDoorRuntime doors, DaggerfallDungeonMotionProjection motion,
        IReadOnlyDictionary<long, DaggerfallActorId> dynamicActors)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(dynamicActors);
        long[] authoredIds = [.. inputs.Project.Actors.Keys.Where(id => !BanishedActors().Contains(id)).OrderBy(id => id)];
        DaggerfallActorSave[] actors = authoredIds.Select(id =>
        {
            ActorState actor = State.Actors.TryGet(id, out ActorState? current)
                ? current
                : throw new InvalidOperationException($"Site actor {id} disappeared before its site state could be captured.");
            return new DaggerfallActorSave(actor.DurableId, actor.Position.X, actor.Position.Y, actor.Position.Z,
                actor.HeadingYawRadians, DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity)) { WabbajackDefinition = DaggerfallWabbajack.DefinitionOf(actor.Actor), ForcedHostile = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile, MagicallyPacified=actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().MagicallyPacified };
        }).ToArray();
        DaggerfallDynamicActorSave[] spawned = dynamicActors.OrderBy(entry => entry.Key)
            .Select(entry => CaptureDynamicActor(entry.Key, entry.Value.Value)).ToArray();
        long[] ids = [.. authoredIds, .. spawned.Select(actor => actor.EntityId)];
        DaggerfallActorInventorySave[] inventories = ids.Select(id => new DaggerfallActorInventorySave(
            id,
            CaptureInventory(State.ActorInventories.InventoryFor(id) ?? throw new InvalidOperationException($"Site actor {id} has no inventory."),
                State.ActorInventories.EquipmentFor(id), DaggerfallItemOwner.Actor(id)))).ToArray();
        DaggerfallActiveEffectSave[] effects = State.Effects.Capture().Where(effect => ids.Contains(effect.TargetId)).ToArray();
        return new DaggerfallSiteRuntimeDelta(actors, spawned, inventories, CaptureCorpses(ids), doors.Capture(), effects, motion.Capture()) { BanishedActors = [.. BanishedActors().Order()] };
    }

    private DaggerfallDynamicActorSave CaptureDynamicActor(long id, string definition)
    {
        ActorState actor = LiveDynamicActor(id);
        return new(id, definition, actor.Position.X, actor.Position.Y, actor.Position.Z, actor.HeadingYawRadians,
            DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity))
        {
            Level = _actorDefinitions[id].Level ?? 1,
            CorruptionOrigin = actor.Actor.TryGet<DaggerfallCorruptionOrigin>(out var origin) ? origin : null,
            WabbajackActive = DaggerfallWabbajack.DefinitionOf(actor.Actor) is not null,
            PlayerAllied = _actorDefinitions[id].Team == "player-ally",
            ForcedHostile = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile,
            MagicallyPacified = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().MagicallyPacified,
        };
    }

    /// <summary>Captures one canonical materialized actor back to its detached site owner.</summary>
    internal DaggerfallSiteRuntimeDelta CaptureDynamicActorDelta(long id, string definition) => new([], [CaptureDynamicActor(id, definition)],
        [new(id, CaptureInventory(State.ActorInventories.InventoryFor(id) ?? throw new InvalidOperationException($"Actor {id} has no inventory."),
            State.ActorInventories.EquipmentFor(id), DaggerfallItemOwner.Actor(id)))], CaptureCorpses([id]), [],
        State.Effects.Capture().Where(effect => effect.TargetId == id).ToArray());

    /// <summary>Reapplies detached values after the destination has created fresh authored actors.</summary>
    internal void RestoreSiteDelta(DaggerfallSiteRuntimeDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        ApplyActorInventories(delta.ActorInventories);
        _corpseLoot.Restore(delta.Corpses, CorpseIdentities(delta.Corpses));
        _effects.Restore(delta.Effects);
    }

    private DaggerfallCorpseSave[] CaptureCorpses(IEnumerable<long> actorIds)
    {
        HashSet<long> selected = [.. actorIds];
        return _corpseLoot.Corpses.Values.Where(corpse => selected.Contains(corpse.ActorId)).OrderBy(corpse => corpse.ActorId).Select(corpse =>
        {
            (DaggerfallStackSave[] stacks, DaggerfallUniqueSave[] uniques) = corpse.IsRegistered
                ? CaptureContents(State.Containers.Read(corpse.Owner), DaggerfallItemOwner.Corpse(corpse.ActorId))
                : ([], []);
            return new DaggerfallCorpseSave(corpse.ActorId, corpse.ContainerIdentity.Value, corpse.OriginatingSequence,
                corpse.IsRegistered, corpse.IsInteractable, stacks, uniques);
        }).ToArray();
    }

    private ActorState LiveDynamicActor(long durableId) =>
        State.Actors.TryGet(durableId, out ActorState? actor)
            ? actor
            : throw new InvalidOperationException($"Daggerfall cannot save dynamic actor {durableId} without a live entity.");

    /// <summary>
    /// Applies a resolved save's relational sections to a freshly composed session, mirroring
    /// <see cref="Capture"/>. Composition has already built each owner from its own section: actor,
    /// player and dynamic-actor stats, variables, NPCs, social standing, character and quest training
    /// (<see cref="DaggerActorFactory"/>); then the calendar, site context, action graphs, dungeon
    /// discovery, doors and motion, the identity ledger, currency, bank, loans, property, crime,
    /// services, knightly claims and regional prices, and the roster's spawned actors. This applies the
    /// rest, in this order:
    /// <list type="number">
    /// <item>inactive sites' deltas;</item>
    /// <item>encounters, against every live and detached spawned actor and the retired identities;</item>
    /// <item>actor and spawned-actor poses;</item>
    /// <item>progression, skill uses, locomotion, climbing, level-up and quests;</item>
    /// <item>player, actor and ground inventories, wagon, transport and notebook;</item>
    /// <item>the player pose, corpses, attack cooldowns and active effects, then the camera;</item>
    /// <item>held enchantments over the restored equipment, once;</item>
    /// <item>defeated actors' appearance, the pending dungeon text and the action-trigger player rebase.</item>
    /// </list>
    /// The exterior window is admitted by the site lifecycle afterwards for both start kinds.
    /// </summary>
    internal void Restore(DaggerfallResolvedRestore restore, DaggerfallSiteLifecycle sites, DaggerfallActorRoster roster,
        DaggerfallEncounterRuntime encounters, DaggerfallHeldEnchantments heldEnchantments,
        Action<DaggerfallDungeonTextSnapshot> restoreDungeonText)
    {
        ArgumentNullException.ThrowIfNull(restore);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(encounters);
        ArgumentNullException.ThrowIfNull(heldEnchantments);
        ArgumentNullException.ThrowIfNull(restoreDungeonText);
        DaggerfallSavePayload saved = restore.Payload;
        sites.RestoreDeltas(saved.SiteDeltas);
        encounters.Restore(saved.Encounters, DynamicActorDefinitions(roster, sites), restore.TombstonedActors);
        RestoreSections(saved);
        heldEnchantments.Refresh();
        sites.Projection.Appearance.SyncRestoredDefeat(State.Actors);
        restoreDungeonText(saved.DungeonText);
        sites.ActionTriggers.RebaseRestoredPlayer(State.PlayerControl, State.Actors.Player.Actor.Entity);
    }

    /// <summary>Every spawned actor's definition, live in the active site or detached in an inactive one.</summary>
    private static IReadOnlyDictionary<long, string> DynamicActorDefinitions(DaggerfallActorRoster roster, DaggerfallSiteLifecycle sites)
    {
        Dictionary<long, string> values = roster.Dynamic.ToDictionary(entry => entry.Key, entry => entry.Value.Value);
        foreach (DaggerfallSiteRuntimeDelta delta in sites.Deltas.Values)
        foreach (DaggerfallDynamicActorSave actor in delta.DynamicActors)
            if (!values.TryAdd(actor.EntityId, actor.Definition))
                throw new InvalidOperationException($"Dynamic actor {actor.EntityId} is active in more than one site profile.");
        return values;
    }

    private void RestoreSections(DaggerfallSavePayload saved)
    {
        DaggerfallActorSave[] actors = saved.Actors.OrderBy(actor => actor.EntityId).ToArray();
        foreach (DaggerfallActorSave actor in actors)
        {
            if (!State.Actors.TryGet(actor.EntityId, out ActorState? current))
                throw new ArgumentException($"The saved Daggerfall actor '{actor.EntityId}' is not present in the selected content.", nameof(saved));
            current.ApplyPose(new ActorPose(new WorldPoint(actor.X, actor.Y, actor.Z), actor.HeadingRadians));
        }

        // Dynamic actors were materialized by construction; restore only re-applies their poses.
        foreach (DaggerfallDynamicActorSave spawned in saved.DynamicActors.OrderBy(actor => actor.EntityId))
        {
            if (!State.Actors.TryGet(spawned.EntityId, out ActorState? current))
                throw new InvalidOperationException($"The saved dynamic actor '{spawned.EntityId}' was not materialized by the selected content.");
            current.ApplyPose(new ActorPose(new WorldPoint(spawned.X, spawned.Y, spawned.Z), spawned.HeadingRadians));
        }

        State.Progression.AdvanceTo(saved.Experience, saved.Level);
        State.SkillUses.Restore(saved.SkillUses);
        _locomotion.Restore(saved.Locomotion);
        _climbing.Restore(saved.Climbing);
        _swimming.Restore(saved.Swimming);
        State.LevelUps.Restore(saved.LevelUp);
        State.Quests.Restore(saved.Quests);

        ApplyInventory(saved.Inventory, State.Inventory, State.Equipment, DaggerfallItemOwner.Player);
        ApplyActorInventories(saved.ActorInventories);
        _groundContainers.Restore(saved.GroundContainers);
        State.QuestItems.Restore(saved.QuestCustody);
        if (saved.Wagon is { } wagon) State.Wagon.Restore(wagon);
        State.Transport.Restore(saved.Transport);
        _notebook.Restore(saved.Notebook);

        WorldPoint position = new(saved.Player.X, saved.Player.Y, saved.Player.Z);
        State.PlayerControl.YawRadians = saved.Player.YawRadians;
        State.PlayerControl.PitchRadians = saved.Player.PitchRadians;
        State.PlayerControl.Restore(position, default);
        // Native continuation and held input start fresh; durable pose and corpse relationships are restored.
        _corpseLoot.Restore(saved.Corpses, CorpseIdentities(saved.Corpses));
        State.Kit.AttackExecution.RestoreCooldowns(saved.CombatCooldowns.Select(value => new AttackCooldown(value.AttackerId, value.RemainingSteps)));
        // Actors, their shared stats/tracks, and item identities exist before active effects rebuild
        // their reversible contributions. Resume deliberately does not replay an initial magic round.
        _effects.Restore(saved.ActiveEffects);
        _camera.Update(State.PlayerControl);
        // Enemy behavior, perception leases, held input, pending loot, facts,
        // presentation effects and a swing still waiting for its damage frame are
        // intentionally transient.  The next admitted step observes rebuilt actor
        // state without replaying them; a swing cut off by a save keeps the cooldown
        // it already charged but never lands.
    }

    private DaggerfallInventorySave CaptureInventory(MechanicsInventoryCoordinator inventoryOwner, MechanicsEquipmentCoordinator equipmentOwner, DaggerfallItemOwner owner)
    {
        InventoryView inventory = inventoryOwner.Read();
        EquipmentRead equipped = equipmentOwner.Read();
        (DaggerfallStackSave[] stacks, DaggerfallUniqueSave[] uniques) = CaptureContents(inventory, owner);
        return new(
            stacks,
            uniques,
            equipped.Assignments.OrderBy(assignment => assignment.Slot.Value, StringComparer.Ordinal)
                .Select(assignment => new DaggerfallEquipmentSave(assignment.Slot.Value, State.Actors.Entities.IdentityOf(new EntityId(assignment.Item.EntityId)).Value)).ToArray());
    }

    /// <summary>Shared stack/unique contents mapping for actor inventories and corpse containers.</summary>
    private (DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems) CaptureContents(InventoryView contents, DaggerfallItemOwner owner) =>
        DaggerfallInventorySaveBoundary.CaptureContents(contents, owner, State.ItemInstances, State.Actors.Entities);

    private static IReadOnlyDictionary<long, DurableIdentityReference> CorpseIdentities(IEnumerable<DaggerfallCorpseSave> corpses) =>
        corpses.ToDictionary(
            corpse => corpse.ActorId,
            corpse => new DurableIdentityReference(DurableIdentityKind.Container, corpse.ContainerId));

    private void ApplyInventory(DaggerfallInventorySave saved, MechanicsInventoryCoordinator inventory, MechanicsEquipmentCoordinator equipment, DaggerfallItemOwner owner)
    {
        foreach (DaggerfallStackSave stack in saved.Stacks)
        {
            InventoryStackId stackId = InventoryStackId.Parse(stack.StackId);
            inventory.Grant(new InventoryGrant(new InventoryItemId(stack.ItemId), stackId, stack.Quantity));
            State.ItemInstances.RegisterStack(owner, stackId, DaggerfallItemInstanceMetadata.Restore(stack.ItemId, stack.Metadata));
        }
        Dictionary<ulong, KitUniqueInventoryItem> unique = [];
        foreach (DaggerfallUniqueSave item in saved.UniqueItems)
        {
            unique.Add(item.EntityId, equipment.Materialize(new DurableIdentityReference(DurableIdentityKind.Item, item.EntityId), new InventoryItemId(item.ItemId)));
            State.ItemInstances.RegisterUnique(item.EntityId, DaggerfallItemInstanceMetadata.Restore(item.ItemId, item.Metadata));
        }
        foreach (IGrouping<ulong, DaggerfallEquipmentSave> group in saved.Equipment.GroupBy(value => value.ItemEntityId))
        {
            equipment.Equip(unique[group.Key], group.Select(value => new KitEquipmentSlotId(value.SlotId)).ToArray());
        }
    }

    private void ApplyActorInventories(DaggerfallActorInventorySave[] saved)
    {
        foreach (DaggerfallActorInventorySave section in saved)
        {
            MechanicsInventoryCoordinator inventory = State.ActorInventories.InventoryFor(section.EntityId)
                ?? throw new ArgumentException($"Saved inventory owner {section.EntityId} is missing.");
            ApplyInventory(section.Inventory, inventory, State.ActorInventories.EquipmentFor(section.EntityId), DaggerfallItemOwner.Actor(section.EntityId));
        }
    }
}
