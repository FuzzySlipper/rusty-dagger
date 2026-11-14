using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Projected questors: bodies at their placements, moved and retired as the site's people change, built without the session.</summary>
public sealed class DaggerfallNpcProjectionTests
{
    [Fact]
    public void Active_placed_questors_of_the_site_are_projected_and_the_rest_retired()
    {
        using CivilianStage stage = new();
        bool loaded = true;
        DaggerfallNpcProjection projection = new(stage.Npcs, stage.Actors, () => stage.Profile, () => stage.Appearance, () => loaded,
            point => WorldPoint.From(point.ToVector() + CivilianStage.Offset));
        DaggerfallNpcAppearance look = stage.Look;
        long questor = stage.Npcs.RegisterStable(DaggerfallNpcKind.Questor, "questor", stage.NpcSite, look, "quest person", ["talk"]);
        stage.Npcs.Place(questor, stage.Profile.ProfileKey, new WorldPoint(1f, 0f, 2f));
        // An unplaced questor stands nowhere, so it has no body.
        _ = stage.Npcs.RegisterStable(DaggerfallNpcKind.Questor, "unplaced", stage.NpcSite, look, "quest person", ["talk"]);

        projection.Reconcile();
        DaggerfallNpcView view = Assert.Single(projection.Views());
        Assert.Equal(new DaggerfallNpcView(questor, new WorldPoint(101f, 0f, 2f)), view);

        // A body follows its placement on the next reconcile, and leaves when the town is unloaded.
        stage.Npcs.Place(questor, stage.Profile.ProfileKey, new WorldPoint(5f, 0f, 2f));
        projection.Reconcile();
        Assert.Equal(new WorldPoint(105f, 0f, 2f), Assert.Single(projection.Views()).Position);
        loaded = false;
        projection.Reconcile();
        Assert.Empty(projection.Views());
    }
}
