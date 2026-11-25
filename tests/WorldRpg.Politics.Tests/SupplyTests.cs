using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class SupplyTests
{
    private static RegionCondition Lasting(ConditionKind kind) => new(kind, null, 1, null, 0, 100);

    /// <summary>West holds a famished march at control 3; East, hostile, holds the other side of it.</summary>
    private static Board Famished()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchControl: 3, relation: -2));
        board.AddCondition(board.Region("march"), Lasting(ConditionKind.Famine));
        return board;
    }

    [Fact]
    public void A_region_whose_harvest_fails_is_fed_from_a_friendly_neighbour()
    {
        Board board = Famished();

        Assert.Contains(board.Region("march"), Supply.Fed(board));
        Assert.DoesNotContain(BoardTurn.Play(board).Upkeep, change => change.Field == "supply");
    }

    [Fact]
    public void A_blockade_cuts_the_line_and_the_cut_region_pays_nothing_until_it_is_fed_again()
    {
        Board board = Famished();
        Seat west = board.Seat("west");
        // Raiders on West's capital stop it passing food; East is hostile and passes none.
        board.AddCondition(board.Region("west-capital"), Lasting(ConditionKind.Raiders));
        int force = west.Force;

        IReadOnlyList<BoardChange> cut = BoardTurn.Play(board).Upkeep;

        Assert.Contains(new BoardChange("march", "supply", "fed", "cut"), cut);
        Assert.True(board.Region("march").Cut);
        // West's force is a quarter of its fed regions' control: the capital's 5 alone, not 5 and the march's 3.
        Assert.Contains(new BoardChange("west", "force", $"{force}", $"{force + 1}"), cut);

        board.RemoveConditions(board.Region("west-capital"), ConditionKind.Raiders);
        force = west.Force;
        IReadOnlyList<BoardChange> restored = BoardTurn.Play(board).Upkeep;

        Assert.Contains(new BoardChange("march", "supply", "cut", "fed"), restored);
        Assert.Contains(new BoardChange("west", "force", $"{force}", $"{force + 2}"), restored);
    }

    [Fact]
    public void A_cut_region_loses_order_every_fourth_week()
    {
        Board board = Famished();
        board.AddCondition(board.Region("west-capital"), Lasting(ConditionKind.Raiders));
        board.Turn = Supply.CutOrderWeeks - 1;

        Assert.Contains(BoardTurn.Play(board).Upkeep, change => change is { Subject: "march", Field: "order", From: "3", To: "2" });
    }

    [Fact]
    public void Food_comes_only_from_the_next_valley_so_hunger_spreads()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        board.AddCondition(board.Region("west-capital"), Lasting(ConditionKind.Famine));
        board.AddCondition(board.Region("march"), Lasting(ConditionKind.Famine));

        HashSet<Region> fed = Supply.Fed(board);

        // The march is fed from East's capital, which feeds itself; West's capital borders only the march, which does
        // not, so no food reaches it.
        Assert.Contains(board.Region("march"), fed);
        Assert.DoesNotContain(board.Region("west-capital"), fed);
    }

    [Fact]
    public void Food_passes_from_independent_regions_but_not_between_hostile_crowns()
    {
        Board hostile = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: "east", relation: -2));
        hostile.AddCondition(hostile.Region("march"), Lasting(ConditionKind.Famine));
        hostile.AddCondition(hostile.Region("east-capital"), Lasting(ConditionKind.Raiders));
        Assert.DoesNotContain(hostile.Region("march"), Supply.Fed(hostile));

        Board independent = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: null, relation: -2));
        independent.AddCondition(independent.Region("march"), Lasting(ConditionKind.Famine));
        independent.AddCondition(independent.Region("east-capital"), Lasting(ConditionKind.Raiders));
        Assert.Contains(independent.Region("march"), Supply.Fed(independent));
    }
}
