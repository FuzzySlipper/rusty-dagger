using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallLoanTests
{
    [Fact]
    public void Max_loan_uses_level_only_and_repayment_truncates_the_ten_percent_premium()
    {
        Assert.Equal(50_000L, DaggerfallLoanPolicy.CalculateMaxBankLoan(1));
        Assert.Equal(1_100_000L, DaggerfallLoanPolicy.CalculateMaxBankLoan(22));
        Assert.Equal((long)int.MaxValue, DaggerfallLoanPolicy.CalculateMaxBankLoan(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallLoanPolicy.CalculateMaxBankLoan(0));

        Assert.Equal(110L, DaggerfallLoanPolicy.CalculateBankLoanRepayment(100, 0));
        Assert.Equal(1_098L, DaggerfallLoanPolicy.CalculateBankLoanRepayment(999, 61));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallLoanPolicy.CalculateBankLoanRepayment(100, 62));
    }

    [Fact]
    public void Issue_records_one_year_due_date_and_refuses_repeat_or_defaulted_borrowing()
    {
        DaggerfallLoanIssueDecision issued = DaggerfallLoanPolicy.EvaluateIssue(
            region: 3, playerLevel: 2, principal: 1_000, DaggerfallCalendar.Start);

        Assert.True(issued.Approved);
        Assert.Equal(1_000L, issued.RequestedPrincipal);
        Assert.Equal(100_000L, issued.MaximumPrincipal);
        Assert.Equal(1_100L, issued.Repayment);
        Assert.Equal(360L * DaggerfallLoanPolicy.MinutesPerDay, issued.DueMinute);
        DaggerfallLoanRecord loan = Assert.IsType<DaggerfallLoanRecord>(issued.Loan);
        Assert.Equal((3, 1_000UL, 1_100UL, false), (loan.Region, loan.Principal, loan.Remaining, loan.Defaulted));
        loan.Validate();

        DaggerfallLoanIssueDecision repeated = DaggerfallLoanPolicy.EvaluateIssue(
            3, 2, 100, DaggerfallCalendar.Start, loan);
        Assert.Equal(DaggerfallLoanIssueResult.AlreadyHaveLoan, repeated.Result);

        DaggerfallLoanIssueDecision defaulted = DaggerfallLoanPolicy.EvaluateIssue(
            3, 2, 100, DaggerfallCalendar.Start, loan with { Defaulted = true });
        Assert.Equal(DaggerfallLoanIssueResult.AlreadyDefaulted, defaulted.Result);

        Assert.Equal(DaggerfallLoanIssueResult.AmountTooLow,
            DaggerfallLoanPolicy.EvaluateIssue(3, 2, 99, DaggerfallCalendar.Start).Result);
        Assert.Equal(DaggerfallLoanIssueResult.AmountTooHigh,
            DaggerfallLoanPolicy.EvaluateIssue(3, 2, 100_001, DaggerfallCalendar.Start).Result);
    }

    [Fact]
    public void Repayment_supports_partial_and_overpayment_boundaries_without_partial_fund_collection()
    {
        DaggerfallLoanRecord loan = DaggerfallLoanPolicy.EvaluateIssue(
            0, 1, 1_000, DaggerfallCalendar.Start).Loan!;

        DaggerfallLoanRepaymentDecision insufficient = DaggerfallLoanPolicy.EvaluateRepayment(loan, 500, 499);
        Assert.Equal(DaggerfallLoanRepaymentResult.InsufficientFunds, insufficient.Result);
        Assert.Equal(loan.Remaining, insufficient.RemainingAfter);
        Assert.Same(loan, DaggerfallLoanPolicy.ApplyRepayment(loan, insufficient));

        DaggerfallLoanRepaymentDecision partial = DaggerfallLoanPolicy.EvaluateRepayment(loan, 500, 500);
        Assert.Equal(DaggerfallLoanRepaymentResult.Applied, partial.Result);
        Assert.Equal(500UL, partial.AppliedAmount);
        DaggerfallLoanRecord reduced = Assert.IsType<DaggerfallLoanRecord>(DaggerfallLoanPolicy.ApplyRepayment(loan, partial));
        Assert.Equal(600UL, reduced.Remaining);

        DaggerfallLoanRepaymentDecision overpaid = DaggerfallLoanPolicy.EvaluateRepayment(reduced, 700, 700);
        Assert.Equal(DaggerfallLoanRepaymentResult.OverpaidLoan, overpaid.Result);
        Assert.Equal(600UL, overpaid.AppliedAmount);
        Assert.Null(DaggerfallLoanPolicy.ApplyRepayment(reduced, overpaid));
        Assert.Equal(DaggerfallLoanRepaymentResult.InvalidAmount,
            DaggerfallLoanPolicy.EvaluateRepayment(reduced, 0, 0).Result);
    }

    [Fact]
    public void Paying_a_defaulted_loan_keeps_a_zero_balance_default_marker_and_blocks_new_borrowing()
    {
        DaggerfallLoanRecord loan = DaggerfallLoanPolicy.EvaluateIssue(
            4, 1, 100, DaggerfallCalendar.Start).Loan!;
        DaggerfallLoanCalendarPair times = DaggerfallLoanCalendarPair.For(loan);
        DaggerfallLoanRecord defaulted = Assert.IsType<DaggerfallLoanRecord>(
            DaggerfallLoanPolicy.EvaluateDue(loan, times.AfterDue).Loan);
        Assert.True(defaulted.Defaulted);

        DaggerfallLoanRepaymentDecision repayment = DaggerfallLoanPolicy.EvaluateRepayment(
            defaulted, defaulted.Remaining, defaulted.Remaining);
        Assert.Equal(DaggerfallLoanRepaymentResult.Applied, repayment.Result);
        DaggerfallLoanRecord marker = Assert.IsType<DaggerfallLoanRecord>(
            DaggerfallLoanPolicy.ApplyRepayment(defaulted, repayment));
        Assert.Equal((0UL, 0L, true), (marker.Remaining, marker.DueMinute, marker.Defaulted));
        marker.Validate();

        Assert.Equal(DaggerfallLoanRepaymentResult.NoLoan,
            DaggerfallLoanPolicy.EvaluateRepayment(marker, 1, 1).Result);
        Assert.Equal(DaggerfallLoanDueResult.AlreadyDefaulted,
            DaggerfallLoanPolicy.EvaluateDue(marker, times.AfterDue).Result);
        Assert.Equal(DaggerfallLoanIssueResult.AlreadyDefaulted,
            DaggerfallLoanPolicy.EvaluateIssue(4, 1, 100, DaggerfallCalendar.Start, marker).Result);
    }

    [Fact]
    public void Default_is_due_at_the_deadline_but_applies_once_after_it_and_carries_donor_reputation_deltas()
    {
        DaggerfallLoanRecord loan = DaggerfallLoanPolicy.EvaluateIssue(
            7, 1, 100, DaggerfallCalendar.Start).Loan!;
        DaggerfallCalendar due = DaggerfallCalendar.FromAbsoluteSeconds(loan.DueMinute * 60);
        DaggerfallCalendar afterDue = DaggerfallCalendar.FromAbsoluteSeconds((loan.DueMinute + 1) * 60);

        Assert.Equal(DaggerfallLoanDueResult.Due, DaggerfallLoanPolicy.EvaluateDue(loan, due).Result);

        DaggerfallLoanDueDecision defaulted = DaggerfallLoanPolicy.EvaluateDue(loan, afterDue);
        Assert.True(defaulted.DefaultApplied);
        Assert.Equal(loan.Remaining, defaulted.RemainingDebt);
        Assert.Equal((-10, -5), (defaulted.RegionalReputationDelta, defaulted.PeopleFactionReputationDelta));
        DaggerfallLoanRecord marked = Assert.IsType<DaggerfallLoanRecord>(defaulted.Loan);
        Assert.True(marked.Defaulted);

        DaggerfallLoanDueDecision repeated = DaggerfallLoanPolicy.EvaluateDue(marked, afterDue);
        Assert.Equal(DaggerfallLoanDueResult.AlreadyDefaulted, repeated.Result);
        Assert.Equal((0, 0), (repeated.RegionalReputationDelta, repeated.PeopleFactionReputationDelta));
    }

    [Fact]
    public void Settlement_adapter_routes_each_movement_to_the_existing_owner_and_keeps_no_balance()
    {
        List<string> calls = [];
        DaggerfallLoanSettlementAdapter adapter = new(
            (region, amount) => { calls.Add($"credit:{region}:{amount}"); return true; },
            (region, amount) => { calls.Add($"debit:{region}:{amount}"); return true; },
            amount => { calls.Add($"inventory:{amount}"); return true; });

        Assert.True(adapter.Issue(2, 500).Applied);
        Assert.True(adapter.RepayFromAccount(2, 200).Applied);
        Assert.True(adapter.RepayFromInventory(2, 300).Applied);
        Assert.Equal(["credit:2:500", "debit:2:200", "inventory:300"], calls);

        Assert.Equal(DaggerfallLoanSettlementResult.InvalidRegion, adapter.Issue(-1, 1).Result);
        Assert.Equal(DaggerfallLoanSettlementResult.InvalidAmount, adapter.RepayFromAccount(2, 0).Result);
        Assert.Equal(3, calls.Count);

        DaggerfallLoanSettlementAdapter rejected = new(
            (_, _) => false,
            (_, _) => false,
            _ => false);
        Assert.Equal(DaggerfallLoanSettlementResult.AccountMovementRejected, rejected.Issue(2, 1).Result);
        Assert.Equal(DaggerfallLoanSettlementResult.AccountMovementRejected, rejected.RepayFromAccount(2, 1).Result);
        Assert.Equal(DaggerfallLoanSettlementResult.InventoryMovementRejected, rejected.RepayFromInventory(2, 1).Result);
    }

    private readonly record struct DaggerfallLoanCalendarPair(DaggerfallCalendar AfterDue)
    {
        internal static DaggerfallLoanCalendarPair For(DaggerfallLoanRecord loan) =>
            new(DaggerfallCalendar.FromAbsoluteSeconds((loan.DueMinute + 1) * 60));
    }
}
