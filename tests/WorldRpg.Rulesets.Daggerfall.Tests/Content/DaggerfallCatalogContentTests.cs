using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using System.Globalization;
using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCatalogContentTests
{
    [Fact]
    public void LoadsTheCompleteDonorCatalogWithTheExplicitHorseGap()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        Assert.Equal(9, definitions.Vocabulary.Attributes.Count);
        Assert.Equal(35, definitions.Vocabulary.Skills.Count);
        Assert.Equal(4, definitions.Vocabulary.Tracks.Count);
        Assert.Equal(7, definitions.Vocabulary.ArmorParts.Count);
        Assert.Equal(2, definitions.Vocabulary.Progression.Count);
        Assert.Equal(42, definitions.Actors.Values.Count(actor => actor.Kind == "monster"));
        Assert.DoesNotContain(definitions.Actors.Values, actor => actor.MobileId == 39);
        Assert.Equal(32, definitions.Items.Count);
        Assert.Equal(25, definitions.EquipmentSlots.Count);
        Assert.Equal(7, definitions.Actions.Count);
        Assert.Equal(22, definitions.LootTables.Count);
        Assert.Equal(12, definitions.ArmorValuesByMaterial.Count);
        Assert.NotEmpty(definitions.RequireActor(new DaggerfallActorId("player")).Loadout);
        // One authored quiver policy supplies every bow-capable class, including placed archers.
        DaggerfallActorDefinition archerDefinition = definitions.RequireActor(new DaggerfallActorId("archer"));
        Assert.Empty(archerDefinition.Loadout);
        Assert.Equal(new DaggerfallItemId("arrow"), definitions.EnemyRanged.ArrowItem);
        Assert.Equal(12, definitions.EnemyRanged.StartingArrows);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 40, 41, 42 }, definitions.Actors.Values.Where(actor => actor.Kind == "monster").Select(actor => actor.MobileId!.Value).Order());
        Assert.Equal("both", definitions.Items[new DaggerfallItemId("iron-short-bow")].Weapon!.Handedness);
        Assert.Equal("right-hand", definitions.RequireActor(new DaggerfallActorId("player")).Loadout[0].EquipSlot!.Value.Value);
        Assert.Equal(8, definitions.RequireActor(new DaggerfallActorId("player")).HitPointsPerLevel);
        // Every swing, a bow shot included, costs the same stamina; attack tuning adds the classic's 11 fatigue units.
        Assert.Equal(10, definitions.Actions["melee-attack"].StaminaCost);
        Assert.Equal(["archer-shot", "bow-shot", "enemy-class-equipped-melee", "melee-attack", "monster-strike", "skeleton-strike", "thief-strike"], definitions.Actions.Values.OrderBy(action => action.Id).Select(action => action.Id));
        // The player's bow: the archer's reach and skill, no authored cooldown (FORM-04 owns it) and the
        // same stamina cost a swing pays.
        Assert.Equal(10d, definitions.Actions["bow-shot"].Reach);
        Assert.Equal("archery", definitions.Actions["bow-shot"].Skill);
        Assert.Null(definitions.Actions["bow-shot"].CooldownSeconds);
        Assert.Equal(10, definitions.Actions["bow-shot"].StaminaCost);
        Assert.Equal(0, definitions.Actions["monster-strike"].AttackRangeIndex);
        Assert.Null(definitions.Actions["monster-strike"].MinimumDamage);
        Assert.Equal(.1, definitions.Actions["melee-attack"].CooldownSeconds);
        Assert.Equal(0, definitions.Actions["skeleton-strike"].AttackRangeIndex);
        Assert.Equal(2, definitions.Actions["thief-strike"].MinimumDamage);
        Assert.Equal(8, definitions.Actions["thief-strike"].MaximumDamage);
        Assert.Equal(80, definitions.LootTables["T"].MaximumGold);
        Assert.Equal("ratIdle", definitions.RequireActor(new DaggerfallActorId("rat")).Presentation.PreferredRestState);
        Assert.Equal("move", definitions.RequireActor(new DaggerfallActorId("imp")).Presentation.PreferredRestState);
        Assert.Equal(10F, definitions.RequireActor(new DaggerfallActorId("imp")).Presentation.EffectiveFramesPerSecond["move"]);
        Assert.Equal("move", definitions.RequireActor(new DaggerfallActorId("giant-bat")).Presentation.PreferredRestState);
        Assert.Equal(10F, definitions.RequireActor(new DaggerfallActorId("giant-bat")).Presentation.EffectiveFramesPerSecond["move"]);
    }

    [Fact]
    public void ArcherRetainsItsDistinctClassicClassCareerIdentity()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        // Daggerfall Unity loads class enemy 141 from CLASS13.CFG; its EnemyBasics record assigns KnightsAndMages and loot C.
        DaggerfallActorDefinition archer = definitions.RequireActor(new DaggerfallActorId("archer"));
        Assert.Equal("enemy-class", archer.Kind);
        Assert.Equal(141, archer.MobileId);
        Assert.Equal((11, 30), (archer.Health.Minimum, archer.Health.Maximum));
        Assert.Equal("knights-and-mages", archer.Team);
        Assert.Equal("C", archer.LootTableKey);
        // The archer's policy is its ranged attack, not a melee swing: the donor record carries no melee
        // damage range, so its own attacks stay empty and the bow's authored action supplies the damage.
        Assert.Equal("archer-shot", archer.ActionId);
        Assert.Empty(archer.Attacks);
        Assert.Equal(55, archer.Stats[DaggerfallMechanicsIds.Strength]);
        Assert.Equal(43, archer.Stats[DaggerfallMechanicsIds.Intelligence]);
        Assert.Equal(60, archer.Stats[DaggerfallMechanicsIds.Agility]);
        Assert.Equal(35, archer.Stats[new DaggerfallStatId("archery")]);
        Assert.Equal(35, archer.Stats[DaggerfallMechanicsIds.HandToHand]);
        Assert.Equal(35, archer.Stats[DaggerfallMechanicsIds.Dodging]);
    }

    [Fact]
    public void HumanEncounterActorsResolveSourceCareerCombatLootAndCasterCapability()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition mage = definitions.RequireActor(new DaggerfallActorId("encounter-mage"));
        DaggerfallActorDefinition warrior = definitions.RequireActor(new DaggerfallActorId("encounter-warrior"));
        DaggerfallActorDefinition rogue = definitions.RequireActor(new DaggerfallActorId("encounter-rogue"));
        DaggerfallActorDefinition guard = definitions.RequireActor(new DaggerfallActorId("encounter-city-watch-the-haltmeister"));

        Assert.Equal((128, "class00", "U"), (mage.MobileId, mage.Career, mage.LootTableKey));
        Assert.Equal((144, "class16", "T"), (warrior.MobileId, warrior.Career, warrior.LootTableKey));
        Assert.Equal((136, "class08", "O"), (rogue.MobileId, rogue.Career, rogue.LootTableKey));
        Assert.Equal((146, "class18", (string?)null), (guard.MobileId, guard.Career, guard.LootTableKey));
        Assert.All([mage, warrior, rogue, guard], actor => Assert.Equal("enemy-class-equipped-melee", actor.ActionId));
        Assert.True(definitions.Mobiles.Mobiles[128].RequiresEnemySpellBehavior);
        Assert.False(definitions.Mobiles.Mobiles[131].RequiresEnemySpellBehavior);
        Assert.Equal(35, mage.Stats[DaggerfallMechanicsIds.LongBlade]);
        Assert.Equal(35, warrior.Stats[new DaggerfallStatId("archery")]);
    }

    [Fact]
    public void LootTablesMatchTheCompleteClassicDonorMatrix()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        string actual = string.Join(';', definitions.LootTables.Values.OrderBy(table => table.Key).Select(table => $"{table.Key}:{table.MinimumGold}-{table.MaximumGold}[{string.Join(',', table.Categories.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"))}]"));
        const string expected = "-:0-0[];A:1-10[armor=5,clothing=4,magic=2,misc2=2,weapons=5];B:0-0[plant1=10,plant2=10];C:2-20[armor=5,books=2,creature1=5,creature2=5,creature3=5,magic=3,misc1=5,misc2=2,plant1=10,plant2=10,religious=2,weapons=25];D:1-4[creature1=6,creature2=6,creature3=6,misc1=6,plant1=6,plant2=6,religious=4];E:20-80[armor=10,books=2,clothing=4,magic=3,misc2=1,religious=15,weapons=10];F:4-30[armor=50,creature1=5,creature2=5,creature3=5,magic=1,misc1=2,misc2=3,plant1=2,plant2=2,weapons=50];G:3-15[armor=50,clothing=5,magic=1,misc2=3,weapons=50];H:2-10[clothing=2,magic=1,weapons=100];I:0-0[magic=2,religious=5];J:50-150[armor=5,magic=3,weapons=5];K:1-10[armor=5,books=5,creature1=3,creature2=3,creature3=3,magic=3,misc1=3,misc2=2,plant1=3,plant2=3,religious=100,weapons=5];L:1-20[armor=50,clothing=75,creature1=3,creature2=3,creature3=3,magic=1,misc1=3,misc2=5,religious=3,weapons=50];M:1-15[armor=10,books=2,clothing=15,creature1=1,creature2=1,creature3=1,magic=1,misc1=2,misc2=3,plant1=1,plant2=1,religious=1,weapons=10];N:1-80[armor=5,books=5,clothing=20,creature1=5,creature2=5,creature3=5,magic=1,misc1=5,misc2=2,plant1=5,plant2=5,religious=5,weapons=5];O:5-20[armor=10,creature1=1,creature2=1,creature3=1,magic=2,misc1=1,plant1=1,plant2=1,weapons=15];P:5-20[armor=5,books=10,creature1=5,creature2=5,creature3=5,magic=2,misc1=5,misc2=5,plant1=5,plant2=5,weapons=10];Q:20-80[armor=10,books=5,clothing=35,creature1=8,creature2=8,creature3=8,magic=3,misc1=2,misc2=3,plant1=2,plant2=2,weapons=25];R:5-20[armor=5,creature1=3,creature2=3,creature3=3,magic=2,misc1=5,weapons=15];S:50-125[armor=10,books=5,creature1=5,creature2=5,creature3=5,magic=3,misc1=15,misc2=5,plant1=5,plant2=5,weapons=10];T:20-80[armor=100,magic=1,weapons=100];U:7-30[armor=10,books=2,creature1=5,creature2=5,creature3=5,magic=2,misc1=10,misc2=2,plant1=5,plant2=5,religious=10,weapons=10]";
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LootReceiptsFollowTheCompleteAdoptedCategoryOrder()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallLootResult result = DaggerfallLootPolicy.Generate(definitions, "C", 1, (_, minimum, _) => minimum);

        Assert.Equal(["weapons", "armor", "creature1", "creature2", "creature3", "plant1", "plant2", "misc1", "misc2", "magic", "books", "religious"], result.Categories.Select(category => category.Category));
        Assert.Equal([6, 4, 4, 4, 4, 5, 5, 4, 3, 3, 3, 3], result.Categories.Select(category => category.Rolls.Count));
        Assert.All(result.Categories.Where(category => category.Category is "weapons" or "armor"), category => Assert.All(category.Rolls, roll => Assert.Equal(roll.Chance > 0, roll.Success)));
        Assert.All(result.Categories, category => Assert.All(category.Rolls, roll => Assert.Equal(roll.Roll < roll.Chance, roll.Success)));
    }

    [Fact]
    public void SessionAllocatorIsMonotonicAndCollisionFreeAcrossRetries()
    {
        DaggerfallUniqueItemAllocator allocator = new(100);
        Assert.Equal(new DurableIdentityReference(DurableIdentityKind.Item, 100), allocator.AllocateReference());
        Assert.Equal(new DurableIdentityReference(DurableIdentityKind.Item, 101), allocator.AllocateReference());
        Assert.Equal(new DurableIdentityReference(DurableIdentityKind.Item, 102), allocator.AllocateReference());
    }

    [Fact]
    public void Reads_a_template_modelled_on_another_as_an_ordinary_row()
    {
        DaggerfallItemTemplateSet templates = TestPayload.Definitions.ItemTemplateCatalog;

        // The mace is lighter than the classic's and drawn under its own refreshed name; the club is a template of its
        // own with the classic mace's values, which reads the classic's per-template tables (sounds, equip delay) by the
        // mace's index.
        DaggerfallItemTemplateDefinition mace = templates.Resolve(124), club = templates.Resolve(288);
        Assert.Equal((3.0, "mace", (int?)null), (mace.BaseWeight, mace.RefreshedArt, mace.ClassicModel));
        Assert.Equal(("Club", 4.5, 124, 124), (club.Name, club.BaseWeight, club.ClassicModel, club.ClassicIndex));

        System.Text.Json.Nodes.JsonObject payload = TestPayload.Sections("itemTemplates");
        payload["itemTemplates"]!["templates"]!.AsArray().Single(row => row!["index"]!.GetValue<int>() == 288)!["classicModel"] = 999;
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(payload)));
        Assert.Contains("modelled on template 999", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An action that reaches past what its own kind of attack can resolve is refused, naming the action.
    /// </summary>
    /// <remarks>
    /// The bounds are code constants rather than authored values, so no pack in the corpus exercises them:
    /// melee is capped at the distance the melee query resolves and ranged at the distance an attacker
    /// perceives the player within. A reach past either is a number nothing would ever use, and the failure
    /// has to name the action so the author knows which one to fix.
    /// </remarks>
    [Theory]
    [InlineData("\"attackRangeIndex\": 0,\n      \"reach\": 2.0", "\"attackRangeIndex\": 0,\n      \"reach\": 50.0", "beyond any melee distance")]
    [InlineData("\"reach\": 10.0,", "\"reach\": 100.0,", "beyond the admitted classic ranged maximum")]
    // An action with no reach at all is the case the combat module's own refusal guards, and the one an
    // author is most likely to produce by omission. Validation owns it: the swing a missing reach would
    // admit is refused here rather than at the first attack.
    [InlineData("\"cooldownSeconds\": 1.5,\n      \"attackRangeIndex\": 0,\n      \"reach\": 2.0", "\"cooldownSeconds\": 1.5,\n      \"attackRangeIndex\": 0", "must declare the positive reach")]
    public void RejectsAnActionThatReachesFurtherThanItsKindOfAttackResolves(string before, string after, string expected)
    {
        Assert.True(TestPayload.Contains(before), "The fixture's text is not in the payload.");
        byte[] tampered = TestPayload.Replaced((before, after));

        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(
            () => DaggerfallBaseContent.Read(tampered));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"preferredRestState\": \"ratIdle\"", "\"preferredRestState\": \"\"")]
    [InlineData("\"move\": 10", "\"move\": 0")]
    [InlineData("\"move\": 10", "\"move\": \"fast\"")]
    public void RejectsMalformedActorPresentationDefinitions(string before, string after)
    {
        Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Replaced((before, after))));
    }

    [Theory]
    [InlineData("\"strength\": 50", "\"strength\": 50, \"strength\": 51")]
    [InlineData("\"material\": \"iron\"", "\"material\": \"not-a-material\"")]
    [InlineData("\"material\": \"iron\"", "\"material\": \"leather\"")]
    [InlineData("\"material\": \"iron\"", "\"material\": \"chain\"")]
    [InlineData("\"skill\": \"short-blade\"", "\"skill\": \"not-a-skill\"")]
    [InlineData("\"item\": \"iron-longsword\"", "\"item\": \"not-an-item\"")]
    [InlineData("\"action\": \"melee-attack\"", "\"action\": \"not-an-action\"")]
    [InlineData("\"lootTableKey\": \"D\"", "\"lootTableKey\": \"Z\"")]
    [InlineData("\"mobileId\": 0", "\"mobileId\": 128")]
    [InlineData("\"equipSlot\": \"right-hand\"", "\"equipSlot\": \"not-a-slot\"")]
    [InlineData("\"quantity\": 25", "\"quantity\": 0")]
    [InlineData("\"quantity\": 25", "\"quantity\": \"25\"")]
    [InlineData("\"entityId\": 1001", "\"entityId\": 0")]
    [InlineData("\"entityId\": 1001", "\"entityId\": \"1001\"")]
    public void RejectsMalformedCatalogReferencesAndCanonicalLoadoutShapes(string before, string after)
    {
        Assert.True(TestPayload.Contains(before), "The fixture's text is not in the payload.");
        Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Replaced((before, after))));
    }
    [Theory]
    [InlineData("duplicate-outcome")]
    [InlineData("duplicate-archetype")]
    public void Malformed_questionnaire_cannot_leave_a_live_answer_without_a_recommendation(string mutation)
    {
        var payload = TestPayload.Sections("catalogs");
        var quiz = payload["catalogs"]!["classQuestionnaire"]!;
        if (mutation == "duplicate-outcome") quiz["recommendations"]![0] = quiz["recommendations"]![1]!.DeepClone();
        else quiz["questions"]![0]!["answers"]![0]!["archetype"] = quiz["questions"]![0]!["answers"]![1]!["archetype"]!.DeepClone();
        Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(payload.AsObject())));
    }

}
