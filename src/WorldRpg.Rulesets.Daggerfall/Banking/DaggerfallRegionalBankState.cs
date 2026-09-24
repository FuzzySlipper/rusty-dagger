using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;

namespace WorldRpg.Rulesets.Daggerfall.Banking;

/// <summary>
/// Regional bank balances over the existing currency and inventory owners. The currency
/// service's account balance is the aggregate settlement amount; this owner's 62 balances
/// partition it and are checked against that aggregate before every transaction and restore.
/// </summary>
internal sealed class DaggerfallRegionalBankState
{
    private const string LetterDefinition = "template-275";
    private readonly DaggerfallCurrencyService _currency;
    private readonly MechanicsInventoryCoordinator _inventory;
    private readonly DaggerfallItemInstances _instances;
    private readonly ulong[] _balances = new ulong[DaggerfallRegionalBankPolicy.RegionCount];
    private ulong _revision;

    /// <summary>Ephemeral projection revision; account meaning is persisted separately.</summary>
    internal ulong Revision => _revision;

    internal DaggerfallRegionalBankState(DaggerfallCurrencyService currency, MechanicsInventoryCoordinator inventory,
        DaggerfallItemInstances instances, DaggerfallRegionalBankSave? restored = null)
    {
        _currency = currency ?? throw new ArgumentNullException(nameof(currency));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        if (restored is not null)
        {
            restored.Validate();
            foreach (DaggerfallBankAccountSave account in restored.Accounts)
                _balances[account.Region] = account.Gold;
        }

        if (!LedgerMatches())
            throw new ArgumentException("Restored regional bank balances do not equal the currency settlement account.", nameof(restored));
    }

    internal ulong BalanceForRegion(int region)
    {
        if (!DaggerfallRegionalBankPolicy.IsValidRegion(region))
            throw new ArgumentOutOfRangeException(nameof(region), $"Region must be in [0, {DaggerfallRegionalBankPolicy.RegionCount - 1}].");
        return _balances[region];
    }

    internal IReadOnlyList<DaggerfallBankAccountSave> ReadBalances() => Array.AsReadOnly(
        _balances.Select((balance, region) => new DaggerfallBankAccountSave(region, balance)).ToArray());

    internal DaggerfallRegionalBankSave Capture() => new(
        _balances.Select((balance, region) => new DaggerfallBankAccountSave(region, balance)).ToArray());

    /// <summary>Issues a bank-owned credit into one regional account without inventing a second balance.</summary>
    internal bool TryCreditAccount(int region, ulong amount)
    {
        if (!DaggerfallRegionalBankPolicy.IsValidRegion(region) || amount == 0
            || amount > (ulong)DaggerfallRegionalBankPolicy.MaximumTransactionAmount
            || !LedgerMatches() || !CanIncrease(_balances[region], amount)
            || !_currency.TryCreditAccount(amount)) return false;
        _balances[region] += amount;
        Touch();
        return true;
    }

    /// <summary>Repays or spends from a regional account through the same settlement ledger.</summary>
    internal bool TryDebitAccount(int region, ulong amount)
    {
        if (!CanDebitAccount(region, amount)
            || !_currency.TryDebitAccount(amount)) return false;
        _balances[region] -= amount;
        Touch();
        return true;
    }

    internal bool CanDebitAccount(int region, ulong amount) =>
        DaggerfallRegionalBankPolicy.IsValidRegion(region) && amount > 0
        && amount <= (ulong)DaggerfallRegionalBankPolicy.MaximumTransactionAmount
        && LedgerMatches() && _balances[region] >= amount;

    internal DaggerfallBankTransactionOutcome DepositGold(int region, long amount)
        => DepositGold(region, amount, null, null);

    /// <summary>
    /// Deposits carried gold first, then pulls any shortfall from the accessible wagon through
    /// its existing owner before the currency service consumes the player's gold.
    /// </summary>
    internal DaggerfallBankTransactionOutcome DepositGold(int region, long amount, DaggerfallWagonStorage? wagon,
        DaggerfallTransportAccessContext? accessContext)
    {
        if (!DaggerfallRegionalBankPolicy.IsValidRegion(region))
            return Outcome(DaggerfallBankTransactionKind.DepositGold, DaggerfallBankTransactionResult.InvalidRegion, null, region, amount);
        if (!DaggerfallRegionalBankPolicy.IsValidAmount(amount))
            return Outcome(DaggerfallBankTransactionKind.DepositGold, AmountFailureResult(amount), null, region, amount);
        if (!LedgerMatches())
            return Outcome(DaggerfallBankTransactionKind.DepositGold, DaggerfallBankTransactionResult.LedgerMismatch, null, region, amount);

        ulong value = (ulong)amount;
        if (!CanIncrease(_balances[region], value))
            return Outcome(DaggerfallBankTransactionKind.DepositGold, DaggerfallBankTransactionResult.AccountBalanceLimit, null, region, amount);

        ulong carriedGold = _currency.Read().Gold;
        if (carriedGold < value)
        {
            if (wagon is null && accessContext is null)
                return Outcome(DaggerfallBankTransactionKind.DepositGold, DaggerfallBankTransactionResult.CurrencyMovementRejected, null, region, amount);
            DaggerfallBankTransactionResult wagonResult = TransferWagonGold(wagon, accessContext, value - carriedGold, out ulong moved);
            if (wagonResult != DaggerfallBankTransactionResult.Applied)
                return Outcome(DaggerfallBankTransactionKind.DepositGold, wagonResult, null, region, amount, moved);
        }
        if (!_currency.DepositGold(value))
            return Outcome(DaggerfallBankTransactionKind.DepositGold, DaggerfallBankTransactionResult.CurrencyMovementRejected, null, region, amount);

        _balances[region] += value;
        Touch();
        return Outcome(DaggerfallBankTransactionKind.DepositGold, DaggerfallBankTransactionResult.Applied, null, region, amount, value);
    }

    internal DaggerfallBankTransactionOutcome WithdrawGold(int region, long amount)
    {
        if (!DaggerfallRegionalBankPolicy.IsValidRegion(region))
            return Outcome(DaggerfallBankTransactionKind.WithdrawGold, DaggerfallBankTransactionResult.InvalidRegion, region, null, amount);
        if (!DaggerfallRegionalBankPolicy.IsValidAmount(amount))
            return Outcome(DaggerfallBankTransactionKind.WithdrawGold, AmountFailureResult(amount), region, null, amount);
        if (!LedgerMatches())
            return Outcome(DaggerfallBankTransactionKind.WithdrawGold, DaggerfallBankTransactionResult.LedgerMismatch, region, null, amount);

        ulong value = (ulong)amount;
        if (_balances[region] < value)
            return Outcome(DaggerfallBankTransactionKind.WithdrawGold, DaggerfallBankTransactionResult.InsufficientAccountBalance, region, null, amount);
        if (!_currency.WithdrawGold(value))
            return Outcome(DaggerfallBankTransactionKind.WithdrawGold, DaggerfallBankTransactionResult.CurrencyMovementRejected, region, null, amount);

        _balances[region] -= value;
        Touch();
        return Outcome(DaggerfallBankTransactionKind.WithdrawGold, DaggerfallBankTransactionResult.Applied, region, null, amount, value);
    }

    /// <summary>Deposits every player-owned letter currently held in the Engine-backed player inventory.</summary>
    internal DaggerfallBankTransactionOutcome DepositLetters(int region)
    {
        if (!DaggerfallRegionalBankPolicy.IsValidRegion(region))
            return Outcome(DaggerfallBankTransactionKind.DepositLettersOfCredit, DaggerfallBankTransactionResult.InvalidRegion, null, region, 0);
        if (!LedgerMatches())
            return Outcome(DaggerfallBankTransactionKind.DepositLettersOfCredit, DaggerfallBankTransactionResult.LedgerMismatch, null, region, 0);

        DaggerfallBankTransactionResult inspection = InspectLetters(out ulong value);
        if (inspection != DaggerfallBankTransactionResult.Applied)
            return Outcome(DaggerfallBankTransactionKind.DepositLettersOfCredit, inspection, null, region, 0);
        if (!CanIncrease(_balances[region], value))
            return Outcome(DaggerfallBankTransactionKind.DepositLettersOfCredit, DaggerfallBankTransactionResult.AccountBalanceLimit, null, region, (long)value);
        if (!_currency.DepositLetters())
            return Outcome(DaggerfallBankTransactionKind.DepositLettersOfCredit, DaggerfallBankTransactionResult.CurrencyMovementRejected, null, region, (long)value);

        _balances[region] += value;
        Touch();
        return Outcome(DaggerfallBankTransactionKind.DepositLettersOfCredit, DaggerfallBankTransactionResult.Applied, null, region, (long)value, value);
    }

    internal DaggerfallBankTransactionOutcome WithdrawLetter(int region, long amount)
    {
        if (!DaggerfallRegionalBankPolicy.IsValidRegion(region))
            return Outcome(DaggerfallBankTransactionKind.WithdrawLetterOfCredit, DaggerfallBankTransactionResult.InvalidRegion, region, null, amount);
        if (amount is <= 0 or > DaggerfallRegionalBankPolicy.MaximumTransactionAmount)
            return Outcome(DaggerfallBankTransactionKind.WithdrawLetterOfCredit, AmountFailureResult(amount), region, null, amount);
        if (amount < DaggerfallRegionalBankPolicy.MinimumLetterOfCredit)
            return Outcome(DaggerfallBankTransactionKind.WithdrawLetterOfCredit, DaggerfallBankTransactionResult.LetterTooSmall, region, null, amount);
        if (!DaggerfallRegionalBankPolicy.TryLetterDebit(amount, out long debit, out long fee))
            return Outcome(DaggerfallBankTransactionKind.WithdrawLetterOfCredit, DaggerfallBankTransactionResult.SourceLimitExceeded, region, null, amount);
        if (!LedgerMatches())
            return Outcome(DaggerfallBankTransactionKind.WithdrawLetterOfCredit, DaggerfallBankTransactionResult.LedgerMismatch, region, null, amount);
        if (_balances[region] < (ulong)debit)
            return Outcome(DaggerfallBankTransactionKind.WithdrawLetterOfCredit, DaggerfallBankTransactionResult.InsufficientAccountBalance, region, null, amount);
        if (!_currency.WithdrawLetter((ulong)amount))
            return Outcome(DaggerfallBankTransactionKind.WithdrawLetterOfCredit, DaggerfallBankTransactionResult.CurrencyMovementRejected, region, null, amount);

        _balances[region] -= (ulong)debit;
        Touch();
        return Outcome(DaggerfallBankTransactionKind.WithdrawLetterOfCredit, DaggerfallBankTransactionResult.Applied,
            region, null, amount, (ulong)amount, (ulong)fee);
    }

    internal DaggerfallBankTransactionOutcome Transfer(int sourceRegion, int destinationRegion, long amount)
    {
        if (!DaggerfallRegionalBankPolicy.IsValidRegion(sourceRegion)
            || !DaggerfallRegionalBankPolicy.IsValidRegion(destinationRegion))
            return Outcome(DaggerfallBankTransactionKind.Transfer, DaggerfallBankTransactionResult.InvalidRegion,
                sourceRegion, destinationRegion, amount);
        if (sourceRegion == destinationRegion)
            return Outcome(DaggerfallBankTransactionKind.Transfer, DaggerfallBankTransactionResult.SameRegion,
                sourceRegion, destinationRegion, amount);
        if (!DaggerfallRegionalBankPolicy.IsValidAmount(amount))
            return Outcome(DaggerfallBankTransactionKind.Transfer, AmountFailureResult(amount), sourceRegion, destinationRegion, amount);
        if (!LedgerMatches())
            return Outcome(DaggerfallBankTransactionKind.Transfer, DaggerfallBankTransactionResult.LedgerMismatch,
                sourceRegion, destinationRegion, amount);

        ulong value = (ulong)amount;
        if (_balances[sourceRegion] < value)
            return Outcome(DaggerfallBankTransactionKind.Transfer, DaggerfallBankTransactionResult.InsufficientAccountBalance,
                sourceRegion, destinationRegion, amount);
        if (!CanIncrease(_balances[destinationRegion], value))
            return Outcome(DaggerfallBankTransactionKind.Transfer, DaggerfallBankTransactionResult.AccountBalanceLimit,
                sourceRegion, destinationRegion, amount);

        _balances[sourceRegion] -= value;
        _balances[destinationRegion] += value;
        Touch();
        return Outcome(DaggerfallBankTransactionKind.Transfer, DaggerfallBankTransactionResult.Applied,
            sourceRegion, destinationRegion, amount, value);
    }

    private DaggerfallBankTransactionResult InspectLetters(out ulong total)
    {
        total = 0;
        Rusty.Engine.Mechanics.UniqueInventoryItem[] letters = _inventory.Read().UniqueItems
            .Where(item => string.Equals(item.Definition.Value, LetterDefinition, StringComparison.Ordinal)).ToArray();
        if (letters.Length == 0) return DaggerfallBankTransactionResult.NoLettersOfCredit;

        foreach (Rusty.Engine.Mechanics.UniqueInventoryItem item in letters)
        {
            DaggerfallItemInstanceMetadata metadata;
            try
            {
                ulong identity = _inventory.GetDurableItemId(item.Entity).Value;
                metadata = _instances.RequireUnique(identity);
            }
            catch (InvalidOperationException)
            {
                return DaggerfallBankTransactionResult.InvalidLetterMetadata;
            }

            if (metadata.Owner != DaggerfallItemOwner.Player)
                return DaggerfallBankTransactionResult.InvalidLetterOwnership;
            if (!string.Equals(metadata.ItemId, LetterDefinition, StringComparison.Ordinal)
                || metadata.CreditValue is not ulong credit || credit == 0)
                return DaggerfallBankTransactionResult.InvalidLetterMetadata;
            if (credit > DaggerfallRegionalBankPolicy.MaximumAccountBalance
                || total > DaggerfallRegionalBankPolicy.MaximumAccountBalance - credit)
                return DaggerfallBankTransactionResult.SourceLimitExceeded;
            total += credit;
        }

        return total == 0 ? DaggerfallBankTransactionResult.InvalidLetterMetadata : DaggerfallBankTransactionResult.Applied;
    }

    private DaggerfallBankTransactionResult TransferWagonGold(DaggerfallWagonStorage? wagon,
        DaggerfallTransportAccessContext? accessContext, ulong required, out ulong moved)
    {
        moved = 0;
        if (wagon is null || accessContext is not DaggerfallTransportAccessContext context || wagon.Current is not DaggerfallWagon owner)
            return DaggerfallBankTransactionResult.WagonUnavailable;

        InventoryView playerView = _inventory.Read();
        if (!wagon.CanAccess(playerView, context)) return DaggerfallBankTransactionResult.WagonUnavailable;
        InventoryView? wagonView = wagon.Read();
        if (wagonView is null) return DaggerfallBankTransactionResult.WagonUnavailable;

        DaggerfallItemOwner itemOwner = DaggerfallItemOwner.Wagon(owner.Id);
        List<InventoryContainerSelection> selected = [];
        HashSet<string> reservedDestinationIds = [];
        ulong remaining = required;
        foreach (InventoryStack stack in wagonView.Stacks.OrderBy(value => value.Id.Value, StringComparer.Ordinal))
        {
            if (remaining == 0) break;
            if (!IsGoldDefinition(stack.Definition.Value)) continue;
            DaggerfallItemInstanceMetadata metadata;
            try { metadata = _instances.RequireStack(itemOwner, stack.Id); }
            catch (InvalidOperationException) { return DaggerfallBankTransactionResult.InvalidWagonGoldOwnership; }
            if (metadata.Owner != itemOwner)
                return DaggerfallBankTransactionResult.InvalidWagonGoldOwnership;
            if (!string.Equals(metadata.ItemId, stack.Definition.Value, StringComparison.Ordinal))
                return DaggerfallBankTransactionResult.InvalidWagonGoldOwnership;

            ulong transfer = Math.Min(remaining, stack.Quantity);
            InventoryStackId destination = NewPlayerStackId(owner.Id, playerView.StoreRevision, reservedDestinationIds);
            selected.Add(new InventoryContainerSelection(new InventoryItemId(stack.Definition.Value), transfer,
                Stack: stack.Id, DestinationStack: destination));
            remaining -= transfer;
        }
        if (remaining != 0) return DaggerfallBankTransactionResult.WagonUnavailable;

        foreach (InventoryContainerSelection selection in selected)
        {
            try
            {
                wagon.TransferFromWagon(selection, context, _inventory.Read().StoreRevision);
                moved = checked(moved + selection.Quantity);
            }
            catch (Exception error) when (error is MechanicsException or InvalidOperationException or ArgumentException)
            {
                return moved == 0
                    ? DaggerfallBankTransactionResult.WagonUnavailable
                    : DaggerfallBankTransactionResult.WagonTransferPartiallyApplied;
            }
        }

        return DaggerfallBankTransactionResult.Applied;
    }

    private InventoryStackId NewPlayerStackId(long wagonId, ulong inventoryRevision, ISet<string> reserved)
    {
        HashSet<string> occupied = _inventory.Read().Stacks.Select(stack => stack.Id.Value).ToHashSet(StringComparer.Ordinal);
        for (int suffix = 0; ; suffix++)
        {
            InventoryStackId candidate = InventoryStackId.Parse($"daggerfall.bank.wagon.{wagonId}.{inventoryRevision}.{suffix}");
            if (!occupied.Contains(candidate.Value) && !reserved.Contains(candidate.Value)
                && !_instances.ContainsStack(DaggerfallItemOwner.Player, candidate))
            {
                reserved.Add(candidate.Value);
                return candidate;
            }
        }
    }

    private static bool IsGoldDefinition(string itemId) => itemId is "gold-piece" or "template-276";

    private bool LedgerMatches()
    {
        ulong total = 0;
        foreach (ulong balance in _balances) total = checked(total + balance);
        return total == _currency.Capture().AccountGold;
    }

    private void Touch() => _revision = unchecked(_revision + 1);

    private DaggerfallBankTransactionOutcome Outcome(DaggerfallBankTransactionKind kind, DaggerfallBankTransactionResult result,
        int? sourceRegion, int? destinationRegion, long requestedAmount, ulong amountMoved = 0, ulong fee = 0)
    {
        ulong? sourceBalance = sourceRegion is int source && DaggerfallRegionalBankPolicy.IsValidRegion(source) ? _balances[source] : null;
        ulong? destinationBalance = destinationRegion is int destination && DaggerfallRegionalBankPolicy.IsValidRegion(destination)
            ? _balances[destination] : null;
        return new(kind, result, sourceRegion, destinationRegion, requestedAmount, amountMoved, fee, sourceBalance, destinationBalance);
    }

    private static DaggerfallBankTransactionResult AmountFailureResult(long amount) => amount > DaggerfallRegionalBankPolicy.MaximumTransactionAmount
        ? DaggerfallBankTransactionResult.SourceLimitExceeded
        : DaggerfallBankTransactionResult.InvalidAmount;

    private static bool CanIncrease(ulong balance, ulong amount) => balance <= DaggerfallRegionalBankPolicy.MaximumAccountBalance
        && amount <= DaggerfallRegionalBankPolicy.MaximumAccountBalance - balance;
}
