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
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerActorAssembly(DaggerfallState State, Dictionary<long, DaggerfallActorDefinition> Definitions, DaggerfallActorDefinition PlayerDefinition);

/// <summary>Explicit entity/component construction from admitted Dagger definitions or current saves.</summary>
internal static class DaggerActorFactory
{
    private const ulong PlayerMechanicsEntityId = (ulong)DaggerfallActorIdentity.PlayerEntityId;
    internal static CapacityMetricId ClassicWeightMetric { get; } = CapacityMetricId.Parse("daggerfall.classic-weight");
    internal static DaggerActorAssembly Create(IRandomService random, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallSavePayload? saved)
    {
        ActorsState actors = new();
        try
        {
            DaggerfallMechanicsState mechanics = new();
            DaggerfallActorDefinition playerDefinition = definitions.RequireActor(new DaggerfallActorId("player"));
            DaggerfallCareerDefinition initialCareer = definitions.Catalogs.RequireCareer(playerDefinition.Career
                ?? throw new InvalidOperationException("The Daggerfall player definition must name its initial career."));
            ValidateInitialEntityIds(inputs, playerDefinition.Loadout);
            Dictionary<InventoryItemId, ItemDefinition> itemDefinitions = definitions.Items.Values
                .Concat(definitions.TemplateItems.Values)
                .ToDictionary(item => new InventoryItemId(item.Id.Value), ToManagedItem);
            Dictionary<KitEquipmentSlotId, EquipmentSlotDefinition> equipmentSlots = definitions.EquipmentSlots.Values
                .ToDictionary(slot => new KitEquipmentSlotId(slot.Id.Value), ToManagedSlot);
            PlayerActorState player = actors.CreatePlayer(checked((long)PlayerMechanicsEntityId),
                new EntityTypeId(playerDefinition.Id.Value), mechanics.CreateStats(playerDefinition, DaggerfallPlayerVitals.Initial(playerDefinition.Stats, initialCareer)), playerDefinition.Combat.Health.Value);
            EntityId playerEntity = player.Actor.Entity;
            if (saved is not null) RestoreStats(player.Actor, saved.Player.Stats);
            InventoryStore inventoryStore = new();
            // Engine owns one authoritative sum of item capacity costs. Daggerfall's mutable
            // strength maximum remains policy, so the unbounded Engine limit supplies the live
            // used value without inventing a second inventory ledger.
            inventoryStore.RegisterInventory(new InventoryState(playerEntity, [new InventoryCapacityLimit(ClassicWeightMetric, ulong.MaxValue)]));
            inventoryStore.RegisterEquipment(new EquipmentState(playerEntity));
            player.Actor.Add(new InventoryComponent(inventoryStore, playerEntity));
            player.Actor.Add(new EquipmentComponent(inventoryStore, playerEntity));
            MechanicsInventoryCoordinator inventory = new(player.Inventory, actors.Entities, itemDefinitions);
            MechanicsInventoryContainerCoordinator containers = new(inventoryStore, actors.Entities, itemDefinitions);
            MechanicsEquipmentCoordinator equipmentCoordinator = new(player.Inventory, player.Equipment, actors.Entities, itemDefinitions, equipmentSlots);
            DaggerfallItemInstances itemInstances = new();
            foreach ((DaggerfallLoadoutEntry entry, int ordinal) in playerDefinition.Loadout
                .Where(entry => saved is null && definitions.Items[entry.ItemId].IsFungible)
                .Select((entry, ordinal) => (entry, ordinal)))
            {
                InventoryStackId stackId = DaggerfallInventoryStackIds.ForInitialLoadout(checked((long)PlayerMechanicsEntityId), ordinal);
                inventory.Grant(new InventoryGrant(new InventoryItemId(entry.ItemId.Value),
                    stackId, entry.Quantity));
                itemInstances.RegisterDefaultStack(DaggerfallItemOwner.Player,
                    new InventoryStack(stackId, ItemDefinitionId.Parse(entry.ItemId.Value), entry.Quantity), definitions.RequireItem(entry.ItemId));
            }
            foreach (DaggerfallLoadoutEntry entry in playerDefinition.Loadout.Where(entry => saved is null && !definitions.Items[entry.ItemId].IsFungible))
            {
                KitUniqueInventoryItem item = equipmentCoordinator.Materialize(
                    new DurableIdentityReference(DurableIdentityKind.Item, entry.UniqueEntityId!.Value),
                    new InventoryItemId(entry.ItemId.Value));
                itemInstances.RegisterDefaultUnique(entry.UniqueEntityId.Value, definitions.RequireItem(entry.ItemId), DaggerfallItemOwner.Player);
                if (entry.EquipSlot is DaggerfallEquipmentSlotId slot)
                {
                    equipmentCoordinator.Equip(
                        item,
                        [new KitEquipmentSlotId(slot.Value)]);
                }
            }
            Dictionary<long, DaggerfallActorDefinition> authored = [];
            foreach (AuthoredActor source in inputs.Project.Actors.Values)
            {
                if (!definitions.Actors.TryGetValue(source.ActorId, out DaggerfallActorDefinition? definition))
                    throw new InvalidOperationException($"Privateer's Hold placement '{source.EntityId}' refers to missing actor '{source.ActorId.Value}'.");
                ActorState actor = actors.CreateActor(source.EntityId, new EntityTypeId(definition.Id.Value),
                    mechanics.CreateStats(definition, InitialVitals(random, definition, source.EntityId)),
                    new ActorPose(source.Position, 0f), definition.Combat.Health.Value);
                if (saved is not null) RestoreStats(actor.Actor, saved.Actors.Single(value => value.EntityId == source.EntityId).Stats);
                authored.Add(source.EntityId, definition);
                RegisterActorInventory(actor, inventoryStore);
                // A placed actor whose definition declares a loadout carries it in a managed
                // inventory over the session's inventory store: today that is the ranged actors'
                // quiver, which a shot draws from and the save persists. A unique loadout entry
                // would need an equipped placement, which no placed actor has yet, so one is
                // refused rather than half-granted.
                if (definition.Loadout.Count > 0)
                {
                    if (definitions.Items.Where(item => definition.Loadout.Any(entry => entry.ItemId == item.Key)).Any(item => item.Value.IsFungible is false))
                        throw new InvalidOperationException($"Placed actor '{source.ActorId.Value}' loadout carries a unique item, which placed actors do not equip yet.");
                    MechanicsInventoryCoordinator actorInventory = new(actor.Inventory, actors.Entities, itemDefinitions);
                    if (saved is null)
                    {
                        foreach ((DaggerfallLoadoutEntry entry, int ordinal) in definition.Loadout
                            .Where(entry => definitions.Items[entry.ItemId].IsFungible)
                            .Select((entry, ordinal) => (entry, ordinal)))
                        {
                            InventoryStackId stackId = DaggerfallInventoryStackIds.ForInitialLoadout(source.EntityId, ordinal);
                            actorInventory.Grant(new InventoryGrant(new InventoryItemId(entry.ItemId.Value),
                                stackId, entry.Quantity));
                            itemInstances.RegisterDefaultStack(DaggerfallItemOwner.Actor(source.EntityId),
                                new InventoryStack(stackId, ItemDefinitionId.Parse(entry.ItemId.Value), entry.Quantity), definitions.RequireItem(entry.ItemId));
                        }
                    }
                }
            }
            DaggerfallVariableStore variables = new(definitions.QuestSources.Tables.Globals.Lookup);
            DaggerfallNpcRegistry npcs = new();
            if (saved?.Variables is { } restoredVariables)
            {
                variables.Restore(restoredVariables.Entries.Select(entry => (entry.Require(), entry.Value)));
            }

            if (saved?.Npcs is { } restoredNpcs)
            {
                npcs.Restore(restoredNpcs.Entries.Select(entry => new DaggerfallNpc(
                    entry.DurableId,
                    (DaggerfallNpcKind)entry.Kind,
                    entry.StableKey,
                    new DaggerfallNpcSite(entry.Region, entry.Location, entry.Building),
                    new DaggerfallNpcAppearance(entry.Race, entry.Gender, entry.BillboardArchive, entry.BillboardRecord, entry.NameSeed, entry.FactionId),
                    entry.Role,
                    entry.Services,
                    (DaggerfallNpcPresence)entry.Presence,
                    entry.X, entry.Y, entry.Z)));
            }

            DaggerfallSocialState social = new(definitions.Factions);
            if (saved?.Social is { } restoredSocial)
            {
                social.Restore(restoredSocial);
            }

            DaggerfallCharacterState character = new(definitions, player.Stats, playerDefinition, saved?.Character);
            DaggerfallState state = new(new PlayerControlState(inputs.Project.PlayerPosition, inputs.InitialLook.YawRadians, inputs.InitialLook.PitchRadians), actors, inventory, equipmentCoordinator, containers, itemDefinitions, equipmentSlots, inventoryStore, variables, npcs, social, itemInstances, character, new DaggerfallQuestInstances(definitions, random));
            authored.Add(DaggerfallActorIdentity.PlayerEntityId, playerDefinition);
            if (saved is not null) MaterializeDynamicActors(random, actors, mechanics, definitions, saved, authored, inventoryStore);
            return new(state, authored, playerDefinition);
        }
        catch { actors.Dispose(); throw; }
    }
    /// <summary>
    /// Rebuilds dynamically spawned actors from the save: fresh runtime entities bound to the same
    /// durable identities, stats restored from the saved boundary, and definition references
    /// re-registered so every definition consumer keeps working. Inventories restore through the
    /// actor inventory sections, never through a loadout re-grant.
    /// </summary>
    internal static void MaterializeDynamicActors(
        IRandomService random,
        ActorsState actors,
        DaggerfallMechanicsState mechanics,
        DaggerfallDefinitions definitions,
        DaggerfallSavePayload saved,
        Dictionary<long, DaggerfallActorDefinition> definitionsByActor,
        InventoryStore inventoryStore)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(mechanics);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(definitionsByActor);
        ArgumentNullException.ThrowIfNull(inventoryStore);
        foreach (DaggerfallDynamicActorSave spawned in saved.DynamicActors.OrderBy(value => value.EntityId))
        {
            DaggerfallActorDefinition definition = definitions.RequireActor(new DaggerfallActorId(spawned.Definition));
            // Keyed draws are deterministic per identity, so this construction roll cannot skew
            // any other roll; the saved boundary replaces the whole component immediately after.
            ActorState actor = actors.CreateActor(spawned.EntityId, new EntityTypeId(definition.Id.Value),
                mechanics.CreateStats(definition, InitialVitals(random, definition, spawned.EntityId)),
                new ActorPose(new WorldPoint(spawned.X, spawned.Y, spawned.Z), spawned.HeadingRadians),
                definition.Combat.Health.Value);
            RestoreStats(actor.Actor, spawned.Stats);
            definitionsByActor.Add(spawned.EntityId, definition);
            RegisterActorInventory(actor, inventoryStore);
        }
    }

    /// <summary>
    /// Gives one actor its managed inventory and equipment over the session store, the same
    /// binding authored placement actors are constructed with.
    /// </summary>
    internal static void RegisterActorInventory(ActorState actor, InventoryStore inventoryStore)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(inventoryStore);
        inventoryStore.RegisterInventory(new InventoryState(actor.Actor.Entity));
        inventoryStore.RegisterEquipment(new EquipmentState(actor.Actor.Entity));
        actor.Actor.Add(new InventoryComponent(inventoryStore, actor.Actor.Entity));
        actor.Actor.Add(new EquipmentComponent(inventoryStore, actor.Actor.Entity));
    }

    private static void RestoreStats(Actor actor, DaggerfallStatsSave saved)
    {
        DaggerfallRestoredStats restored = DaggerfallStatsSaveBoundary.Restore(saved, actor.Entity);
        actor.Replace(restored.Component);
        actor.Add(restored);
    }

    internal static ItemDefinition ToManagedItem(DaggerfallItemDefinition item)
    {
        ItemEquipmentPolicy? equipment = item.Equipment is null
            ? null
            : new ItemEquipmentPolicy(
                item.Equipment.RequiredSlots,
                item.Equipment.ExclusiveGroup is { } group && group != "hands" ? EquipmentExclusivityId.Parse(group) : null);
        ulong classicWeight = DaggerfallEncumbrancePolicy.ClassicWeightCost(item);
        IEnumerable<ItemCapacityCost>? capacity = classicWeight > 0
            ? [new ItemCapacityCost(ClassicWeightMetric, classicWeight)]
            : null;
        return new ItemDefinition(
            ItemDefinitionId.Parse(item.Id.Value),
            item.IsFungible ? ItemKind.Fungible : ItemKind.Unique,
            item.MaximumQuantity,
            item.Equipment?.Classifications.Select(ItemClassificationId.Parse),
            capacity,
            equipment);
    }

    /// <summary>Content owns the complete vocabulary of every Daggerfall equipment slot.</summary>
    internal static EquipmentSlotDefinition ToManagedSlot(DaggerfallEquipmentSlotDefinition slot) =>
        new(Rusty.Engine.Mechanics.EquipmentSlotId.Parse(slot.Id.Value), slot.AllowedClassifications.Select(ItemClassificationId.Parse));

    /// <summary>
    /// Rolls one actor's initial health from its authored range under a spawn-identity key.
    /// Restore paths discard this roll when the saved boundary replaces the whole component.
    /// </summary>
    internal static DaggerfallVitalValues InitialVitals(IRandomService random, DaggerfallActorDefinition definition, long entityId)
    {
        if (definition.Id.Value == "player")
            throw new ArgumentException("Player vitals require the selected career and are assembled by the player factory path.", nameof(definition));
        int health = checked((int)random.DrawKeyed(new KeyedRngRequest(CombatRandomKey.Seed, CombatRandomKey.EnemyScope, CombatRandomKey.InitialHealth(entityId, definition.Id.Value), definition.Health.Minimum, definition.Health.Maximum)).Value);
        return new DaggerfallVitalValues(health, 0, 0);
    }

    private static void ValidateInitialEntityIds(PrivateersHoldInputs inputs, IReadOnlyList<DaggerfallLoadoutEntry> loadout) =>
        _ = AdmittedAuthoredEntityIds(inputs, loadout);

    /// <summary>
    /// The authored entity ids admitted for one construction: the player, every placement actor,
    /// and every loadout unique item. This is one global numeric namespace because every entry
    /// here materializes as a runtime <see cref="EntityId"/> — and the unique-item allocator draws
    /// future runtime ids from the same space, so its reservations are this same set. Typed
    /// durable kinds may share numbers elsewhere (a corpse container shares its actor's number
    /// under a different kind), but anything taking a runtime entity joins this check, which is
    /// why cross-kind sharing is rejected here rather than allowed. Actor construction and the
    /// allocator both take this set; neither rebuilds it.
    /// </summary>
    internal static HashSet<ulong> AdmittedAuthoredEntityIds(PrivateersHoldInputs inputs, IReadOnlyList<DaggerfallLoadoutEntry> loadout)
    {
        HashSet<ulong> ids = [PlayerMechanicsEntityId];
        foreach (AuthoredActor actor in inputs.Project.Actors.Values)
        {
            if (actor.EntityId <= 0 || !ids.Add(checked((ulong)actor.EntityId)))
                throw new InvalidOperationException($"Initial Mechanics entity id '{actor.EntityId}' collides with another player, placement, or item entity.");
        }
        foreach (DaggerfallLoadoutEntry item in loadout)
            if (item.UniqueEntityId is ulong entityId && !ids.Add(entityId))
                throw new InvalidOperationException($"Initial Mechanics entity id '{entityId}' collides with another player, placement, or item entity.");
        return ids;
    }

}
