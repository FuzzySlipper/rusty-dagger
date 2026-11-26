using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class WarTests
{
    private static readonly Card Broker = new("broker-a-truce", "Broker a Truce", CardFamily.Court, 0, 3, 0, CardTarget.War, Visibility.Public,
        [new(CardEffectKind.Broker)]);

    [Fact]
    public void Crowns_at_war_relation_across_a_border_go_to_war_and_stay_at_war_relation()
    {
        Board board = AtWar(out War war);

        Assert.Equal(("west", "east", 1), (war.A, war.B, war.Started));
        Assert.Contains(new BoardChange("west", "war:east", "none", "war-1"), board.TakeChanges());
        Assert.Equal(["west-march", "east-march"], Wars.Fronts(board, war).Select(region => region.Id));

        board.AdjustRelation(board.Seat("west"), board.Seat("east"), 2);

        Assert.Equal(-Board.RelationLimit, board.Relation("west", "east"));
    }

    [Fact]
    public void No_war_opens_without_a_border_short_of_war_relation_or_under_a_truce()
    {
        Board apart = TestBoards.Create(TestBoards.Granary(relation: -3));
        Board cool = TestBoards.Create(TestBoards.Frontier(relation: -2));
        Board truce = TestBoards.Create(TestBoards.Frontier(relation: -3));
        truce.AddTruce(new Truce("east", "west", 10));

        foreach (Board board in new[] { apart, cool, truce })
        {
            board.Turn = 1;
            Wars.Check(board);
            Assert.Empty(board.Wars);
        }
    }

    [Fact]
    public void A_siege_takes_a_broken_front_region_and_the_front_moves_on()
    {
        Board board = AtWar(out _, rich: "west");
        Seat west = board.Seat("west");
        Card siege = TestBoards.Card("siege");

        Assert.True(siege.Playable(board, west, ["east-march"]));
        Assert.False(siege.Playable(board, west, ["east-capital"]));

        siege.Play(board, west, ["east-march"]);

        Region taken = board.Region("east-march");
        War war = Assert.Single(board.Wars);
        Assert.Equal(("west", Card.AnnexedControl), (taken.Allegiance, taken.Control));
        Assert.Equal(0, taken.InfluenceOf("east"));
        Assert.Contains(new RegionCondition(ConditionKind.WarFront, "west,east", 3, "west", 1, 9), taken.Conditions);
        Assert.Equal(Wars.HurtWeariness + Wars.LossWeariness, war.WearinessOfB);
        Assert.Equal(["east-march", "east-capital"], Wars.Fronts(board, war).Select(region => region.Id));
        Assert.False(TestBoards.Card("border-raid").Fits(board, board.Seat("east"), ["east-march"]));
    }

    [Fact]
    public void A_capital_is_besieged_but_never_taken()
    {
        Board board = AtWar(out _, rich: "west");
        Seat west = board.Seat("west");
        board.SetAllegiance(board.Region("east-march"), west);
        board.AdjustControl(board.Region("east-capital"), -4);

        TestBoards.Card("siege").Play(board, west, ["east-capital"]);

        Assert.Equal(("east", 0), (board.Region("east-capital").Allegiance, board.Region("east-capital").Control));
    }

    [Fact]
    public void A_principal_grows_weary_with_time_and_exhaustion_ends_the_war_in_a_treaty_that_cedes_a_front_region()
    {
        Board board = AtWar(out War war);
        board.Turn = 1 + Wars.WearinessWeeks;
        Wars.Check(board);
        Assert.Equal((1, 1), (board.War(war.Id).WearinessOfA, board.War(war.Id).WearinessOfB));
        board.AddCondition(board.Region("west-march"), new RegionCondition(ConditionKind.WarFront, war.Detail, 2, "east", 1, 9));

        board.AddWeariness(war, "east", Wars.Exhausted);
        board.Turn++;
        Wars.Check(board);

        Assert.Empty(board.Wars);
        Assert.Equal("west", board.Region("east-march").Allegiance);
        Assert.Equal(Wars.TreatyRelation, board.Relation("west", "east"));
        Assert.Empty(board.Region("west-march").Conditions);
        Assert.Equal([new Truce("west", "east", board.Turn + Wars.TruceWeeks)], board.Truces);
        Assert.Contains(new BoardChange("west", "war:east", "war-1", "exhausted"), board.TakeChanges());
    }

    [Fact]
    public void A_weary_principal_sues_for_peace_and_cedes_a_region_only_to_an_enemy_not_yet_weary()
    {
        Board losing = AtWar(out War war, rich: "east");
        Seat east = losing.Seat("east");
        Card sue = TestBoards.Card("sue-for-peace");
        Assert.False(sue.Playable(losing, east, [war.Id]));
        losing.AddWeariness(war, "east", Wars.PeaceWeariness);
        Assert.True(sue.Playable(losing, east, [war.Id]));

        sue.Play(losing, east, [war.Id]);

        Assert.Equal("west", losing.Region("east-march").Allegiance);
        Assert.Contains(new BoardChange("west", "war:east", "war-1", "peace sued by east"), losing.TakeChanges());

        Board even = AtWar(out war, rich: "east");
        even.AddWeariness(war, "east", Wars.PeaceWeariness);
        even.AddWeariness(war, "west", Wars.PeaceWeariness);

        sue.Play(even, even.Seat("east"), [war.Id]);

        Assert.Empty(even.Wars);
        Assert.Equal("east", even.Region("east-march").Allegiance);
    }

    [Fact]
    public void A_seat_allied_to_one_side_joins_it_and_turns_on_the_other_and_one_allied_to_both_stays_out()
    {
        Board board = TestBoards.Create(TestBoards.Frontier(relation: -3).WithGuild().Rich("guild"));
        Seat guild = board.Seat("guild");
        board.AdjustRelation(guild, board.Seat("west"), Wars.AllianceRelation);
        board.AdjustInfluence(board.Region("west-march"), guild, 1);
        board.Turn = 1;

        Wars.Check(board);

        War war = Assert.Single(board.Wars);
        Assert.Equal(["guild"], war.AlliesOfA);
        Assert.Equal(Wars.AllyEnmity, board.Relation("guild", "east"));
        Assert.Contains(new BoardChange("guild", "ally:war-1", "none", "west"), board.TakeChanges());
        Assert.True(TestBoards.Card("border-raid").Playable(board, guild, ["east-march"]));
        Assert.Contains(MoveChooser.Options(board, guild), option => option.Card == "border-raid");

        Board torn = TestBoards.Create(TestBoards.Frontier(relation: -3).WithGuild());
        torn.AdjustRelation(torn.Seat("guild"), torn.Seat("west"), Wars.AllianceRelation);
        torn.AdjustRelation(torn.Seat("guild"), torn.Seat("east"), Wars.AllianceRelation);
        torn.Turn = 1;

        Wars.Check(torn);

        Assert.Empty(Assert.Single(torn.Wars).AlliesOfA);
        Assert.Empty(torn.Wars[0].AlliesOfB);
    }

    [Fact]
    public void A_third_seat_friendly_with_both_brokers_a_truce_once_both_are_weary()
    {
        Board board = TestBoards.Create(TestBoards.Frontier(relation: -3).WithGuild().Rich("guild"));
        Seat guild = board.Seat("guild");
        board.AdjustRelation(guild, board.Seat("west"), Wars.BrokerRelation);
        board.AdjustRelation(guild, board.Seat("east"), Wars.BrokerRelation);
        board.Turn = 1;
        Wars.Check(board);
        War war = Assert.Single(board.Wars);
        board.AddWeariness(war, "west", Wars.BrokerWeariness);
        Assert.False(Broker.Playable(board, guild, [war.Id]));
        board.AddWeariness(war, "east", Wars.BrokerWeariness);
        Assert.True(Broker.Playable(board, guild, [war.Id]));

        Broker.Play(board, guild, [war.Id]);

        Assert.Empty(board.Wars);
        Assert.Equal("east", board.Region("east-march").Allegiance);
        Assert.Equal(Wars.TreatyRelation, board.Relation("west", "east"));
        Assert.Equal((2, 2), (board.Relation("guild", "west"), board.Relation("guild", "east")));
        Assert.Contains(new BoardChange("west", "war:east", "war-1", "truce brokered by guild"), board.TakeChanges());
    }

    /// <summary>
    /// A treaty cedes a front region and lifts war fronts wherever they lie, so the move that makes one is witnessed over
    /// all of them: each seat's witnessed gain is its whole change in prestige.
    /// </summary>
    [Fact]
    public void Suing_for_peace_or_brokering_a_truce_is_weighed_for_every_region_the_treaty_changes()
    {
        foreach (bool brokered in new[] { false, true })
        {
            Board board = TestBoards.Create(TestBoards.Frontier(relation: -3).WithGuild().Rich("east").Rich("guild"));
            Seat guild = board.Seat("guild");
            board.AdjustRelation(guild, board.Seat("west"), Wars.BrokerRelation);
            board.AdjustRelation(guild, board.Seat("east"), Wars.BrokerRelation);
            board.Turn = 1;
            Wars.Check(board);
            War war = Assert.Single(board.Wars);
            board.AddCondition(board.Region("west-capital"), new RegionCondition(ConditionKind.WarFront, war.Detail, 2, "east", 1, 9));
            board.AddWeariness(war, "east", Wars.PeaceWeariness);
            board.AddWeariness(war, "west", Wars.BrokerWeariness);
            (Seat player, Card card) = brokered ? (guild, Broker) : (board.Seat("east"), TestBoards.Card("sue-for-peace"));
            Dictionary<string, int> before = board.Seats.ToDictionary(seat => seat.Id, seat => Whole(board, seat));
            Prestige.Witness witness = Prestige.Witness.OfCard(board, player, new Prestige.Before(board), card, [war.Id]);

            card.Play(board, player, [war.Id]);

            Assert.Equal(brokered ? "east" : "west", board.Region("east-march").Allegiance);
            Assert.Empty(board.Region("west-capital").Conditions);
            SortedDictionary<string, int> gains = witness.Gains(board, [.. board.ChangesSince(witness.Mark)]);
            Assert.All(board.Seats, seat => Assert.Equal(Whole(board, seat) - before[seat.Id], gains.GetValueOrDefault(seat.Id)));
        }
    }

    [Fact]
    public void No_seat_draws_war_cards_and_only_a_seat_at_war_is_offered_them()
    {
        Board peace = TestBoards.Create(TestBoards.Frontier(relation: -2).Rich("west"));
        Board war = AtWar(out _, rich: "west");

        Assert.DoesNotContain(peace.Seats.SelectMany(seat => peace.Deck(seat).DrawPile.Concat(peace.Deck(seat).Hand)), card => card.Family == CardFamily.War);
        Assert.DoesNotContain(MoveChooser.Options(peace, peace.Seat("west")), option => option.Card is "border-raid" or "siege" or "muster");
        Assert.Contains(MoveChooser.Options(war, war.Seat("west")), option => option.Card is "border-raid" or "siege" or "muster");
    }

    [Fact]
    public void The_turn_records_its_war_checks()
    {
        Board board = TestBoards.Create(TestBoards.Frontier(relation: -3));

        TurnTrace turn = BoardTurn.Play(board);

        Assert.Contains(new BoardChange("west", "war:east", "none", "war-1"), turn.Wars);
        using StringWriter writer = new();
        BoardTraceWriter.WriteText(writer, turn);
        Assert.Contains("wars: west war:east none->war-1", writer.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A seat's whole prestige on a board with no trade agreements: its standing everywhere and its bearing.</summary>
    private static int Whole(Board board, Seat seat) => Prestige.Standing(seat, board.Regions) + Prestige.Bearing(board, seat);

    /// <summary>The frontier with West and East gone to war on turn 1, and <paramref name="rich"/> at the top of every resource.</summary>
    private static Board AtWar(out War war, string? rich = null)
    {
        BoardSetup setup = TestBoards.Frontier(relation: -3);
        Board board = TestBoards.Create(rich is null ? setup : setup.Rich(rich));
        board.Turn = 1;
        Wars.Check(board);
        war = Assert.Single(board.Wars);
        return board;
    }
}
