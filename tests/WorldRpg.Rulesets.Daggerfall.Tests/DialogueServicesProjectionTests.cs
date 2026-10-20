using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The dialogue services publish what their screens need to decide nothing themselves: topic groups,
/// training eligibility and hours, shop quantity bounds and the large-trade amount, and the makers' limits.
/// </summary>
public sealed class DialogueServicesProjectionTests
{
    private static Dictionary<string, object?> Built(Func<UiValueBuilder, uint> section)
    {
        UiValueBuilder builder = new();
        UiValue value = builder.Build(section(builder));
        return Assert.IsType<Dictionary<string, object?>>(EngineContextFake.UiServiceFake.Decode(value));
    }

    private static Dictionary<string, object?>[] Rows(object? value) =>
        [.. Assert.IsType<object?[]>(value).Cast<Dictionary<string, object?>>()];

    [Fact]
    public void A_conversation_publishes_topic_groups_training_eligibility_and_shop_bounds()
    {
        DaggerfallNpcSite site = new(17, "Daggerfall", string.Empty);
        DaggerfallSkillTrainingProviderView training = new(new DaggerfallServiceProvider(9, site, "training"), 1, 1, true, 0, 100,
            [new("medical", 10, 50), new("archery", 50, 50)], 5400, 0);
        DaggerfallMerchantView merchant = new("shop-1", "Smith", 10, 120, true, true, false, false,
            [new("stack:shop.arrows", "template-131", "Arrows", 40, 7, 10, 10, true, false, true, false),
             new("unique:5", "template-113", "Sword", 1, 90, 10, 10, true, false, true, false)], [], []);
        DaggerfallDialogueView view = new("d-1", "Hrolf", "Well met.", "normal", null, null,
            [new("news", "Any news?", Group: DaggerfallDialogueTopicGroups.Talk), new("donate", "Donate gold")], [])
        { Training = training, Merchant = merchant };

        Dictionary<string, object?> dialogue = Built(builder => DaggerfallHudProjection.Dialogue(builder, view));

        Assert.Equal(["talk", "quests", "places", "services"], Rows(dialogue["topicGroups"]).Select(group => (string)group["id"]!));
        Assert.Equal(["talk", "services"], Rows(dialogue["topics"]).Select(topic => (string)topic["group"]!));
        Dictionary<string, object?> trained = Assert.IsType<Dictionary<string, object?>>(dialogue["training"]);
        Assert.Equal(2d, Convert.ToDouble(trained["durationHours"]));
        Assert.Equal([true, false], Rows(trained["skills"]).Select(skill => (bool)skill["trainable"]!));
        Dictionary<string, object?> shop = Assert.IsType<Dictionary<string, object?>>(dialogue["merchant"]);
        Assert.Equal((double)DaggerfallMerchantService.LargeTradeGold, Convert.ToDouble(shop["largeTradeGold"]));
        Assert.Equal([40d, 1d], Rows(shop["stock"]).Select(item => Convert.ToDouble(item["maximumQuantity"])));
    }

    [Fact]
    public void The_makers_publish_the_limits_their_rules_enforce()
    {
        DaggerfallSpellMakerView spell = new("maker-1", "Mage", [], new("Freedom", 4, 0, 0, []), null);
        Dictionary<string, object?> limits = Assert.IsType<Dictionary<string, object?>>(
            Built(builder => DaggerfallHudProjection.SpellMaker(builder, spell))["limits"]);
        Assert.Equal(DaggerfallSpellConstruction.IconCount, Convert.ToInt32(limits["icons"]));
        Assert.Equal(DaggerfallSpellConstruction.MaximumEffects, Convert.ToInt32(limits["effects"]));
        Assert.Equal(DaggerfallSpellConstruction.MaximumDuration, Convert.ToInt32(limits["duration"]));
        Assert.Equal(DaggerfallSpellConstruction.MaximumPerLevels, Convert.ToInt32(limits["perLevels"]));
        Assert.Equal(DaggerfallSpellConstruction.MaximumNameLength, Convert.ToInt32(limits["nameLength"]));

        DaggerfallPotionMakerView potion = new("mix-1", "Apothecary", true, [], []);
        Dictionary<string, object?> maker = Built(builder => DaggerfallHudProjection.PotionMaker(builder, potion));
        Assert.Equal(DaggerfallPotionMaking.MinimumIngredients, Convert.ToInt32(maker["minimumIngredients"]));
        Assert.Equal(DaggerfallPotionMaking.MaximumIngredients, Convert.ToInt32(maker["maximumIngredients"]));
    }
}
