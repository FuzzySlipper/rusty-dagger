using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Where a quest's resources stand, built without the session over <see cref="SocietyStage"/>'s actors, people and site
/// directory; the exterior frame and the floor are the identity, and the roster's spawning, the ground piles, the bound
/// item's placement, the questor projection and the retained actor's return are delegates the test watches.
/// </summary>
public sealed class DaggerfallQuestPlacementTests
{
    private static readonly DaggerfallSiteMarker Marker = new("quest-marker", DaggerfallSiteMarkerKind.QuestSpawn, new WorldPoint(3f, 0f, 4f));

    private sealed class Watched
    {
        internal readonly List<(string Definition, ActorPose Pose)> Spawned = [];
        internal readonly List<(DaggerfallCreatedItem Item, WorldPoint Position)> Created = [];
        internal readonly List<(DaggerfallQuestResourceBinding Binding, WorldPoint Position)> Placed = [];
        internal readonly List<(long Id, WorldPoint Position)> BroughtOut = [];
        internal long NextActor = 7000;
    }

    private static DaggerfallQuestPlacement Placement(SocietyStage stage, Watched watched) => new(stage.Actors, stage.Npcs, stage.Sites,
        point => point, point => point, (point, _) => point,
        (definition, pose) => { watched.Spawned.Add((definition, pose)); return watched.NextActor++; },
        (item, position) => { watched.Created.Add((item, position)); return new(DaggerfallItemOwner.Ground(41), null, 99UL); },
        (binding, position) => { watched.Placed.Add((binding, position)); return binding; },
        (_, _, _) => throw new InvalidOperationException("No questor is projected in this stage."),
        (id, position) => watched.BroughtOut.Add((id, position)));

    [Fact]
    public void A_foe_spawns_at_its_marker_once_per_head_and_bound_foes_move_there_live_or_kept()
    {
        using SocietyStage stage = new();
        Watched watched = new();
        DaggerfallQuestPlacement placement = Placement(stage, watched);
        DaggerfallSiteProfile profile = TestSessions.MediaInputs();

        DaggerfallQuestResourceBinding? spawned = placement.Place("quest", new("enemy", DaggerfallQuestResourceBinding.Pending())
            { SelectedFoe = new("nymph", 2, false) }, profile, Marker);
        Assert.Equal([7000L, 7001L], spawned!.ActorIds);
        Assert.All(watched.Spawned, spawn => Assert.Equal(("nymph", Marker.Position), (spawn.Definition, spawn.Pose.Position)));

        long live = stage.CityWatch();
        DaggerfallQuestResourceState bound = new("enemy", DaggerfallQuestResourceBinding.Actors(live, 424242)) { SelectedFoe = new("nymph", 2, false) };
        Assert.Same(bound.Binding, placement.Place("quest", bound, profile, Marker));
        Assert.Equal(Marker.Position, stage.Actors.Get(live).Position);
        Assert.Equal([(424242L, Marker.Position)], watched.BroughtOut);
        Assert.Null(placement.Place("quest", bound with { IsHidden = true }, profile, Marker));
    }

    [Fact]
    public void An_unplaced_item_is_created_at_its_marker_and_a_bound_one_placed_again()
    {
        using SocietyStage stage = new();
        Watched watched = new();
        DaggerfallQuestPlacement placement = Placement(stage, watched);
        DaggerfallCreatedItem gift = new DaggerfallItemFactory(stage.Definitions, RandomMinimum.Create())
            .Create(new("MiscItems", "quest.gift", DaggerfallItemOwner.Player, TemplateIndex: 132));

        DaggerfallQuestResourceBinding? created = placement.Place("quest", new("gift", DaggerfallQuestResourceBinding.Pending()) { SelectedItem = gift },
            TestSessions.MediaInputs(), Marker);
        Assert.Equal([99UL], created!.UniqueItemIds);
        Assert.Equal(Marker.Position, Assert.Single(watched.Created).Position);

        DaggerfallQuestResourceBinding bound = DaggerfallQuestResourceBinding.UniqueItem(99);
        Assert.Same(bound, placement.Place("quest", new("gift", bound) { SelectedItem = gift }, TestSessions.MediaInputs(), Marker));
        Assert.Equal((bound, Marker.Position), Assert.Single(watched.Placed));
    }
}
