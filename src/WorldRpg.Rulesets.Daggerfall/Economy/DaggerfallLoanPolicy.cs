using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Banking;

/// <summary>The current durable loan state in one regional account.</summary>
/// <remarks>
/// A loan is a debt record, not another money store. The account credit and every repayment are
/// performed by <see cref="DaggerfallLoanSettlementAdapter"/> through the existing bank and currency
/// owners. A non-defaulted record is removed when its debt is paid. A defaulted record remains as a
/// zero-balance marker so the donor's persistent <c>hasDefaulted</c> flag cannot be lost when the
/// remaining debt is later paid.
/// </remarks>
internal sealed record DaggerfallLoanRecord(
    int Region,
    ulong Principal,
    ulong Remaining,
    long DueMinute,
    bool Defaulted)
{
    internal bool HasOutstandingDebt => Remaining > 0;

    internal DaggerfallLoanRecord Validate()
    {
        if (!DaggerfallRegionalBankPolicy.IsValidRegion(Region))
            throw new ArgumentOutOfRangeException(nameof(Region), Region, "A loan must name one of the classic bank regions.");
        if (Principal < DaggerfallLoanPolicy.MinimumPrincipal
            || Principal > (ulong)DaggerfallLoanPolicy.MaximumPrincipal)
            throw new ArgumentOutOfRangeException(nameof(Principal), Principal, "A loan principal is outside the source bounds.");
        ulong repayment = checked((ulong)DaggerfallLoanPolicy.CalculateBankLoanRepayment((long)Principal, Region));
        if (Remaining > repayment || (Remaining == 0 && !Defaulted))
            throw new ArgumentOutOfRangeException(nameof(Remaining), Remaining, "A saved loan must carry a balance no greater than its repayment; only a defaulted marker may be zero.");
        if (Remaining == 0)
        {
            // DaggerfallBankManager.RepayLoan clears loanDueDate when the debt reaches zero while
            // leaving hasDefaulted set. Keep that same settled-default marker shape here.
            if (DueMinute != 0)
                throw new ArgumentOutOfRangeException(nameof(DueMinute), DueMinute, "A settled default marker must clear its due minute.");
        }
        else if (DueMinute <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DueMinute), DueMinute, "An outstanding loan must carry a positive classic due minute.");
        }
        return this;
    }
}

internal enum DaggerfallLoanIssueResult
{
    Approved,
    InvalidRegion,
    InvalidLevel,
    AmountTooLow,
    AmountTooHigh,
    AlreadyHaveLoan,
    AlreadyDefaulted,
    DueDateOverflow,
    AccountLimitReached,
}

/// <summary>The complete answer to a loan request, including the record the save owner may persist.</summary>
internal readonly record struct DaggerfallLoanIssueDecision(
    DaggerfallLoanIssueResult Result,
    int Region,
    long RequestedPrincipal,
    long MaximumPrincipal,
    long Repayment,
    long DueMinute,
    DaggerfallLoanRecord? Loan)
{
    internal bool Approved => Result == DaggerfallLoanIssueResult.Approved;
}

internal enum DaggerfallLoanRepaymentResult
{
    Applied,
    InvalidAmount,
    NoLoan,
    InsufficientFunds,
    OverpaidLoan,
}

/// <summary>The policy answer before an account or inventory owner moves repayment funds.</summary>
internal readonly record struct DaggerfallLoanRepaymentDecision(
    DaggerfallLoanRepaymentResult Result,
    int Region,
    ulong RequestedAmount,
    ulong AppliedAmount,
    ulong RemainingAfter)
{
    internal bool Applied => Result is DaggerfallLoanRepaymentResult.Applied or DaggerfallLoanRepaymentResult.OverpaidLoan;
}

internal enum DaggerfallLoanDueResult
{
    NoLoan,
    NotDue,
    Due,
    Overdue,
    AlreadyDefaulted,
    DefaultApplied,
}

/// <summary>The due/default answer, including the one-time social consequences of a default.</summary>
internal readonly record struct DaggerfallLoanDueDecision(
    DaggerfallLoanDueResult Result,
    int Region,
    ulong RemainingDebt,
    int RegionalReputationDelta,
    int PeopleFactionReputationDelta,
    DaggerfallLoanRecord? Loan)
{
    internal bool DefaultApplied => Result == DaggerfallLoanDueResult.DefaultApplied;
}

/// <summary>
/// Daggerfall loan formulas and time policy. The donor's max-loan formula is level-only; regional
/// reputation is not an input to the cap. Region remains part of the loan record because each bank
/// account carries an independent debt and due date.
/// </summary>
internal static class DaggerfallLoanPolicy
{
    internal const int MinimumPrincipal = 100;
    internal const int MaximumPrincipalPerLevel = 50_000;
    internal const int RepaymentPercent = 10;
    internal const int LoanTermDays = DaggerfallCalendar.DaysPerYear;
    internal const int DefaultRegionalReputationDelta = -10;
    internal const int DefaultPeopleFactionReputationDelta = -5;

    internal const long MinutesPerDay = DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour;
    internal const long MaximumPrincipal = DaggerfallRegionalBankPolicy.MaximumTransactionAmount;

    /// <summary>FORM-09.CalculateMaxBankLoan: player level multiplied by the donor's 50,000 cap.</summary>
    internal static long CalculateMaxBankLoan(int playerLevel)
    {
        if (playerLevel < 1)
            throw new ArgumentOutOfRangeException(nameof(playerLevel), playerLevel, "A Daggerfall player level begins at one.");

        // The source returns an int. The current account owner has the same int.MaxValue transaction
        // bound, but retaining the arithmetic as long keeps a crafted high level from overflowing
        // before that existing owner can reject it.
        return Math.Min(checked((long)playerLevel * MaximumPrincipalPerLevel), MaximumPrincipal);
    }

    /// <summary>
    /// FORM-09.CalculateBankLoanRepayment: principal plus a truncated ten percent premium. The
    /// retained region parameter matches FormulaHelper's overload even though the donor does not use it.
    /// </summary>
    internal static long CalculateBankLoanRepayment(long principal, int region)
    {
        RequireRegion(region);
        if (principal <= 0 || principal > MaximumPrincipal)
            throw new ArgumentOutOfRangeException(nameof(principal), principal, "A loan principal is outside the source bounds.");

        return checked(principal + (principal * RepaymentPercent / 100));
    }

    /// <summary>Creates the issue answer without mutating bank or currency state.</summary>
    internal static DaggerfallLoanIssueDecision EvaluateIssue(
        int region,
        int playerLevel,
        long principal,
        DaggerfallCalendar current,
        DaggerfallLoanRecord? existing = null)
    {
        if (!DaggerfallRegionalBankPolicy.IsValidRegion(region))
            return Failure(DaggerfallLoanIssueResult.InvalidRegion, region, principal);
        if (playerLevel < 1)
            return Failure(DaggerfallLoanIssueResult.InvalidLevel, region, principal);

        if (existing is not null)
        {
            existing.Validate();
            if (existing.Region != region)
                throw new ArgumentException("An existing loan belongs to a different region.", nameof(existing));
            if (existing.Defaulted)
                return Failure(DaggerfallLoanIssueResult.AlreadyDefaulted, region, principal);
            if (existing.HasOutstandingDebt)
                return Failure(DaggerfallLoanIssueResult.AlreadyHaveLoan, region, principal);
        }

        long maximum = CalculateMaxBankLoan(playerLevel);
        if (principal < MinimumPrincipal)
            return new(DaggerfallLoanIssueResult.AmountTooLow, region, principal, maximum, 0, 0, null);
        if (principal > maximum)
            return new(DaggerfallLoanIssueResult.AmountTooHigh, region, principal, maximum, 0, 0, null);

        long repayment = CalculateBankLoanRepayment(principal, region);
        long dueMinute;
        try
        {
            dueMinute = checked(ClassicMinute(current) + checked((long)LoanTermDays * MinutesPerDay));
        }
        catch (OverflowException)
        {
            return new(DaggerfallLoanIssueResult.DueDateOverflow, region, principal, maximum, repayment, 0, null);
        }

        // Due minute zero is the bank ledger's empty sentinel. A running Daggerfall session starts at
        // minute zero, so every admitted issue has a positive due minute after one full classic year.
        if (dueMinute <= 0)
            return new(DaggerfallLoanIssueResult.DueDateOverflow, region, principal, maximum, repayment, dueMinute, null);

        DaggerfallLoanRecord loan = new(region, (ulong)principal, (ulong)repayment, dueMinute, false);
        return new(DaggerfallLoanIssueResult.Approved, region, principal, maximum, repayment, dueMinute, loan);
    }

    /// <summary>Returns a repayment answer before any owner consumes funds.</summary>
    internal static DaggerfallLoanRepaymentDecision EvaluateRepayment(
        DaggerfallLoanRecord? loan,
        ulong requestedAmount,
        ulong availableFunds)
    {
        if (loan is null)
            return new(DaggerfallLoanRepaymentResult.NoLoan, 0, requestedAmount, 0, 0);
        loan.Validate();
        if (!loan.HasOutstandingDebt)
            return new(DaggerfallLoanRepaymentResult.NoLoan, loan.Region, requestedAmount, 0, 0);
        if (requestedAmount == 0)
            return new(DaggerfallLoanRepaymentResult.InvalidAmount, loan.Region, 0, 0, loan.Remaining);
        if (requestedAmount > availableFunds)
            return new(DaggerfallLoanRepaymentResult.InsufficientFunds, loan.Region, requestedAmount, 0, loan.Remaining);

        ulong applied = Math.Min(requestedAmount, loan.Remaining);
        DaggerfallLoanRepaymentResult result = requestedAmount > loan.Remaining
            ? DaggerfallLoanRepaymentResult.OverpaidLoan
            : DaggerfallLoanRepaymentResult.Applied;
        return new(result, loan.Region, requestedAmount, applied, loan.Remaining - applied);
    }

    /// <summary>Applies an already-settled repayment to the one durable loan record.</summary>
    internal static DaggerfallLoanRecord? ApplyRepayment(DaggerfallLoanRecord loan, DaggerfallLoanRepaymentDecision decision)
    {
        ArgumentNullException.ThrowIfNull(loan);
        loan.Validate();
        if (!decision.Applied || decision.Region != loan.Region || decision.AppliedAmount == 0)
            return loan;
        if (decision.AppliedAmount > loan.Remaining)
            throw new ArgumentException("A repayment decision exceeds the outstanding debt.", nameof(decision));
        ulong expectedRemaining = loan.Remaining - decision.AppliedAmount;
        if (decision.RemainingAfter != expectedRemaining)
            throw new ArgumentException("A repayment decision does not describe the remaining debt.", nameof(decision));
        if (decision.RemainingAfter != 0)
            return loan with { Remaining = decision.RemainingAfter };

        // The donor clears the due date when the debt is paid but does not clear its separate
        // hasDefaulted flag. Keep that durable marker so a later borrow remains blocked.
        return loan.Defaulted
            ? loan with { Remaining = 0, DueMinute = 0 }
            : null;
    }

    /// <summary>Reads the shared calendar in the same whole-minute units used by session deadlines.</summary>
    internal static long ClassicMinute(DaggerfallCalendar calendar) => checked(
        (calendar.DayNumber * MinutesPerDay) + (calendar.Hour * DaggerfallCalendar.MinutesPerHour) + calendar.Minute);

    internal static DaggerfallLoanDueDecision EvaluateDue(DaggerfallLoanRecord? loan, DaggerfallCalendar current)
    {
        if (loan is null)
            return new(DaggerfallLoanDueResult.NoLoan, 0, 0, 0, 0, null);
        loan.Validate();
        if (loan.Defaulted)
            return new(DaggerfallLoanDueResult.AlreadyDefaulted, loan.Region, loan.Remaining, 0, 0, loan);

        long minute = ClassicMinute(current);
        if (minute < loan.DueMinute)
            return new(DaggerfallLoanDueResult.NotDue, loan.Region, loan.Remaining, 0, 0, loan);
        if (minute == loan.DueMinute)
            return new(DaggerfallLoanDueResult.Due, loan.Region, loan.Remaining, 0, 0, loan);

        return new(DaggerfallLoanDueResult.DefaultApplied, loan.Region, loan.Remaining,
            DefaultRegionalReputationDelta, DefaultPeopleFactionReputationDelta, loan with { Defaulted = true });
    }

    private static DaggerfallLoanIssueDecision Failure(DaggerfallLoanIssueResult result, int region, long principal) =>
        new(result, region, principal, 0, 0, 0, null);

    private static void RequireRegion(int region)
    {
        if (!DaggerfallRegionalBankPolicy.IsValidRegion(region))
            throw new ArgumentOutOfRangeException(nameof(region), region, "A loan formula must name one of the classic bank regions.");
    }
}
