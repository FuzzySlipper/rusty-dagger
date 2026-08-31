using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
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
    ulong NextUniqueItemEntityId,
    ulong[] ReservedUniqueItemEntityIds,
    DaggerfallCombatCooldownSave[] CombatCooldowns,
    DaggerfallContinuationSave? Continuation)
{
    internal const uint CurrentSchemaVersion = 1;

    internal static RulesetSavePayload Encode(DaggerfallSavePayload value) =>
        new(DaggerfallRuleset.Identity, CurrentSchemaVersion,
            JsonSerializer.SerializeToUtf8Bytes(value, DaggerfallSaveJsonContext.Default.DaggerfallSavePayload));

    internal static DaggerfallSavePayload Decode(RulesetSavePayload payload)
    {
        if (payload.Ruleset != DaggerfallRuleset.Identity)
            throw new ArgumentException("The save payload does not belong to the Daggerfall ruleset.", nameof(payload));
        if (payload.SchemaVersion != CurrentSchemaVersion)
            throw new ArgumentException($"Daggerfall save schema {payload.SchemaVersion} is not supported.", nameof(payload));
        try
        {
            DaggerfallSavePayload value = JsonSerializer.Deserialize(payload.Bytes.Span, DaggerfallSaveJsonContext.Default.DaggerfallSavePayload)
                ?? throw new ArgumentException("The Daggerfall save payload is empty.", nameof(payload));
            return value.Validate();
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
        ArgumentNullException.ThrowIfNull(ReservedUniqueItemEntityIds);
        ArgumentNullException.ThrowIfNull(CombatCooldowns);
        if (Experience < 0 || Level < 1) throw new ArgumentOutOfRangeException(nameof(Experience));
        if (NextUniqueItemEntityId == 0) throw new ArgumentOutOfRangeException(nameof(NextUniqueItemEntityId));
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

    /// <summary>Checks every ruleset/content reference before a restore session owns Engine resources.</summary>
    internal void ValidateForRestore(DaggerfallDefinitions definitions, PrivateersHoldInputs inputs, DaggerfallTuning tuning, IRandomService random)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(inputs);
        tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        ArgumentNullException.ThrowIfNull(random);
        Validate();
        Dictionary<long, DaggerfallActorSave> savedActors = Actors.ToDictionary(actor => actor.EntityId);
        long[] expectedActors = inputs.Project.Actors.Keys.OrderBy(value => value).ToArray();
        if (!savedActors.Keys.OrderBy(value => value).SequenceEqual(expectedActors))
            throw new ArgumentException("The saved actor identities do not exactly match the selected Daggerfall content.");
        foreach (AuthoredActor authored in inputs.Project.Actors.Values)
        {
            if (!definitions.Actors.TryGetValue(authored.ActorId, out DaggerfallActorDefinition? definition))
                throw new ArgumentException($"Selected content references unknown actor '{authored.ActorId.Value}'.");
            ValidateTracks(savedActors[authored.EntityId], definition, $"actor {authored.EntityId}");
        }
        DaggerfallActorDefinition playerDefinition = definitions.RequireActor(new DaggerfallActorId("player"));
        ValidateTracks(Player, playerDefinition, "player");
        if (Player.PitchRadians < tuning.PlayerControl.PitchMinimumRadians || Player.PitchRadians > tuning.PlayerControl.PitchMaximumRadians)
            throw new ArgumentException("Saved player pitch is outside the selected tuning bounds.");
        int expectedLevel = checked(1 + DaggerfallFormulaPolicy.ExperimentalXpLevel(Experience, DaggerfallFormulaPolicy.Experimental));
        if (Level != expectedLevel)
            throw new ArgumentException("Saved progression level does not match the selected Daggerfall XP curve.");
        int maximumExperience = inputs.Project.Actors.Values
            .Select(actor => definitions.RequireActor(actor.ActorId).Rewards.ExperienceReward)
            .Where(value => value > 0)
            .Aggregate(0, (total, value) => checked(total + value));
        if (Experience > maximumExperience)
            throw new ArgumentException("Saved progression exceeds the maximum authored rewards in the selected content.");
        int endurance = playerDefinition.Stats.Endurance;
        long playerHealthMaximum = playerDefinition.PlayerInitialVitals.HealthMaximum;
        for (int restoredLevel = 2; restoredLevel <= Level; restoredLevel++)
            playerHealthMaximum = checked(playerHealthMaximum + DaggerfallLevelUpHealthSource.RollGain(random, playerDefinition, endurance, restoredLevel));
        if (Player.Health > playerHealthMaximum)
            throw new ArgumentException("Saved player health exceeds its reconstructed Daggerfall maximum.");

        HashSet<long> corpses = [];
        foreach (DaggerfallCorpseSave corpse in Corpses)
        {
            if (!corpses.Add(corpse.ActorId) || !savedActors.TryGetValue(corpse.ActorId, out DaggerfallActorSave? actor) || actor.Health != 0)
                throw new ArgumentException("Saved corpses must refer once to a defeated selected actor.");
            if (!corpse.IsInteractable && (corpse.Stacks.Length != 0 || corpse.UniqueItems.Length != 0))
                throw new ArgumentException("A looted corpse cannot retain inventory contents.");
        }

        HashSet<ulong> allUnique = [];
        ValidateInventory(Inventory, definitions, allUnique, "player", requireEquipmentSlots: true);
        foreach (DaggerfallCorpseSave corpse in Corpses)
            ValidateInventory(new DaggerfallInventorySave(corpse.Stacks, corpse.UniqueItems, []), definitions, allUnique, $"corpse {corpse.ActorId}", requireEquipmentSlots: false);
        HashSet<ulong> reserved = ReservedUniqueItemEntityIds.ToHashSet();
        HashSet<ulong> stableEntityIds = inputs.Project.Actors.Keys.Select(value => checked((ulong)value)).ToHashSet();
        stableEntityIds.Add((ulong)DaggerfallActorIdentity.PlayerEntityId);
        if (allUnique.Overlaps(stableEntityIds))
            throw new ArgumentException("Saved unique items cannot collide with player, actor, or corpse-owner identities.");
        stableEntityIds.UnionWith(allUnique);
        if (!stableEntityIds.IsSubsetOf(reserved))
            throw new ArgumentException("Allocator reservations must include every stable and current unique entity identity.");
    }

    private static void ValidateTracks(DaggerfallPlayerSave value, DaggerfallActorDefinition definition, string owner)
    {
        DaggerfallVitalValues initial = definition.PlayerInitialVitals;
        if (value.Health < 0 || value.Stamina < 0 || value.Stamina > initial.StaminaMaximum || value.Magicka < 0 || value.Magicka > initial.MagickaMaximum)
            throw new ArgumentException($"Saved {owner} tracks cannot be negative.");
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
[JsonSerializable(typeof(CharacterContinuationCheckpoint))]
internal partial class DaggerfallSaveJsonContext : JsonSerializerContext;
