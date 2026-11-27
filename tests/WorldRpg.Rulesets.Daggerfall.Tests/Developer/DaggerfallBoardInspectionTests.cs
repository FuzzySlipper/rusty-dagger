using System.Text.Json;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Politics;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The political board's runtime diagnostics (B3, #11119): <c>board.state</c>, <c>board.seat</c>, <c>board.turns</c> and <c>board.moves</c> over the live board.</summary>
public sealed class DaggerfallBoardInspectionTests
{
    [Fact]
    public void Board_state_shows_the_whole_table_as_the_live_board_holds_it()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;
        IDeveloperBoardCommands commands = session.Commands<IDeveloperBoardCommands>();
        session.AdvanceElapsedTime((board.NextTurnDay - session.Calendar.DayNumber + 7 * 5) * 86400);

        DebugCommandResult result = commands.State();
        Assert.True(result.Succeeded, result.Message);
        using JsonDocument document = JsonDocument.Parse(result.Message);
        JsonElement root = document.RootElement;
        Assert.Equal(board.Board.Turn, root.GetProperty("turn").GetInt32());
        Assert.Equal(board.NextTurnDay, root.GetProperty("nextTurnDay").GetInt64());

        JsonElement[] seats = [.. root.GetProperty("seats").EnumerateArray()];
        Assert.Equal(board.Board.Seats.Select(seat => seat.Id), seats.Select(seat => seat.GetProperty("id").GetString()));
        JsonElement daggerfall = seats.Single(seat => seat.GetProperty("id").GetString() == "daggerfall");
        Assert.Equal("king-gothryd", daggerfall.GetProperty("holder").GetString());
        Assert.Equal(session.OfficeLedger.ResolveBoardSeatPersona("daggerfall").DisplayName, daggerfall.GetProperty("worldHolder").GetString());
        Assert.Equal(board.Board.Seat("daggerfall").Coin, daggerfall.GetProperty("coin").GetInt32());
        Assert.Equal(Prestige.Total(board.Board, board.Board.Seat("daggerfall")), daggerfall.GetProperty("prestige").GetInt32());

        JsonElement[] regions = [.. root.GetProperty("regions").EnumerateArray()];
        Assert.Equal(board.Board.Regions.Count, regions.Length);
        JsonElement home = regions.Single(region => region.GetProperty("id").GetString() == "daggerfall");
        Assert.Equal(board.Board.Region("daggerfall").Supply.ToString(), home.GetProperty("supply").GetString());
        Assert.Equal(board.Town("daggerfall")!.Name, home.GetProperty("town").GetString());
        Assert.Equal(board.Board.Wars.Count, root.GetProperty("wars").GetArrayLength());
        Assert.Equal(board.Board.Agreements.Count, root.GetProperty("agreements").GetArrayLength());
        Assert.Equal(board.Board.Pending.Count, root.GetProperty("pending").GetArrayLength());
        // A slow move under way shows the courier riding with it.
        Assert.All(root.GetProperty("pending").EnumerateArray().Where(move => move.GetProperty("courier").ValueKind != JsonValueKind.Null),
            move => Assert.True(session.CourierDispatches.TryGet(move.GetProperty("id").GetString()!, out _)));
        Assert.Contains(root.GetProperty("arbiters").EnumerateArray(), arbiter => arbiter.GetProperty("seat").GetString() == "empire");
    }

    [Fact]
    public void Board_seat_shows_one_seat_in_depth_and_names_the_seats_when_asked_for_one_the_board_lacks()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;
        IDeveloperBoardCommands commands = session.Commands<IDeveloperBoardCommands>();

        DebugCommandResult result = commands.Seat("daggerfall");
        Assert.True(result.Succeeded, result.Message);
        using JsonDocument document = JsonDocument.Parse(result.Message);
        JsonElement seat = document.RootElement;
        Assert.Equal("king-gothryd", seat.GetProperty("holder").GetString());
        Assert.Equal(JsonValueKind.Null, seat.GetProperty("playsAs").ValueKind);
        Assert.Equal("king-gothryd", seat.GetProperty("world").GetProperty("castId").GetString());
        Assert.Equal(board.Board.Persona(board.Board.Seat("daggerfall")).Risk, seat.GetProperty("persona").GetProperty("risk").GetInt32());
        Assert.Equal(board.Board.Seats.Count - 1, seat.GetProperty("others").GetArrayLength());
        Assert.Equal(board.Board.Deck(board.Board.Seat("daggerfall")).Hand.Count, seat.GetProperty("deck").GetProperty("hand").GetArrayLength());
        Assert.Contains(seat.GetProperty("officesHeld").EnumerateArray(), office => office.GetString() == "daggerfall:watch");

        DebugCommandResult missing = commands.Seat("free-company");
        Assert.False(missing.Succeeded);
        Assert.Contains("mages-guild", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Board_turns_show_the_latest_turns_newest_first_with_each_move_and_its_courier()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;
        IDeveloperBoardCommands commands = session.Commands<IDeveloperBoardCommands>();
        Assert.False(commands.Turns(0).Succeeded);
        Assert.False(commands.Turns(DaggerfallPoliticalBoard.RecentTurnsKept + 1).Succeeded);

        session.AdvanceElapsedTime((board.NextTurnDay - session.Calendar.DayNumber + 7 * 14) * 86400);
        Assert.Equal(DaggerfallPoliticalBoard.RecentTurnsKept, board.RecentTurns.Count);

        DebugCommandResult result = commands.Turns(3);
        Assert.True(result.Succeeded, result.Message);
        using JsonDocument document = JsonDocument.Parse(result.Message);
        JsonElement[] turns = [.. document.RootElement.GetProperty("turns").EnumerateArray()];
        Assert.Equal([board.Board.Turn, board.Board.Turn - 1, board.Board.Turn - 2], turns.Select(turn => turn.GetProperty("turn").GetInt32()));
        Assert.Equal(board.LastTurnDay, turns[0].GetProperty("day").GetInt64());

        TurnTrace latest = board.RecentTurns[^1];
        JsonElement[] moves = [.. turns[0].GetProperty("moves").EnumerateArray()];
        Assert.Equal(latest.Moves.Select(move => move.Id), moves.Select(move => move.GetProperty("id").GetString()));
        foreach ((MoveTrace move, JsonElement shown) in latest.Moves.Zip(moves))
        {
            Assert.Equal(move.Card, shown.GetProperty("card").GetString());
            Assert.Equal(move.Changes.Count, shown.GetProperty("changes").GetArrayLength());
            Assert.Equal(session.CourierDispatches.TryGet(move.Id, out _), shown.GetProperty("courier").ValueKind == JsonValueKind.Object);
        }
        Assert.Equal(latest.Nudges.Count, turns[0].GetProperty("nudges").GetArrayLength());
    }

    [Fact]
    public void Board_moves_log_every_seats_move_newest_first_with_how_each_came_out()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;
        IDeveloperBoardCommands commands = session.Commands<IDeveloperBoardCommands>();
        session.AdvanceElapsedTime((board.NextTurnDay - session.Calendar.DayNumber + 7 * 10) * 86400);

        DebugCommandResult result = commands.Moves();
        Assert.True(result.Succeeded, result.Message);
        using JsonDocument document = JsonDocument.Parse(result.Message);
        JsonElement root = document.RootElement;
        Assert.Equal(board.Board.Seats.Select(seat => seat.Name), root.GetProperty("seats").EnumerateArray().Select(seat => seat.GetProperty("name").GetString()));
        JsonElement[] turns = [.. root.GetProperty("turns").EnumerateArray()];
        Assert.Equal(board.RecentTurns.Reverse().Select(trace => trace.Turn), turns.Select(turn => turn.GetProperty("turn").GetInt32()));

        Dictionary<string, (int Turn, MoveOutcome Outcome)> arrived = board.RecentTurns
            .SelectMany(trace => trace.Arrivals.Select(arrival => (arrival.Id, Value: (trace.Turn, arrival.Outcome))))
            .ToDictionary(pair => pair.Id, pair => pair.Value);
        foreach ((TurnTrace trace, JsonElement shown) in board.RecentTurns.Reverse().Zip(turns))
        {
            Assert.Equal(board.TurnDay(trace.Turn), shown.GetProperty("day").GetInt64());
            JsonElement[] moves = [.. shown.GetProperty("moves").EnumerateArray()];
            Assert.Equal(trace.Moves.Select(move => move.Id), moves.Select(move => move.GetProperty("id").GetString()));
            foreach ((MoveTrace move, JsonElement row) in trace.Moves.Zip(moves))
            {
                Assert.Equal(move.Visibility.ToString(), row.GetProperty("visibility").GetString());
                Assert.Equal(move.Targets, row.GetProperty("targets").EnumerateArray().Select(target => target.GetProperty("id").GetString()));
                JsonElement fate = row.GetProperty("fate");
                string state = fate.GetProperty("state").GetString()!;
                PendingMove? pending = board.Board.Pending.FirstOrDefault(held => held.Id == move.Id);
                if (move.Arrives is null)
                {
                    Assert.Equal(move.Card == Gathering.Id ? "Gathered" : "Landed", state);
                    Assert.Equal(trace.Turn, fate.GetProperty("turn").GetInt32());
                }
                else if (pending is not null)
                {
                    Assert.Equal("UnderWay", state);
                    Assert.Equal(pending.Arrives, fate.GetProperty("turn").GetInt32());
                }
                else if (arrived.TryGetValue(move.Id, out var arrival))
                {
                    Assert.Equal(arrival.Outcome == MoveOutcome.Resolved ? "Arrived" : "Failed", state);
                    Assert.Equal(arrival.Turn, fate.GetProperty("turn").GetInt32());
                }
                // The world's delays to a move are listed with it, at the Sunday each was heard.
                Assert.Equal(
                    board.RecentTurns.SelectMany(heard => heard.Nudges.Where(nudge => nudge.Outcome == NudgeOutcome.Applied && nudge.Nudge is MoveDelayed delayed && delayed.MoveId == move.Id)
                        .Select(_ => heard.Turn)),
                    fate.GetProperty("delays").EnumerateArray().Select(delay => delay.GetProperty("turn").GetInt32()));
            }
            Assert.Equal(trace.Arrivals.Select(arrival => arrival.Id), shown.GetProperty("arrivals").EnumerateArray().Select(arrival => arrival.GetProperty("id").GetString()));
            Assert.Equal(trace.Nudges.Count, shown.GetProperty("nudges").GetArrayLength());
            // The chronicle's arcs show at the turn they peak or settle.
            Assert.Equal(trace.Chronicle.Select(arc => arc.Phase.ToString()),
                shown.GetProperty("arcs").EnumerateArray().Select(arc => arc.GetProperty("phase").GetString()).Where(phase => phase != "Opened"));
        }
        // An arc still open shows where it opened, if that turn is kept.
        foreach (OpenArc arc in board.Board.Arcs.Where(arc => board.RecentTurns.Any(trace => trace.Turn == arc.Started)))
            Assert.Contains(turns.Single(turn => turn.GetProperty("turn").GetInt32() == arc.Started).GetProperty("arcs").EnumerateArray(),
                shown => shown.GetProperty("key").GetString() == arc.Key && shown.GetProperty("phase").GetString() == "Opened");
    }

    [Fact]
    public void Board_moves_show_a_move_whose_courier_was_seized_as_intercepted_with_the_nudge_that_told_the_board()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;
        IDeveloperBoardCommands commands = session.Commands<IDeveloperBoardCommands>();
        long day = session.Calendar.DayNumber;
        PendingMove? ridden = null;
        while (ridden is null)
        {
            session.AdvanceElapsedTime((board.NextTurnDay - day) * 86400);
            day = board.NextTurnDay - DaggerfallPoliticalBoard.DaysPerWeek;
            ridden = board.Board.Pending.FirstOrDefault(move => move.Played == board.Board.Turn && move.Arrives > board.Board.Turn + 1
                && session.CourierDispatches.TryGet(move.Id, out DaggerfallCourierDispatchSave? staged) && staged!.Status == DaggerfallDispatchStatus.InTransit);
            Assert.True(board.Board.Turn < 26, "no slow move with a courier still on the road in half a year");
        }
        Assert.True(session.CourierDispatches.TryGet(ridden.Id, out DaggerfallCourierDispatchSave? dispatch));

        using (JsonDocument before = JsonDocument.Parse(commands.Moves().Message))
        {
            JsonElement fate = MoveRow(before, ridden.Id).GetProperty("fate");
            Assert.Equal("UnderWay", fate.GetProperty("state").GetString());
            Assert.Equal(nameof(DaggerfallDispatchStatus.InTransit), fate.GetProperty("courier").GetProperty("status").GetString());
        }

        Assert.True(session.CourierDispatches.InterceptDispatch(ridden.Id, dispatch!.DispatchedAtMinute + 1, checked((int)day)).Accepted);
        session.AdvanceElapsedTime(7 * 86400);

        using JsonDocument after = JsonDocument.Parse(commands.Moves().Message);
        JsonElement intercepted = MoveRow(after, ridden.Id).GetProperty("fate");
        Assert.Equal("Intercepted", intercepted.GetProperty("state").GetString());
        Assert.Equal(board.Board.Turn, intercepted.GetProperty("turn").GetInt32());
        JsonElement latest = after.RootElement.GetProperty("turns")[0];
        Assert.Contains(latest.GetProperty("nudges").EnumerateArray(), nudge => nudge.GetProperty("move").GetString() == ridden.Id
            && nudge.GetProperty("seat").GetString() == ridden.Seat && nudge.GetProperty("kind").GetString() == nameof(MoveIntercepted));
    }

    private static JsonElement MoveRow(JsonDocument moves, string id) =>
        moves.RootElement.GetProperty("turns").EnumerateArray().SelectMany(turn => turn.GetProperty("moves").EnumerateArray())
            .Single(move => move.GetProperty("id").GetString() == id);
}
