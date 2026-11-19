using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallPeopleStage"/> built without the session: walking civilians in a directory, a player facing
/// -z, and ground everywhere a little above the player's feet. That staged walkers walk is the Kit's wander test.
/// </summary>
public sealed class DaggerfallPeopleStageTests
{
    private const float Ground = .9F;

    [Fact]
    public void Stage_stands_the_nearest_walkers_in_a_line_ahead_on_the_ground_facing_the_player()
    {
        using ActorsState actors = Actors(out PlayerControlState player);
        DaggerfallPeopleStage stage = new(actors, id => id != 13, player, (at, _) => WorldPoint.From(at with { Y = Ground }));

        Assert.True(stage.Stage(3, 6, 4).Succeeded);
        // The three nearest walkers, not the standing civilian (13) nearer still: 6 m ahead (-z), 4 m wide, on the ground.
        Assert.Equal([new Vector3(-2, Ground, -6), new Vector3(0, Ground, -6), new Vector3(2, Ground, -6)],
            new long[] { 10, 11, 12 }.Select(id => actors.Get(id).Position.ToVector()));
        foreach (long id in new long[] { 10, 11, 12 })
        {
            ActorState walker = actors.Get(id);
            Assert.Equal([walker.Position], walker.Wander.Route!);
            // Facing the player at the origin: their forward points back toward +z.
            Assert.True(ActorHeading.Forward(walker.HeadingYawRadians).Z > .9F);
        }
        Assert.Null(actors.Get(13).Wander.Route);
        Assert.Null(actors.Get(14).Wander.Route);
    }

    [Fact]
    public void Cross_walks_each_walker_across_the_view_in_its_own_row_each_the_other_way_to_the_last()
    {
        using ActorsState actors = Actors(out PlayerControlState player);
        DaggerfallPeopleStage stage = new(actors, _ => true, player, (at, _) => WorldPoint.From(at with { Y = Ground }));

        Assert.True(stage.Cross(2, 4, 6).Succeeded);
        IReadOnlyList<WorldPoint> first = actors.Get(13).Wander.Route!, second = actors.Get(10).Wander.Route!;
        Assert.Equal([new Vector3(-3, Ground, -4), new Vector3(3, Ground, -4)], first.Select(point => point.ToVector()));
        Assert.Equal([new Vector3(3, Ground, -5.5F), new Vector3(-3, Ground, -5.5F)], second.Select(point => point.ToVector()));
        // Each starts at its route's start, facing along it.
        Assert.Equal(first[0], actors.Get(13).Position);
        Assert.True(ActorHeading.Forward(actors.Get(13).HeadingYawRadians).X > .9F);

        // Unstaged, they wander on their own.
        Assert.Equal("2 walking people wander on their own again.", stage.Unstage().Message);
        Assert.Null(actors.Get(13).Wander.Route);
    }

    [Fact]
    public void Staging_refuses_too_few_walkers_and_ground_it_cannot_stand_on_and_moves_nobody()
    {
        using ActorsState actors = Actors(out PlayerControlState player);
        Vector3[] before = [.. actors.All.Select(actor => actor.Position.ToVector())];

        var tooMany = new DaggerfallPeopleStage(actors, _ => true, player, (at, _) => WorldPoint.From(at)).Stage(6, 6, 4);
        Assert.False(tooMany.Succeeded);
        Assert.Equal("Only 5 walking people are loaded here.", tooMany.Message);

        var blocked = new DaggerfallPeopleStage(actors, _ => true, player, (at, _) => at.X > 0 ? null : WorldPoint.From(at)).Cross(2, 4, 6);
        Assert.False(blocked.Succeeded);
        Assert.Equal("No clear ground for walker 1 4 m ahead; face open ground or come closer.", blocked.Message);
        Assert.Equal(before, actors.All.Select(actor => actor.Position.ToVector()));
        Assert.All(actors.All.Where(actor => actor.Actor.TypeId.Value == DaggerfallActorKinds.Civilian), actor => Assert.Null(actor.Wander.Route));
    }

    /// <summary>A player at the origin facing -z, and five civilians 10 to 30 m off, 13 the nearest.</summary>
    private static ActorsState Actors(out PlayerControlState player)
    {
        ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
        foreach ((long id, float distance) in new[] { (10L, 12F), (11L, 15F), (12L, 20F), (13L, 10F), (14L, 30F) })
        {
            ActorState civilian = actors.CreateActor(id, new EntityTypeId(DaggerfallActorKinds.Civilian), new StatsComponent(),
                new ActorPose(new WorldPoint(distance, 0F, 0F), 0F), "health");
            civilian.Stats.AddTrack(TrackId.Parse("health"), new Track(new Stat(1), 1));
            civilian.Actor.Add(new WanderMemoryComponent());
        }
        player = new PlayerControlState(new WorldPoint(0F, 0F, 0F), 0F, 0F);
        return actors;
    }
}
