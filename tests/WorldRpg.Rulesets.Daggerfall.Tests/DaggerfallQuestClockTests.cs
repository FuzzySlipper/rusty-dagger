using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallQuestClockTests
{
    [Fact]
    public void Compiler_preserves_duration_forms_range_and_donor_default()
    {
        DaggerfallQuestClockDefinition[] clocks = DaggerfallQuestClockCompiler.Compile(Source(
            Clock(1, "clock _default_"),
            Clock(2, "clock _day_ 2.03:04"),
            Clock(3, "clock _hour_ 03:04"),
            Clock(4, "clock _minute_ 5"),
            Clock(5, "clock _range_ 1 2 flag 9 range 3 5")));

        Assert.Equal((DaggerfallQuestClockCompiler.DefaultMinimumSeconds, DaggerfallQuestClockCompiler.DefaultMaximumSeconds), (clocks[0].MinimumSeconds, clocks[0].MaximumSeconds));
        Assert.Equal((2 * DaggerfallCalendar.SecondsPerDay) + (3 * 3600) + (4 * 60), clocks[1].MinimumSeconds);
        Assert.Equal((3 * 3600) + (4 * 60), clocks[2].MinimumSeconds);
        Assert.Equal(5 * 60, clocks[3].MinimumSeconds);
        Assert.Equal((60L, 120L, 9, 3, 5), (clocks[4].MinimumSeconds, clocks[4].MaximumSeconds, clocks[4].Flag, clocks[4].MinRange, clocks[4].MaxRange));
    }

    [Fact]
    public void Compiler_rejects_clock_option_gaps_and_invalid_daily_windows()
    {
        Assert.Throws<ArgumentException>(() => DaggerfallQuestClockCompiler.Compile(Source(Clock(1, "clock _alarm_ 1 nonsense 2"))));
        Assert.Throws<ArgumentException>(() => DaggerfallQuestTaskCompiler.Compile(Source(Block("task", 2, "_window_ task:", "daily from 24:00 to 01:00"))));
    }

    [Fact]
    public void Travel_derived_donor_forms_are_reported_until_the_quest_place_owner_can_supply_them()
    {
        DaggerfallQuestSourceDefinition source = Source(Clock(1, "clock _traveltime_ 00:00 0 flag 17 range 0 2"));
        DaggerfallQuestClockDefinition derived = Assert.Single(DaggerfallQuestClockCompiler.Compile(source));
        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        DaggerfallQuestClockState dormant = new("traveltime", 0, 0, 17, 0, 2, false, false);
        DaggerfallQuestClockState destination = new("2place", 0, 0, 0, 0, 0, false, false);

        Assert.Contains("travel-derived", DaggerfallQuestClockCompiler.UnsupportedTravelCondition(derived), StringComparison.Ordinal);
        DaggerfallQuestClockCompiler.ValidateSavedState("quest:travel", [derived], [dormant]);
        Assert.Throws<ArgumentException>(() => DaggerfallQuestClockCompiler.ValidateSavedState("quest:travel", [derived], [dormant with { Enabled = true }]));
        DaggerfallQuestRuntimeInstance runtime = Runtime(source, program, [dormant]);
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => runtime.StartClock("traveltime"));
        Assert.Contains("#8051", exception.Message, StringComparison.Ordinal);
        Assert.False(Assert.Single(runtime.Capture().Clocks).Enabled);
        Assert.Contains("destination travel", DaggerfallQuestClockCompiler.UnsupportedStartCondition(destination), StringComparison.Ordinal);
    }

    [Fact]
    public void Published_compiled_quest_clock_lines_have_no_unclaimed_option_text()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));

        foreach (DaggerfallQuestSourceDefinition source in definitions.QuestSources.Quests.Values.Where(source => source.Disposition == DaggerfallQuestDisposition.Compiled))
            _ = DaggerfallQuestClockCompiler.Compile(source);
    }

    [Fact]
    public void Start_stop_and_daily_window_use_the_admitted_calendar_without_resetting_clock_state()
    {
        DaggerfallQuestSourceDefinition source = Source(
            Block("task", 1, "_window_ task:", "daily from 08:00 to 17:00"));
        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        DaggerfallQuestRuntimeInstance runtime = Runtime(source, program,
            [new("alarm", 120, 30, 0, 0, 0, false, false)]);
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));

        Assert.True(runtime.StartClock("alarm"));
        Assert.True(runtime.StopClock("alarm"));
        Assert.True(runtime.StartClock("alarm"));
        Assert.Equal(30, runtime.Capture().Clocks.Single().RemainingSeconds);

        DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start.Advance(9 * 3600, out _));
        Assert.True(runtime.Capture().Tasks.Single().IsSet);
        DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start.Advance(20 * 3600, out _));
        Assert.False(runtime.Capture().Tasks.Single().IsSet);
    }

    [Fact]
    public void Save_state_validates_clock_identity_and_round_trips_remaining_duration()
    {
        DaggerfallQuestSourceDefinition source = Source(Clock(1, "clock _alarm_ 2"));
        DaggerfallQuestClockDefinition[] definitions = DaggerfallQuestClockCompiler.Compile(source);
        DaggerfallQuestClockState saved = new("alarm", 120, 60, 0, 0, 0, true, false);
        DaggerfallQuestClockCompiler.ValidateSavedState("quest:clock", definitions, [saved]);
        Assert.Throws<ArgumentException>(() => DaggerfallQuestClockCompiler.ValidateSavedState("quest:clock", definitions, [saved with { Symbol = "other" }]));
        Assert.Throws<ArgumentException>(() => DaggerfallQuestClockCompiler.ValidateSavedState("quest:clock", definitions, [saved with { RemainingSeconds = 121 }]));

        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        DaggerfallQuestRuntimeInstance restored = new(Runtime(source, program, [saved]).Capture(), program);
        Assert.Equal(saved, Assert.Single(restored.Capture().Clocks));
    }

    [Fact]
    public void Deadline_crossing_during_time_skip_runs_earlier_clock_consequences_before_later_source_tasks()
    {
        DaggerfallQuestSourceDefinition source = Source(
            Clock(1, "clock _early_ 1"),
            Clock(2, "clock _late_ 2"),
            Block("task", 3, "_late_ task:", "end quest"),
            Block("task", 5, "_early_ task:", "start task _result_"),
            Block("variable", 7, "variable _result_"));
        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        DaggerfallQuestRuntimeInstance runtime = Runtime(source, program,
            [new("early", 60, 60, 0, 0, 0, true, false), new("late", 120, 120, 0, 0, 0, true, false)]);
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));

        DaggerfallQuestClockAdvancer.Advance(runtime, program, variables, DaggerfallCalendar.Start, DaggerfallCalendar.Start.Advance(120, out _));

        DaggerfallQuestInstanceSave advanced = runtime.Capture();
        Assert.Equal(DaggerfallQuestLifecycle.Ended, advanced.Lifecycle);
        Assert.True(advanced.Tasks.Single(task => task.Symbol == "result").IsSet);
        Assert.All(advanced.Clocks, clock => Assert.True(clock.Finished));
    }

    private static DaggerfallQuestRuntimeInstance Runtime(DaggerfallQuestSourceDefinition source, DaggerfallQuestTaskProgram program, DaggerfallQuestClockState[] clocks) =>
        new(new("quest:clock", source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], [])
        {
            Tasks = DaggerfallQuestTaskCompiler.InitialState(program),
            Clocks = clocks,
        }, program);

    private static DaggerfallQuestSourceDefinition Source(params DaggerfallQuestBlockDefinition[] blocks) =>
        new("test", string.Empty, "test.txt", DaggerfallQuestDisposition.Compiled, [], blocks, []);

    private static DaggerfallQuestBlockDefinition Clock(int line, string value) => Block("clock", line, value);
    private static DaggerfallQuestBlockDefinition Block(string kind, int line, params string[] lines) => new(kind, line, lines, null);

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("The repository content root was not found.");
    }
}
