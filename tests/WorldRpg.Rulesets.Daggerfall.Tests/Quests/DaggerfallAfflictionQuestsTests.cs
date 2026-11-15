using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>What the curses ask of the quests, answered by the quest instances with no session.</summary>
public sealed class DaggerfallAfflictionQuestsTests
{
    [Fact]
    public void A_cure_quest_offered_runs_until_failed()
    {
        DaggerfallDefinitions definitions = QuestWorldAdmissionTests.Definitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player,
            DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
        using ActorsState actors = new();
        actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health", DaggerfallActorFactory.PlayerCapabilities);
        DaggerfallQuestInstances quests = new(definitions, RandomMinimum.Create());
        DaggerfallSocialState social = new(definitions.Factions);
        quests.BindSocial(social, new WorldRpg.Rulesets.Daggerfall.Crime.DaggerfallCrimeState(), () => 17);
        DaggerfallAfflictionQuests asked = new(quests, definitions, actors, social,
            new DaggerfallCharacterState(definitions, stats, player));

        Assert.False(asked.Active("world-test.txt"));
        Assert.False(asked.Started("cure"));
        Assert.True(asked.Offer("cure", "world-test.txt", 150));
        Assert.True(asked.Active("world-test.txt"));
        Assert.True(asked.Started("cure"));
        Assert.Equal(150, Assert.Single(quests.All, quest => quest.InstanceId == "cure").FactionId);

        asked.FailActive("other", "not this one");
        Assert.True(asked.Active("world-test.txt"));
        asked.FailActive("WORLD", "cured");
        Assert.False(asked.Active("world-test.txt"));
        Assert.True(asked.Started("cure"));
    }
}
