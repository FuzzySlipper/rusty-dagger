using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallQuestTaskRuntimeTests
{
    [Fact]
    public void Saved_prompt_history_is_required_to_prevent_occurrence_reuse()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"Deliveries\":[],\"Journal\":[],\"Pending\":null}", typeof(DaggerfallQuestMessagesSave), DaggerfallSaveJsonContext.Default));
    }

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
    public void Quest_success_flag_is_required_even_when_an_active_instance_has_not_set_it()
    {
        DaggerfallQuestInstanceSave saved = Runtime(Source()).Capture();
        JsonObject json = JsonNode.Parse(JsonSerializer.Serialize(saved, typeof(DaggerfallQuestInstanceSave), DaggerfallSaveJsonContext.Default))!.AsObject();
        Assert.True(json.Remove("Succeeded"));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json.ToJsonString(), typeof(DaggerfallQuestInstanceSave), DaggerfallSaveJsonContext.Default));
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

    [Fact]
    public void Pick_one_of_persists_the_selected_task_before_a_save_can_replay_the_operation()
    {
        DaggerfallQuestSourceDefinition source = Source(
            Block("variable", 1, "variable _first_"), Block("variable", 2, "variable _second_"),
            Block("headless", 3, "pick one of _first_ _second_"));
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);
        DaggerfallQuestTaskRunner.Advance(runtime, Program(source), new(new Dictionary<string, int>(StringComparer.Ordinal)),
            DaggerfallCalendar.Start, lifecycle: new LifecycleFake(pick: "second"));

        DaggerfallQuestInstanceSave captured = runtime.Capture();
        string json = JsonSerializer.Serialize(captured, typeof(DaggerfallQuestInstanceSave), DaggerfallSaveJsonContext.Default);
        DaggerfallQuestInstanceSave restored = (DaggerfallQuestInstanceSave)JsonSerializer.Deserialize(json, typeof(DaggerfallQuestInstanceSave), DaggerfallSaveJsonContext.Default)!;

        Assert.Equal("second", restored.Tasks.Single(task => task.Symbol == "headless.3").OperationState[0].PickedTarget);
        Assert.True(restored.Tasks.Single(task => task.Symbol == "second").IsSet);
    }

    [Fact]
    public void Run_quest_keeps_its_child_receipt_until_the_terminal_success_branch_is_started()
    {
        DaggerfallQuestSourceDefinition source = Source(
            Block("variable", 1, "variable _success_"), Block("variable", 2, "variable _failure_"),
            Block("headless", 3, "run quest child then _success_ or _failure_"));
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);
        LifecycleFake lifecycle = new(childBranch: null);
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));

        DaggerfallQuestTaskRunner.Advance(runtime, Program(source), variables, DaggerfallCalendar.Start, lifecycle: lifecycle);
        Assert.Equal("child:1", runtime.Tasks.Single(task => task.Symbol == "headless.3").OperationState[0].ChildInstanceId);
        Assert.False(runtime.Tasks.Single(task => task.Symbol == "headless.3").OperationCompleted[0]);

        lifecycle.ChildBranch = "success";
        DaggerfallQuestTaskRunner.Advance(runtime, Program(source), variables, DaggerfallCalendar.Start, lifecycle: lifecycle);
        Assert.True(runtime.Tasks.Single(task => task.Symbol == "success").IsSet);
    }

    [Fact]
    public void Run_quest_with_a_missing_source_starts_its_failure_branch_through_the_session_owner()
    {
        DaggerfallQuestSourceDefinition source = Source(
            Block("variable", 1, "variable _success_"), Block("variable", 2, "variable _failure_"),
            Block("headless", 3, "run quest missing then _success_ or _failure_"));
        DaggerfallQuestRuntimeInstance runtime = Runtime(source);

        DaggerfallQuestTaskRunner.Advance(runtime, Program(source), new(new Dictionary<string, int>(StringComparer.Ordinal)),
            DaggerfallCalendar.Start, lifecycle: Instances(Definitions()));

        DaggerfallQuestTaskRuntimeState task = Assert.Single(runtime.Tasks, task => task.Symbol == "headless.3");
        Assert.True(task.OperationCompleted[0]);
        Assert.True(Assert.Single(runtime.Tasks, task => task.Symbol == "failure").IsSet);
        Assert.Null(task.OperationState[0].ChildInstanceId);
    }

    [Fact]
    public void Instance_policy_rejects_duplicate_ids_and_cyclic_children_then_removes_terminal_tombstones_after_a_week()
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallQuestSourceDefinition[] sources = StartableSources(definitions, 3);
        DaggerfallQuestInstances instances = Instances(definitions);
        DaggerfallQuestInstanceSave parent = new("parent", sources[0].SourceFile, sources[0].Name, DaggerfallQuestLifecycle.Active, null, [], []);
        instances.Start(parent);

        Assert.Throws<ArgumentException>(() => instances.Start(parent));
        Assert.Throws<ArgumentException>(() => instances.ScheduleChild("parent", sources[0].SourceFile, 0, "cycle"));
        instances.Start(new("child", sources[1].SourceFile, sources[1].Name, DaggerfallQuestLifecycle.Active, null, [], []) { ParentInstanceId = "parent" });
        instances.ScheduleChild("parent", sources[2].SourceFile, 0, "pending-child");
        DaggerfallQuestInstancesSave queued = instances.Capture();
        Assert.Equal("pending-child", Assert.Single(queued.PendingStarts).InstanceId);
        instances.Complete("parent", "finished");
        instances.Advance(new(new Dictionary<string, int>(StringComparer.Ordinal)), DaggerfallCalendar.Start);
        DaggerfallQuestInstanceSave[] tombstoned = [.. instances.All];
        Assert.Equal(DaggerfallQuestLifecycle.Tombstoned, Assert.Single(tombstoned, instance => instance.InstanceId == "parent").Lifecycle);
        DaggerfallQuestInstanceSave child = Assert.Single(tombstoned, instance => instance.InstanceId == "child");
        Assert.Equal(DaggerfallQuestLifecycle.Tombstoned, child.Lifecycle);
        Assert.False(child.Succeeded);
        Assert.Contains("Parent quest 'parent' ended.", child.Outcome, StringComparison.Ordinal);

        DaggerfallCalendar afterWeek = DaggerfallCalendar.Start.Advance((7 * 24 * 60 * 60) + 1, out _);
        instances.Advance(new(new Dictionary<string, int>(StringComparer.Ordinal)), afterWeek);
        Assert.Empty(instances.All);
    }

    [Fact]
    public void Restore_rejects_saved_missing_and_cyclic_parents()
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallQuestSourceDefinition[] sources = StartableSources(definitions, 2);
        DaggerfallQuestInstances instances = Instances(definitions);
        instances.Start(new("parent", sources[0].SourceFile, sources[0].Name, DaggerfallQuestLifecycle.Active, null, [], []));
        instances.Start(new("child", sources[1].SourceFile, sources[1].Name, DaggerfallQuestLifecycle.Active, null, [], []) { ParentInstanceId = "parent" });

        DaggerfallQuestInstancesSave saved = RoundTrip(instances.Capture());
        DaggerfallQuestInstanceSave parent = Assert.Single(saved.Instances, instance => instance.InstanceId == "parent");
        DaggerfallQuestInstanceSave child = Assert.Single(saved.Instances, instance => instance.InstanceId == "child");

        Assert.Throws<ArgumentException>(() => Instances(definitions).Restore(saved with
        {
            Instances = [parent with { ParentInstanceId = "child" }, child],
        }));
        Assert.Throws<ArgumentException>(() => Instances(definitions).Restore(saved with
        {
            Instances = [parent, child with { ParentInstanceId = "missing" }],
        }));
        Assert.Throws<ArgumentException>(() => Instances(definitions).Restore(saved with
        {
            PendingStarts = [new("pending", sources[0].SourceFile, "missing", 0)],
        }));
    }

    [Fact]
    public void Restore_rejects_a_pick_receipt_that_does_not_belong_to_its_compiled_operation()
    {
        DaggerfallDefinitions definitions = Definitions();
        (DaggerfallQuestInstances instances, DaggerfallQuestSourceDefinition source) = StartedSource(definitions,
            program => program.Tasks.SelectMany(task => task.Operations).Any(operation => operation.Kind == DaggerfallQuestTaskOperationKind.PickOneOf));
        DaggerfallQuestInstancesSave saved = RoundTrip(instances.Capture());
        DaggerfallQuestInstanceSave instance = Assert.Single(saved.Instances);
        DaggerfallQuestTaskProgram program = Program(source);
        int taskIndex = program.Tasks.Select((task, index) => (task, index))
            .Single(value => value.task.Operations.Any(operation => operation.Kind == DaggerfallQuestTaskOperationKind.PickOneOf)).index;
        int operationIndex = program.Tasks[taskIndex].Operations
            .Select((operation, index) => (operation, index))
            .Single(value => value.operation.Kind == DaggerfallQuestTaskOperationKind.PickOneOf).index;
        DaggerfallQuestTaskState[] tasks = [.. instance.Tasks];
        DaggerfallQuestTaskOperationState[] operationState = [.. tasks[taskIndex].OperationState];
        operationState[operationIndex] = new("not-a-compiled-target", "unrelated-child");
        tasks[taskIndex] = tasks[taskIndex] with { OperationState = operationState };

        Assert.Throws<ArgumentException>(() => Instances(definitions).Restore(saved with
        {
            Instances = [instance with { Tasks = tasks }],
        }));
    }

    [Fact]
    public void Start_quest_is_independent_when_its_parent_ends_before_the_queued_start()
    {
        DaggerfallDefinitions definitions = DefinitionsWithLifecycleFixtures();
        DaggerfallQuestInstances instances = Instances(definitions);
        DaggerfallQuestSourceDefinition parent = definitions.QuestSources.Resolve("startparent.txt");
        instances.Start(new("parent", parent.SourceFile, parent.Name, DaggerfallQuestLifecycle.Active, null, [], []));

        instances.Advance(new(new Dictionary<string, int>(StringComparer.Ordinal)), DaggerfallCalendar.Start);
        DaggerfallQuestStartSave queued = Assert.Single(instances.Capture().PendingStarts);
        Assert.Null(queued.ParentInstanceId);
        Assert.Equal("parent:start:11", queued.InstanceId);

        instances.Advance(new(new Dictionary<string, int>(StringComparer.Ordinal)), DaggerfallCalendar.Start);
        Assert.Contains(instances.All, instance => instance.InstanceId == queued.InstanceId);
    }

    [Fact]
    public void Restore_keeps_a_completed_run_quest_receipt_after_its_child_tombstone_expires()
    {
        DaggerfallDefinitions definitions = DefinitionsWithLifecycleFixtures();
        DaggerfallQuestInstances instances = Instances(definitions);
        DaggerfallQuestSourceDefinition parent = definitions.QuestSources.Resolve("runparent.txt");
        instances.Start(new("parent", parent.SourceFile, parent.Name, DaggerfallQuestLifecycle.Active, null, [], []));
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));

        instances.Advance(variables, DaggerfallCalendar.Start);
        instances.Advance(variables, DaggerfallCalendar.Start);
        const string childId = "parent:run:headless.3:0";
        instances.Complete(childId, "completed");
        instances.Advance(variables, DaggerfallCalendar.Start);
        Assert.True(Assert.Single(instances.All, instance => instance.InstanceId == "parent").Tasks
            .Single(task => task.Symbol == "success").IsSet);

        DaggerfallCalendar afterWeek = DaggerfallCalendar.Start.Advance((7 * 24 * 60 * 60) + 1, out _);
        instances.Advance(variables, afterWeek);
        DaggerfallQuestInstancesSave saved = RoundTrip(instances.Capture());
        Assert.DoesNotContain(saved.Instances, instance => instance.InstanceId == childId);
        Assert.Equal(DaggerfallQuestLifecycle.Active, Assert.Single(saved.Instances).Lifecycle);

        DaggerfallQuestInstances restored = Instances(definitions);
        restored.Restore(saved);
        Assert.Equal("parent", Assert.Single(restored.All).InstanceId);
    }

    [Fact]
    public void Plain_child_end_uses_the_run_quest_failure_branch()
    {
        DaggerfallDefinitions definitions = DefinitionsWithLifecycleFixtures();
        DaggerfallQuestInstances instances = Instances(definitions);
        DaggerfallQuestSourceDefinition parent = definitions.QuestSources.Resolve("endparent.txt");
        instances.Start(new("parent", parent.SourceFile, parent.Name, DaggerfallQuestLifecycle.Active, null, [], []));
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));

        instances.Advance(variables, DaggerfallCalendar.Start);
        instances.Advance(variables, DaggerfallCalendar.Start);
        instances.Advance(variables, DaggerfallCalendar.Start);

        DaggerfallQuestInstanceSave saved = Assert.Single(instances.All, instance => instance.InstanceId == "parent");
        Assert.True(saved.Tasks.Single(task => task.Symbol == "failure").IsSet);
        Assert.False(saved.Tasks.Single(task => task.Symbol == "success").IsSet);
    }

    [Fact]
    public void Live_player_conditions_and_train_pc_use_the_named_quest_runtime_and_saved_training_time()
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer(player.Career!);
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, career));
        ProgressionState progression = new();
        DaggerfallQuestTrainingState training = new();
        DaggerfallCalendar calendar = DaggerfallCalendar.Start;
        DaggerfallQuestRuntime runtime = new(progression, stats, definitions, training, DaggerfallLocomotionTuning.Classic, RandomMinimum.Create(), () => calendar,
            seconds => calendar = calendar.Advance(seconds, out _));
        string skill = definitions.Vocabulary.Skills.First().Value;
        string attribute = definitions.Vocabulary.Attributes.First().Value;
        stats.GetStat(StatId.Parse(skill)).BaseValue = 42;
        stats.GetStat(StatId.Parse(attribute)).BaseValue = 43;
        progression.AdvanceTo(0, 3);

        Assert.True(runtime.IsLevelCompleted(3));
        Assert.True(runtime.IsAttributeAtLeast(attribute, 43));
        Assert.True(runtime.IsSkillAtLeast(skill, 42));

        DaggerfallQuestSourceDefinition source = Source(Block("headless", 7, "train pc " + skill.Replace("-", string.Empty, StringComparison.Ordinal)));
        DaggerfallQuestRuntimeInstance instance = Runtime(source);
        DaggerfallQuestTaskOperation operation = Program(source).Tasks.Single().Operations.Single();
        double fatigueBefore = stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current;
        runtime.Train(instance, operation);

        Assert.True(instance.Succeeded);
        Assert.Equal(DaggerfallCalendar.Start.ToAbsoluteSeconds(), training.LastSkillTrainingSecond);
        Assert.Equal(DaggerfallCalendar.Start.Advance(3 * 60 * 60, out _), calendar);
        Assert.Equal(10 * DaggerfallFormulaPolicy.SkillAdvancementMultiplier(skill), progression.SkillUses[skill]);
        Assert.Equal(training.Capture(), new DaggerfallQuestTrainingState(training.Capture()).Capture());
        Assert.Equal(fatigueBefore - (11 * 180), stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
    }

    [Fact]
    public void Compiler_claims_the_donor_level_attribute_skill_and_train_pc_forms()
    {
        DaggerfallQuestSourceDefinition source = Source(Block("headless", 4,
            "level 2 completed", "when attribute Strength is at least 40", "when skill LongBlade is at least 30", "train pc LongBlade"));

        Assert.Equal([
            DaggerfallQuestTaskOperationKind.LevelCompleted,
            DaggerfallQuestTaskOperationKind.WhenAttributeLevel,
            DaggerfallQuestTaskOperationKind.WhenSkillLevel,
            DaggerfallQuestTaskOperationKind.TrainPc,
        ], Program(source).Tasks.Single().Operations.Select(operation => operation.Kind));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Quest_instances_run_train_pc_through_the_bound_session_runtime(bool hasRewardMessage)
    {
        DaggerfallDefinitions definitions = DefinitionsWithLifecycleFixtures(hasRewardMessage);
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer(player.Career!);
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, career));
        DaggerfallCalendar calendar = DaggerfallCalendar.Start;
        DaggerfallQuestTrainingState training = new();
        DaggerfallQuestRuntime runtime = new(new ProgressionState(), stats, definitions, training, DaggerfallLocomotionTuning.Classic, RandomMinimum.Create(), () => calendar,
            seconds => calendar = calendar.Advance(seconds, out _));
        DaggerfallQuestInstances instances = Instances(definitions);
        instances.BindRuntime(runtime);
        DaggerfallQuestSourceDefinition source = definitions.QuestSources.Resolve("trainquest.txt");
        instances.Start(new("training", source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], []));

        instances.Advance(new(new Dictionary<string, int>(StringComparer.Ordinal)), calendar);

        Assert.Equal(DaggerfallCalendar.Start.Advance(3 * 60 * 60, out _), calendar);
        Assert.True(Assert.Single(instances.All).Succeeded);
        Assert.NotNull(training.LastSkillTrainingSecond);
        if (hasRewardMessage) Assert.Equal(new DaggerfallQuestMessageDeliverySave("training", 1004, DaggerfallQuestMessageDelivery.Popup), Assert.Single(instances.Messages.Deliveries));
        else Assert.Empty(instances.Messages.Deliveries);
    }

    [Fact]
    public void Rearming_a_task_cannot_repeat_train_pc_time_fatigue_or_skill_reward()
    {
        DaggerfallDefinitions definitions = DefinitionsWithLifecycleFixtures();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer(player.Career!);
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, career));
        DaggerfallCalendar calendar = DaggerfallCalendar.Start;
        ProgressionState progression = new();
        DaggerfallQuestTrainingState training = new();
        DaggerfallQuestRuntime runtime = new(progression, stats, definitions, training, DaggerfallLocomotionTuning.Classic, RandomMinimum.Create(), () => calendar,
            seconds => calendar = calendar.Advance(seconds, out _));
        DaggerfallQuestInstances instances = Instances(definitions);
        instances.BindRuntime(runtime);
        DaggerfallQuestSourceDefinition source = definitions.QuestSources.Resolve("rearmtraining.txt");
        instances.Start(new("rearm-training", source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], []));
        DaggerfallVariableStore variables = new(new Dictionary<string, int>(StringComparer.Ordinal));

        instances.Advance(variables, calendar);
        long trainedAt = training.LastSkillTrainingSecond!.Value;
        int skillUses = progression.SkillUses["long-blade"];
        double fatigue = stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current;
        instances.Advance(variables, calendar);

        Assert.Equal(DaggerfallCalendar.Start.Advance(3 * 60 * 60, out _), calendar);
        Assert.Equal(trainedAt, training.LastSkillTrainingSecond);
        Assert.Equal(skillUses, progression.SkillUses["long-blade"]);
        Assert.Equal(fatigue, stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
        Assert.Single(instances.Messages.Deliveries, delivery => delivery is { MessageId: 1004, Delivery: DaggerfallQuestMessageDelivery.Popup });
    }

    [Theory]
    [InlineData("S0000999.txt")]
    [InlineData("s0000977")]
    [InlineData("_BRISIEN.txt")]
    [InlineData("S0000001.txt")]
    public void Protected_main_quest_names_are_a_daggerfall_lifecycle_policy(string source)
    {
        Assert.Equal(source is not "S0000001.txt", DaggerfallQuestInstances.IsProtectedMainQuest(source));
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

    /// <summary>The leg a lifecycle test's quest clock measures: the session binds the real admitted
    /// route calculator before anything starts, and these tests only need a deterministic duration.
    /// </summary>
    private const long QuestLegMinutes = 60;

    /// <summary>One lifecycle owner carrying the session-bound travel owner these tests would otherwise lack.</summary>
    private static DaggerfallQuestInstances Instances(DaggerfallDefinitions definitions)
    {
        DaggerfallQuestInstances instances = new(definitions, RandomMinimum.Create());
        instances.BindTravelMinutes(_ => QuestLegMinutes);
        return instances;
    }

    private static DaggerfallQuestInstancesSave RoundTrip(DaggerfallQuestInstancesSave saved) =>
        (DaggerfallQuestInstancesSave)JsonSerializer.Deserialize(
            JsonSerializer.Serialize(saved, typeof(DaggerfallQuestInstancesSave), DaggerfallSaveJsonContext.Default),
            typeof(DaggerfallQuestInstancesSave), DaggerfallSaveJsonContext.Default)!;

    private static (DaggerfallQuestInstances Instances, DaggerfallQuestSourceDefinition Source) StartedSource(
        DaggerfallDefinitions definitions, Func<DaggerfallQuestTaskProgram, bool> matches)
    {
        foreach (DaggerfallQuestSourceDefinition source in definitions.QuestSources.Quests.Values.Where(source => source.Disposition == DaggerfallQuestDisposition.Compiled))
        {
            DaggerfallQuestTaskProgram program = Program(source);
            if (!matches(program)) continue;
            DaggerfallQuestInstances instances = Instances(definitions);
            try
            {
                instances.Start(new("quest", source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], []));
                return (instances, source);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                // The normalized corpus correctly rejects sources with unresolved authored bindings, and a
                // travel-derived clock needs the live session's bound place resources and route calculator,
                // which a bare lifecycle fixture does not carry. Continue to an admitted source.
            }
        }
        throw new Xunit.Sdk.XunitException("The normalized corpus contained no startable matching quest source.");
    }

    private static DaggerfallQuestSourceDefinition[] StartableSources(DaggerfallDefinitions definitions, int count)
    {
        List<DaggerfallQuestSourceDefinition> sources = [];
        foreach (DaggerfallQuestSourceDefinition source in definitions.QuestSources.Quests.Values.Where(source => source.Disposition == DaggerfallQuestDisposition.Compiled))
        {
            try
            {
                Instances(definitions)
                    .Start(new("probe", source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], []));
                sources.Add(source);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                // A source with unresolved normalized bindings, or one whose clock needs the live session's
                // travel owners, is not an eligible lifecycle test fixture.
            }
            if (sources.Count == count) return [.. sources];
        }
        throw new Xunit.Sdk.XunitException($"The normalized corpus contained fewer than {count} startable quest sources.");
    }

    private static DaggerfallDefinitions Definitions()
    {
        return TestPayload.Definitions;
    }

    private static DaggerfallDefinitions DefinitionsWithLifecycleFixtures(bool hasRewardMessage = true)
    {
        JsonObject root = JsonNode.Parse(File.ReadAllText(BasePayloadPath()))!.AsObject();
        JsonArray quests = root["questSources"]!["quests"]!.AsArray();
        quests.Add(JsonNode.Parse("""
            {"name":"startparent","displayName":"Start parent","sourceFile":"startparent.txt","disposition":"compiled","messages":[],"blocks":[{"kind":"headless","firstLine":11,"lines":["start quest independentchild","end quest"],"global":null}],"diagnostics":[]}
            """));
        quests.Add(JsonNode.Parse("""
            {"name":"runparent","displayName":"Run parent","sourceFile":"runparent.txt","disposition":"compiled","messages":[],"blocks":[{"kind":"variable","firstLine":1,"lines":["variable _success_"],"global":null},{"kind":"variable","firstLine":2,"lines":["variable _failure_"],"global":null},{"kind":"headless","firstLine":3,"lines":["run quest runchild then _success_ or _failure_"],"global":null}],"diagnostics":[]}
            """));
        quests.Add(JsonNode.Parse("""
            {"name":"independentchild","displayName":"Independent child","sourceFile":"independentchild.txt","disposition":"compiled","messages":[],"blocks":[{"kind":"variable","firstLine":1,"lines":["variable _idle_"],"global":null}],"diagnostics":[]}
            """));
        quests.Add(JsonNode.Parse("""
            {"name":"runchild","displayName":"Run child","sourceFile":"runchild.txt","disposition":"compiled","messages":[],"blocks":[{"kind":"variable","firstLine":1,"lines":["variable _idle_"],"global":null}],"diagnostics":[]}
            """));
        quests.Add(JsonNode.Parse("""
            {"name":"endparent","displayName":"End parent","sourceFile":"endparent.txt","disposition":"compiled","messages":[],"blocks":[{"kind":"variable","firstLine":1,"lines":["variable _success_"],"global":null},{"kind":"variable","firstLine":2,"lines":["variable _failure_"],"global":null},{"kind":"headless","firstLine":3,"lines":["run quest endchild then _success_ or _failure_"],"global":null}],"diagnostics":[]}
            """));
        quests.Add(JsonNode.Parse("""
            {"name":"endchild","displayName":"End child","sourceFile":"endchild.txt","disposition":"compiled","messages":[],"blocks":[{"kind":"headless","firstLine":1,"lines":["end quest"],"global":null}],"diagnostics":[]}
            """));
        quests.Add(JsonNode.Parse("""
            {"name":"trainquest","displayName":"Train quest","sourceFile":"trainquest.txt","disposition":"compiled","messages":[{"id":1004,"firstLine":1,"lines":["Complete."]}],"blocks":[{"kind":"headless","firstLine":1,"lines":["train pc LongBlade"],"global":null}],"diagnostics":[]}
            """));
        quests.Add(JsonNode.Parse("""
            {"name":"rearmtraining","displayName":"Rearm training","sourceFile":"rearmtraining.txt","disposition":"compiled","messages":[{"id":1004,"firstLine":1,"lines":["Complete."]}],"blocks":[{"kind":"variable","firstLine":1,"lines":["variable _stop_"],"global":null},{"kind":"task","firstLine":2,"lines":["until _stop_ performed:","start task _source_"],"global":null},{"kind":"task","firstLine":4,"lines":["_source_ task:","train pc LongBlade","clear _source_"],"global":null}],"diagnostics":[]}
            """));
        if (!hasRewardMessage)
            quests.Single(quest => quest!["sourceFile"]!.GetValue<string>() == "trainquest.txt")!["messages"] = new JsonArray();
        return DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()));
    }

    private static string BasePayloadPath()
    {
        DirectoryInfo? root = new(Environment.CurrentDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) root = root.Parent;
        return Path.Combine(root!.FullName, "content/worldrpg/payloads/daggerfall.base.json");
    }

    private static DaggerfallQuestSourceDefinition Source(params DaggerfallQuestBlockDefinition[] blocks) =>
        new("test", string.Empty, "test.txt", DaggerfallQuestDisposition.Compiled, [], blocks, []);

    private static DaggerfallQuestMessages Messages(DaggerfallQuestSourceDefinition source, IReadOnlyDictionary<string, int>? staticMessages = null) => new(
        new DaggerfallTextResolver(new DaggerfallTextSet(new Dictionary<DaggerfallTextKey, DaggerfallTextValue>(), [], [])),
        new Dictionary<string, DaggerfallQuestSourceDefinition>(StringComparer.Ordinal) { [source.SourceFile] = source }, staticMessages);

    private static DaggerfallQuestBlockDefinition Block(string kind, int line, params string[] lines) => new(kind, line, lines, null);

    private sealed class LifecycleFake(string? pick = null, string? childBranch = null) : IDaggerfallQuestTaskLifecycle
    {
        internal string? ChildBranch { get; set; } = childBranch;
        public bool IsLevelCompleted(int minimum) => false;
        public bool IsAttributeAtLeast(string attribute, int minimum) => false;
        public bool IsSkillAtLeast(string skill, int minimum) => false;
        public void Train(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation) { }
        public string Pick(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation, int operationIndex, DaggerfallQuestTaskRuntimeState state)
        {
            string selected = pick ?? operation.Targets[0];
            state.OperationState[operationIndex] = state.OperationState[operationIndex] with { PickedTarget = selected };
            return selected;
        }

        public string? RunChild(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskDefinition task, DaggerfallQuestTaskOperation operation, int operationIndex, DaggerfallQuestTaskRuntimeState state)
        {
            state.OperationState[operationIndex] = state.OperationState[operationIndex] with { ChildInstanceId = "child:1" };
            return ChildBranch;
        }

        public void Schedule(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation) { }
    }

    private class RandomMinimum : DispatchProxy
    {
        internal static IRandomService Create() => DispatchProxy.Create<IRandomService, RandomMinimum>();
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }
}
