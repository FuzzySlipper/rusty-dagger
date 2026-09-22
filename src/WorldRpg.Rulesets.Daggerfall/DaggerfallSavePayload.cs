using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Daggerfall's complete current state. The Host stores its encoded bytes without interpreting them.</summary>
internal sealed record DaggerfallSavePayload(
    DaggerfallPlayerSave Player,
    DaggerfallActorSave[] Actors,
    DaggerfallDynamicActorSave[] DynamicActors,
    int Experience,
    int Level,
    DaggerfallInventorySave Inventory,
    DaggerfallCorpseSave[] Corpses,
    DurableIdentityState Identities,
    DaggerfallCombatCooldownSave[] CombatCooldowns,
    DaggerfallCalendarSave Calendar,
    DaggerfallSiteSave Site,
    DaggerfallActorInventorySave[] ActorInventories,
    DaggerfallVariablesSave Variables,
    DaggerfallNpcSave Npcs,
    DaggerfallActiveEffectSave[] ActiveEffects,
    DaggerfallSkillProgressionSave SkillUses,
    DaggerfallSocialSave Social,
    DaggerfallCharacterSave? Character = null,
    DaggerfallLevelUpSave? LevelUp = null)
{
    /// <summary>Every current quest instance; an empty collection is meaningful current state.</summary>
    [JsonRequired]
    public DaggerfallQuestInstancesSave Quests { get; init; } = new([]);
    /// <summary>Every selected RDB door's current state, including a partially completed motion.</summary>
    [JsonRequired]
    public DaggerfallDoorSave[] Doors { get; init; } = [];
    /// <summary>The dynamic identity kinds owned by the current Daggerfall ruleset.</summary>
    internal static readonly DurableIdentityKind[] PersistedKinds = [DurableIdentityKind.Actor, DurableIdentityKind.Item];

    internal static RulesetSavePayload Encode(DaggerfallSavePayload value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        return new RulesetSavePayload(
            DaggerfallRuleset.Identity,
            JsonSerializer.SerializeToUtf8Bytes(value, DaggerfallSaveJsonContext.Default.DaggerfallSavePayload));
    }

    internal static DaggerfallSavePayload Read(RulesetSavePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Ruleset != DaggerfallRuleset.Identity)
            throw new ArgumentException("The save payload does not belong to the Daggerfall ruleset.", nameof(payload));
        try
        {
            return (JsonSerializer.Deserialize(payload.Bytes.Span, DaggerfallSaveJsonContext.Default.DaggerfallSavePayload)
                ?? throw new ArgumentException("The Daggerfall save payload is empty.", nameof(payload))).Validate();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The Daggerfall save payload is malformed.", nameof(payload), exception);
        }
    }

    /// <summary>
    /// Verifies every current-state relationship against the selected definitions before session construction.
    /// Missing or incompatible meaning is a rejected load, never a partially restored world.
    /// </summary>
    internal DaggerfallSavePayload ResolveRestore(DaggerfallDefinitions definitions, PrivateersHoldInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(inputs);
        HashSet<DaggerfallRdbDoorId> selectedDoors = [.. inputs.Doors.Select(door => door.Id)];
        HashSet<DaggerfallRdbDoorId> savedDoors = [];
        foreach (DaggerfallDoorSave door in Doors)
        {
            ArgumentNullException.ThrowIfNull(door);
            door.Validate();
            if (!selectedDoors.Contains(door.Id) || !savedDoors.Add(door.Id))
                throw new ArgumentException($"Saved RDB door '{door.Id}' is not a distinct door in the selected world.");
        }
        if (!savedDoors.SetEquals(selectedDoors))
            throw new ArgumentException("Current save must carry one state for every selected RDB door.");
        _ = definitions.RequireActor(new DaggerfallActorId("player"));
        HashSet<long> savedActorIds = [];
        foreach (DaggerfallActorSave actor in Actors)
        {
            if (!inputs.Project.Actors.TryGetValue(actor.EntityId, out AuthoredActor? placement))
                throw new ArgumentException($"Saved actor {actor.EntityId} is not placed by the selected content.");
            if (!definitions.Actors.ContainsKey(placement.ActorId))
                throw new ArgumentException($"Saved actor {actor.EntityId} refers to missing definition '{placement.ActorId.Value}'.");
            if (!savedActorIds.Add(actor.EntityId))
                throw new ArgumentException($"Saved actor {actor.EntityId} appears more than once.");
        }
        foreach (AuthoredActor placement in inputs.Project.Actors.Values)
        {
            if (!savedActorIds.Contains(placement.EntityId))
                throw new ArgumentException($"Current save is missing authored actor {placement.EntityId}.");
        }
        // Dynamic actors are spawn-time registrations, not content placements: each one names
        // the definition it was spawned from, and a definition the selected content cannot
        // explain is reported rather than silently materialized.
        HashSet<long> dynamicActorIds = [];
        foreach (DaggerfallDynamicActorSave actor in DynamicActors)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.EntityId <= 0 || !dynamicActorIds.Add(actor.EntityId))
                throw new ArgumentException("Saved dynamic actor identities must be positive and unique.");
            if (savedActorIds.Contains(actor.EntityId))
                throw new ArgumentException($"Saved dynamic actor {actor.EntityId} collides with an authored actor.");
            if (string.IsNullOrWhiteSpace(actor.Definition) || !definitions.Actors.ContainsKey(new DaggerfallActorId(actor.Definition)))
                throw new ArgumentException($"Saved dynamic actor {actor.EntityId} refers to missing definition '{actor.Definition}'.");
            ValidateStats(actor.Stats, $"dynamic actor {actor.EntityId}");
        }
        // A dynamic actor the ledger does not call live is either tombstoned or forged: restoring
        // it would reissue an identity the save already retired.
        DurableIdentityAllocator savedLedger = DurableIdentityAllocator.Restore(RestoredIdentities());
        foreach (long actorId in dynamicActorIds)
        {
            if (savedLedger.Classify(new DurableIdentityReference(DurableIdentityKind.Actor, checked((ulong)actorId))) != DurableIdentityClassification.Live)
                throw new ArgumentException($"Saved dynamic actor {actorId} is not live in the persisted identity ledger.");
        }

        HashSet<ulong> uniqueItems = [];
        ValidateInventory(Inventory, definitions, uniqueItems, DaggerfallItemOwner.Player, requireEquipment: true);
        foreach (DaggerfallCorpseSave corpse in Corpses)
        {
            if (!savedActorIds.Contains(corpse.ActorId) && !dynamicActorIds.Contains(corpse.ActorId))
                throw new ArgumentException($"Saved corpse refers to missing actor {corpse.ActorId}.");
            corpse.Validate();
            ValidateInventory(new DaggerfallInventorySave(corpse.Stacks, corpse.UniqueItems, []), definitions, uniqueItems, DaggerfallItemOwner.Corpse(corpse.ActorId), requireEquipment: false);
        }
        HashSet<long> actorInventories = [];
        foreach (DaggerfallActorInventorySave inventory in ActorInventories)
        {
            inventory.Validate();
            if ((!savedActorIds.Contains(inventory.EntityId) && !dynamicActorIds.Contains(inventory.EntityId)) || !actorInventories.Add(inventory.EntityId))
                throw new ArgumentException($"Saved actor inventory {inventory.EntityId} has no distinct saved actor.");
            ValidateInventory(inventory.Inventory, definitions, uniqueItems, DaggerfallItemOwner.Actor(inventory.EntityId), requireEquipment: true);
        }
        HashSet<long> allActors = [.. savedActorIds, .. dynamicActorIds];
        if (!actorInventories.SetEquals(allActors))
            throw new ArgumentException("Current save must carry one actor inventory section for every saved actor.");
        RequireLiveUniqueItems(uniqueItems);
        Quests.Validate(definitions);

        HashSet<(int Region, int Index)> locations = [.. definitions.Locations.Records.Select(value => (value.Region, value.Index))];
        RequireSite(Site.Active, locations, "active site");
        RequireSite(Site.ReturnAnchor, locations, "return anchor");
        foreach (DaggerfallSiteIdSave discovered in Site.Discovered)
            RequireSite(discovered, locations, "discovered site");
        HashSet<long> combatants = [DaggerfallActorIdentity.PlayerEntityId, .. allActors];
        foreach (DaggerfallCombatCooldownSave cooldown in CombatCooldowns)
            if (!combatants.Contains(cooldown.AttackerId))
                throw new ArgumentException($"Saved attack cooldown refers to missing actor {cooldown.AttackerId}.");
        HashSet<(string Scope, long OwnerId, string StackId)> questStacks = [];
        AddQuestStacks(questStacks, DaggerfallItemOwner.Player, Inventory.Stacks);
        foreach (DaggerfallCorpseSave corpse in Corpses)
            AddQuestStacks(questStacks, DaggerfallItemOwner.Corpse(corpse.ActorId), corpse.Stacks);
        foreach (DaggerfallActorInventorySave inventory in ActorInventories)
            AddQuestStacks(questStacks, DaggerfallItemOwner.Actor(inventory.EntityId), inventory.Inventory.Stacks);
        Quests.ValidateBindings(combatants, savedLedger, locations, questStacks);
        ValidateActiveEffects(ActiveEffects, combatants, uniqueItems);
        Social.Validate(definitions.Factions);
        Character?.Validate(definitions);
        ValidateEffectSourceReferences(
        [
            (DaggerfallActorIdentity.PlayerEntityId, Player.Stats),
            .. Actors.Select(actor => (actor.EntityId, actor.Stats)),
            .. DynamicActors.Select(actor => (actor.EntityId, actor.Stats)),
        ],
        ActiveEffects);
        return this;
    }

    internal DurableIdentityState RestoredIdentities() => Identities.Validate().RequireKinds(PersistedKinds);

    internal DaggerfallSavePayload Validate()
    {
        ArgumentNullException.ThrowIfNull(Player);
        ArgumentNullException.ThrowIfNull(Actors);
        ArgumentNullException.ThrowIfNull(DynamicActors);
        ArgumentNullException.ThrowIfNull(Inventory);
        ArgumentNullException.ThrowIfNull(Corpses);
        ArgumentNullException.ThrowIfNull(Identities);
        ArgumentNullException.ThrowIfNull(CombatCooldowns);
        ArgumentNullException.ThrowIfNull(Calendar);
        ArgumentNullException.ThrowIfNull(Site);
        ArgumentNullException.ThrowIfNull(ActorInventories);
        ArgumentNullException.ThrowIfNull(Variables);
        Variables.Validate();
        ArgumentNullException.ThrowIfNull(Npcs);
        Npcs.Validate();
        ArgumentNullException.ThrowIfNull(ActiveEffects);
        ArgumentNullException.ThrowIfNull(SkillUses);
        SkillUses.Validate();
        ArgumentNullException.ThrowIfNull(Social);
        Social.Validate();
        ArgumentNullException.ThrowIfNull(Quests);
        Quests.Validate();
        ArgumentNullException.ThrowIfNull(Doors);
        foreach (DaggerfallDoorSave door in Doors) { ArgumentNullException.ThrowIfNull(door); door.Validate(); }
        ArgumentNullException.ThrowIfNull(Character);
        LevelUp?.Validate();
        if (LevelUp is not null && LevelUp.Level != Level + 1)
            throw new ArgumentException("Saved Daggerfall level-up must target exactly the next progression level.", nameof(LevelUp));
        if (Experience < 0 || Level < 1)
            throw new ArgumentOutOfRangeException(nameof(Experience), "Saved progression must be non-negative and begin at level one.");
        if (!double.IsFinite(Calendar.RemainderSeconds) || Calendar.RemainderSeconds < 0d || Calendar.RemainderSeconds >= 1d)
            throw new ArgumentException("Saved calendar remainder must be a fraction of one second.");
        Player.Validate();
        ValidateStats(Player.Stats, "player");
        HashSet<long> actorIds = [];
        foreach (DaggerfallActorSave actor in Actors)
        {
            ArgumentNullException.ThrowIfNull(actor);
            actor.Validate();
            if (actor.EntityId <= 0 || !actorIds.Add(actor.EntityId))
                throw new ArgumentException("Saved actor identities must be positive and unique.");
            ValidateStats(actor.Stats, $"actor {actor.EntityId}");
        }
        foreach (DaggerfallDynamicActorSave actor in DynamicActors)
        {
            ArgumentNullException.ThrowIfNull(actor);
            actor.Validate();
            if (actor.EntityId <= 0 || !actorIds.Add(actor.EntityId))
                throw new ArgumentException("Saved dynamic actor identities must be positive and unique.");
            ValidateStats(actor.Stats, $"dynamic actor {actor.EntityId}");
        }
        Inventory.Validate();
        HashSet<long> corpseActors = [];
        foreach (DaggerfallCorpseSave corpse in Corpses)
        {
            ArgumentNullException.ThrowIfNull(corpse);
            if (corpse.ActorId <= 0 || !corpseActors.Add(corpse.ActorId))
                throw new ArgumentException("Saved corpse actor identities must be positive and unique.");
            corpse.Validate();
        }
        HashSet<long> inventoryActors = [];
        foreach (DaggerfallActorInventorySave inventory in ActorInventories)
        {
            ArgumentNullException.ThrowIfNull(inventory);
            if (inventory.EntityId <= 0 || !inventoryActors.Add(inventory.EntityId))
                throw new ArgumentException("Saved actor inventories must name distinct positive actors.");
            inventory.Validate();
        }
        HashSet<long> cooldownActors = [];
        foreach (DaggerfallCombatCooldownSave cooldown in CombatCooldowns)
        {
            ArgumentNullException.ThrowIfNull(cooldown);
            if (cooldown.AttackerId <= 0 || cooldown.RemainingSteps == 0 || !cooldownActors.Add(cooldown.AttackerId))
                throw new ArgumentException("Saved attack cooldowns must name distinct actors and positive remaining steps.");
        }
        foreach (DaggerfallActiveEffectSave effect in ActiveEffects)
        {
            ArgumentNullException.ThrowIfNull(effect);
            effect.Validate();
        }
        Identities.Validate().RequireKinds(PersistedKinds);
        Site.Validate();
        return this;
    }

    private static void ValidateStats(DaggerfallStatsSave stats, string owner)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(stats.Snapshot);
        ArgumentNullException.ThrowIfNull(stats.Sources);
        _ = DaggerfallStatsSaveBoundary.Restore(stats, default);
    }

    private static void RequireSite(DaggerfallSiteIdSave? id, HashSet<(int Region, int Index)> locations, string owner)
    {
        if (id is null) return;
        DaggerfallSiteId site = id.Require();
        if (!locations.Contains((site.Region, site.Index)))
            throw new ArgumentException($"Saved {owner} {site} is not defined by the selected content.");
    }

    /// <summary>Every materialized unique item must already be live in the persisted item ledger.</summary>
    private void RequireLiveUniqueItems(IEnumerable<ulong> uniqueItems)
    {
        DurableIdentityAllocator identities = DurableIdentityAllocator.Restore(RestoredIdentities());
        foreach (ulong itemId in uniqueItems)
        {
            DurableIdentityClassification classification = identities.Classify(new DurableIdentityReference(DurableIdentityKind.Item, itemId));
            if (classification != DurableIdentityClassification.Live)
                throw new ArgumentException($"Saved unique item '{itemId}' is {classification} in the persisted item identity ledger.");
        }
    }

    private static void ValidateInventory(DaggerfallInventorySave inventory, DaggerfallDefinitions definitions, HashSet<ulong> allUnique, DaggerfallItemOwner owner, bool requireEquipment)
    {
        inventory.Validate();
        foreach (DaggerfallStackSave stack in inventory.Stacks) RequireFungible(definitions, stack, owner);
        Dictionary<ulong, DaggerfallItemDefinition> unique = [];
        foreach (DaggerfallUniqueSave saved in inventory.UniqueItems)
        {
            if (!definitions.TryResolveItem(new DaggerfallItemId(saved.ItemId), out DaggerfallItemDefinition definition) || definition.IsFungible || !allUnique.Add(saved.EntityId))
                throw new ArgumentException($"Saved {owner.Scope} {owner.Id} unique item '{saved.EntityId}' is missing, incompatible, or duplicated.");
            RequireMetadata(saved.ItemId, saved.Metadata, owner);
            unique.Add(saved.EntityId, definition);
        }
        if (!requireEquipment && inventory.Equipment.Length != 0)
            throw new ArgumentException($"Saved {owner.Scope} {owner.Id} cannot equip items.");
        foreach (IGrouping<ulong, DaggerfallEquipmentSave> group in inventory.Equipment.GroupBy(value => value.ItemEntityId))
        {
            if (!unique.TryGetValue(group.Key, out DaggerfallItemDefinition? item) || item.Equipment is null)
                throw new ArgumentException($"Saved {owner} equipment refers to unknown unique item {group.Key}.");
            foreach (DaggerfallEquipmentSave equipped in group)
            {
                if (!definitions.EquipmentSlots.TryGetValue(new DaggerfallEquipmentSlotId(equipped.SlotId), out DaggerfallEquipmentSlotDefinition? slot)
                    || !item.Equipment.Classifications.Any(classification => slot.AllowedClassifications.Contains(classification)))
                    throw new ArgumentException($"Saved equipment slot '{equipped.SlotId}' is incompatible with unique item {group.Key}.");
            }
        }
    }

    private static void RequireFungible(DaggerfallDefinitions definitions, DaggerfallStackSave stack, DaggerfallItemOwner owner)
    {
        if (!definitions.TryResolveItem(new DaggerfallItemId(stack.ItemId), out DaggerfallItemDefinition definition) || !definition.IsFungible)
            throw new ArgumentException($"Saved {owner.Scope} {owner.Id} stack '{stack.ItemId}' is not a selected fungible item.");
        if (stack.Quantity > definition.MaximumQuantity)
            throw new ArgumentException($"Saved {owner.Scope} {owner.Id} stack '{stack.ItemId}' exceeds its authored maximum quantity.");
        RequireMetadata(stack.ItemId, stack.Metadata, owner);
    }

    private static void AddQuestStacks(HashSet<(string Scope, long OwnerId, string StackId)> target, DaggerfallItemOwner owner, IEnumerable<DaggerfallStackSave> stacks)
    {
        foreach (DaggerfallStackSave stack in stacks)
            if (!target.Add((owner.Scope, owner.Id, stack.StackId)))
                throw new ArgumentException($"Saved stack '{stack.StackId}' appears more than once for {owner.Scope} {owner.Id}.");
    }

    private static void RequireMetadata(string itemId, DaggerfallItemMetadataSave metadata, DaggerfallItemOwner owner)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        DaggerfallItemInstanceMetadata restored = DaggerfallItemInstanceMetadata.Restore(itemId, metadata);
        if (restored.Owner != owner)
            throw new ArgumentException($"Saved item '{itemId}' has metadata ownership {restored.Owner.Scope} {restored.Owner.Id}, not {owner.Scope} {owner.Id}.");
    }

    /// <summary>Every effect-backed stat source must have the active instance that owns its eventual cleanup.</summary>
    private static void ValidateEffectSourceReferences(
        IEnumerable<(long ActorId, DaggerfallStatsSave Stats)> actors,
        IEnumerable<DaggerfallActiveEffectSave> activeEffects)
    {
        HashSet<(string Instance, long Target)> active = activeEffects
            .Select(effect => (effect.Instance, effect.TargetId))
            .ToHashSet();
        foreach ((long actorId, DaggerfallStatsSave stats) in actors)
        foreach (DaggerfallStatSourceSave source in stats.Sources)
        {
            if (source.Identity.Kind != DaggerfallStatSourceIdentityKind.Effect) continue;
            if (!active.Contains((source.Identity.InstanceId, actorId)))
                throw new ArgumentException($"Saved effect source '{source.Identity.InstanceId}' on actor {actorId} has no matching active effect cleanup owner.");
        }
    }

    private static void ValidateActiveEffects(IEnumerable<DaggerfallActiveEffectSave> effects, ISet<long> actors, ISet<ulong> uniqueItems)
    {
        HashSet<string> instances = new(StringComparer.Ordinal);
        foreach (DaggerfallActiveEffectSave effect in effects)
        {
            effect.Validate();
            if (!instances.Add(effect.Instance)) throw new ArgumentException($"Saved effect instance '{effect.Instance}' appears more than once.");
            if (!actors.Contains(effect.TargetId)) throw new ArgumentException($"Saved effect instance '{effect.Instance}' targets missing actor {effect.TargetId}.");
            if (effect.CasterId is long caster && !actors.Contains(caster)) throw new ArgumentException($"Saved effect instance '{effect.Instance}' names missing caster {caster}.");
            if (effect.ItemId is ulong item && !uniqueItems.Contains(item)) throw new ArgumentException($"Saved effect instance '{effect.Instance}' names missing item {item}.");
        }
    }
}

/// <summary>One placed actor's full Engine inventory meaning, including unique instances and equipment.</summary>
internal sealed record DaggerfallActorInventorySave(long EntityId, DaggerfallInventorySave Inventory)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Inventory);
        Inventory.Validate();
    }
}

internal sealed record DaggerfallInventorySave(DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems, DaggerfallEquipmentSave[] Equipment)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Stacks);
        ArgumentNullException.ThrowIfNull(UniqueItems);
        ArgumentNullException.ThrowIfNull(Equipment);
        HashSet<string> stackIds = [];
        foreach (DaggerfallStackSave stack in Stacks)
            if (stack is null || string.IsNullOrWhiteSpace(stack.StackId) || string.IsNullOrWhiteSpace(stack.ItemId) || stack.Quantity == 0 || !stackIds.Add(stack.StackId))
                throw new ArgumentException("Inventory stacks must have distinct explicit identities, definitions, and positive quantities.");
            else _ = DaggerfallItemInstanceMetadata.Restore(stack.ItemId, stack.Metadata);
        HashSet<ulong> items = [];
        foreach (DaggerfallUniqueSave unique in UniqueItems)
            if (unique is null || string.IsNullOrWhiteSpace(unique.ItemId) || unique.EntityId == 0 || !items.Add(unique.EntityId))
                throw new ArgumentException("Unique inventory entries must be valid and distinct.");
            else _ = DaggerfallItemInstanceMetadata.Restore(unique.ItemId, unique.Metadata);
        HashSet<string> slots = [];
        foreach (DaggerfallEquipmentSave equipment in Equipment)
            if (equipment is null || string.IsNullOrWhiteSpace(equipment.SlotId) || !slots.Add(equipment.SlotId) || !items.Contains(equipment.ItemEntityId))
                throw new ArgumentException("Equipment must refer to a saved unique item once per slot.");
    }
}

/// <summary>One Engine-backed stack preserved by its product-selected owner-scoped identity.</summary>
internal sealed record DaggerfallStackSave(string StackId, string ItemId, ulong Quantity, DaggerfallItemMetadataSave Metadata);
internal sealed record DaggerfallUniqueSave(string ItemId, ulong EntityId, DaggerfallItemMetadataSave Metadata);
internal sealed record DaggerfallItemOwnerSave(string Scope, long Id);
internal sealed record DaggerfallItemMetadataSave(
    string Material,
    int Variant,
    int CurrentCondition,
    int MaximumCondition,
    bool Identified,
    bool Stolen,
    string? QuestId,
    string? QuestItemSymbol,
    string? Enchantment,
    DaggerfallItemOwnerSave Owner,
    string? Race = null,
    string? Gender = null,
    string? Dye = null,
    int? BookId = null,
    int? PotionRecipeKey = null);
internal sealed record DaggerfallEquipmentSave(string SlotId, ulong ItemEntityId);
internal sealed record DaggerfallCombatCooldownSave(long AttackerId, ulong RemainingSteps);

/// <summary>Stable current-state data for one Daggerfall active effect; compiled policy rebuilds its behavior.</summary>
internal sealed record DaggerfallActiveEffectSave(
    string Instance,
    string EffectKey,
    string Source,
    long? CasterId,
    long TargetId,
    string Settings,
    string? Element,
    ulong? ItemId,
    uint? RemainingRounds,
    ushort Stacks,
    JsonElement State)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(EffectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(Settings);
        if (TargetId <= 0 || CasterId is <= 0 || ItemId == 0 || Stacks == 0)
            throw new ArgumentOutOfRangeException(nameof(TargetId), "Saved effect identities and stacks must be positive.");
        if (State.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("Saved effect state must be explicit.", nameof(State));
    }

    internal DaggerfallEffectRequest ToRequest() => new(
        Instance, EffectKey, Source, CasterId, TargetId, Settings, Element, ItemId, Stacks, RemainingRounds, State.Clone());
}

internal sealed record DaggerfallCorpseSave(long ActorId, ulong OriginatingSequence, bool IsRegistered, bool IsInteractable, DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Stacks);
        ArgumentNullException.ThrowIfNull(UniqueItems);
        if (!IsRegistered && (Stacks.Length != 0 || UniqueItems.Length != 0))
            throw new ArgumentException("An unregistered corpse cannot contain inventory.");
        new DaggerfallInventorySave(Stacks, UniqueItems, []).Validate();
    }
}

internal sealed record DaggerfallCalendarSave(int Year, int Month, int Day, int Hour, int Minute, int Second, double RemainderSeconds);

internal sealed record DaggerfallSiteIdSave(int? Region, int? Index)
{
    internal void Validate(string owner)
    {
        if (Region is not int region || Index is not int index || region < 0 || index < 0)
            throw new ArgumentException($"A saved {owner} must name a non-negative region and index.");
    }

    internal DaggerfallSiteId Require()
    {
        Validate("site");
        return new DaggerfallSiteId(Region!.Value, Index!.Value);
    }
}

internal sealed record DaggerfallSiteSave(DaggerfallSiteIdSave? Active, DaggerfallSiteIdSave? ReturnAnchor, DaggerfallSiteIdSave[] Discovered)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Discovered);
        Active?.Validate("active site");
        ReturnAnchor?.Validate("return anchor");
        if (ReturnAnchor is not null && Active is null)
            throw new ArgumentException("A saved return anchor requires an active site.");
        HashSet<(int Region, int Index)> seen = [];
        foreach (DaggerfallSiteIdSave discovered in Discovered)
        {
            ArgumentNullException.ThrowIfNull(discovered);
            discovered.Validate("discovered site");
            if (!seen.Add((discovered.Region!.Value, discovered.Index!.Value)))
                throw new ArgumentException("A save must record each discovered site once.");
        }
    }
}

/// <summary>One persisted variable: its scope, owner, key and value.</summary>
internal sealed record DaggerfallVariableSave(int Scope, int Owner, int Key, bool Value)
{
    internal DaggerfallVariableAddress Require()
    {
        DaggerfallVariableScope scope = Scope switch
        {
            0 => DaggerfallVariableScope.Global,
            1 => DaggerfallVariableScope.Region,
            2 => DaggerfallVariableScope.Faction,
            _ => throw new InvalidOperationException($"Saved variable names scope {Scope}, which the contract does not declare."),
        };
        return new DaggerfallVariableAddress(scope, Owner, Key);
    }
}


/// <summary>One mutable reputation entry for an admitted faction.</summary>
internal sealed record DaggerfallFactionReputationSave(int FactionId, int Value);

/// <summary>One mutable regional legal-reputation entry.</summary>
internal sealed record DaggerfallRegionalReputationSave(int Region, int Value);

/// <summary>One player social-group reputation entry.</summary>
internal sealed record DaggerfallPersonalReputationSave(int SocialGroup, int Value);

/// <summary>One guild-group membership and the compact counters the donor persists with it.</summary>
internal sealed record DaggerfallGuildMembershipSave(int GuildGroup, int FactionId, int Rank, int LastRankChangeDay, int NotedByGuild);

/// <summary>Meaningful social state, independently persisted from loaded NPC entities.</summary>
internal sealed record DaggerfallSocialSave(
    DaggerfallFactionReputationSave[] Factions,
    DaggerfallRegionalReputationSave[] Regions,
    DaggerfallPersonalReputationSave[] Personal,
    DaggerfallGuildMembershipSave[] Memberships)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Factions);
        ArgumentNullException.ThrowIfNull(Regions);
        ArgumentNullException.ThrowIfNull(Personal);
        ArgumentNullException.ThrowIfNull(Memberships);
        HashSet<int> factions = [];
        foreach (DaggerfallFactionReputationSave entry in Factions)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.FactionId < 0 || !factions.Add(entry.FactionId) || entry.Value is < DaggerfallSocialState.MinimumReputation or > DaggerfallSocialState.MaximumReputation)
                throw new ArgumentException("Saved faction reputation entries must name distinct non-negative factions within the social bounds.");
        }
        HashSet<int> regions = [];
        foreach (DaggerfallRegionalReputationSave entry in Regions)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.Region is < 0 or > 61 || !regions.Add(entry.Region) || entry.Value is < DaggerfallSocialState.MinimumReputation or > DaggerfallSocialState.MaximumReputation)
                throw new ArgumentException("Saved regional reputation entries must name distinct classic regions within the social bounds.");
        }
        HashSet<int> groups = [];
        foreach (DaggerfallPersonalReputationSave entry in Personal)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.SocialGroup is < 0 or >= DaggerfallSocialState.SocialGroupCount || !groups.Add(entry.SocialGroup) || entry.Value is < DaggerfallSocialState.MinimumPersonalReputation or > DaggerfallSocialState.MaximumPersonalReputation)
                throw new ArgumentException("Saved personal reputation entries must name distinct donor social groups within the signed-short source bounds.");
        }
        HashSet<int> guilds = [];
        foreach (DaggerfallGuildMembershipSave entry in Memberships)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.GuildGroup <= 0 || entry.FactionId < 0 || !guilds.Add(entry.GuildGroup)
                || entry.Rank is < 0 or > DaggerfallSocialState.MaximumGuildRank
                || entry.NotedByGuild is < 0 or > byte.MaxValue)
                throw new ArgumentException("Saved guild memberships must name distinct guild groups with valid faction, rank, day, and recognition values.");
        }
    }

    internal void Validate(DaggerfallFactionsSet catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Validate();
        HashSet<int> savedFactionIds = [.. Factions.Select(entry => entry.FactionId)];
        if (!savedFactionIds.SetEquals(catalog.Factions.Keys))
            throw new ArgumentException("Current social state must carry one faction reputation entry for every admitted faction.");
        foreach (DaggerfallRegionalReputationSave entry in Regions)
            if (!catalog.Regions.ContainsKey(entry.Region))
                throw new ArgumentException($"Saved regional reputation names absent region {entry.Region}.");
        foreach (DaggerfallGuildMembershipSave entry in Memberships)
        {
            if (!catalog.Factions.TryGetValue(entry.FactionId, out DaggerfallFactionDefinition? faction) || faction.GuildGroup != entry.GuildGroup)
                throw new ArgumentException($"Saved guild membership group {entry.GuildGroup} does not match admitted faction {entry.FactionId}.");
        }
    }
}

/// <summary>One persisted NPC: its identity, kind, site, appearance, role and presence.</summary>
internal sealed record DaggerfallNpcEntry(
    long DurableId,
    int Kind,
    string StableKey,
    int Region,
    string Location,
    string Building,
    string Race,
    string Gender,
    int BillboardArchive,
    int BillboardRecord,
    ushort NameSeed,
    int FactionId,
    string Role,
    string[] Services,
    int Presence,
    int? X,
    int? Y,
    int? Z);

/// <summary>The session's NPCs in durable order.</summary>
internal sealed record DaggerfallNpcSave(DaggerfallNpcEntry[] Entries)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Entries);
        foreach (DaggerfallNpcEntry entry in Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!Enum.IsDefined((DaggerfallNpcKind)entry.Kind) || !Enum.IsDefined((DaggerfallNpcPresence)entry.Presence))
            {
                throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, "A saved NPC names a kind or presence the contract does not declare.");
            }

            ArgumentNullException.ThrowIfNull(entry.Services);
        }
    }
}

/// <summary>The session's written variables in a stable order.</summary>
internal sealed record DaggerfallVariablesSave(DaggerfallVariableSave[] Entries)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Entries);
        foreach (DaggerfallVariableSave entry in Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            entry.Require();
        }
    }
}

internal sealed record DaggerfallPlayerSave(float X, float Y, float Z, float YawRadians, float PitchRadians, DaggerfallStatsSave Stats)
{
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z) || !float.IsFinite(YawRadians) || !float.IsFinite(PitchRadians))
            throw new ArgumentOutOfRangeException(nameof(X), "Saved player pose must be finite.");
        ArgumentNullException.ThrowIfNull(Stats);
    }
}

internal sealed record DaggerfallActorSave(long EntityId, float X, float Y, float Z, float HeadingRadians, DaggerfallStatsSave Stats)
{
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z) || !float.IsFinite(HeadingRadians))
            throw new ArgumentOutOfRangeException(nameof(X), "Saved actor pose must be finite.");
        ArgumentNullException.ThrowIfNull(Stats);
    }
}

/// <summary>
/// One dynamically spawned actor: the definition it was registered from plus its pose and full
/// Engine stat meaning. Authored placement actors need no definition reference because the
/// selected content already names theirs.
/// </summary>
internal sealed record DaggerfallDynamicActorSave(long EntityId, string Definition, float X, float Y, float Z, float HeadingRadians, DaggerfallStatsSave Stats)
{
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z) || !float.IsFinite(HeadingRadians))
            throw new ArgumentOutOfRangeException(nameof(X), "Saved dynamic actor pose must be finite.");
        if (string.IsNullOrWhiteSpace(Definition))
            throw new ArgumentException("A saved dynamic actor must name its definition.", nameof(Definition));
        ArgumentNullException.ThrowIfNull(Stats);
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(DaggerfallSavePayload))]
[JsonSerializable(typeof(DaggerfallStatsSave))]
[JsonSerializable(typeof(DaggerfallCharacterSave))]
[JsonSerializable(typeof(DurableIdentityState))]
[JsonSerializable(typeof(KindAllocatorState))]
internal partial class DaggerfallSaveJsonContext : JsonSerializerContext;
