using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The service panels' actions: the bank and its loans, the merchant, the tavern, the tavern's rooms and training.
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
}
