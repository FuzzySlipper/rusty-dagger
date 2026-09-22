using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallQuestTaskRuntimeTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Multi_choice_survives_pending_save_records_before_branch_and_rejects_replayed_occurrence(int count)
    {
        string[] choices = ["3 _a_", "4 _b_", "19 _c_", "20 _d_"];
        DaggerfallQuestSourceDefinition source = new("test", string.Empty, "multi.txt", DaggerfallQuestDisposition.Compiled,
            [new(1010, 1, ["Choose."])],
            [Block("variable", 1, "variable _a_"), Block("variable", 2, "variable _b_"),
             Block("variable", 3, "variable _c_"), Block("variable", 4, "variable _d_"),
             Block("task", 5, "_source_ task:", "promptmulti 1010 " + string.Join(' ', choices.Take(count)), "clear _source_")], []);
        DaggerfallQuestTaskProgram program = Program(source);
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));
        runtime.Tasks.Single(task => task.Symbol == "source").IsSet = true;
        DaggerfallQuestMessages messages = Messages(source);
        DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start, messages);
        DaggerfallQuestMessages restored = Messages(source);
        string json = JsonSerializer.Serialize(messages.Capture(), typeof(DaggerfallQuestMessagesSave), DaggerfallSaveJsonContext.Default);
        DaggerfallQuestMessagesSave persisted = (DaggerfallQuestMessagesSave)JsonSerializer.Deserialize(json, typeof(DaggerfallQuestMessagesSave), DaggerfallSaveJsonContext.Default)!;
        restored.Restore(persisted, new Dictionary<string, DaggerfallQuestRuntimeInstance> { [runtime.InstanceId] = runtime });
        DaggerfallQuestPromptSave pending = restored.Pending!;
        Assert.Equal(count, pending.Options.Length);
        DaggerfallQuestRenderedMessage projected = Assert.Single(restored.Render([runtime]));
        Assert.Equal(pending.Id, projected.PromptId);
        Assert.Equal(pending.Options, projected.Options);
        Assert.False(restored.TryChoose(runtime.InstanceId, 1010, pending.Id, 999, (_, _) => throw new Exception("invalid choice invoked"), out _, out _));
        Assert.True(restored.TryChoose(runtime.InstanceId, 1010, pending.Id, pending.Options[^1].Id, (prompt, choice) =>
        {
            Action apply = DaggerfallQuestTaskRunner.PrepareChoice(runtime, program, variables, prompt, choice);
            return () =>
            {
                Assert.Equal(choice, Assert.Single(restored.Capture().Choices));
                Assert.Null(restored.Pending);
                apply();
            };
        }, out _, out _));
        Assert.True(runtime.Tasks.Single(task => task.Symbol == pending.Options[^1].Target).IsSet);
        Assert.False(restored.TryChoose(runtime.InstanceId, 1010, pending.Id, pending.Options[^1].Id, (_, _) => throw new Exception("replay invoked"), out _, out _));
        DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start, restored);
        runtime.Tasks.Single(task => task.Symbol == "source").IsSet = true;
        DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start, restored);
        Assert.NotEqual(pending.Id, restored.Pending!.Id);
        Assert.False(restored.TryChoose(runtime.InstanceId, 1010, pending.Id, pending.Options[0].Id, (_, _) => throw new Exception("stale occurrence invoked"), out _, out _));
        Assert.Single(restored.Choices);
        DaggerfallQuestTaskRunner.ValidatePrompt(runtime, program, restored.Pending);
        Assert.Throws<ArgumentException>(() => DaggerfallQuestTaskRunner.ValidatePrompt(runtime, program,
            restored.Pending with { Options = [new(3, "Yes", "a"), new(4, "No", "wrong")] }));
    }

    [Fact]
    public void Direct_message_operations_keep_journal_prompt_choice_and_delivery_state_through_a_save()
    {
        DaggerfallQuestSourceDefinition source = new("test", string.Empty, "test.txt", DaggerfallQuestDisposition.Compiled,
            [new(1010, 1, ["Dear %pcn, _giver_ of ==giver_ in ___town_. Find =monster_ at __dungeon_."])],
        [
            Block("variable", 1, "variable _yes_"),
            Block("variable", 2, "variable _no_"),
            Block("headless", 3, "log 1010 step 4", "rumor mill 1010", "prompt 1010 yes _yes_ no _no_"),
        ], []);
        DaggerfallQuestTaskProgram program = Program(source);
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);
        DaggerfallQuestMessages messages = Messages(source);
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));

        DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start, messages);

        Assert.Equal(new DaggerfallQuestJournalEntrySave("quest:1", 4, 1010), Assert.Single(messages.Journal));
        Assert.Equal(DaggerfallQuestMessageDelivery.Rumor, Assert.Single(messages.Deliveries, value => value.Delivery == DaggerfallQuestMessageDelivery.Rumor).Delivery);
        Assert.NotNull(messages.Pending);
        DaggerfallQuestMessagesSave saved = messages.Capture();
        DaggerfallQuestMessages restored = Messages(source);
        restored.Restore(saved with { Choices = [
            new("quest:1", 1010, 3, "yes", "earlier-prompt", 0, 0),
            new("quest:1", 1010, 4, "no", "rearmed-prompt", 0, 0),
        ] }, new Dictionary<string, DaggerfallQuestRuntimeInstance>(StringComparer.Ordinal) { [runtime.InstanceId] = runtime });
        Assert.Equal(2, restored.Choices.Count);

        Assert.True(restored.TryChoose("quest:1", 1010, restored.Pending?.Id ?? "expired", 3,
            (prompt, choice) => DaggerfallQuestTaskRunner.PrepareChoice(runtime, program, variables, prompt, choice),
            out DaggerfallQuestChoiceSave? choice, out _));
        Assert.True(runtime.Tasks.Single(task => task.Symbol == "yes").IsSet);
        Assert.False(restored.TryChoose("quest:1", 1010, restored.Pending?.Id ?? "expired", 3, (_, _) => () => { }, out _, out _));
        Assert.Contains(restored.Choices, choice => choice.Target == "yes" && choice.TaskSymbol == "headless.3");
    }

    [Fact]
    public void Rearmed_prompt_records_distinct_occurrences_and_restores_both_answers()
    {
        DaggerfallQuestSourceDefinition source = new("test", string.Empty, "rearmed.txt", DaggerfallQuestDisposition.Compiled,
            [new(1010, 1, ["Choose."])],
            [Block("variable", 1, "variable _yes_"), Block("variable", 2, "variable _no_"),
                Block("task", 3, "_source_ task:", "prompt 1010 yes _yes_ no _no_", "clear _source_")], []);
        DaggerfallQuestTaskProgram program = Program(source);
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);
        DaggerfallQuestMessages messages = Messages(source);
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));
        DaggerfallQuestTaskRuntimeState sourceTask = runtime.Tasks.Single(task => task.Symbol == "source");

        sourceTask.IsSet = true;
        DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start, messages);
        Assert.True(messages.TryChoose("quest:1", 1010, messages.Pending?.Id ?? "expired", 3,
            (prompt, choice) => DaggerfallQuestTaskRunner.PrepareChoice(runtime, program, variables, prompt, choice), out _, out _));
        DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start, messages);
        Assert.False(sourceTask.IsSet);
        Assert.All(sourceTask.OperationCompleted, Assert.False);
        sourceTask.IsSet = true;
        DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start, messages);
        Assert.True(messages.TryChoose("quest:1", 1010, messages.Pending?.Id ?? "expired", 4,
            (prompt, choice) => DaggerfallQuestTaskRunner.PrepareChoice(runtime, program, variables, prompt, choice), out _, out _));

        Assert.Equal([0, 1], messages.Choices.Select(choice => choice.Occurrence));
        DaggerfallQuestMessages restored = Messages(source);
        restored.Restore(messages.Capture(), new Dictionary<string, DaggerfallQuestRuntimeInstance>(StringComparer.Ordinal) { [runtime.InstanceId] = runtime });
        Assert.Equal([0, 1], restored.Choices.Select(choice => choice.Occurrence));
    }

    [Fact]
    public void Stale_prompt_rejection_keeps_the_pending_choice_visible()
    {
        DaggerfallQuestSourceDefinition source = new("test", string.Empty, "stale.txt", DaggerfallQuestDisposition.Compiled,
            [new(1010, 1, ["Choose."])],
            [Block("variable", 1, "variable _no_"),
                Block("headless", 3, "prompt 1010 yes _missing_ no _no_")], []);
        DaggerfallQuestTaskProgram program = Program(source);
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);
        DaggerfallQuestMessages messages = Messages(source);
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));

        DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start, messages);
        DaggerfallQuestTaskRuntimeState sourceTask = runtime.Tasks.Single(task => task.Symbol.StartsWith("headless.", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => messages.TryChoose("quest:1", 1010, messages.Pending?.Id ?? "expired", 3,
            (prompt, choice) => DaggerfallQuestTaskRunner.PrepareChoice(runtime, program, variables, prompt, choice), out _, out _));
        Assert.False(sourceTask.OperationCompleted[0]);
        Assert.NotNull(messages.Pending);
        Assert.Empty(messages.Choices);
    }

    [Fact]
    public void Quest_message_state_is_required_in_the_current_save_shape()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"Instances\":[]}",
            DaggerfallSaveJsonContext.Default.GetTypeInfo(typeof(DaggerfallQuestInstancesSave))!));
        Assert.Throws<ArgumentNullException>(() => (new DaggerfallQuestInstancesSave([]) { Messages = null! }).Validate());
    }

    [Fact]
    public void Direct_message_compiler_claims_every_retained_task_form()
    {
        DaggerfallQuestSourceDefinition source = Source(Block("headless", 1,
            "log 1010 step 3", "remove log step 3", "rumor mill 1010", "prompt QuestorOffer yes _yes_ no _no_", "end quest saying 1010"));

        DaggerfallQuestTaskOperationKind[] operations = Program(source).Tasks.Single().Operations.Select(operation => operation.Kind).ToArray();

        Assert.Equal([DaggerfallQuestTaskOperationKind.Journal, DaggerfallQuestTaskOperationKind.RemoveJournal,
            DaggerfallQuestTaskOperationKind.Rumor, DaggerfallQuestTaskOperationKind.Prompt, DaggerfallQuestTaskOperationKind.End], operations);
        DaggerfallQuestTaskOperation prompt = Program(source).Tasks.Single().Operations[3];
        Assert.Null(prompt.MessageId);
        Assert.Equal("QuestorOffer", prompt.MessageAlias);
        DaggerfallQuestSourceDefinition withMessage = new("test", string.Empty, source.SourceFile, DaggerfallQuestDisposition.Compiled,
            [new(1010, 1, ["An offered quest."])], source.Blocks, []);
        DaggerfallQuestMessages messages = Messages(withMessage, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["QuestorOffer"] = 1010 });
        Assert.True(messages.TryResolveMessage(Runtime(withMessage), null, "QuestorOffer", out int resolved, out string? diagnostic));
        Assert.Equal(1010, resolved);
        Assert.Null(diagnostic);
    }

    [Fact]
    public void Persisted_prompt_alias_must_resolve_to_its_compiled_source_message()
    {
        DaggerfallQuestSourceDefinition source = new("test", string.Empty, "alias.txt", DaggerfallQuestDisposition.Compiled,
            [new(1010, 1, ["Offer."]), new(1011, 2, ["Different valid message."])],
            [Block("headless", 3, "prompt QuestorOffer yes _yes_ no _no_")], []);
        DaggerfallQuestTaskProgram program = Program(source);
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);
        DaggerfallQuestMessages messages = Messages(source, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["QuestorOffer"] = 1010 });
        DaggerfallQuestTaskOperation operation = program.Tasks.Single().Operations.Single();
        DaggerfallQuestPromptSave wrongMessage = new("quest:1", 1011, [new(3, "Yes", "yes"), new(4, "No", "no")], "headless.3", 0, 0);

        Assert.Equal(1010, messages.ResolvePromptMessage(runtime, operation));
        Assert.Throws<ArgumentException>(() => DaggerfallQuestTaskRunner.ValidatePrompt(runtime, program, wrongMessage,
            candidate => messages.ResolvePromptMessage(runtime, candidate)));
    }

    [Fact]
    public void Journal_removal_rumor_and_terminal_popup_execute_in_source_order()
    {
        DaggerfallQuestSourceDefinition source = new("test", string.Empty, "delivery.txt", DaggerfallQuestDisposition.Compiled,
            [new(1010, 1, ["Message."])],
            [Block("headless", 1, "log 1010 step 3", "remove log step 3", "rumor mill 1010", "end quest saying 1010")], []);
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);
        DaggerfallQuestMessages messages = Messages(source);

        DaggerfallQuestTaskRunner.Advance(runtime, Program(source), new(new Dictionary<string, int>(StringComparer.Ordinal)), DaggerfallCalendar.Start, messages);

        Assert.Empty(messages.Journal);
        Assert.Equal(DaggerfallQuestLifecycle.Ended, runtime.Lifecycle);
        Assert.Equal([DaggerfallQuestMessageDelivery.Rumor, DaggerfallQuestMessageDelivery.Popup], messages.Deliveries.Select(value => value.Delivery));
    }

    [Fact]
    public void Quest_message_macros_use_all_retained_resource_arms_before_shared_global_macros()
    {
        DaggerfallQuestSourceDefinition source = new("test", string.Empty, "macros.txt", DaggerfallQuestDisposition.Compiled,
            [new(1010, 1, ["%pcn meets _giver_ of ==giver_ in __dungeon_, ___town_, and ____realm_: =monster_ (=#giver_).", "Sincerely _giver_", "<--->", "unused variant"])], [], []);
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);
        DaggerfallQuestMessages messages = Messages(source);
        DaggerfallQuestMessageContext context = new(new(new(Name: "Nulfaga"), new(), new(), new(), new(), new()),
            new Dictionary<string, DaggerfallQuestResourceTextContext>(StringComparer.Ordinal)
            {
                ["giver"] = new(Name: "Aubk-i", Binding: "the guildmaster", Faction: "The Mages Guild"),
                ["dungeon"] = new(NameTwo: "Daggerfall Castle"),
                ["town"] = new(NameThree: "Daggerfall"),
                ["realm"] = new(NameFour: "Glenumbra"),
                ["monster"] = new(Details: "a daedra"),
            });

        messages.Popup(runtime, 1010);
        messages.Letter(runtime, 1010);
        DaggerfallQuestRenderedMessage popup = Assert.Single(messages.Render([runtime], context), message => message.Delivery == DaggerfallQuestMessageDelivery.Popup);
        DaggerfallQuestRenderedMessage letter = Assert.Single(messages.Render([runtime], context), message => message.Delivery == DaggerfallQuestMessageDelivery.Letter);

        Assert.Equal("Nulfaga meets Aubk-i of The Mages Guild in Daggerfall Castle, Daggerfall, and Glenumbra: a daedra (the guildmaster).\nSincerely Aubk-i", popup.Text);
        Assert.Empty(popup.Diagnostics);
        Assert.Equal("Nulfaga meets Aubk-i of The Mages Guild in ..., ..., and ...: a daedra (the guildmaster).", letter.Text);
        Assert.Equal("Sincerely Aubk-i", letter.Signoff);
        Assert.Empty(letter.Diagnostics);
    }

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
    public void Assessment_returns_the_actual_unsupported_operation_line_and_source()
    {
        DaggerfallQuestSourceDefinition source = Source(Block("headless", 4, "when _a_ BAD and _b_"));

        DaggerfallQuestDiagnosticDefinition diagnostic = Assert.Single(DaggerfallQuestTaskCompiler.Assess(source));

        Assert.Equal(4, diagnostic.Line);
        Assert.Equal("when _a_ BAD and _b_", diagnostic.Text);
        Assert.Contains("runner operation supports", diagnostic.Reason, StringComparison.Ordinal);
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

    private static DaggerfallQuestMessages Messages(DaggerfallQuestSourceDefinition source, IReadOnlyDictionary<string, int>? staticMessages = null) => new(
        new DaggerfallTextResolver(new DaggerfallTextSet(new Dictionary<DaggerfallTextKey, DaggerfallTextValue>(), [], [])),
        new Dictionary<string, DaggerfallQuestSourceDefinition>(StringComparer.Ordinal) { [source.SourceFile] = source }, staticMessages);

    private static DaggerfallQuestBlockDefinition Block(string kind, int line, params string[] lines) => new(kind, line, lines, null);
}
