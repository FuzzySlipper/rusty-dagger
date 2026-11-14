using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The bank counter: opening the bank for a live provider, whether the person the player talks to offers it, the
/// region the open bank serves, loans issued and repaid there, and the loans' daily reminders and defaults. The
/// regional accounts and letters of credit are the bank state's; the open provider is held where the player stands
/// (the site crossing clears it), read and set through <paramref name="provider"/> and <paramref name="open"/>.
/// </summary>
/// <param name="regionName">The region's name, for a loan's reminder or default.</param>
/// <param name="inventorySuppressed">Whether a racial override keeps the player from the inventory, and so from the bank.</param>
/// <param name="provider">The bank provider the player opened, while the site holds it.</param>
/// <param name="open">Holds an opened bank provider where the player stands.</param>
internal sealed class DaggerfallBanking(
    DaggerfallRegionalBankState bank,
    DaggerfallCurrencyService currency,
    DaggerfallLoanState loans,
    DaggerfallServiceTransactions services,
    DaggerfallSocialState social,
    DaggerfallFactionsSet factions,
    ProgressionState progression,
    Func<int, string> regionName,
    Func<bool> inventorySuppressed,
    Func<DaggerfallCalendar> calendar,
    Func<DaggerfallServiceProvider?> provider,
    Action<DaggerfallServiceProvider> open)
{
    private const string Service = "banking";
    internal const string Unavailable = "A bank service is not available here.";

    /// <summary>Opens banking only for a live, site-bound NPC that actually offers it.</summary>
    internal bool TryOpen(DaggerfallServiceProvider requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        requested.Validate();
        if (inventorySuppressed()
            || !string.Equals(requested.Service, Service, StringComparison.Ordinal)
            || services.ProviderAvailable(requested) != DaggerfallServiceDenial.None)
            return false;
        open(requested);
        return true;
    }

    /// <summary>Opens banking with the person the player talks to.</summary>
    internal bool TryOpenWith(DaggerfallNpc? npc) => npc is not null && TryOpen(new DaggerfallServiceProvider(npc.DurableId, npc.Site, Service));

    /// <summary>Whether this person offers banking and can serve now.</summary>
    internal bool Offers(DaggerfallNpc? npc) => npc is not null
        && npc.Services.Contains(Service, StringComparer.Ordinal)
        && services.ProviderAvailable(new(npc.DurableId, npc.Site, Service)) == DaggerfallServiceDenial.None;

    /// <summary>The region the open bank serves, while its provider can still serve.</summary>
    internal int? ActiveRegion => provider() is { } open
        && services.ProviderAvailable(open) == DaggerfallServiceDenial.None
        ? open.Site.Region
        : null;

    /// <summary>The loans' due dates crossed between two calendar readings: the reminders, then the defaults, as lines.</summary>
    internal IReadOnlyList<string> AdvanceLoans(DaggerfallCalendar before, DaggerfallCalendar after)
    {
        DaggerfallLoanDayResult day = loans.AdvanceDue(before, after,
            bank, currency, DaggerfallLoanSettlementAdapter.ForBank(bank, currency), social, factions);
        if (day.Reminders.Count == 0 && day.Defaults.Count == 0) return [];
        List<string> lines = [.. day.Reminders.Select(reminder => $"You have a loan of {reminder.Remaining} gold pieces due in less than "
            + $"{reminder.MonthsLeft} months in {regionName(reminder.Region)}.")];
        if (day.Defaults.Count > 0)
            lines.Add(day.Defaults.Count == 1
                ? $"Loan defaulted in {regionName(day.Defaults[0].Region)}; legal and faction standing fell."
                : $"Loans defaulted in {day.Defaults.Count} regions; legal and faction standing fell.");
        return lines;
    }

    /// <summary>Borrows <paramref name="amount"/> gold into the open bank's regional account.</summary>
    internal string Borrow(ulong? amount)
    {
        if (LoanRequest(amount, out int region, out ulong principal) is { } refusal) return refusal;
        DaggerfallLoanIssueDecision issue = loans.Issue(region, progression.Level, (long)principal, calendar(),
            DaggerfallLoanSettlementAdapter.ForBank(bank, currency));
        return issue.Approved
            ? $"Borrowed {principal} gold into this regional account; {issue.Repayment} gold is due at classic minute {issue.DueMinute}."
            : $"Loan refused: {issue.Result}.";
    }

    /// <summary>Repays <paramref name="amount"/> of the open bank's loan from its account or from carried funds.</summary>
    internal string Repay(ulong? amount, bool fromAccount)
    {
        if (LoanRequest(amount, out int region, out ulong payment) is { } refusal) return refusal;
        DaggerfallLoanRepaymentDecision repayment = loans.Repay(region, payment, fromAccount,
            bank, currency, DaggerfallLoanSettlementAdapter.ForBank(bank, currency));
        return repayment.Applied
            ? $"Paid {repayment.AppliedAmount} gold toward this loan; {repayment.RemainingAfter} remains."
            : $"Loan repayment refused: {repayment.Result}.";
    }

    private string? LoanRequest(ulong? requested, out int region, out ulong amount)
    {
        amount = 0;
        if (ActiveRegion is not int active) { region = 0; return Unavailable; }
        region = active;
        if (requested is not ulong value || value > int.MaxValue)
            return "Enter a positive bank loan amount within the regional transaction limit.";
        amount = value;
        return null;
    }
}
