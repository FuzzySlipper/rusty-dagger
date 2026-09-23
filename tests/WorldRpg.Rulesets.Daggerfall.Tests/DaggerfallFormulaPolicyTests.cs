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
        Assert.Equal(0, DaggerfallFormulaPolicy.FallDamage(5f));
        Assert.Equal(12, DaggerfallFormulaPolicy.FallDamage(7.5f));
        Assert.Equal(256, DaggerfallFormulaPolicy.FatigueDamage(2));
        Assert.Equal(33, DaggerfallFormulaPolicy.SpellPoints(22, 1500));
        Assert.Equal(4, DaggerfallFormulaPolicy.HandToHandMinimumDamage(30));
        Assert.Equal(7, DaggerfallFormulaPolicy.HandToHandMaximumDamage(30));
        Assert.Equal(2, DaggerfallFormulaPolicy.ClassicPlayerLevel(4, 0));
        Assert.Equal(2, DaggerfallFormulaPolicy.CalculatePlayerLevel(0, 4));
        Assert.Equal(0, DaggerfallFormulaPolicy.ExperimentalXpLevel(0, DaggerfallFormulaPolicy.Experimental));
        Assert.Equal(0, DaggerfallFormulaPolicy.ExperimentalXpLevel(499, DaggerfallFormulaPolicy.Experimental));
        Assert.Equal(1, DaggerfallFormulaPolicy.ExperimentalXpLevel(500, DaggerfallFormulaPolicy.Experimental));
        Assert.Equal(2, DaggerfallFormulaPolicy.ExperimentalXpLevel(1_000, DaggerfallFormulaPolicy.Experimental));
        Assert.Equal(33, DaggerfallFormulaPolicy.SkillUsesForAdvancement(30, 2, 130, 1));
        Assert.Equal(26, DaggerfallFormulaPolicy.CalculateSkillUsesForAdvancement(30, 2, 1.0390625f, 1));
        Assert.Equal(12, DaggerfallFormulaPolicy.SkillAdvancementMultiplier("medical"));
        Assert.Equal((4, 8), DaggerfallFormulaPolicy.HitPointsPerLevelRollBounds(8));
        Assert.Equal(3, DaggerfallFormulaPolicy.HitPointsPerLevelUp(4, 40));
        Assert.Equal(156, DaggerfallFormulaPolicy.CalculateStealthChance(25f, 80));
        Assert.Equal(156, DaggerfallFormulaPolicy.CalculateStealthChance(25d, 80));
        Assert.Equal(26, DaggerfallFormulaPolicy.CalculateEnemyPacificationChance(
            DaggerfallSkills.Etiquette, 60, 50, weaponSheathed: true));
        Assert.True(DaggerfallFormulaPolicy.CalculateEnemyPacification(
            DaggerfallSkills.Etiquette, 60, 50, weaponSheathed: true, roll: 25));
        Assert.False(DaggerfallFormulaPolicy.CalculateEnemyPacification(
            DaggerfallSkills.Etiquette, 60, 50, weaponSheathed: true, roll: 26));
        Assert.True(DaggerfallFormulaPolicy.CalculateEnemyPacification(
            DaggerfallSkills.Etiquette, 60, 50, weaponSheathed: true,
            comprehendLanguagesBonus: 5, roll: 30));
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
    public void Complete_hit_pipeline_keeps_body_weapon_armor_stats_skills_adrenaline_and_adjustments_in_donor_order()
    {
        Assert.Equal(-10, DaggerfallFormulaPolicy.CalculateWeaponToHit("iron", DaggerfallFormulaPolicy.ClassicWeaponToHitMaterialModifiers));
        Assert.Equal(0, DaggerfallFormulaPolicy.CalculateWeaponToHit("steel", DaggerfallFormulaPolicy.ClassicWeaponToHitMaterialModifiers));
        Assert.Equal(30, DaggerfallFormulaPolicy.CalculateWeaponToHit("mithril"));
        Assert.Equal(30, DaggerfallFormulaPolicy.CalculateWeaponToHit("adamantium"));
        Assert.Equal(40, DaggerfallFormulaPolicy.CalculateWeaponToHit("ebony"));
        Assert.Equal(50, DaggerfallFormulaPolicy.CalculateWeaponToHit("orcish", DaggerfallFormulaPolicy.ClassicWeaponToHitMaterialModifiers));
        Assert.Equal(0, DaggerfallFormulaPolicy.CalculateWeaponToHit(null, DaggerfallFormulaPolicy.ClassicWeaponToHitMaterialModifiers));
        Assert.Equal(5, DaggerfallFormulaPolicy.CalculateAdrenalineRushToHit(true, false, 11.99d, 100d, false, false, 100d, 100d));
        Assert.Equal(0, DaggerfallFormulaPolicy.CalculateAdrenalineRushToHit(true, false, 12d, 100d, false, false, 100d, 100d));
        Assert.Equal(-8, DaggerfallFormulaPolicy.CalculateAdrenalineRushToHit(false, false, 100d, 100d, true, true, 11.99d, 100d));
        Assert.Equal(37, DaggerfallFormulaPolicy.BackstabChance(37, true));
        Assert.Equal(37, DaggerfallFormulaPolicy.CalculateBackstabChance(37, true));
        Assert.Equal(0, DaggerfallFormulaPolicy.BackstabChance(37, false));
        Assert.Equal(0, DaggerfallFormulaPolicy.BackstabChance(-1, true));
        Assert.Equal(0, DaggerfallFormulaPolicy.CalculateStatsToHit(45, 50, 45, 50));
        Assert.Equal(1, DaggerfallFormulaPolicy.CalculateStatsToHit(65, 50, 45, 50));
        Assert.Equal(-5, DaggerfallFormulaPolicy.CalculateSkillsToHit(20, 39, false));
        Assert.Equal(-2, DaggerfallFormulaPolicy.CalculateSkillsToHit(20, 39, true));
        Assert.Equal(-10, DaggerfallFormulaPolicy.CalculateAdjustmentsToHit(true, 0));
        Assert.Equal(97, DaggerfallFormulaPolicy.CalculateSuccessfulHitChance(80, 65, 5, -1, -2, -10));
        Assert.True(DaggerfallFormulaPolicy.CalculateSuccessfulHit(80, 65, 5, -1, -2, -10, 97));
        Assert.False(DaggerfallFormulaPolicy.CalculateSuccessfulHit(0, -100, 0, -20, -20, -50, 4));
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
    public void EnemyClassHealthAddsOneInclusiveRollPerLevelToTheDonorBase()
    {
        // Donor: 10 plus Random.Range(1, hitPointsPerLevel + 1) per level.
        Assert.Equal(10, DaggerfallFormulaPolicy.RollEnemyClassMaxHealth(0, 8, (minimum, maximum) => maximum));
        Assert.Equal(18, DaggerfallFormulaPolicy.RollEnemyClassMaxHealth(1, 8, (minimum, maximum) => maximum));
        Assert.Equal(11, DaggerfallFormulaPolicy.RollEnemyClassMaxHealth(1, 8, (minimum, maximum) => minimum));
        int calls = 0;
        int[] scripted = [2, 5, 8];
        Assert.Equal(10 + 2 + 5 + 8, DaggerfallFormulaPolicy.RollEnemyClassMaxHealth(3, 8, (minimum, maximum) =>
        {
            Assert.Equal(1, minimum);
            Assert.Equal(8, maximum);
            return scripted[calls++];
        }));
        Assert.Equal(3, calls);
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallFormulaPolicy.RollEnemyClassMaxHealth(-1, 8, (minimum, maximum) => minimum));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallFormulaPolicy.RollEnemyClassMaxHealth(1, 0, (minimum, maximum) => minimum));
        Assert.Throws<InvalidOperationException>(() => DaggerfallFormulaPolicy.RollEnemyClassMaxHealth(1, 8, (minimum, maximum) => maximum + 1));
    }

    [Fact]
    public void EnemyGroupFollowsTheDonorCareerTableIncludingTheClassicMisgroupings()
    {
        // Spot-checks across every donor arm, with the donor's own comments preserved:
        // Horse_Invalid and Dragonling_Alternate group as undead-turned-animals, Dreugh and
        // Lamia as humanoid despite their undead grouping in classic.
        Assert.Equal(DaggerfallEnemyGroup.Animals, DaggerfallFormulaPolicy.EnemyGroupFor("monster", 0));
        Assert.Equal(DaggerfallEnemyGroup.Animals, DaggerfallFormulaPolicy.EnemyGroupFor("monster", 11));
        Assert.Equal(DaggerfallEnemyGroup.Animals, DaggerfallFormulaPolicy.EnemyGroupFor("monster", 39));
        Assert.Equal(DaggerfallEnemyGroup.Humanoid, DaggerfallFormulaPolicy.EnemyGroupFor("monster", 1));
        Assert.Equal(DaggerfallEnemyGroup.Humanoid, DaggerfallFormulaPolicy.EnemyGroupFor("monster", 41));
        Assert.Equal(DaggerfallEnemyGroup.Undead, DaggerfallFormulaPolicy.EnemyGroupFor("monster", 15));
        Assert.Equal(DaggerfallEnemyGroup.Undead, DaggerfallFormulaPolicy.EnemyGroupFor("monster", 17));
        Assert.Equal(DaggerfallEnemyGroup.Daedra, DaggerfallFormulaPolicy.EnemyGroupFor("monster", 25));
        Assert.Equal(DaggerfallEnemyGroup.None, DaggerfallFormulaPolicy.EnemyGroupFor("monster", 35));
        Assert.Equal(DaggerfallEnemyGroup.None, DaggerfallFormulaPolicy.EnemyGroupFor("monster", 99));
        // Class enemies travel the same switch: the pack thief (mobile 138, index 10) meets the
        // Nymph arm and the pack archer (mobile 141, index 13) meets the Harpy arm.
        Assert.Equal(DaggerfallEnemyGroup.Humanoid, DaggerfallFormulaPolicy.EnemyGroupFor("enemy-class", 138));
        Assert.Equal(DaggerfallEnemyGroup.Humanoid, DaggerfallFormulaPolicy.EnemyGroupFor("enemy-class", 141));
        Assert.Equal(DaggerfallEnemyGroup.None, DaggerfallFormulaPolicy.EnemyGroupFor("player", 0));
        Assert.Equal(DaggerfallEnemyGroup.None, DaggerfallFormulaPolicy.EnemyGroupFor("civilian", 10));

        DaggerfallDefinitions definitions = LoadDefinitions();
        Assert.Equal(DaggerfallEnemyGroup.Humanoid, DaggerfallFormulaPolicy.EnemyGroupFor(definitions.RequireActor(new DaggerfallActorId("thief"))));
        Assert.Equal(DaggerfallEnemyGroup.Animals, DaggerfallFormulaPolicy.EnemyGroupFor(definitions.RequireActor(new DaggerfallActorId("rat"))));
        Assert.Equal(DaggerfallEnemyGroup.Undead, DaggerfallFormulaPolicy.EnemyGroupFor(definitions.RequireActor(new DaggerfallActorId("lich"))));
        Assert.Equal(DaggerfallEnemyGroup.Daedra, DaggerfallFormulaPolicy.EnemyGroupFor(definitions.RequireActor(new DaggerfallActorId("daedra-lord"))));
        Assert.Equal(DaggerfallEnemyGroup.None, DaggerfallFormulaPolicy.EnemyGroupFor(definitions.RequireActor(new DaggerfallActorId("fire-atronach"))));
    }

    [Fact]
    public void EnemyLanguageSkillFollowsTheDonorTableWithStreetwiseForRoguishClasses()
    {
        Assert.Equal("streetwise", DaggerfallFormulaPolicy.LanguageSkillFor("enemy-class", 138));
        Assert.Equal("etiquette", DaggerfallFormulaPolicy.LanguageSkillFor("enemy-class", 141));
        Assert.Equal("etiquette", DaggerfallFormulaPolicy.LanguageSkillFor("enemy-class", 128));
        Assert.Equal("orcish", DaggerfallFormulaPolicy.LanguageSkillFor("monster", 7));
        Assert.Equal("harpy", DaggerfallFormulaPolicy.LanguageSkillFor("monster", 13));
        Assert.Equal("giantish", DaggerfallFormulaPolicy.LanguageSkillFor("monster", 16));
        Assert.Equal("dragonish", DaggerfallFormulaPolicy.LanguageSkillFor("monster", 34));
        Assert.Equal("nymph", DaggerfallFormulaPolicy.LanguageSkillFor("monster", 10));
        Assert.Equal("daedric", DaggerfallFormulaPolicy.LanguageSkillFor("monster", 25));
        Assert.Equal("spriggan", DaggerfallFormulaPolicy.LanguageSkillFor("monster", 2));
        Assert.Equal("centaurian", DaggerfallFormulaPolicy.LanguageSkillFor("monster", 8));
        Assert.Equal("impish", DaggerfallFormulaPolicy.LanguageSkillFor("monster", 1));
        Assert.Equal("etiquette", DaggerfallFormulaPolicy.LanguageSkillFor("monster", 28));
        Assert.Null(DaggerfallFormulaPolicy.LanguageSkillFor("monster", 0));
        Assert.Null(DaggerfallFormulaPolicy.LanguageSkillFor("monster", 35));
        Assert.Null(DaggerfallFormulaPolicy.LanguageSkillFor("player", 0));

        DaggerfallDefinitions definitions = LoadDefinitions();
        foreach (string skill in definitions.Actors.Values.Select(actor => DaggerfallFormulaPolicy.LanguageSkillFor(actor)).OfType<string>())
            Assert.Contains(skill, definitions.Vocabulary.Skills.Select(entry => entry.Value), StringComparer.Ordinal);
    }

    [Fact]
    public void EnemyWeightAddsFourTimesCarriedWeightToTheDonorBodyWeight()
    {
        Assert.Equal(2, DaggerfallFormulaPolicy.ActorWeightInClassicUnits(2, 0));
        Assert.Equal(42, DaggerfallFormulaPolicy.ActorWeightInClassicUnits(40, 0.5));
        Assert.Equal(350, DaggerfallFormulaPolicy.EnemyBaseWeight("enemy-class", null, female: false));
        Assert.Equal(240, DaggerfallFormulaPolicy.EnemyBaseWeight("enemy-class", null, female: true));
        Assert.Equal(40, DaggerfallFormulaPolicy.EnemyBaseWeight("monster", 40, female: false));
        Assert.Throws<ArgumentException>(() => DaggerfallFormulaPolicy.EnemyBaseWeight("monster", null, female: false));
        Assert.Throws<ArgumentException>(() => DaggerfallFormulaPolicy.EnemyBaseWeight("player", null, female: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallFormulaPolicy.ActorWeightInClassicUnits(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallFormulaPolicy.ActorWeightInClassicUnits(0, -1));
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
        Assert.All(result.Categories.Where(category => category.Supported && category.Category != "magic"), category =>
        {
            Assert.True(category.Supported);
            Assert.Equal([100, 50, 25, 12, 6, 3, 1, 0], category.Rolls.Select(roll => roll.Chance));
            Assert.All(category.Rolls.Take(7), roll => Assert.True(roll.Success));
            Assert.False(category.Rolls[^1].Success);
        });
        Assert.Equal(16, result.Drops.Count);
        Assert.True(result.Categories.Single(category => category.Category == "magic").Supported);
        Assert.Contains(result.Drops, drop => drop.SourceCategory == "magic" && drop.ItemId.StartsWith("magic-item.", StringComparison.Ordinal));
        Assert.Contains("loot.T.armor.0", rolls);
        Assert.Contains("loot.T.weapons.2.pick", rolls);
    }

    [Fact]
    public void LootGenerationMaterializesClassicIngredientCategoriesAndScalesOnlyTheirDonorGroups()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallLootResult result = DaggerfallLootPolicy.Generate(definitions, "C", 2, (_, minimum, _) => minimum);

        DaggerfallLootCategoryResult creatureOne = result.Categories.Single(category => category.Category == "creature1");
        DaggerfallLootCategoryResult creatureThree = result.Categories.Single(category => category.Category == "creature3");
        Assert.Equal(10, creatureOne.EffectiveChance);
        Assert.Equal(5, creatureThree.EffectiveChance);
        Assert.True(creatureOne.Supported);
        Assert.Equal([10, 5, 2, 1, 0], creatureOne.Rolls.Select(roll => roll.Chance));
        Assert.False(creatureOne.Rolls[^1].Success);
        Assert.Contains(result.Drops, drop => drop.SourceCategory == "creature1" && drop.ItemId.StartsWith("template-", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_retained_category_pool_is_selectable_and_the_empty_table_has_no_rolls()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallLootCategoryResult[] categories = definitions.LootTables.Values
            .Where(table => table.Key != "-")
            .SelectMany(table => DaggerfallLootPolicy.Generate(definitions, table.Key, 1, (_, minimum, _) => minimum, "MensClothing").Categories)
            .ToArray();

        Assert.Equal(
            ["armor", "books", "clothing", "creature1", "creature2", "creature3", "magic", "misc1", "misc2", "plant1", "plant2", "religious", "weapons"],
            categories.Select(category => category.Category).Distinct(StringComparer.Ordinal).OrderBy(category => category, StringComparer.Ordinal));
        Assert.All(categories, category => Assert.True(category.Supported));

        DaggerfallLootResult empty = DaggerfallLootPolicy.Generate(definitions, "-", 1,
            (_, _, _) => throw new InvalidOperationException("The empty table must not draw random values."));
        Assert.Empty(empty.Categories);
        Assert.Empty(empty.Drops);
        Assert.Null(empty.GoldRoll);
    }

    [Fact]
    public void Dungeon_loot_maps_all_nineteen_donor_types_and_retains_map_potion_and_recipe_rolls()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        Assert.Equal(
            ["K", "N", "N", "N", "K", "M", "M", "Q", "K", "U", "D", "N", "L", "F", "S", "N", "M", "L", "N"],
            Enumerable.Range(0, 19).Select(DaggerfallLootPolicy.DungeonTableKey));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallLootPolicy.DungeonTableKey(19));

        DaggerfallDungeonLootResult dungeon = DaggerfallLootPolicy.GenerateDungeon(definitions, 12, 1, (_, minimum, _) => minimum);

        Assert.Equal("L", dungeon.TableKey);
        Assert.Equal(["map", "potion", "potion-recipe"], dungeon.Extras.Select(extra => extra.Kind));
        Assert.All(dungeon.Extras, extra => Assert.True(extra.Success));
        Assert.Equal(["template-287", "template-83", "template-278"], dungeon.Loot.Drops.TakeLast(3).Select(drop => drop.ItemId));
        Assert.Equal(221871, dungeon.Loot.Drops.Last(drop => drop.SourceCategory == "potion").PotionRecipeKey);
        Assert.Equal(221871, dungeon.Loot.Drops.Last(drop => drop.SourceCategory == "potion-recipe").PotionRecipeKey);
        DaggerfallDungeonLootResult missedMap = DaggerfallLootPolicy.GenerateDungeon(definitions, 12, 1,
            (id, minimum, maximum) => id.EndsWith(".map", StringComparison.Ordinal) ? maximum : minimum);
        Assert.False(missedMap.Extras.Single(extra => extra.Kind == "map").Success);
        Assert.DoesNotContain(missedMap.Loot.Drops, drop => drop.SourceCategory == "map");
        Assert.Empty(DaggerfallLootPolicy.GenerateDungeon(definitions, 7, 1, (_, minimum, _) => minimum).Extras);
    }

    private static DaggerfallDefinitions LoadDefinitions() => DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
