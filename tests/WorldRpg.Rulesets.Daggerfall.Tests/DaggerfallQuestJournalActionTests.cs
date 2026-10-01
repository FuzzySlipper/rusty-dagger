using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed partial class DaggerfallQuestTaskRuntimeTests
{
    [Fact]
    public void Journal_preserves_write_order_across_restore_replaces_by_step_and_removes_only_that_identity()
    {
        var source = Source() with { Messages = [new(10, 1, ["Same text."]), new(20, 2, ["Same text."])] };
        var first = new DaggerfallQuestRuntimeInstance(Runtime(source).Capture() with { InstanceId = "z-first" }, Program(source));
        var second = new DaggerfallQuestRuntimeInstance(Runtime(source).Capture() with { InstanceId = "a-second" }, Program(source));
        var messages = Messages(source);
        messages.Log(first, 10, 9);
        messages.Log(first, 20, 2);
        messages.Log(second, 10, 9);
        Assert.Equal(["quest-journal/z-first/9", "quest-journal/z-first/2", "quest-journal/a-second/9"],
            messages.RenderJournal([first, second]).Select(entry => entry.EntryId));
        var saved = messages.Capture();
        var restored = Messages(source);
        restored.Restore(saved, new Dictionary<string, DaggerfallQuestRuntimeInstance> { [first.InstanceId] = first, [second.InstanceId] = second });
        Assert.Equal(messages.Journal, restored.Journal);
        restored.Log(first, 10, 2); // The donor replaces a step at its newest write position.
        Assert.Equal([9, 9, 2], restored.Journal.Select(entry => entry.Step));
        restored.RemoveLog(first, 9);
        Assert.Equal(["a-second", "z-first"], restored.Journal.Select(entry => entry.InstanceId));
        Assert.Equal([9, 2], restored.Journal.Select(entry => entry.Step));
    }

    [Fact]
    public void Compiled_remove_log_action_removes_its_step_when_messages_have_the_same_text()
    {
        var source = Source(Block("headless", 1, "log 10 9", "log 20 step 2", "remove log step 9")) with
        { Messages = [new(10, 1, ["Same text."]), new(20, 2, ["Same text."])] };
        var runtime = Runtime(source);
        var messages = Messages(source);
        DaggerfallQuestTaskRunner.Advance(runtime, Program(source), new DaggerfallVariableStore(new Dictionary<string, int>()),
            DaggerfallCalendar.Start, messages, new LifecycleFake());
        var remaining = Assert.Single(messages.Journal);
        Assert.Equal(2, remaining.Step);
        Assert.Equal(20, remaining.MessageId);
    }

    [Fact]
    public void Say_numeric_and_static_alias_are_once_per_action_across_rearm_dismiss_and_restore()
    {
        var source = Source(Block("variable", 1, "variable _stop_"),
            Block("task", 2, "until _stop_ performed:", "say 10", "say QuestComplete")) with
        { Messages = [new(10, 1, ["Same text."]), new(1004, 2, ["Same text."])] };
        var program = Program(source);
        var runtime = Runtime(source);
        var variables = new DaggerfallVariableStore(new Dictionary<string, int>());
        var messages = Messages(source, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["QuestComplete"] = 1004 });
        runtime.Tasks.Single(task => task.Kind == DaggerfallQuestTaskKind.PersistUntil).IsSet = true;
        void Advance() => DaggerfallQuestTaskRunner.Advance(runtime, program, variables, DaggerfallCalendar.Start, messages, new LifecycleFake());
        Advance(); Advance();
        Assert.Equal([10, 1004], messages.Deliveries.Select(delivery => delivery.MessageId));
        Assert.Equal(["quest-message:1", "quest-message:2"], messages.Deliveries.Select(delivery => delivery.EntryId));
        Assert.False(messages.Dismiss("wrong-instance", "quest-message:1"));
        Assert.True(messages.Dismiss(runtime.InstanceId, "quest-message:1"));
        Assert.False(messages.Dismiss(runtime.InstanceId, "quest-message:1"));
        var restored = Messages(source, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["QuestComplete"] = 1004 });
        var current = new DaggerfallQuestRuntimeInstance(runtime.Capture(), program);
        restored.Restore(messages.Capture(), new Dictionary<string, DaggerfallQuestRuntimeInstance> { [current.InstanceId] = current });
        DaggerfallQuestTaskRunner.Advance(current, program, variables, DaggerfallCalendar.Start, restored, new LifecycleFake());
        Assert.Equal([1004], restored.Deliveries.Select(delivery => delivery.MessageId));
        restored.Popup(current, 10);
        Assert.Equal("quest-message:3", restored.Deliveries.Last().EntryId);
        Assert.Throws<ArgumentException>(() => restored.Restore(messages.Capture() with { LastDeliveryId = 0 }, new Dictionary<string, DaggerfallQuestRuntimeInstance> { [current.InstanceId] = current }));
    }

    [Fact]
    public void Session_quest_note_uses_existing_personal_notebook_and_dismissed_say_stays_dismissed_after_save()
    {
        var definitions = DefinitionsWithJournalFixture();
        var inputs = TestSessions.ReadInputs(TestData.RepositoryRoot);
        EngineContextFake Engine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            TestSessions.PopulateContent(content, inputs);
            return EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        }
        var identity = GameCompositionResolver.Resolve(TestSessions.FullContent(TestData.RepositoryRoot), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        DaggerfallSessionComposition composition = new(definitions, inputs, DaggerfallTuning.Defaults, identity);
        RulesetSavePayload save;
        string player;
        using (var session = DaggerfallSession.StartNew(Engine().Context, composition))
        {
            player = session.State.Character.Identity.Name;
            session.State.Quests.Start(new("journal-session", "journal.txt", "journal", DaggerfallQuestLifecycle.Active, null, [], [new("giver", "Akorithi")]));
            for (int index = 0; index < 3; index++) session.State.Quests.Advance(session.State.Variables, DaggerfallCalendar.Start);
            Assert.Equal([9, 2], session.State.Quests.Messages.Journal.Select(entry => entry.Step));
            var pending = Assert.Single(session.State.Quests.Messages.Deliveries);
            session.Update(new ProductUpdate(TestSessions.OuterUpdate(1), [TestSessions.Ui(JsonSerializer.Serialize(new
            { action = "quest-dismiss", questInstance = pending.InstanceId, questDelivery = pending.EntryId }))]));
            Assert.Empty(session.State.Quests.Messages.Deliveries);
            save = session.CaptureSave();
            var note = Assert.Single(DaggerfallSavePayload.Read(save).Notebook.Notes);
            Assert.Equal($"{player} wrote for Akorithi.", note.Text);
            Assert.StartsWith("quest-note/journal-session/", note.Id);
        }
        using var restored = DaggerfallSession.Restore(Engine().Context, composition, save);
        for (int index = 0; index < 3; index++) restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        var current = DaggerfallSavePayload.Read(restored.CaptureSave());
        Assert.Empty(current.Quests.Messages.Deliveries);
        Assert.Equal([9, 2], current.Quests.Messages.Journal.Select(entry => entry.Step));
        Assert.Equal($"{player} wrote for Akorithi.", Assert.Single(current.Notebook.Notes).Text);
        restored.State.Quests.Complete("journal-session", "completed");
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start.Advance(7 * 24 * 60 * 60 + 1, out _));
        Assert.Empty(restored.State.Quests.All);
        Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).Notebook.Notes);
    }

    private static DaggerfallDefinitions DefinitionsWithJournalFixture()
    {
        JsonObject root = JsonNode.Parse(TestPayload.CombinedText)!.AsObject();
        root["questSources"]!["quests"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"journal","displayName":"Journal","sourceFile":"journal.txt","disposition":"compiled",
             "messages":[{"id":10,"firstLine":1,"lines":["Same text."]},{"id":20,"firstLine":2,"lines":["Same text."]},{"id":30,"firstLine":3,"lines":["%pcn wrote for _giver_."]}],
             "blocks":[{"kind":"variable","firstLine":1,"lines":["variable _stop_"],"global":null},{"kind":"task","firstLine":2,"lines":["until _stop_ performed:","say 10","journal note 30"],"global":null},{"kind":"headless","firstLine":5,"lines":["log 10 9","log 20 step 2"],"global":null}],"diagnostics":[]}
            """));
        return DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }
}
