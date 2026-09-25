using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using Rusty.Engine;
using System.Numerics;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
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
    private readonly DaggerfallDungeonTextActions _dungeonText;
    private readonly Func<DaggerfallPropertyStorageKey, DaggerfallInventorySave> _capturePropertyStorage;
    internal DaggerSessionPersistence(DaggerfallState state, DaggerfallCorpseLootModule corpses, DaggerfallGroundContainers groundContainers, DaggerfallBookNotebook notebook,
        DaggerfallUniqueItemAllocator uniqueItems, FirstPersonCameraSystem camera, DaggerfallWorldTime time, DaggerfallSiteContext site,
        DaggerfallEffectLifecycle effects, Func<DaggerfallDoorRuntime> doors, DaggerfallLocomotionPolicy locomotion,
        DaggerfallClimbingPolicy climbing, DaggerfallDungeonTextActions dungeonText,
        Func<DaggerfallPropertyStorageKey, DaggerfallInventorySave> capturePropertyStorage)
    {
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(locomotion);
        ArgumentNullException.ThrowIfNull(climbing);
        ArgumentNullException.ThrowIfNull(dungeonText);
        ArgumentNullException.ThrowIfNull(capturePropertyStorage);
        State = state; _corpseLoot = corpses; _groundContainers = groundContainers ?? throw new ArgumentNullException(nameof(groundContainers)); _notebook = notebook ?? throw new ArgumentNullException(nameof(notebook)); _uniqueItems = uniqueItems; _camera = camera; _time = time; _site = site; _effects = effects; _doors = doors; _locomotion = locomotion; _climbing = climbing; _dungeonText = dungeonText;
        _capturePropertyStorage = capturePropertyStorage;
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
                DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity)))
            .ToArray();
        DaggerfallDynamicActorSave[] spawned = dynamicActors
            .OrderBy(entry => entry.Key)
            .Select(entry =>
            {
                ActorState actor = LiveDynamicActor(entry.Key);
                return new DaggerfallDynamicActorSave(
                    entry.Key,
                    entry.Value.Value,
                    actor.Position.X, actor.Position.Y, actor.Position.Z, actor.HeadingYawRadians,
                    DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity));
            })
            .ToArray();
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
        DaggerfallActorInventorySave[] actorInventories = State.ActorInventories.OrderBy(entry => entry.Key)
            .Select(entry => new DaggerfallActorInventorySave(entry.Key, CaptureInventory(entry.Value, State.EquipmentFor(entry.Key), DaggerfallItemOwner.Actor(entry.Key)))).ToArray();
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
                (int)npc.Presence, npc.X, npc.Y, npc.Z))]),
            _effects.Capture(),
            State.SkillUses.Capture(),
            State.Social.Capture(),
            Character: State.Character.Capture(),
            LevelUp: State.LevelUps.Capture())
        {
            Quests = State.Quests.Capture(),
            Doors = _doors().Capture(),
            SiteDeltas = [.. siteDeltas.OrderBy(entry => entry.Key.Site.Region).ThenBy(entry => entry.Key.Site.Index).ThenBy(entry => entry.Key.LogicalId, StringComparer.Ordinal).Select(entry => new DaggerfallSiteDeltaSave(
                DaggerfallWorldProfileKeySave.Capture(entry.Key), entry.Value.Actors, entry.Value.DynamicActors, entry.Value.ActorInventories, entry.Value.Corpses, entry.Value.Doors, entry.Value.Effects)
            {
                Motion = entry.Value.Motion
                    ?? throw new InvalidOperationException($"Inactive site '{entry.Key.LogicalId}' has no captured dungeon motion snapshot."),
            })],
            DungeonMotion = dungeonMotion,
            ExteriorResidency = exteriorResidency,
            Currency = State.Currency.Capture(),
            Bank = State.Bank.Capture(),
            Loans = State.Loans.Capture(),
            Poisons = State.Poisons.Capture(),
            Property = State.Property.Capture(_capturePropertyStorage),
            Crime = State.Crime.Capture(),
            KnightlyClaims = State.KnightlyClaims.Capture(),
            Services = State.Services.Capture(),
            QuestTraining = State.QuestTraining.Capture(),
            RegionalPrices = State.RegionalPrices.Capture(),
            Transport = State.Transport.Capture(),
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
            GroundContainers = _groundContainers.Persisted.OrderBy(container => container.Id).Select(container =>
            {
                (DaggerfallStackSave[] stacks, DaggerfallUniqueSave[] uniques) = CaptureContents(State.Containers.Read(container.Owner), DaggerfallItemOwner.Ground(container.Id));
                return new DaggerfallGroundContainerSave(DaggerfallWorldProfileKeySave.Capture(container.Profile), container.Id, container.Position.X, container.Position.Y, container.Position.Z,
                    new DaggerfallInventorySave(stacks, uniques, []));
            }).ToArray(),
        });
    }

    /// <summary>Captures one unloading site's actor-owned state without retaining runtime entities.</summary>
    internal DaggerfallSiteRuntimeDelta CaptureSiteDelta(PrivateersHoldInputs inputs, DaggerfallDoorRuntime doors, DaggerfallDungeonMotionProjection motion,
        IReadOnlyDictionary<long, DaggerfallActorId> dynamicActors)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(dynamicActors);
        long[] authoredIds = [.. inputs.Project.Actors.Keys.OrderBy(id => id)];
        DaggerfallActorSave[] actors = authoredIds.Select(id =>
        {
            ActorState actor = State.Actors.TryGet(id, out ActorState? current)
                ? current
                : throw new InvalidOperationException($"Site actor {id} disappeared before its site state could be captured.");
            return new DaggerfallActorSave(actor.DurableId, actor.Position.X, actor.Position.Y, actor.Position.Z,
                actor.HeadingYawRadians, DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity));
        }).ToArray();
        DaggerfallDynamicActorSave[] spawned = dynamicActors.OrderBy(entry => entry.Key).Select(entry =>
        {
            ActorState actor = LiveDynamicActor(entry.Key);
            return new DaggerfallDynamicActorSave(entry.Key, entry.Value.Value, actor.Position.X, actor.Position.Y, actor.Position.Z,
                actor.HeadingYawRadians, DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity));
        }).ToArray();
        long[] ids = [.. authoredIds, .. spawned.Select(actor => actor.EntityId)];
        DaggerfallActorInventorySave[] inventories = ids.Select(id => new DaggerfallActorInventorySave(
            id,
            CaptureInventory(State.InventoryFor(id) ?? throw new InvalidOperationException($"Site actor {id} has no inventory."),
                State.EquipmentFor(id), DaggerfallItemOwner.Actor(id)))).ToArray();
        DaggerfallActiveEffectSave[] effects = State.Effects.Active
            .Where(effect => ids.Contains(checked((long)effect.Lifecycle.Context.Target.Value)))
            .Select(effect => new DaggerfallActiveEffectSave(
                effect.Lifecycle.Context.Instance.Value,
                effect.Definition.Key,
                effect.Lifecycle.Context.Source.Key,
                effect.Lifecycle.Context.Caster?.Value is ulong caster ? checked((long)caster) : null,
                checked((long)effect.Lifecycle.Context.Target.Value),
                effect.Lifecycle.Context.Settings,
                effect.Lifecycle.Context.Element,
                effect.Lifecycle.Context.Item?.Value,
                effect.Lifecycle.RemainingRounds,
                effect.Lifecycle.Stacks,
                effect.State.Clone()))
            .ToArray();
        return new DaggerfallSiteRuntimeDelta(actors, spawned, inventories, CaptureCorpses(ids), doors.Capture(), effects, motion.Capture());
    }

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

    internal void Restore(DaggerfallSavePayload saved)
    {
        // Current-state relationships were resolved before this fresh session was constructed.
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
        State.LevelUps.Restore(saved.LevelUp);
        State.Quests.Restore(saved.Quests);

        ApplyInventory(saved.Inventory, State.Inventory, State.Equipment, DaggerfallItemOwner.Player);
        ApplyActorInventories(saved.ActorInventories);
        _groundContainers.Restore(saved.GroundContainers);
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
    private (DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems) CaptureContents(InventoryView contents, DaggerfallItemOwner owner) => (
        contents.Stacks.OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
            .Select(stack => new DaggerfallStackSave(stack.Id.Value, stack.Definition.Value, stack.Quantity,
                State.ItemInstances.RequireStack(owner, stack.Id).Capture())).ToArray(),
        contents.UniqueItems.OrderBy(item => item.Entity.Value)
            .Select(item =>
            {
                ulong identity = State.Actors.Entities.IdentityOf(item.Entity).Value;
                return new DaggerfallUniqueSave(item.Definition.Value, identity, State.ItemInstances.RequireUnique(identity).Capture());
        }).ToArray());

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
            MechanicsInventoryCoordinator inventory = State.InventoryFor(section.EntityId)
                ?? throw new ArgumentException($"Saved inventory owner {section.EntityId} is missing.");
            ApplyInventory(section.Inventory, inventory, State.EquipmentFor(section.EntityId), DaggerfallItemOwner.Actor(section.EntityId));
        }
    }
}
