using System.Text.RegularExpressions;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Durable clock state. Flag and range are retained source meaning; no undocumented flag behavior is inferred.</summary>
internal sealed record DaggerfallQuestClockState(string Symbol, long StartingSeconds, long RemainingSeconds, int Flag, int MinRange, int MaxRange, bool Enabled, bool Finished);
internal sealed record DaggerfallQuestClockDefinition(string Symbol, long MinimumSeconds, long MaximumSeconds, int Flag, int MinRange, int MaxRange);

internal static class DaggerfallQuestClockCompiler
{
    private static readonly Regex Declaration = new("^clock\\s+(?<symbol>[a-zA-Z0-9_.-]+)(?<options>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Option = new("\\G(?:flag\\s+(?<flag>\\d+)|range\\s+(?<min>\\d+)\\s+(?<max>\\d+)|(?<day>\\d+)\\.(?<dayHour>\\d+):(?<dayMinute>\\d+)|(?<hour>\\d+):(?<minute>\\d+)|(?<bare>\\d+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // DFU preserves this 1-minute to 7-day fallback while documenting uncertainty about the
    // original executable's exact range. Keep that donor behavior explicit; it is not a claim
    // that the original range has been independently recovered.
    internal const long DefaultMinimumSeconds = DaggerfallCalendar.SecondsPerMinute;
    internal const long DefaultMaximumSeconds = DaggerfallCalendar.SecondsPerDay * DaggerfallCalendar.DaysPerWeek;

    internal static DaggerfallQuestClockDefinition[] Compile(DaggerfallQuestSourceDefinition source)
    {
        List<DaggerfallQuestClockDefinition> clocks = [];
        foreach (DaggerfallQuestBlockDefinition block in source.Blocks.Where(block => block.Kind.Equals("clock", StringComparison.OrdinalIgnoreCase)))
        {
            if (block.Lines.Count != 1 || Declaration.Match(block.Lines[0].Trim()) is not { Success: true } match)
                throw new ArgumentException($"Quest clock at source line {block.FirstLine} is malformed.");
            List<long> times = [];
            int flag = 0, min = 0, max = 0;
            string options = match.Groups["options"].Value;
            for (int position = 0; position < options.Length;)
            {
                if (char.IsWhiteSpace(options[position])) { position++; continue; }
                Match option = Option.Match(options, position);
                if (!option.Success)
                    throw new ArgumentException($"Quest clock at source line {block.FirstLine} has malformed option text beginning '{options[position..]}'.");
                position += option.Length;
                if (option.Groups["flag"].Success) { flag = int.Parse(option.Groups["flag"].Value, System.Globalization.CultureInfo.InvariantCulture); continue; }
                if (option.Groups["min"].Success) { min = int.Parse(option.Groups["min"].Value, System.Globalization.CultureInfo.InvariantCulture); max = int.Parse(option.Groups["max"].Value, System.Globalization.CultureInfo.InvariantCulture); continue; }
                long seconds = Seconds(option, block.FirstLine);
                times.Add(seconds);
            }
            if (times.Count > 2) throw new ArgumentException($"Quest clock at source line {block.FirstLine} names more than two durations.");
            long minimum = times.Count == 0 ? DefaultMinimumSeconds : times[0];
            long maximum = times.Count == 0 ? DefaultMaximumSeconds : times.Count == 2 && times[1] > minimum ? times[1] : minimum;
            clocks.Add(new(DaggerfallQuestInstanceSave.Canonical(match.Groups["symbol"].Value, "quest clock"), minimum, maximum, flag, min, max));
        }
        return [.. clocks];
    }

    internal static string? UnsupportedTravelCondition(DaggerfallQuestClockDefinition clock)
    {
        if ((clock.Flag & 16) != 0)
            return $"flag {clock.Flag} requires the donor's travel-derived duration";
        if ((clock.Flag & 1) != 0 && clock.MaxRange > 0 && clock.MinimumSeconds == 0)
            return $"flag {clock.Flag} with range {clock.MinRange} {clock.MaxRange} requires the donor's travel-derived fallback duration";
        return null;
    }

    internal static string? UnsupportedStartCondition(DaggerfallQuestClockState clock)
    {
        if (clock.Symbol.StartsWith("2", StringComparison.Ordinal) && clock.StartingSeconds == 0)
            return "the donor's _2..._ destination travel duration";
        return UnsupportedTravelCondition(new(clock.Symbol, clock.StartingSeconds, clock.StartingSeconds, clock.Flag, clock.MinRange, clock.MaxRange));
    }

    internal static void ValidateSavedState(string owner, IReadOnlyList<DaggerfallQuestClockDefinition> definitions,
        IReadOnlyList<DaggerfallQuestClockState> clocks)
    {
        if (clocks.Count != definitions.Count)
            throw new ArgumentException($"Quest instance '{owner}' clock state count is incompatible with its definition.");
        for (int index = 0; index < definitions.Count; index++)
        {
            DaggerfallQuestClockState clock = clocks[index] ?? throw new ArgumentException($"Quest instance '{owner}' has null clock state.");
            DaggerfallQuestClockDefinition definition = definitions[index];
            if (!string.Equals(clock.Symbol, definition.Symbol, StringComparison.Ordinal)
                || clock.Flag != definition.Flag || clock.MinRange != definition.MinRange || clock.MaxRange != definition.MaxRange)
                throw new ArgumentException($"Quest instance '{owner}' clock state at position {index} does not match its definition.");
            if (UnsupportedTravelCondition(definition) is not null)
            {
                if (clock.StartingSeconds != 0 || clock.RemainingSeconds != 0 || clock.Enabled || clock.Finished)
                    throw new ArgumentException($"Quest instance '{owner}' clock '{clock.Symbol}' requires #8051 travel policy before it can leave its dormant state.");
                continue;
            }
            if (clock.StartingSeconds < definition.MinimumSeconds || clock.StartingSeconds > definition.MaximumSeconds
                || clock.RemainingSeconds < 0 || clock.RemainingSeconds > clock.StartingSeconds
                || (clock.Finished && (clock.Enabled || clock.RemainingSeconds != 0))
                || (!clock.Finished && clock.RemainingSeconds == 0))
                throw new ArgumentException($"Quest instance '{owner}' clock '{clock.Symbol}' has incompatible remaining state.");
        }
    }

    private static long Seconds(Match option, int sourceLine)
    {
        if (option.Groups["bare"].Success)
            return checked(long.Parse(option.Groups["bare"].Value, System.Globalization.CultureInfo.InvariantCulture) * DaggerfallCalendar.SecondsPerMinute);
        int hour = int.Parse(option.Groups["day"].Success ? option.Groups["dayHour"].Value : option.Groups["hour"].Value, System.Globalization.CultureInfo.InvariantCulture);
        int minute = int.Parse(option.Groups["day"].Success ? option.Groups["dayMinute"].Value : option.Groups["minute"].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (hour >= DaggerfallCalendar.HoursPerDay || minute >= DaggerfallCalendar.MinutesPerHour)
            throw new ArgumentException($"Quest clock at source line {sourceLine} has an invalid hour or minute duration.");
        if (option.Groups["day"].Success)
            return checked((long.Parse(option.Groups["day"].Value, System.Globalization.CultureInfo.InvariantCulture) * DaggerfallCalendar.SecondsPerDay) + (hour * 3600L) + (minute * 60L));
        return checked((hour * 3600L) + (minute * 60L));
    }
}

/// <summary>Consumes an admitted calendar interval without owning a timer or update loop.</summary>
internal static class DaggerfallQuestClockAdvancer
{
    internal static void Advance(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program,
        DaggerfallVariableStore variables, DaggerfallCalendar before, DaggerfallCalendar after)
    {
        long remainingInterval = after.ToAbsoluteSeconds() - before.ToAbsoluteSeconds();
        if (remainingInterval <= 0 || instance.Lifecycle != DaggerfallQuestLifecycle.Active) return;

        long elapsed = 0;
        while (instance.Lifecycle == DaggerfallQuestLifecycle.Active && TryNextDeadline(instance.Clocks, remainingInterval, out int deadline, out long seconds))
        {
            Consume(instance, seconds);
            remainingInterval -= seconds;
            elapsed += seconds;
            DaggerfallQuestClockState clock = instance.Clocks[deadline];
            instance.Clocks[deadline] = clock with { RemainingSeconds = 0, Enabled = false, Finished = true };
            DaggerfallQuestTaskRunner.TriggerClockDeadline(instance, program, variables, clock.Symbol);
            DaggerfallQuestTaskRunner.Advance(instance, program, variables, before.Advance(elapsed, out _));
        }

        if (remainingInterval > 0 && instance.Lifecycle == DaggerfallQuestLifecycle.Active)
            Consume(instance, remainingInterval);
    }

    private static bool TryNextDeadline(IReadOnlyList<DaggerfallQuestClockState> clocks, long interval, out int index, out long seconds)
    {
        index = -1;
        seconds = long.MaxValue;
        for (int candidate = 0; candidate < clocks.Count; candidate++)
        {
            DaggerfallQuestClockState clock = clocks[candidate];
            if (!clock.Enabled || clock.Finished || clock.RemainingSeconds > interval) continue;
            if (clock.RemainingSeconds < seconds)
            {
                index = candidate;
                seconds = clock.RemainingSeconds;
            }
        }
        return index >= 0;
    }

    private static void Consume(DaggerfallQuestRuntimeInstance instance, long seconds)
    {
        if (seconds == 0) return;
        for (int index = 0; index < instance.Clocks.Length; index++)
        {
            DaggerfallQuestClockState clock = instance.Clocks[index];
            if (clock.Enabled && !clock.Finished)
                instance.Clocks[index] = clock with { RemainingSeconds = checked(clock.RemainingSeconds - seconds) };
        }
    }
}
