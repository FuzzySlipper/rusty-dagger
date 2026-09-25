using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallTradeSide
{
    BuyFromMerchant,
    SellToMerchant,
}

internal enum DaggerfallTradeQuoteFailure
{
    None,
    EmptySelection,
    InvalidInput,
    InvalidItemMetadata,
    Overflow,
}

/// <summary>A selected Engine-backed inventory row and its current Daggerfall meaning.</summary>
internal sealed record DaggerfallTradeLine(
    DaggerfallItemDefinition Definition,
    DaggerfallItemInstanceMetadata Metadata,
    ulong Quantity);

/// <summary>The calculated shop subtotal and final player price. A quote does not commit a transaction.</summary>
internal sealed record DaggerfallTradeQuote(
    DaggerfallTradeSide Side,
    int? Region,
    int ShopQuality,
    int ShopSubtotal,
    int Total,
    IReadOnlyList<DaggerfallTradeLine> Lines);

internal sealed record DaggerfallTradeQuoteResult(
    DaggerfallTradeQuote? Quote,
    DaggerfallTradeQuoteFailure Failure,
    string? Detail)
{
    internal bool Accepted => Quote is not null && Failure == DaggerfallTradeQuoteFailure.None;

    internal static DaggerfallTradeQuoteResult Rejected(DaggerfallTradeQuoteFailure failure, string detail) =>
        new(null, failure, detail);
}

/// <summary>
/// The product's buy/sell quote caller. It reads caller-selected Engine inventory rows and item
/// metadata, calculates a price, and leaves inventory and currency mutation to their owners.
/// </summary>
internal sealed class DaggerfallTradeQuoteService
{
    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallItemValuation _valuation;
    private readonly DaggerfallRegionalPriceState _regionalPrices;

    internal DaggerfallTradeQuoteService(DaggerfallDefinitions definitions, DaggerfallItemValuation valuation, DaggerfallRegionalPriceState regionalPrices)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _valuation = valuation ?? throw new ArgumentNullException(nameof(valuation));
        _regionalPrices = regionalPrices ?? throw new ArgumentNullException(nameof(regionalPrices));
    }

    internal DaggerfallTradeQuoteResult Quote(DaggerfallTradeSide side, IReadOnlyList<DaggerfallTradeLine> lines,
        int shopQuality, int playerMercantile, int playerPersonality, int reaction, int? region)
    {
        if (side is not (DaggerfallTradeSide.BuyFromMerchant or DaggerfallTradeSide.SellToMerchant))
            return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidInput, "Trade side is not supported.");
        if (lines is null || lines.Count == 0)
            return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.EmptySelection, "A quote requires at least one selected item row.");
        if (shopQuality is < DaggerfallRegionalEconomyPolicy.MinimumShopQuality or > DaggerfallRegionalEconomyPolicy.MaximumShopQuality)
            return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidInput, "Shop quality is outside 0..20.");
        if (playerMercantile is < 0 or > 100 || playerPersonality is < 0 or > 100
            || reaction is < DaggerfallRegionalEconomyPolicy.MinimumReaction or > DaggerfallRegionalEconomyPolicy.MaximumReaction)
            return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidInput, "Mercantile, personality, or reaction is outside its validated range.");
        if (region is int selectedRegion && (selectedRegion < 0 || selectedRegion >= DaggerfallRegionalEconomyPolicy.RegionCount))
            return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidInput, "Region is outside the admitted classic range.");

        long subtotal = 0;
        try
        {
            foreach (DaggerfallTradeLine? line in lines)
            {
                if (line?.Definition is null || line.Metadata is null || line.Quantity == 0)
                    return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidInput, "Every quote row requires an item, metadata, and positive quantity.");
                if (line.Quantity > line.Definition.MaximumQuantity)
                    return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidInput,
                        $"Quantity {line.Quantity} exceeds the admitted maximum {line.Definition.MaximumQuantity} for '{line.Definition.Id.Value}'.");

                DaggerfallItemInstanceMetadata metadata = line.Metadata.Validate();
                int itemValue = TradeValue(line.Definition, metadata);
                if (metadata.MaximumCondition > 0)
                    itemValue = checked((int)(checked((long)itemValue * metadata.CurrentCondition) / metadata.MaximumCondition));

                int regionFactor = region is int regionIndex ? _regionalPrices.AdjustmentForRegion(regionIndex) : 0;
                int unitCost = DaggerfallRegionalEconomyPolicy.CalculateCost(itemValue, shopQuality, region, regionFactor);
                long lineCost = checked((long)unitCost * checked((long)line.Quantity));
                subtotal = checked(subtotal + lineCost);
                if (subtotal > int.MaxValue)
                    return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.Overflow, "The selected items exceed the supported shop subtotal.");
            }

            int shopSubtotal = checked((int)subtotal);
            bool selling = side == DaggerfallTradeSide.SellToMerchant;
            int total = DaggerfallRegionalEconomyPolicy.CalculateTradePrice(shopSubtotal, shopQuality, selling,
                playerMercantile, playerPersonality, reaction);
            DaggerfallTradeQuote quote = new(side, region, shopQuality, shopSubtotal, total, Array.AsReadOnly(lines.ToArray()));
            return new(quote, DaggerfallTradeQuoteFailure.None, null);
        }
        catch (OverflowException exception)
        {
            return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.Overflow, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidInput, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidItemMetadata, exception.Message);
        }
    }

    private int TradeValue(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata metadata)
    {
        // DFU's current quote caller uses item.value regardless of identification. WorldRpg requires
        // unidentified enchantments to retain the ordinary item's source value until identification.
        if (metadata.Enchantment is { } enchantment && !metadata.Identified)
        {
            if (!StringComparer.Ordinal.Equals(definition.Id.Value, metadata.ItemId))
                throw new InvalidOperationException($"Item metadata '{metadata.ItemId}' does not belong to '{definition.Id.Value}'.");
            // An item the item maker enchanted carries one of its settings, which has no published magic
            // item by design; it keeps the ordinary item's value until identification for the same reason
            // an unidentified published enchantment does.
            if (DaggerfallEnchantmentSettings.TryResolve(enchantment, out _)) return definition.Value;
            if (!_definitions.Magic.MagicItems.ContainsKey(enchantment))
                throw new InvalidOperationException($"Item '{definition.Id.Value}' names unpublished magic metadata '{enchantment}'.");

            string suffix = $"-magic-{enchantment.Replace('.', '-')}";
            if (definition.Id.Value.Contains("-magic-", StringComparison.Ordinal)
                && !definition.Id.Value.EndsWith(suffix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Item '{definition.Id.Value}' has an invalid magic item identity for '{enchantment}'.");
            if (!definition.Id.Value.EndsWith(suffix, StringComparison.Ordinal))
            {
                string expectedMagicId = DaggerfallMagicItemIds.For(definition.Id.Value, enchantment);
                if (!_definitions.TryResolveItem(new DaggerfallItemId(expectedMagicId), out _))
                    throw new InvalidOperationException($"Item '{definition.Id.Value}' cannot carry published enchantment '{enchantment}'.");
                return definition.Value;
            }

            string baseItemId = definition.Id.Value[..^suffix.Length];
            if (!_definitions.TryResolveItem(new DaggerfallItemId(baseItemId), out DaggerfallItemDefinition baseItem))
                throw new InvalidOperationException($"Unidentified enchanted item '{definition.Id.Value}' has no published ordinary base item '{baseItemId}'.");
            return baseItem.Value;
        }
        return _valuation.CurrentValue(definition, metadata);
    }
}
