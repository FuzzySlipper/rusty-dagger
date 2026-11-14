using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>A dungeon's treasure piles and marked enemies as it loads, built without the session.</summary>
public sealed class DaggerfallDungeonPopulationTests
{
    [Fact]
    public void A_dungeon_lays_its_treasure_each_load_and_spawns_its_enemies_at_their_markers_once()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile dungeon = TestSessions.ReadInputs(TestData.RepositoryRoot);
        Assert.Equal(DaggerfallWorldProfileKind.Dungeon, dungeon.ProfileKind);
        Assert.NotEmpty(dungeon.DungeonPopulation.Enemies);
        Assert.NotEmpty(dungeon.DungeonPopulation.Treasures);

        using EntityDirectory entities = new();
        InventoryStore store = new();
        EntityId playerEntity = entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("player"));
        store.RegisterInventory(new InventoryState(playerEntity));
        Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.Concat(definitions.TemplateItems.Values)
            .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
        DaggerfallGroundContainers ground = new(new MechanicsInventoryContainerCoordinator(store, entities, items), new DaggerfallItemInstances(),
            playerEntity, new DurableIdentityAllocator(DurableIdentityKind.Container, 100), dungeon.ProfileKey);

        DaggerfallActorDefinition player = definitions.RequireActor(new("player"));
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player,
            DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
        DaggerfallEncounterRuntime encounters = new(definitions, RandomMinimum.Create());
        List<(string Definition, ActorPose Pose, int SpawnDistance, bool Passive)> spawned = [];
        DaggerfallDungeonPopulation population = new(definitions, RandomMinimum.Create(),
            new DaggerfallUniqueItemAllocator(DaggerfallUniqueItemAllocator.DefaultFirstEntityId), ground,
            position => WorldPoint.From(position.ToVector() - System.Numerics.Vector3.UnitY), encounters,
            (definition, pose, spawnDistance, passive) => spawned.Add((definition, pose, spawnDistance, passive)),
            new DaggerfallCharacterState(definitions, stats, player), () => 3);

        population.Populate(dungeon);
        Assert.Equal(dungeon.DungeonPopulation.Treasures.Count, ground.All.Count);
        // Each pile rests where the floor under its marker is.
        Assert.All(dungeon.DungeonPopulation.Treasures, marker => Assert.Contains(ground.All.Values,
            pile => pile.Position == WorldPoint.From(marker.Position.ToVector() - System.Numerics.Vector3.UnitY)));
        Assert.NotEmpty(spawned);
        Assert.All(spawned, spawn => Assert.Contains(dungeon.DungeonPopulation.Enemies, marker => marker.Position == spawn.Pose.Position
            && marker.SpawnDistance == spawn.SpawnDistance && marker.Passive == spawn.Passive));
        Assert.True(encounters.DungeonPopulated(dungeon.ProfileKey.LogicalId));

        // A second load keeps the same piles and spawns nobody again.
        int first = spawned.Count;
        population.Populate(dungeon);
        Assert.Equal(dungeon.DungeonPopulation.Treasures.Count, ground.All.Count);
        Assert.Equal(first, spawned.Count);
    }
}
