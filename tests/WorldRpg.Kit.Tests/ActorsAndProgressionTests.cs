using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Progression;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class ActorsAndProgressionTests
{
    [Fact]
    public void Actor_collection_owns_bound_entity_lifetimes_and_progression_rejects_negative_awards()
    {
        ActorMechanicsState playerMechanics = CreateMechanics(1);
        ActorMechanicsState actorMechanics = CreateMechanics(2);
        using ActorsState actors = new(
            new PlayerActorState(playerMechanics, "health"),
            [new ActorState(42, actorMechanics, new WorldPoint(1f, 0f, 1f), "health")]);

        Assert.True(actors.TryGet(42, out ActorState? actor));
        Assert.Equal(42, actor.EntityId);

        ProgressionState progression = new();
        progression.Award(-4);
        progression.Award(7);
        Assert.Equal(7, progression.Experience);

        actors.Dispose();
        Assert.Throws<ObjectDisposedException>(() => playerMechanics.ReadTrack(TrackId.Parse("health")));
        Assert.Throws<ObjectDisposedException>(() => actorMechanics.ReadTrack(TrackId.Parse("health")));
    }

    private static ActorMechanicsState CreateMechanics(ulong entityId) => new(
        new EntityId(entityId),
        Array.Empty<(ExactStatDefinition Definition, ExactValue Base)>(),
        [new ExactTrack(
            new ExactTrackDefinition(
                TrackId.Parse("health"),
                ExactValue.Zero,
                new ExactTrackMaximum.Fixed(new ExactValue(100))),
            new ExactValue(100))]);
}
