using System.Globalization;
using System.Text.Json;

namespace WorldRpg.Politics;

/// <summary>
/// One seat's move in a turn: its id (the turn and seat), what it played on which targets, its score, who may see it,
/// the next best moves it passed over and what changed. A slow move names the region it set out from and the turn it
/// arrives; its changes are only its payment until then. A move's seat is always the true one, secret or not.
/// </summary>
public sealed record MoveTrace(
    string Id,
    string Seat,
    string Card,
    IReadOnlyList<string> Targets,
    int Score,
    Visibility Visibility,
    string? Origin,
    int? Arrives,
    IReadOnlyList<MoveOption> Alternatives,
    IReadOnlyList<BoardChange> Changes)
{
    public const int AlternativesShown = 3;
}

/// <summary>How a slow move ended: it resolved, or on arrival it no longer fitted its targets or changed nothing.</summary>
public enum MoveOutcome
{
    Resolved,
    Failed,
}

/// <summary>A slow move arriving: the move it was, how it ended and what it changed.</summary>
public sealed record ArrivalTrace(
    string Id,
    string Seat,
    string Card,
    IReadOnlyList<string> Targets,
    Visibility Visibility,
    MoveOutcome Outcome,
    IReadOnlyList<BoardChange> Changes);

/// <summary>
/// One week's record: the upkeep's changes, the slow moves arriving, then each seat's move in table order. Turn 1 is
/// the first Sunday; <see cref="Year"/> and <see cref="Day"/> place it in the Tamrielic year of twelve 30-day months.
/// </summary>
public sealed record TurnTrace(int Turn, IReadOnlyList<BoardChange> Upkeep, IReadOnlyList<ArrivalTrace> Arrivals, IReadOnlyList<MoveTrace> Moves)
{
    public const int DaysPerWeek = 7;
    public const int DaysPerYear = 360;

    public int Year => (Turn - 1) * DaysPerWeek / DaysPerYear + 1;

    public int Day => (Turn - 1) * DaysPerWeek % DaysPerYear + 1;

    /// <summary>
    /// The number of weekly turns in <paramref name="years"/> Tamrielic years: every Sunday whose day falls inside the
    /// span, so a run ends on the span's last Sunday and the next turn is the first of the following year. A year
    /// holds 51 or 52 Sundays, so this is not 52 times the years: ten years are 515 turns, not 520.
    /// </summary>
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
        foreach (ArrivalTrace arrival in turn.Arrivals)
        {
            writer.WriteLine($"  arrives {arrival.Id}: {arrival.Seat} {arrival.Card}{Targets(arrival.Targets)}{Secret(arrival.Visibility)}: {arrival.Outcome.ToString().ToLowerInvariant()}");
            if (arrival.Changes.Count > 0) writer.WriteLine($"      {Changes(arrival.Changes)}");
        }
        foreach (MoveTrace move in turn.Moves)
        {
            string slow = move.Arrives is null ? "" : string.Create(CultureInfo.InvariantCulture,
                $", from {move.Origin ?? "nowhere"}, arrives week {move.Arrives}");
            string passed = move.Alternatives.Count == 0 ? "" : $"; over {string.Join(", ", move.Alternatives.Select(Move))}";
            writer.WriteLine($"  {move.Seat}: {Move(new MoveOption(move.Card, move.Targets, move.Score))}{Secret(move.Visibility)}{slow}{passed}");
            if (move.Changes.Count > 0) writer.WriteLine($"      {Changes(move.Changes)}");
        }
    }

    private static string Move(MoveOption option) => string.Create(CultureInfo.InvariantCulture,
        $"{option.Card}{Targets(option.Targets)} ({option.Score})");

    private static string Targets(IReadOnlyList<string> targets) => targets.Count == 0 ? "" : " -> " + string.Join(", ", targets);

    private static string Secret(Visibility visibility) => visibility == Visibility.Secret ? " [secret]" : "";

    private static string Changes(IReadOnlyList<BoardChange> changes) =>
        string.Join("; ", changes.Select(change => $"{change.Subject} {change.Field} {change.From}->{change.To}"));
}
