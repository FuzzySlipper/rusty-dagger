using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>What a quest does to the world, built without the session over the fixture's site directory, site crossing and a notebook.</summary>
public sealed class DaggerfallQuestWorldActionsTests
{
    [Fact]
    public void Revealing_a_place_discovers_it_with_its_map_note_and_the_teleport_sends_the_player_to_its_marker()
    {
        DaggerfallDefinitions definitions = QuestWorldAdmissionTests.Definitions(secondPlace: true);
        using SanguineRoseSessionTests.Fixture fixture = new(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        DaggerfallSiteProfile castle = QuestWorldAdmissionTests.WithMarker(fixture.Castle);
        fixture.Session.AdmitSiteProfiles(new DaggerfallSiteProfiles([fixture.Inputs, castle]));
        DaggerfallBookNotebook notebook = new(definitions, new DaggerfallTextResolver(definitions.Text));
        DaggerfallQuestWorldActions actions = new(fixture.Session.Site, fixture.Session.Sites, definitions, notebook);
        DaggerfallQuestRuntimeInstance instance = DaggerfallQuestEnvironmentTests.Instance();
        DaggerfallSiteRecord site = definitions.Locations.Records.Single(value => value.Id == castle.Site);
        instance.Resources = [new("destination", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index))
            with { PlaceSelection = new(castle.ProfileKind, site.MapId) })];

        Assert.True(actions.Apply(instance, Compiled("reveal _destination_ readmap")));
        Assert.True(fixture.Session.Site.IsDiscovered(castle.Site!.Value));
        Assert.Contains(site.Name, Assert.Single(notebook.Notes).Text);

        Assert.True(actions.Apply(instance, Compiled("teleport pc to _destination_")));
        Assert.Equal(castle.ProfileKey, fixture.Session.Sites.ActiveProfile);
        Assert.Equal(castle.QuestMarkers[0].Position, fixture.Session.State.PlayerControl.Position);
    }

    private static DaggerfallQuestTaskOperation Compiled(string action) =>
        Assert.Single(Assert.Single(DaggerfallQuestTaskCompiler.Compile(new DaggerfallQuestSourceDefinition("world", "", "world.txt",
            DaggerfallQuestDisposition.Compiled, [], [new("headless", 1, [action], null)], [])).Tasks).Operations);
}
