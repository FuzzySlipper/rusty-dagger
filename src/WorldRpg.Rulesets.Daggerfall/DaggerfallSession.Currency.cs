using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Banking;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{

    /// <summary>Opens banking only for a live, site-bound NPC that actually offers it.</summary>
    internal bool TryOpenBank(DaggerfallServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        provider.Validate();
        if (!string.Equals(provider.Service, "banking", StringComparison.Ordinal)
            || State.Services.ProviderAvailable(provider) != DaggerfallServiceDenial.None)
            return false;
        _sites.BankProvider = provider;
        return true;
    }

    private int? ActiveBankRegion() => _sites.BankProvider is { } provider
        && State.Services.ProviderAvailable(provider) == DaggerfallServiceDenial.None
        ? provider.Site.Region
        : null;

    private void ChangeCurrency(DaggerfallPlayerUiAction action)
    {
        if (ActiveBankRegion() is not int region)
        {
            Presentation.SetOutcome("A bank service is not available here.");
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
}
