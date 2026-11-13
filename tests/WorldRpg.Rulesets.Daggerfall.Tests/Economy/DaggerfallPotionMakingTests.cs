using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The potion maker built alone over its inputs; saves are the session's (PotionMakingSessionTests).</summary>
public sealed class DaggerfallPotionMakingTests
{
    [Fact]
    public void A_recipe_mixed_from_pack_and_wagon_ingredients_makes_one_potion_and_a_second_joins_its_stack()
    {
        using GuildMakerFixture f = new();
        DaggerfallServiceProvider provider = f.Provider("make-potions", 108, 840, 3);
        DaggerfallPotionRecipeDefinition recipe = f.Definitions.Magic.PotionRecipes[4975678];
        DaggerfallPotionMaking maker = f.PotionMaker();
        for (int brew = 0; brew < 2; brew++)
        {
            int index = 0;
            foreach (var ingredient in recipe.Ingredients) f.Ingredient(ingredient.Template, inWagon: index++ % 2 == 1);
            DaggerfallPotionMakingResult result = maker.Mix(provider, [.. recipe.Ingredients.Select(value => value.Template)]);
            Assert.Equal(new DaggerfallPotionMakingResult(true, "PotionMade", recipe.Key), result);
        }

        Assert.Empty(maker.ReadIngredients());
        var potion = Assert.Single(f.Inventory.Read().Stacks, value => value.Definition.Value == "template-83");
        Assert.Equal(2UL, potion.Quantity);
        Assert.Equal(recipe.Key, f.Instances.RequireStack(DaggerfallItemOwner.Player, potion.Id).PotionRecipeKey);
    }

    [Fact]
    public void Missing_ingredients_and_lost_membership_keep_the_ingredients_but_a_failed_mixture_uses_them()
    {
        using GuildMakerFixture f = new();
        DaggerfallServiceProvider provider = f.Provider("make-potions", 108, 840, 3);
        DaggerfallPotionMaking maker = f.PotionMaker();
        f.Ingredient(62);
        f.Ingredient(31, inWagon: true);

        Assert.Equal("InsufficientIngredients", maker.Mix(provider, [62, 31, 56]).Outcome);
        Assert.Equal("InvalidIngredients", maker.Mix(provider, []).Outcome);
        Assert.Equal(2, maker.ReadIngredients().Length);
        f.Social.ExpelGuild(108);
        Assert.False(maker.CanUse(provider));
        Assert.Equal("ProviderUnavailable", maker.Mix(provider, [62, 31]).Outcome);
        Assert.Equal(2, maker.ReadIngredients().Length);

        f.Social.JoinGuild(108, 0);
        for (int rank = 0; rank < 3; rank++) f.Social.PromoteGuild(108, 0);
        Assert.Equal("MixtureFailed", maker.Mix(provider, [62, 31]).Outcome);
        Assert.Empty(maker.ReadIngredients());
        Assert.DoesNotContain(f.Inventory.Read().Stacks, item => item.Definition.Value == "template-83");
    }
}
