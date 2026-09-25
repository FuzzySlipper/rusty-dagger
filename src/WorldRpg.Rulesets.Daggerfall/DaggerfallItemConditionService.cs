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
    int PreviousCondition,
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

    /// <summary>Returns the item-maker's pure capacity and spell-cost quotation without changing item condition or equipment.</summary>
    internal DaggerfallItemEnchantmentQuote QuoteEnchantment(UniqueInventoryItem item, string magicItemKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magicItemKey);
        (_, DaggerfallItemInstanceMetadata metadata) = RequirePlayerItem(item);
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(metadata.ItemId));
        // The quotation entry point answers for both kinds of enchantment an item can receive, exactly as
        // the action that applies them does.
        if (DaggerfallEnchantmentSettings.TryResolve(magicItemKey, out DaggerfallEnchantmentSetting setting))
            return DaggerfallMagicCostPolicy.QuoteItemEnchantment(definition, metadata, setting);
        DaggerfallMagicItemDefinition magic = definitions.Magic.MagicItems.TryGetValue(magicItemKey, out DaggerfallMagicItemDefinition? found)
            ? found : throw new InvalidOperationException($"Magic item '{magicItemKey}' is not published.");
        return DaggerfallMagicCostPolicy.QuoteItemEnchantment(definitions, definition, metadata, magic);
    }

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
            return new(DaggerfallItemConditionOutcome.AlreadyBroken, durableItemId, metadata, metadata.CurrentCondition);

        int current = Math.Max(0, checked(metadata.CurrentCondition - units));
        DaggerfallEquipmentChange? removed = null;
        if (current == 0)
        {
            removed = removeBroken(item);
        }
        DaggerfallItemInstanceMetadata changed = metadata with { CurrentCondition = current };
        instances.ReplaceUnique(durableItemId, changed);
        if (current != 0)
            return new(DaggerfallItemConditionOutcome.Damaged, durableItemId, changed, metadata.CurrentCondition);
        return new(DaggerfallItemConditionOutcome.Broken, durableItemId, changed, metadata.CurrentCondition, removed);
    }

    /// <summary>Restores an existing condition-bearing item to its authored maximum without changing durable identity.</summary>
    internal DaggerfallItemConditionResult Repair(UniqueInventoryItem item)
    {
        (ulong durableItemId, DaggerfallItemInstanceMetadata metadata) = RequirePlayerItem(item);
        if (metadata.MaximumCondition == 0)
            throw new InvalidOperationException($"Item '{metadata.ItemId}' has no condition units to repair.");
        if (metadata.CurrentCondition == metadata.MaximumCondition)
            return new(DaggerfallItemConditionOutcome.AlreadyRepaired, durableItemId, metadata, metadata.CurrentCondition);
        DaggerfallItemInstanceMetadata repaired = metadata with { CurrentCondition = metadata.MaximumCondition };
        instances.ReplaceUnique(durableItemId, repaired);
        return new(DaggerfallItemConditionOutcome.Repaired, durableItemId, repaired, metadata.CurrentCondition);
    }

    /// <summary>Restores a bounded number of fuel condition units without changing durable identity.</summary>
    internal DaggerfallItemConditionResult Refuel(UniqueInventoryItem item, int units)
    {
        if (units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
        (ulong durableItemId, DaggerfallItemInstanceMetadata metadata) = RequirePlayerItem(item);
        if (metadata.MaximumCondition == 0)
            throw new InvalidOperationException($"Item '{metadata.ItemId}' has no condition units to refuel.");
        if (metadata.CurrentCondition == metadata.MaximumCondition)
            return new(DaggerfallItemConditionOutcome.AlreadyRepaired, durableItemId, metadata, metadata.CurrentCondition);
        DaggerfallItemInstanceMetadata refueled = metadata with
        {
            CurrentCondition = Math.Min(metadata.MaximumCondition, checked(metadata.CurrentCondition + units)),
        };
        instances.ReplaceUnique(durableItemId, refueled);
        return new(DaggerfallItemConditionOutcome.Repaired, durableItemId, refueled, metadata.CurrentCondition);
    }

    /// <summary>Discloses the published magic template for an existing enchanted instance without changing its identity.</summary>
    internal DaggerfallItemConditionResult Identify(UniqueInventoryItem item)
    {
        (ulong durableItemId, DaggerfallItemInstanceMetadata metadata) = RequirePlayerItem(item);
        if (metadata.Enchantment is null || metadata.Identified)
            return new(DaggerfallItemConditionOutcome.AlreadyIdentified, durableItemId, metadata, metadata.CurrentCondition);
        // A setting has no published template to disclose, so it is identified by its own param meaning;
        // anything else must still name a published magic item.
        if (!DaggerfallEnchantmentSettings.TryResolve(metadata.Enchantment, out _)) RequireMagic(metadata);
        DaggerfallItemInstanceMetadata identified = metadata with { Identified = true };
        instances.ReplaceUnique(durableItemId, identified);
        return new(DaggerfallItemConditionOutcome.Identified, durableItemId, identified, metadata.CurrentCondition);
    }

    /// <summary>
    /// Applies a published magic template to an ordinary live item. The Engine retains the item's
    /// structural definition; metadata owns the added Daggerfall policy, donor uses, and disclosure.
    /// </summary>
    internal DaggerfallItemConditionResult Enchant(UniqueInventoryItem item, string magicItemKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magicItemKey);
        (ulong durableItemId, DaggerfallItemInstanceMetadata metadata) = RequirePlayerItem(item);
        if (metadata.Enchantment is { } existing)
        {
            if (existing == magicItemKey) return new(DaggerfallItemConditionOutcome.AlreadyEnchanted, durableItemId, metadata, metadata.CurrentCondition);
            throw new InvalidOperationException($"Item '{metadata.ItemId}' already has enchantment '{existing}'.");
        }
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(metadata.ItemId));
        // A published magic item brings its own template, uses and published identity; an item maker's
        // setting brings only its donor cost, so the item keeps its condition and its own identity.
        DaggerfallMagicItemDefinition? magic = null;
        DaggerfallItemEnchantmentQuote quote;
        if (DaggerfallEnchantmentSettings.TryResolve(magicItemKey, out DaggerfallEnchantmentSetting setting))
        {
            quote = DaggerfallMagicCostPolicy.QuoteItemEnchantment(definition, metadata, setting);
        }
        else
        {
            magic = definitions.Magic.MagicItems.TryGetValue(magicItemKey, out DaggerfallMagicItemDefinition? found)
                ? found : throw new InvalidOperationException($"Magic item '{magicItemKey}' is not published.");
            quote = DaggerfallMagicCostPolicy.QuoteItemEnchantment(definitions, definition, metadata, magic);
        }
        if (!quote.Eligible)
            throw new InvalidOperationException($"Enchantment '{magicItemKey}' cannot enchant item '{metadata.ItemId}': {quote.Reason}");
        if (magic is not null)
        {
            string publishedMagicDefinition = DaggerfallMagicItemIds.For(metadata.ItemId, magic.Key);
            if (!definitions.TryResolveItem(new DaggerfallItemId(publishedMagicDefinition), out _))
                throw new InvalidOperationException($"Magic item '{magic.Key}' cannot enchant item definition '{metadata.ItemId}'.");
        }
        EquipmentMoveResult unequipped = equipment.UnequipForEnchantment(item);
        if (unequipped.Outcome != EquipmentMoveOutcome.Applied)
            throw new InvalidOperationException($"Item '{metadata.ItemId}' could not be unequipped before enchanting: {unequipped.Detail}");
        DaggerfallItemInstanceMetadata enchanted = magic is null
            ? metadata with { Enchantment = magicItemKey, Identified = true }
            : metadata with
            {
                Enchantment = magic.Key,
                Identified = true,
                CurrentCondition = magic.Uses,
                MaximumCondition = magic.Uses,
            };
        instances.ReplaceUnique(durableItemId, enchanted);
        return new(DaggerfallItemConditionOutcome.Enchanted, durableItemId, enchanted, metadata.CurrentCondition, unequipped.Change);
    }

    internal DaggerfallItemCondition Condition(DaggerfallItemInstanceMetadata metadata) =>
        new(metadata.CurrentCondition, metadata.MaximumCondition,
            DaggerfallFormulaPolicy.ConditionPercentage(metadata.CurrentCondition, metadata.MaximumCondition));

    private void RequireItemMetadata(DaggerfallItemInstanceMetadata metadata)
    {
        if (!definitions.TryResolveItem(new DaggerfallItemId(metadata.ItemId), out _))
            throw new InvalidOperationException($"Item instance names unpublished definition '{metadata.ItemId}'.");
        // An item maker's setting is a legitimate enchantment that has no published magic item, so it is
        // not held to the published-template requirement here; anything else still is.
        if (metadata.Enchantment is { } enchantment && !DaggerfallEnchantmentSettings.TryResolve(enchantment, out _))
            _ = RequireMagic(metadata);
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
