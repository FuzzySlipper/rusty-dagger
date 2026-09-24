using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Banking;

/// <summary>The current regional debts and settled default markers.</summary>
internal sealed record DaggerfallLoansSave(DaggerfallLoanRecord[] Records)
{
    internal static DaggerfallLoansSave Empty { get; } = new([]);

    internal DaggerfallLoansSave Validate()
    {
        ArgumentNullException.ThrowIfNull(Records);
        HashSet<int> regions = [];
        foreach (DaggerfallLoanRecord record in Records)
        {
            ArgumentNullException.ThrowIfNull(record);
            record.Validate();
            if (!regions.Add(record.Region))
                throw new ArgumentException($"Loan save repeats region {record.Region}.", nameof(Records));
        }
        return this;
    }
}

/// <summary>One saved loan owner; all money stays in the bank and inventory ledgers.</summary>
internal sealed class DaggerfallLoanState
{
    private readonly Dictionary<int, DaggerfallLoanRecord> _records = [];

    internal DaggerfallLoanState(DaggerfallLoansSave? saved = null)
    {
        if (saved is null) return;
        saved.Validate();
        foreach (DaggerfallLoanRecord record in saved.Records) _records.Add(record.Region, record);
    }

    internal DaggerfallLoansSave Capture() => new DaggerfallLoansSave(
        [.. _records.Values.OrderBy(record => record.Region)]).Validate();

    internal DaggerfallLoanRecord? Read(int region)
    {
        if (!DaggerfallRegionalBankPolicy.IsValidRegion(region))
            throw new ArgumentOutOfRangeException(nameof(region));
        return _records.GetValueOrDefault(region);
    }

    internal DaggerfallLoanIssueDecision Issue(int region, int level, long principal,
        DaggerfallCalendar calendar, DaggerfallLoanSettlementAdapter settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        DaggerfallLoanIssueDecision decision = DaggerfallLoanPolicy.EvaluateIssue(
            region, level, principal, calendar, _records.GetValueOrDefault(region));
        if (!decision.Approved) return decision;
        if (!settlement.Issue(region, (ulong)principal).Applied)
            return decision with { Result = DaggerfallLoanIssueResult.AccountLimitReached, Loan = null };
        _records[region] = decision.Loan!;
        return decision;
    }

    internal DaggerfallLoanRepaymentDecision Repay(int region, ulong amount, bool fromAccount,
        DaggerfallRegionalBankState bank, DaggerfallCurrencyService currency,
        DaggerfallLoanSettlementAdapter settlement)
    {
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(settlement);
        DaggerfallLoanRecord? loan = Read(region);
        DaggerfallCurrencyTotals funds = currency.Read();
        ulong carried = checked(funds.Gold + funds.LettersOfCredit);
        ulong account = bank.BalanceForRegion(region);
        ulong available = fromAccount ? account : account > ulong.MaxValue - carried ? ulong.MaxValue : account + carried;
        DaggerfallLoanRepaymentDecision decision = DaggerfallLoanPolicy.EvaluateRepayment(loan, amount, available);
        if (!decision.Applied) return decision;
        if (fromAccount)
        {
            if (!settlement.RepayFromAccount(region, decision.AppliedAmount).Applied)
                throw new InvalidOperationException($"Loan repayment in region {region} was approved but its account rejected the debit.");
        }
        else
        {
            ulong carriedPayment = Math.Min(carried, decision.AppliedAmount);
            ulong accountPayment = decision.AppliedAmount - carriedPayment;
            if (accountPayment > 0 && !bank.CanDebitAccount(region, accountPayment))
                throw new InvalidOperationException($"Loan repayment in region {region} cannot debit its account ledger.");
            if (carriedPayment > 0 && !settlement.RepayFromCarried(region, carriedPayment).Applied)
                throw new InvalidOperationException($"Loan repayment in region {region} was approved but carried currency rejected the debit.");
            if (accountPayment > 0 && !settlement.RepayFromAccount(region, accountPayment).Applied)
                throw new InvalidOperationException($"Loan repayment in region {region} was approved but its account rejected the remaining debit.");
        }
        DaggerfallLoanRecord? remaining = DaggerfallLoanPolicy.ApplyRepayment(loan!, decision);
        if (remaining is null) _records.Remove(region);
        else _records[region] = remaining;
        return decision;
    }

    /// <summary>Runs donor account-first overdue settlement once after an admitted calendar advance.</summary>
    internal IReadOnlyList<DaggerfallLoanDueDecision> AdvanceDue(DaggerfallCalendar calendar,
        DaggerfallRegionalBankState bank, DaggerfallCurrencyService currency,
        DaggerfallLoanSettlementAdapter settlement, DaggerfallSocialState social,
        DaggerfallFactionsSet factions)
    {
        List<DaggerfallLoanDueDecision> outcomes = [];
        foreach (DaggerfallLoanRecord initial in _records.Values.OrderBy(record => record.Region).ToArray())
        {
            if (initial.Defaulted || DaggerfallLoanPolicy.ClassicMinute(calendar) <= initial.DueMinute) continue;
            ulong account = bank.BalanceForRegion(initial.Region);
            if (account > 0)
                _ = Repay(initial.Region, Math.Min(account, initial.Remaining), fromAccount: true,
                    bank, currency, settlement);
            DaggerfallLoanRecord? loan = Read(initial.Region);
            if (loan is null || loan.Remaining == 0) continue;
            DaggerfallLoanDueDecision due = DaggerfallLoanPolicy.EvaluateDue(loan, calendar);
            if (!due.DefaultApplied) continue;
            _records[loan.Region] = due.Loan!;
            social.ChangeRegionalReputation(loan.Region, due.RegionalReputationDelta);
            DaggerfallFactionDefinition? people = factions.Factions.Values
                .Where(faction => faction.Type == 15 && faction.Region == loan.Region)
                .OrderBy(faction => faction.Id).FirstOrDefault();
            if (people is not null)
                social.ChangeFactionReputation(people.Id, due.PeopleFactionReputationDelta,
                    DaggerfallFactionReputationChange.Propagate);
            outcomes.Add(due);
        }
        return outcomes;
    }
}
