using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class CardTests
{
    [Fact]
    public void A_card_pays_its_cost_and_applies_its_effects_to_its_target()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        Seat west = board.Seat("west");

        Assert.True(TestBoards.Card("garrison").Play(board, west, ["march"]));

        Assert.Equal(3, board.Region("march").Control);
        Assert.Equal(2, west.Force);
        Assert.Equal([new BoardChange("west", "force", "4", "2"), new BoardChange("march", "control", "2", "3")], board.TakeChanges());
    }

    [Fact]
    public void A_card_the_seat_cannot_pay_for_is_not_playable()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        Seat west = board.Seat("west");
        board.AdjustResource(west, Resource.Force, -3);

        Assert.False(TestBoards.Card("garrison").Playable(board, west, ["march"]));
        Assert.True(TestBoards.Card("garrison").Fits(board, west, ["march"]));
    }

    [Fact]
    public void A_card_that_would_change_nothing_but_its_payment_says_so_and_the_chooser_never_offers_it()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchControl: 5));
        board.Hold("west", "garrison");

        Assert.False(board.Trial(() => TestBoards.Card("garrison").Play(board, board.Seat("west"), ["march"])));
        IReadOnlyList<MoveOption> options = MoveChooser.Options(board, board.Seat("west"));
        Assert.DoesNotContain(options, option => option.Card == "garrison");
        Assert.Contains(options, option => option.Card == Gathering.Id);
    }

    [Fact]
    public void A_seat_chooses_only_from_the_cards_in_its_hand()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        board.Hold("west", "keep-the-peace");

        Assert.Equal([Gathering.Id, "keep-the-peace"], MoveChooser.Options(board, board.Seat("west")).Select(option => option.Card).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Regions_are_reached_only_from_a_held_neighbour_and_capitals_are_never_contested()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        Seat east = board.Seat("east");

        Assert.True(TestBoards.Card("install-factor").Playable(board, east, ["march"]));
        Assert.True(TestBoards.Card("press-claim").Playable(board, east, ["march"]));
        Assert.False(TestBoards.Card("press-claim").Playable(board, east, ["west-capital"]));
        Assert.False(TestBoards.Card("discredit").Playable(board, east, ["west-capital"]));
        Assert.False(TestBoards.Card("garrison").Playable(board, east, ["march"]));
    }

    [Fact]
    public void A_power_reaches_the_board_through_its_influence_and_offices_and_never_annexes()
    {
        Board nowhere = TestBoards.Create(TestBoards.TwoCrowns().WithGuild());
        Assert.False(TestBoards.Card("install-factor").Playable(nowhere, nowhere.Seat("guild"), ["march"]));

        Board office = TestBoards.Create(TestBoards.TwoCrowns(marchOffices: new Dictionary<Office, string> { [Office.Watch] = "guild" }).WithGuild());
        Assert.True(TestBoards.Card("install-factor").Playable(office, office.Seat("guild"), ["west-capital"]));

        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchControl: 0, marchInfluence: new Dictionary<string, int> { ["guild"] = 3 }).WithGuild());
        Seat guild = board.Seat("guild");
        board.Hold("guild", "annex");

        Assert.True(TestBoards.Card("discredit").Playable(board, guild, ["march"]));
        Assert.True(TestBoards.Card("install-factor").Playable(board, guild, ["west-capital"]));
        Assert.False(TestBoards.Card("annex").Playable(board, guild, ["march"]));
        Assert.DoesNotContain(MoveChooser.Options(board, guild), option => option.Card == "annex");
    }

    [Fact]
    public void A_seat_with_its_whole_supply_of_pieces_on_the_board_places_no_more_influence_and_takes_no_office()
    {
        Board board = Board.Create(BoardSetup.Canon(), CardSet.Canon());
        Seat guild = board.Seat("mages-guild");
        Card factor = TestBoards.Card("install-factor");
        Assert.True(factor.Playable(board, guild, ["glenpoint"]));

        foreach (string region in new[] { "daggerfall", "wayrest", "sentinel", "betony" })
            board.AdjustInfluence(board.Region(region), guild, Math.Min(Board.InfluenceLimit, Board.InfluencePieces - board.PiecesPlaced(guild)));

        Assert.Equal(Board.InfluencePieces, board.PiecesPlaced(guild));
        Assert.False(factor.Playable(board, guild, ["glenpoint"]));
        Assert.False(TestBoards.Card("suborn").Fits(board, guild, ["glenpoint"]));
        board.Hold("mages-guild", "install-factor");
        Assert.DoesNotContain(MoveChooser.Options(board, guild), option => option.Card == "install-factor");
    }

    [Fact]
    public void A_placement_of_several_pieces_takes_only_what_is_left_in_the_supply()
    {
        Board board = Board.Create(BoardSetup.Canon(), CardSet.Canon());
        Seat guild = board.Seat("mages-guild");
        FillSupply(board, guild, leave: 1);

        Assert.True(TestBoards.Card("plant-cell").Play(board, guild, ["glenpoint"]));

        Assert.Equal(1, board.Region("glenpoint").InfluenceOf("mages-guild"));
        Assert.Equal(Board.InfluencePieces, board.PiecesPlaced(guild));
    }

    [Fact]
    public void A_card_that_places_a_piece_and_takes_an_office_spends_its_supply_once()
    {
        Board board = Board.Create(BoardSetup.Canon(), CardSet.Canon());
        Seat guild = board.Seat("mages-guild");
        FillSupply(board, guild, leave: 1);

        TestBoards.Card("cell-and-watch").Play(board, guild, ["glenpoint"]);

        // The last piece went to influence, so the office stays with its local holders.
        Assert.Equal(1, board.Region("glenpoint").InfluenceOf("mages-guild"));
        Assert.False(board.Region("glenpoint").Offices.ContainsKey(Office.Watch));
        Assert.Equal(Board.InfluencePieces, board.PiecesPlaced(guild));
    }

    [Fact]
    public void A_seat_with_its_whole_supply_on_the_board_can_still_move_its_pieces()
    {
        Board board = Board.Create(BoardSetup.Canon(), CardSet.Canon());
        Seat guild = board.Seat("mages-guild");
        FillSupply(board, guild, leave: 0);
        int before = board.Region("daggerfall").InfluenceOf("mages-guild");

        Assert.True(TestBoards.Card("reassign").Play(board, guild, ["daggerfall", "glenpoint"]));

        Assert.Equal((before - 1, 1), (board.Region("daggerfall").InfluenceOf("mages-guild"), board.Region("glenpoint").InfluenceOf("mages-guild")));
        Assert.Equal(Board.InfluencePieces, board.PiecesPlaced(guild));
    }

    /// <summary>Places the guild's pieces in the great capitals and Betony until only <paramref name="leave"/> remain.</summary>
    private static void FillSupply(Board board, Seat guild, int leave)
    {
        foreach (string region in new[] { "daggerfall", "wayrest", "sentinel", "betony" })
            board.AdjustInfluence(board.Region(region), guild, Math.Min(Board.InfluenceLimit, board.PiecesLeft(guild) - leave));
        Assert.Equal(leave, board.PiecesLeft(guild));
    }

    [Fact]
    public void Annexing_needs_no_control_left_and_an_influence_lead()
    {
        Card annex = TestBoards.Card("annex");
        Board guarded = TestBoards.Create(TestBoards.TwoCrowns(marchControl: 1, marchInfluence: new Dictionary<string, int> { ["east"] = 3 }));
        Assert.False(annex.Playable(guarded, guarded.Seat("east"), ["march"]));

        Board level = TestBoards.Create(TestBoards.TwoCrowns(marchControl: 0, marchInfluence: new Dictionary<string, int> { ["east"] = 2, ["west"] = 2 }));
        Assert.False(annex.Playable(level, level.Seat("east"), ["march"]));

        Board absent = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: null, marchControl: 0));
        Assert.False(annex.Playable(absent, absent.Seat("east"), ["march"]));

        Board alone = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: null, marchControl: 0, marchInfluence: new Dictionary<string, int> { ["east"] = 1 }));
        Assert.True(annex.Playable(alone, alone.Seat("east"), ["march"]));

        Board ready = TestBoards.Create(TestBoards.TwoCrowns(marchControl: 0, marchInfluence: new Dictionary<string, int> { ["east"] = 2, ["west"] = 1 }));
        Assert.True(annex.Playable(ready, ready.Seat("east"), ["march"]));
    }

    [Fact]
    public void Annexing_takes_the_region_expels_the_former_seat_and_consolidates_it_for_a_year()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchControl: 0, marchInfluence: new Dictionary<string, int> { ["east"] = 2, ["west"] = 1 }));
        board.Turn = 10;
        Seat east = board.Seat("east");

        TestBoards.Card("annex").Play(board, east, ["march"]);

        Region march = board.Region("march");
        Assert.Equal("east", march.Allegiance);
        Assert.Equal(Card.AnnexedControl, march.Control);
        Assert.Equal(0, march.InfluenceOf("west"));
        Assert.Equal(10 + Card.ConsolidationWeeks, march.ContestableFrom);
        Assert.Equal(-2, board.Relation("west", "east"));

        Seat west = board.Seat("west");
        Assert.False(TestBoards.Card("press-claim").Playable(board, west, ["march"]));
        board.Turn = 10 + Card.ConsolidationWeeks;
        Assert.True(TestBoards.Card("press-claim").Playable(board, west, ["march"]));
    }

    [Fact]
    public void Discrediting_strips_a_piece_of_the_holders_influence_and_stirs_unrest()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchInfluence: new Dictionary<string, int> { ["west"] = 2 }));

        TestBoards.Card("discredit").Play(board, board.Seat("east"), ["march"]);

        Assert.Equal(1, board.Region("march").InfluenceOf("west"));
        Assert.Equal(2, board.Region("march").Order);
        Assert.Equal(-1, board.Relation("east", "west"));
    }

    [Fact]
    public void Undercutting_strips_a_rivals_piece_where_both_reach_and_never_its_last_foothold()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchInfluence: new Dictionary<string, int> { ["guild"] = 2 }).WithGuild());
        Card undercut = TestBoards.Card("undercut");
        Seat east = board.Seat("east");
        Assert.False(undercut.Fits(board, east, ["march", "east"]));
        Assert.False(undercut.Fits(board, east, ["march", "west"]));

        Assert.True(undercut.Play(board, east, ["march", "guild"]));
        Assert.Equal(1, board.Region("march").InfluenceOf("guild"));
        Assert.Equal(-1, board.Relation("east", "guild"));

        // The guild's last piece is its only foothold on the board, so it stays.
        undercut.Play(board, east, ["march", "guild"]);
        Assert.Equal(1, board.Region("march").InfluenceOf("guild"));
    }

    [Fact]
    public void A_region_pair_moves_a_piece_across_a_border_and_tends_both_regions()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        Seat west = board.Seat("west");
        Assert.False(TestBoards.Card("reassign").Fits(board, west, ["east-capital", "march"]));

        Assert.True(TestBoards.Card("reassign").Play(board, west, ["west-capital", "march"]));
        Assert.Equal(1, board.Region("west-capital").InfluenceOf("west"));
        Assert.Equal(1, board.Region("march").InfluenceOf("west"));

        Assert.True(TestBoards.Card("open-route").Play(board, west, ["march", "east-capital"]));
        Assert.Equal(3, board.Region("march").Prosperity);
        Assert.Equal(5, board.Region("east-capital").Prosperity);
    }

    [Fact]
    public void A_challenge_takes_an_office_only_when_it_outweighs_the_defence()
    {
        // Local people defend with 1; the guild's single piece and the card's 1 carry it.
        Board local = TestBoards.Create(TestBoards.TwoCrowns(marchInfluence: new Dictionary<string, int> { ["guild"] = 1 }).WithGuild());
        Card suborn = TestBoards.Card("suborn");
        Assert.True(suborn.Play(local, local.Seat("guild"), ["march"]));
        Assert.Equal("guild", local.Region("march").Offices[Office.Watch]);
        Assert.Contains(new BoardChange("march", "office:watch", "local", "guild"), local.TakeChanges());
        Assert.Equal(2, local.PiecesPlaced(local.Seat("guild")));

        // West holds the watch with 1 piece there: 2 against 2 falls short.
        Board held = TestBoards.Create(TestBoards.TwoCrowns(
            marchInfluence: new Dictionary<string, int> { ["guild"] = 1, ["west"] = 1 },
            marchOffices: new Dictionary<Office, string> { [Office.Watch] = "west" }).WithGuild());
        Assert.False(suborn.Play(held, held.Seat("guild"), ["march"]));

        // With a second piece the guild takes it, and West thinks the worse of it.
        held.AdjustInfluence(held.Region("march"), held.Seat("guild"), 1);
        Assert.True(suborn.Play(held, held.Seat("guild"), ["march"]));
        Assert.Equal("guild", held.Region("march").Offices[Office.Watch]);
        Assert.Equal(-1, held.Relation("guild", "west"));
    }

    [Fact]
    public void An_office_that_is_its_holders_last_foothold_is_never_taken()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(
            marchInfluence: new Dictionary<string, int> { ["east"] = 3 },
            marchOffices: new Dictionary<Office, string> { [Office.Watch] = "guild" }).WithGuild());

        Assert.False(TestBoards.Card("suborn").Play(board, board.Seat("east"), ["march"]));
        Assert.Equal("guild", board.Region("march").Offices[Office.Watch]);
    }

    [Fact]
    public void Relations_resources_and_taking_move_between_seats()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        Seat west = board.Seat("west");
        Seat east = board.Seat("east");

        TestBoards.Card("envoy").Play(board, west, ["east"]);
        Assert.Equal(1, board.Relation("west", "east"));

        TestBoards.Card("levy").Play(board, west, ["west-capital"]);
        Assert.Equal((6, 5, 3), (west.Coin, west.Force, board.Region("west-capital").Order));

        // Extortion takes what the victim has, up to its amount.
        board.AdjustResource(east, Resource.Coin, -2);
        TestBoards.Card("extort").Play(board, west, ["east"]);
        Assert.Equal((0, 7), (east.Coin, west.Coin));
    }
}
