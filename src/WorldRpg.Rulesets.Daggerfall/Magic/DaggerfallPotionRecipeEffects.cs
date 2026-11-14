using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Whether the effects a potion recipe names are ones the effect catalog has compiled.</summary>
internal static class DaggerfallPotionRecipeEffects
{
    internal static void ValidateEffects(this DaggerfallPotionRecipeDefinition recipe, DaggerfallEffectCatalog catalog)
    {
        foreach (var effect in recipe.Effects)
            if (!catalog.TryResolveSpell(effect, out var compiled) || compiled.Key != effect.Key)
                throw new ArgumentException($"Potion recipe {recipe.Key} names unavailable compiled effect '{effect.Key}'.");
        if (recipe.SpellPointRestore is { } restore) _ = catalog.Require(restore.Effect);
    }
}
