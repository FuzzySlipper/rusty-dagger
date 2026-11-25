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
using WorldRpg.Kit.Ai;
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

/// <summary>
/// The actors, inventories and registries the factory constructs from admitted definitions or a current
/// save's actor sections, before any session service exists.
/// </summary>
internal sealed record DaggerfallActorAssembly(
    PlayerControlState PlayerControl,
    ActorsState Actors,
    InventoryStore InventoryStore,
    MechanicsInventoryCoordinator Inventory,
    MechanicsEquipmentCoordinator Equipment,
    MechanicsInventoryContainerCoordinator Containers,
    DaggerfallActorInventories ActorInventories,
    DaggerfallItemInstances ItemInstances,
    DaggerfallNpcRegistry Npcs,
    DaggerfallActorLedger ActorLedger,
    DaggerfallCharacterState Character,
    Dictionary<long, DaggerfallActorDefinition> Definitions,
    DaggerfallActorDefinition PlayerDefinition,
    DaggerfallMechanicsState Mechanics);

/// <summary>
/// The save's actor sections the factory restores from: the player, the placed actors it keeps and those banished, the
/// spawned actors, the people and the character. A new game has none.
/// </summary>
internal sealed record DaggerfallActorSaves(DaggerfallPlayerSave Player, DaggerfallActorSave[] Actors, long[] BanishedActors,
    DaggerfallDynamicActorSave[] DynamicActors, DaggerfallCharacterSave? Character,
    ActorLedgerEntry[]? Ledger = null, IReadOnlySet<long>? SavedInventoryIds = null);

/// <summary>
/// The atlas gender a spawned enemy took when it entered the world. DaggerfallMobileUnit.GetTextureArchive
/// gives a human mobile with no gender flag a random gender once; a mobile without a female atlas keeps its own.
/// </summary>
internal sealed record DaggerfallActorGender(string Value)
{
    internal static string Resolve(IRandomService random, long durableId, string authored, NormalizedActorSprite sprite) =>
        authored != "unspecified" || sprite.Female is null ? authored
            : random.DrawKeyed(new KeyedRngRequest(DaggerfallRandomKey.Seed, CombatRandomKey.EnemyScope, $"enemy-gender:{durableId}", 0, 1)).Value == 0
                ? "male" : "female";
}

/// <summary>Explicit entity/component construction from admitted Dagger definitions or current saves.</summary>
internal static class DaggerfallActorFactory
{
    private const ulong PlayerMechanicsEntityId = (ulong)DaggerfallActorIdentity.PlayerEntityId;
    /// <summary>Every Daggerfall actor targets and attacks; only the player levels.</summary>
    internal const ActorCapabilities PlayerCapabilities = ActorCapabilities.Targeting | ActorCapabilities.Attacks | ActorCapabilities.Progression;
    internal const ActorCapabilities NonPlayerCapabilities = ActorCapabilities.Targeting | ActorCapabilities.Attacks;
    internal static DaggerfallActorAssembly Create(IRandomService random, DaggerfallDefinitions definitions, DaggerfallSiteProfile inputs, DaggerfallActorSaves? saved,
        bool materializeActiveSite = true, DaggerfallSiteAnchor? newGamePose = null)
    {
        ActorsState actors = new();
        try
        {
            DaggerfallMechanicsState mechanics = new(definitions.EnemySpells, definitions.Vocabulary);
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
                new EntityTypeId(playerDefinition.Id.Value), mechanics.CreateStats(playerDefinition, DaggerfallPlayerVitals.Initial(playerDefinition.Stats, initialCareer)), playerDefinition.Combat.Health.Value,
                PlayerCapabilities);
            player.Actor.Add(new DaggerfallSpellReadiness());
            player.Actor.Add(new CombatContributions());
            player.Actor.Add(new ActorHarmPolicy(ActorHarmPolicyKind.Combatant));
            player.Actor.Add(new ActorInteractionPolicy(ActorInteractionPolicyKind.None));
            player.Actor.Add(new FactionComponent(creatureFactionId: "player-ally"));
            player.Actor.Add(new ActorOverlayComponent { ArchetypeId = "player" });
            player.Actor.Add(new ActorRenderComponent(ActorVisualSpec.NoneSpec));
            EntityId playerEntity = player.Actor.Entity;
            if (saved is not null) RestoreStats(player.Actor, saved.Player.Stats);
            InventoryStore inventoryStore = new();
            // Engine owns one authoritative sum of item capacity costs. Daggerfall's mutable
            // strength maximum remains policy, so the unbounded Engine limit supplies the live
            // used value without inventing a second inventory ledger.
            inventoryStore.RegisterInventory(new InventoryState(playerEntity, [new InventoryCapacityLimit(DaggerfallEncumbrancePolicy.ClassicWeightMetric, ulong.MaxValue)]));
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
                // A starting weapon, shield or suit of armour enters play with the condition its native
                // template and material author. Anything else would put a one-unit wear budget on gear
                // that the first landed physical hit takes to nothing.
                DaggerfallItemDefinition loadoutDefinition = definitions.RequireItem(entry.ItemId);
                int condition = DaggerfallItemFactory.AuthoredMaximumCondition(definitions, loadoutDefinition);
                itemInstances.RegisterUnique(entry.UniqueEntityId.Value,
                    DaggerfallItemInstanceMetadata.Default(loadoutDefinition, DaggerfallItemOwner.Player) with
                    {
                        CurrentCondition = condition,
                        MaximumCondition = condition,
                    });
                if (entry.EquipSlot is DaggerfallEquipmentSlotId slot)
                {
                    equipmentCoordinator.Equip(
                        item,
                        [new KitEquipmentSlotId(slot.Value)]);
                }
            }
            DaggerfallActorLedger actorLedger = new();
            if (saved?.Ledger is { } savedLedgerEntries)
                actorLedger.Restore(savedLedgerEntries);

            Dictionary<long, DaggerfallActorDefinition> authored = [];
            foreach (AuthoredActor source in inputs.Project.Actors.Values)
            {
                if (saved?.BanishedActors.Contains(source.EntityId) == true
                    || actorLedger.IsHomeSlotSuppressed(source.EntityId, inputs.ProfileKey)) continue;
                if (!definitions.Actors.TryGetValue(source.ActorId, out DaggerfallActorDefinition? definition))
                    throw new InvalidOperationException($"Site placement '{source.EntityId}' refers to missing actor '{source.ActorId.Value}'.");
                authored.Add(source.EntityId, definition);
                if (!materializeActiveSite) continue;
                DaggerfallActorSave? prior = saved?.Actors.SingleOrDefault(value => value.EntityId == source.EntityId);
                ActorLedgerEntry? sparseEntry = actorLedger.TryGet(source.EntityId, out ActorLedgerEntry? foundEntry) ? foundEntry : null;
                string? transformedDef = prior?.WabbajackDefinition ?? sparseEntry?.Combat?.WabbajackDefinition;
                if (transformedDef is { } transformed) definition = DaggerfallWabbajack.RequireDefinition(definitions, transformed);
                authored[source.EntityId] = definition;
                ActorState actor = CreateNonPlayerActor(actors, source.EntityId, definition,
                    mechanics.CreateStats(definition, InitialVitals(random, definition, source.EntityId)),
                    new ActorPose(source.Position, 0f));
                DaggerfallStatsSave baselineStats = DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity);
                actor.Actor.Add(new ActorOriginComponent(
                    ActorOrigin.FixedPlacement(inputs.ProfileKey, source.EntityId),
                    source.Position, 0f, baselineStats));
                if (prior is not null) RestoreStats(actor.Actor, prior.Stats);
                else if (sparseEntry?.Vitals is { } sparseVitals) RestoreStats(actor.Actor, sparseVitals.Stats);
                DaggerfallWabbajack.Restore(actor.Actor, transformedDef);
                actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Passive = source.Passive;
                actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().SpawnDistance = source.SpawnDistance;
                actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().SetForcedHostile(prior?.ForcedHostile ?? sparseEntry?.Combat?.ForcedHostile ?? false);
                actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().LanguagePacified = prior?.LanguagePacified ?? sparseEntry?.Combat?.LanguagePacified ?? false;
                actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().MagicallyPacified = prior?.MagicallyPacified ?? sparseEntry?.Combat?.MagicallyPacified ?? false;
                if (sparseEntry?.Combat?.TeamOverride is { } teamOverride)
                    actor.Actor.Get<FactionComponent>().SetTeamOverride(teamOverride);
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
                    bool hasSavedInventory = saved is not null && (saved.SavedInventoryIds is null || saved.SavedInventoryIds.Contains(source.EntityId));
                    if (!hasSavedInventory)
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
            DaggerfallNpcRegistry npcs = new(actors, actorLedger);

            DaggerfallCharacterState character = new(definitions, player.Stats, playerDefinition, saved?.Character);
            authored.Add(DaggerfallActorIdentity.PlayerEntityId, playerDefinition);
            if (saved is not null) MaterializeDynamicActors(random, actors, mechanics, definitions, saved.DynamicActors, authored, inventoryStore, npcs);
            // A restored player stands where the save left them from construction on: the active profile may
            // have no start of its own (a location without start markers), and the camera is built from this pose.
            // A new game stands where the new-game definition places it, else at the start site's start.
            PlayerControlState control = saved?.Player is { } pose
                ? new(new WorldPoint(pose.X, pose.Y, pose.Z), pose.YawRadians, pose.PitchRadians)
                : newGamePose is { } start
                    ? new(start.Position, start.YawRadians, start.PitchRadians)
                    : new(inputs.Project.PlayerPosition, inputs.InitialLook.YawRadians, inputs.InitialLook.PitchRadians);
            return new(
                control,
                actors, inventoryStore, inventory, equipmentCoordinator, containers,
                new DaggerfallActorInventories(actors, itemDefinitions, equipmentSlots),
                itemInstances, npcs, actorLedger, character,
                authored, playerDefinition, mechanics);
        }
        catch { actors.Dispose(); throw; }
    }

    /// <summary>Materializes one selected site's authored placement into the existing canonical actor store.</summary>
    internal static ActorState CreateAuthoredActor(IRandomService random, DaggerfallMechanicsState mechanics,
        DaggerfallDefinitions definitions, ActorsState actors, InventoryStore inventoryStore,
        IReadOnlyDictionary<InventoryItemId, ItemDefinition> items, DaggerfallItemInstances instances,
        AuthoredActor source, DaggerfallActorSave? restored = null, DaggerfallWorldProfileKey? profileKey = null)
    {
        DaggerfallActorDefinition definition = restored?.WabbajackDefinition is { } transformed
            ? DaggerfallWabbajack.RequireDefinition(definitions, transformed) : definitions.RequireActor(source.ActorId);
        ActorState actor = CreateNonPlayerActor(actors, source.EntityId, definition,
            mechanics.CreateStats(definition, InitialVitals(random, definition, source.EntityId)),
            new ActorPose(source.Position, 0F));
        DaggerfallStatsSave baselineStats = DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity);
        if (profileKey is { } pk)
            actor.Actor.Add(new ActorOriginComponent(ActorOrigin.FixedPlacement(pk, source.EntityId), source.Position, 0F, baselineStats));
        if (restored is not null) RestoreStats(actor.Actor, restored.Stats);
        DaggerfallWabbajack.Restore(actor.Actor, restored?.WabbajackDefinition);
        actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Passive = source.Passive;
        actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().SpawnDistance = source.SpawnDistance;
        actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().SetForcedHostile(restored?.ForcedHostile ?? false);
        actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().LanguagePacified = restored?.LanguagePacified ?? false;
        actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().MagicallyPacified = restored?.MagicallyPacified ?? false;
        RegisterActorInventory(actor, inventoryStore);
        if (restored is null)
        {
            MechanicsInventoryCoordinator inventory = new(actor.Inventory, actors.Entities, items);
            foreach ((DaggerfallLoadoutEntry entry, int ordinal) in definition.Loadout.Where(entry => definitions.RequireItem(entry.ItemId).IsFungible).Select((entry, ordinal) => (entry, ordinal)))
            {
                InventoryStackId stack = DaggerfallInventoryStackIds.ForInitialLoadout(source.EntityId, ordinal);
                inventory.Grant(new InventoryGrant(new InventoryItemId(entry.ItemId.Value), stack, entry.Quantity));
                instances.RegisterDefaultStack(DaggerfallItemOwner.Actor(source.EntityId), new InventoryStack(stack, ItemDefinitionId.Parse(entry.ItemId.Value), entry.Quantity), definitions.RequireItem(entry.ItemId));
            }
        }
        return actor;
    }

    /// <summary>
    /// Builds the runtime mechanics definition for one NPC from its content-authored <see cref="ArchetypeProfile"/>.
    /// Appearance and social meaning remain on the person component; this definition supplies the archetype's
    /// authored attributes, skills, vital range, armor, level, and baseline fungible loadout.
    /// </summary>
    internal static DaggerfallActorDefinition ArchetypeActorDefinition(ArchetypeProfile profile, bool isStatic = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.ToActorDefinition(isStatic ? DaggerfallActorKinds.StaticNpc : DaggerfallActorKinds.Civilian);
    }

    internal static ArchetypeProfile ResolveNpcArchetype(DaggerfallDefinitions definitions, DaggerfallNpc npc,
        string? explicitArchetypeId = null, string? spawnerId = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(npc);
        string? castId = npc.StableKey.StartsWith("cast:", StringComparison.Ordinal)
            ? npc.StableKey["cast:".Length..]
            : null;
        string defaultId = npc.Kind switch
        {
            DaggerfallNpcKind.Static => DaggerfallArchetypeIds.StaticNpc,
            DaggerfallNpcKind.Questor => DaggerfallArchetypeIds.Questor,
            _ => DaggerfallArchetypeIds.Civilian,
        };
        return definitions.People.ResolveArchetype(defaultId, explicitArchetypeId, spawnerId, castId);
    }

    /// <summary>
    /// Materializes one registry NPC (civilian, static, or questor) as a canonical actor with real stats,
    /// faction, capability, and render components derived from its content-authored <see cref="ArchetypeProfile"/>.
    /// </summary>
    internal static ActorState CreateCivilianActor(DaggerfallMechanicsState mechanics, ActorsState actors,
        InventoryStore inventoryStore, DaggerfallNpc npc, ActorPose pose, DaggerfallDefinitions definitions,
        out DaggerfallActorDefinition definition, string? explicitArchetypeId = null, string? spawnerId = null)
    {
        ArchetypeProfile profile = ResolveNpcArchetype(definitions, npc, explicitArchetypeId, spawnerId);
        return CreateCivilianActor(mechanics, actors, inventoryStore, npc, pose, profile, definitions, out definition);
    }

    /// <summary>Materializes one registry NPC from an explicit <see cref="ArchetypeProfile"/>.</summary>
    internal static ActorState CreateCivilianActor(DaggerfallMechanicsState mechanics, ActorsState actors,
        InventoryStore inventoryStore, DaggerfallNpc npc, ActorPose pose, ArchetypeProfile profile,
        DaggerfallDefinitions? definitions, out DaggerfallActorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(mechanics);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(inventoryStore);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(profile);
        definition = ArchetypeActorDefinition(profile, npc.Kind != DaggerfallNpcKind.Civilian);
        int health = profile.RollDeterministicHealth(npc.DurableId);
        ActorState actor = CreateNonPlayerActor(actors, npc.DurableId, definition,
            mechanics.CreateStats(definition, new DaggerfallVitalValues(health, profile.Fatigue, profile.Magicka)), pose);
        ApplyNpcComponents(actor, npc, profile, definitions);
        RegisterActorInventory(actor, inventoryStore);
        return actor;
    }

    internal static void ApplyNpcComponents(ActorState actor, DaggerfallNpc npc, ArchetypeProfile profile, DaggerfallDefinitions? definitions)
    {
        actor.Actor.Get<ActorHarmPolicy>().Set(profile.DefaultHarm);
        actor.Actor.Get<ActorInteractionPolicy>().Set(profile.DefaultInteraction);
        int? regionalPeopleId = definitions is not null
            ? DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, npc.Site.Region)?.Id
            : null;
        int? primaryFactionId = npc.Appearance.FactionId != 0 ? npc.Appearance.FactionId : regionalPeopleId;
        FactionComponent faction = actor.Actor.Get<FactionComponent>();
        faction.SetPeopleFactions(primaryFactionId, regionalPeopleId);
        faction.SetCreatureFaction(string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase) ? "city-watch" : null);

        ActorOverlayComponent overlay = actor.Actor.Get<ActorOverlayComponent>();
        overlay.ArchetypeId = profile.Id;
        overlay.IsQuestor = npc.Kind == DaggerfallNpcKind.Questor;

        DaggerfallNpc liveSnapshot = npc with { X = null, Y = null, Z = null };
        if (actor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? existingPerson))
            existingPerson.Snapshot = liveSnapshot;
        else
            actor.Actor.Add(new ActorPersonComponent(liveSnapshot));

        ActorVisualSpec visual = npc.Kind == DaggerfallNpcKind.Civilian
            ? ActorVisualSpec.SplatCivilian(npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord, npc.Appearance.Gender)
            : ActorVisualSpec.Billboard(npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord, onBase: true);
        actor.Actor.Get<ActorRenderComponent>().SetVisual(visual);
    }

    /// <summary>Resolves authored encounter definitions and content-backed NPC archetype definitions.</summary>
    private static DaggerfallActorDefinition ResolveDynamicDefinition(DaggerfallDefinitions definitions, DaggerfallDynamicActorSave saved,
        DaggerfallNpc? restoredNpc)
    {
        DaggerfallActorDefinition definition;
        if (saved.Definition is DaggerfallActorKinds.Civilian or DaggerfallActorKinds.StaticNpc)
        {
            ArchetypeProfile archetype = restoredNpc is not null
                ? ResolveNpcArchetype(definitions, restoredNpc, saved.PromotedArchetype)
                : definitions.People.RequireArchetype(
                    !string.IsNullOrWhiteSpace(saved.PromotedArchetype)
                        ? saved.PromotedArchetype
                        : saved.Definition == DaggerfallActorKinds.StaticNpc
                            ? DaggerfallArchetypeIds.StaticNpc
                            : DaggerfallArchetypeIds.Civilian);
            definition = ArchetypeActorDefinition(archetype, saved.Definition == DaggerfallActorKinds.StaticNpc);
        }
        else
        {
            definition = definitions.RequireActor(new DaggerfallActorId(saved.Definition));
        }
        definition = definition with { Level = saved.Level };
        if (saved.PlayerAllied) definition = definition with { Team = "player-ally" };
        return definition.Kind is DaggerfallActorKinds.Civilian or DaggerfallActorKinds.StaticNpc
            ? definition
            : DaggerfallEncounterActors.ApplyEncounterClassPolicy(definition);
    }

    /// <summary>Recreates an inactive site's spawned actor with its durable identity and saved pose.</summary>
    internal static ActorState CreateDynamicActor(IRandomService random, DaggerfallMechanicsState mechanics,
        DaggerfallDefinitions definitions, ActorsState actors, InventoryStore inventoryStore,
        Dictionary<long, DaggerfallActorDefinition> definitionsByActor, DaggerfallDynamicActorSave saved,
        DaggerfallNpcRegistry? npcs = null)
    {
        ArgumentNullException.ThrowIfNull(saved);
        DaggerfallNpc? restoredNpc = npcs is not null && npcs.TryGet(saved.EntityId, out DaggerfallNpc? existingNpc)
            ? existingNpc
            : null;
        // Encounter class actors carry a spawn-time policy adjustment (including the thief
        // action identity and zero flat reward).  The save intentionally stores the canonical
        // definition identity, so reapply that pure policy while rebuilding the runtime binding.
        DaggerfallActorDefinition definition = ResolveDynamicDefinition(definitions, saved, restoredNpc);
        ActorState actor = CreateNonPlayerActor(actors, saved.EntityId, definition,
            mechanics.CreateStats(definition, InitialVitals(random, definition, saved.EntityId)),
            new ActorPose(new WorldPoint(saved.X, saved.Y, saved.Z), saved.HeadingRadians));
        RestoreStats(actor.Actor, saved.Stats);
        DaggerfallWabbajack.Restore(actor.Actor, saved.WabbajackActive ? saved.Definition : null);
        if (saved.CorruptionOrigin is { } origin) actor.Actor.Add(origin);
        if (saved.Gender is { } gender) actor.Actor.Add(new DaggerfallActorGender(gender));
        if (saved.TeamOverride is { } teamOverride) actor.Actor.Get<FactionComponent>().SetTeamOverride(teamOverride);
        else if (saved.PlayerAllied) actor.Actor.Get<FactionComponent>().SetTeamOverride("player-ally");
        if (saved.EncounterBandId is int bandId && saved.EncounterGeneration is ulong bandGen)
            actor.Actor.Add(new DaggerfallEncounterBandMember(bandId, bandGen));
        actor.Actor.Add(new ActorOriginComponent(
            ActorOrigin.MintedActor(saved.Definition, saved.Level, gender: saved.Gender),
            new WorldPoint(saved.X, saved.Y, saved.Z), saved.HeadingRadians));
        if (restoredNpc is not null)
        {
            bool promotedGuard = string.Equals(saved.PromotedArchetype, DaggerfallArchetypeIds.Guard, StringComparison.Ordinal)
                || definition.MobileId == 146;
            ArchetypeProfile npcProfile = ResolveNpcArchetype(
                definitions,
                restoredNpc,
                promotedGuard ? DaggerfallArchetypeIds.Guard : saved.PromotedArchetype);
            ApplyNpcComponents(actor, restoredNpc, npcProfile, definitions);
            if (promotedGuard && definition.MobileId is int watchMobile)
            {
                actor.Actor.Get<FactionComponent>().SetCreatureFaction("city-watch");
                actor.Actor.Get<ActorRenderComponent>().SetVisual(
                    ActorVisualSpec.DirectionalMobile(watchMobile, saved.Gender ?? restoredNpc.Appearance.Gender));
            }
        }
        actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().SetForcedHostile(saved.ForcedHostile);
        actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().LanguagePacified = saved.LanguagePacified;
        actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().MagicallyPacified = saved.MagicallyPacified;
        actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().SpawnDistance = saved.SpawnDistance;
        actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Passive = saved.Passive;
        definitionsByActor.Add(saved.EntityId, definition);
        RegisterActorInventory(actor, inventoryStore);
        return actor;
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
        IEnumerable<DaggerfallDynamicActorSave> saved,
        Dictionary<long, DaggerfallActorDefinition> definitionsByActor,
        InventoryStore inventoryStore,
        DaggerfallNpcRegistry? npcs = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(mechanics);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(definitionsByActor);
        ArgumentNullException.ThrowIfNull(inventoryStore);
        foreach (DaggerfallDynamicActorSave spawned in saved.OrderBy(value => value.EntityId))
            CreateDynamicActor(random, mechanics, definitions, actors, inventoryStore, definitionsByActor, spawned, npcs);
    }

    /// <summary>
    /// Creates one non-player actor with the per-actor state every Daggerfall NPC carries: the Kit
    /// actor components, capability policies, unified faction, Tier-2 render spec, pursuit memory,
    /// and the enemy-senses memory the behavior policy reads.
    /// </summary>
    internal static ActorState CreateNonPlayerActor(ActorsState actors, long durableId, DaggerfallActorDefinition definition,
        StatsComponent stats, ActorPose pose)
    {
        ActorState actor = actors.CreateActor(durableId, new EntityTypeId(definition.Id.Value), stats, pose, definition.Combat.Health.Value, NonPlayerCapabilities);
        actor.Actor.Add(new DaggerfallSpellReadiness());
        actor.Actor.Add(new CombatContributions());
        actor.Actor.Add(new PursuitMemoryComponent());
        actor.Actor.Add(new DaggerfallEnemyPerceptionMemory());

        ActorHarmPolicyKind harmKind = definition.Kind switch
        {
            DaggerfallActorKinds.StaticNpc => ActorHarmPolicyKind.Protected,
            DaggerfallActorKinds.Civilian => ActorHarmPolicyKind.LawProtectedCivilian,
            _ => ActorHarmPolicyKind.Combatant,
        };
        ActorInteractionPolicyKind interactionKind = definition.Kind is DaggerfallActorKinds.StaticNpc or DaggerfallActorKinds.Civilian
            ? ActorInteractionPolicyKind.FullDialogue
            : ActorInteractionPolicyKind.None;
        actor.Actor.Add(new ActorHarmPolicy(harmKind));
        actor.Actor.Add(new ActorInteractionPolicy(interactionKind));
        actor.Actor.Add(new FactionComponent(creatureFactionId: definition.Team ?? (harmKind == ActorHarmPolicyKind.Combatant ? "player-enemy" : null)));
        actor.Actor.Add(new ActorOverlayComponent { ArchetypeId = definition.Kind });
        ActorVisualSpec visualSpec = definition.MobileId is int mobileId
            ? ActorVisualSpec.DirectionalMobile(mobileId)
            : definition.Kind == DaggerfallActorKinds.Civilian
                ? new ActorVisualSpec(ActorVisualKind.SplatCharacter, definition.Id.Value)
                : new ActorVisualSpec(ActorVisualKind.Billboard, definition.Id.Value);
        actor.Actor.Add(new ActorRenderComponent(visualSpec));

        if (definition.Kind == DaggerfallActorKinds.Civilian)
            actor.Actor.Add(new WanderMemoryComponent());
        return actor;
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

    internal static void RestoreStats(Actor actor, DaggerfallStatsSave saved)
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
            ? [new ItemCapacityCost(DaggerfallEncumbrancePolicy.ClassicWeightMetric, classicWeight)]
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
        int health = checked((int)random.DrawKeyed(new KeyedRngRequest(DaggerfallRandomKey.Seed, CombatRandomKey.EnemyScope, CombatRandomKey.InitialHealth(entityId, definition.Id.Value), definition.Health.Minimum, definition.Health.Maximum)).Value);
        return new DaggerfallVitalValues(health, 0, 0);
    }

    private static void ValidateInitialEntityIds(DaggerfallSiteProfile inputs, IReadOnlyList<DaggerfallLoadoutEntry> loadout) =>
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
    internal static HashSet<ulong> AdmittedAuthoredEntityIds(DaggerfallSiteProfile inputs, IReadOnlyList<DaggerfallLoadoutEntry> loadout)
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
