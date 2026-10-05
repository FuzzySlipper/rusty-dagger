using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Resolves merchant actions from the current dialogue NPC and the admitted interior source.</summary>
internal sealed partial class DaggerfallSession
{
    private static readonly string[] MerchantServices = ["shop", "merchant", "buy-items", "sell-items", "repair", "identify", "buy-potions"];
    private static readonly string[] BuyServices = ["shop", "merchant", "buy-items", "buy-potions"];
    private static readonly string[] SellServices = ["shop", "merchant", "sell-items"];

    private DaggerfallMerchantProviderContext? CurrentMerchantProvider(string? revision = null, string? requestedService = null)
    {
        DaggerfallNpc? npc = _dialogue?.CurrentNpc(DialogueRevision(revision));
        DaggerfallInteriorBuilding? building = CurrentInteriorBuilding();
        if (npc is null || building is null) return null;
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
            source = _site.RequireBuildingSource(_activeProfileKey.Site,
                new(building.BlockX, building.BlockY, building.Building.Index));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        return new(new(npc.DurableId, npc.Site, service), source.Quality, source.Source.BuildingType,
            building.BlockX, building.BlockY, building.Building.Index);
    }

    internal DaggerfallMerchantView? ReadCurrentMerchant()
    {
        DaggerfallMerchantProviderContext? context = CurrentMerchantProvider();
        return context is null ? null : State.Merchants.Read(context, dialogueRevision: _activationPresentation.View.Dialogue?.Revision);
    }

    internal void ChangeMerchant(DaggerfallPlayerUiAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        string? service = action.Kind switch
        {
            DaggerfallUiActionKind.MerchantBuy or DaggerfallUiActionKind.MerchantShoplift => "buy",
            DaggerfallUiActionKind.MerchantSell => "sell",
            DaggerfallUiActionKind.MerchantRepair or DaggerfallUiActionKind.MerchantCollectRepair => "repair",
            DaggerfallUiActionKind.MerchantIdentify => "identify",
            _ => null,
        };
        DaggerfallMerchantProviderContext? context = CurrentMerchantProvider(action.Revision, service);
        DaggerfallMerchantResult result = context is null
            ? new(false, "ProviderUnavailable")
            : action.Kind switch
            {
                DaggerfallUiActionKind.MerchantBuy => State.Merchants.Buy(context, action.Revision!, action.Item!, action.Amount ?? 1),
                DaggerfallUiActionKind.MerchantSell => State.Merchants.Sell(context, action.Revision!, action.Item!, action.Amount ?? 1),
                DaggerfallUiActionKind.MerchantRepair => State.Merchants.RequestRepair(context, action.Revision!, action.Item!),
                DaggerfallUiActionKind.MerchantCollectRepair => State.Merchants.CollectRepair(context, action.Revision!, action.Key!),
                DaggerfallUiActionKind.MerchantIdentify => State.Merchants.Identify(context, action.Revision!, action.Item!),
                DaggerfallUiActionKind.MerchantShoplift => State.Merchants.Shoplift(context, action.Revision!, action.Item!, action.Amount ?? 1),
                _ => new(false, "Unsupported"),
            };
        Presentation.SetOutcome(result.Outcome);
        _dialogue?.RefreshEligibility();
    }

    private static string? DialogueRevision(string? merchantRevision)
    {
        if (merchantRevision is null) return null;
        int separator = merchantRevision.IndexOf('|');
        return separator < 0 ? merchantRevision : merchantRevision[..separator];
    }
}
