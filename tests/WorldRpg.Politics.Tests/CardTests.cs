using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class CardTests
{
    [Fact]
    public void A_card_pays_its_cost_and_applies_its_effects_to_its_target()
    {
        Board board = Board.Create(TestBoards.TwoCrowns());
        Seat west = board.Seat("west");

        Assert.True(TestBoards.Card("garrison").Play(board, west, "march"));

        Assert.Equal(3, board.Region("march").Control);
        Assert.Equal(2, west.Force);
        Assert.Equal([new BoardChange("west", "force", "4", "2"), new BoardChange("march", "control", "2", "3")], board.TakeChanges());
    }

    [Fact]
    public void A_card_that_would_change_nothing_but_its_payment_says_so_and_the_chooser_never_offers_it()
    {
        Board board = Board.Create(TestBoards.TwoCrowns(marchControl: 5));

        Assert.False(TestBoards.Card("garrison").Play(board.Clone(), board.Seat("west"), "march"));
        Assert.DoesNotContain(MoveChooser.Options(board, board.Seat("west")), option => option.Card == "garrison");
        Assert.Contains(MoveChooser.Options(board, board.Seat("west")), option => option.Card == "bank");
    }

    [Fact]
    public void Regions_are_reached_only_from_a_held_neighbour_and_capitals_are_never_contested()
    {
        Board board = Board.Create(TestBoards.TwoCrowns());
        Seat east = board.Seat("east");

        Assert.True(TestBoards.Card("install-factor").Playable(board, east, "march"));
        Assert.True(TestBoards.Card("press-claim").Playable(board, east, "march"));
        Assert.False(TestBoards.Card("press-claim").Playable(board, east, "west-capital"));
        Assert.False(TestBoards.Card("discredit").Playable(board, east, "west-capital"));
        Assert.False(TestBoards.Card("garrison").Playable(board, east, "march"));
    }

    [Fact]
    public void A_power_reaches_the_board_through_its_influence_and_never_annexes()
    {
        Board nowhere = Board.Create(TestBoards.TwoCrowns().WithGuild());
        Assert.False(TestBoards.Card("install-factor").Playable(nowhere, nowhere.Seat("guild"), "march"));

        Board board = Board.Create(TestBoards.TwoCrowns(marchControl: 0, marchInfluence: new Dictionary<string, int> { ["guild"] = 3 }).WithGuild());
        Seat guild = board.Seat("guild");

        Assert.True(TestBoards.Card("discredit").Playable(board, guild, "march"));
        Assert.True(TestBoards.Card("install-factor").Playable(board, guild, "west-capital"));
        Assert.False(TestBoards.Card("annex").Playable(board, guild, "march"));
        Assert.DoesNotContain(MoveChooser.Options(board, guild), option => option.Card == "annex");
    }

    [Fact]
    public void A_seat_with_its_whole_supply_of_pieces_on_the_board_places_no_more()
    {
        Board board = Board.Create(BoardSetup.Canon());
        Seat guild = board.Seat("mages-guild");
        Card factor = TestBoards.Card("install-factor");
        Assert.True(factor.Playable(board, guild, "glenpoint"));

        foreach (string region in new[] { "daggerfall", "wayrest", "sentinel", "betony" })
            board.AdjustInfluence(board.Region(region), guild, Math.Min(Board.InfluenceLimit, Board.InfluencePieces - board.PiecesPlaced(guild)));

        Assert.Equal(Board.InfluencePieces, board.PiecesPlaced(guild));
        Assert.False(factor.Playable(board, guild, "glenpoint"));
        Assert.DoesNotContain(MoveChooser.Options(board, guild), option => option.Card == "install-factor");
    }

    [Fact]
    public void Annexing_needs_no_control_left_and_an_influence_lead_of_at_least_two()
    {
        Card annex = TestBoards.Card("annex");
        Assert.False(annex.Playable(Board.Create(TestBoards.TwoCrowns(marchControl: 1, marchInfluence: new Dictionary<string, int> { ["east"] = 3 })),
            Board.Create(TestBoards.TwoCrowns()).Seat("east"), "march"));

        Board level = Board.Create(TestBoards.TwoCrowns(marchControl: 0, marchInfluence: new Dictionary<string, int> { ["east"] = 2, ["west"] = 2 }));
        Assert.False(annex.Playable(level, level.Seat("east"), "march"));

        Board alone = Board.Create(TestBoards.TwoCrowns(marchAllegiance: null, marchControl: 0, marchInfluence: new Dictionary<string, int> { ["east"] = 1 }));
        Assert.False(annex.Playable(alone, alone.Seat("east"), "march"));

        Board ready = Board.Create(TestBoards.TwoCrowns(marchControl: 0, marchInfluence: new Dictionary<string, int> { ["east"] = 2, ["west"] = 1 }));
        Assert.True(annex.Playable(ready, ready.Seat("east"), "march"));
    }

    [Fact]
    public void Annexing_takes_the_region_expels_the_former_seat_and_consolidates_it_for_a_year()
    {
        Board board = Board.Create(TestBoards.TwoCrowns(marchControl: 0, marchInfluence: new Dictionary<string, int> { ["east"] = 2, ["west"] = 1 }));
        board.Turn = 10;
        Seat east = board.Seat("east");

        TestBoards.Card("annex").Play(board, east, "march");

        Region march = board.Region("march");
        Assert.Equal("east", march.Allegiance);
        Assert.Equal(Card.AnnexedControl, march.Control);
        Assert.Equal(0, march.InfluenceOf("west"));
        Assert.Equal(10 + Card.ConsolidationWeeks, march.ContestableFrom);
        Assert.Equal(-2, board.Relation("west", "east"));

        Seat west = board.Seat("west");
        Assert.False(TestBoards.Card("press-claim").Playable(board, west, "march"));
        board.Turn = 10 + Card.ConsolidationWeeks;
        Assert.True(TestBoards.Card("press-claim").Playable(board, west, "march"));
    }

    [Fact]
    public void Discrediting_strips_a_piece_of_the_holders_influence_and_stirs_unrest()
    {
        Board board = Board.Create(TestBoards.TwoCrowns(marchInfluence: new Dictionary<string, int> { ["west"] = 2 }));

        TestBoards.Card("discredit").Play(board, board.Seat("east"), "march");

        Assert.Equal(1, board.Region("march").InfluenceOf("west"));
        Assert.Equal(2, board.Region("march").Order);
        Assert.Equal(-1, board.Relation("east", "west"));
    }

    [Fact]
    public void Banking_adds_to_the_scarcest_resource()
    {
        Board board = Board.Create(TestBoards.TwoCrowns());
        Seat west = board.Seat("west");
        board.AdjustResource(west, Resource.Favour, -3);

        StarterCards.Bank.Play(board, west, null);

        Assert.Equal(2, west.Favour);
    }
}
