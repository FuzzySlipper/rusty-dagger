using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Materializing and retiring civilians, built without the session.</summary>
public sealed class DaggerfallCiviliansTests
{
    [Fact]
    public void A_civilian_materializes_once_and_its_registry_placement_gives_way_to_the_live_actor()
    {
        using CivilianStage stage = new();
        long id = stage.Register();
        stage.Npcs.Place(id, stage.Profile.ProfileKey, new WorldPoint(1f, 0f, 2f));
        ActorPose pose = new(new WorldPoint(3f, 0f, 4f), .5f);

        Assert.Equal(id, stage.Civilians.Materialize(id, pose));
        Assert.Equal((id, pose), Assert.Single(stage.Materialized));
        Assert.Null(stage.Npcs.Require(id).X);

        // A live actor is the same durable owner: nothing is created again.
        Assert.Equal(id, stage.Civilians.Materialize(id, pose));
        Assert.Single(stage.Materialized);
    }

    [Fact]
    public void Only_a_present_civilian_materializes()
    {
        using CivilianStage stage = new();
        long questor = stage.Npcs.RegisterStable(DaggerfallNpcKind.Questor, "questor", stage.NpcSite, stage.Look, "quest person", ["talk"]);
        Assert.Throws<InvalidOperationException>(() => stage.Civilians.Materialize(questor, default));
        long removed = stage.Register();
        stage.Npcs.SetPresence(removed, DaggerfallNpcPresence.Removed);
        Assert.Throws<InvalidOperationException>(() => stage.Civilians.Materialize(removed, default));
        Assert.Empty(stage.Materialized);
    }

    [Fact]
    public void The_sites_active_civilians_materialize_at_their_saved_places_or_the_fallback()
    {
        using CivilianStage stage = new();
        long placed = stage.Register();
        stage.Npcs.Place(placed, stage.Profile.ProfileKey, new WorldPoint(1f, 0f, 2f));
        long unplaced = stage.Register();
        long hidden = stage.Register();
        stage.Npcs.SetPresence(hidden, DaggerfallNpcPresence.Hidden);
        long elsewhere = stage.Npcs.RegisterCivilian(new(stage.Site.Id.Region + 1, "Elsewhere", string.Empty), stage.Look, "civilian", []);

        WorldPoint fallback = new(9f, 0f, 9f);
        Assert.Equal([placed, unplaced], stage.Civilians.MaterializeAll(fallback));
        Assert.Equal(new WorldPoint(101f, 0f, 2f), stage.Actors.Get(placed).Position);
        Assert.Equal(fallback, stage.Actors.Get(unplaced).Position);
        Assert.False(stage.Actors.TryGet(hidden, out _));
        Assert.False(stage.Actors.TryGet(elsewhere, out _));
        Assert.Empty(stage.Civilians.MaterializeAll(fallback));
    }

    [Fact]
    public void Retiring_a_civilian_retires_its_live_or_retained_actor_and_removes_the_person()
    {
        using CivilianStage stage = new();
        long live = stage.Register();
        stage.Civilians.Materialize(live, default);
        long retained = stage.Register();
        stage.Retained.Add(retained);
        long unknown = stage.Register();

        stage.Civilians.Retire(live);
        stage.Civilians.Retire(retained);
        stage.Civilians.Retire(unknown);

        // A live actor and one no site retains go to the roster's retirement; a retained one leaves its delta.
        Assert.Equal([live, unknown], stage.Retired);
        Assert.Empty(stage.Retained);
        Assert.All(new[] { live, retained, unknown }, id => Assert.Equal(DaggerfallNpcPresence.Removed, stage.Npcs.Require(id).Presence));
    }
}
