using WorldRpg.Kit.Inventory;

namespace WorldRpg.Rulesets.Daggerfall.Banking;

internal enum DaggerfallLoanSettlementResult
{
    Applied,
    InvalidRegion,
    InvalidAmount,
    AccountMovementRejected,
    InventoryMovementRejected,
}

/// <summary>The result of one real account or inventory movement for a loan.</summary>
internal readonly record struct DaggerfallLoanSettlementOutcome(
    DaggerfallLoanSettlementResult Result,
    int Region,
    ulong Amount)
{
    internal bool Applied => Result == DaggerfallLoanSettlementResult.Applied;
}

/// <summary>
/// Routes loan money through current owners without retaining an account or wallet balance. Account
/// delegates must be atomic operations on <see cref="DaggerfallRegionalBankState"/>; the currency
/// factory uses the existing inventory-backed currency owner for carried coins and letters.
/// </summary>
internal sealed class DaggerfallLoanSettlementAdapter
{
    private readonly Func<int, ulong, bool> _creditAccount;
    private readonly Func<int, ulong, bool> _debitAccount;
    private readonly Func<ulong, bool> _spendInventoryGold;
    private readonly Func<ulong, bool> _spendCarried;

    internal DaggerfallLoanSettlementAdapter(
        Func<int, ulong, bool> creditAccount,
        Func<int, ulong, bool> debitAccount,
        Func<ulong, bool> spendInventoryGold,
        Func<ulong, bool>? spendCarried = null)
    {
        _creditAccount = creditAccount ?? throw new ArgumentNullException(nameof(creditAccount));
        _debitAccount = debitAccount ?? throw new ArgumentNullException(nameof(debitAccount));
        _spendInventoryGold = spendInventoryGold ?? throw new ArgumentNullException(nameof(spendInventoryGold));
        _spendCarried = spendCarried ?? spendInventoryGold;
    }

    /// <summary>Builds the inventory leg over the existing currency owner.</summary>
    internal static DaggerfallLoanSettlementAdapter ForCurrency(
        DaggerfallCurrencyService currency,
        Func<int, ulong, bool> creditAccount,
        Func<int, ulong, bool> debitAccount)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new(creditAccount, debitAccount,
            amount => currency.TrySpendGold(amount, Array.Empty<InventoryAtomicGrant>()),
            currency.TrySpendCarried);
    }

    /// <summary>Binds both legs to the current regional bank and currency owners.</summary>
    internal static DaggerfallLoanSettlementAdapter ForBank(
        DaggerfallRegionalBankState bank,
        DaggerfallCurrencyService currency)
    {
        ArgumentNullException.ThrowIfNull(bank);
        return ForCurrency(currency, bank.TryCreditAccount, bank.TryDebitAccount);
    }

    /// <summary>Credits the regional account with newly issued funds.</summary>
    internal DaggerfallLoanSettlementOutcome Issue(int region, ulong amount)
    {
        DaggerfallLoanSettlementOutcome invalid = Validate(region, amount);
        if (invalid.Result != DaggerfallLoanSettlementResult.Applied)
            return invalid;
        return _creditAccount(region, amount)
            ? new(DaggerfallLoanSettlementResult.Applied, region, amount)
            : new(DaggerfallLoanSettlementResult.AccountMovementRejected, region, amount);
    }

    /// <summary>Debits an existing regional account for an account-funded repayment.</summary>
    internal DaggerfallLoanSettlementOutcome RepayFromAccount(int region, ulong amount)
    {
        DaggerfallLoanSettlementOutcome invalid = Validate(region, amount);
        if (invalid.Result != DaggerfallLoanSettlementResult.Applied)
            return invalid;
        return _debitAccount(region, amount)
            ? new(DaggerfallLoanSettlementResult.Applied, region, amount)
            : new(DaggerfallLoanSettlementResult.AccountMovementRejected, region, amount);
    }

    /// <summary>Consumes carried gold through the existing Engine-backed currency owner.</summary>
    internal DaggerfallLoanSettlementOutcome RepayFromInventory(int region, ulong amount)
    {
        DaggerfallLoanSettlementOutcome invalid = Validate(region, amount);
        if (invalid.Result != DaggerfallLoanSettlementResult.Applied)
            return invalid;
        return _spendInventoryGold(amount)
            ? new(DaggerfallLoanSettlementResult.Applied, region, amount)
            : new(DaggerfallLoanSettlementResult.InventoryMovementRejected, region, amount);
    }

    internal DaggerfallLoanSettlementOutcome RepayFromCarried(int region, ulong amount)
    {
        DaggerfallLoanSettlementOutcome invalid = Validate(region, amount);
        if (invalid.Result != DaggerfallLoanSettlementResult.Applied) return invalid;
        return _spendCarried(amount)
            ? new(DaggerfallLoanSettlementResult.Applied, region, amount)
            : new(DaggerfallLoanSettlementResult.InventoryMovementRejected, region, amount);
    }

    private static DaggerfallLoanSettlementOutcome Validate(int region, ulong amount)
    {
        if (!DaggerfallRegionalBankPolicy.IsValidRegion(region))
            return new(DaggerfallLoanSettlementResult.InvalidRegion, region, amount);
        if (amount == 0 || amount > (ulong)DaggerfallRegionalBankPolicy.MaximumTransactionAmount)
            return new(DaggerfallLoanSettlementResult.InvalidAmount, region, amount);
        return new(DaggerfallLoanSettlementResult.Applied, region, amount);
    }
}
