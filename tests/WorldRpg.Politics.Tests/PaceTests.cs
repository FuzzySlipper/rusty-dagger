using Xunit;

namespace WorldRpg.Politics.Tests;

/// <summary>Fifty canon years, played once for every check here.</summary>
public sealed class FiftyCanonYears
{
    public FiftyCanonYears()
    {
        Board = Board.Create(BoardSetup.Canon(), CardSet.Canon());
        Turns = [.. Enumerable.Range(0, TurnTrace.TurnsIn(50)).Select(_ => BoardTurn.Play(Board))];
    }

    public Board Board { get; }

    public IReadOnlyList<TurnTrace> Turns { get; }

    /// <summary>How many weeks pass, on average, between <paramref name="seat"/>'s card plays.</summary>
    public double WeeksPerPlay(string seat) =>
        (double)Turns.Count / Turns.Sum(turn => turn.Moves.Count(move => move.Seat == seat && move.Card != Gathering.Id));
}

public sealed class PaceTests(FiftyCanonYears run) : IClassFixture<FiftyCanonYears>
{
    private static readonly string[] Gatherers = ["underking", "necromancers", "dark-brotherhood"];

    [Fact]
    public void Across_the_table_five_to_seven_cards_are_played_a_week_in_every_decade()
    {
        foreach (TurnTrace[] decade in run.Turns.Chunk(TurnTrace.TurnsIn(10)))
        {
            double plays = decade.Average(turn => turn.Moves.Count(move => move.Card != Gathering.Id));
            Assert.InRange(plays, 5.0, 7.0);
        }
    }

    /// <summary>
    /// Crowns and guilds pursuing committed plans play a little more often than the greedy chooser did, about every
    /// one and a half weeks at the busiest, so the floor sits just below it.
    /// </summary>
    [Fact]
    public void Crowns_and_guilds_play_every_two_weeks_or_so_and_the_shadow_seats_gather_for_three_or_four()
    {
        Assert.All(run.Board.Seats.Where(seat => !Gatherers.Contains(seat.Id)), seat => Assert.InRange(run.WeeksPerPlay(seat.Id), 1.4, 3.0));
        Assert.All(Gatherers, seat => Assert.InRange(run.WeeksPerPlay(seat), 3.0, 5.0));
    }

    [Fact]
    public void Slow_moves_arrive_and_most_of_them_resolve()
    {
        ArrivalTrace[] arrivals = [.. run.Turns.SelectMany(turn => turn.Arrivals)];

        Assert.NotEmpty(arrivals);
        Assert.True(arrivals.Count(arrival => arrival.Outcome == MoveOutcome.Resolved) > 0.8 * arrivals.Length);
        Assert.Contains(run.Turns.SelectMany(turn => turn.Moves), move => move.Visibility == Visibility.Secret);
    }

    [Fact]
    public void Two_to_five_regions_go_hungry_a_year_and_traded_grain_relieves_some()
    {
        BoardChange[] supply = [.. run.Turns.SelectMany(turn => turn.Upkeep).Where(change => change.Field == "supply")];

        Assert.InRange(supply.Count(change => change.From == "fed") / 50.0, 2.0, 5.0);
        Assert.Contains(supply, change => change.To == "relieved");
    }

    [Fact]
    public void Seats_trade_and_most_deals_run_until_they_lapse_or_break()
    {
        BoardChange[] deals = [.. run.Turns.SelectMany(turn => turn.Upkeep.Concat(turn.Arrivals.SelectMany(arrival => arrival.Changes))
            .Concat(turn.Moves.SelectMany(move => move.Changes))).Where(change => change.Field.StartsWith("deal:", StringComparison.Ordinal))];
        int made = deals.Count(change => change.From == "none" && !change.To.StartsWith("refused", StringComparison.Ordinal));
        int withdrawn = deals.Count(change => change.To.StartsWith("withdrawn", StringComparison.Ordinal));

        Assert.True(made > 50 * 4);
        Assert.Contains(deals, change => change.From == "none" && change.To.Contains("grain:", StringComparison.Ordinal));
        Assert.InRange(withdrawn, 1, made / 5);
    }

    [Fact]
    public void No_capital_changes_hands()
    {
        Assert.All(run.Board.Seats.Where(seat => seat.Kind == SeatKind.Crown), seat => Assert.Equal(seat.Id, run.Board.Region(seat.Home!).Allegiance));
    }
}
