using System.Globalization;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
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
/// rooms and training.
/// Each reads its action and asks Economy's owner, then says the outcome.
/// </summary>
internal sealed partial class DaggerfallSession
{
    private void BuyTavernMeal(DaggerfallPlayerUiAction action)
    {
        if (action.Revision is not { } revision || action.Key is not { } item || action.Amount is not { } price
            || _dialogue?.CurrentNpc(revision) is not { } npc || State.Tavern.ReadAt(npc, CurrentInteriorBuilding) is null)
        { Presentation.SetOutcome("That tavern keeper is no longer available."); return; }
        Presentation.SetOutcome(State.Tavern.BuyMeal(revision, npc, item, price));
    }

    private void ApplyTrainingAction(DaggerfallPlayerUiAction action)
    {
        (string line, bool accepted) = State.SkillTraining.Train(action.Revision, action.Key, action.Amount, action.Confirm,
            revision => _dialogue?.CurrentNpc(revision));
        Presentation.SetOutcome(line);
        if (accepted) _dialogue?.RefreshEligibility();
    }

    /// <summary>The merchant the player is talking to, each row drawing the icon its item would draw in the pack.</summary>
    internal DaggerfallMerchantView? ReadCurrentMerchant()
    {
        if (MerchantTrade.Read() is not { } view) return null;
        DaggerfallMerchantItemView WithIcon(DaggerfallMerchantItemView row)
        {
            (string? icon, PaperDollTint? tint) = _inventoryUi.Icon(_definitions.RequireItem(new DaggerfallItemId(row.Definition)), row.IconMetadata);
            return row with { Icon = icon, IconTint = tint };
        }
        return view with { Stock = [.. view.Stock.Select(WithIcon)], PlayerItems = [.. view.PlayerItems.Select(WithIcon)] };
    }

    private void QuoteMerchantTrade(DaggerfallPlayerUiAction action) =>
        MerchantTrade.Quote(action.Revision!, action.Item!, action.Key == "buy", action.Amount!.Value);

    private void ChangeMerchant(DaggerfallPlayerUiAction action)
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
        Presentation.SetOutcome(DaggerfallMerchantTrade.OutcomeText(
            MerchantTrade.Trade(kind, action.Revision, action.Item, action.Key, action.Amount, action.Confirm)));
        _dialogue?.RefreshEligibility();
    }

    private void ChangeLodging(DaggerfallPlayerUiAction action)
    {
        if (RoomRental.Choose(action.Key, action.Days!.Value, action.Kind == DaggerfallUiActionKind.LodgingBook, action.Amount) is { } line)
            Presentation.SetOutcome(line);
    }

    private void OpenCurrentBank(string? revision)
    {
        if (!Banking.TryOpenWith(_dialogue?.CurrentNpc(revision)))
        { Presentation.SetOutcome("That bank service is no longer available."); return; }
        _dialogue!.Close();
        RequestPanel(DaggerfallPanel.Inventory);
        Presentation.SetOutcome("Bank services opened.");
    }

    private void ChangeCurrency(DaggerfallPlayerUiAction action)
    {
        if (Banking.ActiveRegion is not int region)
        {
            Presentation.SetOutcome(DaggerfallBanking.Unavailable);
            return;
        }
        long amount = action.Amount is ulong requested
            ? requested > long.MaxValue ? long.MaxValue : (long)requested
            : 0;
        DaggerfallBankTransactionOutcome outcome = action.Kind switch
        {
            DaggerfallUiActionKind.CurrencyDepositGold => State.Bank.DepositGold(region, amount, State.Wagon, TransportAccess()),
            DaggerfallUiActionKind.CurrencyWithdrawGold => State.Bank.WithdrawGold(region, amount),
            DaggerfallUiActionKind.CurrencyDepositLetters => State.Bank.DepositLetters(region),
            DaggerfallUiActionKind.CurrencyWithdrawLetter => State.Bank.WithdrawLetter(region, amount),
            DaggerfallUiActionKind.BankTransfer => State.Bank.Transfer(region, action.Destination ?? -1, amount),
            _ => throw new ArgumentException($"'{action.Action}' is not a currency or bank operation.", nameof(action)),
        };
        _inventoryUi.ReportBankTransaction(outcome);
        Presentation.SetOutcome(_inventoryUi.Message);
    }

    private void ChangeLoan(DaggerfallPlayerUiAction action) => Presentation.SetOutcome(action.Kind == DaggerfallUiActionKind.BankLoanIssue
        ? Banking.Borrow(action.Amount)
        : Banking.Repay(action.Amount, fromAccount: action.Kind == DaggerfallUiActionKind.BankLoanRepayAccount));

    /// <summary>The property panel: the houses and ships offered here, and the storage the player can reach with its rows.</summary>
    internal DaggerfallPropertyView ReadPropertyPresentation()
    {
        bool bank = Banking.ActiveRegion is not null;
        DaggerfallPropertyOfferView[] offers = Holdings.Offers(bank);
        DaggerfallPropertyStorageView? storage = null;
        if (Holdings.AccessibleStorage() is { } key)
        {
            InventoryView contents = Holdings.Storage.Read(key);
            storage = new(key.Value, contents.StoreRevision.ToString(CultureInfo.InvariantCulture), PropertyItems(contents, Holdings.Storage.DaggerfallItemOwnerFor(key)),
                [.. _inventoryUi.Read().Items.Where(item => item.EquippedSlots.Length > 0).Select(item => new DaggerfallTransferRefusal(item.Key, UnequipBeforeStoring))]);
        }
        return new(bank, offers, storage);
    }

    private void ChangeProperty(DaggerfallPlayerUiAction action)
    {
        if (action.Kind is DaggerfallUiActionKind.PropertyPut or DaggerfallUiActionKind.PropertyTake)
        { ChangePropertyStorage(action); return; }
        DaggerfallHouseOffer? house = Holdings.HouseOffer(action.Key);
        if (action.Kind == DaggerfallUiActionKind.PropertyEnter)
        {
            if (Holdings.Entrance(house) is not { } interior)
            { Presentation.SetOutcome("Stand at your house entrance to enter the admitted interior."); return; }
            Presentation.SetOutcome(TryTransitionTo(interior) ? "Entered your house." : "The house interior is unavailable.");
            return;
        }
        if (Banking.ActiveRegion is null) { Presentation.SetOutcome(DaggerfallBanking.Unavailable); return; }
        Presentation.SetOutcome(Holdings.Trade(action.Kind == DaggerfallUiActionKind.PropertyBuy, house, action.Key)?.Message
            ?? "That property offer is no longer available.");
    }

    private void ChangePropertyStorage(DaggerfallPlayerUiAction action)
    {
        if (Holdings.AccessibleStorage() is not { } key || key.Value != action.Key)
        { Presentation.SetOutcome("You must be inside your property to use its storage."); return; }
        bool put = action.Kind == DaggerfallUiActionKind.PropertyPut;
        InventoryView source = put ? State.Inventory.Read() : Holdings.Storage.Read(key);
        if (!ulong.TryParse(action.Revision, out ulong revision) || source.StoreRevision != revision
            || action.Item is null || !DaggerfallContainerSelection.TryParse(source, action.Item, action.Amount, out InventoryContainerSelection? selection))
        { Presentation.SetOutcome("Inventory changed. Choose the item again."); return; }
        if (put && _inventoryUi.Read().Items.Any(item => item.Key == action.Item && item.EquippedSlots.Length > 0))
        { Presentation.SetOutcome(UnequipBeforeStoring); return; }
        try
        {
            if (put) Holdings.Storage.TransferTo(key, selection!, revision);
            else Holdings.Storage.TransferFrom(key, selection!, revision);
            Presentation.SetOutcome(put ? "Item stored in property." : "Item taken from property.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { Presentation.SetOutcome(put ? "That item cannot be stored here." : "That item cannot be taken from storage."); }
    }

    private const string UnequipBeforeStoring = "Unequip that item before storing it.";

    private InventoryItemPresentation[] PropertyItems(InventoryView contents, DaggerfallItemOwner owner) =>
        [.. contents.UniqueItems.Select(item => _inventoryUi.DescribeItem($"unique:{item.Entity.Value}", item.Definition.Value, 1, owner: owner)),
            .. contents.Stacks.Select(item => _inventoryUi.DescribeItem($"stack:{item.Id.Value}", item.Definition.Value, item.Quantity, owner: owner))];
}
