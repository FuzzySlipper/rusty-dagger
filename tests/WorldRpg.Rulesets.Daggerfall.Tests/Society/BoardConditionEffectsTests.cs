using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Every board condition shows in the world as a token with words of its own and changes something in normal play
/// (#11120; Patch: "every thing should produce at least minor normal play differences").
/// </summary>
public sealed class BoardConditionEffectsTests
{
    private const long Wednesday = 7 * 100 + 3;
    private const int Glenpoint = 18, Betony = 19, DaggerfallRegion = 17, DaggerfallCrown = 201;

    private static DaggerfallPoliticalCanon Canon => TestPayload.Definitions.PoliticalCanon!;

    private static DaggerfallPoliticalBoard NewBoard(DaggerfallWorldConditions conditions, DaggerfallPoliticalBoardSave? saved = null) =>
        new(Canon, TestPayload.Definitions.BoardSeats, new DaggerfallRegionLiege(TestPayload.Definitions.Factions, TestPayload.Definitions.BoardSeats),
            seat => new(seat, seat, $"office:{seat}", DaggerfallOfficeKind.Ruler, 0, null, null, null, string.Empty, null, false, IsOverridden: true),
            conditions, TestPayload.Definitions.Locations.Records, null, Wednesday, saved);

    private static string? Detail(ConditionKind kind) => kind switch
    {
        ConditionKind.PriceSurge => "iron",
        ConditionKind.MonsterSurge => "orc",
        ConditionKind.WarFront or ConditionKind.Skirmish => "daggerfall,wayrest",
        ConditionKind.Persecution => "mages-guild",
        _ => null,
    };

    [Fact]
    public void Every_region_and_court_condition_and_a_cut_off_region_shows_as_a_token_naming_no_secret_seat()
    {
        DaggerfallPoliticalBoard laid = NewBoard(new DaggerfallWorldConditions());
        Board board = laid.Board;
        Region glenpoint = board.Region("glenpoint"), betony = board.Region("betony");
        foreach (ConditionKind kind in Enum.GetValues<ConditionKind>())
            board.AddCondition(glenpoint, new RegionCondition(kind, Detail(kind), 2, "thieves-guild", board.Turn, board.Turn + 50));
        board.SetSupply(betony, RegionSupply.Cut);
        for (int week = 0; week < 5; week++) board.CountCut(betony);
        foreach (CourtConditionKind kind in Enum.GetValues<CourtConditionKind>())
            board.AddCourtCondition(board.Seat("daggerfall"), new CourtCondition(kind, 2, "underking", board.Turn, board.Turn + 50));

        DaggerfallWorldConditions world = new();
        DaggerfallPoliticalBoard rebuilt = NewBoard(world, laid.Capture());

        IReadOnlyList<DaggerfallWorldConditionToken> glen = rebuilt.TokensFor(Glenpoint);
        Assert.Equal(Enum.GetValues<ConditionKind>().Length, glen.Count);
        Assert.All(glen.Where(token => token.Kind != DaggerfallWorldConditionKind.MonsterSurge), token =>
        {
            Assert.False(string.IsNullOrWhiteSpace(token.Headline));
            Assert.False(string.IsNullOrWhiteSpace(token.Body));
        });
        Assert.Contains(glen, token => token is { Kind: DaggerfallWorldConditionKind.Decree, SubjectTag: DaggerfallConditionPrices.PriceSurgeTag }
            && token.Headline == "Iron Dear in Glenpoint");

        DaggerfallWorldConditionToken cut = Assert.Single(rebuilt.TokensFor(Betony), token => token.SubjectTag == DaggerfallPoliticalBoard.CutOffTag);
        Assert.Equal(DaggerfallWorldConditionKind.GrainShortage, cut.Kind);
        Assert.Equal(2, cut.Severity);
        Assert.Contains("5 weeks", cut.Body, StringComparison.Ordinal);

        DaggerfallWorldConditionToken[] court = [.. rebuilt.TokensFor(DaggerfallRegion).Where(token => token.Kind == DaggerfallWorldConditionKind.Decree)];
        Assert.Equal(
            [DaggerfallConditionPrices.CorruptAdvisorTag, DaggerfallPoliticalBoard.HostageTreatyTag, DaggerfallPoliticalBoard.ParanoidCourtTag],
            court.Select(token => token.SubjectTag).Order(StringComparer.Ordinal));
        Assert.All(court, token => Assert.Equal(DaggerfallCrown, token.SourceFactionId));

        // A plot's plotter is never named: not the thieves who laid the conditions, nor the Underking behind the court's troubles.
        string underking = board.Seat("underking").Name, thieves = board.Seat("thieves-guild").Name;
        Assert.All(glen.Concat(court).Where(token => token.Kind != DaggerfallWorldConditionKind.MonsterSurge), token =>
        {
            Assert.DoesNotContain(thieves, token.Headline + token.Body, StringComparison.Ordinal);
            if (token.SubjectTag != DaggerfallPoliticalBoard.HostageTreatyTag)
                Assert.DoesNotContain(underking, token.Headline + token.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Price_surges_corrupt_advisors_and_prosperity_raise_prices_and_recruitment_makes_hirelings_cheap()
    {
        DaggerfallWorldConditions world = new();
        DaggerfallConditionPrices prices = new(world, () => 10);
        Assert.Equal(0, prices.GoodsAdjustment(Glenpoint));
        Assert.Equal(25UL, prices.HireFee(Glenpoint, 25UL));

        foreach (string tag in new[] { DaggerfallConditionPrices.PriceSurgeTag, DaggerfallConditionPrices.CorruptAdvisorTag, DaggerfallConditionPrices.ProsperityTag, DaggerfallConditionPrices.RecruitmentTag })
            world.Set(new DaggerfallWorldConditionToken($"board:glenpoint:{tag}", DaggerfallWorldConditionKind.Decree, Region: Glenpoint, SubjectTag: tag, Severity: 2).Validate());

        Assert.Equal(2 * (DaggerfallConditionPrices.PriceSurgePerSeverity + DaggerfallConditionPrices.CorruptAdvisorPerSeverity + DaggerfallConditionPrices.ProsperityPerSeverity),
            prices.GoodsAdjustment(Glenpoint));
        Assert.Equal(12UL, prices.HireFee(Glenpoint, 25UL));
        Assert.Equal(0, prices.GoodsAdjustment(Betony));
    }

    /// <summary>Patch: "crimecrackdown should increase the number of guard spawns in affected cities".</summary>
    [Fact]
    public void A_town_holds_a_reserve_watch_after_its_walkers_that_leaves_their_slots_alone()
    {
        DaggerfallSiteProfile town = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallPopulationPlacement[] walkers = [.. town.Population.Where(placement => placement.CrackdownGuard == 0)];
        DaggerfallPopulationPlacement[] reserve = [.. town.Population.Where(placement => placement.CrackdownGuard > 0)];

        Assert.Equal(Enumerable.Range(1, DaggerfallExteriorPopulation.CrackdownGuardSlots), reserve.Select(placement => placement.CrackdownGuard));
        Assert.All(reserve, placement => Assert.True(placement.IsGuard));
        Assert.Equal(town.Population.Take(walkers.Length), walkers);
        Assert.Equal(DaggerfallExteriorPopulation.ExteriorSlotId(TestPayload.Definitions.Locations.Records.Single(site => site.Id == Charing), walkers.Length),
            reserve[0].Id);
        Assert.Equal(reserve.Length, reserve.Select(placement => placement.Position).Distinct().Count());
    }

    [Fact]
    public void A_crackdown_calls_out_the_reserve_watch_by_its_severity_and_a_crime_wave_calls_out_no_one()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallPopulationPlacement[] reserve = [.. Assembled(DaggerfallWorldProfileIds.Exterior(Charing)).Population.Where(placement => placement.CrackdownGuard > 0)];
        DaggerfallPopulationPlacement watch = Assembled(DaggerfallWorldProfileIds.Exterior(Charing)).Population.First(placement => placement.IsGuard && placement.CrackdownGuard == 0);
        DaggerfallSchedulePlacement Walks(DaggerfallPopulationPlacement placement) =>
            session.EvaluateExteriorPlacementSchedule(Charing, placement, 0, 100).Placement;
        DaggerfallSchedulePlacement onDuty = Walks(watch);

        Assert.Equal(0, session.CrackdownGuards.For(DaggerfallRegion));
        Assert.All(reserve, placement => Assert.Equal(DaggerfallSchedulePlacement.ShelteredInHome, Walks(placement)));

        session.WorldConditions.Set(new DaggerfallWorldConditionToken("board:daggerfall:crime-wave", DaggerfallWorldConditionKind.CrimeCrackdown,
            Region: DaggerfallRegion, SubjectTag: DaggerfallConditionPrices.CrimeWaveTag, Severity: 3).Validate());
        Assert.Equal(0, session.CrackdownGuards.For(DaggerfallRegion));

        session.WorldConditions.Set(new DaggerfallWorldConditionToken("board:daggerfall:crackdown", DaggerfallWorldConditionKind.CrimeCrackdown,
            Region: DaggerfallRegion, SubjectTag: DaggerfallCrackdownGuards.CrackdownTag, Severity: 2).Validate());
        Assert.Equal(2 * DaggerfallTuning.Defaults.Law.CrackdownGuardsPerSeverity, session.CrackdownGuards.For(DaggerfallRegion));
        Assert.Equal([onDuty, onDuty, DaggerfallSchedulePlacement.ShelteredInHome], reserve.Take(3).Select(Walks));
    }

    [Fact]
    public void A_paranoid_court_tightens_its_capitals_gates_and_with_a_hostage_treaty_pauses_court_introductions()
    {
        using DaggerfallSession session = FreshSession();
        Assert.Equal(DaggerfallScrutinyLevel.Normal, session.Scrutiny.EvaluateGateScrutiny(Betony).Level);
        Assert.False(session.Introductions.IsCourtInVacancyOrCrisis(Betony, 203));

        session.WorldConditions.Set(new DaggerfallWorldConditionToken("board:betony:hostage-treaty:wayrest", DaggerfallWorldConditionKind.Decree,
            Region: Betony, SubjectTag: DaggerfallPoliticalBoard.HostageTreatyTag, Severity: 1).Validate());
        Assert.True(session.Introductions.IsCourtInVacancyOrCrisis(Betony, 203));
        Assert.Equal(DaggerfallScrutinyLevel.Normal, session.Scrutiny.EvaluateGateScrutiny(Betony).Level);

        session.WorldConditions.Set(new DaggerfallWorldConditionToken("board:betony:paranoid-court:underking", DaggerfallWorldConditionKind.Decree,
            Region: Betony, SubjectTag: DaggerfallPoliticalBoard.ParanoidCourtTag, Severity: 2).Validate());
        DaggerfallGateScrutinyAssessment gate = session.Scrutiny.EvaluateGateScrutiny(Betony);
        Assert.Equal(DaggerfallScrutinyLevel.Strict, gate.Level);
        Assert.Contains("trusts no one", gate.Reason, StringComparison.Ordinal);
    }
}
