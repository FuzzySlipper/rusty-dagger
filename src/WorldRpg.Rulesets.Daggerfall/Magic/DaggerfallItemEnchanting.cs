using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using UniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Enchanting a live item the player holds: a published magic template, or item-maker settings committed as a made
/// enchantment, with the soul a soul-bound setting consumes and the weight its settings give. The Engine keeps the
/// item's structural definition; its metadata gains the enchantment. Condition and identification stay
/// <see cref="DaggerfallItemConditionService"/>'s.
/// </summary>
internal sealed class DaggerfallItemEnchanting(
    DaggerfallDefinitions definitions,
    DaggerfallItemInstances instances,
    DaggerfallEquipmentMoves equipment,
    DaggerfallItemConditionService conditions,
    MechanicsInventoryCoordinator? inventory = null, Func<DaggerfallSoulGems>? souls = null)
{
    internal event Action<ulong>? Enchanted;

    /// <summary>Returns the item-maker's pure capacity and spell-cost quotation without changing item condition or equipment.</summary>
    internal DaggerfallItemEnchantmentQuote QuoteEnchantment(UniqueInventoryItem item, string magicItemKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magicItemKey);
        (_, DaggerfallItemInstanceMetadata metadata) = conditions.RequirePlayerItem(item);
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(metadata.ItemId));
        // The quotation entry point answers for both kinds of enchantment an item can receive, exactly as
        // the action that applies them does.
        if (definitions.Magic.EnchantmentSettings.TryGetValue(magicItemKey, out DaggerfallEnchantmentSetting setting))
            return DaggerfallMagicCostPolicy.QuoteItemEnchantment(definition, metadata, setting);
        DaggerfallMagicItemDefinition magic = definitions.Magic.MagicItems.TryGetValue(magicItemKey, out DaggerfallMagicItemDefinition? found)
            ? found : throw new InvalidOperationException($"Magic item '{magicItemKey}' is not published.");
        return DaggerfallMagicCostPolicy.QuoteItemEnchantment(definitions, definition, metadata, magic);
    }

    /// <summary>
    /// Applies a published magic template, or one item-maker setting, to an ordinary live item. A
    /// published template brings its own uses and identity; a setting is committed as a made
    /// enchantment, the one representation the item maker produces. The Engine retains the item's
    /// structural definition; metadata owns the added Daggerfall policy, donor uses, and disclosure.
    /// </summary>
    internal DaggerfallItemConditionResult Enchant(UniqueInventoryItem item, string magicItemKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(magicItemKey);
        (ulong durableItemId, DaggerfallItemInstanceMetadata metadata) = conditions.RequirePlayerItem(item);
        bool setting = definitions.Magic.EnchantmentSettings.ContainsKey(magicItemKey);
        if (metadata.MadeEnchantment is not null) throw new InvalidOperationException("This item is already enchanted.");
        if (setting)
        {
            DaggerfallMadeEnchantmentQuote quoted;
            try { quoted = DaggerfallEnchantmentConstruction.Quote(definitions, metadata, definitions.RequireItem(new(metadata.ItemId)).Template!.Name, [magicItemKey]); }
            catch (ArgumentException refused)
            { throw new InvalidOperationException($"Enchantment '{magicItemKey}' cannot enchant item '{metadata.ItemId}': {refused.Message}", refused); }
            return EnchantMade(item, quoted.Enchantment);
        }
        if (metadata.Enchantment is { } existing)
        {
            if (existing == magicItemKey) return new(DaggerfallItemConditionOutcome.AlreadyEnchanted, durableItemId, metadata, metadata.CurrentCondition);
            throw new InvalidOperationException($"Item '{metadata.ItemId}' already has enchantment '{existing}'.");
        }
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(metadata.ItemId));
        DaggerfallMagicItemDefinition magic = definitions.Magic.MagicItems.TryGetValue(magicItemKey, out DaggerfallMagicItemDefinition? found)
            ? found : throw new InvalidOperationException($"Magic item '{magicItemKey}' is not published.");
        DaggerfallItemEnchantmentQuote quote = DaggerfallMagicCostPolicy.QuoteItemEnchantment(definitions, definition, metadata, magic);
        if (!quote.Eligible)
            throw new InvalidOperationException($"Enchantment '{magicItemKey}' cannot enchant item '{metadata.ItemId}': {quote.Reason}");
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
        Enchanted?.Invoke(durableItemId);
        enchanted = instances.RequireUnique(durableItemId);
        return new(DaggerfallItemConditionOutcome.Enchanted, durableItemId, enchanted, metadata.CurrentCondition, unequipped.Change);
    }

    /// <summary>Commits fully resolved maker settings once, retaining item identity and current condition.</summary>
    internal DaggerfallItemConditionResult EnchantMade(UniqueInventoryItem item, DaggerfallMadeEnchantment made) =>
        EnchantMadeCore(item, made, null)!;

    /// <summary>Finishes a newly granted item whose final capacity costs already published with payment.</summary>
    internal DaggerfallItemConditionResult CompleteGrantedEnchantment(UniqueInventoryItem item, DaggerfallMadeEnchantment made) =>
        EnchantMadeCore(item, made, null, capacityAlreadyApplied: true)!;

    internal DaggerfallItemConditionResult? TryEnchantMade(UniqueInventoryItem item, DaggerfallMadeEnchantment made,
        Func<Action<InventoryEdit>, bool> publish) => EnchantMadeCore(item, made, publish);

    private DaggerfallItemConditionResult? EnchantMadeCore(UniqueInventoryItem item, DaggerfallMadeEnchantment made,
        Func<Action<InventoryEdit>, bool>? publish, bool capacityAlreadyApplied = false)
    {
        (ulong id, var metadata) = conditions.RequirePlayerItem(item);
        if (metadata.HasEnchantment) throw new InvalidOperationException("This item is already enchanted.");
        // Validate the complete payload before consuming the selected soul or changing weight.
        var quote = DaggerfallEnchantmentConstruction.Quote(definitions, metadata, made.Name,
            made.Settings.Where(value => value.Parent is null).Select(value => value.Key));
        if (!quote.Enchantment.Settings.SequenceEqual(made.Settings)) throw new ArgumentException("Made settings differ from their forced children.");
        var settings = made.Settings.Select(value => definitions.Magic.EnchantmentSettings[value.Key]).ToArray();
        int? soul = settings.Where(value => value.Type == 15).Select(value => (int?)value.Param).SingleOrDefault();
        DaggerfallSoulGems? gems = soul is not null ? souls?.Invoke() : null;
        if (soul is int mobile && gems?.HasSoul(mobile) != true) throw new InvalidOperationException("No matching filled soul trap remains.");
        ulong? weight = DaggerfallEnchantmentConstruction.EnchantedWeight(definitions, metadata, made);
        var enchanted = (metadata with { MadeEnchantment = quote.Enchantment, Identified = true, WeightClassicUnits = weight }).Validate();
        if (publish is not null)
        {
            if (!publish(candidate =>
            {
                if (weight != metadata.WeightClassicUnits)
                    candidate.SetCapacityCosts(new(item.EntityId), DaggerfallEncumbrancePolicy.CapacityOverride(weight));
            })) return null;
        }
        else if (!capacityAlreadyApplied && weight != metadata.WeightClassicUnits)
            (inventory ?? throw new InvalidOperationException("Item mutation requires the live inventory owner."))
                .SetCapacityCosts(item, DaggerfallEncumbrancePolicy.CapacityOverride(weight));
        var moved = equipment.UnequipForEnchantment(item);
        if (moved.Outcome != EquipmentMoveOutcome.Applied) throw new InvalidOperationException($"Could not unequip the item: {moved.Detail}");
        if (soul is int consumed && !gems!.Consume(consumed)) throw new InvalidOperationException("The selected soul is no longer available.");
        instances.ReplaceUnique(id, enchanted);
        Enchanted?.Invoke(id);
        return new(DaggerfallItemConditionOutcome.Enchanted, id, instances.RequireUnique(id), metadata.CurrentCondition, moved.Change);
    }
}
