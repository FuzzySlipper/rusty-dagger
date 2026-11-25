using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class BoardTurnTests
{
    [Fact]
    public void Upkeep_pays_each_seat_from_its_regions_and_influence()
    {
        Board board = Board.Create(TestBoards.TwoCrowns(marchInfluence: new Dictionary<string, int> { ["west"] = 1 }));

        TurnTrace turn = BoardTurn.Play(board);

        // West holds its capital (prosperity 4, control 5) and the march (prosperity 2, control 2) with 3 influence.
        Assert.Contains(new BoardChange("west", "coin", "4", "5"), turn.Upkeep);
        Assert.Contains(new BoardChange("west", "favour", "4", "6"), turn.Upkeep);
        Assert.Contains(new BoardChange("west", "force", "4", "6"), turn.Upkeep);
        // East holds only its capital (prosperity 4, control 5) with 2 influence.
        Assert.Contains(new BoardChange("east", "coin", "4", "5"), turn.Upkeep);
        Assert.Contains(new BoardChange("east", "favour", "4", "5"), turn.Upkeep);
        Assert.Contains(new BoardChange("east", "force", "4", "6"), turn.Upkeep);
    }

    [Fact]
    public void A_seats_grants_fall_due_on_their_schedule()
    {
        Board board = Board.Create(TestBoards.TwoCrowns().WithGuild([new SeatGrant(Resource.Force, 2, 3)]));

        // Before its third week the guild is paid only its income of 1 force; in the third, the grant of 2 follows it.
        Assert.Contains(new BoardChange("guild", "force", "4", "5"), BoardTurn.Play(board).Upkeep);
        board.Turn = 2;
        Seat guild = board.Seat("guild");
        int force = guild.Force;
        Assert.Contains(new BoardChange("guild", "force", $"{force + 1}", $"{force + 3}"), BoardTurn.Play(board).Upkeep);
    }

    [Fact]
    public void Unrest_wears_down_control_and_independent_regions_recover_every_fourth_week()
    {
        Board unrest = Board.Create(TestBoards.TwoCrowns(marchOrder: BoardTurn.UnrestOrder));
        Assert.Contains(new BoardChange("march", "control", "2", "1"), BoardTurn.Play(unrest).Upkeep);

        Board independent = Board.Create(TestBoards.TwoCrowns(marchAllegiance: null, marchControl: 1));
        independent.Turn = BoardTurn.IndependentRecoveryWeeks - 1;
        Assert.Contains(new BoardChange("march", "control", "1", "2"), BoardTurn.Play(independent).Upkeep);
    }

    [Fact]
    public void Every_seat_moves_once_a_week_and_the_first_to_move_turns_with_the_weeks()
    {
        Board board = Board.Create(TestBoards.TwoCrowns());

        Assert.Equal(["west", "east"], BoardTurn.Play(board).Moves.Select(move => move.Seat));
        Assert.Equal(["east", "west"], BoardTurn.Play(board).Moves.Select(move => move.Seat));
    }

    [Fact]
    public void A_move_records_its_score_the_moves_passed_over_and_what_it_changed()
    {
        Board board = Board.Create(TestBoards.TwoCrowns());

        MoveTrace move = BoardTurn.Play(board).Moves[0];

        Assert.InRange(move.Alternatives.Count, 1, MoveTrace.AlternativesShown);
        Assert.All(move.Alternatives, option => Assert.True(option.Score <= move.Score));
        Assert.NotEmpty(move.Changes);
    }

    [Fact]
    public void Over_fifty_canon_years_no_capital_changes_hands()
    {
        Board board = Board.Create(BoardSetup.Canon());

        for (int turn = 0; turn < TurnTrace.TurnsIn(50); turn++) BoardTurn.Play(board);

        Assert.All(board.Seats.Where(seat => seat.Kind == SeatKind.Crown), seat => Assert.Equal(seat.Id, board.Region(seat.Home!).Allegiance));
    }
}
