using WorldRpg.Rulesets.Daggerfall.Content;
using Rusty.Engine.Mechanics;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class PotionRecipeTests
{
    [Fact]
    public void All_twenty_loaded_recipes_resolve_real_compiled_payloads_and_exact_ingredients()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var recipes = TestPayload.Definitions.Magic.PotionRecipes;
        Assert.Equal(20, recipes.Count);
        Assert.Equal(Enumerable.Range(0, 20), recipes.Values.Select(value => value.ClassicIndex).Order());
        foreach (var recipe in recipes.Values)
        {
            recipe.Validate(TestPayload.Definitions.ItemTemplateCatalog);
            recipe.ValidateEffects(f.Session.State.Effects.Catalog);
            int[] ingredients = recipe.Ingredients.SelectMany(value => Enumerable.Repeat(value.Template, value.Count)).ToArray();
            Assert.True(recipe.Match(ingredients.Reverse()).Matches);
            Assert.Equal(recipe.Key, DaggerfallPotionRecipeDefinition.RecipeKey(ingredients.Reverse()));
            var missing = recipe.Match(ingredients.Skip(1));
            Assert.Equal(recipe.Ingredients[0] with { Count = 1 }, Assert.Single(missing.Missing));
            Assert.Empty(missing.Extra);
            var duplicate = recipe.Match(ingredients.Append(ingredients[0]));
            Assert.Empty(duplicate.Missing);
            Assert.Equal(recipe.Ingredients[0] with { Count = 1 }, Assert.Single(duplicate.Extra));
        }
    }

    [Fact]
    public void Similar_ingredients_are_not_substitutes_and_duplicate_counts_are_significant()
    {
        var recipe = TestPayload.Definitions.Magic.PotionRecipes[221871];
        var wrongWater = recipe.Match([60, 30, 27]);
        Assert.Equal(59, Assert.Single(wrongWater.Missing).Template);
        Assert.Equal(60, Assert.Single(wrongWater.Extra).Template);
        var duplicate = recipe with { Ingredients = [new(59, "template-59", 2)] };
        Assert.False(duplicate.Match([59]).Matches);
        Assert.True(duplicate.Match([59, 59]).Matches);
        Assert.False(duplicate.Match([59, 59, 59]).Matches);
    }

    [Fact]
    public void Potion_item_uses_published_recipe_and_encoded_restore_rejects_missing_definition()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random);
        var request = new DaggerfallItemCreateRequest("UselessItems1", "recipe-save", DaggerfallItemOwner.Player,
            TemplateIndex: 83, PotionRecipeKey: 221871);
        Assert.Throws<ArgumentException>(() => factory.Create(request with { PotionRecipeKey = 999 }));
        var stack = InventoryStackId.Parse("recipe-save");
        factory.Materialize(factory.Create(request), f.Session.State.Inventory, f.Session.State.ItemInstances, stack);
        using var restored = f.Restore();
        Assert.Equal(221871, restored.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player, stack).PotionRecipeKey);
        var saved = DaggerfallSavePayload.Read(f.Session.CaptureSave());
        foreach (int? recipeKey in new int?[] { 999, null })
        {
            var invalid = saved with { Inventory = saved.Inventory with { Stacks = [.. saved.Inventory.Stacks.Select(value => value.StackId == stack.Value
                ? value with { Metadata = value.Metadata with { PotionRecipeKey = recipeKey } } : value)] } };
            Assert.ThrowsAny<ArgumentException>(() => DaggerfallSession.Restore(f.Engine.Context, f.Composition, DaggerfallSavePayload.Encode(invalid)));
        }
    }

    [Fact]
    public void Malformed_ingredient_identity_and_settings_are_rejected()
    {
        var recipe = TestPayload.Definitions.Magic.PotionRecipes[221871];
        foreach (var bad in new[] { recipe with { Key = 1 }, recipe with { Ingredients = [new(9999, "template-9999", 1)] },
            recipe with { Ingredients = [new(59, "template-60", 1)] }, recipe with { Ingredients = [recipe.Ingredients[0], recipe.Ingredients[0]] },
            recipe with { Effects = [recipe.Effects[0] with { DurationPerLevel = 0 }] }, recipe with { Effects = [] } })
            Assert.Throws<ArgumentException>(() => bad.Validate(TestPayload.Definitions.ItemTemplateCatalog));
    }
}
