using System.Text.Json;
using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>How an inactive site's delta keeps and lets go of a detached actor, as plain data.</summary>
public sealed class DaggerfallSiteRuntimeDeltaTests
{
    private static readonly DaggerfallWorldProfileKey Town = new(new DaggerfallSiteId(17, 4), DaggerfallWorldProfileKind.Exterior, "town");
    private static readonly DaggerfallWorldProfileKey Cave = new(new DaggerfallSiteId(17, 9), DaggerfallWorldProfileKind.Dungeon, "cave");

    [Fact]
    public void The_one_delta_that_keeps_an_actor_is_found_and_two_is_refused()
    {
        DaggerfallSiteRuntimeDelta town = Delta([Dynamic(7), Dynamic(8)]);
        Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> deltas = new() { [Town] = town, [Cave] = Delta([Dynamic(9)]) };

        var found = DaggerfallSiteRuntimeDelta.Retaining(deltas, 8) ?? throw new Xunit.Sdk.XunitException("No delta keeps actor 8.");
        Assert.Equal(Town, found.Profile);
        Assert.Same(town, found.Delta);
        Assert.Equal(8, found.Actor.EntityId);
        Assert.Null(DaggerfallSiteRuntimeDelta.Retaining(deltas, 5));
        deltas[Cave] = Delta([Dynamic(8)]);
        Assert.Throws<InvalidOperationException>(() => DaggerfallSiteRuntimeDelta.Retaining(deltas, 8));
    }

    [Fact]
    public void A_delta_lets_go_of_an_actor_with_its_inventory_corpse_and_effects_and_others_drop_only_its_effects()
    {
        DaggerfallSiteRuntimeDelta town = Delta([Dynamic(7), Dynamic(8)]) with
        {
            ActorInventories = [new(7, new([], [], [])), new(8, new([], [], []))],
            Corpses = [Corpse(7), Corpse(8)],
            Effects = [Effect("cast-by-7", caster: 7, target: 8), Effect("on-7", caster: null, target: 7), Effect("8-alone", caster: null, target: 8)],
        };

        DaggerfallSiteRuntimeDelta without = town.WithoutActor(7);
        Assert.Equal([8L], without.DynamicActors.Select(actor => actor.EntityId));
        Assert.Equal([8L], without.ActorInventories.Select(inventory => inventory.EntityId));
        Assert.Equal([8L], without.Corpses.Select(corpse => corpse.ActorId));
        Assert.Equal(["8-alone"], without.Effects.Select(effect => effect.Instance));

        DaggerfallSiteRuntimeDelta? elsewhere = town.WithoutEffectsOf(7);
        Assert.NotNull(elsewhere);
        Assert.Equal(["8-alone"], elsewhere.Effects.Select(effect => effect.Instance));
        Assert.Equal([7L, 8L], elsewhere.DynamicActors.Select(actor => actor.EntityId));
        Assert.Equal(2, elsewhere.Corpses.Length);
        Assert.Null(Delta([Dynamic(9)]).WithoutEffectsOf(7));
    }

    private static DaggerfallSiteRuntimeDelta Delta(DaggerfallDynamicActorSave[] dynamic) => new([], dynamic, [], [], [], []);

    private static DaggerfallDynamicActorSave Dynamic(long id) =>
        new(id, "rat", 0f, 0f, 0f, 0f, DaggerfallStatsSaveBoundary.Capture(TestSessions.HealthyMechanics(), new EntityId((ulong)id)))
        { Level = 1, SpawnDistance = 0, Passive = false };

    private static DaggerfallCorpseSave Corpse(long actor) => new(actor, (ulong)actor + 100, 1, true, true, [], []);

    private static DaggerfallActiveEffectSave Effect(string instance, long? caster, long target)
    {
        using JsonDocument state = JsonDocument.Parse("{}");
        return new(instance, "Test", "test", caster, target, "classic", null, null, null, 1, state.RootElement.Clone());
    }
}
