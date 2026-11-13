using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using UniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;
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
/// Daggerfall-owned mutations of persisted item condition and identification. Enchanting is Magic's
/// (<see cref="DaggerfallItemEnchanting"/>).
/// Engine inventory and equipment continue to own containment and equipment relationship state.
/// </summary>
internal sealed class DaggerfallItemConditionService(
    DaggerfallDefinitions definitions,
    DaggerfallItemInstances instances,
    DaggerfallEquipmentMoves equipment)
{
    internal event Action<ulong>? Broken;

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
            return new(DaggerfallItemConditionOutcome.AlreadyBroken, durableItemId, metadata, metadata.CurrentCondition);

        int current = Math.Max(0, checked(metadata.CurrentCondition - units));
        DaggerfallEquipmentChange? removed = null;
        if (current == 0)
        {
            removed = removeBroken(item);
        }
        bool soulBreak = current == 0 && !metadata.BoundSoulReleased && definitions.Magic.TryEnchantments(metadata, out var payloads) && payloads.Any(value => value.Type == 15);
        DaggerfallItemInstanceMetadata changed = metadata with { CurrentCondition = current, HeldCast = current == 0 ? null : metadata.HeldCast, BoundSoulReleasePending = soulBreak || metadata.BoundSoulReleasePending };
        instances.ReplaceUnique(durableItemId, changed);
        if (current != 0)
            return new(DaggerfallItemConditionOutcome.Damaged, durableItemId, changed, metadata.CurrentCondition);
        Broken?.Invoke(durableItemId);
        return new(DaggerfallItemConditionOutcome.Broken, durableItemId, instances.RequireUnique(durableItemId), metadata.CurrentCondition, removed);
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

    /// <summary>
    /// Repairs an item held by an admitted provider custody owner. Containment remains the Kit
    /// container authority; this overload keeps the Daggerfall condition mutation in this owner
    /// while allowing a queued repair to finish before the item returns to the player.
    /// </summary>
    internal DaggerfallItemConditionResult Repair(ulong durableItemId, DaggerfallItemOwner owner)
    {
        owner.Validate();
        DaggerfallItemInstanceMetadata metadata = instances.RequireUnique(durableItemId);
        if (metadata.Owner != owner)
            throw new InvalidOperationException($"Item '{durableItemId}' belongs to {metadata.Owner.Scope} {metadata.Owner.Id}, not {owner.Scope} {owner.Id}.");
        RequireItemMetadata(metadata);
        if (metadata.MaximumCondition == 0)
            throw new InvalidOperationException($"Item '{metadata.ItemId}' has no condition units to repair.");
        if (metadata.CurrentCondition == metadata.MaximumCondition)
            return new(DaggerfallItemConditionOutcome.AlreadyRepaired, durableItemId, metadata, metadata.CurrentCondition);
        DaggerfallItemInstanceMetadata repaired = metadata with { CurrentCondition = metadata.MaximumCondition };
        instances.ReplaceUnique(durableItemId, repaired);
        return new(DaggerfallItemConditionOutcome.Repaired, durableItemId, repaired, metadata.CurrentCondition);
    }

    /// <summary>
    /// Raises condition by bounded units up to the authored maximum, without changing durable identity.
    /// Unlike <see cref="Repair"/>, which restores an item whole, this is the steady restoration a worn
    /// enchantment performs on the round cadence.
    /// </summary>
    internal DaggerfallItemConditionResult Restore(UniqueInventoryItem item, int units)
    {
        if (units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
        (ulong durableItemId, DaggerfallItemInstanceMetadata metadata) = RequirePlayerItem(item);
        if (metadata.MaximumCondition == 0)
            throw new InvalidOperationException($"Item '{metadata.ItemId}' has no condition units to restore.");
        long restored = Math.Min(checked((long)metadata.MaximumCondition), checked((long)metadata.CurrentCondition + units));
        if (restored == metadata.CurrentCondition)
            return new(DaggerfallItemConditionOutcome.AlreadyRepaired, durableItemId, metadata, metadata.CurrentCondition);
        DaggerfallItemInstanceMetadata updated = metadata with { CurrentCondition = checked((int)restored) };
        instances.ReplaceUnique(durableItemId, updated);
        return new(DaggerfallItemConditionOutcome.Repaired, durableItemId, updated, metadata.CurrentCondition);
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
        if (!metadata.HasEnchantment || metadata.Identified)
            return new(DaggerfallItemConditionOutcome.AlreadyIdentified, durableItemId, metadata, metadata.CurrentCondition);
        // A made enchantment discloses its own settings; the magic-item field names only a published item.
        if (metadata.Enchantment is not null) RequireMagic(metadata);
        DaggerfallItemInstanceMetadata identified = metadata with { Identified = true };
        instances.ReplaceUnique(durableItemId, identified);
        return new(DaggerfallItemConditionOutcome.Identified, durableItemId, identified, metadata.CurrentCondition);
    }

    /// <summary>Discloses an item while it is in provider custody, preserving its durable identity.</summary>
    internal DaggerfallItemConditionResult Identify(ulong durableItemId, DaggerfallItemOwner owner)
    {
        owner.Validate();
        DaggerfallItemInstanceMetadata metadata = instances.RequireUnique(durableItemId);
        if (metadata.Owner != owner)
            throw new InvalidOperationException($"Item '{durableItemId}' belongs to {metadata.Owner.Scope} {metadata.Owner.Id}, not {owner.Scope} {owner.Id}.");
        RequireItemMetadata(metadata);
        if (!metadata.HasEnchantment || metadata.Identified)
            return new(DaggerfallItemConditionOutcome.AlreadyIdentified, durableItemId, metadata, metadata.CurrentCondition);
        if (metadata.Enchantment is not null) RequireMagic(metadata);
        DaggerfallItemInstanceMetadata identified = metadata with { Identified = true };
        instances.ReplaceUnique(durableItemId, identified);
        return new(DaggerfallItemConditionOutcome.Identified, durableItemId, identified, metadata.CurrentCondition);
    }

    internal DaggerfallItemCondition Condition(DaggerfallItemInstanceMetadata metadata) =>
        new(metadata.CurrentCondition, metadata.MaximumCondition,
            DaggerfallFormulaPolicy.ConditionPercentage(metadata.CurrentCondition, metadata.MaximumCondition));

    private void RequireItemMetadata(DaggerfallItemInstanceMetadata metadata)
    {
        if (!definitions.TryResolveItem(new DaggerfallItemId(metadata.ItemId), out _))
            throw new InvalidOperationException($"Item instance names unpublished definition '{metadata.ItemId}'.");
        // Item-maker settings travel only as a made enchantment; the magic-item field names a published item.
        if (metadata.Enchantment is not null) _ = RequireMagic(metadata);
    }

    private void RequireItemMetadata(UniqueInventoryItem item, DaggerfallItemInstanceMetadata metadata)
    {
        if (!string.Equals(item.Definition.Value, metadata.ItemId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Live item definition '{item.Definition.Value}' does not match durable metadata '{metadata.ItemId}'.");
        RequireItemMetadata(metadata);
    }

    /// <summary>The durable identity and meaning of a live item the player holds, checked against its definition.</summary>
    internal (ulong DurableItemId, DaggerfallItemInstanceMetadata Metadata) RequirePlayerItem(UniqueInventoryItem item)
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
