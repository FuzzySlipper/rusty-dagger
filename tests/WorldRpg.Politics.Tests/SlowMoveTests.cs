using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class SlowMoveTests
{
    /// <summary>East holds the march; West, rich and holding only marches, sends one against it.</summary>
    private static Board Marching()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: "east", marchControl: 4).Rich("west"));
        board.Hold("west", "march-on", "march-on", "march-on");
        board.Hold("east", "envoy", "envoy", "envoy");
        return board;
    }

    [Fact]
    public void A_slow_card_is_paid_when_played_and_arrives_its_weeks_later_from_its_origin()
    {
        Board board = Marching();

        MoveTrace played = BoardTurn.Play(board).Moves.Single(move => move.Seat == "west");

        Assert.Equal(("1-west", "march-on", 3), (played.Id, played.Card, played.Arrives));
        Assert.Equal("west-capital", played.Origin);
        Assert.Equal([new BoardChange("west", "force", "12", "9")], played.Changes);
        Assert.Equal(4, board.Region("march").Control);
        PendingMove pending = Assert.Single(board.Pending);
        Assert.Equal(("1-west", 1, 3), (pending.Id, pending.Played, pending.Arrives));

        BoardTurn.Play(board);
        board.Hold("west", "envoy", "envoy", "envoy");
        TurnTrace arrival = BoardTurn.Play(board);

        ArrivalTrace arrived = Assert.Single(arrival.Arrivals, move => move.Id == "1-west");
        Assert.Equal(MoveOutcome.Resolved, arrived.Outcome);
        Assert.Contains(arrived.Changes, change => change is { Subject: "march", Field: "control" });
        Assert.DoesNotContain(board.Pending, move => move.Id == "1-west");
    }

    [Fact]
    public void A_slow_move_that_no_longer_fits_its_target_fails_on_arrival()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: "east"));
        Seat west = board.Seat("west");
        board.Schedule(new PendingMove("0-west", "west", TestBoards.Card("march-on"), ["march"], "west-capital", 0, 1));
        // West takes the march before the march arrives: a claim on its own region fits no longer.
        board.SetAllegiance(board.Region("march"), west);

        ArrivalTrace arrival = Assert.Single(BoardTurn.Play(board).Arrivals);

        Assert.Equal(MoveOutcome.Failed, arrival.Outcome);
        Assert.Empty(arrival.Changes);
    }

    [Fact]
    public void A_secret_move_shows_as_secret_and_keeps_its_true_seat()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns().WithGuild().Rich("guild"));
        board.Hold("guild", "extort", "extort", "extort");
        board.Hold("west", "envoy", "envoy", "envoy");
        board.Hold("east", "envoy", "envoy", "envoy");

        MoveTrace move = BoardTurn.Play(board).Moves.Single(move => move.Seat == "guild");

        Assert.Equal(("extort", Visibility.Secret), (move.Card, move.Visibility));
        PendingMove pending = Assert.Single(board.Pending);
        Assert.Equal("guild", pending.Seat);
        Assert.Null(pending.Origin);
    }
}
