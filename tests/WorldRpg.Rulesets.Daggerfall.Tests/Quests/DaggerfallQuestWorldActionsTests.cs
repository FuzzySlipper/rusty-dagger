using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// What a quest does to the world, built without the session over a site directory and notebook of its own; the site
/// catalog, the current profile and the crossing's relocation are plain inputs the test watches.
/// </summary>
public sealed class DaggerfallQuestWorldActionsTests
{
    private sealed class Stage
    {
        internal DaggerfallDefinitions Definitions { get; } = TestPayload.Definitions;
        internal DaggerfallSiteContext Sites { get; }
        internal DaggerfallBookNotebook Notebook { get; }
        internal DaggerfallSiteProfiles? Profiles { get; set; }
        internal DaggerfallSiteProfile Current { get; }
        internal List<(DaggerfallWorldProfileKey Profile, DaggerfallSiteAnchor Anchor)> Relocated { get; } = [];
        internal int ReturnsCleared { get; private set; }

        internal Stage()
        {
            Sites = new(Definitions.Locations);
            Notebook = new(Definitions, new DaggerfallTextResolver(Definitions.Text));
            // The player stands outside a town that is not the quest's place.
            DaggerfallSiteProfile source = TestSessions.MediaInputs();
            Current = new(new ProjectFacts(new WorldPoint(1f, 1f, 1f), new Dictionary<long, AuthoredActor>()), source.Geometry, source.WorldAppearance,
                source.InitialLook, source.Materials, source.ActorSprites, site: Sites.Records.First(site => site.Exterior is not null).Id,
                profileKind: DaggerfallWorldProfileKind.Exterior, logicalProfileId: "elsewhere");
        }

        internal DaggerfallQuestWorldActions Actions() => new(Sites, Definitions, Notebook, () => Profiles, () => Current,
            (profile, anchor) => { Relocated.Add((profile, anchor)); return true; }, () => ReturnsCleared++);
    }

    private static DaggerfallQuestRuntimeInstance At(DaggerfallSiteRecord site)
    {
        DaggerfallQuestRuntimeInstance instance = DaggerfallQuestEnvironmentTests.Instance();
        instance.Resources = [new("destination", DaggerfallQuestResourceBinding.Place(new(site.Id.Region, site.Id.Index))
            with { PlaceSelection = new(DaggerfallWorldProfileKind.Dungeon, site.MapId) })];
        return instance;
    }

    private static DaggerfallQuestTaskOperation Compiled(string action) =>
        Assert.Single(Assert.Single(DaggerfallQuestTaskCompiler.Compile(new DaggerfallQuestSourceDefinition("world", "", "world.txt",
            DaggerfallQuestDisposition.Compiled, [], [new("headless", 1, [action], null)], [])).Tasks).Operations);

    [Fact]
    public void Revealing_a_place_discovers_it_and_reading_the_map_notes_it()
    {
        Stage stage = new();
        DaggerfallSiteRecord site = stage.Sites.Records.First(value => !stage.Sites.IsDiscovered(value.Id));

        Assert.True(stage.Actions().Apply(At(site), Compiled("reveal _destination_ readmap")));

        Assert.True(stage.Sites.IsDiscovered(site.Id));
        Assert.Contains(site.Name, Assert.Single(stage.Notebook.Notes).Text);
    }

    [Fact]
    public void A_world_update_needs_the_site_catalog_and_a_teleport_an_admitted_profile()
    {
        Stage stage = new();
        DaggerfallSiteRecord site = stage.Sites.Records.First(value => value.DungeonBlocks.Count > 0 && value.Id != stage.Current.Site);
        DaggerfallQuestTaskOperation update = new(DaggerfallQuestTaskOperationKind.WorldUpdate, 1, "worldupdate", [], [], null,
            WorldUpdate: new("locationnew", site.Id.Region, null, site.Name, "variant"));

        Assert.Contains("site catalog", Assert.Throws<NotSupportedException>(() => stage.Actions().Apply(At(site), update)).Message);
        Assert.Contains("no admitted world profile", Assert.Throws<NotSupportedException>(() =>
            stage.Actions().Apply(At(site), Compiled("teleport pc to _destination_"))).Message);
        Assert.Empty(stage.Relocated);
        Assert.Equal(0, stage.ReturnsCleared);
    }
}
