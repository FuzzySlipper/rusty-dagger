using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Daggerfall-owned durable meaning. The Host treats these bytes as opaque.</summary>
internal sealed record DaggerfallSavePayload(
    uint SchemaVersion,
    DaggerfallPlayerSave Player,
    DaggerfallActorSave[] Actors,
    int Experience,
    int Level,
    DaggerfallInventorySave Inventory,
    DaggerfallCorpseSave[] Corpses,
    DurableIdentityState Identities,
    DaggerfallCombatCooldownSave[] CombatCooldowns,
    DaggerfallContinuationSave? Continuation,
    DaggerfallOwnerSave[] Owners)
{
    internal const uint CurrentSchemaVersion = 2;

    /// <summary>The schema whose identity fields map onto the current shape without loss.</summary>
    internal const uint MigratableSchemaVersion = 1;

    /// <summary>The one durable identity kind Daggerfall currently allocates dynamically.</summary>
    internal static readonly DurableIdentityKind[] PersistedKinds = [DurableIdentityKind.Item];

    internal ulong NextUniqueItemEntityId => Identities.RequireKinds(PersistedKinds).Kinds
        .Single(state => state.Kind == DurableIdentityKind.Item).NextIdentity;

    internal ulong[] ReservedUniqueItemEntityIds => Identities.RequireKinds(PersistedKinds).Kinds
        .Single(state => state.Kind == DurableIdentityKind.Item).Reserved;

    internal ulong[] RemovedUniqueItemEntityIds => Identities.RequireKinds(PersistedKinds).Kinds
        .Single(state => state.Kind == DurableIdentityKind.Item).Removed;

    internal static RulesetSavePayload Encode(DaggerfallSavePayload value) =>
        new(DaggerfallRuleset.Identity, CurrentSchemaVersion,
            JsonSerializer.SerializeToUtf8Bytes(value, DaggerfallSaveJsonContext.Default.DaggerfallSavePayload));

    /// <summary>
    /// Reads a save into the current shape. A payload whose meaning is recoverable is
    /// migrated and reported; only a version this code cannot interpret is refused.
    /// </summary>
    internal static DaggerfallSaveRead Read(RulesetSavePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Ruleset != DaggerfallRuleset.Identity)
            throw new ArgumentException("The save payload does not belong to the Daggerfall ruleset.", nameof(payload));
        if (payload.SchemaVersion == CurrentSchemaVersion)
        {
            return ReadCurrent(payload);
        }

        if (payload.SchemaVersion == MigratableSchemaVersion)
        {
            DaggerfallSavePayloadV1 legacy = ReadLegacy(payload);
            DaggerfallSavePayload migrated = legacy.Migrate();
            return new DaggerfallSaveRead(migrated.Validate(),
            [
                new SaveRestoreNotice("save-schema-migrated",
                    $"The save was written as Daggerfall schema {MigratableSchemaVersion} and was read as schema {CurrentSchemaVersion}: the reservation list became the item kind's reservations, cursor {legacy.NextUniqueItemEntityId} became the progress marker, and no identities were recorded as removed."),
            ]);
        }

        throw new ArgumentException($"Daggerfall save schema {payload.SchemaVersion} is not supported.", nameof(payload));
    }

    private static DaggerfallSaveRead ReadCurrent(RulesetSavePayload payload)
    {
        DaggerfallSavePayload value;
        try
        {
            value = JsonSerializer.Deserialize(payload.Bytes.Span, DaggerfallSaveJsonContext.Default.DaggerfallSavePayload)
                ?? throw new ArgumentException("The Daggerfall save payload is empty.", nameof(payload));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The Daggerfall save payload is malformed.", nameof(payload), exception);
        }

        // A save written before the payload carried owner sections simply has none. A
        // field added under an unchanged schema version is absent-but-recoverable, so it
        // is filled in and reported rather than making every earlier save unreadable.
        List<SaveRestoreNotice> notices = [];
        if (value.Owners is null)
        {
            value = value with { Owners = [] };
            notices.Add(new SaveRestoreNotice("owner-sections-absent",
                "The save was written before durable owner sections existed and carries none; the rest of its state is read unchanged."));
        }

        return new DaggerfallSaveRead(value.Validate(), notices);
    }

    private static DaggerfallSavePayloadV1 ReadLegacy(RulesetSavePayload payload)
    {
        try
        {
            return JsonSerializer.Deserialize(payload.Bytes.Span, DaggerfallSaveJsonContext.Default.DaggerfallSavePayloadV1)
                ?? throw new ArgumentException("The Daggerfall save payload is empty.", nameof(payload));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The Daggerfall save payload is malformed.", nameof(payload), exception);
        }
    }

    internal DaggerfallSavePayload Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion) throw new ArgumentException("The embedded Daggerfall schema version is unsupported.");
        ArgumentNullException.ThrowIfNull(Player);
        ArgumentNullException.ThrowIfNull(Actors);
        ArgumentNullException.ThrowIfNull(Inventory);
        ArgumentNullException.ThrowIfNull(Corpses);
        ArgumentNullException.ThrowIfNull(Identities);
        ArgumentNullException.ThrowIfNull(CombatCooldowns);
        ArgumentNullException.ThrowIfNull(Owners);
        HashSet<string> owners = new(StringComparer.Ordinal);
        foreach (DaggerfallOwnerSave owner in Owners)
        {
            ArgumentNullException.ThrowIfNull(owner);
            if (string.IsNullOrWhiteSpace(owner.OwnerId) || owner.Section.Length == 0 || !owners.Add(owner.OwnerId))
                throw new ArgumentException("Durable owner sections must name one non-empty owner id each and be distinct.");
        }

        if (Experience < 0 || Level < 1) throw new ArgumentOutOfRangeException(nameof(Experience));
        Identities.Validate().RequireKinds(PersistedKinds);
        KindAllocatorState identityState = Identities.Kinds.Single(state => state.Kind == DurableIdentityKind.Item);
        if (identityState.NextIdentity == 0) throw new ArgumentOutOfRangeException(nameof(Identities));
        Player.Validate();
        HashSet<long> ids = [];
        foreach (DaggerfallActorSave actor in Actors)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.EntityId <= 0 || !ids.Add(actor.EntityId)) throw new ArgumentException("Actor save identities must be positive and unique.");
            actor.Validate();
        }
        Inventory.Validate();
        HashSet<long> corpseActors = [];
        foreach (DaggerfallCorpseSave corpse in Corpses)
        {
            ArgumentNullException.ThrowIfNull(corpse);
            if (corpse.ActorId <= 0 || !corpseActors.Add(corpse.ActorId)) throw new ArgumentException("Corpse actor identities must be positive and unique.");
            corpse.Validate();
        }
        HashSet<ulong> reserved = [];
        foreach (ulong value in ReservedUniqueItemEntityIds)
            if (value == 0 || !reserved.Add(value)) throw new ArgumentException("Reserved unique entity identities must be non-zero and unique.");
        // Authored content normally reserves identities below the cursor, but a
        // reservation at or above it is tolerated rather than refused: the allocator
        // treats a reservation as live content wherever it sits and skips it when
        // issuing, so such a save still restores correctly. Nothing is silently
        // repaired or dropped here; the save keeps the values it carries.
        HashSet<long> cooldowns = [];
        foreach (DaggerfallCombatCooldownSave value in CombatCooldowns)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.AttackerId <= 0 || value.RemainingSteps == 0 || !cooldowns.Add(value.AttackerId))
                throw new ArgumentException("Combat readiness entries must be positive and distinct.");
        }
        Continuation?.Validate();
        return this;
    }

    /// <summary>
    /// The allocator state a save carries. It is transported verbatim rather than
    /// re-derived from the inventory: "allocated and not yet removed" is not the same
    /// fact as "currently held by this save", and a transport that guesses the first
    /// from the second invents tombstones for identities the player is carrying.
    /// </summary>
    internal DurableIdentityState RestoredIdentities() => Identities.Validate().RequireKinds(PersistedKinds);

    /// <summary>
    /// Resolves every ruleset/content reference before a restore session owns Engine
    /// resources, and reports what it could not explain instead of refusing an
    /// otherwise restorable save. An authored reference is a placement the selected
    /// content carries; a dynamic reference is an identity the allocator issued. A
    /// reference neither explains is reported and left out rather than materialized
    /// with an invented identity.
    /// </summary>
    internal DaggerfallRestorePlan ResolveRestore(DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, IRandomService random)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(inputs);
        tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        ArgumentNullException.ThrowIfNull(random);
        Validate();
        List<SaveRestoreNotice> notices = [];
        Dictionary<long, DaggerfallActorSave> savedActors = Actors.ToDictionary(actor => actor.EntityId);
        // The authored placement each saved actor explains. A saved actor the content
        // does not place is a reference nothing here can explain.
        List<DaggerfallActorSave> resolvedActors = [];
        foreach (DaggerfallActorSave actor in Actors.OrderBy(value => value.EntityId))
        {
            if (inputs.Project.Actors.TryGetValue(actor.EntityId, out AuthoredActor? placement))
            {
                if (!definitions.Actors.TryGetValue(placement.ActorId, out DaggerfallActorDefinition? definition))
                    throw new ArgumentException($"Selected content references unknown actor '{placement.ActorId.Value}'.");
                ValidateTracks(actor, definition, $"actor {actor.EntityId}");
                resolvedActors.Add(actor);
                continue;
            }

            notices.Add(new SaveRestoreNotice("unexplained-actor",
                $"Saved actor {actor.EntityId} is neither a placement in the selected content nor an identity the allocator issued, so it is not restored."));
        }

        // An authored placement the save does not mention is recoverable: the site load
        // materializes it at its authored position with authored vitals.
        foreach (AuthoredActor placement in inputs.Project.Actors.Values.OrderBy(value => value.EntityId))
        {
            if (!savedActors.ContainsKey(placement.EntityId))
            {
                notices.Add(new SaveRestoreNotice("authored-actor-not-saved",
                    $"Authored placement {placement.EntityId} is absent from the save and is materialized at its authored position."));
            }
        }

        DaggerfallSavePayload resolved = this with { Actors = [.. resolvedActors] };
        DaggerfallActorDefinition playerDefinition = definitions.RequireActor(new DaggerfallActorId("player"));
        ValidateTracks(Player, playerDefinition, "player");
        // Saved values the selected content disagrees with are reported with what was
        // observed. The saved state is what the player had; refusing would cost the whole
        // save over a difference the content cannot settle, and the donor assigns these
        // values verbatim on restore (DaggerfallEntity.SetHealth in restore mode) while
        // its pitch setter clamps.
        if (Player.PitchRadians < tuning.PlayerControl.PitchMinimumRadians || Player.PitchRadians > tuning.PlayerControl.PitchMaximumRadians)
        {
            float clamped = Math.Clamp(Player.PitchRadians, tuning.PlayerControl.PitchMinimumRadians, tuning.PlayerControl.PitchMaximumRadians);
            notices.Add(new SaveRestoreNotice("player-pitch-outside-tuning",
                $"Saved player pitch {Player.PitchRadians} is outside the selected tuning bounds [{tuning.PlayerControl.PitchMinimumRadians}, {tuning.PlayerControl.PitchMaximumRadians}] and was clamped to {clamped}."));
            resolved = resolved with { Player = resolved.Player with { PitchRadians = clamped } };
        }

        int expectedLevel = checked(1 + DaggerfallFormulaPolicy.ExperimentalXpLevel(Experience, DaggerfallFormulaPolicy.Experimental));
        if (Level != expectedLevel)
        {
            notices.Add(new SaveRestoreNotice("progression-level-recomputed",
                $"Saved level {Level} does not match the level {expectedLevel} that the saved experience {Experience} derives on the selected Daggerfall XP curve; the derived level is used."));
            resolved = resolved with { Level = expectedLevel };
        }

        int maximumExperience = inputs.Project.Actors.Values
            .Select(actor => definitions.RequireActor(actor.ActorId).Rewards.ExperienceReward)
            .Where(value => value > 0)
            .Aggregate(0, (total, value) => checked(total + value));
        if (Experience > maximumExperience)
        {
            notices.Add(new SaveRestoreNotice("progression-above-authored-rewards",
                $"Saved experience {Experience} exceeds the {maximumExperience} the selected content can award; the saved value is kept because the selected content may have changed."));
        }

        int endurance = playerDefinition.Stats.Endurance;
        long playerHealthMaximum = playerDefinition.PlayerInitialVitals.HealthMaximum;
        for (int restoredLevel = 2; restoredLevel <= expectedLevel; restoredLevel++)
            playerHealthMaximum = checked(playerHealthMaximum + DaggerfallLevelUpHealthSource.RollGain(random, playerDefinition, endurance, restoredLevel));
        if (Player.Health > playerHealthMaximum)
        {
            notices.Add(new SaveRestoreNotice("player-health-above-reconstruction",
                $"Saved player health {Player.Health} exceeds the {playerHealthMaximum} this session reconstructs for level {expectedLevel}; the saved value is kept because the reconstruction is not an authority on the player's health."));
        }

        // Durable references resolve against the ledger the save carries: an identity is
        // explainable when authored content reserved it or the allocator issued it
        // below the progress marker. Anything else is reported and left out.
        KindAllocatorState identityState = Identities.Kinds.Single(state => state.Kind == DurableIdentityKind.Item);
        HashSet<ulong> removed = identityState.Removed.ToHashSet();
        HashSet<ulong> placementIdentities = PlacementEntityIds(inputs);
        bool Explainable(ulong value) =>
            identityState.Reserved.Contains(value)
            || (value < identityState.NextIdentity && !removed.Contains(value));
        foreach (ulong value in resolved.Inventory.UniqueItems.Select(item => item.EntityId)
            .Concat(resolved.Corpses.SelectMany(corpse => corpse.UniqueItems.Select(item => item.EntityId)))
            .Where(value => !Explainable(value))
            .Distinct()
            .Order())
        {
            notices.Add(new SaveRestoreNotice("unexplained-item-identity",
                $"Saved unique item identity {value} is neither reserved content nor issued by the carried ledger, so that item is not restored."));
        }

        DaggerfallInventorySave inventory = ResolveInventory(resolved.Inventory, definitions, "player", Explainable, notices);
        List<DaggerfallCorpseSave> resolvedCorpses = [];
        Dictionary<long, DaggerfallActorSave> resolvedSaved = resolvedActors.ToDictionary(actor => actor.EntityId);
        HashSet<long> corpseActors = [];
        foreach (DaggerfallCorpseSave corpse in Corpses)
        {
            if (!resolvedSaved.TryGetValue(corpse.ActorId, out DaggerfallActorSave? actor))
            {
                notices.Add(new SaveRestoreNotice("unexplained-corpse",
                    $"Saved corpse for actor {corpse.ActorId} refers to an actor that is not restored, so the corpse is not restored."));
                continue;
            }

            if (!corpseActors.Add(corpse.ActorId) || actor.Health != 0)
                throw new ArgumentException("Saved corpses must refer once to a defeated selected actor.");
            if (!corpse.IsInteractable && (corpse.Stacks.Length != 0 || corpse.UniqueItems.Length != 0))
                throw new ArgumentException("A looted corpse cannot retain inventory contents.");
            DaggerfallInventorySave corpseInventory = ResolveInventory(
                new DaggerfallInventorySave(corpse.Stacks, corpse.UniqueItems, []), definitions, $"corpse {corpse.ActorId}", Explainable, notices);
            resolvedCorpses.Add(corpse with { Stacks = corpseInventory.Stacks, UniqueItems = corpseInventory.UniqueItems });
        }

        HashSet<ulong> live = inventory.UniqueItems.Select(item => item.EntityId)
            .Concat(resolvedCorpses.SelectMany(corpse => corpse.UniqueItems.Select(item => item.EntityId)))
            .ToHashSet();
        if (live.Overlaps(placementIdentities))
            throw new ArgumentException("Saved unique items cannot collide with player or actor placement identities.");
        foreach (ulong value in removed.Intersect(placementIdentities).Order())
        {
            // Authored content claims the identity whether or not the ledger tombstoned
            // it, so the reservation wins and the contradiction is reported.
            notices.Add(new SaveRestoreNotice("authored-identity-removed",
                $"Identity {value} is authored content and is also recorded as removed; the authored reservation is kept and the tombstone is ignored."));
        }

        resolved = resolved with { Inventory = inventory, Corpses = [.. resolvedCorpses] };
        return new DaggerfallRestorePlan(resolved, notices);
    }

    /// <summary>
    /// Keeps the inventory whose durable references the ledger explains and reports the
    /// rest. Structure, item definitions and equipment compatibility are still checked
    /// exactly: only the identity evidence is treated as recoverable.
    /// </summary>
    private static DaggerfallInventorySave ResolveInventory(
        DaggerfallInventorySave inventory,
        DaggerfallDefinitions definitions,
        string owner,
        Func<ulong, bool> explainable,
        List<SaveRestoreNotice> notices)
    {
        List<DaggerfallStackSave> stacks = [];
        foreach (DaggerfallStackSave stack in inventory.Stacks)
        {
            if (!definitions.Items.TryGetValue(new DaggerfallItemId(stack.ItemId), out DaggerfallItemDefinition? stackItem) || !stackItem.IsFungible)
            {
                notices.Add(new SaveRestoreNotice("unexplained-item-template",
                    $"Saved {owner} stack '{stack.ItemId}' is not a fungible item in the selected content, so that stack is not restored."));
                continue;
            }

            if (stack.Quantity > stackItem.MaximumQuantity)
            {
                notices.Add(new SaveRestoreNotice("stack-above-authored-maximum",
                    $"Saved {owner} stack '{stack.ItemId}' holds {stack.Quantity}, above the selected maximum of {stackItem.MaximumQuantity}; it was reduced to the maximum."));
                stacks.Add(stack with { Quantity = stackItem.MaximumQuantity });
                continue;
            }

            stacks.Add(stack);
        }

        List<DaggerfallUniqueSave> unique = [];
        foreach (DaggerfallUniqueSave item in inventory.UniqueItems)
        {
            if (!definitions.Items.TryGetValue(new DaggerfallItemId(item.ItemId), out DaggerfallItemDefinition? definition) || definition.IsFungible)
            {
                notices.Add(new SaveRestoreNotice("unexplained-item-template",
                    $"Saved {owner} unique item '{item.ItemId}' is not a non-fungible item in the selected content, so that item is not restored."));
                continue;
            }

            if (!explainable(item.EntityId))
            {
                // Reported by the caller with the identity it observed; the item is not
                // materialized under an identity nothing explains.
                continue;
            }

            unique.Add(item);
        }

        DaggerfallUniqueSave[] kept = [.. unique];
        DaggerfallEquipmentSave[] equipment = [.. inventory.Equipment.Where(entry => kept.Any(item => item.EntityId == entry.ItemEntityId))];
        DaggerfallInventorySave resolved = new([.. stacks], kept, equipment);
        HashSet<ulong> live = [];
        ValidateInventory(resolved, definitions, live, owner, requireEquipmentSlots: owner == "player");
        return resolved;
    }

    /// <summary>The player and every authored placement identity, which no dynamic allocation may collide with.</summary>
    internal static HashSet<ulong> PlacementEntityIds(PrivateersHoldInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        HashSet<ulong> ids = [(ulong)DaggerfallActorIdentity.PlayerEntityId];
        foreach (AuthoredActor actor in inputs.Project.Actors.Values) ids.Add(checked((ulong)actor.EntityId));
        return ids;
    }

    /// <summary>
    /// Every durable identity authored content claims: the player, each placement,
    /// and each unique loadout item. The allocator reserves them before any dynamic
    /// allocation, which is what keeps later dynamic entities from colliding with
    /// content that a subsequent site load materializes.
    /// </summary>
    internal static HashSet<ulong> ContentEntityIds(PrivateersHoldInputs inputs, IReadOnlyList<DaggerfallLoadoutEntry> loadout)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(loadout);
        HashSet<ulong> ids = [(ulong)DaggerfallActorIdentity.PlayerEntityId];
        foreach (AuthoredActor actor in inputs.Project.Actors.Values) ids.Add(checked((ulong)actor.EntityId));
        foreach (DaggerfallLoadoutEntry entry in loadout)
            if (entry.UniqueEntityId is ulong entityId) ids.Add(entityId);
        return ids;
    }

    private static void ValidateTracks(DaggerfallPlayerSave value, DaggerfallActorDefinition definition, string owner)
    {
        DaggerfallVitalValues initial = definition.PlayerInitialVitals;
        if (value.Health < 0 || value.Stamina < 0 || value.Stamina > initial.StaminaMaximum || value.Magicka < 0 || value.Magicka > initial.MagickaMaximum)
        {
            throw new ArgumentException(
                $"Saved {owner} tracks are outside the selected bounds: health {value.Health}, stamina {value.Stamina} of at most {initial.StaminaMaximum}, magicka {value.Magicka} of at most {initial.MagickaMaximum}.");
        }
    }

    private static void ValidateTracks(DaggerfallActorSave value, DaggerfallActorDefinition definition, string owner)
    {
        if (value.Health < 0 || value.Health > definition.Health.Maximum || value.Stamina != 0 || value.Magicka != 0)
            throw new ArgumentException($"Saved {owner} tracks are outside selected actor bounds.");
    }

    private static void ValidateInventory(DaggerfallInventorySave inventory, DaggerfallDefinitions definitions, HashSet<ulong> allUnique, string owner, bool requireEquipmentSlots)
    {
        foreach (DaggerfallStackSave stack in inventory.Stacks)
        {
            if (!definitions.Items.TryGetValue(new DaggerfallItemId(stack.ItemId), out DaggerfallItemDefinition? item) || !item.IsFungible)
                throw new ArgumentException($"Saved {owner} stack '{stack.ItemId}' is not a selected fungible item.");
            if (stack.Quantity > item.MaximumQuantity)
                throw new ArgumentException($"Saved {owner} stack '{stack.ItemId}' exceeds its authored maximum quantity.");
        }
        Dictionary<ulong, DaggerfallItemDefinition> unique = [];
        foreach (DaggerfallUniqueSave value in inventory.UniqueItems)
        {
            if (!definitions.Items.TryGetValue(new DaggerfallItemId(value.ItemId), out DaggerfallItemDefinition? item) || item.IsFungible || !allUnique.Add(value.EntityId))
                throw new ArgumentException($"Saved {owner} unique item '{value.EntityId}' is invalid or duplicated.");
            unique.Add(value.EntityId, item);
        }
        if (!requireEquipmentSlots) return;
        foreach (DaggerfallEquipmentSave equipped in inventory.Equipment)
        {
            if (!definitions.EquipmentSlots.TryGetValue(new DaggerfallEquipmentSlotId(equipped.SlotId), out DaggerfallEquipmentSlotDefinition? slot)
                || !unique.TryGetValue(equipped.ItemEntityId, out DaggerfallItemDefinition? item)
                || item.Equipment is null
                || !item.Equipment.Classifications.Any(classification => slot.AllowedClassifications.Contains(classification)))
                throw new ArgumentException($"Saved equipment slot '{equipped.SlotId}' is incompatible with its unique item.");
        }
    }
}

/// <summary>
/// A save reduced to what the selected content and the carried ledger explain, with
/// what had to be reported about the rest.
/// </summary>
internal sealed record DaggerfallRestorePlan(DaggerfallSavePayload Payload, IReadOnlyList<SaveRestoreNotice> Notices);

/// <summary>What reading a save produced: the payload in the current shape, and what it had to report.</summary>
internal sealed record DaggerfallSaveRead(DaggerfallSavePayload Payload, IReadOnlyList<SaveRestoreNotice> Notices);

/// <summary>
/// A later world, item, effect or quest owner's durable section. Sections are opaque to
/// every owner but their own and are carried in owner-id order, which is what lets a
/// later task add state without reshaping the payload every other owner reads.
/// </summary>
internal sealed record DaggerfallOwnerSave(string OwnerId, byte[] Section);

/// <summary>
/// A later world, item, effect or quest owner's durable state. The session hands each
/// owner exactly its own section on restore and writes what it captures back under the
/// same id; a task that supplies records implements this beside them and passes it to
/// the session, so there is no reflection registry and no service locator.
/// </summary>
internal interface IDaggerfallSaveOwner
{
    string OwnerId { get; }

    byte[] Capture();

    void Restore(ReadOnlySpan<byte> section);
}

/// <summary>
/// The schema-1 shape, kept only to read what it wrote: a flat reservation list, an
/// explicit next identity, and no notion of removed identities. Its meaning is fully
/// recoverable, so it is migrated rather than refused.
/// </summary>
internal sealed record DaggerfallSavePayloadV1(
    uint SchemaVersion,
    DaggerfallPlayerSave Player,
    DaggerfallActorSave[] Actors,
    int Experience,
    int Level,
    DaggerfallInventorySave Inventory,
    DaggerfallCorpseSave[] Corpses,
    ulong NextUniqueItemEntityId,
    ulong[] ReservedUniqueItemEntityIds,
    DaggerfallCombatCooldownSave[] CombatCooldowns,
    DaggerfallContinuationSave? Continuation)
{
    internal DaggerfallSavePayload Migrate()
    {
        // A schema-1 label on bytes that never carried schema-1 fields is a misread, not
        // a migration: the values it should have are absent, so its meaning is not
        // recoverable and the save is refused with what was actually found.
        if (ReservedUniqueItemEntityIds is null || NextUniqueItemEntityId == 0)
        {
            throw new ArgumentException(
                $"A payload labelled as Daggerfall schema {DaggerfallSavePayload.MigratableSchemaVersion} does not carry that schema's identity fields (next unique item identity {NextUniqueItemEntityId}, reservation list {(ReservedUniqueItemEntityIds is null ? "absent" : "present")}).");
        }

        return new DaggerfallSavePayload(
            DaggerfallSavePayload.CurrentSchemaVersion,
            Player,
            Actors,
            Experience,
            Level,
            Inventory,
            Corpses,
            new DurableIdentityState([new KindAllocatorState(DurableIdentityKind.Item, NextUniqueItemEntityId, ReservedUniqueItemEntityIds, [])]),
            CombatCooldowns,
            Continuation,
            []);
    }
}

internal sealed record DaggerfallInventorySave(DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems, DaggerfallEquipmentSave[] Equipment)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Stacks);
        ArgumentNullException.ThrowIfNull(UniqueItems);
        ArgumentNullException.ThrowIfNull(Equipment);
        HashSet<string> stackItems = new(StringComparer.Ordinal);
        foreach (DaggerfallStackSave stack in Stacks)
        {
            ArgumentNullException.ThrowIfNull(stack);
            if (string.IsNullOrWhiteSpace(stack.ItemId) || stack.Quantity == 0 || !stackItems.Add(stack.ItemId)) throw new ArgumentException("Inventory stacks must be unique non-empty positive entries.");
        }
        HashSet<ulong> uniqueItems = [];
        foreach (DaggerfallUniqueSave unique in UniqueItems)
        {
            ArgumentNullException.ThrowIfNull(unique);
            if (string.IsNullOrWhiteSpace(unique.ItemId) || unique.EntityId == 0 || !uniqueItems.Add(unique.EntityId)) throw new ArgumentException("Unique inventory entries must be valid and distinct.");
        }
        HashSet<string> slots = new(StringComparer.Ordinal);
        foreach (DaggerfallEquipmentSave equipped in Equipment)
        {
            ArgumentNullException.ThrowIfNull(equipped);
            if (string.IsNullOrWhiteSpace(equipped.SlotId) || !slots.Add(equipped.SlotId) || !uniqueItems.Contains(equipped.ItemEntityId)) throw new ArgumentException("Equipment must refer to a saved unique item once per slot.");
        }
    }
}

internal sealed record DaggerfallStackSave(string ItemId, ulong Quantity);
internal sealed record DaggerfallUniqueSave(string ItemId, ulong EntityId);
internal sealed record DaggerfallEquipmentSave(string SlotId, ulong ItemEntityId);
/// <summary>Ruleset-relative cooldown remaining at the save boundary, not a Host generation identity.</summary>
internal sealed record DaggerfallCombatCooldownSave(long AttackerId, ulong RemainingSteps);
internal sealed record DaggerfallCorpseSave(long ActorId, ulong OriginatingSequence, bool IsRegistered, bool IsInteractable, DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Stacks);
        ArgumentNullException.ThrowIfNull(UniqueItems);
        if (!IsRegistered && (Stacks.Length != 0 || UniqueItems.Length != 0)) throw new ArgumentException("An unregistered corpse cannot contain inventory.");
        new DaggerfallInventorySave(Stacks, UniqueItems, []).Validate();
    }
}

internal sealed record DaggerfallPlayerSave(float X, float Y, float Z, float YawRadians, float PitchRadians, long Health, long Stamina, long Magicka)
{
    internal void Validate()
    {
        RequireFinite(X, nameof(X));
        RequireFinite(Y, nameof(Y));
        RequireFinite(Z, nameof(Z));
        if (!float.IsFinite(YawRadians) || !float.IsFinite(PitchRadians)) throw new ArgumentOutOfRangeException(nameof(YawRadians));
    }

    private static void RequireFinite(float value, string name)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(name);
    }
}

internal sealed record DaggerfallActorSave(long EntityId, float X, float Y, float Z, float HeadingRadians, long Health, long Stamina, long Magicka)
{
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z)) throw new ArgumentOutOfRangeException(nameof(X));
        if (!float.IsFinite(HeadingRadians)) throw new ArgumentOutOfRangeException(nameof(HeadingRadians));
    }
}

/// <summary>Contains no Engine handles; source identity is diagnostic only and the Engine validates the compatibility fingerprints.</summary>
internal sealed record DaggerfallContinuationSave(CharacterContinuationCheckpoint Checkpoint)
{
    internal void Validate()
    {
        if (Checkpoint.SourceGeneration == 0 || Checkpoint.SpatialSessionFingerprint == 0 || Checkpoint.ContentAuthorityHash == 0 || Checkpoint.ConfigFingerprint == 0)
            throw new ArgumentException("The spatial continuation checkpoint is incomplete.");
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(DaggerfallSavePayload))]
[JsonSerializable(typeof(DaggerfallSavePayloadV1))]
[JsonSerializable(typeof(DurableIdentityState))]
[JsonSerializable(typeof(KindAllocatorState))]
[JsonSerializable(typeof(CharacterContinuationCheckpoint))]
internal partial class DaggerfallSaveJsonContext : JsonSerializerContext;
