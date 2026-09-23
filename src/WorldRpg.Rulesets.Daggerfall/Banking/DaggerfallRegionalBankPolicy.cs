namespace WorldRpg.Rulesets.Daggerfall.Banking;

/// <summary>Bounds and arithmetic retained from Daggerfall's regional bank records.</summary>
internal static class DaggerfallRegionalBankPolicy
{
    internal const int RegionCount = 62;
    internal const long MaximumTransactionAmount = int.MaxValue;
    internal const ulong MaximumAccountBalance = int.MaxValue;
    internal const long MinimumLetterOfCredit = 100;
    internal const long LetterCommissionPercent = 1;

    internal static bool IsValidAmount(long amount) => amount is > 0 and <= MaximumTransactionAmount;

    internal static bool IsValidRegion(int region) => region is >= 0 and < RegionCount;

    internal static bool TryLetterDebit(long amount, out long debit, out long fee)
    {
        debit = 0;
        fee = 0;
        if (amount < MinimumLetterOfCredit || amount > MaximumTransactionAmount)
            return false;

        fee = checked(amount * LetterCommissionPercent / 100);
        debit = checked(amount + fee);
        return debit <= MaximumTransactionAmount;
    }
}

internal enum DaggerfallBankTransactionKind
{
    DepositGold,
    WithdrawGold,
    DepositLettersOfCredit,
    WithdrawLetterOfCredit,
    Transfer,
}

internal enum DaggerfallBankTransactionResult
{
    Applied,
    InvalidRegion,
    SameRegion,
    InvalidAmount,
    LetterTooSmall,
    SourceLimitExceeded,
    InsufficientAccountBalance,
    AccountBalanceLimit,
    CurrencyMovementRejected,
    WagonUnavailable,
    InvalidWagonGoldOwnership,
    WagonTransferPartiallyApplied,
    NoLettersOfCredit,
    InvalidLetterOwnership,
    InvalidLetterMetadata,
    LedgerMismatch,
}

internal sealed record DaggerfallBankAccountSave(int Region, ulong Gold);

/// <summary>Only bank meaning is saved here; carried gold and letters remain in Engine inventories.</summary>
internal sealed record DaggerfallRegionalBankSave(DaggerfallBankAccountSave[] Accounts)
{
    internal static DaggerfallRegionalBankSave Empty { get; } = new(
        Enumerable.Range(0, DaggerfallRegionalBankPolicy.RegionCount)
            .Select(region => new DaggerfallBankAccountSave(region, 0))
            .ToArray());

    internal DaggerfallRegionalBankSave Validate()
    {
        ArgumentNullException.ThrowIfNull(Accounts);
        if (Accounts.Length != DaggerfallRegionalBankPolicy.RegionCount)
            throw new ArgumentException($"Regional bank state requires {DaggerfallRegionalBankPolicy.RegionCount} accounts.", nameof(Accounts));

        HashSet<int> regions = [];
        foreach (DaggerfallBankAccountSave account in Accounts)
        {
            ArgumentNullException.ThrowIfNull(account);
            if (!DaggerfallRegionalBankPolicy.IsValidRegion(account.Region))
                throw new ArgumentOutOfRangeException(nameof(Accounts), $"Bank account region {account.Region} is outside the classic region range.");
            if (account.Gold > DaggerfallRegionalBankPolicy.MaximumAccountBalance)
                throw new ArgumentOutOfRangeException(nameof(Accounts), $"Bank account {account.Region} exceeds the donor account bound.");
            if (!regions.Add(account.Region))
                throw new ArgumentException($"Regional bank state repeats region {account.Region}.", nameof(Accounts));
        }

        return this;
    }
}

internal sealed record DaggerfallBankTransactionOutcome(
    DaggerfallBankTransactionKind Kind,
    DaggerfallBankTransactionResult Result,
    int? SourceRegion,
    int? DestinationRegion,
    long RequestedAmount,
    ulong AmountMoved,
    ulong Fee,
    ulong? SourceBalance,
    ulong? DestinationBalance)
{
    internal bool Applied => Result == DaggerfallBankTransactionResult.Applied;
}
