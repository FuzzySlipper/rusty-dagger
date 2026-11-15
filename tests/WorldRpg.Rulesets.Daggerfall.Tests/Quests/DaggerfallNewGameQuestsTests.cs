using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The bundle's new-game quests, started by the quest instances with no session.</summary>
public sealed class DaggerfallNewGameQuestsTests
{
    [Fact]
    public void Each_new_game_quest_starts_active_under_its_own_new_game_instance()
    {
        DaggerfallQuestInstances quests = new(QuestWorldAdmissionTests.Definitions(), RandomMinimum.Create());

        quests.StartNewGame(["world-test.txt"]);

        DaggerfallQuestInstanceSave started = Assert.Single(quests.All);
        Assert.Equal(("new-game:world-test", "world-test.txt", DaggerfallQuestLifecycle.Active), (started.InstanceId, started.SourceFile, started.Lifecycle));
    }
}
