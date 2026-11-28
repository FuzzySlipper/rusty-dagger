using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Timing budgets, measured with nothing else running beside them (a parallel class's load would skew one week).</summary>
[Collection(TimingBudgets.Name)]
public sealed class BoardTurnBudgetTests
{
    /// <summary>
    /// #11190: no week's rest, its Sunday turn included, stands out from the year's. The weeks the board changed an office
    /// once took 0.6 to 1.3 s against a 50 ms median, about 25 times, re-reading every location's buildings to find an
    /// exile, a cast home or a deputy. On den-agents the worst week is now about 160 ms (the first, routing its couriers),
    /// about 3 times the median. The bound is a multiple of the median so a loaded machine slows both alike.
    /// </summary>
    [Fact]
    public void A_year_of_weekly_rests_keeps_every_week_within_its_budget()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;
        session.AdvanceElapsedTime((board.NextTurnDay - (long)session.Calendar.DayNumber) * 86400 + 3600);
        List<(int Week, long Milliseconds)> weeks = [];
        for (int week = 0; week < 52; week++)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            session.AdvanceElapsedTime(7 * 86400);
            weeks.Add((week, (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        }
        (int worstWeek, long worst) = weeks.MaxBy(entry => entry.Milliseconds);
        long median = weeks.Select(entry => entry.Milliseconds).Order().ElementAt(weeks.Count / 2);
        Assert.True(worst < 8 * Math.Max(median, 10), $"Week {worstWeek} of the year took {worst} ms against a median of {median} ms.");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingBudgets
{
    public const string Name = "Timing budgets";
}
