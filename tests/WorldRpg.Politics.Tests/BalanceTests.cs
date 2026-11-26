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

    /// <summary>
    /// A crown whose Force arrives through its march: its capital's Force income stops in the same week as everything
    /// else once the march has been contested four weeks running, and comes back in the week the contest lifts.
    /// </summary>
    [Fact]
    public void A_cut_stops_and_a_reopening_restores_the_Force_of_held_land_in_the_same_week_as_the_rest()
    {
        BoardSetup setup = TestBoards.TwoCrowns();
        Board board = TestBoards.Create(setup with { Seats = [.. setup.Seats.Select(seat => seat.Id == "west" ? seat with { Reserves = "march" } : seat)] });
        Assert.True(Paid(BoardTurn.Play(board), Resource.Force, "west"));
        board.AddCondition(board.Region("march"), new RegionCondition(ConditionKind.Raiders, null, 1, null, board.Turn, board.Turn + 100));

        TurnTrace[] contested = [.. Enumerable.Range(0, Balance.ReservesBite).Select(_ => BoardTurn.Play(board))];

        Assert.All(contested[..^1], turn => Assert.True(Paid(turn, Resource.Force, "west")));
        Assert.False(Paid(contested[^1], Resource.Force, "west"));
        Assert.Contains(new BoardChange("west", "reserves", "open", "cut"), contested[^1].Upkeep);

        board.RemoveConditions(board.Region("march"), ConditionKind.Raiders);
        board.AdjustResource(board.Seat("west"), Resource.Force, -Board.ResourceLimit);
        TurnTrace lifted = BoardTurn.Play(board);

        Assert.Contains(new BoardChange("west", "reserves", "cut", "open"), lifted.Upkeep);
        Assert.True(Paid(lifted, Resource.Force, "west"));
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
    public void An_arbiter_loses_for_each_war_and_brokers_a_truce_with_any_principal_it_is_not_hostile_to()
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

    [Fact]
    public void An_arbiter_loses_half_the_gap_between_the_strongest_crown_and_the_weakest()
    {
        Board board = TestBoards.Create(Arbitrated());
        int gap = Prestige.Total(board, board.Seat("west")) - Prestige.Total(board, board.Seat("east"));

        Assert.True(gap > 0);
        Assert.Equal(-gap / 2, Balance.Arbitration(board, board.Seat("guild")));
        Assert.Equal(0, Balance.Arbitration(board, board.Seat("west")));
    }

    [Fact]
    public void Sowing_discord_cools_a_friend_of_the_leader_towards_it()
    {
        Board board = TestBoards.Create(Arbitrated().Rich("guild"));
        Seat guild = board.Seat("guild");
        board.SetLeader(board.Seat("west"));
        board.AdjustRelation(board.Seat("east"), board.Seat("west"), 2);

        Assert.False(SowDiscord.Playable(board, guild, ["guild"]));
        Assert.True(SowDiscord.Playable(board, guild, ["east"]));
        SowDiscord.Play(board, guild, ["east"]);

        Assert.Equal(0, board.Relation("east", "west"));
        Assert.False(SowDiscord.Playable(board, guild, ["east"]));
    }

    [Fact]
    public void An_arbiter_backs_the_weakest_crown_and_whoever_opposes_an_outsider()
    {
        BoardSetup arbitrated = Arbitrated().Rich("guild");
        BoardSetup setup = arbitrated with
        {
            Seats = [.. arbitrated.Seats, new SeatSetup("cult", "Cult", SeatKind.Power, "cult-holder", null, 4, 4, 4, [CardFamily.Shadow, CardFamily.Arcane], Outsider: true)],
            Personas = new Dictionary<string, Persona>(arbitrated.Personas) { ["cult-holder"] = new(new Dictionary<GoalKind, int>(), Risk: Persona.RiskLimit) },
        };
        Board board = TestBoards.Create(setup);
        Seat guild = board.Seat("guild");

        Assert.Equal(["east"], Balance.Underdogs(board, guild).Select(seat => seat.Id));

        board.AdjustRelation(board.Seat("west"), board.Seat("cult"), Supply.Hostile);
        Assert.Equal(["west", "east"], Balance.Underdogs(board, guild).Select(seat => seat.Id));

        int coin = board.Seat("east").Coin;
        QuietBacking.Play(board, guild, ["east"]);
        Assert.Equal(coin + 3, board.Seat("east").Coin);
        Assert.False(QuietBacking.Playable(board, guild, ["cult"]));
        Assert.False(QuietBacking.Playable(board, board.Seat("west"), ["east"]));
    }

    private static readonly Card SowDiscord = new("sow-discord", "Sow Discord", CardFamily.Arbitration, 0, 1, 2, CardTarget.LeaderFriend, Visibility.Secret,
        [new(CardEffectKind.Estrange, 2)]);

    private static readonly Card QuietBacking = new("quiet-backing", "Quiet Backing", CardFamily.Arbitration, 0, 0, 2, CardTarget.Underdog, Visibility.Secret,
        [new(CardEffectKind.Fund, 3, Resource.Coin)]);

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
                && card.Family == CardFamily.Arbitration);
            if (Balance.ReservesCut(empire)) cut++;
        }
        return (interventions, cut);
    }

    private static bool Paid(TurnTrace turn, Resource resource, string seat = "guild") =>
        turn.Upkeep.Any(change => change.Subject == seat && change.Field == resource.ToString().ToLowerInvariant()
            && int.Parse(change.To, System.Globalization.CultureInfo.InvariantCulture) > int.Parse(change.From, System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>The canon table played from three fixed seeds for fifty years, once for every band here.</summary>
public sealed class FiftyYearsOfSeeds
{
    public IReadOnlyList<GameBalance> Games { get; } = BalanceReport.Run(BoardSetup.Canon(), CardSet.Canon(), [1, 2, 3], years: 50);
}

/// <summary>
/// The bands the canon table's balance is held to over fifty years, settled with Patch on the design thread: rare big
/// changes amid constant froth, with no seat strong enough to break away.
/// </summary>
public sealed class BalanceBandTests(FiftyYearsOfSeeds run) : IClassFixture<FiftyYearsOfSeeds>
{
    [Fact]
    public void No_seat_ever_holds_more_than_two_fifths_of_the_regions()
    {
        int regions = BoardSetup.Canon().Regions.Count;

        Assert.All(run.Games, game => Assert.All(game.PeakRegions.Values, peak => Assert.True(peak * 5 <= regions * 2, $"{peak} of {regions}")));
    }

    [Fact]
    public void Allegiance_changes_hands_once_to_a_few_times_a_year()
    {
        Assert.All(run.Games, game => Assert.InRange((double)game.AllegianceChanges / game.Years, 0.5, 3.0));
    }

    [Fact]
    public void Two_to_eight_wars_break_out_a_decade_and_feuds_simmer_between_them()
    {
        Assert.All(run.Games, game => Assert.InRange(game.Wars * 10.0 / game.Years, 2.0, 8.0));
        Assert.All(run.Games, game => Assert.True(game.Feuds > game.Wars / 2, $"{game.Feuds} feuds, {game.Wars} wars"));
    }

    [Fact]
    public void Every_seat_plays_cards_of_more_than_one_family()
    {
        Assert.All(run.Games, game => Assert.All(game.Families, seat => Assert.True(seat.Value.Count > 1, seat.Key)));
    }

    [Fact]
    public void The_Empire_intervenes_every_few_weeks_and_its_reserves_are_sometimes_cut()
    {
        Assert.All(run.Games, game => Assert.InRange((double)game.Interventions / game.Years, 5.0, 25.0));
        Assert.Contains(run.Games, game => game.ReservesCutWeeks > 0);
    }
}
