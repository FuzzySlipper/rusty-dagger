using System.Text.Json;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Politics;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The political board's runtime diagnostics (B3, #11119): <c>board.state</c>, <c>board.seat</c> and <c>board.turns</c> over the live board.</summary>
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
}
