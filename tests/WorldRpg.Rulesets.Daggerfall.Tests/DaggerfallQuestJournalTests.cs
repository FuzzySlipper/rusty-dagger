using System.Text.Json;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The quest journal's active pages, grouped by quest, and its bounded finished-quest history.</summary>
public sealed class DaggerfallQuestJournalTests
{
    private static readonly DaggerfallDefinitions Definitions = JournalDefinitions();

    [Fact]
    public void Active_journal_groups_entries_by_quest_with_title_and_the_deadline_its_log_names()
    {
        DaggerfallQuestInstances instances = Instances();
        instances.Start(new("timed", "timed.txt", "timed", DaggerfallQuestLifecycle.Active, null, [], []));
        instances.Start(new("plain", "plain.txt", "plain", DaggerfallQuestLifecycle.Active, null, [], []));
        instances.Advance(Variables(), DaggerfallCalendar.Start);

        DaggerfallQuestJournalPresentation journal = instances.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty, DaggerfallCalendar.Start).Journal;

        Assert.Equal(["timed", "plain"], journal.Active.Select(group => group.InstanceId));
        DaggerfallQuestJournalGroup timed = journal.Active[0];
        Assert.Equal("Timed errand", timed.Title);
        Assert.Equal(["Bring the ring within 2 days.", "The ring is in the crypt."], timed.Entries.Select(entry => entry.Text));
        // The unnamed shorter clock is running too, but only the clock the log names is the player's deadline.
        DaggerfallCalendar due = DaggerfallCalendar.FromAbsoluteSeconds(DaggerfallCalendar.Start.ToAbsoluteSeconds() + 2 * DaggerfallCalendar.SecondsPerDay);
        Assert.Equal($"Due by {due.DescribeDate()} at {due.DescribeTime()} (2 days left)", timed.Deadline);
        DaggerfallQuestJournalGroup plain = journal.Active[1];
        Assert.Equal("Quest", plain.Title); // The source states no display name.
        Assert.Null(plain.Deadline);
        Assert.Equal(["Speak to the smith."], plain.Entries.Select(entry => entry.Text));
        Assert.Empty(journal.Finished);
    }

    [Fact]
    public void Finished_record_keeps_title_outcome_and_day_through_save_after_the_runtime_expires()
    {
        DaggerfallQuestInstances instances = Instances();
        DaggerfallVariableStore variables = Variables();
        instances.Start(new("short", "short.txt", "short", DaggerfallQuestLifecycle.Active, null, [], []));
        instances.Start(new("silent", "silent.txt", "silent", DaggerfallQuestLifecycle.Active, null, [], []));
        DaggerfallCalendar finishedAt = DaggerfallCalendar.Start.Advance(3 * DaggerfallCalendar.SecondsPerDay, out _);
        for (int pass = 0; pass < 4; pass++) instances.Advance(variables, finishedAt);

        // Like the donor notebook, only a quest that logged something leaves a finished record.
        DaggerfallFinishedQuestSave record = Assert.Single(instances.Finished);
        Assert.Equal(new DaggerfallFinishedQuestSave("short", "short.txt", false, finishedAt.ToAbsoluteSeconds()), record);

        instances.Advance(variables, finishedAt.Advance(8 * DaggerfallCalendar.SecondsPerDay, out _));
        Assert.Empty(instances.All);
        DaggerfallQuestInstances restored = Instances();
        restored.Restore(RoundTrip(instances.Capture()));

        Assert.Equal([record], restored.Finished);
        DaggerfallQuestJournalPresentation journal = restored.ReadPresentation(
            _ => throw new Exception("A finished journal must not need its runtime."), DaggerfallCalendar.Start).Journal;
        Assert.Empty(journal.Active);
        DaggerfallFinishedQuestJournalGroup finished = Assert.Single(journal.Finished);
        Assert.Equal(("short", "Short errand", false, $"Ended on {finishedAt.DescribeDate()}"),
            (finished.InstanceId, finished.Title, finished.Succeeded, finished.Status));
        Assert.Equal(["The errand is done."], finished.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void Finished_history_is_bounded_and_drops_the_oldest_record_with_its_journal()
    {
        DaggerfallQuestInstances instances = Instances();
        DaggerfallVariableStore variables = Variables();
        int count = DaggerfallQuestInstances.FinishedQuestCapacity + 1;
        for (int index = 0; index < count; index++)
        {
            instances.Start(new($"short:{index:000}", "short.txt", "short", DaggerfallQuestLifecycle.Active, null, [], []));
            for (int pass = 0; pass < 4; pass++) instances.Advance(variables, DaggerfallCalendar.Start.Advance(index * 60L, out _));
        }

        Assert.Equal(DaggerfallQuestInstances.FinishedQuestCapacity, instances.Finished.Count);
        Assert.Equal("short:001", instances.Finished[0].InstanceId);
        Assert.Equal($"short:{count - 1:000}", instances.Finished[^1].InstanceId);
        Assert.DoesNotContain(instances.Messages.Journal, entry => entry.InstanceId == "short:000");
        DaggerfallQuestInstances restored = Instances();
        restored.Restore(RoundTrip(instances.Capture()));
        Assert.Equal(instances.Finished, restored.Finished);
    }

    [Fact]
    public void Restore_rejects_finished_history_that_disagrees_with_its_retained_journal()
    {
        DaggerfallQuestInstances instances = Instances();
        instances.Start(new("short", "short.txt", "short", DaggerfallQuestLifecycle.Active, null, [], []));
        for (int pass = 0; pass < 4; pass++) instances.Advance(Variables(), DaggerfallCalendar.Start);
        DaggerfallQuestInstancesSave saved = RoundTrip(instances.Capture());
        DaggerfallFinishedQuestSave record = Assert.Single(saved.Finished);

        Assert.Throws<ArgumentException>(() => Instances().Restore(saved with { Finished = [] }));
        Assert.Throws<ArgumentException>(() => Instances().Restore(saved with { Finished = [record, record] }));
        Assert.Throws<ArgumentException>(() => Instances().Restore(saved with { Finished = [record with { SourceFile = "missing.txt" }] }));
        Assert.Throws<ArgumentException>(() => Instances().Restore(saved with { Finished = [record, record with { InstanceId = "no-journal" }] }));
    }

    private static DaggerfallQuestInstances Instances() => new(Definitions, RandomMinimum.Create());

    private static DaggerfallVariableStore Variables() => new(new Dictionary<string, int>(StringComparer.Ordinal));

    private static DaggerfallQuestInstancesSave RoundTrip(DaggerfallQuestInstancesSave saved) =>
        (DaggerfallQuestInstancesSave)JsonSerializer.Deserialize(
            JsonSerializer.Serialize(saved, typeof(DaggerfallQuestInstancesSave), DaggerfallSaveJsonContext.Default),
            typeof(DaggerfallQuestInstancesSave), DaggerfallSaveJsonContext.Default)!;

    private static DaggerfallDefinitions JournalDefinitions()
    {
        JsonObject root = TestPayload.Sections("questSources");
        JsonArray quests = root["questSources"]!["quests"]!.AsArray();
        quests.Add(JsonNode.Parse("""
            {"name":"timed","displayName":"Timed errand","sourceFile":"timed.txt","disposition":"compiled",
             "messages":[{"id":10,"firstLine":1,"lines":["Bring the ring within =limit_ days."]},{"id":11,"firstLine":2,"lines":["The ring is in the crypt."]}],
             "blocks":[{"kind":"clock","firstLine":1,"lines":["clock _limit_ 2.00:00"],"global":null},
                       {"kind":"clock","firstLine":2,"lines":["clock _hidden_ 1.00:00"],"global":null},
                       {"kind":"task","firstLine":3,"lines":["_limit_ task:","end quest"],"global":null},
                       {"kind":"task","firstLine":5,"lines":["_hidden_ task:","end quest"],"global":null},
                       {"kind":"headless","firstLine":7,"lines":["log 10 step 0","log 11 step 1","start timer _limit_","start timer _hidden_"],"global":null}],"diagnostics":[]}
            """));
        quests.Add(JsonNode.Parse("""
            {"name":"plain","displayName":"","sourceFile":"plain.txt","disposition":"compiled",
             "messages":[{"id":10,"firstLine":1,"lines":["Speak to the smith."]}],
             "blocks":[{"kind":"clock","firstLine":1,"lines":["clock _limit_ 1.00:00"],"global":null},
                       {"kind":"task","firstLine":2,"lines":["_limit_ task:","end quest"],"global":null},
                       {"kind":"headless","firstLine":4,"lines":["log 10 step 0","start timer _limit_"],"global":null}],"diagnostics":[]}
            """));
        quests.Add(JsonNode.Parse("""
            {"name":"short","displayName":"Short errand","sourceFile":"short.txt","disposition":"compiled",
             "messages":[{"id":10,"firstLine":1,"lines":["The errand is done."]}],
             "blocks":[{"kind":"headless","firstLine":1,"lines":["log 10 step 0","end quest"],"global":null}],"diagnostics":[]}
            """));
        quests.Add(JsonNode.Parse("""
            {"name":"silent","displayName":"Silent errand","sourceFile":"silent.txt","disposition":"compiled",
             "messages":[],
             "blocks":[{"kind":"headless","firstLine":1,"lines":["end quest"],"global":null}],"diagnostics":[]}
            """));
        return TestPayload.WithQuestSections(root.AsObject());
    }
}
