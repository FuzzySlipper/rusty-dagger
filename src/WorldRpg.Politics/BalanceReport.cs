using System.Globalization;

namespace WorldRpg.Politics;

/// <summary>
/// One game's balance, played from <see cref="Seed"/> for <see cref="Years"/>: each seat's prestige at the end, the most
/// regions it held at once, the weeks it led the table and the card families it played; how often allegiance changed
/// and wars and feuds opened; the weeks with a war in force; how many conditions of each kind were laid; and, for the
/// arbiter, the weeks its reserves were cut off and the countermoves it made (cards played on a war, an independent
/// region or the leader).
/// </summary>
public sealed record GameBalance(
    ulong Seed,
    int Years,
    IReadOnlyDictionary<string, int> Prestige,
    IReadOnlyDictionary<string, int> PeakRegions,
    IReadOnlyDictionary<string, int> WeeksLeading,
    IReadOnlyDictionary<string, IReadOnlySet<CardFamily>> Families,
    int AllegianceChanges,
    int Wars,
    int Feuds,
    int WeeksAtWar,
    IReadOnlyDictionary<ConditionKind, int> Conditions,
    int ReservesCutWeeks,
    int Interventions)
{
    public int Weeks => TurnTrace.TurnsIn(Years);
}

/// <summary>Plays many seeds of a table and reports its balance (<c>politics balance</c>).</summary>
public static class BalanceReport
{
    /// <summary>Plays each of <paramref name="seeds"/> for <paramref name="years"/> on its own board, in parallel, and measures it.</summary>
    public static IReadOnlyList<GameBalance> Run(BoardSetup setup, CardSet cards, IReadOnlyList<ulong> seeds, int years)
    {
        GameBalance[] games = new GameBalance[seeds.Count];
        Parallel.For(0, seeds.Count, index => games[index] = Play(setup, cards, seeds[index], years));
        return games;
    }

    /// <summary>Plays one game and measures it.</summary>
    public static GameBalance Play(BoardSetup setup, CardSet cards, ulong seed, int years)
    {
        Board board = Board.Create(setup, cards, seed);
        Dictionary<string, Card> byId = cards.Cards.ToDictionary(card => card.Id, StringComparer.Ordinal);
        Dictionary<string, int> peak = board.Seats.ToDictionary(seat => seat.Id, _ => 0, StringComparer.Ordinal);
        Dictionary<string, int> leading = board.Seats.ToDictionary(seat => seat.Id, _ => 0, StringComparer.Ordinal);
        Dictionary<string, HashSet<CardFamily>> families = board.Seats.ToDictionary(seat => seat.Id, _ => new HashSet<CardFamily>(), StringComparer.Ordinal);
        Dictionary<ConditionKind, int> conditions = Enum.GetValues<ConditionKind>().ToDictionary(kind => kind, _ => 0);
        Seat? arbiter = board.Seats.FirstOrDefault(seat => seat.Arbiter);
        int allegiance = 0, wars = 0, feuds = 0, atWar = 0, cut = 0, interventions = 0;
        for (int turn = 0; turn < TurnTrace.TurnsIn(years); turn++)
        {
            TurnTrace trace = BoardTurn.Play(board);
            if (board.Leader is { } leader) leading[leader]++;
            foreach (MoveTrace move in trace.Moves)
            {
                if (!byId.TryGetValue(move.Card, out Card? card)) continue;
                families[move.Seat].Add(card.Family);
                if (move.Seat == arbiter?.Id && card.Target is CardTarget.War or CardTarget.IndependentRegion or CardTarget.Leader) interventions++;
            }
            foreach (BoardChange change in Changes(trace))
            {
                if (change.Field == "allegiance") allegiance++;
                else if (change.From == "none" && change.Field.StartsWith("war:", StringComparison.Ordinal)) wars++;
                else if (change.From == "none" && change.Field.StartsWith("feud:", StringComparison.Ordinal)) feuds++;
                else if (change.From == "none" && change.Field.StartsWith("condition:", StringComparison.Ordinal))
                    conditions[Enum.Parse<ConditionKind>(change.Field.Split(':')[1], ignoreCase: true)]++;
            }
            foreach (Seat seat in board.Seats) peak[seat.Id] = Math.Max(peak[seat.Id], board.Regions.Count(region => region.Allegiance == seat.Id));
            if (board.Wars.Count > 0) atWar++;
            if (arbiter is not null && Balance.ReservesCut(arbiter)) cut++;
        }
        return new GameBalance(seed, years,
            board.Seats.ToDictionary(seat => seat.Id, seat => Prestige.Total(board, seat), StringComparer.Ordinal),
            peak, leading, families.ToDictionary(entry => entry.Key, entry => (IReadOnlySet<CardFamily>)entry.Value, StringComparer.Ordinal),
            allegiance, wars, feuds, atWar, conditions, cut, interventions);
    }

    /// <summary>Writes the report: per seat, then across the table, each as the spread over the games.</summary>
    public static void Write(TextWriter writer, BoardSetup setup, IReadOnlyList<GameBalance> games)
    {
        int years = games[0].Years;
        writer.WriteLine(Format($"politics balance: {games.Count} seeds x {years} years"));
        writer.WriteLine(Format($"{"seat",-18} {"prestige min/median/max",24} {"peak regions",13} {"weeks leading",14} {"families",9}"));
        foreach (SeatSetup seat in setup.Seats)
        {
            int[] prestige = [.. games.Select(game => game.Prestige[seat.Id]).Order()];
            writer.WriteLine(Format($"{seat.Id,-18} {$"{prestige[0]}/{prestige[prestige.Length / 2]}/{prestige[^1]}",24} {games.Max(game => game.PeakRegions[seat.Id]),13} {games.Average(game => (double)game.WeeksLeading[seat.Id] / game.Weeks),14:P0} {games.Min(game => game.Families[seat.Id].Count),9}"));
        }
        writer.WriteLine(Spread("allegiance changes a year", games, game => (double)game.AllegianceChanges / years));
        writer.WriteLine(Spread("wars a decade", games, game => game.Wars * 10.0 / years));
        writer.WriteLine(Spread("feuds a decade", games, game => game.Feuds * 10.0 / years));
        writer.WriteLine(Spread("weeks at war", games, game => 100.0 * game.WeeksAtWar / game.Weeks, "%"));
        writer.WriteLine("conditions laid a year: " + string.Join(", ", Enum.GetValues<ConditionKind>()
            .Select(kind => Format($"{kind} {games.Average(game => (double)game.Conditions[kind] / years):0.#}"))));
        if (setup.Seats.FirstOrDefault(seat => seat.Arbiter) is { } arbiter)
        {
            writer.WriteLine(Spread($"{arbiter.Id}: reserves cut", games, game => 100.0 * game.ReservesCutWeeks / game.Weeks, "% of weeks"));
            writer.WriteLine(Spread($"{arbiter.Id}: interventions a year", games, game => (double)game.Interventions / years));
        }
    }

    private static string Spread(string label, IReadOnlyList<GameBalance> games, Func<GameBalance, double> measure, string unit = "")
    {
        double[] values = [.. games.Select(measure)];
        return Format($"{label}: {values.Min():0.##}–{values.Max():0.##}{unit} (mean {values.Average():0.##}{unit})");
    }

    private static IEnumerable<BoardChange> Changes(TurnTrace trace) =>
        trace.Upkeep.Concat(trace.World?.Changes ?? []).Concat(trace.Arrivals.SelectMany(arrival => arrival.Changes))
            .Concat(trace.Moves.SelectMany(move => move.Changes)).Concat(trace.Wars);

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
