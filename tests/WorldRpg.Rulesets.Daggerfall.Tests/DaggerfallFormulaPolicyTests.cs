using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallFormulaPolicyTests
{
    [Fact]
    public void DerivedFormulasKeepClassicRoundingAndSeparateProgressionProfiles()
    {
        Assert.Equal(-1, DaggerfallFormulaPolicy.DamageModifier(45));
        Assert.Equal(-1, DaggerfallFormulaPolicy.ToHitModifier(45));
        Assert.Equal(67, DaggerfallFormulaPolicy.MaxEncumbrance(45));
        Assert.Equal(6_400, DaggerfallFormulaPolicy.MaxFatigue(45, 55));
        Assert.Equal(33, DaggerfallFormulaPolicy.SpellPoints(22, 1500));
        Assert.Equal(4, DaggerfallFormulaPolicy.HandToHandMinimumDamage(30));
        Assert.Equal(7, DaggerfallFormulaPolicy.HandToHandMaximumDamage(30));
        Assert.Equal(2, DaggerfallFormulaPolicy.ClassicPlayerLevel(4, 0));
        Assert.Equal(0, DaggerfallFormulaPolicy.ExperimentalXpLevel(0, DaggerfallFormulaPolicy.Experimental));
        Assert.Equal(0, DaggerfallFormulaPolicy.ExperimentalXpLevel(499, DaggerfallFormulaPolicy.Experimental));
        Assert.Equal(1, DaggerfallFormulaPolicy.ExperimentalXpLevel(500, DaggerfallFormulaPolicy.Experimental));
        Assert.Equal(2, DaggerfallFormulaPolicy.ExperimentalXpLevel(1_000, DaggerfallFormulaPolicy.Experimental));
        Assert.Equal(33, DaggerfallFormulaPolicy.SkillUsesForAdvancement(30, 2, 130, 1));
        Assert.Equal(12, DaggerfallFormulaPolicy.SkillAdvancementMultiplier("medical"));
        Assert.Equal((4, 8), DaggerfallFormulaPolicy.HitPointsPerLevelRollBounds(8));
        Assert.Equal(3, DaggerfallFormulaPolicy.HitPointsPerLevelUp(4, 40));
    }

    [Fact]
    public void CombatFormulaUsesTruncatingAttributeTermsAndClassicBodyTable()
    {
        Assert.Equal(10, DaggerfallFormulaPolicy.CalculateHitChance(60, 0, 45, 50, 45, 50, 0));
        Assert.Equal(3, DaggerfallFormulaPolicy.CalculateHitChance(0, -100, 0, 100, 0, 100, 100));
        Assert.Equal(97, DaggerfallFormulaPolicy.CalculateHitChance(100, 100, 100, 0, 100, 0, 0));
        Assert.Equal(0, DaggerfallFormulaPolicy.StruckBodyPart(0));
        Assert.Equal(6, DaggerfallFormulaPolicy.StruckBodyPart(19));
    }

    [Fact]
    public void MaterialGateRequiresTheTargetMinimumWithoutInventingNaturalWeaponMaterials()
    {
        IReadOnlyDictionary<string, int> materials = DaggerfallFormulaPolicy.ClassicWeaponMaterialRanks;

        Assert.True(DaggerfallFormulaPolicy.CanHitMaterial("silver", "iron", materials));
        Assert.False(DaggerfallFormulaPolicy.CanHitMaterial("iron", "silver", materials));
        Assert.False(DaggerfallFormulaPolicy.CanHitMaterial("steel", "silver", materials));
        Assert.False(DaggerfallFormulaPolicy.CanHitMaterial("leather", "iron", materials));
        Assert.False(DaggerfallFormulaPolicy.CanHitMaterial("chain", "iron", materials));
        Assert.True(DaggerfallFormulaPolicy.CanHitMaterial(null, null, materials));
        Assert.False(DaggerfallFormulaPolicy.CanHitMaterial(null, "silver", materials));
    }

    [Fact]
    public void FormulaTuningUsesSelectedDivisorsAndAdmitsTheDonorSkillLevelBounds()
    {
        DaggerfallFormulaTuning tuning = new(DamageModifierDivisor: 10);

        Assert.Equal(-1, DaggerfallFormulaPolicy.DamageModifier(40, tuning));
        Assert.True(DaggerfallFormulaPolicy.SkillUsesForAdvancement(30, 2, 130, 64) > 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallFormulaPolicy.SkillUsesForAdvancement(30, 2, 130, 65));
    }

    [Fact]
    public void LootGenerationUsesDonorOrderHalvingAndLoadedWeaponArmorPools()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        List<string> rolls = [];
        DaggerfallLootResult result = DaggerfallLootPolicy.Generate(definitions, "T", 2, (id, minimum, maximum) =>
        {
            rolls.Add(id);
            return id.EndsWith(".gold", StringComparison.Ordinal) ? 20 : minimum;
        });

        Assert.Equal(40, result.GoldRoll!.Value * result.PlayerLevel);
        Assert.Equal(["weapons", "armor", "magic"], result.Categories.Select(category => category.Category));
        Assert.All(result.Categories.Where(category => category.Supported), category =>
        {
            Assert.True(category.Supported);
            Assert.Equal([100, 50, 25], category.Rolls.Select(roll => roll.Chance));
            Assert.All(category.Rolls, roll => Assert.True(roll.Success));
        });
        Assert.Equal(7, result.Drops.Count);
        Assert.False(result.Categories.Single(category => category.Category == "magic").Supported);
        Assert.Contains("loot.T.armor.0", rolls);
        Assert.Contains("loot.T.weapons.2.pick", rolls);
    }

    [Fact]
    public void LootGenerationKeepsUnsupportedCategorySuccessVisibleAndScalesOnlyClassicIngredientGroups()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallLootResult result = DaggerfallLootPolicy.Generate(definitions, "C", 2, (_, minimum, _) => minimum);

        DaggerfallLootCategoryResult creatureOne = result.Categories.Single(category => category.Category == "creature1");
        DaggerfallLootCategoryResult creatureThree = result.Categories.Single(category => category.Category == "creature3");
        Assert.Equal(10, creatureOne.EffectiveChance);
        Assert.Equal(5, creatureThree.EffectiveChance);
        Assert.False(creatureOne.Supported);
        Assert.All(creatureOne.Rolls, roll => Assert.True(roll.Success));
        Assert.DoesNotContain(result.Drops, drop => drop.SourceCategory == "creature1");
    }

    private static DaggerfallDefinitions LoadDefinitions() => DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
