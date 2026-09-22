using Rusty.Engine;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Current condition units for one durable item instance.</summary>
internal sealed record DaggerfallItemCondition(int Current, int Maximum, int Percentage)
{
    internal bool IsBroken => Maximum > 0 && Current == 0;
}

internal enum DaggerfallItemConditionOutcome
{
    Damaged,
    Broken,
    AlreadyBroken,
    Repaired,
    AlreadyRepaired,
    Identified,
    AlreadyIdentified,
    Enchanted,
    AlreadyEnchanted,
}

/// <summary>One completed item-state operation; a break carries its single canonical equipment change.</summary>
internal sealed record DaggerfallItemConditionResult(
    DaggerfallItemConditionOutcome Outcome,
    ulong DurableItemId,
    DaggerfallItemInstanceMetadata Metadata,
    DaggerfallEquipmentChange? EquipmentChange = null);

/// <summary>
/// Daggerfall-owned mutations of persisted item condition, identification and enchantment meaning.
/// Engine inventory and equipment continue to own containment and equipment relationship state.
/// </summary>
internal sealed class DaggerfallItemConditionService(
    DaggerfallDefinitions definitions,
    DaggerfallItemInstances instances,
    DaggerfallEquipmentMoves equipment)
{
    internal DaggerfallItemCondition Read(ulong durableItemId) => Condition(instances.RequireUnique(durableItemId));

    /// <summary>Lowers condition by named classic units, clamps at zero, and unequips exactly once on the break transition.</summary>
    internal DaggerfallItemConditionResult Damage(UniqueInventoryItem item, int units)
    {
        if (units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
        (ulong durableItemId, DaggerfallItemInstanceMetadata metadata) = RequirePlayerItem(item);
        return Damage(item, durableItemId, metadata, units, broken =>
        {
            EquipmentMoveResult result = equipment.RemoveBroken(broken);
            if (result.Outcome != EquipmentMoveOutcome.Applied)
                throw new InvalidOperationException($"Broken item '{metadata.ItemId}' could not be removed from equipment: {result.Detail}");
            return result.Change;
        });
    }

    /// <summary>
    /// Applies the same condition transition to an equipped non-player item. Its owning actor's
    /// Engine equipment coordinator remains the authoritative containment/removal owner; player
    /// equipment continues through <see cref="DaggerfallEquipmentMoves"/> for its layout and UI cue.
    /// </summary>
    internal DaggerfallItemConditionResult Damage(UniqueInventoryItem item, DaggerfallItemOwner owner,
        MechanicsEquipmentCoordinator actorEquipment, int units)
    {
        if (units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
        ArgumentNullException.ThrowIfNull(actorEquipment);
        owner.Validate();
        if (owner == DaggerfallItemOwner.Player)
            return Damage(item, units);
        if (!actorEquipment.Inventory.UniqueItems.Any(candidate => candidate.Entity.Value == item.EntityId))
            throw new InvalidOperationException($"Unique item '{item.EntityId}' is no longer in this inventory.");
        ulong durableItemId = actorEquipment.GetDurableItemId(new Rusty.Engine.Entities.EntityId(item.EntityId)).Value;
        DaggerfallItemInstanceMetadata metadata = instances.RequireUnique(durableItemId);
        RequireItemMetadata(item, metadata);
        if (metadata.Owner != owner)
            throw new InvalidOperationException($"Item '{metadata.ItemId}' belongs to {metadata.Owner.Scope} {metadata.Owner.Id}, not {owner.Scope} {owner.Id}.");
        return Damage(item, durableItemId, metadata, units, broken => RemoveActorBroken(actorEquipment, broken));
    }

    private DaggerfallItemConditionResult Damage(UniqueInventoryItem item, ulong durableItemId,
        DaggerfallItemInstanceMetadata metadata, int units, Func<UniqueInventoryItem, DaggerfallEquipmentChange?> removeBroken)
    {
        if (metadata.MaximumCondition == 0)
            throw new InvalidOperationException($"Item '{metadata.ItemId}' has no condition units.");
        if (metadata.CurrentCondition == 0)
            return new(DaggerfallItemConditionOutcome.AlreadyBroken, durableItemId, metadata);

        int current = Math.Max(0, checked(metadata.CurrentCondition - units));
        DaggerfallEquipmentChange? removed = null;
        if (current == 0)
        {
            removed = removeBroken(item);
        }
        DaggerfallItemInstanceMetadata changed = metadata with { CurrentCondition = current };
        instances.ReplaceUnique(durableItemId, changed);
        if (current != 0)
            return new(DaggerfallItemConditionOutcome.Damaged, durableItemId, changed);
        return new(DaggerfallItemConditionOutcome.Broken, durableItemId, changed, removed);
    }

    /// <summary>Restores an existing condition-bearing item to its authored maximum without changing durable identity.</summary>
    internal DaggerfallItemConditionResult Repair(UniqueInventoryItem item)
    {
        (ulong durableItemId, DaggerfallItemInstanceMetadata metadata) = RequirePlayerItem(item);
        if (metadata.MaximumCondition == 0)
            throw new InvalidOperationException($"Item '{metadata.ItemId}' has no condition units to repair.");
        if (metadata.CurrentCondition == metadata.MaximumCondition)
            return new(DaggerfallItemConditionOutcome.AlreadyRepaired, durableItemId, metadata);
        DaggerfallItemInstanceMetadata repaired = metadata with { CurrentCondition = metadata.MaximumCondition };
        instances.ReplaceUnique(durableItemId, repaired);
        return new(DaggerfallItemConditionOutcome.Repaired, durableItemId, repaired);
    }

    /// <summary>Discloses the published magic template for an existing enchanted instance without changing its identity.</summary>
    internal DaggerfallItemConditionResult Identify(UniqueInventoryItem item)
    {
        (ulong durableItemId, DaggerfallItemInstanceMetadata metadata) = RequirePlayerItem(item);
        if (metadata.Enchantment is null || metadata.Identified)
            return new(DaggerfallItemConditionOutcome.AlreadyIdentified, durableItemId, metadata);
        RequireMagic(metadata);
        DaggerfallItemInstanceMetadata identified = metadata with { Identified = true };
        instances.ReplaceUnique(durableItemId, identified);
        return new(DaggerfallItemConditionOutcome.Identified, durableItemId, identified);
    }

    /// <summary>
    /// Applies a published magic template to an ordinary live item. The Engine retains the item's
    /// structural definition; metadata owns the added Daggerfall policy, donor uses, and disclosure.
    /// </summary>
    internal DaggerfallItemConditionResult Enchant(UniqueInventoryItem item, string magicItemKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magicItemKey);
        (ulong durableItemId, DaggerfallItemInstanceMetadata metadata) = RequirePlayerItem(item);
        DaggerfallMagicItemDefinition magic = definitions.Magic.MagicItems.TryGetValue(magicItemKey, out DaggerfallMagicItemDefinition? found)
            ? found : throw new InvalidOperationException($"Magic item '{magicItemKey}' is not published.");
        if (metadata.Enchantment is not null)
        {
            if (metadata.Enchantment == magic.Key) return new(DaggerfallItemConditionOutcome.AlreadyEnchanted, durableItemId, metadata);
            throw new InvalidOperationException($"Item '{metadata.ItemId}' already has enchantment '{metadata.Enchantment}'.");
        }
        string publishedMagicDefinition = DaggerfallMagicItemIds.For(metadata.ItemId, magic.Key);
        if (!definitions.TryResolveItem(new DaggerfallItemId(publishedMagicDefinition), out _))
            throw new InvalidOperationException($"Magic item '{magic.Key}' cannot enchant item definition '{metadata.ItemId}'.");
        EquipmentMoveResult unequipped = equipment.UnequipForEnchantment(item);
        if (unequipped.Outcome != EquipmentMoveOutcome.Applied)
            throw new InvalidOperationException($"Item '{metadata.ItemId}' could not be unequipped before enchanting: {unequipped.Detail}");
        DaggerfallItemInstanceMetadata enchanted = metadata with
        {
            Enchantment = magic.Key,
            Identified = true,
            CurrentCondition = magic.Uses,
            MaximumCondition = magic.Uses,
        };
        instances.ReplaceUnique(durableItemId, enchanted);
        return new(DaggerfallItemConditionOutcome.Enchanted, durableItemId, enchanted, unequipped.Change);
    }

    internal DaggerfallItemCondition Condition(DaggerfallItemInstanceMetadata metadata) =>
        new(metadata.CurrentCondition, metadata.MaximumCondition,
            DaggerfallFormulaPolicy.ConditionPercentage(metadata.CurrentCondition, metadata.MaximumCondition));

    private void RequireItemMetadata(DaggerfallItemInstanceMetadata metadata)
    {
        if (!definitions.TryResolveItem(new DaggerfallItemId(metadata.ItemId), out _))
            throw new InvalidOperationException($"Item instance names unpublished definition '{metadata.ItemId}'.");
        if (metadata.Enchantment is not null) _ = RequireMagic(metadata);
    }

    private void RequireItemMetadata(UniqueInventoryItem item, DaggerfallItemInstanceMetadata metadata)
    {
        if (!string.Equals(item.Definition.Value, metadata.ItemId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Live item definition '{item.Definition.Value}' does not match durable metadata '{metadata.ItemId}'.");
        RequireItemMetadata(metadata);
    }

    private (ulong DurableItemId, DaggerfallItemInstanceMetadata Metadata) RequirePlayerItem(UniqueInventoryItem item)
    {
        ulong durableItemId = equipment.RequireDurableIdentity(item);
        DaggerfallItemInstanceMetadata metadata = instances.RequireUnique(durableItemId);
        RequireItemMetadata(item, metadata);
        if (metadata.Owner != DaggerfallItemOwner.Player)
            throw new InvalidOperationException($"Item '{metadata.ItemId}' belongs to {metadata.Owner.Scope} {metadata.Owner.Id}, not the player inventory.");
        return (durableItemId, metadata);
    }

    private DaggerfallEquipmentChange? RemoveActorBroken(MechanicsEquipmentCoordinator equipment, UniqueInventoryItem item)
    {
        EquipmentRead before = equipment.Read();
        if (!before.Assignments.Any(assignment => assignment.Item.EntityId == item.EntityId)) return null;
        try { equipment.Unequip(item); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException($"Broken item '{item.Definition.Value}' could not be removed from equipment: {error.Message}", error);
        }
        return new DaggerfallEquipmentChange(null, [item], DaggerfallHandEquipTiming.None, DaggerfallEquipmentCue.Unequip);
    }

    private DaggerfallMagicItemDefinition RequireMagic(DaggerfallItemInstanceMetadata metadata) =>
        metadata.Enchantment is { } key && definitions.Magic.MagicItems.TryGetValue(key, out DaggerfallMagicItemDefinition? magic)
            ? magic
            : throw new InvalidOperationException($"Item '{metadata.ItemId}' names unpublished magic metadata '{metadata.Enchantment}'.");
}
