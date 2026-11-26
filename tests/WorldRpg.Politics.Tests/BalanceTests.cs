using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class BalanceTests
{
    /// <summary>The two crowns and a guild that keeps the balance, whose Force arrives through the march; its coin comes regardless.</summary>
    private static BoardSetup Arbitrated(string? marchAllegiance = "west") => TestBoards.TwoCrowns(marchAllegiance: marchAllegiance)
        .WithGuild([new SeatGrant(Resource.Force, 1, 1), new SeatGrant(Resource.Coin, 1, 1)]) is var setup
            ? setup with { Seats = [.. setup.Seats.Select(seat => seat.Id == "guild" ? seat with { Reserves = "march", Arbiter = true } : seat)] }
            : setup;

    [Fact]
    public void Deliveries_stop_once_the_reserves_point_has_been_contested_four_weeks_running_and_resume_when_it_lifts()
    {
        Board board = TestBoards.Create(Arbitrated());
        Assert.True(Paid(BoardTurn.Play(board), Resource.Force));
        board.AddCondition(board.Region("march"), new RegionCondition(ConditionKind.Raiders, null, 1, null, board.Turn, board.Turn + 100));

        TurnTrace[] contested = [.. Enumerable.Range(0, Balance.ReservesBite - 1).Select(_ => BoardTurn.Play(board))];
        board.AdjustResource(board.Seat("guild"), Resource.Coin, -Board.ResourceLimit);
        contested = [.. contested, BoardTurn.Play(board)];

        Assert.All(contested[..^1], turn => Assert.True(Paid(turn, Resource.Force)));
        Assert.False(Paid(contested[^1], Resource.Force));
        Assert.True(Paid(contested[^1], Resource.Coin));
        Assert.Contains(new BoardChange("guild", "reserves", "open", "cut"), contested[^1].Upkeep);

        board.RemoveConditions(board.Region("march"), ConditionKind.Raiders);
        TurnTrace lifted = BoardTurn.Play(board);

        Assert.True(Paid(lifted, Resource.Force));
        Assert.Contains(new BoardChange("guild", "reserves", "cut", "open"), lifted.Upkeep);
    }

    [Fact]
    public void A_hostile_seat_out_influencing_the_receiver_contests_its_reserves_point()
    {
        Board board = TestBoards.Create(Arbitrated());
        Seat guild = board.Seat("guild");
        Assert.False(Balance.Contested(board, guild));

        board.AdjustInfluence(board.Region("march"), board.Seat("east"), 1);
        Assert.False(Balance.Contested(board, guild));

        board.AdjustRelation(guild, board.Seat("east"), Supply.Hostile);
        Assert.True(Balance.Contested(board, guild));

        board.AdjustInfluence(board.Region("march"), guild, 1);
        Assert.False(Balance.Contested(board, guild));
    }

    [Fact]
    public void A_seat_leads_only_once_it_has_pulled_ahead_and_an_arbiter_never_leads()
    {
        Board even = TestBoards.Create(Arbitrated(marchAllegiance: null));
        Board ahead = TestBoards.Create(Arbitrated());

        Assert.Null(Balance.FindLeader(even));
        Assert.Equal("west", Balance.FindLeader(ahead)?.Id);
        Assert.True(Prestige.Total(ahead, ahead.Seat("west")) - Prestige.Total(ahead, ahead.Seat("east")) >= Balance.LeadMargin);
    }

    [Fact]
    public void Each_season_every_relation_with_the_leader_drifts_towards_coolness()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns().WithGuild());
        board.AdjustRelation(board.Seat("guild"), board.Seat("east"), 1);
        board.Turn = BoardTurn.SeasonWeeks - 1;

        IReadOnlyList<BoardChange> upkeep = BoardTurn.Play(board).Upkeep;

        Assert.Equal("west", board.Leader);
        Assert.Contains(new BoardChange("east", "relation:west", "0", "-1"), upkeep);
        Assert.Contains(new BoardChange("guild", "relation:west", "0", "-1"), upkeep);
        Assert.Contains(new BoardChange("east", "relation:guild", "1", "0"), upkeep);
    }

    [Fact]
    public void Trouble_that_may_fall_anywhere_falls_twice_as_often_on_the_leaders_regions()
    {
        WorldCard plague = new("plague", "Plague", ConditionKind.Plague, 1, 1, WorldTarget.AnyRegion);
        Board board = Board.Create(TestBoards.TwoCrowns(), new CardSet([.. TestBoards.Cards.Cards.Where(card => card.Seat is null)], [plague]), seed: 3);
        board.SetLeader(board.Seat("west"));

        int west = 0;
        const int Turns = 600;
        for (int turn = 0; turn < Turns; turn++)
            if (board.TurnWorldCard() is (_, Region region) && region.Allegiance == "west") west++;

        Assert.InRange((double)west / Turns, 0.75, 0.85);
    }

    [Fact]
    public void An_arbiter_counts_buffer_baronies_and_wars_and_brokers_a_truce_with_any_principal_it_is_not_hostile_to()
    {
        Board board = TestBoards.Create(TestBoards.Frontier(relation: -3).WithGuild().Rich("guild") is var setup
            ? setup with { Seats = [.. setup.Seats.Select(seat => seat.Id == "guild" ? seat with { Arbiter = true } : seat)] }
            : setup);
        Seat guild = board.Seat("guild");
        int before = Prestige.Bearing(board, guild);
        board.Turn = 1;
        Wars.Check(board);
        War war = Assert.Single(board.Wars);

        Assert.Equal(before - Balance.WarCost, Prestige.Bearing(board, guild));
        board.AddWeariness(war, "west", Wars.BrokerWeariness);
        board.AddWeariness(war, "east", Wars.BrokerWeariness);
        Assert.True(Wars.MayBroker(board, board.War(war.Id), guild));
        board.AdjustRelation(guild, board.Seat("east"), Supply.Hostile);
        Assert.False(Wars.MayBroker(board, board.War(war.Id), guild));
    }

    /// <summary>
    /// Raiders kept on the roads around the Empire's reserves point in Daggerfall, as a seat's raids or the player's
    /// meddling might keep them: its Force runs dry and its countermoves fall off.
    /// </summary>
    [Fact]
    public void When_the_Empires_reserves_point_is_kept_contested_its_interventions_fall_off()
    {
        (int freeInterventions, _) = Play(contest: false);
        (int starvedInterventions, int cutWeeks) = Play(contest: true);

        Assert.True(cutWeeks > TurnTrace.TurnsIn(Years) - Balance.ReservesBite - 1);
        Assert.True(starvedInterventions < 0.75 * freeInterventions, $"{starvedInterventions} interventions starved, {freeInterventions} free");
    }

    private const int Years = 20;

    /// <summary>The canon game for <see cref="Years"/> years, with raiders laid every week at Daggerfall if <paramref name="contest"/>.</summary>
    private static (int Interventions, int CutWeeks) Play(bool contest)
    {
        Board board = Board.Create(BoardSetup.Canon(), CardSet.Canon());
        Seat empire = board.Seat("empire");
        Dictionary<string, Card> cards = CardSet.Canon().Cards.ToDictionary(card => card.Id);
        int interventions = 0, cut = 0;
        for (int turn = 0; turn < TurnTrace.TurnsIn(Years); turn++)
        {
            if (contest) board.AddCondition(board.Region("daggerfall"), new RegionCondition(ConditionKind.Raiders, null, 1, null, board.Turn, board.Turn + 2));
            TurnTrace trace = BoardTurn.Play(board);
            interventions += trace.Moves.Count(move => move.Seat == "empire" && cards.TryGetValue(move.Card, out Card? card)
                && card.Target is CardTarget.War or CardTarget.IndependentRegion or CardTarget.Leader);
            if (Balance.ReservesCut(empire)) cut++;
        }
        return (interventions, cut);
    }

    private static bool Paid(TurnTrace turn, Resource resource) =>
        turn.Upkeep.Any(change => change.Subject == "guild" && change.Field == resource.ToString().ToLowerInvariant()
            && int.Parse(change.To, System.Globalization.CultureInfo.InvariantCulture) > int.Parse(change.From, System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>The canon table played from three fixed seeds for fifty years, once for every band here.</summary>
public sealed class FiftyYearsOfSeeds
{
    public IReadOnlyList<GameBalance> Games { get; } = BalanceReport.Run(BoardSetup.Canon(), CardSet.Canon(), [1, 2, 3], years: 50);
}

/// <summary>The bands the canon table's balance is held to over fifty years, tuned with Patch on the design thread.</summary>
public sealed class BalanceBandTests(FiftyYearsOfSeeds run) : IClassFixture<FiftyYearsOfSeeds>
{
    [Fact]
    public void No_seat_ever_holds_more_than_two_fifths_of_the_regions()
    {
        int regions = BoardSetup.Canon().Regions.Count;

        Assert.All(run.Games, game => Assert.All(game.PeakRegions.Values, peak => Assert.True(peak * 5 <= regions * 2, $"{peak} of {regions}")));
    }

    [Fact]
    public void Allegiance_changes_hands_a_few_times_a_decade()
    {
        Assert.All(run.Games, game => Assert.InRange((double)game.AllegianceChanges / game.Years, 0.05, 1.0));
    }

    [Fact]
    public void Every_seat_plays_cards_of_more_than_one_family()
    {
        Assert.All(run.Games, game => Assert.All(game.Families, seat => Assert.True(seat.Value.Count > 1, seat.Key)));
    }

    [Fact]
    public void The_Empire_intervenes_and_its_reserves_are_sometimes_cut()
    {
        Assert.All(run.Games, game => Assert.True(game.Interventions > 0));
        Assert.Contains(run.Games, game => game.ReservesCutWeeks > 0);
    }
}
