using System.Reflection;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallRegionalEconomyTests
{
    [Fact]
    public void Cost_trade_and_daily_price_formulas_keep_the_donor_order_and_bounds()
    {
        Assert.Equal(2, DaggerfallRegionalEconomyPolicy.CalculateCost(0, 10, null, 0));
        Assert.Equal(180, DaggerfallRegionalEconomyPolicy.CalculateCost(100, 0, null, 0));
        Assert.Equal(220, DaggerfallRegionalEconomyPolicy.CalculateCost(100, 20, null, 0));
        Assert.Equal(200, DaggerfallRegionalEconomyPolicy.CalculateCost(100, 10, null, 0, conditionPercentage: 20));
        Assert.Equal(75, DaggerfallRegionalEconomyPolicy.ApplyRegionalPriceAdjustment(100, 0, 750));
        Assert.Equal(250, DaggerfallRegionalEconomyPolicy.CalculateTradePrice(500, 10, selling: true, 50, 50, 0));
        Assert.Equal(281, DaggerfallRegionalEconomyPolicy.CalculateTradePrice(500, 10, selling: false, 50, 50, 0));
        Assert.Equal(1020, DaggerfallRegionalEconomyPolicy.UpdateRegionalPrice(1000, 100, 1, roll: 68));
        Assert.Equal(980, DaggerfallRegionalEconomyPolicy.UpdateRegionalPrice(1000, 100, 1, roll: 69));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallRegionalEconomyPolicy.CalculateTradePrice(-1, 10, false, 50, 50, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallRegionalEconomyPolicy.CalculateCost(100, 21, null, 0));
    }

    [Fact]
    public void Actual_quote_caller_distinguishes_buy_sell_shop_quality_and_instance_condition()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallCreatedItem created = OrdinaryItem(definitions, currentCondition: 50, maximumCondition: 100);
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(created.Item.Value)) with { Value = 5000 };
        DaggerfallRegionalPriceState prices = PriceState(definitions);
        DaggerfallTradeQuoteService service = new(definitions, new DaggerfallItemValuation(definitions), prices);
        DaggerfallTradeLine line = new(definition, created.Metadata, 1);

        DaggerfallTradeQuote buy = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [line], shopQuality: 10, playerMercantile: 50, playerPersonality: 50, reaction: 0, region: null).Quote);
        DaggerfallTradeQuote sell = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.SellToMerchant,
            [line], shopQuality: 10, playerMercantile: 50, playerPersonality: 50, reaction: 0, region: null).Quote);
        DaggerfallTradeQuote lowQuality = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [line], shopQuality: 0, playerMercantile: 50, playerPersonality: 50, reaction: 0, region: null).Quote);
        DaggerfallTradeQuote highQuality = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [line], shopQuality: 20, playerMercantile: 50, playerPersonality: 50, reaction: 0, region: null).Quote);
        DaggerfallTradeLine fullConditionLine = line with
        { Metadata = created.Metadata with { CurrentCondition = created.Metadata.MaximumCondition } };
        DaggerfallTradeQuote fullCondition = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [fullConditionLine], 10, 50, 50, 0, null).Quote);

        Assert.Equal((definition.Value / 2) * 2, buy.ShopSubtotal);
        Assert.Equal(DaggerfallRegionalEconomyPolicy.CalculateTradePrice(buy.ShopSubtotal, 10, selling: false, 50, 50, 0), buy.Total);
        Assert.Equal(DaggerfallRegionalEconomyPolicy.CalculateTradePrice(sell.ShopSubtotal, 10, selling: true, 50, 50, 0), sell.Total);
        Assert.True(lowQuality.ShopSubtotal < buy.ShopSubtotal);
        Assert.True(highQuality.ShopSubtotal > buy.ShopSubtotal);
        Assert.True(fullCondition.ShopSubtotal > buy.ShopSubtotal);
        Assert.True(buy.Total > sell.Total);
    }

    [Fact]
    public void Enchantment_value_waits_for_identification_and_reaction_is_bounded_before_personality()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallCreatedItem created = MagicItem(definitions);
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(created.Item.Value));
        DaggerfallRegionalPriceState prices = PriceState(definitions);
        DaggerfallTradeQuoteService service = new(definitions, new DaggerfallItemValuation(definitions), prices);
        DaggerfallTradeLine identified = new(definition, created.Metadata with { Identified = true }, 1);
        DaggerfallTradeLine unidentified = new(definition, created.Metadata with { Identified = false }, 1);

        DaggerfallTradeQuote identifiedQuote = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [identified], 10, 50, 50, 0, null).Quote);
        DaggerfallTradeQuote unidentifiedQuote = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [unidentified], 10, 50, 50, 0, null).Quote);
        DaggerfallTradeQuote neutralSell = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.SellToMerchant,
            [identified], 10, 50, 50, 0, null).Quote);
        DaggerfallTradeQuote favorableReaction = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.SellToMerchant,
            [identified], 10, 50, 50, 50, null).Quote);
        DaggerfallTradeQuote cappedReaction = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.SellToMerchant,
            [identified], 10, 50, 50, 100, null).Quote);

        Assert.NotEqual(identifiedQuote.ShopSubtotal, unidentifiedQuote.ShopSubtotal);
        Assert.True(favorableReaction.Total > neutralSell.Total);
        Assert.Equal(favorableReaction.Total, cappedReaction.Total);
        Assert.Equal(DaggerfallTradeQuoteFailure.InvalidInput,
            service.Quote(DaggerfallTradeSide.BuyFromMerchant, [identified], 10, 50, 50, 101, null).Failure);
    }

    [Fact]
    public void Regional_factors_update_at_daily_boundaries_and_restored_quotes_match()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallRegionalPriceState initial = PriceState(definitions);
        Assert.Equal(DaggerfallRegionalEconomyPolicy.RegionCount, initial.Factors.Count);
        Assert.All(initial.Factors, factor => Assert.Equal(750, factor));

        DaggerfallCreatedItem created = OrdinaryItem(definitions, 100, 100);
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(created.Item.Value)) with { Value = 5000 };
        DaggerfallTradeLine line = new(definition, created.Metadata, 1);
        DaggerfallTradeQuoteService initialService = new(definitions, new DaggerfallItemValuation(definitions), initial);
        DaggerfallTradeQuote beforeUpdate = Assert.IsType<DaggerfallTradeQuote>(initialService.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [line], 10, 50, 50, 0, 0).Quote);

        initial.AdvanceToDay(3);
        DaggerfallRegionalPriceSave captured = initial.Capture();
        Assert.Equal(3, captured.LastAdvancedDay);
        Assert.NotEqual(750, initial.AdjustmentForRegion(0));

        DaggerfallRegionalPriceState restored = new(definitions.Factions, MinimumRandom(), currentDay: 3, restored: captured);
        DaggerfallTradeQuoteService firstService = new(definitions, new DaggerfallItemValuation(definitions), initial);
        DaggerfallTradeQuoteService restoredService = new(definitions, new DaggerfallItemValuation(definitions), restored);
        DaggerfallTradeQuote first = Assert.IsType<DaggerfallTradeQuote>(firstService.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [line], 10, 50, 50, 0, 0).Quote);
        DaggerfallTradeQuote afterLoad = Assert.IsType<DaggerfallTradeQuote>(restoredService.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [line], 10, 50, 50, 0, 0).Quote);
        Assert.Equal((first.ShopSubtotal, first.Total), (afterLoad.ShopSubtotal, afterLoad.Total));
        Assert.NotEqual(beforeUpdate.Total, first.Total);

        initial.AdvanceToDay(4);
        restored.AdvanceToDay(4);
        Assert.Equal(initial.LastAdvancedDay, restored.LastAdvancedDay);
        Assert.Equal(initial.Capture().Factors, restored.Capture().Factors);
        Assert.Throws<ArgumentOutOfRangeException>(() => restored.AdvanceToDay(2));
    }

    [Fact]
    public void Quotes_reject_negative_values_overflow_and_malformed_regional_saves()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallCreatedItem created = OrdinaryItem(definitions, 100, 100);
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(created.Item.Value));
        DaggerfallTradeLine line = new(definition with { Value = int.MaxValue, MaximumQuantity = ulong.MaxValue }, created.Metadata, 1);
        DaggerfallTradeQuoteService service = new(definitions, new DaggerfallItemValuation(definitions), PriceState(definitions));
        DaggerfallTradeQuoteResult overflow = service.Quote(DaggerfallTradeSide.BuyFromMerchant, [line], 20, 50, 50, 0, 0);
        Assert.Equal(DaggerfallTradeQuoteFailure.Overflow, overflow.Failure);

        DaggerfallTradeQuoteResult negative = service.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [new(definition with { Value = -1 }, created.Metadata, 1)], 10, 50, 50, 0, null);
        Assert.Equal(DaggerfallTradeQuoteFailure.InvalidInput, negative.Failure);
        Assert.Throws<ArgumentException>(() => new DaggerfallRegionalPriceSave(0, new int[61]).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallRegionalPriceSave(0,
            Enumerable.Repeat(100, DaggerfallRegionalEconomyPolicy.RegionCount).ToArray()).Validate());
    }

    private static DaggerfallCreatedItem OrdinaryItem(DaggerfallDefinitions definitions, int currentCondition, int maximumCondition) =>
        new DaggerfallItemFactory(definitions, MinimumRandom()).Create(new DaggerfallItemCreateRequest(
            "Weapons", "regional-economy-test-weapon", DaggerfallItemOwner.Player,
            TemplateIndex: 113, Material: "iron")) with
        { Metadata = CreateItemWithCondition(definitions, currentCondition, maximumCondition) };

    private static DaggerfallItemInstanceMetadata CreateItemWithCondition(DaggerfallDefinitions definitions, int currentCondition, int maximumCondition)
    {
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId("template-113-iron"));
        return DaggerfallItemInstanceMetadata.Default(definition, DaggerfallItemOwner.Player) with
        { CurrentCondition = currentCondition, MaximumCondition = maximumCondition };
    }

    [Fact]
    public void An_item_makers_enchantment_is_valued_like_any_other_unidentified_one()
    {
        // A setting has no published magic item, so quoting a setting-enchanted item must not be rejected
        // as unpublished metadata: it keeps the ordinary item's value until identification, exactly as an
        // unidentified published enchantment does.
        DaggerfallDefinitions definitions = LoadDefinitions();
        string setting = DaggerfallEnchantmentSettings.All.Single(candidate => candidate.Type == 7 && candidate.Param == 0).Key;
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId("iron-longsword"));
        DaggerfallItemInstanceMetadata metadata = DaggerfallItemInstanceMetadata.Default(definition, DaggerfallItemOwner.Player);
        DaggerfallRegionalPriceState prices = PriceState(definitions);
        DaggerfallTradeQuoteService service = new(definitions, new DaggerfallItemValuation(definitions), prices);
        DaggerfallTradeLine unidentified = new(definition, metadata with { Enchantment = setting, Identified = false }, 1);
        DaggerfallTradeLine plain = new(definition, metadata with { Identified = false }, 1);

        DaggerfallTradeQuote enchanted = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [unidentified], 10, 50, 50, 0, null).Quote);
        DaggerfallTradeQuote ordinary = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [plain], 10, 50, 50, 0, null).Quote);

        Assert.Equal(ordinary.ShopSubtotal, enchanted.ShopSubtotal);

        // The identified half of the same rule: an identified setting is worth the ordinary item too, so a
        // regression that invented a magic value for it would fail here.
        DaggerfallTradeLine identifiedEnchanted = new(definition, metadata with { Enchantment = setting, Identified = true }, 1);
        DaggerfallTradeLine identifiedPlain = new(definition, metadata with { Identified = true }, 1);
        DaggerfallTradeQuote identifiedSettingQuote = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [identifiedEnchanted], 10, 50, 50, 0, null).Quote);
        DaggerfallTradeQuote identifiedPlainQuote = Assert.IsType<DaggerfallTradeQuote>(service.Quote(DaggerfallTradeSide.BuyFromMerchant,
            [identifiedPlain], 10, 50, 50, 0, null).Quote);
        Assert.Equal(identifiedPlainQuote.ShopSubtotal, identifiedSettingQuote.ShopSubtotal);
    }

    private static DaggerfallCreatedItem MagicItem(DaggerfallDefinitions definitions)
    {
        DaggerfallCreatedItem created = new DaggerfallItemFactory(definitions, MinimumRandom()).Create(new DaggerfallItemCreateRequest(
            "Magic", "regional-economy-test-magic", DaggerfallItemOwner.Player,
            Race: "breton", Gender: "male", MagicItemKey: "magic-item.0010"));
        return created with { Metadata = created.Metadata with { Identified = true } };
    }

    private static DaggerfallRegionalPriceState PriceState(DaggerfallDefinitions definitions) =>
        new(definitions.Factions, MinimumRandom(), currentDay: 0, randomizationKey: "regional-economy-test");

    private static IRandomService MinimumRandom() => DispatchProxy.Create<IRandomService, MinimumRandomProxy>();

    private class MinimumRandomProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }

    private static DaggerfallDefinitions LoadDefinitions() =>
        TestPayload.Definitions;

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
