using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Focused unit and session tests for #11083:
/// 1. Road-side trade with intercepted <see cref="DaggerfallOverlandTraffic"/> traveling caravan merchants
///    (origin-vs-local regional prices, <see cref="DaggerfallWorldConditions"/> scarcity/surplus, social modifiers,
///    outlaw refusal and personal-ally exemption, save/reload continuity, and clean return-home retirement).
/// 2. Road-side origin-region news exchange with intercepted regional couriers (surfacing real
///    <see cref="DaggerfallWordSpread"/> wavefronts ahead of the road wave without spoiling unrevealed sealed
///    <see cref="DaggerfallCourierDispatches"/>, surviving save/reload and clean return-home pruning).
/// </summary>
public sealed class OverlandEncounterExchangeTests
{
    [Fact]
    public void Caravan_trade_quote_blends_origin_and_local_regional_prices_world_conditions_and_social_reaction()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallItemDefinition item = definitions.Items.Values.First(i => i.Value >= 100 && i.MaximumQuantity == 1);
        DaggerfallItemInstanceMetadata meta = DaggerfallItemInstanceMetadata.Default(item, DaggerfallItemOwner.Player);
        DaggerfallTradeLine line = new(item, meta, 1);

        int localRegion = 17;  // Daggerfall
        int originRegion = 20; // Sentinel

        int[] baseFactors = Enumerable.Repeat(DaggerfallRegionalEconomyPolicy.NeutralRegionalAdjustment, DaggerfallRegionalEconomyPolicy.RegionCount).ToArray();
        DaggerfallRegionalPriceState prices = new(definitions.Factions, RandomMinimum.Create(), 0,
            new DaggerfallRegionalPriceSave(0, baseFactors));
        DaggerfallWorldConditions conditions = new();
        DaggerfallConditionPrices conditionPrices = new(conditions, () => 1);
        DaggerfallTradeQuoteService quotes = new(definitions, new DaggerfallItemValuation(definitions), prices, conditionPrices.GoodsAdjustment);

        // Baseline: neutral origin and local regions match a local shop identically.
        DaggerfallTradeQuote neutralLocal = quotes.Quote(DaggerfallTradeSide.BuyFromMerchant, [line], 10, 40, 50, 0, localRegion).Quote!;
        DaggerfallTradeQuote neutralCaravan = quotes.Quote(DaggerfallTradeSide.BuyFromMerchant, [line], 10, 40, 50, 0, localRegion, originRegion).Quote!;
        Assert.Equal(neutralLocal.Total, neutralCaravan.Total);

        // Local grain shortage raises both local and caravan buy prices, while a festival surplus in the caravan's
        // origin region keeps the caravan cheaper than a local shop in the scarce region.
        conditions.Set(new DaggerfallWorldConditionToken("cond:local-shortage", DaggerfallWorldConditionKind.GrainShortage,
            localRegion, Severity: 2, StartedDay: 1, ExpiresDay: 10));
        DaggerfallTradeQuote scarceLocal = quotes.Quote(DaggerfallTradeSide.BuyFromMerchant, [line], 10, 40, 50, 0, localRegion).Quote!;
        DaggerfallTradeQuote scarceCaravanCalmOrigin = quotes.Quote(DaggerfallTradeSide.BuyFromMerchant, [line], 10, 40, 50, 0, localRegion, originRegion).Quote!;
        Assert.True(scarceLocal.Total > neutralLocal.Total);
        Assert.True(scarceCaravanCalmOrigin.Total > neutralCaravan.Total);
        Assert.True(scarceCaravanCalmOrigin.Total < scarceLocal.Total,
            $"Caravan from unaffected origin ({scarceCaravanCalmOrigin.Total}) should undercut scarce local shop ({scarceLocal.Total}).");

        conditions.Set(new DaggerfallWorldConditionToken("cond:origin-festival", DaggerfallWorldConditionKind.Decree,
            originRegion, Severity: 2, StartedDay: 1, ExpiresDay: 10, SubjectTag: DaggerfallConditionPrices.FestivalTag));
        DaggerfallTradeQuote scarceCaravanSurplusOrigin = quotes.Quote(DaggerfallTradeSide.BuyFromMerchant, [line], 10, 40, 50, 0, localRegion, originRegion).Quote!;
        Assert.True(scarceCaravanSurplusOrigin.Total < scarceCaravanCalmOrigin.Total,
            $"Origin festival surplus ({scarceCaravanSurplusOrigin.Total}) should further lower caravan price ({scarceCaravanCalmOrigin.Total}).");

        // Social reaction modifier improves buy prices and sell payouts on caravan quotes.
        DaggerfallTradeQuote friendlyBuy = quotes.Quote(DaggerfallTradeSide.BuyFromMerchant, [line], 10, 40, 50, reaction: 40, localRegion, originRegion).Quote!;
        DaggerfallTradeQuote hostileBuy = quotes.Quote(DaggerfallTradeSide.BuyFromMerchant, [line], 10, 40, 50, reaction: -40, localRegion, originRegion).Quote!;
        Assert.True(friendlyBuy.Total < hostileBuy.Total);

        DaggerfallTradeQuote friendlySell = quotes.Quote(DaggerfallTradeSide.SellToMerchant, [line], 10, 40, 50, reaction: 40, localRegion, originRegion).Quote!;
        DaggerfallTradeQuote hostileSell = quotes.Quote(DaggerfallTradeSide.SellToMerchant, [line], 10, 40, 50, reaction: -40, localRegion, originRegion).Quote!;
        Assert.True(friendlySell.Total > hostileSell.Total);
    }

    [Fact]
    public void Road_courier_news_surfaces_ahead_of_wave_word_spread_without_spoiling_sealed_dispatches()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord localSite = definitions.Locations.Records.Single(r => r.Id == Charing);
        DaggerfallSiteRecord farOrigin = definitions.Locations.Records.First(r =>
            r.Exterior is not null
            && r.Id.Region != localSite.Id.Region
            && Math.Sqrt(Math.Pow(r.MapPixelX - localSite.MapPixelX, 2) + Math.Pow(r.MapPixelY - localSite.MapPixelY, 2)) >= 60.0);

        DaggerfallWorldConditions conditions = new();
        conditions.Set(new DaggerfallWorldConditionToken("cond:war-delay", DaggerfallWorldConditionKind.BorderTension,
            farOrigin.Id.Region, Severity: 2, StartedDay: 0, ExpiresDay: 15,
            Body: $"Border patrols are mobilizing along the roads of {definitions.BuildingNames.RegionName(farOrigin.Id.Region)}."));
        conditions.Set(new DaggerfallWorldConditionToken("cond:sealed-move-token", DaggerfallWorldConditionKind.CrimeCrackdown,
            farOrigin.Id.Region, Severity: 1, StartedDay: 0, ExpiresDay: 15,
            Body: "CLASSIFIED SECRET MOVE CONDITION THAT MUST NOT LEAK"));

        DaggerfallWordSpread wordSpread = new(DaggerfallTuning.Defaults.Social, conditions);
        long nowMinute = 200;
        int nowDay = 0;

        // Public news wavefront emitted at farOrigin — still in transit to localSite.
        DaggerfallWordWavefrontSave publicWavefront = wordSpread.RecordNews(
            "wf:public:11083",
            "Granary Opened",
            $"The granaries of {farOrigin.Name} have opened their stores to travelers.",
            farOrigin,
            currentMinute: 180);
        Assert.False(wordSpread.EvaluateAt(publicWavefront, localSite, nowMinute, nowDay).HasArrived);

        // Sealed board-game dispatch linked to its own wavefront and condition token (unrevealed).
        DaggerfallWordWavefrontSave sealedWavefront = wordSpread.RecordNews(
            "wf:sealed:11083",
            "Secret Levy",
            "TOP SECRET SEALED WAVEFRONT MUST NOT LEAK",
            farOrigin,
            currentMinute: 190);
        DaggerfallActorLedger ledger = new();
        DaggerfallSocialState social = new(definitions.Factions);
        DaggerfallCourierDispatches dispatches = new(
            definitions, wordSpread, conditions, social, ledger,
            _ => true, _ => { }, () => 0UL);
        _ = dispatches.StageDispatch(
            dispatchId: "disp:11083",
            seatId: "sentinel",
            actionKind: "raise-levy",
            summary: "SECRET SEALED SUMMARY MUST NOT LEAK",
            origin: farOrigin,
            destination: localSite,
            currentMinute: 190,
            currentDay: nowDay,
            linkedWavefrontId: sealedWavefront.EventId,
            linkedConditionTokenId: "cond:sealed-move-token",
            stageOverlandBand: false);

        DaggerfallNpc courierNpc = new(
            DurableId: 0x4000_0000_0099L,
            Kind: DaggerfallNpcKind.Civilian,
            StableKey: "traffic/courier/0",
            Site: new DaggerfallNpcSite(farOrigin.Id.Region, farOrigin.Name, string.Empty, DaggerfallWorldProfileIds.Exterior(farOrigin.Id).LogicalId),
            Appearance: new DaggerfallNpcAppearance("breton", "Male", 182, 0, 11, 0),
            Role: "courier",
            Services: ["talk"],
            Presence: DaggerfallNpcPresence.Active,
            X: null,
            Y: null,
            Z: null);
        DaggerfallRoadEncounterContext encounter = new(
            IsCaravan: false,
            IsCourier: true,
            OriginRegion: farOrigin.Id.Region,
            OriginRegionName: definitions.BuildingNames.RegionName(farOrigin.Id.Region),
            OriginLocationName: farOrigin.Name,
            OriginSite: farOrigin);

        IReadOnlyList<DaggerfallCourierNewsItem> items = DaggerfallRoadCourierNews.BuildNewsItems(
            encounter, courierNpc, localSite, nowMinute, nowDay, wordSpread, conditions, dispatches, definitions);

        Assert.NotEmpty(items);
        Assert.True(items[0].AheadOfRoadWave);
        Assert.Equal(publicWavefront.EventId, items[0].WavefrontEventId);
        Assert.Contains(publicWavefront.Body!, items[0].Reply, StringComparison.Ordinal);
        Assert.Contains("war-zone border disruption", items[0].Reply, StringComparison.Ordinal);

        // Sealed unrevealed dispatch details must never appear in courier public road news.
        Assert.DoesNotContain(items, i => i.Reply.Contains("TOP SECRET", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Reply.Contains("SECRET SEALED", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Reply.Contains("CLASSIFIED", StringComparison.OrdinalIgnoreCase));

        // Once the player buys/reveals the sealed dispatch via DaggerfallCourierDispatches, the leaked news becomes public.
        Assert.True(dispatches.BuyDispatch("disp:11083", nowMinute, nowDay).Accepted);
        IReadOnlyList<DaggerfallCourierNewsItem> afterReveal = DaggerfallRoadCourierNews.BuildNewsItems(
            encounter, courierNpc, localSite, nowMinute, nowDay, wordSpread, conditions, dispatches, definitions);
        Assert.Contains(afterReveal, i => i.Reply.Contains("SECRET SEALED SUMMARY", StringComparison.Ordinal));
    }

    [Fact]
    public void Intercepted_caravan_opens_trade_window_applies_pricing_social_and_outlaw_rules_and_survives_save_reload_and_return_home()
    {
        using ExchangeFixture fixture = ExchangeFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);
        // Advance to 21:00 (after normal town shop hours 06:00-18:00) to prove road caravans trade on the road at any hour.
        DaggerfallCalendar startCal = ((IDaggerfallExpeditionSurroundings)session).Calendar;
        int hoursToEvening = ((21 - startCal.Hour) + 24) % 24;
        if (hoursToEvening == 0) hoursToEvening = 24;
        session.AdvanceElapsedTime(hoursToEvening * 60 * 60);
        DaggerfallCalendar calendarAfterAdvance = ((IDaggerfallExpeditionSurroundings)session).Calendar;
        Assert.True(calendarAfterAdvance.Hour >= 18 || calendarAfterAdvance.Hour < 6,
            $"Expected after-hours clock, got hour {calendarAfterAdvance.Hour}.");
        session.State.Currency.ReceiveGold(5000);

        session.OpenExpedition();
        // Use an autonomously dispatched Caravan from the daytime stretch, or dispatch odd window 501.
        DaggerfallPersistentOverlandBand? caravan = session.Expedition.PersistentBands
            .FirstOrDefault(b => b.BandId.StartsWith(DaggerfallOverlandTraffic.BandIdPrefix, StringComparison.Ordinal)
                && b.Kind == DaggerfallBandKind.Caravan)
            ?? session.OverlandTraffic.TryDispatchWindow(501L, session.Expedition.Position);
        Assert.NotNull(caravan);
        Assert.Equal(DaggerfallBandKind.Caravan, caravan!.Kind);

        ActorLedgerEntry member = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(caravan.BandId));
        long merchantId = member.DurableId;
        Assert.Equal("merchant", member.Social!.Role);
        Assert.Contains("merchant", member.Social.Services);

        // Intercept the caravan on foot and enter the 3D scene.
        session.OverlandParties.OnMeetPersistentBand(caravan, session.Expedition.Party, DaggerfallEncounterStaging.Travelling);
        session.EnterExpedition();
        Assert.True(session.State.Actors.TryGet(merchantId, out _));
        Assert.True(session.State.ActorLedger.TryGet(merchantId, out ActorLedgerEntry? metEntry));
        Assert.True(metEntry!.Location?.TrafficVisitor);

        // Open dialogue with the intercepted caravan merchant: real trade service and Merchant projection appear.
        DaggerfallDialogueView? open = TalkTo(session, merchantId);
        Assert.NotNull(open);
        Assert.Contains("hauling trade goods", open!.Greeting, StringComparison.OrdinalIgnoreCase);
        DaggerfallDialogueView? panelView = session.ActivationPanel.View.Dialogue;
        Assert.NotNull(panelView);
        Assert.NotNull(panelView!.Merchant);
        Assert.True(panelView.Merchant!.CanBuy);
        Assert.True(panelView.Merchant.CanSell);
        Assert.NotEmpty(panelView.Merchant.Stock);
        Assert.Contains(panelView.Services, s => s.Id == DaggerfallPersonServices.Trade);
        Assert.Equal(DaggerfallPersonServices.Chooser, panelView.OpensOn);

        // Verify WorldConditions scarcity raises the caravan's quoted prices live.
        DaggerfallMerchantItemView sampleStock = panelView.Merchant.Stock
            .OrderByDescending(s => s.UnitPrice)
            .First(s => s.UnitPrice > 10);
        ulong baselineUnitPrice = sampleStock.UnitPrice;
        int localRegion = fixture.Origin.Site!.Value.Region;
        int currentDay = checked((int)((IDaggerfallExpeditionSurroundings)session).Calendar.DayNumber);
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            "cond:caravan-shortage",
            DaggerfallWorldConditionKind.GrainShortage,
            localRegion,
            Severity: 3,
            StartedDay: currentDay,
            ExpiresDay: currentDay + 10));
        DaggerfallMerchantView scarceView = session.ActivationPanel.View.Dialogue!.Merchant!;
        ulong scarceUnitPrice = scarceView.Stock.Single(s => s.Key == sampleStock.Key).UnitPrice;
        Assert.True(scarceUnitPrice > baselineUnitPrice,
            $"Local GrainShortage should raise caravan buy price ({scarceUnitPrice} vs {baselineUnitPrice}).");

        // Verify social modifiers (personal ally disposition) reduce caravan buy prices.
        session.Introductions.AdjustActorDisposition(merchantId, 30);
        DaggerfallMerchantView friendlyView = session.ActivationPanel.View.Dialogue!.Merchant!;
        ulong friendlyUnitPrice = friendlyView.Stock.Single(s => s.Key == sampleStock.Key).UnitPrice;
        Assert.True(friendlyUnitPrice < scarceUnitPrice,
            $"Positive disposition should lower caravan buy price ({friendlyUnitPrice} vs {scarceUnitPrice}).");

        // Verify outlaw refusal when wanted in region (reset disposition to neutral first).
        session.Introductions.AdjustActorDisposition(merchantId, -30);
        int repDrop = -25 - session.State.Social.RegionalReputation(localRegion);
        session.State.Social.ChangeRegionalReputation(localRegion, repDrop);
        DaggerfallMerchantView wantedView = session.ActivationPanel.View.Dialogue!.Merchant!;
        DaggerfallMerchantItemView buyTarget = wantedView.Stock.First(s => s.UnitPrice > 0 && s.UnitPrice <= 1500);
        DaggerfallMerchantResult denied = session.MerchantTrade.Trade(
            DaggerfallMerchantTradeKind.Buy, wantedView.Revision, buyTarget.Key, null, 1, confirm: true);
        Assert.False(denied.Accepted);
        Assert.Equal(nameof(DaggerfallServiceDenial.WantedOrBanished), denied.Outcome);

        // Verify outlaw exemption: a personal ally (DispositionDelta >= 20) still trades with a wanted player on the road.
        session.Introductions.AdjustActorDisposition(merchantId, DaggerfallLegalServicePolicy.PersonalAllyDispositionThreshold);
        DaggerfallMerchantView allyView = session.ActivationPanel.View.Dialogue!.Merchant!;
        DaggerfallMerchantItemView allyTarget = allyView.Stock.Single(s => s.Key == buyTarget.Key);
        ulong goldBeforeBuy = session.State.Currency.Read().Gold;
        DaggerfallMerchantResult bought = session.MerchantTrade.Trade(
            DaggerfallMerchantTradeKind.Buy, allyView.Revision, allyTarget.Key, null, 1, confirm: true);
        Assert.True(bought.Accepted, $"Expected outlaw-exempt personal ally caravan trade to succeed, got '{bought.Outcome}'.");
        Assert.Equal("Purchased", bought.Outcome);
        Assert.True(session.State.Currency.Read().Gold < goldBeforeBuy);
        Assert.NotEmpty(session.State.Merchants.Capture());

        // Save and reload mid-encounter: caravan merchant and its remaining stock persist cleanly.
        int stockCountAfterBuy = session.ActivationPanel.View.Dialogue!.Merchant!.Stock.Count;
        RulesetSavePayload savedMidTrade = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, savedMidTrade);
        Assert.True(restored.State.Actors.TryGet(merchantId, out _));
        Assert.True(restored.State.ActorLedger.TryGet(merchantId, out ActorLedgerEntry? restoredEntry));
        Assert.True(restoredEntry!.Location?.TrafficVisitor);

        DaggerfallDialogueView? reopened = TalkTo(restored, merchantId);
        Assert.NotNull(reopened);
        DaggerfallMerchantView? restoredMerchant = restored.ActivationPanel.View.Dialogue?.Merchant;
        Assert.NotNull(restoredMerchant);
        Assert.Equal(stockCountAfterBuy, restoredMerchant!.Stock.Count);

        // Sell a purchased or carried accepted item back to the caravan after reload.
        DaggerfallMerchantItemView? sellable = restoredMerchant.PlayerItems.FirstOrDefault(i => i.CanSell);
        Assert.NotNull(sellable);
        ulong goldBeforeSell = restored.State.Currency.Read().Gold;
        DaggerfallMerchantResult sold = restored.MerchantTrade.Trade(
            DaggerfallMerchantTradeKind.Sell, restoredMerchant.Revision, sellable!.Key, null, 1, confirm: true);
        Assert.True(sold.Accepted, $"Expected road-side sell to caravan to succeed, got '{sold.Outcome}'.");
        Assert.Equal("Sold", sold.Outcome);
        Assert.True(restored.State.Currency.Read().Gold >= goldBeforeSell);

        // Leave encounter to expedition map -> :return band dispatches -> save mid-return validates cleanly ->
        // return arrival retires the transient road-side merchant binding without leaking containers.
        restored.OpenExpedition();
        Assert.False(restored.State.Actors.TryGet(merchantId, out _));
        DaggerfallPersistentOverlandBand returnBand = Assert.Single(restored.Expedition.PersistentBands,
            b => b.BandId.StartsWith(DaggerfallOverlandTraffic.BandIdPrefix, StringComparison.Ordinal)
                && b.BandId.EndsWith(DaggerfallOverlandTraffic.ReturnBandSuffix, StringComparison.Ordinal)
                && restored.State.ActorLedger.EntriesOnOverlandBand(b.BandId).Any(e => e.DurableId == merchantId));
        _ = restored.CaptureSave();

        _ = restored.OverlandParties.OnPersistentBandArrived(returnBand);
        Assert.DoesNotContain(restored.State.Merchants.Capture(), m => m.ProviderNpcId == merchantId);
        RulesetSavePayload savedAfterReturn = restored.CaptureSave();
        using DaggerfallSession afterReturn = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, savedAfterReturn);
        Assert.DoesNotContain(afterReturn.State.Merchants.Capture(), m => m.ProviderNpcId == merchantId);
    }

    [Fact]
    public void Intercepted_regional_courier_shares_origin_word_spread_ahead_of_road_wave_without_spoiling_sealed_dispatch_and_returns_home()
    {
        using ExchangeFixture fixture = ExchangeFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);
        session.AdvanceElapsedTime(8 * 60 * 60);

        session.OpenExpedition();
        // Find an even-window courier dispatched from a town outside the local InstantNewsBubblePixels so a freshly
        // recorded origin wavefront is genuinely still in transit at the encounter site.
        DaggerfallSiteRecord localSite = TestPayload.Definitions.Locations.Records.Single(r => r.Id == Charing);
        DaggerfallPersistentOverlandBand? courier = null;
        DaggerfallSiteRecord? originSite = null;
        for (long window = 602L; window <= 720L; window += 2L)
        {
            if (window % 4 == 0) continue;
            DaggerfallPersistentOverlandBand? candidate = session.OverlandTraffic.TryDispatchWindow(window, session.Expedition.Position);
            if (candidate is null) continue;
            DaggerfallSiteId? oid = candidate.OriginSite?.Require();
            DaggerfallSiteRecord? os = oid is { } id
                ? TestPayload.Definitions.Locations.Records.FirstOrDefault(r => r.Id == id)
                : null;
            double dist = os is null ? 0d : Math.Sqrt(Math.Pow(os.MapPixelX - localSite.MapPixelX, 2) + Math.Pow(os.MapPixelY - localSite.MapPixelY, 2));
            if (os is not null && dist > session.WordSpread.Tuning.InstantNewsBubblePixels + 1.0)
            {
                courier = candidate;
                originSite = os;
                break;
            }
            _ = session.Expedition.RemovePersistentBand(candidate.BandId);
            session.OverlandParties.RetireBandOffScreen(candidate.BandId);
        }
        Assert.NotNull(courier);
        Assert.NotNull(originSite);
        Assert.Equal(DaggerfallBandKind.Courier, courier!.Kind);

        ActorLedgerEntry member = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(courier.BandId));
        long courierId = member.DurableId;

        DaggerfallCalendar nowCal = ((IDaggerfallExpeditionSurroundings)session).Calendar;
        long nowMinute = nowCal.MinuteNumber;
        int nowDay = checked((int)nowCal.DayNumber);

        // Emit a real public WordSpread wavefront at originSite (still InTransit to localSite).
        DaggerfallWordWavefrontSave publicNews = session.WordSpread.RecordNews(
            "wf:road-news:11083",
            "Bridge Reopened",
            $"The north bridge at {originSite!.Name} has reopened to merchant wagons.",
            originSite,
            nowMinute);
        Assert.False(session.WordSpread.EvaluateAt(publicNews, localSite, nowMinute, nowDay).HasArrived);

        // Stage an unrevealed sealed board-game dispatch with its own linked wavefront.
        DaggerfallWordWavefrontSave sealedWf = session.WordSpread.RecordNews(
            "wf:sealed-dispatch:11083",
            "Sealed Levy",
            "SEALED BOARD MOVE WAVEFRONT DO NOT REVEAL",
            originSite,
            nowMinute);
        _ = session.CourierDispatches.StageDispatch(
            dispatchId: "disp:road-test:11083",
            seatId: "daggerfall",
            actionKind: "mobilize",
            summary: "SEALED BOARD MOVE SUMMARY DO NOT REVEAL",
            origin: originSite,
            destination: localSite,
            currentMinute: nowMinute,
            currentDay: nowDay,
            linkedWavefrontId: sealedWf.EventId,
            stageOverlandBand: false);

        // Intercept the courier and enter 3D scene.
        session.OverlandParties.OnMeetPersistentBand(courier, session.Expedition.Party, DaggerfallEncounterStaging.Travelling);
        session.EnterExpedition();
        Assert.True(session.State.Actors.TryGet(courierId, out _));

        // Talk to the intercepted courier: greeting mentions origin and "courier-news" topic appears.
        DaggerfallDialogueView? open = TalkTo(session, courierId);
        Assert.NotNull(open);
        Assert.Contains(originSite.Name, open!.Greeting, StringComparison.Ordinal);
        Assert.Contains(open.Topics, t => t.Id == "courier-news");

        // Ask for regional courier news -> surfaces the real WordSpread wavefront ahead of the road wave!
        DaggerfallActivationOutcome firstNews = session.Dialogue.Ask(open.Revision, "courier-news", null, null);
        Assert.True(firstNews.Applied);
        Assert.Contains("Ahead of the road posts", firstNews.Message, StringComparison.Ordinal);
        Assert.Contains(publicNews.Body!, firstNews.Message, StringComparison.Ordinal);

        // The road wave itself remains InTransit at localSite (not prematurely broadcast to townsfolk).
        Assert.False(session.WordSpread.EvaluateAt(publicNews, localSite, nowMinute, nowDay).HasArrived);

        // Repeated courier-news queries must NEVER spoil the unrevealed sealed board-game dispatch.
        for (int i = 0; i < 4; i++)
        {
            DaggerfallActivationOutcome more = session.Dialogue.Ask(open.Revision, "courier-news", null, null);
            Assert.True(more.Applied);
            Assert.DoesNotContain("SEALED BOARD MOVE", more.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Save/reload mid-encounter preserves the courier visitor and news topic.
        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, saved);
        Assert.True(restored.State.Actors.TryGet(courierId, out _));
        DaggerfallDialogueView? reopened = TalkTo(restored, courierId);
        Assert.NotNull(reopened);
        Assert.Contains(reopened!.Topics, t => t.Id == "courier-news");

        // Departing to expedition dispatches :return leg; arrival at home clears Location cleanly (no AtSite orphan)
        // while preserving that the player met this courier on the road.
        restored.OpenExpedition();
        Assert.False(restored.State.Actors.TryGet(courierId, out _));
        DaggerfallPersistentOverlandBand returnBand = Assert.Single(restored.Expedition.PersistentBands,
            b => b.BandId.StartsWith(DaggerfallOverlandTraffic.BandIdPrefix, StringComparison.Ordinal)
                && b.BandId.EndsWith(DaggerfallOverlandTraffic.ReturnBandSuffix, StringComparison.Ordinal)
                && restored.State.ActorLedger.EntriesOnOverlandBand(b.BandId).Any(e => e.DurableId == courierId));
        _ = restored.OverlandParties.OnPersistentBandArrived(returnBand);
        if (restored.State.ActorLedger.TryGet(courierId, out ActorLedgerEntry? afterHome))
        {
            Assert.Null(afterHome!.Location);
            Assert.True(afterHome.Social?.Met == true);
        }
    }

    private static DaggerfallDialogueView? TalkTo(DaggerfallSession session, long npcId)
    {
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(),
            t => t.Identity.Value == checked((ulong)npcId));
        DaggerfallActivationOutcome opened = session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target));
        Assert.True(opened.Applied, $"Failed to open dialogue with NPC {npcId}: {opened.Message}");
        return session.ActivationPanel.View.Dialogue;
    }

    private sealed class ExchangeFixture : IDisposable
    {
        private ExchangeFixture(
            DaggerfallSession session,
            DaggerfallSessionComposition composition,
            DaggerfallSiteProfile origin,
            DaggerfallSiteProfile destination,
            EngineContextFake engine)
        {
            Session = session;
            Composition = composition;
            Origin = origin;
            Destination = destination;
            Engine = engine;
        }

        internal DaggerfallSession Session { get; }
        internal DaggerfallSessionComposition Composition { get; }
        internal DaggerfallSiteProfile Origin { get; }
        internal DaggerfallSiteProfile Destination { get; }
        internal EngineContextFake Engine { get; }

        internal static ExchangeFixture Create()
        {
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            DaggerfallSiteRecord originRecord = definitions.Locations.Records.Single(r => r.Id == Charing);
            DaggerfallSiteRecord destRecord = definitions.Locations.Records.First(r =>
                r.Exterior is not null
                && r.Id != originRecord.Id
                && r.Id.Region == originRecord.Id.Region
                && Math.Abs(r.MapPixelX - originRecord.MapPixelX) + Math.Abs(r.MapPixelY - originRecord.MapPixelY) is >= 3 and <= 15);

            DaggerfallSiteProfile template = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
            DaggerfallSiteProfile origin = WithPopulation(template, originRecord.Id, count: 2);
            DaggerfallSiteProfile destination = WithPopulation(template, destRecord.Id, count: 1);

            DaggerfallSessionComposition composition = new(definitions, origin, DaggerfallTuning.Defaults)
            {
                Profiles = new DaggerfallSiteProfiles([origin, destination]),
            };
            EngineContextFake engine = BuildEngine(origin, destination);
            DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
            session.Update(new ProductUpdate(OuterUpdate(1), []));
            return new(session, composition, origin, destination, engine);
        }

        internal EngineContextFake CreateEngine() => BuildEngine(Origin, Destination);

        private static EngineContextFake BuildEngine(DaggerfallSiteProfile origin, DaggerfallSiteProfile destination)
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, origin);
            PopulateContent(content, destination);
            SpatialFake spatial = SpatialFake.Create(origin.Geometry.Spatial[0].Sha256, releases);
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        }

        private static DaggerfallSiteProfile WithPopulation(DaggerfallSiteProfile template, DaggerfallSiteId siteId, int count)
        {
            DaggerfallSiteProfile exterior = SameContentAt(template, siteId, DaggerfallWorldProfileKind.Exterior,
                DaggerfallWorldProfileIds.Exterior(siteId).LogicalId);
            NormalizedBillboardSprite billboard = new("sprite/population.png", Hash, 8, 8,
                [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new Vector2(.5F, .5F), Vector2.One);
            Dictionary<(int Archive, int Record), NormalizedBillboardSprite> billboards = template.BillboardSprites.ToDictionary();
            billboards[(210, 4)] = billboard;
            int factionId = TestPayload.Definitions.Factions.Factions.Values.First(f => f.Id > 0 && f.Type != 10
                && !StringComparer.Ordinal.Equals(f.TypeName, "KnightlyGuard")
                && TestPayload.Definitions.Catalogs.Races.Any(r => r.DonorRaceId == f.Race)).Id;
            DaggerfallPopulationPlacement[] pop = [.. Enumerable.Range(0, count).Select(i =>
                new DaggerfallPopulationPlacement($"population/source/0/{i}", new WorldPoint(1.5F + i, 1F, 1.5F),
                    210, 4, factionId, (byte)(i % 2 == 1 ? 0x20 : 0), (ushort)(300 + i * 19)))];
            return new DaggerfallSiteProfile(
                new ProjectFacts(exterior.Project.PlayerPosition, new Dictionary<long, AuthoredActor>()),
                exterior.Geometry,
                exterior.WorldAppearance,
                exterior.InitialLook,
                exterior.Materials,
                exterior.ActorSprites,
                exterior.MobileSprites,
                exterior.Audio,
                exterior.ClassicPresentation,
                exterior.Site,
                exterior.Doors,
                exterior.ProfileKind,
                exterior.ProfileKey.LogicalId,
                Array.Empty<DaggerfallSitePortal>(),
                exterior.Anchors.Values.ToArray(),
                exterior.Lights,
                exterior.GroundContainerSprites,
                exterior.DungeonMap,
                exterior.DungeonActions,
                exterior.DungeonActionModels,
                exterior.InteriorBuilding,
                exterior.QuestMarkers,
                billboardSprites: billboards,
                terrainTextures: template.TerrainTextures,
                population: pop);
        }

        public void Dispose() => Session.Dispose();
    }
}
