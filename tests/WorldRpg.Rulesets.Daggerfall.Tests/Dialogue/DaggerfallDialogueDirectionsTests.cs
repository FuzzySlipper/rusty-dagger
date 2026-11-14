using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The "Where is" directory built without the session: the fixture's site, people and definitions, with the quests'
/// subjects, claims and contacts as plain inputs.
/// </summary>
public sealed class DaggerfallDialogueDirectionsTests
{
    [Fact]
    public void A_quest_subject_is_listed_where_its_place_resolves_and_disclosing_it_discovers_the_site()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSiteContext site = fixture.Session.Site;
        DaggerfallSiteRecord active = site.ActiveSite ?? throw new InvalidOperationException("The directions test needs an admitted site.");
        DaggerfallSiteRecord far = site.Records
            .Where(place => place.Id.Region == active.Id.Region && place.Id != active.Id && !site.IsDiscovered(place.Id))
            .OrderBy(place => place.Id.Index).First();
        DaggerfallDialogueQuestSubject placed = new("q", "place", "The meeting place", [], far.Id, null);
        // A person the conversation cannot point to falls back to their home's site.
        DaggerfallDialogueQuestSubject person = new("q", "person", "Lady Sarah", [987654], far.Id, null);
        DaggerfallDialogueQuestSubject nowhere = new("q", "item", "A lost ring", [], null, null);
        DaggerfallDialogueDirections directions = Directions(fixture, [placed, person, nowhere]);

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
        using ConditionSessionFixture fixture = new();
        DaggerfallSiteContext site = fixture.Session.Site;
        DaggerfallSiteRecord active = site.ActiveSite ?? throw new InvalidOperationException("The directions test needs an admitted site.");
        DaggerfallDialogueDirections directions = Directions(fixture, []);

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
    public void Only_people_the_conversation_can_talk_to_are_listed()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite ?? throw new InvalidOperationException("The directions test needs an admitted site.");
        (int archive, int record) = session.Sites.Projection.Inputs.BillboardSprites.Keys
            .OrderBy(key => key.Item1).ThenBy(key => key.Item2).First();
        long id = session.State.Npcs.RegisterCivilian(
            new DaggerfallNpcSite(site.Id.Region, site.Name, string.Empty),
            new DaggerfallNpcAppearance("Breton", "Female", archive, record, 0, 0), "baker", ["talk"]);
        session.Civilians.Materialize(id, session.State.Actors.Get(2000).Pose);
        session.State.Npcs.Bind(id, session.Sites.ActiveProfile);

        Assert.DoesNotContain(Directions(fixture, [], live: false).Directory(), topic => topic.Id == $"direction:npc:{id}");
        DaggerfallDialogueTopicOption person = Assert.Single(Directions(fixture, []).Directory(), topic => topic.Id == $"direction:npc:{id}");
        Assert.Equal(DaggerfallDialogueTopicGroups.People, person.Category);
        DaggerfallNpc baker = session.State.Npcs.Require(id);
        Assert.Equal($"Where is {baker.DisplayName ?? baker.Role}?", person.Label);
    }

    private static DaggerfallDialogueDirections Directions(ConditionSessionFixture fixture, IReadOnlyList<DaggerfallDialogueQuestSubject> subjects,
        bool live = true)
    {
        DaggerfallSession session = fixture.Session;
        return new(session.Site, session.State.Npcs, fixture.Definitions, session.State.PlayerControl,
            dialogue: () => live ? session.Dialogue : null,
            activeProfile: () => session.Sites.ActiveProfile,
            exteriorSitePosition: point => point,
            questSubjects: () => subjects,
            questClaimsBuilding: (_, _) => false,
            questContacts: _ => []);
    }
}
