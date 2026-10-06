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
    public void Travel_derived_donor_forms_validate_sampled_state_and_destination_start()
    {
        DaggerfallQuestSourceDefinition source = Source(Clock(1, "clock _traveltime_ 00:00 0 flag 17 range 0 2"));
        DaggerfallQuestClockDefinition derived = Assert.Single(DaggerfallQuestClockCompiler.Compile(source));
        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        DaggerfallQuestClockState sampled = new("traveltime", 216000, 216000, 17, 0, 2, false, false);
        DaggerfallQuestClockState destination = new("2place", 0, 0, 0, 0, 0, false, false);

        Assert.True(DaggerfallQuestClockCompiler.UsesTravelDuration(derived));
        DaggerfallQuestClockCompiler.ValidateSavedState("quest:travel", [derived], [sampled]);
        Assert.Throws<ArgumentException>(() => DaggerfallQuestClockCompiler.ValidateSavedState("quest:travel", [derived], [sampled with { RemainingSeconds = 216001 }]));
        DaggerfallQuestRuntimeInstance runtime = Runtime(source, program, [sampled]);
        Assert.True(runtime.StartClock("traveltime"));
        Assert.Equal(sampled.StartingSeconds, Assert.Single(runtime.Capture().Clocks).StartingSeconds);
        DaggerfallQuestSourceDefinition destinationSource = Source(Clock(1, "clock _2place_ 00:00"));
        DaggerfallQuestClockDefinition destinationDefinition = Assert.Single(DaggerfallQuestClockCompiler.Compile(destinationSource));
        DaggerfallQuestRuntimeInstance destinationRuntime = Runtime(destinationSource,
            DaggerfallQuestTaskCompiler.Compile(destinationSource), [destination]);
        destinationRuntime.TravelClockSeconds = (_, symbol) => symbol == "place" ? 86400 : throw new Exception("wrong place");
        Assert.True(destinationRuntime.StartClock("2place"));
        Assert.Equal(86400, Assert.Single(destinationRuntime.Capture().Clocks).RemainingSeconds);
        DaggerfallQuestClockCompiler.ValidateSavedState("quest:destination", [destinationDefinition], destinationRuntime.Capture().Clocks);
        DaggerfallQuestRuntimeInstance restored = new(destinationRuntime.Capture(), program);
        Assert.Equal(86400, Assert.Single(restored.Capture().Clocks).RemainingSeconds);
    }

    [Fact]
    public void Published_compiled_quest_clock_lines_have_no_unclaimed_option_text()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;

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

        DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start.Advance(9 * 3600, out _), DaggerfallQuestTaskRuntimeTests.Messages(source), new DaggerfallQuestTaskRuntimeTests.LifecycleFake());
        Assert.True(runtime.Capture().Tasks.Single().IsSet);
        DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start.Advance(20 * 3600, out _), DaggerfallQuestTaskRuntimeTests.Messages(source), new DaggerfallQuestTaskRuntimeTests.LifecycleFake());
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

        DaggerfallQuestClockAdvancer.Advance(runtime, program, variables, DaggerfallCalendar.Start, DaggerfallCalendar.Start.Advance(120, out _),
            DaggerfallQuestTaskRuntimeTests.Messages(source), new DaggerfallQuestTaskRuntimeTests.LifecycleFake());

        DaggerfallQuestInstanceSave advanced = runtime.Capture();
        Assert.Equal(DaggerfallQuestLifecycle.Active, advanced.Lifecycle);
        Assert.Equal(2, advanced.PendingEndPasses);
        Assert.True(advanced.Tasks.Single(task => task.Symbol == "result").IsSet);
        Assert.All(advanced.Clocks, clock => Assert.True(clock.Finished));
    }

    [Fact]
    public void A_prompt_opened_by_an_earlier_deadline_holds_a_later_deadline_task_in_the_same_interval()
    {
        DaggerfallQuestSourceDefinition source = new("test", string.Empty, "prompted.txt", DaggerfallQuestDisposition.Compiled,
            [new(1010, 1, ["Will you help?"])],
            [Clock(1, "clock _early_ 1"),
             Clock(2, "clock _late_ 2"),
             // The later deadline's task precedes the prompting task, so a task pass would reach it.
             Block("task", 3, "_late_ task:", "start task _result_"),
             Block("task", 5, "_early_ task:", "prompt 1010 yes _yes_ no _no_"),
             Block("variable", 7, "variable _yes_"),
             Block("variable", 8, "variable _no_"),
             Block("variable", 9, "variable _result_")], []);
        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        DaggerfallQuestRuntimeInstance runtime = Runtime(source, program,
            [new("early", 60, 60, 0, 0, 0, true, false), new("late", 120, 120, 0, 0, 0, true, false)]);
        DaggerfallQuestMessages messages = DaggerfallQuestTaskRuntimeTests.Messages(source);
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));

        DaggerfallQuestClockAdvancer.Advance(runtime, program, variables, DaggerfallCalendar.Start, DaggerfallCalendar.Start.Advance(120, out _),
            messages, new DaggerfallQuestTaskRuntimeTests.LifecycleFake());

        // Both clocks are consumed and the later one's task is triggered, but its action waits for
        // the prompt the earlier deadline opened.
        DaggerfallQuestInstanceSave advanced = runtime.Capture();
        Assert.NotNull(messages.Pending);
        Assert.All(advanced.Clocks, clock => Assert.True(clock.Finished));
        Assert.True(advanced.Tasks.Single(task => task.Symbol == "late").IsSet);
        Assert.False(advanced.Tasks.Single(task => task.Symbol == "result").IsSet);
    }

    [Fact]
    public void Deadline_task_that_logs_the_journal_runs_through_the_quest_owners_instead_of_failing()
    {
        // The ordinary instance owner advances clocks with its message and lifecycle owners; a deadline
        // task that writes the journal or starts a task must run exactly as it does on the ordinary step.
        DaggerfallQuestSourceDefinition source = new("test", string.Empty, "deadline.txt", DaggerfallQuestDisposition.Compiled,
            [new(1010, 1, ["Time has run out."])],
            [Clock(1, "clock _deadline_ 1"),
             Block("task", 2, "_deadline_ task:", "log 1010 step 2", "start task _late_"),
             Block("variable", 4, "variable _late_")], []);
        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        DaggerfallQuestRuntimeInstance runtime = Runtime(source, program, [new("deadline", 60, 60, 0, 0, 0, true, false)]);
        DaggerfallQuestMessages messages = DaggerfallQuestTaskRuntimeTests.Messages(source);
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));

        DaggerfallQuestClockAdvancer.Advance(runtime, program, variables, DaggerfallCalendar.Start, DaggerfallCalendar.Start.Advance(120, out _),
            messages, new DaggerfallQuestTaskRuntimeTests.LifecycleFake());

        Assert.Equal(DaggerfallQuestLifecycle.Active, runtime.Lifecycle);
        DaggerfallQuestJournalEntrySave entry = Assert.Single(messages.Journal);
        Assert.Equal((1010, 2), (entry.MessageId, entry.Step));
        Assert.True(runtime.Capture().Tasks.Single(task => task.Symbol == "late").IsSet);
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
}
