using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class TradeTests
{
    private static RegionCondition Lasting(ConditionKind kind) => new(kind, null, 1, null, 0, 100);

    private static TradeTerm Term(string text) => TradeTerm.Parse(text)!;

    /// <summary>
    /// The granary board with West's march famished and cut off: raiders on West's capital stop it passing food on, and
    /// the hills beyond grow too little to pass any. East's granary has grain to spare.
    /// </summary>
    private static Board Hungry()
    {
        Board board = TestBoards.Create(TestBoards.Granary());
        board.AddCondition(board.Region("march"), Lasting(ConditionKind.Famine));
        board.AddCondition(board.Region("west-capital"), Lasting(ConditionKind.Raiders));
        return board;
    }

    /// <summary>Puts West's coin for East's grain in force without asking East, as if it had been agreed.</summary>
    private static Agreement CoinForGrain(Board board)
    {
        board.AddAgreement(Trade.Offer(board, board.Seat("west"), board.Seat("east"), Term("coin:1/2"), Term("grain:granary")));
        return board.Agreements[^1];
    }

    [Fact]
    public void Terms_read_and_write_as_text()
    {
        Assert.Equal(new TradeTerm(Resource.Coin, 1, 2, null), TradeTerm.Parse("coin:1/2"));
        Assert.Equal(new TradeTerm(null, 1, 1, "granary"), TradeTerm.Parse("grain:granary"));
        Assert.Equal("grain", TradeTerm.Parse("grain")!.ToString());
        Assert.Equal("force:2/4", TradeTerm.Parse("force:2/4")!.ToString());
        Assert.All(new[] { "Coin:1/2", "coin:1", "coin:-1/2", "ore:1/2", "grain:" }, text => Assert.Null(TradeTerm.Parse(text)));
    }

    [Fact]
    public void Traded_grain_relieves_a_cut_region_so_it_pays_its_holder_again()
    {
        Board board = Hungry();
        Seat west = board.Seat("west");
        Assert.Contains(new BoardChange("march", "supply", "fed", "cut"), BoardTurn.Play(board).Upkeep);

        CoinForGrain(board);
        int force = west.Force;
        IReadOnlyList<BoardChange> upkeep = BoardTurn.Play(board).Upkeep;

        Assert.Contains(new BoardChange("march", "supply", "cut", "relieved"), upkeep);
        Assert.Equal(RegionSupply.Relieved, board.Region("march").Supply);
        // West's force is a quarter of its capital's control and the relieved march's: (5 + 3) / 4.
        Assert.Contains(new BoardChange("west", "force", $"{force}", $"{force + 2}"), upkeep);
    }

    [Fact]
    public void A_ration_reaches_only_overland_past_no_blockade_and_only_a_seat_with_land()
    {
        Board board = Hungry();
        Seat west = board.Seat("west");
        Seat east = board.Seat("east");
        Region granary = board.Region("granary");

        Assert.Equal(Term("grain:granary"), Trade.Resolve(board, Term("grain"), east, west));
        Assert.Contains(board.Region("march"), Trade.Reached(board, granary, east, west));

        Board guilded = TestBoards.Create(TestBoards.Granary().WithGuild());
        Assert.Null(Trade.Resolve(guilded, Term("grain"), guilded.Seat("east"), guilded.Seat("guild")));

        board.AddCondition(board.Region("hills"), Lasting(ConditionKind.Raiders));
        Assert.DoesNotContain(board.Region("march"), Trade.Reached(board, granary, east, west));
        Assert.Null(Trade.Resolve(board, Term("grain"), east, west));
    }

    [Fact]
    public void Each_resource_term_is_paid_on_the_weeks_it_falls_due()
    {
        Board board = TestBoards.Create(TestBoards.Granary());
        Seat west = board.Seat("west");
        Seat east = board.Seat("east");
        board.AddAgreement(Trade.Offer(board, west, east, Term("coin:1/2"), Term("force:2/4")));
        int start = board.Turn;

        board.Turn = start + 1;
        Trade.Settle(board);
        Assert.DoesNotContain(board.TakeChanges(), change => change.Field is "coin" or "force");

        board.Turn = start + 2;
        Trade.Settle(board);
        Assert.Equal([new("west", "coin", "4", "3"), new("east", "coin", "4", "5")], board.TakeChanges());

        board.Turn = start + 4;
        Trade.Settle(board);
        Assert.Equal(
            [new("west", "coin", "3", "2"), new("east", "coin", "5", "6"), new("east", "force", "4", "2"), new("west", "force", "4", "6")],
            board.TakeChanges());
    }

    [Fact]
    public void A_merchant_with_no_land_sells_force_for_coin_to_a_seat_that_lacks_it()
    {
        Board board = TestBoards.Create(TestBoards.Granary().WithGuild());
        Seat west = board.Seat("west");
        Seat guild = board.Seat("guild");
        board.AdjustResource(west, Resource.Coin, 4);
        board.AdjustResource(west, Resource.Force, -3);

        Trade.Propose(board, guild, west, Term("force:1/2"), Term("coin:1/2"));

        Agreement deal = Assert.Single(board.Agreements);
        Assert.Equal(("guild", "west"), (deal.Proposer, deal.Partner));
        Assert.Equal(1, board.Relation("guild", "west"));
        board.Turn = deal.Start + 2;
        board.TakeChanges();
        Trade.Settle(board);
        Assert.Contains(new BoardChange("west", "force", "1", "2"), board.TakeChanges());
    }

    [Fact]
    public void A_refused_offer_spends_the_card_and_puts_nothing_in_force()
    {
        Board board = TestBoards.Create(TestBoards.Granary());
        Seat west = board.Seat("west");

        // East has no hungry land, so West's grain is worth nothing to it against the coin it would pay.
        Assert.True(TestBoards.Card("barter").Play(board, west, ["east", "grain:west-capital", "coin:1/2"]));

        Assert.Empty(board.Agreements);
        Assert.Equal(3, west.Coin);
        Assert.Contains(new BoardChange("west", "deal:east", "none", "refused grain:west-capital for coin:1/2"), board.TakeChanges());
        Assert.Equal(0, board.Relation("west", "east"));
    }

    [Theory]
    [InlineData("lapsed", "lapsed")]
    [InlineData("hostile", "hostile")]
    [InlineData("source lost", "granary lost")]
    [InlineData("no surplus", "no surplus in granary")]
    [InlineData("route cut", "route from granary cut")]
    public void A_deal_breaks_for_both_sides_when_the_land_or_the_seats_turn_against_it(string cause, string reason)
    {
        Board board = Hungry();
        Seat west = board.Seat("west");
        Agreement deal = CoinForGrain(board);
        switch (cause)
        {
            case "lapsed": board.Turn = deal.End; break;
            case "hostile": board.AdjustRelation(west, board.Seat("east"), Supply.Hostile); break;
            case "source lost": board.SetAllegiance(board.Region("granary"), west); break;
            case "no surplus": board.AdjustProsperity(board.Region("granary"), -1); break;
            case "route cut": board.AddCondition(board.Region("hills"), Lasting(ConditionKind.Raiders)); break;
        }
        board.TakeChanges();

        HashSet<Region> relieved = Trade.Relieve(board, Supply.Fed(board));

        Assert.Empty(relieved);
        Assert.Empty(board.Agreements);
        Assert.Contains(new BoardChange("west", "deal:east", deal.ToString(), reason), board.TakeChanges());
    }

    [Fact]
    public void A_default_or_a_withdrawal_breaks_a_deal_and_costs_a_point_of_relation()
    {
        Board defaulted = Hungry();
        Agreement deal = CoinForGrain(defaulted);
        defaulted.AdjustResource(defaulted.Seat("west"), Resource.Coin, -Board.ResourceLimit);
        defaulted.Turn = deal.Start + 2;
        Trade.Settle(defaulted);
        Assert.Empty(defaulted.Agreements);
        Assert.Equal(-1, defaulted.Relation("west", "east"));
        Assert.Contains(new BoardChange("west", "deal:east", deal.ToString(), "default by west"), defaulted.TakeChanges());

        Board withdrawn = Hungry();
        deal = CoinForGrain(withdrawn);
        Trade.Withdraw(withdrawn, withdrawn.Seat("east"), deal);
        Assert.Empty(withdrawn.Agreements);
        Assert.Equal(-1, withdrawn.Relation("west", "east"));
        Assert.Contains(new BoardChange("west", "deal:east", deal.ToString(), "withdrawn by east"), withdrawn.TakeChanges());
    }

    [Fact]
    public void A_deal_fits_only_between_seats_not_hostile_with_room_for_it_and_grain_to_spare()
    {
        Board board = Hungry();
        Seat west = board.Seat("west");
        Seat east = board.Seat("east");

        Assert.True(Trade.Fits(board, west, east, Term("coin:1/2"), Term("grain:granary")));
        Assert.False(Trade.Fits(board, west, east, Term("coin:1/2"), Term("coin:1/4")));
        Assert.False(Trade.Fits(board, west, west, Term("coin:1/2"), Term("force:1/4")));

        CoinForGrain(board);
        // The granary's ration is promised.
        Assert.False(Trade.Fits(board, west, east, Term("force:1/4"), Term("grain:granary")));
        board.AddAgreement(Trade.Offer(board, west, east, Term("coin:1/2"), Term("force:1/4")));
        Assert.True(Trade.Fits(board, west, east, Term("favour:1/4"), Term("force:1/4")));
        board.AddAgreement(Trade.Offer(board, west, east, Term("favour:1/4"), Term("force:1/4")));
        Assert.False(Trade.Fits(board, east, west, Term("favour:1/4"), Term("force:1/4")));

        Board hostile = TestBoards.Create(TestBoards.Granary(relation: Supply.Hostile));
        Assert.False(Trade.Fits(hostile, hostile.Seat("west"), hostile.Seat("east"), Term("coin:1/2"), Term("force:1/4")));
    }

    [Fact]
    public void A_seat_whose_land_is_cut_off_offers_to_buy_grain_and_may_later_withdraw_as_a_move()
    {
        Board board = Hungry();
        BoardTurn.Play(board);
        board.Hold("west", "barter");
        Seat west = board.Seat("west");

        MoveOption chosen = MoveChooser.Choose(board, west, MoveChooser.Options(board, west));

        Assert.Equal("barter", chosen.Card);
        Assert.Equal(["east", "grain:granary"], new[] { chosen.Targets[0], chosen.Targets[2] });

        Agreement deal = CoinForGrain(board);
        Assert.Contains(MoveChooser.Options(board, board.Seat("east")), option => option.Card == Trade.WithdrawId && option.Targets.SequenceEqual([deal.Id]));
    }
}
