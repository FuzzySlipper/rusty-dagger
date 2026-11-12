using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private void AdvanceLoans(DaggerfallCalendar before, DaggerfallCalendar after)
    {
        DaggerfallLoanDayResult day = State.Loans.AdvanceDue(before, after,
            State.Bank, State.Currency, DaggerfallLoanSettlementAdapter.ForBank(State.Bank, State.Currency),
            State.Social, _definitions.Factions);
        foreach (DaggerfallLoanReminder reminder in day.Reminders)
            Presentation.SetOutcome($"You have a loan of {reminder.Remaining} gold pieces due in less than "
                + $"{reminder.MonthsLeft} months in {LoanRegionName(reminder.Region)}.");
        if (day.Defaults.Count > 0)
            Presentation.SetOutcome(day.Defaults.Count == 1
                ? $"Loan defaulted in {LoanRegionName(day.Defaults[0].Region)}; legal and faction standing fell."
                : $"Loans defaulted in {day.Defaults.Count} regions; legal and faction standing fell.");
    }

    private string LoanRegionName(int region) => _definitions.BuildingNames.RegionName(region);

    private void ChangeLoan(DaggerfallPlayerUiAction action)
    {
        if (ActiveBankRegion() is not int region)
        {
            Presentation.SetOutcome("A bank service is not available here.");
            return;
        }
        if (action.Amount is not ulong amount || amount > int.MaxValue)
        {
            Presentation.SetOutcome("Enter a positive bank loan amount within the regional transaction limit.");
            return;
        }
        DaggerfallLoanSettlementAdapter settlement = DaggerfallLoanSettlementAdapter.ForBank(State.Bank, State.Currency);
        if (action.Kind == DaggerfallUiActionKind.BankLoanIssue)
        {
            DaggerfallLoanIssueDecision issue = State.Loans.Issue(region, State.Progression.Level,
                (long)amount, _time.Calendar, settlement);
            Presentation.SetOutcome(issue.Approved
                ? $"Borrowed {amount} gold into this regional account; {issue.Repayment} gold is due at classic minute {issue.DueMinute}."
                : $"Loan refused: {issue.Result}.");
            return;
        }
        bool fromAccount = action.Kind == DaggerfallUiActionKind.BankLoanRepayAccount;
        DaggerfallLoanRepaymentDecision repayment = State.Loans.Repay(region, amount, fromAccount,
            State.Bank, State.Currency, settlement);
        Presentation.SetOutcome(repayment.Applied
            ? $"Paid {repayment.AppliedAmount} gold toward this loan; {repayment.RemainingAfter} remains."
            : $"Loan repayment refused: {repayment.Result}.");
    }
}
