using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit.World;
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
        DaggerfallLootResult loot = generated.Loot;
        Assert.Contains(loot.Drops, drop => drop.SourceCategory == "books");
        Assert.Contains(loot.Drops, drop => drop.SourceCategory == "magic");
        Assert.Contains(generated.Generated, seed => seed.Seed.Stack is not null && seed.Metadata is null); // gold keeps the canonical default metadata path.
        Assert.Contains(generated.Generated, seed => seed.Seed.Stack is not null && seed.Metadata is not null);
    }

    [Fact]
    public void Population_requires_the_shared_source_identity_to_match_its_inventory_owner()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallLootPopulation population = new(definitions, RandomMinimum(), new DaggerfallUniqueItemAllocator(1));
        DaggerfallLootPopulationRequest malformed = Request("C") with { Owner = DaggerfallItemOwner.Encounter(4000) };

        Assert.Throws<ArgumentException>(() => population.Generate(malformed));
        Assert.Throws<ArgumentException>(() => population.Generate(Request("C") with { DungeonType = 12 }));
    }

    [Fact]
    public void Dungeon_population_materializes_map_potion_and_recipe_instance_identity()
    {
        DaggerfallLootPopulation population = new(LoadDefinitions(), RandomMinimum(), new DaggerfallUniqueItemAllocator(1));

        DaggerfallLootPopulationResult generated = population.Generate(Request("L") with { DungeonType = 12 });

        GeneratedLootSeed map = generated.Generated.Last(seed => seed.Seed.Item.Value == "template-287");
        GeneratedLootSeed potion = generated.Generated.Last(seed => seed.Seed.Item.Value == "template-83");
        GeneratedLootSeed recipe = generated.Generated.Last(seed => seed.Seed.Item.Value == "template-278");
        Assert.Null(map.Metadata?.PotionRecipeKey);
        Assert.Equal(221871, potion.Metadata?.PotionRecipeKey);
        Assert.Equal(221871, recipe.Metadata?.PotionRecipeKey);
    }

    private static DaggerfallLootPopulationRequest Request(string table) => new(
        new DaggerfallLootPopulationId("world-treasure", 4000), DaggerfallItemOwner.WorldTreasure(4000), table,
        1, 1, 1, "breton", "male", "MensClothing");

    private static IRandomService RandomMinimum() => DispatchProxy.Create<IRandomService, RandomMinimumProxy>();

    private class RandomMinimumProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
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

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
