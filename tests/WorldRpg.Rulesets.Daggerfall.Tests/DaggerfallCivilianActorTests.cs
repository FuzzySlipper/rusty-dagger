using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCivilianActorTests
{
    [Fact]
    public void Civilian_materialization_creates_the_canonical_actor_and_empty_inventory()
    {
        using ActorsState actors = new();
        InventoryStore inventoryStore = new();
        DaggerfallMechanicsState mechanics = new();
        DaggerfallNpc npc = new(
            4_000_000,
            DaggerfallNpcKind.Civilian,
            string.Empty,
            new DaggerfallNpcSite(17, "Daggerfall", string.Empty),
            new DaggerfallNpcAppearance("Breton", "Female", 210, 4, 3, 0),
            "civilian",
            ["talk"],
            DaggerfallNpcPresence.Active,
            null, null, null);

        ActorState actor = DaggerActorFactory.CreateCivilianActor(
            mechanics,
            actors,
            inventoryStore,
            npc,
            new ActorPose(new WorldPoint(2f, 0f, -3f), 0.25f));

        Assert.Equal(npc.DurableId, actor.DurableId);
        Assert.Equal(DaggerfallActorKinds.Civilian, DaggerActorFactory.CivilianDefinition(npc.DurableId).Kind);
        Assert.False(actor.IsDefeated);
        Assert.Empty(actor.Inventory.View().Stacks);
        Assert.Empty(actor.Inventory.View().UniqueItems);
        Assert.Equal(new WorldPoint(2f, 0f, -3f), actor.Position);
    }
}
