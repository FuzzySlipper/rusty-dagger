using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The "Where is" directory built without the session, over <see cref="DialogueStage"/>: the stage's site context,
/// people and definitions, with the quests' subjects as a plain input.
/// </summary>
public sealed class DaggerfallDialogueDirectionsTests
{
    [Fact]
    public void A_quest_subject_is_listed_where_its_place_resolves_and_disclosing_it_discovers_the_site()
    {
        using DialogueStage stage = new();
        DaggerfallSiteContext site = stage.Society.Sites;
        DaggerfallSiteRecord active = site.ActiveSite ?? throw new InvalidOperationException("The stage stands at a site.");
        DaggerfallSiteRecord far = site.Records
            .Where(place => place.Id.Region == active.Id.Region && place.Id != active.Id && !site.IsDiscovered(place.Id))
            .OrderBy(place => place.Id.Index).First();
        DaggerfallDialogueQuestSubject placed = new("q", "place", "The meeting place", [], far.Id, null);
        // A person the conversation cannot point to falls back to their home's site.
        DaggerfallDialogueQuestSubject person = new("q", "person", "Lady Sarah", [987654], far.Id, null);
        DaggerfallDialogueQuestSubject nowhere = new("q", "item", "A lost ring", [], null, null);
        DaggerfallDialogueDirections directions = stage.Directions(stage.Conversation(), [placed, person, nowhere]);

        IReadOnlyList<DaggerfallDialogueTopicOption> directory = directions.Directory();
        DaggerfallDialogueTopicOption listed = Assert.Single(directory, topic => topic.Id == "direction:quest:q:place");
        Assert.Equal("Where is The meeting place?", listed.Label);
        Assert.Equal(DaggerfallDialogueTopicGroups.QuestPlaces, listed.Category);
        Assert.Contains(directory, topic => topic.Id == "direction:quest:q:person" && topic.Label == "Where is Lady Sarah?");
        Assert.DoesNotContain(directory, topic => topic.Id == "direction:quest:q:item");

        DaggerfallDialogueDestination answer = Assert.IsType<DaggerfallDialogueDestination>(directions.Resolve("direction:quest:q:place"));
        Assert.Equal("The meeting place", answer.Name);
        Assert.Equal("on the map", answer.Hint);
        Assert.False(answer.Known);

        Assert.True(directions.Disclose("direction:quest:q:place"));
        Assert.True(site.IsDiscovered(far.Id));
        Assert.True(Assert.IsType<DaggerfallDialogueDestination>(directions.Resolve("direction:quest:q:place")).Known);
    }

    [Fact]
    public void The_regions_other_sites_are_listed_and_a_site_elsewhere_is_neither_answered_nor_disclosed()
    {
        using DialogueStage stage = new();
        DaggerfallSiteContext site = stage.Society.Sites;
        DaggerfallSiteRecord active = site.ActiveSite ?? throw new InvalidOperationException("The stage stands at a site.");
        DaggerfallDialogueDirections directions = stage.Directions(stage.Conversation());

        DaggerfallDialogueTopicOption[] elsewhere = [.. directions.Directory().Where(topic => topic.Id.StartsWith("direction:site:", StringComparison.Ordinal))];
        Assert.Equal(site.Records.Count(place => place.Id.Region == active.Id.Region && place.Id != active.Id), elsewhere.Length);
        Assert.All(elsewhere, topic => Assert.Equal(DaggerfallDialogueTopicGroups.Elsewhere, topic.Category));
        Assert.DoesNotContain(elsewhere, topic => topic.Id == $"direction:site:{active.Id.Region}:{active.Id.Index}");

        DaggerfallSiteRecord other = site.Records.First(place => place.Id.Region != active.Id.Region);
        string target = $"direction:site:{other.Id.Region}:{other.Id.Index}";
        bool discovered = site.IsDiscovered(other.Id);
        Assert.Null(directions.Resolve(target));
        Assert.False(directions.Disclose(target));
        Assert.Equal(discovered, site.IsDiscovered(other.Id));
    }

    [Fact]
    public void Only_people_the_conversation_can_talk_to_are_listed_and_a_person_is_pointed_to_here()
    {
        using DialogueStage stage = new();
        long baker = stage.Talker();
        long mute = stage.Society.Civilian("mute");
        DaggerfallDialogueService conversation = stage.Conversation();

        Assert.DoesNotContain(stage.Directions(null).Directory(), topic => topic.Id == $"direction:npc:{baker}");
        IReadOnlyList<DaggerfallDialogueTopicOption> directory = stage.Directions(conversation).Directory();
        DaggerfallDialogueTopicOption person = Assert.Single(directory, topic => topic.Id == $"direction:npc:{baker}");
        Assert.DoesNotContain(directory, topic => topic.Id == $"direction:npc:{mute}");
        Assert.Equal(DaggerfallDialogueTopicGroups.People, person.Category);
        DaggerfallNpc npc = stage.Society.Npcs.Require(baker);
        Assert.Equal($"Where is {npc.DisplayName ?? npc.Role}?", person.Label);

        DaggerfallDialogueDestination answer = Assert.IsType<DaggerfallDialogueDestination>(stage.Directions(conversation).Resolve(person.Id));
        Assert.Equal(npc.DisplayName ?? npc.Role, answer.Name);
        Assert.Equal("here", answer.Hint);
    }
}
