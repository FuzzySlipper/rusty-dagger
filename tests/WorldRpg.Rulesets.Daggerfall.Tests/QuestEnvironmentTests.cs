using System.Text.Json;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.QuestDiseaseTests;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestEnvironmentTests
{
    [Fact]
    public void Season_is_a_live_condition_and_changes_with_the_admitted_calendar()
    {
        using var f = new SanguineRoseSessionTests.Fixture(definitions: Definitions("season winter"));
        Start(f.Session);
        f.Session.State.Quests.Advance(f.Session.State.Variables, new(405, 0, 0, 0, 0, 0));
        Assert.True(Assert.Single(f.Session.State.Quests.All).Tasks[0].IsSet);
        f.Session.State.Quests.Advance(f.Session.State.Variables, new(405, 5, 0, 0, 0, 0));
        Assert.False(Assert.Single(f.Session.State.Quests.All).Tasks[0].IsSet);
    }

    [Theory]
    [InlineData("play song song_unpublished", "MIDI")]
    [InlineData("play video 1", "cinematic")]
    [InlineData("play sound unlisted 0 0", "Unknown quest sound")]
    public void Unavailable_media_is_diagnosed_before_following_actions(string source, string diagnostic)
    {
        using var f = new SanguineRoseSessionTests.Fixture(definitions: Definitions(source, "end quest"));
        Start(f.Session); Advance(f.Session);
        var quest = Assert.Single(f.Session.State.Quests.All);
        Assert.Equal(DaggerfallQuestLifecycle.Active, quest.Lifecycle);
        Assert.All(quest.Tasks[0].OperationCompleted, done => Assert.False(done));
        Assert.Contains(diagnostic, quest.Tasks[0].OperationState[0].UnavailableReason);
        using var restored = f.Restore(); Advance(restored);
        Assert.Equal(quest.Tasks[0].OperationState[0].UnavailableReason, Assert.Single(restored.State.Quests.All).Tasks[0].OperationState[0].UnavailableReason);
    }

    [Fact]
    public void Unavailable_media_leaves_only_its_own_task_unfinished_and_is_diagnosed_once()
    {
        // The donor ticks each task on its own, so one task's missing sound never holds back its siblings:
        // the noisy task stays unfinished with its later action unrun, while the later task keeps running.
        using var f = new SanguineRoseSessionTests.Fixture(definitions: TaskDefinitions(
            ["start task _noisy_", "start task _later_"],
            ["_noisy_ task:", "play sound unpublished_9702 0 0", "start task _after_"],
            ["_later_ task:", "start task _done_"],
            ["_done_ task:"],
            ["_after_ task:"]));
        using var warnings = new WarningCapture("unpublished_9702");
        Start(f.Session);
        for (int tick = 0; tick < 4; tick++) Advance(f.Session);

        var quest = Assert.Single(f.Session.State.Quests.All);
        Assert.Equal(DaggerfallQuestLifecycle.Active, quest.Lifecycle);
        var noisy = quest.Tasks.Single(task => task.Symbol == "noisy");
        Assert.All(noisy.OperationCompleted, done => Assert.False(done));
        Assert.Contains("Unknown quest sound 'unpublished_9702'", noisy.OperationState[0].UnavailableReason);
        Assert.False(quest.Tasks.Single(task => task.Symbol == "after").IsSet);
        Assert.All(quest.Tasks.Single(task => task.Symbol == "later").OperationCompleted, Assert.True);
        Assert.True(quest.Tasks.Single(task => task.Symbol == "done").IsSet);
        Assert.Equal(1, warnings.Count);
    }

    /// <summary>A quest whose first block is its start-up and each further block a task.</summary>
    private static DaggerfallDefinitions TaskDefinitions(string[] headless, params string[][] tasks)
    {
        JsonObject root = TestPayload.Sections("questSources");
        JsonArray blocks = [new JsonObject { ["kind"] = "headless", ["firstLine"] = 1, ["lines"] = JsonSerializer.SerializeToNode(headless), ["global"] = null }];
        for (int index = 0; index < tasks.Length; index++)
            blocks.Add(new JsonObject { ["kind"] = "task", ["firstLine"] = 10 * (index + 1), ["lines"] = JsonSerializer.SerializeToNode(tasks[index]), ["global"] = null });
        root["questSources"]!["quests"]!.AsArray().Add(new JsonObject {
            ["name"] = "quest-actions", ["displayName"] = "", ["sourceFile"] = "quest-actions.txt", ["disposition"] = "compiled",
            ["messages"] = new JsonArray(), ["diagnostics"] = new JsonArray(), ["blocks"] = blocks });
        return TestPayload.WithQuestSections(root);
    }

    /// <summary>Counts the trace warnings that name one token while it is attached.</summary>
    private sealed class WarningCapture : System.Diagnostics.TraceListener
    {
        private readonly string _token;
        private int _count;
        internal WarningCapture(string token) { _token = token; System.Diagnostics.Trace.Listeners.Add(this); }
        internal int Count => Volatile.Read(ref _count);
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { if (message?.Contains(_token, StringComparison.Ordinal) == true) Interlocked.Increment(ref _count); }
        protected override void Dispose(bool disposing) { System.Diagnostics.Trace.Listeners.Remove(this); base.Dispose(disposing); }
    }

    [Fact]
    public void Disabled_video_counts_as_skipped_and_the_task_continues()
    {
        // Owner decision (#9703): turning videos off is a presentation preference, not a quest blocker.
        // The action completes as a skipped video would, with nothing played and nothing unavailable.
        using var f = new SanguineRoseSessionTests.Fixture(definitions: Definitions("play video 1", "end quest"),
            prepareComposition: composition => composition with { VideosEnabled = false });
        Start(f.Session); Advance(f.Session);
        var quest = Assert.Single(f.Session.State.Quests.All);
        Assert.All(quest.Tasks[0].OperationCompleted, Assert.True);
        Assert.Null(quest.Tasks[0].OperationState[0].UnavailableReason);
        Assert.Null(f.Session.Cinematics?.ActiveSource);
    }

    [Fact]
    public void Periodic_sound_retains_count_and_last_play_time_through_save_load()
    {
        var inputs = TestSessions.ReadInputs(TestData.RepositoryRoot);
        var sound = TestPayload.Definitions.QuestSources.Tables.Sounds.Rows.First(row => inputs.Audio.Any(clip => clip.SourceNumericId == row.Id));
        using var f = new SanguineRoseSessionTests.Fixture(definitions: Definitions($"play sound {sound.Name} every 1 minutes 2 times"));
        Start(f.Session); Advance(f.Session);
        f.Session.AdvanceElapsedTime(60);
        Assert.Equal(1, Assert.Single(f.Session.State.Quests.All).Tasks[0].OperationState[0].Sound!.Played);
        using var restored = f.Restore();
        restored.AdvanceElapsedTime(59);
        Assert.Equal(1, Assert.Single(restored.State.Quests.All).Tasks[0].OperationState[0].Sound!.Played);
        restored.AdvanceElapsedTime(1);
        var task = Assert.Single(restored.State.Quests.All).Tasks[0];
        Assert.Equal(2, task.OperationState[0].Sound!.Played);
        Assert.True(task.OperationCompleted[0]);
        restored.AdvanceElapsedTime(60);
        Assert.Equal(2, Assert.Single(restored.State.Quests.All).Tasks[0].OperationState[0].Sound!.Played);
    }
}
