using System.Reflection;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallLootPopulationTests
{
    [Fact]
    public void Population_shares_the_complete_table_to_seed_conversion_for_new_container_sources()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        IRandomService random = DispatchProxy.Create<IRandomService, CountingRandom>();
        CountingRandom probe = (CountingRandom)(object)random;
        DaggerfallLootPopulation population = new(definitions, random, new DaggerfallUniqueItemAllocator(9000));
        DaggerfallLootPopulationRequest request = new(
            new DaggerfallLootPopulationId("corpse", 2000),
            DaggerfallItemOwner.Corpse(2000),
            "C",
            PlayerLevel: 1,
            Generation: 7,
            Sequence: 11,
            Race: "breton",
            Gender: "male",
            ClothingGroup: "MensClothing");

        DaggerfallLootPopulationResult generated = population.Generate(request);

        Assert.NotEmpty(generated.Seeds);
        Assert.True(probe.Draws > 0);
        DaggerfallLootResult loot = Assert.IsType<DaggerfallLootResult>(generated.Loot);
        Assert.Contains(loot.Drops, drop => drop.SourceCategory == "books");
        Assert.Contains(loot.Drops, drop => drop.SourceCategory == "magic");
        Assert.Contains(generated.Generated, seed => seed.Seed.Stack is not null && seed.Metadata is null); // gold keeps the canonical default metadata path.
        Assert.Contains(generated.Generated, seed => seed.Seed.Stack is not null && seed.Metadata is not null);
    }

    [Fact]
    public void Population_requires_the_shared_source_identity_to_match_its_inventory_owner()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallLootPopulation population = new(definitions, RandomMinimum.Create(), new DaggerfallUniqueItemAllocator(1));
        DaggerfallLootPopulationRequest malformed = Request("C") with { Owner = DaggerfallItemOwner.Encounter(4000) };

        Assert.Throws<ArgumentException>(() => population.Generate(malformed));
        Assert.Throws<ArgumentException>(() => population.Generate(Request("C") with { DungeonType = 12 }));
    }

    [Fact]
    public void Dungeon_population_materializes_map_potion_and_recipe_instance_identity()
    {
        DaggerfallLootPopulation population = new(LoadDefinitions(), RandomMinimum.Create(), new DaggerfallUniqueItemAllocator(1));

        DaggerfallLootPopulationResult generated = population.Generate(Request("L") with { DungeonType = 12 });

        GeneratedLootSeed map = generated.Generated.Last(seed => seed.Seed.Item.Value == "template-287");
        GeneratedLootSeed potion = generated.Generated.Last(seed => seed.Seed.Item.Value == "template-83");
        GeneratedLootSeed recipe = generated.Generated.Last(seed => seed.Seed.Item.Value == "template-278");
        Assert.Null(map.Metadata?.PotionRecipeKey);
        Assert.Equal(221871, potion.Metadata?.PotionRecipeKey);
        Assert.Equal(221871, recipe.Metadata?.PotionRecipeKey);
    }

    [Fact]
    public void Keyed_enemy_corpse_rolls_map_potion_and_recipe_after_its_table_through_keyed_random()
    {
        IRandomService random = DispatchProxy.Create<IRandomService, RollsByKey>();
        RollsByKey rolls = (RollsByKey)(object)random;
        // Each chance succeeds on a roll below its threshold: map 1 (the Monk's MapChance), potion 3, recipe 2.
        rolls.Values["corpse.loot.enemy.map"] = 0;
        rolls.Values["corpse.loot.enemy.potion"] = 2;
        rolls.Values["corpse.loot.enemy.potion.recipe"] = 3;
        rolls.Values["corpse.loot.enemy.potion-recipe"] = 1;
        rolls.Values["corpse.loot.enemy.potion-recipe.recipe"] = 19;
        DaggerfallLootPopulation population = new(LoadDefinitions(), random, new DaggerfallUniqueItemAllocator(1));

        DaggerfallLootPopulationResult generated = population.Generate(CorpseRequest("T", mapChance: 1));

        Assert.Equal("T", Assert.IsType<DaggerfallLootResult>(generated.Loot).TableKey);
        Assert.Equal(["map", "potion", "potion-recipe"], generated.EnemyExtras.Select(extra => extra.Kind));
        Assert.Equal([1, 3, 2], generated.EnemyExtras.Select(extra => extra.Chance));
        Assert.All(generated.EnemyExtras, extra => Assert.True(extra.Success));
        // The chances follow the table's drops, in the donor's map, potion, recipe order.
        Assert.Equal(["template-287", "template-83", "template-278"], generated.Generated.TakeLast(3).Select(seed => seed.Seed.Item.Value));
        GeneratedLootSeed potion = generated.Generated.Single(seed => seed.Seed.Item.Value == "template-83");
        GeneratedLootSeed recipe = generated.Generated.Single(seed => seed.Seed.Item.Value == "template-278");
        Assert.Equal(5017404, potion.Metadata?.PotionRecipeKey);
        Assert.Equal(2031019196, recipe.Metadata?.PotionRecipeKey);

        // A roll at the chance misses: Dice100.SuccessRoll is a 0-99 roll strictly below it.
        rolls.Values["corpse.loot.enemy.map"] = 1;
        rolls.Values["corpse.loot.enemy.potion"] = 3;
        rolls.Values["corpse.loot.enemy.potion-recipe"] = 2;
        DaggerfallLootPopulationResult missed = population.Generate(CorpseRequest("T", mapChance: 1));
        Assert.All(missed.EnemyExtras, extra => Assert.False(extra.Success));
        Assert.DoesNotContain(missed.Generated, seed => seed.Seed.Item.Value is "template-287" or "template-83" or "template-278");
    }

    [Fact]
    public void Keyless_enemy_corpse_rolls_only_its_map_chance_and_no_table()
    {
        IRandomService random = DispatchProxy.Create<IRandomService, RollsByKey>();
        RollsByKey rolls = (RollsByKey)(object)random;
        DaggerfallLootPopulation population = new(LoadDefinitions(), random, new DaggerfallUniqueItemAllocator(1));

        DaggerfallLootPopulationResult generated = population.Generate(CorpseRequest(null, mapChance: 2));

        Assert.Null(generated.TableKey);
        Assert.Null(generated.Loot);
        DaggerfallLootExtraRoll map = Assert.Single(generated.EnemyExtras);
        Assert.Equal(("map", 2, true), (map.Kind, map.Chance, map.Success));
        Assert.Equal("template-287", Assert.Single(generated.Generated).Seed.Item.Value);
        // The empty key gates the potion and recipe chances away entirely: neither is drawn.
        Assert.Equal(["corpse.loot.enemy.map"], rolls.Drawn);

        // A keyless enemy with no map chance generates nothing at all.
        Assert.Empty(population.Generate(CorpseRequest(null, mapChance: 0)).Generated);
    }

    [Fact]
    public void Enemy_chances_belong_to_a_corpse_and_a_missing_table_needs_them()
    {
        DaggerfallLootPopulation population = new(LoadDefinitions(), RandomMinimum.Create(), new DaggerfallUniqueItemAllocator(1));

        Assert.Throws<ArgumentException>(() => population.Generate(CorpseRequest(null, mapChance: null)));
        Assert.Throws<ArgumentException>(() => population.Generate(CorpseRequest("T", mapChance: 101)));
        Assert.Throws<ArgumentException>(() => population.Generate(Request("T") with { EnemyMapChance = 1 }));
    }

    private static DaggerfallLootPopulationRequest CorpseRequest(string? table, int? mapChance) => new(
        new DaggerfallLootPopulationId("corpse", 2000), DaggerfallItemOwner.Corpse(2000), table,
        1, 1, 1, "breton", "male", "MensClothing", EnemyMapChance: mapChance);

    private static DaggerfallLootPopulationRequest Request(string table) => new(
        new DaggerfallLootPopulationId("world-treasure", 4000), DaggerfallItemOwner.WorldTreasure(4000), table,
        1, 1, 1, "breton", "male", "MensClothing");

    /// <summary>Answers each keyed draw by the roll name it ends with, and the minimum otherwise.</summary>
    private class RollsByKey : DispatchProxy
    {
        internal Dictionary<string, long> Values { get; } = [];
        internal List<string> Drawn { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            string roll = request.Key[(request.Key.LastIndexOf(':') + 1)..];
            Drawn.Add(roll);
            return new KeyedRngReceipt(Values.TryGetValue(roll, out long value) ? value : request.Minimum);
        }
    }

    private class CountingRandom : DispatchProxy
    {
        internal int Draws;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            Draws++;
            return new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum);
        }
    }

    private static DaggerfallDefinitions LoadDefinitions() =>
        TestPayload.Definitions;

}
