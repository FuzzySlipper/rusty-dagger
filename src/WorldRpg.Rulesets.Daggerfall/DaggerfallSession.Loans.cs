using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private void AdvanceLoans()
    {
        DaggerfallLoanDueDecision[] defaults = [.. State.Loans.AdvanceDue(_time.Calendar,
            State.Bank, State.Currency, DaggerfallLoanSettlementAdapter.ForBank(State.Bank, State.Currency),
            State.Social, _definitions.Factions)];
        if (defaults.Length > 0)
            Presentation.SetOutcome(defaults.Length == 1
                ? $"Loan defaulted in region {defaults[0].Region}; legal and faction standing fell."
                : $"Loans defaulted in {defaults.Length} regions; legal and faction standing fell.");
    }

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
        if (action.Action == "bank-loan-issue")
        {
            DaggerfallLoanIssueDecision issue = State.Loans.Issue(region, State.Progression.Level,
                (long)amount, _time.Calendar, settlement);
            Presentation.SetOutcome(issue.Approved
                ? $"Borrowed {amount} gold into this regional account; {issue.Repayment} gold is due at classic minute {issue.DueMinute}."
                : $"Loan refused: {issue.Result}.");
            return;
        }
        bool fromAccount = action.Action == "bank-loan-repay-account";
        DaggerfallLoanRepaymentDecision repayment = State.Loans.Repay(region, amount, fromAccount,
            State.Bank, State.Currency, settlement);
        Presentation.SetOutcome(repayment.Applied
            ? $"Paid {repayment.AppliedAmount} gold toward this loan; {repayment.RemainingAfter} remains."
            : $"Loan repayment refused: {repayment.Result}.");
    }
}
