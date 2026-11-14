using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallLocalMap"/> built without the session, over <see cref="SocietyStage"/>'s exterior site: the
/// active profile a plain input, and no site profile read on these paths.
/// </summary>
public sealed class DaggerfallLocalMapTests
{
    [Fact]
    public void The_map_draws_only_while_open_and_with_a_position()
    {
        using SocietyStage stage = new();
        DaggerfallLocalMap map = Map(stage, new PresentationState("Ready", 5d));
        Assert.Null(map.ReadOpen());
        map.SetOpen(true);
        // The stage's player has no position, so there is nothing to draw and no profile is read.
        Assert.Null(map.ReadOpen());
        Assert.Null(map.Read());
    }

    [Fact]
    public void A_building_is_chosen_only_on_the_exterior_and_once_discovered()
    {
        using SocietyStage stage = new();
        PresentationState presentation = new("Ready", 5d);
        DaggerfallLocalMap map = Map(stage, presentation);
        map.SelectBuilding(new DaggerfallPlayerUiAction("map-building", Item: "0/0", Region: stage.Site.Id.Region, Destination: stage.Site.Id.Index));
        Assert.Equal("That map building selection is unavailable.", presentation.LastOutcome);

        map.SelectBuilding(new DaggerfallPlayerUiAction("map-building", Item: "0/0/0", Region: stage.Site.Id.Region, Destination: stage.Site.Id.Index));
        Assert.Equal("That building has not been discovered.", presentation.LastOutcome);

        stage.Active = DaggerfallWorldProfileIds.Dungeon(stage.Site.Id);
        map.SelectBuilding(new DaggerfallPlayerUiAction("map-building", Item: "0/0/0", Region: stage.Site.Id.Region, Destination: stage.Site.Id.Index));
        Assert.Equal("That map building selection is unavailable.", presentation.LastOutcome);
    }

    private static DaggerfallLocalMap Map(SocietyStage stage, PresentationState presentation) =>
        new(stage.Player, stage.Sites, new Dictionary<DaggerfallWorldProfileKey, DaggerfallDungeonDiscovery>(),
            () => throw new InvalidOperationException("No site profile is read here."), () => stage.Active, point => point, presentation);
}
