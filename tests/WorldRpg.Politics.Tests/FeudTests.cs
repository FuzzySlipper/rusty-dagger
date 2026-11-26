using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class FeudTests
{
    [Fact]
    public void Crowns_at_feud_relation_whose_influence_meets_fall_into_a_feud_that_ends_when_their_relation_lifts()
    {
        Board board = InFeud(out Feud feud);

        Assert.Equal(("west", "east", 0), (feud.A, feud.B, feud.Heat));
        Assert.Empty(board.Wars);
        Assert.Contains(new BoardChange("west", "feud:east", "none", "feud-1"), board.TakeChanges());

        board.AdjustRelation(board.Seat("west"), board.Seat("east"), 1);
        board.Turn++;
        Wars.Check(board);

        Assert.Empty(board.Feuds);
        Assert.Contains(new BoardChange("west", "feud:east", "feud-1", "cooled"), board.TakeChanges());
    }

    [Fact]
    public void No_feud_opens_where_influence_does_not_meet()
    {
        Board board = TestBoards.Create(TestBoards.Granary(relation: Feuds.FeudRelation));
        board.Turn = 1;

        Wars.Check(board);

        Assert.Empty(board.Feuds);
    }

    [Fact]
    public void Feud_cards_strike_a_foe_where_it_is_present_and_every_third_blow_cools_the_crowns_into_war()
    {
        Board board = InFeud(out Feud feud, rich: "west");
        Seat west = board.Seat("west");
        Card raid = TestBoards.Card("cattle-raid");
        Assert.True(TestBoards.Card("harass-factors").Playable(board, west, ["east-march", "east"]));
        Assert.False(raid.Playable(board, west, ["west-capital", "east"]));
        Assert.Contains(MoveChooser.Options(board, west), option => option.Card is "border-scuffle" or "cattle-raid" or "harass-factors");

        Strike(board, west, raid, ["east-march", "east"]);

        Assert.Equal((Board.ResourceLimit, 2), (west.Coin, board.Seat("east").Coin));
        Assert.Equal((1, Feuds.FeudRelation), (board.Feud(feud.Id).Heat, board.Relation("west", "east")));

        board.AdjustResource(board.Seat("east"), Resource.Coin, 4);
        Strike(board, west, raid, ["east-march", "east"]);
        Strike(board, west, raid, ["east-march", "east"]);

        Assert.Equal((0, -Board.RelationLimit), (board.Feud(feud.Id).Heat, board.Relation("west", "east")));
        board.Turn++;
        Wars.Check(board);
        Assert.Empty(board.Feuds);
        Assert.Equal(WarAim.Conquest, Assert.Single(board.Wars).Aim);
        Assert.Contains(new BoardChange("west", "feud:east", "feud-1", "war"), board.TakeChanges());
    }

    /// <summary>
    /// A raid that lands is a blow whatever it costs the foe in prestige: coin above what prestige counts, or a raid
    /// struck in payback, still heats the feud.
    /// </summary>
    [Fact]
    public void Every_raid_that_lands_is_a_blow_on_a_wealthy_foe_and_in_payback()
    {
        Board board = InFeud(out Feud feud, rich: "west");
        Seat west = board.Seat("west");
        Seat east = board.Seat("east");
        board.HoldGrudge(west, east, 10);
        Card raid = TestBoards.Card("cattle-raid");

        Strike(board, west, raid, ["east-march", "east"]);

        Assert.Empty(west.Grudges);
        Assert.Equal(1, board.Feud(feud.Id).Heat);

        board.AdjustResource(east, Resource.Coin, Board.ResourceLimit);
        Strike(board, west, raid, ["east-march", "east"]);
        Strike(board, west, raid, ["east-march", "east"]);

        Assert.Equal(Board.ResourceLimit - 4, east.Coin);
        Assert.Equal((0, -Board.RelationLimit), (board.Feud(feud.Id).Heat, board.Relation("west", "east")));
    }

    [Fact]
    public void A_raid_that_takes_nothing_is_no_blow()
    {
        Board board = InFeud(out Feud feud, rich: "west");
        board.AdjustResource(board.Seat("east"), Resource.Coin, -Board.ResourceLimit);

        Strike(board, board.Seat("west"), TestBoards.Card("cattle-raid"), ["east-march", "east"]);

        Assert.Equal(0, board.Feud(feud.Id).Heat);
    }

    [Fact]
    public void A_border_scuffle_lays_a_skirmish_named_for_the_feud()
    {
        Board board = InFeud(out Feud feud, rich: "west");

        TestBoards.Card("border-scuffle").Play(board, board.Seat("west"), ["east-march", "east"]);

        Assert.Contains(new RegionCondition(ConditionKind.Skirmish, feud.Detail, 1, "west", 1, 5), board.Region("east-march").Conditions);
        Assert.Equal(2, board.Region("east-march").Order);
    }

    [Fact]
    public void Restless_vassals_on_a_feuding_border_skirmish_of_their_own_accord()
    {
        Board board = InFeud(out Feud feud);
        board.AdjustOrder(board.Region("west-march"), -2);

        int turn = board.Turn;
        while (!board.Region("west-march").Conditions.Any(held => held.Kind == ConditionKind.Skirmish) && board.Turn < turn + 40)
        {
            board.TakeChanges();
            board.Turn++;
            Wars.Check(board);
        }

        RegionCondition skirmish = new(ConditionKind.Skirmish, feud.Detail, 1, null, board.Turn, board.Turn + Feuds.SkirmishWeeks);
        Assert.Contains(skirmish, board.Region("west-march").Conditions);
        Assert.Contains(skirmish, board.Region("east-march").Conditions);
        Assert.Contains(board.TakeChanges(), change => change is { Subject: "west", Field: "heat:feud-1", From: "0", To: "1" });
    }

    [Fact]
    public void A_heated_feud_holds_off_the_seasonal_drift_and_a_quiet_one_cools()
    {
        Board heated = InFeud(out Feud feud);
        heated.AddHeat(feud, 1);
        Board quiet = InFeud(out _);

        foreach (Board board in new[] { heated, quiet }) board.Turn = BoardTurn.SeasonWeeks - 1;

        Assert.DoesNotContain(BoardTurn.Play(heated).Upkeep, change => change.Field == "relation:west");
        Assert.Contains(new BoardChange("east", "relation:west", "-2", "-1"), BoardTurn.Play(quiet).Upkeep);
    }

    [Fact]
    public void Crowns_whose_influence_meets_without_a_border_go_to_war_over_a_grievance_that_takes_no_land()
    {
        Board board = TestBoards.Create(TestBoards.Granary(relation: -3).Rich("west").Rich("east"));
        Seat west = board.Seat("west");
        Seat east = board.Seat("east");
        board.AdjustInfluence(board.Region("hills"), west, 2);
        board.AdjustInfluence(board.Region("hills"), east, 1);
        board.AdjustControl(board.Region("granary"), -1);
        board.Turn = 1;

        Wars.Check(board);

        War war = Assert.Single(board.Wars);
        Assert.Equal(WarAim.Grievance, war.Aim);
        Assert.Equal(["march", "hills", "granary"], Wars.Fronts(board, war).Select(region => region.Id));

        TestBoards.Card("siege").Play(board, west, ["granary"]);

        Assert.Equal(("east", 0), (board.Region("granary").Allegiance, board.Region("granary").Control));

        board.AddWeariness(war, "east", Wars.PeaceWeariness);
        TestBoards.Card("sue-for-peace").Play(board, east, [war.Id]);

        Assert.Empty(board.Wars);
        Assert.Equal(("east", 0), (board.Region("granary").Allegiance, board.Region("hills").InfluenceOf("east")));
        Assert.Contains(east.Court, held => held is { Kind: CourtConditionKind.HostageTreaty, Source: "west" });
        Assert.Equal(0, board.Relation("west", "east"));
    }

    /// <summary>The frontier with West and East fallen into a feud on turn 1, and <paramref name="rich"/> at the top of every resource.</summary>
    private static Board InFeud(out Feud feud, string? rich = null)
    {
        BoardSetup setup = TestBoards.Frontier(relation: Feuds.FeudRelation);
        Board board = TestBoards.Create(rich is null ? setup : setup.Rich(rich));
        board.Turn = 1;
        Wars.Check(board);
        feud = Assert.Single(board.Feuds);
        return board;
    }

    /// <summary><paramref name="mover"/> plays <paramref name="card"/> as a turn would, and the seats it bears on remember it.</summary>
    private static void Strike(Board board, Seat mover, Card card, IReadOnlyList<string> targets)
    {
        Prestige.Witness witness = Prestige.Witness.OfCard(board, mover, new Prestige.Before(board), card, targets);
        card.Play(board, mover, targets);
        Memory.Remember(board, witness);
    }
}
