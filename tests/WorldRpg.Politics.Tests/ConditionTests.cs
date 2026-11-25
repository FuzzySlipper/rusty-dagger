using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class ConditionTests
{
    private static RegionCondition Condition(ConditionKind kind, int magnitude, int end, string? detail = null, string? source = null) =>
        new(kind, detail, magnitude, source, 0, end);

    [Fact]
    public void A_condition_ends_on_its_end_turn_and_the_trace_says_so()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        board.AddCondition(board.Region("march"), Condition(ConditionKind.Festival, 1, end: 2));

        Assert.Contains(board.Region("march").Conditions, held => held.Kind == ConditionKind.Festival);
        BoardTurn.Play(board);
        Assert.Single(board.Region("march").Conditions);

        IReadOnlyList<BoardChange> upkeep = BoardTurn.Play(board).Upkeep;

        Assert.Empty(board.Region("march").Conditions);
        Assert.Contains(new BoardChange("march", "condition:festival", "1 by world until 2", "none"), upkeep);
    }

    [Fact]
    public void The_same_condition_laid_again_refreshes_it_and_another_detail_stands_beside_it()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        Region march = board.Region("march");

        board.AddCondition(march, Condition(ConditionKind.MonsterSurge, 2, end: 10, detail: "orcs"));
        board.AddCondition(march, Condition(ConditionKind.MonsterSurge, 1, end: 20, detail: "orcs"));
        board.AddCondition(march, Condition(ConditionKind.MonsterSurge, 1, end: 5, detail: "undead"));

        Assert.Equal(
            [Condition(ConditionKind.MonsterSurge, 2, end: 20, detail: "orcs"), Condition(ConditionKind.MonsterSurge, 1, end: 5, detail: "undead")],
            march.Conditions);
        Assert.Contains(new BoardChange("march", "condition:monstersurge:orcs", "2 by world until 10", "2 by world until 20"), board.TakeChanges());
    }

    [Fact]
    public void A_famine_stops_a_regions_prosperity_paying_and_a_price_surge_adds_to_it()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: "east"));
        board.AddCondition(board.Region("east-capital"), Condition(ConditionKind.Famine, 1, end: 10));

        // East's capital (prosperity 4) pays nothing; the march (prosperity 2) alone is too little for a coin.
        Assert.DoesNotContain(BoardTurn.Play(board).Upkeep, change => change is { Subject: "east", Field: "coin" });

        board.AddCondition(board.Region("march"), Condition(ConditionKind.PriceSurge, 2, end: 10, detail: "grain"));
        Seat east = board.Seat("east");
        int coin = east.Coin;
        Assert.Contains(new BoardChange("east", "coin", $"{coin}", $"{coin + 1}"), BoardTurn.Play(board).Upkeep);
    }

    [Fact]
    public void A_stronger_condition_weighs_on_its_region_more_often()
    {
        Board strong = TestBoards.Create(TestBoards.TwoCrowns());
        strong.AddCondition(strong.Region("march"), Condition(ConditionKind.Plague, 3, end: 10));
        Assert.Contains(new BoardChange("march", "order", "3", "2"), BoardTurn.Play(strong).Upkeep);

        Board weak = TestBoards.Create(TestBoards.TwoCrowns());
        weak.AddCondition(weak.Region("march"), Condition(ConditionKind.Plague, 1, end: 10));
        Assert.DoesNotContain(BoardTurn.Play(weak).Upkeep, change => change is { Subject: "march", Field: "order" });
        weak.Turn = 3;
        Assert.Contains(BoardTurn.Play(weak).Upkeep, change => change is { Subject: "march", Field: "order" });
    }

    [Fact]
    public void A_persecution_drives_out_the_persecuted_seats_influence()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchInfluence: new Dictionary<string, int> { ["guild"] = 2 }).WithGuild());
        board.AddCondition(board.Region("march"), Condition(ConditionKind.Persecution, 3, end: 10, detail: "guild"));

        Assert.Contains(new BoardChange("march", "influence:guild", "2", "1"), BoardTurn.Play(board).Upkeep);
    }

    [Fact]
    public void A_card_lays_and_lifts_conditions_and_lifts_only_where_there_is_one()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        Seat west = board.Seat("west");
        board.Turn = 5;
        Assert.False(TestBoards.Card("heal").Fits(board, west, ["march"]));

        Assert.True(TestBoards.Card("plague-them").Play(board, west, ["march"]));
        Assert.Equal(new RegionCondition(ConditionKind.Plague, null, 3, "west", 5, 9), Assert.Single(board.Region("march").Conditions));

        Assert.True(TestBoards.Card("heal").Fits(board, west, ["march"]));
        Assert.True(TestBoards.Card("heal").Play(board, west, ["march"]));
        Assert.Empty(board.Region("march").Conditions);
    }

    [Fact]
    public void A_plot_against_a_court_needs_a_crown_within_reach_and_weighs_on_it()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns().WithGuild());
        Card whisper = TestBoards.Card("whisper");
        Seat guild = board.Seat("guild");
        // The guild is in East's capital: East's court is within its reach, West's, beyond the march, is not.
        board.AdjustInfluence(board.Region("east-capital"), guild, 1);
        Assert.True(whisper.Fits(board, guild, ["east"]));
        Assert.False(whisper.Fits(board, guild, ["west"]));
        // A power has no court to plot against.
        Assert.False(whisper.Fits(board, board.Seat("east"), ["guild"]));

        whisper.Play(board, guild, ["east"]);
        Seat east = board.Seat("east");
        int favour = east.Favour;
        Assert.Contains(new BoardChange("east", "favour", $"{favour}", $"{favour - 1}"), BoardTurn.Play(board).Upkeep);
    }

    [Fact]
    public void Hostages_keep_a_relation_from_sinking_below_neutral()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        Seat west = board.Seat("west");
        Seat east = board.Seat("east");

        TestBoards.Card("hostages").Play(board, west, ["east"]);
        board.AdjustRelation(west, east, -2);

        Assert.Equal(0, board.Relation("west", "east"));
        board.AdjustRelation(west, east, 2);
        board.AdjustRelation(west, east, -1);
        Assert.Equal(1, board.Relation("west", "east"));
    }

    [Fact]
    public void A_corrupt_advisor_skims_the_crowns_coin_for_the_seat_that_planted_it()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns().WithGuild());
        Seat east = board.Seat("east");
        board.AddCourtCondition(east, new CourtCondition(CourtConditionKind.CorruptAdvisor, 3, "guild", 0, 10));
        int guild = board.Seat("guild").Coin;

        IReadOnlyList<BoardChange> upkeep = BoardTurn.Play(board).Upkeep;

        Assert.Contains(new BoardChange("guild", "coin", $"{guild}", $"{guild + 1}"), upkeep);
        Assert.Contains(upkeep, change => change is { Subject: "east", Field: "coin" });
    }

    [Fact]
    public void A_condition_laid_in_a_trial_is_undone_with_it()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        Seat west = board.Seat("west");

        board.Trial(() => TestBoards.Card("plague-them").Play(board, west, ["march"]) && TestBoards.Card("whisper").Play(board, west, ["east"]));

        Assert.Empty(board.Region("march").Conditions);
        Assert.Empty(board.Seat("east").Court);
    }
}
