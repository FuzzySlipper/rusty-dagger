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
        Assert.Equal(1, progression.Level);
        progression.Award(493);
        progression.AdvanceTo(500, 2);
        Assert.Equal(2, progression.Level);

        actors.Dispose();
        Assert.Throws<ObjectDisposedException>(() => playerMechanics.ReadTrack(TrackId.Parse("health")));
        Assert.Throws<ObjectDisposedException>(() => actorMechanics.ReadTrack(TrackId.Parse("health")));
    }

    [Fact]
    public void Paired_stat_track_routes_reads_and_mutations_without_a_second_track_authority()
    {
        ExactStatDefinition healthMaximum = new(StatId.Parse("health-maximum"), ExactValue.Zero, new ExactValue(100));
        ExactTrackDefinition health = new(
            TrackId.Parse("health"),
            ExactValue.Zero,
            new ExactTrackMaximum.FromStat(healthMaximum.Id));
        ExactStatTrackState pair = new(healthMaximum, new ExactValue(40), Array.Empty<ExactSource>(), health, new ExactValue(25));
        ActorMechanicsState mechanics = new(new EntityId(1), Array.Empty<(ExactStatDefinition, ExactValue)>(), Array.Empty<ExactTrack>(), [pair]);

        Assert.Equal(40, mechanics.ReadStat(healthMaximum.Id).Value.Raw);
        Assert.Equal(25, mechanics.ReadTrack(health.Id).Current.Raw);
        Assert.Equal(15, mechanics.SpendTrack(health.Id, new ExactValue(10)).After.Raw);
        Assert.Equal(35, mechanics.RestoreTrack(health.Id, new ExactValue(20)).After.Raw);
        Assert.Equal(40, mechanics.SetTrack(health.Id, new ExactValue(99), ExactTrackSetPolicy.ClampToBounds).After.Raw);
        Assert.Equal(40, pair.Read().TrackCurrent.Raw);

        Assert.Throws<ArgumentException>(() => new ActorMechanicsState(
            new EntityId(2),
            [(healthMaximum, new ExactValue(40))],
            Array.Empty<ExactTrack>(),
            [pair]));
        Assert.Throws<ArgumentException>(() => new ActorMechanicsState(
            new EntityId(3),
            Array.Empty<(ExactStatDefinition, ExactValue)>(),
            [new ExactTrack(health, new ExactValue(40), new ExactTrackBounds(ExactValue.Zero, new ExactValue(40)))],
            [pair]));
    }

    [Fact]
    public void Paired_stat_track_candidates_are_revision_guarded()
    {
        ExactStatDefinition healthMaximum = new(StatId.Parse("health-maximum"), ExactValue.Zero, new ExactValue(100));
        ExactTrackDefinition health = new(TrackId.Parse("health"), ExactValue.Zero, new ExactTrackMaximum.FromStat(healthMaximum.Id));
        ExactStatTrackState pair = new(healthMaximum, new ExactValue(40), Array.Empty<ExactSource>(), health, new ExactValue(40));
        ExactStatTrackChangeCandidate candidate = pair.PrepareSourceChange(
            pair.Base,
            Array.Empty<ExactSource>(),
            ExactStatTrackCurrentPolicy.PreserveCurrent,
            pair.Revision);

        pair.Spend(new ExactValue(1));

        Assert.Throws<MechanicsException>(() => candidate.Publish());
        Assert.Equal(39, pair.Read().TrackCurrent.Raw);
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
