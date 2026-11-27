using System.Globalization;
using System.Text.Json;

namespace WorldRpg.Politics;

/// <summary>
/// One seat's move in a turn: its id (the turn and seat), the plan it moved under, what it played on which targets, its
/// score, who may see it, the region a card set out from (<see cref="BoardTurn.Origin"/>), the next best moves it passed
/// over and what changed. A slow move names the turn it arrives; its changes are only its payment until then. A move's
/// seat is always the true one, secret or not.
/// </summary>
public sealed record MoveTrace(
    string Id,
    string Seat,
    string? Plan,
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

/// <summary>A slow move arriving: the move it was, the region it set out from, how it ended and what it changed.</summary>
public sealed record ArrivalTrace(
    string Id,
    string Seat,
    string Card,
    IReadOnlyList<string> Targets,
    string? Origin,
    Visibility Visibility,
    MoveOutcome Outcome,
    IReadOnlyList<BoardChange> Changes);

/// <summary>
/// One week's record: the world's nudges applied, the upkeep's changes, the World card turned, the slow moves arriving,
/// each seat's move in the order they moved, the changes of the week's war checks (wars opening, allies joining,
/// weariness and treaties), and the arcs the chronicle named. Turn 1 is the first Sunday; <see cref="Year"/> and <see cref="Day"/> place it in the Tamrielic year of
/// twelve 30-day months.
/// </summary>
public sealed record TurnTrace(
    int Turn,
    IReadOnlyList<NudgeTrace> Nudges,
    IReadOnlyList<BoardChange> Upkeep,
    WorldTrace? World,
    IReadOnlyList<ArrivalTrace> Arrivals,
    IReadOnlyList<MoveTrace> Moves,
    IReadOnlyList<BoardChange> Wars,
    IReadOnlyList<ArcRecord> Chronicle)
{
    public const int DaysPerWeek = 7;
    public const int DaysPerYear = 360;

    /// <summary>The trace as a record a game saves: every field written, nulls included.</summary>
    public string ToRecordJson() => JsonSerializer.Serialize(this, PoliticsRecordJson.Default.TurnTrace);

    /// <summary>A trace a game saved; a malformed one is refused, naming what is wrong (<paramref name="source"/> names where it came from).</summary>
    public static TurnTrace FromRecordJson(string json, string source)
    {
        try
        {
            return JsonSerializer.Deserialize(json, PoliticsRecordJson.Default.TurnTrace) ?? throw new BoardSetupException(source, ["the record holds no turn"]);
        }
        catch (JsonException exception)
        {
            throw new BoardSetupException(source, [exception.Message]);
        }
    }

    public int Year => (Turn - 1) * DaysPerWeek / DaysPerYear + 1;

    public int Day => DayOf(Turn);

    /// <summary>The day of its year that <paramref name="turn"/>'s Sunday falls on, from 1.</summary>
    public static int DayOf(int turn) => (turn - 1) * DaysPerWeek % DaysPerYear + 1;

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

    /// <summary>One week's outputs as one JSON object ending in a line feed (<see cref="BoardOutputs"/>).</summary>
    public static void WriteJsonLine(TextWriter writer, BoardOutputs outputs)
    {
        writer.Write(JsonSerializer.Serialize(outputs, PoliticsRecordJson.Default.BoardOutputs));
        writer.Write('\n');
    }

    public static void WriteText(TextWriter writer, TurnTrace turn)
    {
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"== Week {turn.Turn} (year {turn.Year}, day {turn.Day}) =="));
        foreach (NudgeTrace nudge in turn.Nudges)
        {
            writer.WriteLine($"  nudge {JsonSerializer.Serialize(nudge.Nudge, PoliticsJson.Default.WorldNudge)}: {nudge.Outcome.ToString().ToLowerInvariant()}");
            if (nudge.Changes.Count > 0) writer.WriteLine($"      {Changes(nudge.Changes)}");
        }
        if (turn.Upkeep.Count > 0) writer.WriteLine($"  upkeep: {Changes(turn.Upkeep)}");
        if (turn.World is { } world)
        {
            writer.WriteLine($"  world: {world.Card} -> {world.Region}{(world.Actor is null ? "" : ", by " + world.Actor)}");
            if (world.Changes.Count > 0) writer.WriteLine($"      {Changes(world.Changes)}");
        }
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
            string plan = move.Plan is null ? "" : $" [{move.Plan}]";
            writer.WriteLine($"  {move.Seat}{plan}: {Move(new MoveOption(move.Card, move.Targets, move.Score))}{Secret(move.Visibility)}{slow}{passed}");
            if (move.Changes.Count > 0) writer.WriteLine($"      {Changes(move.Changes)}");
        }
        if (turn.Wars.Count > 0) writer.WriteLine($"  wars: {Changes(turn.Wars)}");
        foreach (ArcRecord arc in turn.Chronicle)
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  chronicle: {arc.Kind.ToString().ToLowerInvariant()} {arc.Key} {arc.Phase.ToString().ToLowerInvariant()} ({string.Join(", ", arc.Subjects)}, since week {arc.Started})"));
    }

    private static string Move(MoveOption option) => string.Create(CultureInfo.InvariantCulture,
        $"{option.Card}{Targets(option.Targets)} ({option.Score})");

    private static string Targets(IReadOnlyList<string> targets) => targets.Count == 0 ? "" : " -> " + string.Join(", ", targets);

    private static string Secret(Visibility visibility) => visibility == Visibility.Secret ? " [secret]" : "";

    private static string Changes(IReadOnlyList<BoardChange> changes) =>
        string.Join("; ", changes.Select(change => $"{change.Subject} {change.Field} {change.From}->{change.To}"));
}
