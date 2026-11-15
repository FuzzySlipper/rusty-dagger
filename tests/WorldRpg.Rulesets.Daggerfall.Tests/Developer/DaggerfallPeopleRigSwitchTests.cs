using System.Text.Json;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallPeopleRigSwitch"/> built without the session: the art set's people rig over a recording Engine
/// parts-rig service and an empty actor directory. What the figure draws walking through a town is
/// <c>DaggerfallPopulationSessionTests</c>' end-to-end case.
/// </summary>
public sealed class DaggerfallPeopleRigSwitchTests
{
    [Fact]
    public void A_view_set_loads_the_figure_and_off_returns_people_to_their_own_art()
    {
        PartsRigsRecorder rigs = PartsRigsRecorder.Create();
        DaggerfallPeopleRig rig = PeopleRig();
        using DaggerfallPeopleRigSwitch peopleRigs = new(rig, () => rigs.Service, new ActorsState());

        // Off before any use answers, and makes no crowd.
        Assert.Equal("Walking people show their own art again.", peopleRigs.Rig("off").Message);
        Assert.Null(peopleRigs.Crowd);

        var unknown = peopleRigs.Rig("6");
        Assert.False(unknown.Succeeded);
        Assert.Equal($"People rig {rig.Name} has no view set '6'; it has {string.Join(", ", rig.Directions.Keys.Order(StringComparer.Ordinal))}, or off.", unknown.Message);
        Assert.Null(peopleRigs.Crowd);

        var four = peopleRigs.Rig("4");
        Assert.True(four.Succeeded);
        Assert.Equal($"Walking people draw as {rig.Name} seen from 4 directions; 0 walk here now.", four.Message);
        Assert.NotNull(peopleRigs.Crowd!.Figure);
        Assert.Equal(["front", "side", "back"], rigs.Loaded.Select(path => Path.GetFileName(Path.GetDirectoryName(path))));

        Assert.True(peopleRigs.Rig("off").Succeeded);
        Assert.Null(peopleRigs.Crowd.Figure);
    }

    [Fact]
    public void An_art_set_without_a_people_rig_refuses_every_view_set()
    {
        using DaggerfallPeopleRigSwitch peopleRigs = new(null, () => PartsRigsRecorder.Create().Service, new ActorsState());
        var refused = peopleRigs.Rig("4");
        Assert.False(refused.Succeeded);
        Assert.Equal("The art set carries no people rig.", refused.Message);
        Assert.True(peopleRigs.Rig("off").Succeeded);
    }

    private static DaggerfallPeopleRig PeopleRig()
    {
        using JsonDocument payload = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/packs/daggerfall.art-replacements/payload.json")));
        return DaggerfallArtReplacements.Read(System.Text.Encoding.UTF8.GetBytes(
            """{"artReplacements": {"images": [], "peopleRig": """ + payload.RootElement.GetProperty("artReplacements").GetProperty("peopleRig").GetRawText() + "}}"),
            "daggerfall.art-replacements").PeopleRig!;
    }
}
