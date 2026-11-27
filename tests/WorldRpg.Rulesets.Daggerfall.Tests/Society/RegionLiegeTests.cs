using WorldRpg.Kit;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A region's liege comes from the live political board (#11142, wiki region-liege): the region keeps its own court,
/// and its province answers to the crown the board gives it, for standing, quest credit, notices and text.
/// </summary>
public sealed class RegionLiegeTests
{
    private const int GlenpointRegion = 18;
    private const int Daggerfall = 201, Glenpoint = 202, Betony = 203, Wayrest = 207;
    private const int CourtOfGlenpoint = 521, PeopleOfGlenpoint = 522, CourtOfBetony = 519;

    private static DaggerfallPoliticalCanon Canon => TestPayload.Definitions.PoliticalCanon!;

    private static DaggerfallRegionLiege NewLiege() => new(TestPayload.Definitions.Factions, TestPayload.Definitions.BoardSeats);

    private static Board CanonBoard(DaggerfallRegionLiege liege)
    {
        Board board = Board.Create(Canon.Setup, Canon.Cards);
        liege.Bind(board);
        return board;
    }

    /// <summary>A crown takes <paramref name="region"/> on the board, as an annexation, siege or cession does.</summary>
    private static void Take(Board board, string region, string crown)
    {
        board.SetAllegiance(board.Region(region), board.Seat(crown));
        board.Consolidate(board.Region(region), board.Turn + Card.ConsolidationWeeks);
    }

    [Fact]
    public void Without_a_board_each_province_keeps_its_catalog_parent_and_the_canon_board_agrees()
    {
        DaggerfallRegionLiege liege = NewLiege();
        Assert.Equal(Daggerfall, liege.LiegeOf(19)?.Id);
        Assert.Null(liege.LiegeOf(GlenpointRegion));
        Assert.Equal(Glenpoint, liege.SovereignOf(GlenpointRegion)?.Id);

        CanonBoard(liege);
        Assert.Equal(Daggerfall, liege.LiegeOf(19)?.Id);
        Assert.Null(liege.LiegeOf(GlenpointRegion));
        // A crown's own capital answers to no one, and nothing is newly sworn at the start.
        Assert.Null(liege.LiegeOf(17));
        Assert.All(Enumerable.Range(0, 62), region => Assert.Null(liege.NewLiegeOf(region)));
    }

    [Fact]
    public void A_crown_taking_a_region_becomes_its_provinces_liege_while_the_court_stays_the_regions_own()
    {
        DaggerfallRegionLiege liege = NewLiege();
        Board board = CanonBoard(liege);

        Take(board, "glenpoint", "daggerfall");
        Assert.Equal(Daggerfall, liege.LiegeOf(GlenpointRegion)?.Id);
        Assert.Equal(Glenpoint, liege.ProvinceOf(GlenpointRegion)?.Id);
        Assert.Equal(Daggerfall, liege.ParentOf(Glenpoint));
        Assert.Equal(Glenpoint, liege.ParentOf(CourtOfGlenpoint));
        Assert.Equal([CourtOfGlenpoint, Glenpoint, Daggerfall], liege.Lineage(CourtOfGlenpoint).Select(faction => faction.Id));
        Assert.Equal(Glenpoint, liege.ChildrenOf(TestPayload.Definitions.Factions.Factions[Daggerfall]).Last());

        // Wayrest seizing Betony takes it out from under Daggerfall.
        Take(board, "betony", "wayrest");
        Assert.Equal(Wayrest, liege.ParentOf(Betony));
        Assert.DoesNotContain(Betony, liege.ChildrenOf(TestPayload.Definitions.Factions.Factions[Daggerfall]));
        Assert.Contains(Betony, liege.ChildrenOf(TestPayload.Definitions.Factions.Factions[Wayrest]));
    }

    [Fact]
    public void A_new_liege_is_proclaimed_only_while_the_crown_consolidates_the_region()
    {
        DaggerfallRegionLiege liege = NewLiege();
        Board board = CanonBoard(liege);

        Take(board, "glenpoint", "daggerfall");
        Assert.Equal(Daggerfall, liege.NewLiegeOf(GlenpointRegion)?.Id);

        board.Consolidate(board.Region("glenpoint"), board.Turn);
        Assert.Null(liege.NewLiegeOf(GlenpointRegion));
        Assert.Equal(Daggerfall, liege.LiegeOf(GlenpointRegion)?.Id);
    }

    [Fact]
    public void Standing_with_a_crown_reaches_its_vassals_court_and_a_vassals_crimes_climb_to_the_crown()
    {
        DaggerfallRegionLiege liege = NewLiege();
        Board board = CanonBoard(liege);
        DaggerfallSocialState social = new(TestPayload.Definitions.Factions, liege);
        int glenpointCourt = social.FactionReputation(CourtOfGlenpoint), betonyCourt = social.FactionReputation(CourtOfBetony);

        social.ChangeFactionReputation(Daggerfall, 10, DaggerfallFactionReputationChange.Propagate);
        Assert.Equal(glenpointCourt, social.FactionReputation(CourtOfGlenpoint));
        Assert.Equal(betonyCourt + 5, social.FactionReputation(CourtOfBetony));

        Take(board, "glenpoint", "daggerfall");
        Take(board, "betony", "wayrest");
        social.ChangeFactionReputation(Daggerfall, 10, DaggerfallFactionReputationChange.Propagate);
        Assert.Equal(glenpointCourt + 5, social.FactionReputation(CourtOfGlenpoint));
        // Betony's court no longer hears Daggerfall's standing; Wayrest's reaches it instead.
        Assert.Equal(betonyCourt + 5, social.FactionReputation(CourtOfBetony));
        social.ChangeFactionReputation(Wayrest, 10, DaggerfallFactionReputationChange.Propagate);
        Assert.Equal(betonyCourt + 10, social.FactionReputation(CourtOfBetony));

        int crown = social.FactionReputation(Daggerfall);
        social.ChangeFactionReputation(PeopleOfGlenpoint, -10, DaggerfallFactionReputationChange.Propagate);
        Assert.Equal(crown - 10, social.FactionReputation(Daggerfall));
    }

    [Fact]
    public void A_quest_for_a_taken_regions_court_credits_the_new_liege_and_the_board_never_hears_its_own_office_writes()
    {
        DaggerfallRegionLiege liege = NewLiege();
        DaggerfallPoliticalBoard board = new(Canon, TestPayload.Definitions.BoardSeats, liege,
            seat => Holding(seat, seat == "daggerfall" ? "lord-bridwell" : Canon.Setup.Seats.Single(held => held.Id == seat).Holder),
            new DaggerfallWorldConditions(), TestPayload.Definitions.Locations.Records, null, 7 * 100 + 3, null);

        board.ObserveQuestSettled(CourtOfGlenpoint, succeeded: true, GlenpointRegion);
        Assert.Empty(board.Board.Queued);

        Take(board.Board, "glenpoint", "daggerfall");
        board.ObserveQuestSettled(CourtOfGlenpoint, succeeded: true, GlenpointRegion);
        Assert.Equal("daggerfall", Assert.IsType<QuestOutcome>(Assert.Single(board.Board.Queued)).Seat);

        // An office change the board made itself is already on the board; one the world made is heard.
        board.ObserveOfficeChanged(RulerChanged(DaggerfallOfficeChangeCause.CardMove));
        Assert.Single(board.Board.Queued);
        board.ObserveOfficeChanged(RulerChanged(DaggerfallOfficeChangeCause.Death));
        Assert.IsType<HolderChanged>(board.Board.Queued[^1]);
    }

    [Fact]
    public void A_region_taken_in_play_posts_its_homage_names_its_liege_and_keeps_it_through_a_save()
    {
        using DaggerfallSession session = FreshSession();
        Board board = session.PoliticalBoard!.Board;
        DaggerfallSiteRecord town = session.PoliticalBoard.Town("glenpoint")!;
        Assert.DoesNotContain(Notices(session, town), entry => entry.Id.StartsWith("notice:homage:", StringComparison.Ordinal));

        Take(board, "glenpoint", "daggerfall");
        DaggerfallNoticeEntry homage = Assert.Single(Notices(session, town), entry => entry.Id.StartsWith("notice:homage:", StringComparison.Ordinal));
        Assert.Equal(DaggerfallNoticeEntryKind.Decree, homage.Kind);
        Assert.StartsWith("Glenpoint pays homage to the Crown of Daggerfall.", homage.Body, StringComparison.Ordinal);
        Assert.Equal(Daggerfall, homage.FactionId);

        RulesetSavePayload saved = session.CaptureSave();
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults), saved);

        Assert.Equal(Daggerfall, restored.State.Social.Liege.LiegeOf(GlenpointRegion)?.Id);
        Assert.Contains(Notices(restored, town), entry => entry.Id == homage.Id);
        Assert.NotNull(restored.CurrentTextContext().Faction.Liege);
    }

    private static IReadOnlyList<DaggerfallNoticeEntry> Notices(DaggerfallSession session, DaggerfallSiteRecord town) =>
        session.NoticeBoard.BuildForSite(town, session.Calendar.MinuteNumber).Entries;

    private static DaggerfallSeatLeaderPersona Holding(string seat, string cast) =>
        new(seat, seat, $"office:{seat}", DaggerfallOfficeKind.Ruler, 0, DaggerfallPeopleSlots.CastIndividualIdentity(cast),
            cast, null, cast, null, false, IsOverridden: true);

    private static DaggerfallOfficeChangedNotification RulerChanged(DaggerfallOfficeChangeCause cause) =>
        new("office:changed", DaggerfallOfficeKind.Ruler, DaggerfallOfficeChangeKind.SuccessorSeated, cause, 17, null, null, null, Daggerfall,
            "daggerfall", "King", null, null, "King Gothryd", null, false, null, "lord-bridwell", "Lord Bridwell", null, 10, 14_400, null, "was deposed");
}
