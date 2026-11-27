using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;

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

/// <summary>The calculated shop subtotal, final player price, and exact line-by-line price breakdown. A quote does not commit a transaction.</summary>
internal sealed record DaggerfallTradeQuote(
    DaggerfallTradeSide Side,
    int? Region,
    int ShopQuality,
    int ShopSubtotal,
    int Total,
    IReadOnlyList<DaggerfallTradeLine> Lines,
    DaggerfallPriceBreakdown? Breakdown = null);

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
/// metadata, calculates a price and its exact breakdown in one pass, and leaves inventory and currency
/// mutation to their owners.
/// </summary>
internal sealed class DaggerfallTradeQuoteService
{
    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallItemValuation _valuation;
    private readonly DaggerfallRegionalPriceState _regionalPrices;
    private readonly Func<int, IReadOnlyList<DaggerfallGoodsPriceFactor>>? _regionalConditionFactors;

    internal DaggerfallTradeQuoteService(DaggerfallDefinitions definitions, DaggerfallItemValuation valuation, DaggerfallRegionalPriceState regionalPrices,
        Func<int, int>? regionalConditionSurcharge = null,
        Func<int, IReadOnlyList<DaggerfallGoodsPriceFactor>>? regionalConditionFactors = null)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _valuation = valuation ?? throw new ArgumentNullException(nameof(valuation));
        _regionalPrices = regionalPrices ?? throw new ArgumentNullException(nameof(regionalPrices));
        _regionalConditionFactors = regionalConditionFactors
            ?? (regionalConditionSurcharge is null
                ? null
                : region =>
                {
                    int delta = regionalConditionSurcharge(region);
                    if (delta == 0)
                        return [];
                    string regionLabel = RegionLabel(region);
                    string reason = string.IsNullOrEmpty(regionLabel) ? "regional conditions" : $"regional conditions in {regionLabel}";
                    return [new DaggerfallGoodsPriceFactor(reason, delta, 1)];
                });
    }

    internal DaggerfallTradeQuoteResult Quote(DaggerfallTradeSide side, IReadOnlyList<DaggerfallTradeLine> lines,
        int shopQuality, int playerMercantile, int playerPersonality, int reaction, int? region, int? originRegion = null,
        DaggerfallTradeReactionBreakdown? reactionBreakdown = null)
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
        if (originRegion is int selectedOrigin && (selectedOrigin < 0 || selectedOrigin >= DaggerfallRegionalEconomyPolicy.RegionCount))
            return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidInput, "Origin region is outside the admitted classic range.");

        static int ClampFactor(int factor) => Math.Clamp(
            factor,
            DaggerfallRegionalEconomyPolicy.MinimumRegionalAdjustment,
            DaggerfallRegionalEconomyPolicy.MaximumRegionalAdjustment);

        IReadOnlyList<DaggerfallGoodsPriceFactor> ConditionFactors(int regionIndex) =>
            _regionalConditionFactors?.Invoke(regionIndex) ?? [];

        int ConditionSum(IReadOnlyList<DaggerfallGoodsPriceFactor> factors)
        {
            int sum = 0;
            foreach (DaggerfallGoodsPriceFactor factor in factors)
                sum += factor.FactorDelta;
            return sum;
        }

        // Build ordered regional-factor stages once so both the final subtotal and the line-by-line
        // breakdown step through the exact same regional price factor arithmetic.
        List<(string Reason, string Detail, int TargetFactor)> regionalSteps = [];
        int finalRegionFactor = 0;
        if (region is int regionIndex)
        {
            int localClassic = _regionalPrices.AdjustmentForRegion(regionIndex);
            IReadOnlyList<DaggerfallGoodsPriceFactor> localConditions = ConditionFactors(regionIndex);
            int localEffective = ClampFactor(localClassic + ConditionSum(localConditions));

            if (originRegion is int caravanOrigin && caravanOrigin != regionIndex)
            {
                int originClassic = _regionalPrices.AdjustmentForRegion(caravanOrigin);
                IReadOnlyList<DaggerfallGoodsPriceFactor> originConditions = ConditionFactors(caravanOrigin);
                int originEffective = ClampFactor(originClassic + ConditionSum(originConditions));
                finalRegionFactor = ClampFactor((localEffective + originEffective) / 2);

                int runningLocal = DaggerfallRegionalEconomyPolicy.NeutralRegionalAdjustment;
                int runningOrigin = DaggerfallRegionalEconomyPolicy.NeutralRegionalAdjustment;
                int classicMid = ClampFactor((localClassic + originClassic) / 2);
                if (classicMid != DaggerfallRegionalEconomyPolicy.NeutralRegionalAdjustment)
                {
                    regionalSteps.Add((
                        $"caravan markets ({RegionLabel(originRegion.Value)} & {RegionLabel(regionIndex)})",
                        DaggerfallPriceBreakdown.FormatSignedPercentFromThousandths(classicMid - DaggerfallRegionalEconomyPolicy.NeutralRegionalAdjustment),
                        classicMid));
                }
                runningLocal = localClassic;
                runningOrigin = originClassic;

                foreach (DaggerfallGoodsPriceFactor factor in localConditions)
                {
                    runningLocal += factor.FactorDelta;
                    int nextMid = ClampFactor((ClampFactor(runningLocal) + ClampFactor(runningOrigin)) / 2);
                    regionalSteps.Add((
                        $"{factor.Reason} (caravan share)",
                        DaggerfallPriceBreakdown.FormatSignedPercentFromThousandths(factor.FactorDelta / 2),
                        nextMid));
                }
                foreach (DaggerfallGoodsPriceFactor factor in originConditions)
                {
                    runningOrigin += factor.FactorDelta;
                    int nextMid = ClampFactor((ClampFactor(runningLocal) + ClampFactor(runningOrigin)) / 2);
                    regionalSteps.Add((
                        $"{factor.Reason} (caravan origin)",
                        DaggerfallPriceBreakdown.FormatSignedPercentFromThousandths(factor.FactorDelta / 2),
                        nextMid));
                }
            }
            else
            {
                finalRegionFactor = localEffective;
                int running = DaggerfallRegionalEconomyPolicy.NeutralRegionalAdjustment;
                if (localClassic != DaggerfallRegionalEconomyPolicy.NeutralRegionalAdjustment)
                {
                    running = localClassic;
                    string place = RegionLabel(regionIndex);
                    string label = string.IsNullOrEmpty(place) ? "regional market" : $"regional market in {place}";
                    regionalSteps.Add((
                        label,
                        DaggerfallPriceBreakdown.FormatSignedPercentFromThousandths(localClassic - DaggerfallRegionalEconomyPolicy.NeutralRegionalAdjustment),
                        ClampFactor(running)));
                }
                else
                {
                    running = localClassic;
                }
                foreach (DaggerfallGoodsPriceFactor factor in localConditions)
                {
                    running += factor.FactorDelta;
                    regionalSteps.Add((
                        factor.Reason,
                        DaggerfallPriceBreakdown.FormatSignedPercentFromThousandths(factor.FactorDelta),
                        ClampFactor(running)));
                }
            }
        }

        try
        {
            List<(int UnwornValue, int WornValue, long Quantity, int CurCondition, int MaxCondition)> pricedLines = [];
            long unwornTotal = 0;
            long wornTotal = 0;
            foreach (DaggerfallTradeLine? line in lines)
            {
                if (line?.Definition is null || line.Metadata is null || line.Quantity == 0)
                    return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidInput, "Every quote row requires an item, metadata, and positive quantity.");
                if (line.Quantity > line.Definition.MaximumQuantity)
                    return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidInput,
                        $"Quantity {line.Quantity} exceeds the admitted maximum {line.Definition.MaximumQuantity} for '{line.Definition.Id.Value}'.");

                DaggerfallItemInstanceMetadata metadata = line.Metadata.Validate();
                int unwornValue = TradeValue(line.Definition, metadata);
                int itemValue = metadata.MaximumCondition > 0
                    ? checked((int)(checked((long)unwornValue * metadata.CurrentCondition) / metadata.MaximumCondition))
                    : unwornValue;
                long qty = checked((long)line.Quantity);
                pricedLines.Add((unwornValue, itemValue, qty, metadata.CurrentCondition, metadata.MaximumCondition));
                unwornTotal = checked(unwornTotal + checked((long)unwornValue * qty));
                wornTotal = checked(wornTotal + checked((long)itemValue * qty));
            }

            int SubtotalAtFactor(int? targetRegion, int factor)
            {
                long sum = 0;
                foreach (var item in pricedLines)
                {
                    int unitCost = DaggerfallRegionalEconomyPolicy.CalculateCost(item.WornValue, shopQuality, targetRegion, factor);
                    sum = checked(sum + checked((long)unitCost * item.Quantity));
                    if (sum > int.MaxValue)
                        throw new OverflowException("The selected items exceed the supported shop subtotal.");
                }
                return checked((int)sum);
            }

            int shopNeutral = SubtotalAtFactor(region, DaggerfallRegionalEconomyPolicy.NeutralRegionalAdjustment);
            int shopSubtotal = region is null
                ? shopNeutral
                : SubtotalAtFactor(region, finalRegionFactor);

            bool selling = side == DaggerfallTradeSide.SellToMerchant;
            int total = DaggerfallRegionalEconomyPolicy.CalculateTradePrice(shopSubtotal, shopQuality, selling,
                playerMercantile, playerPersonality, reaction);

            // Assemble exact telescoping breakdown whose steps sum identically to `total`.
            List<DaggerfallPriceLine> steps = [];
            int baseGold = unwornTotal <= int.MaxValue ? checked((int)unwornTotal) : int.MaxValue;
            int runningGold = baseGold;

            if (wornTotal != unwornTotal)
            {
                int wornGold = checked((int)wornTotal);
                string wearReason = pricedLines.Count == 1 && pricedLines[0].MaxCondition > 0
                    ? $"Item condition ({pricedLines[0].CurCondition}/{pricedLines[0].MaxCondition})"
                    : "Item wear";
                steps.Add(new(wearReason, wornGold - runningGold));
                runningGold = wornGold;
            }

            if (shopNeutral != runningGold)
            {
                steps.Add(new(FormatShopQualityLabel(shopQuality), shopNeutral - runningGold));
                runningGold = shopNeutral;
            }

            if (region is not null && regionalSteps.Count > 0)
            {
                foreach ((string reason, string detail, int targetFactor) in regionalSteps)
                {
                    int nextShop = SubtotalAtFactor(region, targetFactor);
                    int delta = nextShop - runningGold;
                    if (delta != 0)
                    {
                        steps.Add(new(reason, delta, detail));
                        runningGold = nextShop;
                    }
                }
            }

            if (runningGold != shopSubtotal)
            {
                steps.Add(new("Regional price clamp", shopSubtotal - runningGold));
                runningGold = shopSubtotal;
            }

            int atNeutralReaction = DaggerfallRegionalEconomyPolicy.CalculateTradePrice(shopSubtotal, shopQuality, selling,
                playerMercantile, playerPersonality, 0);
            if (atNeutralReaction != runningGold)
            {
                string haggleLabel = selling ? "Merchant offer & mercantile" : "Mercantile & haggling";
                steps.Add(new(haggleLabel, atNeutralReaction - runningGold));
                runningGold = atNeutralReaction;
            }

            DaggerfallTradeReactionBreakdown effectiveReaction = reactionBreakdown ?? DaggerfallTradeReactionBreakdown.Plain(reaction);
            int unclampedReaction = effectiveReaction.BaseSocialReaction;
            int clampedStanding = Math.Clamp(unclampedReaction, DaggerfallRegionalEconomyPolicy.MinimumReaction, DaggerfallRegionalEconomyPolicy.MaximumReaction);
            int atStanding = DaggerfallRegionalEconomyPolicy.CalculateTradePrice(shopSubtotal, shopQuality, selling,
                playerMercantile, playerPersonality, clampedStanding);
            if (atStanding != runningGold)
            {
                string standingLabel = clampedStanding >= 0
                    ? $"Standing with merchant (+{clampedStanding})"
                    : $"Standing with merchant (\u2212{Math.Abs(clampedStanding)})";
                steps.Add(new(standingLabel, atStanding - runningGold));
                runningGold = atStanding;
            }

            foreach (DaggerfallReactionShiftCause shift in effectiveReaction.OfficeShifts)
            {
                unclampedReaction += shift.Delta;
                int clampedNext = Math.Clamp(unclampedReaction, DaggerfallRegionalEconomyPolicy.MinimumReaction, DaggerfallRegionalEconomyPolicy.MaximumReaction);
                int nextPrice = DaggerfallRegionalEconomyPolicy.CalculateTradePrice(shopSubtotal, shopQuality, selling,
                    playerMercantile, playerPersonality, clampedNext);
                int delta = nextPrice - runningGold;
                if (delta != 0)
                {
                    steps.Add(new(shift.Label, delta));
                    runningGold = nextPrice;
                }
            }

            if (runningGold != total)
            {
                steps.Add(new("Standing adjustment", total - runningGold));
                runningGold = total;
            }

            string baseLabel = lines.Count == 1 && lines[0].Quantity > 1
                ? $"Base value ({lines[0].Quantity} \u00D7 {pricedLines[0].UnwornValue:N0} gold)"
                : "Base value";
            DaggerfallPriceBreakdown breakdown = new(baseLabel, baseGold, steps, total);

            DaggerfallTradeQuote quote = new(side, region, shopQuality, shopSubtotal, total, Array.AsReadOnly(lines.ToArray()), breakdown);
            return new(quote, DaggerfallTradeQuoteFailure.None, null);
        }
        catch (OverflowException exception)
        {
            return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.Overflow, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidInput, DaggerfallRejectionText.For(exception));
        }
        catch (InvalidOperationException exception)
        {
            return DaggerfallTradeQuoteResult.Rejected(DaggerfallTradeQuoteFailure.InvalidItemMetadata, exception.Message);
        }
    }

    private string RegionLabel(int region)
    {
        string name = _definitions.BuildingNames.RegionName(region);
        return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
    }

    private static string FormatShopQualityLabel(int shopQuality) => shopQuality switch
    {
        <= 5 => "Shop markup (modest shop)",
        <= 13 => "Shop markup (average shop)",
        _ => "Shop markup (fine shop)",
    };

    private int TradeValue(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata metadata)
    {
        // DFU's current quote caller uses item.value regardless of identification. WorldRpg requires
        // unidentified enchantments to retain the ordinary item's source value until identification.
        if (metadata.HasEnchantment && !metadata.Identified)
        {
            if (metadata.MadeEnchantment is not null) return definition.Value;
            string enchantment = metadata.Enchantment!;
            if (!StringComparer.Ordinal.Equals(definition.Id.Value, metadata.ItemId))
                throw new InvalidOperationException($"Item metadata '{metadata.ItemId}' does not belong to '{definition.Id.Value}'.");
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
