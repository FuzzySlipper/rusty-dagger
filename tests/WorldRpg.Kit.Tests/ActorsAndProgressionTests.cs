using Rusty.Engine;
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
        int disposals = 0;
        IMechanicsService mechanics = MechanicsDouble.Create();
        MechanicsEntity playerEntity = new(new MechanicsEntityHandle(1), () => disposals++);
        MechanicsEntity actorEntity = new(new MechanicsEntityHandle(2), () => disposals++);
        using ActorsState actors = new(new PlayerActorState(playerEntity, "health", mechanics), [new ActorState(42, actorEntity, new WorldPoint(1f, 0f, 1f), "health", mechanics)]);

        Assert.True(actors.TryGet(42, out ActorState? actor));
        Assert.Equal(42, actor.EntityId);

        ProgressionState progression = new();
        progression.Award(-4);
        progression.Award(7);
        Assert.Equal(7, progression.Experience);

        actors.Dispose();
        Assert.Equal(2, disposals);
    }

    private class MechanicsDouble : System.Reflection.DispatchProxy
    {
        internal static IMechanicsService Create() => System.Reflection.DispatchProxy.Create<IMechanicsService, MechanicsDouble>();
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args) => throw new NotSupportedException(method?.Name);
    }
}
