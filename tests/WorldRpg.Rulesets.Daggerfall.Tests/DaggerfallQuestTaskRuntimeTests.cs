using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallQuestTaskRuntimeTests
{
    [Fact]
    public void Ordered_tasks_evaluate_and_not_then_end_with_the_retained_message()
    {
        DaggerfallQuestSourceDefinition source = Source(
            Block("variable", 1, "variable _a_"),
            Block("variable", 2, "variable _b_"),
            Block("headless", 4, "start task _a_"),
            Block("task", 5, "_gate_ task:", "when _a_ and not _b_", "start task _done_", "clear _gate_"),
            Block("task", 9, "_done_ task:", "end quest saying 42"));

        DaggerfallQuestInstanceSave advanced = Advance(source);

        Assert.Equal(DaggerfallQuestLifecycle.Ended, advanced.Lifecycle);
        Assert.Equal("end quest", advanced.Outcome);
        Assert.Equal(42, advanced.TerminalMessageId);
        DaggerfallQuestTaskState gate = advanced.Tasks.Single(task => task.Symbol == "gate");
        Assert.False(gate.IsSet);
        Assert.All(gate.OperationCompleted, Assert.False);
    }

    [Fact]
    public void Secondary_always_on_condition_can_start_a_task_after_its_primary_turns_it_off()
    {
        DaggerfallQuestSourceDefinition source = Source(
            Block("variable", 1, "variable _primary_"),
            Block("variable", 2, "variable _secondary_"),
            Block("headless", 3, "start task _secondary_"),
            Block("task", 4, "_gate_ task:", "when _primary_", "when _secondary_", "end quest"));

        DaggerfallQuestInstanceSave advanced = Advance(source);

        Assert.Equal(DaggerfallQuestLifecycle.Ended, advanced.Lifecycle);
    }

    [Fact]
    public void Clear_rearms_but_unset_drops_even_when_a_later_action_starts_the_same_task()
    {
        DaggerfallQuestSourceDefinition source = Source(
            Block("variable", 1, "variable _target_"),
            Block("task", 2, "_source_ task:", "start task _target_", "clear _source_"),
            Block("headless", 5, "setvar _source_", "unset _target_", "start task _target_"));

        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);
        DaggerfallQuestTaskRunner.Advance(runtime, Program(source), variables, DaggerfallCalendar.Start);
        DaggerfallQuestTaskRunner.Advance(runtime, Program(source), variables, DaggerfallCalendar.Start);
        DaggerfallQuestInstanceSave advanced = runtime.Capture();

        DaggerfallQuestTaskState sourceTask = advanced.Tasks.Single(task => task.Symbol == "source");
        DaggerfallQuestTaskState target = advanced.Tasks.Single(task => task.Symbol == "target");
        Assert.False(sourceTask.IsSet);
        Assert.All(sourceTask.OperationCompleted, Assert.False);
        Assert.True(target.IsDropped);
        Assert.False(target.IsSet);
    }

    [Fact]
    public void Persist_until_rearms_its_actions_and_global_tasks_use_the_session_variable_store()
    {
        DaggerfallQuestSourceDefinition source = Source(
            Block("global", 1, "KnownFlag _globalflag_"),
            Block("variable", 2, "variable _stop_"),
            Block("task", 3, "until _stop_ performed:", "start task _globalflag_"),
            Block("headless", 5, "start task _stop_"));
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal) { ["KnownFlag"] = 0 });
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);

        DaggerfallQuestTaskRunner.Advance(runtime, Program(source), variables, DaggerfallCalendar.Start);
        DaggerfallQuestTaskRunner.Advance(runtime, Program(source), variables, DaggerfallCalendar.Start);
        DaggerfallQuestInstanceSave advanced = runtime.Capture();

        Assert.True(variables.ReadGlobal("KnownFlag"));
        DaggerfallQuestTaskState persistent = advanced.Tasks.Single(task => task.Kind == DaggerfallQuestTaskKind.PersistUntil);
        Assert.False(persistent.IsSet);
        Assert.All(persistent.OperationCompleted, Assert.False);
    }

    [Fact]
    public void Malformed_when_chain_is_an_unsupported_action_not_a_partial_condition()
    {
        DaggerfallQuestSourceDefinition source = Source(Block("headless", 4, "when _a_ BAD and _b_"));

        DaggerfallQuestTaskProgram program = Program(source);
        DaggerfallQuestInstanceSave advanced = Advance(source);

        Assert.Equal(DaggerfallQuestTaskOperationKind.Unsupported, program.Tasks.Single().Operations.Single().Kind);
        Assert.Equal(DaggerfallQuestLifecycle.Failed, advanced.Lifecycle);
        Assert.Contains("Unsupported quest action at line 4: when _a_ BAD and _b_", advanced.Outcome, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_action_ends_in_a_diagnosed_failure()
    {
        DaggerfallQuestSourceDefinition source = Source(Block("headless", 7, "start timer _clock_"));

        DaggerfallQuestInstanceSave advanced = Advance(source);

        Assert.Equal(DaggerfallQuestLifecycle.Failed, advanced.Lifecycle);
        Assert.Contains("Quest action at line 7 refers to missing clock 'clock'.", advanced.Outcome, StringComparison.Ordinal);
    }

    private static DaggerfallQuestInstanceSave Advance(DaggerfallQuestSourceDefinition source)
    {
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);
        DaggerfallQuestTaskRunner.Advance(runtime, Program(source), variables, DaggerfallCalendar.Start);
        return runtime.Capture();
    }

    private static DaggerfallQuestRuntimeInstance Runtime(DaggerfallQuestSourceDefinition source)
    {
        DaggerfallQuestTaskProgram program = Program(source);
        DaggerfallQuestInstanceSave instance = new("quest:1", source.SourceFile, source.Name,
            DaggerfallQuestLifecycle.Active, null, [], []) { Tasks = DaggerfallQuestTaskCompiler.InitialState(program) };
        return new DaggerfallQuestRuntimeInstance(instance, program);
    }

    private static DaggerfallQuestTaskProgram Program(DaggerfallQuestSourceDefinition source) => DaggerfallQuestTaskCompiler.Compile(source);

    private static DaggerfallQuestSourceDefinition Source(params DaggerfallQuestBlockDefinition[] blocks) =>
        new("test", string.Empty, "test.txt", DaggerfallQuestDisposition.Compiled, [], blocks, []);

    private static DaggerfallQuestBlockDefinition Block(string kind, int line, params string[] lines) => new(kind, line, lines, null);
}
