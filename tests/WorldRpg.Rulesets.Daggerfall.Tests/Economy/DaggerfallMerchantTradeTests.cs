using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Trading with the merchant the player talks to, built over a real merchant service, without the session.</summary>
public sealed class DaggerfallMerchantTradeTests
{
    private const int GeneralStore = 9;

    [Fact]
    public void The_person_answers_a_trade_only_with_a_merchant_service_in_an_admitted_building_and_the_quote_keeps_to_its_revision()
    {
        // Noon, when the shop is open.
        using GuildMakerFixture f = new() { Calendar = new(405, 5, 0, 12, 0, 0) };
        (DaggerfallSiteId site, DaggerfallSiteBuildingSource building) = f.Definitions.Locations.Records
            .SelectMany(record => record.Exterior!.Buildings.Values
                .Where(value => value.Source.BuildingType == GeneralStore)
                .Select(value => (record.Id, value)))
            .First();
        DaggerfallInteriorBuilding shop = new(building.Id.BlockX, building.Id.BlockY, building.Source.Id, GeneralStore, building.Source.FactionId);
        DaggerfallNpc? talking = null;
        DaggerfallInteriorBuilding? standing = shop;
        DaggerfallMerchantTrade trade = new(f.Merchants(), new DaggerfallSiteContext(f.Definitions.Locations, site, null, []),
            _ => talking, () => standing, () => new(site, DaggerfallWorldProfileKind.Interior, "shop"), () => "page");

        Assert.Null(trade.Read());
        Assert.Equal("ProviderUnavailable", trade.Trade(DaggerfallMerchantTradeKind.Buy, "page", "any", null, 1, true).Outcome);

        talking = f.Person("buy-items", "sell-items");
        DaggerfallMerchantView view = Assert.IsType<DaggerfallMerchantView>(trade.Read());
        Assert.Null(view.Quote);
        Assert.Equal("ProviderUnavailable", trade.Trade(DaggerfallMerchantTradeKind.Repair, view.Revision, "any", null, null, false).Outcome);

        DaggerfallMerchantItemView row = Assert.IsType<DaggerfallMerchantItemView>(view.Stock.FirstOrDefault());
        trade.Quote(view.Revision, row.Key, buying: true, 1);
        DaggerfallMerchantQuoteView quote = Assert.IsType<DaggerfallMerchantQuoteView>(trade.Read()!.Quote);
        Assert.Equal((view.Revision, row.Key, true, 1UL), (quote.Revision, quote.Item, quote.Buying, quote.Amount));
        trade.Quote("other", row.Key, buying: true, 1);
        Assert.Null(trade.Read()!.Quote);

        // A purchase reaches this merchant's stock (what it grants is the merchant service's to test).
        DaggerfallMerchantResult bought = trade.Trade(DaggerfallMerchantTradeKind.Buy, view.Revision, row.Key, null, 1, confirm: true);
        Assert.NotEqual("ProviderUnavailable", bought.Outcome);
        Assert.Equal("Stale", trade.Trade(DaggerfallMerchantTradeKind.Buy, "other", row.Key, null, 1, confirm: true).Outcome);

        standing = shop with { Building = shop.Building with { Index = 999 } };
        Assert.Null(trade.Read());
        standing = null;
        Assert.Null(trade.Read());
    }

    [Fact]
    public void Merchant_results_read_as_the_player_line_with_the_gold_paid()
    {
        Assert.Equal("Sold for 12 gold.", DaggerfallMerchantTrade.OutcomeText(new(true, "Sold", 12)));
        Assert.Equal("The item was identified free of charge.", DaggerfallMerchantTrade.OutcomeText(new(true, "Identified")));
        Assert.Equal("Identified for 5 gold.", DaggerfallMerchantTrade.OutcomeText(new(true, "Identified", 5)));
        Assert.Equal("You have already tried to steal that.", DaggerfallMerchantTrade.OutcomeText(new(false, "AlreadyAttempted")));
        Assert.Equal(DaggerfallServiceOutcomeText.Common("ProviderUnavailable"),
            DaggerfallMerchantTrade.OutcomeText(new(false, "ProviderUnavailable")));
    }
}
