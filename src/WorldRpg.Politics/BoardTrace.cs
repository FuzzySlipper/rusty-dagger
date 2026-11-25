using System.Globalization;
using System.Text.Json;

namespace WorldRpg.Politics;

/// <summary>One seat's move in a turn: what it played, its score, the next best moves it passed over, and what changed.</summary>
public sealed record MoveTrace(string Seat, string Card, string? Target, int Score, IReadOnlyList<MoveOption> Alternatives, IReadOnlyList<BoardChange> Changes)
{
    public const int AlternativesShown = 3;
}

/// <summary>
/// One week's record: the upkeep's changes, then each seat's move in table order. Turn 1 is the first Sunday;
/// <see cref="Year"/> and <see cref="Day"/> place it in the Tamrielic year of twelve 30-day months.
/// </summary>
public sealed record TurnTrace(int Turn, IReadOnlyList<BoardChange> Upkeep, IReadOnlyList<MoveTrace> Moves)
{
    public const int DaysPerWeek = 7;
    public const int DaysPerYear = 360;

    public int Year => (Turn - 1) * DaysPerWeek / DaysPerYear + 1;

    public int Day => (Turn - 1) * DaysPerWeek % DaysPerYear + 1;

    /// <summary>The number of weekly turns in <paramref name="years"/> years, rounded up to a whole week.</summary>
    public static int TurnsIn(int years) => (years * DaysPerYear + DaysPerWeek - 1) / DaysPerWeek;
}

/// <summary>Writes turn traces as readable text or as one JSON object per line.</summary>
public static class BoardTraceWriter
{
    /// <summary>One turn as one JSON object ending in a line feed on every system, so a trace hashes the same anywhere.</summary>
    public static void WriteJsonLine(TextWriter writer, TurnTrace turn)
    {
        writer.Write(JsonSerializer.Serialize(turn, PoliticsJson.Default.TurnTrace));
        writer.Write('\n');
    }

    public static void WriteText(TextWriter writer, TurnTrace turn)
    {
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"== Week {turn.Turn} (year {turn.Year}, day {turn.Day}) =="));
        if (turn.Upkeep.Count > 0) writer.WriteLine($"  upkeep: {Changes(turn.Upkeep)}");
        foreach (MoveTrace move in turn.Moves)
        {
            string passed = move.Alternatives.Count == 0 ? "" : $"; over {string.Join(", ", move.Alternatives.Select(Move))}";
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {move.Seat}: {Move(new MoveOption(move.Card, move.Target, move.Score))}{passed}"));
            if (move.Changes.Count > 0) writer.WriteLine($"      {Changes(move.Changes)}");
        }
    }

    private static string Move(MoveOption option) => string.Create(CultureInfo.InvariantCulture,
        $"{option.Card}{(option.Target is null ? "" : " -> " + option.Target)} ({option.Score})");

    private static string Changes(IReadOnlyList<BoardChange> changes) =>
        string.Join("; ", changes.Select(change => $"{change.Subject} {change.Field} {change.From}->{change.To}"));
}
