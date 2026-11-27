using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>A trade the player asks a merchant for.</summary>
internal enum DaggerfallMerchantTradeKind
{
    Buy,
    Sell,
    Repair,
    CollectRepair,
    Identify,
    Shoplift,
}

/// <summary>
/// Trading with the merchant the player is talking to: which of the person's merchant services answers a request in
/// the admitted building (or with an intercepted overland caravan merchant on the road), the merchant's view with
/// the quote the player priced, and the trade itself with its wording. The stock, prices and custody are the merchant service's.
/// </summary>
/// <param name="currentNpc">The person the player is talking to, for this dialogue revision (any revision when null).</param>
/// <param name="building">The admitted interior building the player stands in.</param>
/// <param name="dialogueRevision">The open dialogue page's revision.</param>
internal sealed class DaggerfallMerchantTrade(
    DaggerfallMerchantService merchants,
    DaggerfallSiteContext site,
    Func<string?, DaggerfallNpc?> currentNpc,
    Func<DaggerfallInteriorBuilding?> building,
    Func<DaggerfallWorldProfileKey> activeProfile,
    Func<string?> dialogueRevision,
    DaggerfallActorLedger? actorLedger = null)
{
    private static readonly string[] MerchantServices = ["shop", "merchant", "buy-items", "sell-items", "repair", "identify", "sell-magic-items", "buy-potions", "buy-magic-items", "buy-soulgems"];
    private static readonly string[] BuyServices = ["shop", "merchant", "buy-items", "buy-potions", "buy-magic-items", "buy-soulgems"];
    private static readonly string[] SellServices = ["shop", "merchant", "sell-items", "sell-magic-items"];

    private DaggerfallMerchantQuoteView? _quote;

    private DaggerfallMerchantProviderContext? Provider(string? revision = null, string? requestedService = null)
    {
        DaggerfallNpc? npc = currentNpc(DialogueRevision(revision));
        if (npc is null) return null;
        DaggerfallInteriorBuilding? placed = building();
        if (placed is null)
            return RoadCaravanProvider(npc, requestedService);
        string? service = requestedService switch
        {
            "buy" => npc.Services.FirstOrDefault(value => BuyServices.Contains(value, StringComparer.Ordinal)),
            "sell" => npc.Services.FirstOrDefault(value => SellServices.Contains(value, StringComparer.Ordinal)),
            "repair" or "identify" => npc.Services.Contains(requestedService, StringComparer.Ordinal) ? requestedService : null,
            _ => npc.Services.FirstOrDefault(value => MerchantServices.Contains(value, StringComparer.Ordinal)),
        };
        if (service is null) return null;
        DaggerfallSiteBuildingSource source;
        try
        {
            source = site.RequireBuildingSource(activeProfile().Site,
                new(placed.BlockX, placed.BlockY, placed.Building.Index));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        return new(new(npc.DurableId, npc.Site, service), source.Quality, source.Source.BuildingType,
            placed.BlockX, placed.BlockY, placed.Building.Index);
    }

    private DaggerfallMerchantProviderContext? RoadCaravanProvider(DaggerfallNpc npc, string? requestedService)
    {
        if (actorLedger is null || requestedService is "repair" or "identify")
            return null;
        if (!string.Equals(npc.Role, "merchant", StringComparison.OrdinalIgnoreCase)
            || !npc.Services.Contains("merchant", StringComparer.Ordinal))
            return null;
        if (!actorLedger.TryGet(npc.DurableId, out ActorLedgerEntry? entry)
            || entry?.Location is not { Kind: ActorLocationKind.AtSite, TrafficVisitor: true })
            return null;

        int buildingType = DaggerfallOverlandTraffic.CaravanBuildingTypeForSpawner(entry.Origin.SpawnerId);
        int quality = DaggerfallOverlandTraffic.CaravanQualityForActor(npc.DurableId);
        int originRegion = entry.Origin.SiteProfile?.Require().Site.Region ?? npc.Site.Region;
        int localRegion = activeProfile().Site.Region;
        return new DaggerfallMerchantProviderContext(
            new DaggerfallServiceProvider(npc.DurableId, npc.Site, "merchant"),
            quality,
            buildingType,
            BlockX: 0,
            BlockY: 0,
            BuildingIndex: 0,
            PricingRegion: localRegion,
            PricingOriginRegion: originRegion,
            IsRoadCaravan: true);
    }

    /// <summary>The merchant the player is talking to, with the quote the player priced on this revision.</summary>
    internal DaggerfallMerchantView? Read()
    {
        DaggerfallMerchantProviderContext? context = Provider();
        if (context is null || merchants.Read(context, dialogueRevision: dialogueRevision()) is not { } view) return null;
        return view with { Quote = _quote is { } quote && quote.Revision == view.Revision ? quote : null };
    }

    /// <summary>Prices the amount the player chose on a row, so the screen shows and confirms what the trade will charge.</summary>
    internal void Quote(string revision, string item, bool buying, ulong amount)
    {
        DaggerfallMerchantProviderContext? context = Provider(revision, buying ? "buy" : "sell");
        _quote = context is not null && merchants.TradeQuote(context, revision, item, buying, amount) is { } quote
            ? new(revision, item, buying, amount, quote.Total, quote.Breakdown) : null;
    }

    /// <summary>Applies a trade with the merchant on this revision.</summary>
    internal DaggerfallMerchantResult Trade(DaggerfallMerchantTradeKind kind, string? revision, string? item, string? key, ulong? amount, bool confirm)
    {
        DaggerfallMerchantProviderContext? context = Provider(revision, kind switch
        {
            DaggerfallMerchantTradeKind.Buy or DaggerfallMerchantTradeKind.Shoplift => "buy",
            DaggerfallMerchantTradeKind.Sell => "sell",
            DaggerfallMerchantTradeKind.Repair or DaggerfallMerchantTradeKind.CollectRepair => "repair",
            DaggerfallMerchantTradeKind.Identify => "identify",
            _ => null,
        });
        return context is null
            ? new(false, "ProviderUnavailable")
            : kind switch
            {
                DaggerfallMerchantTradeKind.Buy => merchants.Buy(context, revision!, item!, amount ?? 1, confirm),
                DaggerfallMerchantTradeKind.Sell => merchants.Sell(context, revision!, item!, amount ?? 1, confirm),
                DaggerfallMerchantTradeKind.Repair => merchants.RequestRepair(context, revision!, item!),
                DaggerfallMerchantTradeKind.CollectRepair => merchants.CollectRepair(context, revision!, key!),
                DaggerfallMerchantTradeKind.Identify => merchants.Identify(context, revision!, item!),
                DaggerfallMerchantTradeKind.Shoplift => merchants.Shoplift(context, revision!, item!, amount ?? 1),
                _ => new(false, "Unsupported"),
            };
    }

    /// <summary>
    /// Player wording for a merchant result. The service keeps its result codes for its callers; the
    /// player reads what happened and, for a paid service, what it cost.
    /// </summary>
    internal static string OutcomeText(DaggerfallMerchantResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string gold = result.PaidGold.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return result.Outcome switch
        {
            "Purchased" => $"Bought for {gold} gold.",
            "Sold" => $"Sold for {gold} gold.",
            "Stolen" => "You took it without being seen.",
            "Caught" => "You were caught stealing.",
            "RepairAccepted" => $"Paid {gold} gold for the repair. Collect the item when it is ready.",
            "RepairCollected" => "You collected your repaired item.",
            "Identified" => result.PaidGold == 0 ? "The item was identified free of charge." : $"Identified for {gold} gold.",
            "Stale" => "The shop's goods changed. Choose the item again.",
            "ItemUnavailable" => "That item is no longer available.",
            "ItemNotAccepted" => "This shop does not buy that kind of item.",
            "QuestItem" => DaggerfallQuestItemExits.Refused,
            "QuoteUnavailable" => "The merchant will not trade that item.",
            "ConfirmationRequired" => "Confirm a trade that large first.",
            "AlreadyRepaired" => "That item does not need repairing.",
            "AlreadyIdentified" => "That item is already identified.",
            "RepairNotReady" => "That repair is not finished yet.",
            "RepairUnavailable" => "That repair order is no longer available.",
            "Capacity" => "You cannot carry that much.",
            "AlreadyAttempted" => "You have already tried to steal that.",
            _ => DaggerfallServiceOutcomeText.Common(result.Outcome),
        };
    }

    private static string? DialogueRevision(string? merchantRevision)
    {
        if (merchantRevision is null) return null;
        int separator = merchantRevision.IndexOf('|');
        return separator < 0 ? merchantRevision : merchantRevision[..separator];
    }
}
