using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class BoardTurnTests
{
    [Fact]
    public void Upkeep_pays_each_seat_from_its_regions_and_influence()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchInfluence: new Dictionary<string, int> { ["west"] = 1 }));

        TurnTrace turn = BoardTurn.Play(board);

        // West holds its capital (prosperity 4, control 5) and the march (prosperity 2, control 2) with 3 influence.
        Assert.Contains(new BoardChange("west", "coin", "4", "5"), turn.Upkeep);
        Assert.Contains(new BoardChange("west", "favour", "4", "5"), turn.Upkeep);
        Assert.Contains(new BoardChange("west", "force", "4", "5"), turn.Upkeep);
        // East holds only its capital (prosperity 4, control 5) with 2 influence, too few for favour.
        Assert.Contains(new BoardChange("east", "coin", "4", "5"), turn.Upkeep);
        Assert.DoesNotContain(turn.Upkeep, change => change is { Subject: "east", Field: "favour" });
        Assert.Contains(new BoardChange("east", "force", "4", "5"), turn.Upkeep);
    }

    [Fact]
    public void A_seats_grants_fall_due_on_their_schedule()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns().WithGuild([new SeatGrant(Resource.Force, 2, 3)]));

        // A power holds no region, so it has no force income: before its third week the grant is all it gets.
        Assert.DoesNotContain(BoardTurn.Play(board).Upkeep, change => change is { Subject: "guild", Field: "force" });
        board.Turn = 2;
        Seat guild = board.Seat("guild");
        int force = guild.Force;
        Assert.Contains(new BoardChange("guild", "force", $"{force}", $"{force + 2}"), BoardTurn.Play(board).Upkeep);
    }

    [Fact]
    public void Offices_pay_their_holders_every_eighth_week()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchOffices: new Dictionary<Office, string> { [Office.Watch] = "guild", [Office.Factor] = "guild" }).WithGuild());
        Seat guild = board.Seat("guild");
        board.Turn = BoardTurn.OfficeWeeks - 1;
        (int coin, int force) = (guild.Coin, guild.Force);

        IReadOnlyList<BoardChange> upkeep = BoardTurn.Play(board).Upkeep;

        Assert.Contains(new BoardChange("guild", "force", $"{force}", $"{force + 1}"), upkeep);
        Assert.Contains(new BoardChange("guild", "coin", $"{coin}", $"{coin + 1}"), upkeep);
    }

    [Fact]
    public void Unrest_wears_down_control_and_independent_regions_recover_every_fourth_week()
    {
        Board unrest = TestBoards.Create(TestBoards.TwoCrowns(marchOrder: BoardTurn.UnrestOrder));
        Assert.Contains(new BoardChange("march", "control", "2", "1"), BoardTurn.Play(unrest).Upkeep);

        Board independent = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: null, marchControl: 1));
        independent.Turn = BoardTurn.IndependentRecoveryWeeks - 1;
        Assert.Contains(new BoardChange("march", "control", "1", "2"), BoardTurn.Play(independent).Upkeep);
    }

    [Fact]
    public void Each_season_untended_tracks_and_relations_drift_back_and_fed_land_recovers()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(relation: -2));
        board.Turn = BoardTurn.SeasonWeeks - 1;

        IReadOnlyList<BoardChange> upkeep = BoardTurn.Play(board).Upkeep;

        Assert.Contains(new BoardChange("west-capital", "control", "5", "4"), upkeep);
        Assert.Contains(new BoardChange("west-capital", "prosperity", "4", "3"), upkeep);
        Assert.Contains(new BoardChange("west-capital", "order", "4", "3"), upkeep);
        // The march, fed and below the settled track, recovers a point of prosperity and keeps its other tracks.
        Assert.Equal([new BoardChange("march", "prosperity", "2", "3")], upkeep.Where(change => change.Subject == "march"));
        Assert.Contains(new BoardChange("east", "relation:west", "-2", "-1"), upkeep);
    }

    [Fact]
    public void Every_seat_moves_once_a_week_and_between_equals_the_first_to_move_turns_with_the_weeks()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: null));
        Assert.Equal(Prestige.Total(board, board.Seat("west")), Prestige.Total(board, board.Seat("east")));

        Assert.Equal(["west", "east"], BoardTurn.Play(board).Moves.Select(move => move.Seat));
        Assert.Equal(["east", "west"], OrderOnTurn(2));
    }

    /// <summary>The order the seats of an untouched, even board would move in on <paramref name="turn"/>.</summary>
    private static IEnumerable<string> OrderOnTurn(int turn)
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: null));
        board.Turn = turn;
        return Balance.Order(board).Select(seat => seat.Id);
    }

    [Fact]
    public void The_trailing_seat_moves_first()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());

        Assert.True(Prestige.Total(board, board.Seat("east")) < Prestige.Total(board, board.Seat("west")));
        Assert.Equal(["east", "west"], BoardTurn.Play(board).Moves.Select(move => move.Seat));
    }

    [Fact]
    public void A_move_records_its_id_its_score_the_moves_passed_over_and_what_it_changed()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        board.Hold("west", "trade-charter", "keep-the-peace", "envoy");

        MoveTrace move = BoardTurn.Play(board).Moves.Single(move => move.Seat == "west");

        Assert.Equal("1-west", move.Id);
        Assert.InRange(move.Alternatives.Count, 1, MoveTrace.AlternativesShown);
        Assert.All(move.Alternatives, option => Assert.True(option.Score <= move.Score));
        Assert.NotEmpty(move.Changes);
    }
}
