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
    public void Disabled_video_records_unavailable_without_completing_the_action()
    {
        using var f = new SanguineRoseSessionTests.Fixture(definitions: Definitions("play video 1", "end quest"),
            prepareComposition: composition => composition with { VideosEnabled = false });
        Start(f.Session); Advance(f.Session);
        var task = Assert.Single(f.Session.State.Quests.All).Tasks[0];
        Assert.Contains("disabled", task.OperationState[0].UnavailableReason);
        Assert.All(task.OperationCompleted, Assert.False);
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
