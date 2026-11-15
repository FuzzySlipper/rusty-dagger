using System.Globalization;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Property;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// An accessible property's storage: its rows as the inventory describes them, and the carried
/// items it will not take now with the reason, so the transfer screen offers only what the store accepts.
/// </summary>
internal sealed record DaggerfallPropertyStorageView(string Key, string Revision, InventoryItemPresentation[] Items, DaggerfallTransferRefusal[] StoreRefusals);
/// <summary>A carried item a transfer refuses, and why.</summary>
internal sealed record DaggerfallTransferRefusal(string Item, string Reason);
internal sealed record DaggerfallPropertyView(bool BankAvailable, DaggerfallPropertyOfferView[] Offers, DaggerfallPropertyStorageView? Storage);

/// <summary>
/// The service panels' actions: the bank and its loans, the property panel, the merchant, the tavern, the tavern's
/// rooms and training. Each reads its action and asks Economy's owner, then says the outcome.
/// </summary>
/// <param name="dialogue">The conversation the player is in, which activation rebuilds with each site.</param>
/// <param name="building">The interior building the player stands in.</param>
/// <param name="transportAccess">Where the player's transport can be reached from here.</param>
/// <param name="requestPanel">Asks the DOM for a panel.</param>
/// <param name="enterInterior">Session: the crossing into a house's interior.</param>
internal sealed class DaggerfallEconomyPanels(DaggerfallBanking banking, DaggerfallRegionalBankState bank, DaggerfallMerchantTrade merchantTrade,
    DaggerfallRoomRental roomRental, DaggerfallTavernService tavern, DaggerfallSkillTrainingService skillTraining,
    DaggerfallPropertyHoldings holdings, MechanicsInventoryCoordinator inventory, DaggerfallWagonStorage wagon,
    DaggerfallInventoryPresentation inventoryUi, DaggerfallDefinitions definitions, Func<DaggerfallDialogueService?> dialogue,
    Func<DaggerfallInteriorBuilding?> building, Func<DaggerfallTransportAccessContext> transportAccess, Action<string> requestPanel,
    Func<DaggerfallWorldProfileKey, bool> enterInterior, PresentationState presentation)
{
    internal void BuyTavernMeal(DaggerfallPlayerUiAction action)
    {
        if (action.Revision is not { } revision || action.Key is not { } item || action.Amount is not { } price
            || dialogue()?.CurrentNpc(revision) is not { } npc || tavern.ReadAt(npc, building) is null)
        { presentation.SetOutcome("That tavern keeper is no longer available."); return; }
        presentation.SetOutcome(tavern.BuyMeal(revision, npc, item, price));
    }

    internal void ApplyTraining(DaggerfallPlayerUiAction action)
    {
        (string line, bool accepted) = skillTraining.Train(action.Revision, action.Key, action.Amount, action.Confirm,
            revision => dialogue()?.CurrentNpc(revision));
        presentation.SetOutcome(line);
        if (accepted) dialogue()?.RefreshEligibility();
    }

    /// <summary>The merchant the player is talking to, each row drawing the icon its item would draw in the pack.</summary>
    internal DaggerfallMerchantView? ReadCurrentMerchant()
    {
        if (merchantTrade.Read() is not { } view) return null;
        DaggerfallMerchantItemView WithIcon(DaggerfallMerchantItemView row)
        {
            (string? icon, PaperDollTint? tint) = inventoryUi.Icon(definitions.RequireItem(new DaggerfallItemId(row.Definition)), row.IconMetadata);
            return row with { Icon = icon, IconTint = tint };
        }
        return view with { Stock = [.. view.Stock.Select(WithIcon)], PlayerItems = [.. view.PlayerItems.Select(WithIcon)] };
    }

    internal void QuoteMerchantTrade(DaggerfallPlayerUiAction action) =>
        merchantTrade.Quote(action.Revision!, action.Item!, action.Key == "buy", action.Amount!.Value);

    internal void ChangeMerchant(DaggerfallPlayerUiAction action)
    {
        DaggerfallMerchantTradeKind kind = action.Kind switch
        {
            DaggerfallUiActionKind.MerchantBuy => DaggerfallMerchantTradeKind.Buy,
            DaggerfallUiActionKind.MerchantSell => DaggerfallMerchantTradeKind.Sell,
            DaggerfallUiActionKind.MerchantRepair => DaggerfallMerchantTradeKind.Repair,
            DaggerfallUiActionKind.MerchantCollectRepair => DaggerfallMerchantTradeKind.CollectRepair,
            DaggerfallUiActionKind.MerchantIdentify => DaggerfallMerchantTradeKind.Identify,
            DaggerfallUiActionKind.MerchantShoplift => DaggerfallMerchantTradeKind.Shoplift,
            _ => throw new ArgumentException($"'{action.Action}' is not a merchant trade.", nameof(action)),
        };
        presentation.SetOutcome(DaggerfallMerchantTrade.OutcomeText(
            merchantTrade.Trade(kind, action.Revision, action.Item, action.Key, action.Amount, action.Confirm)));
        dialogue()?.RefreshEligibility();
    }

    internal void ChangeLodging(DaggerfallPlayerUiAction action)
    {
        if (roomRental.Choose(action.Key, action.Days!.Value, action.Kind == DaggerfallUiActionKind.LodgingBook, action.Amount) is { } line)
            presentation.SetOutcome(line);
    }

    internal void OpenCurrentBank(string? revision)
    {
        if (!banking.TryOpenWith(dialogue()?.CurrentNpc(revision)))
        { presentation.SetOutcome("That bank service is no longer available."); return; }
        dialogue()!.Close();
        requestPanel(DaggerfallPanel.Inventory);
        presentation.SetOutcome("Bank services opened.");
    }

    internal void ChangeCurrency(DaggerfallPlayerUiAction action)
    {
        if (banking.ActiveRegion is not int region)
        {
            presentation.SetOutcome(DaggerfallBanking.Unavailable);
            return;
        }
        long amount = action.Amount is ulong requested
            ? requested > long.MaxValue ? long.MaxValue : (long)requested
            : 0;
        DaggerfallBankTransactionOutcome outcome = action.Kind switch
        {
            DaggerfallUiActionKind.CurrencyDepositGold => bank.DepositGold(region, amount, wagon, transportAccess()),
            DaggerfallUiActionKind.CurrencyWithdrawGold => bank.WithdrawGold(region, amount),
            DaggerfallUiActionKind.CurrencyDepositLetters => bank.DepositLetters(region),
            DaggerfallUiActionKind.CurrencyWithdrawLetter => bank.WithdrawLetter(region, amount),
            DaggerfallUiActionKind.BankTransfer => bank.Transfer(region, action.Destination ?? -1, amount),
            _ => throw new ArgumentException($"'{action.Action}' is not a currency or bank operation.", nameof(action)),
        };
        inventoryUi.ReportBankTransaction(outcome);
        presentation.SetOutcome(inventoryUi.Message);
    }

    internal void ChangeLoan(DaggerfallPlayerUiAction action) => presentation.SetOutcome(action.Kind == DaggerfallUiActionKind.BankLoanIssue
        ? banking.Borrow(action.Amount)
        : banking.Repay(action.Amount, fromAccount: action.Kind == DaggerfallUiActionKind.BankLoanRepayAccount));

    /// <summary>The property panel: the houses and ships offered here, and the storage the player can reach with its rows.</summary>
    internal DaggerfallPropertyView ReadPropertyPresentation()
    {
        bool bankOpen = banking.ActiveRegion is not null;
        DaggerfallPropertyOfferView[] offers = holdings.Offers(bankOpen);
        DaggerfallPropertyStorageView? storage = null;
        if (holdings.AccessibleStorage() is { } key)
        {
            InventoryView contents = holdings.Storage.Read(key);
            storage = new(key.Value, contents.StoreRevision.ToString(CultureInfo.InvariantCulture), PropertyItems(contents, holdings.Storage.DaggerfallItemOwnerFor(key)),
                [.. inventoryUi.Read().Items.Where(item => item.EquippedSlots.Length > 0).Select(item => new DaggerfallTransferRefusal(item.Key, UnequipBeforeStoring))]);
        }
        return new(bankOpen, offers, storage);
    }

    internal void ChangeProperty(DaggerfallPlayerUiAction action)
    {
        if (action.Kind is DaggerfallUiActionKind.PropertyPut or DaggerfallUiActionKind.PropertyTake)
        { ChangePropertyStorage(action); return; }
        DaggerfallHouseOffer? house = holdings.HouseOffer(action.Key);
        if (action.Kind == DaggerfallUiActionKind.PropertyEnter)
        {
            if (holdings.Entrance(house) is not { } interior)
            { presentation.SetOutcome("Stand at your house entrance to enter the admitted interior."); return; }
            presentation.SetOutcome(enterInterior(interior) ? "Entered your house." : "The house interior is unavailable.");
            return;
        }
        if (banking.ActiveRegion is null) { presentation.SetOutcome(DaggerfallBanking.Unavailable); return; }
        presentation.SetOutcome(holdings.Trade(action.Kind == DaggerfallUiActionKind.PropertyBuy, house, action.Key)?.Message
            ?? "That property offer is no longer available.");
    }

    private void ChangePropertyStorage(DaggerfallPlayerUiAction action)
    {
        if (holdings.AccessibleStorage() is not { } key || key.Value != action.Key)
        { presentation.SetOutcome("You must be inside your property to use its storage."); return; }
        bool put = action.Kind == DaggerfallUiActionKind.PropertyPut;
        InventoryView source = put ? inventory.Read() : holdings.Storage.Read(key);
        if (!ulong.TryParse(action.Revision, out ulong revision) || source.StoreRevision != revision
            || action.Item is null || !DaggerfallContainerSelection.TryParse(source, action.Item, action.Amount, out InventoryContainerSelection? selection))
        { presentation.SetOutcome("Inventory changed. Choose the item again."); return; }
        if (put && inventoryUi.Read().Items.Any(item => item.Key == action.Item && item.EquippedSlots.Length > 0))
        { presentation.SetOutcome(UnequipBeforeStoring); return; }
        try
        {
            if (put) holdings.Storage.TransferTo(key, selection!, revision);
            else holdings.Storage.TransferFrom(key, selection!, revision);
            presentation.SetOutcome(put ? "Item stored in property." : "Item taken from property.");
        }
        catch (DaggerfallItemExitRefusedException refused) { presentation.SetOutcome(refused.Message); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { presentation.SetOutcome(put ? "That item cannot be stored here." : "That item cannot be taken from storage."); }
    }

    private const string UnequipBeforeStoring = "Unequip that item before storing it.";

    private InventoryItemPresentation[] PropertyItems(InventoryView contents, DaggerfallItemOwner owner) =>
        [.. contents.UniqueItems.Select(item => inventoryUi.DescribeItem($"unique:{item.Entity.Value}", item.Definition.Value, 1, owner: owner)),
            .. contents.Stacks.Select(item => inventoryUi.DescribeItem($"stack:{item.Id.Value}", item.Definition.Value, item.Quantity, owner: owner))];
}
