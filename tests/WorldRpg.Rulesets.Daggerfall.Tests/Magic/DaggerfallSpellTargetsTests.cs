using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Whom a spell can reach, decided alone over the actors and their definitions; delivery is the session's (CastingSessionTests).</summary>
public sealed class DaggerfallSpellTargetsTests
{
    [Fact]
    public void Every_gameplay_active_actor_but_a_static_npc_takes_part_in_spells_and_the_player_always_casts()
    {
        using GuildMakerFixture items = new();
        using ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), items.Stats, "health", DaggerActorFactory.PlayerCapabilities);
        foreach (long id in new long[] { 2, 3, 4 })
            actors.CreateActor(id, new EntityTypeId("actor"), new DaggerfallMechanicsState().CreateStats(
                    items.Definitions.RequireActor(new("player")), DaggerfallPlayerVitals.Initial(items.Definitions.RequireActor(new("player")).Stats,
                        items.Definitions.Catalogs.RequireCareer("class00"))),
                new ActorPose(new WorldPoint(id, 0f, 0f), 0f), "health", DaggerActorFactory.NonPlayerCapabilities);
        DaggerfallActorDefinition npc = items.Definitions.RequireActor(new("player")) with { Kind = DaggerfallActorKinds.StaticNpc };
        HashSet<long> active = [1, 2, 3];
        DaggerfallSpellTargets targets = new(items.Definitions, actors, items.Character, new DaggerfallEffectLifecycle(actors, DaggerfallEffectCatalog.Empty),
            null!, active.Contains, id => id == 3 ? npc : null, () => new Dictionary<long, WorldPoint>(), _ => throw new InvalidOperationException("No query without candidates."));

        Assert.True(targets.IsEligible(2));
        Assert.False(targets.IsEligible(3));
        Assert.False(targets.IsEligible(4));
        Assert.Same(actors.Player.Actor, targets.CastActor(1));
        Assert.NotNull(targets.CastActor(2));
        Assert.Null(targets.CastActor(3));
        Assert.Empty(targets.Area(null, default, excludeCaster: false));
    }
}
