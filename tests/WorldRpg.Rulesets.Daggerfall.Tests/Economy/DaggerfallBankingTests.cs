using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The bank counter built over a real bank, loans and service transactions, without the session.</summary>
public sealed class DaggerfallBankingTests
{
    [Fact]
    public void A_live_banker_opens_the_bank_for_their_region_and_other_services_or_forms_do_not()
    {
        using Counter counter = new();
        DaggerfallNpc npc = counter.Fixture.Person("banking");
        DaggerfallServiceProvider banker = new(npc.DurableId, npc.Site, "banking");

        Assert.Null(counter.Banking.ActiveRegion);
        Assert.True(counter.Banking.Offers(npc));
        Assert.False(counter.Banking.Offers(null));
        Assert.False(counter.Banking.TryOpen(banker with { Service = "repair" }));
        counter.Suppressed = true;
        Assert.False(counter.Banking.TryOpenWith(npc));
        Assert.Null(counter.Opened);

        counter.Suppressed = false;
        Assert.True(counter.Banking.TryOpenWith(npc));
        Assert.Equal(banker, counter.Opened);
        Assert.Equal(banker.Site.Region, counter.Banking.ActiveRegion);
    }

    [Fact]
    public void Loans_are_refused_without_an_open_bank_or_an_amount_and_are_issued_and_repaid_with_their_lines()
    {
        using Counter counter = new();
        DaggerfallNpc npc = counter.Fixture.Person("banking");
        DaggerfallServiceProvider banker = new(npc.DurableId, npc.Site, "banking");
        Assert.Equal(DaggerfallBanking.Unavailable, counter.Banking.Borrow(1_000));

        Assert.True(counter.Banking.TryOpen(banker));
        Assert.Equal("Enter a positive bank loan amount within the regional transaction limit.", counter.Banking.Borrow(null));
        Assert.Equal("Enter a positive bank loan amount within the regional transaction limit.",
            counter.Banking.Repay((ulong)int.MaxValue + 1, fromAccount: true));

        string borrowed = counter.Banking.Borrow(1_000);
        Assert.StartsWith("Borrowed 1000 gold into this regional account; 1100 gold is due at classic minute ", borrowed);
        Assert.Equal(1_000UL, counter.Bank.BalanceForRegion(banker.Site.Region));
        Assert.Equal("Loan refused: AlreadyHaveLoan.", counter.Banking.Borrow(1_000));

        Assert.Equal("Paid 600 gold toward this loan; 500 remains.", counter.Banking.Repay(600, fromAccount: true));
        Assert.StartsWith("Loan repayment refused: ", counter.Banking.Repay(500, fromAccount: false));
    }

    [Fact]
    public void A_years_loan_reminds_at_six_three_and_one_months_and_says_nothing_on_other_days()
    {
        using Counter counter = new();
        DaggerfallNpc npc = counter.Fixture.Person("banking");
        DaggerfallServiceProvider banker = new(npc.DurableId, npc.Site, "banking");
        Assert.True(counter.Banking.TryOpen(banker));
        _ = counter.Banking.Borrow(100);

        List<string> lines = [];
        DaggerfallCalendar day = DaggerfallCalendar.Start;
        for (int index = 0; index < DaggerfallLoanPolicy.LoanTermDays - 1; index++)
        {
            DaggerfallCalendar next = day.Advance(DaggerfallCalendar.SecondsPerDay, out _);
            lines.AddRange(counter.Banking.AdvanceLoans(day, next));
            day = next;
        }

        string region = counter.RegionName(banker.Site.Region);
        Assert.Equal([
            $"You have a loan of 110 gold pieces due in less than 6 months in {region}.",
            $"You have a loan of 110 gold pieces due in less than 3 months in {region}.",
            $"You have a loan of 110 gold pieces due in less than 1 months in {region}.",
        ], lines);
        Assert.Empty(counter.Banking.AdvanceLoans(day, day));
    }

    private sealed class Counter : IDisposable
    {
        internal GuildMakerFixture Fixture { get; } = new();
        internal DaggerfallRegionalBankState Bank { get; }
        internal DaggerfallBanking Banking { get; }
        internal DaggerfallServiceProvider? Opened { get; private set; }
        internal bool Suppressed { get; set; }

        internal Counter()
        {
            Bank = new(Fixture.Currency, Fixture.Inventory, Fixture.Instances);
            Banking = new(Bank, Fixture.Currency, new DaggerfallLoanState(), Fixture.Services, Fixture.Social, Fixture.Definitions.Factions,
                new ProgressionState(), RegionName, () => Suppressed, () => Fixture.Calendar, () => Opened, provider => Opened = provider);
        }

        internal string RegionName(int region) => $"region {region}";

        public void Dispose() => Fixture.Dispose();
    }
}
