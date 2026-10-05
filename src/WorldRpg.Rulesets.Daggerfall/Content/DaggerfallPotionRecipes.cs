namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallPotionIngredient(int Template, string Item, int Count);
internal sealed record DaggerfallPotionSpellPointRestore(string Effect, int BaseLow, int BaseHigh, int LevelBase, int LevelHigh, int PerLevel);
internal sealed record DaggerfallPotionRecipeMatch(DaggerfallPotionIngredient[] Missing, DaggerfallPotionIngredient[] Extra)
{
    internal bool Matches => Missing.Length == 0 && Extra.Length == 0;
}

/// <summary>One normalized recipe. Ingredient equivalence is an exact multiset, never a hash-only match.</summary>
internal sealed record DaggerfallPotionRecipeDefinition(int Key, int ClassicIndex, string Name, string TextKey,
    int Price, int TextureRecord, string SourceClass, DaggerfallPotionIngredient[] Ingredients,
    DaggerfallSpellEffectDefinition[] Effects, DaggerfallPotionSpellPointRestore? SpellPointRestore)
{
    internal DaggerfallPotionRecipeMatch Match(IEnumerable<int> selected)
    {
        var actual = selected.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
        var expected = Ingredients.ToDictionary(value => value.Template, value => value.Count);
        DaggerfallPotionIngredient[] Difference(IReadOnlyDictionary<int, int> left, IReadOnlyDictionary<int, int> right) =>
            [.. left.Where(value => value.Value > right.GetValueOrDefault(value.Key)).OrderBy(value => value.Key)
                .Select(value => new DaggerfallPotionIngredient(value.Key, $"template-{value.Key}", value.Value - right.GetValueOrDefault(value.Key)))];
        return new(Difference(expected, actual), Difference(actual, expected));
    }

    internal void Validate(DaggerfallItemTemplateSet templates)
    {
        if (Key <= 0 || ClassicIndex < 0 || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(TextKey)
            || Price < 0 || TextureRecord is < 0 or > 35 || string.IsNullOrWhiteSpace(SourceClass)
            || Ingredients.Length == 0 || Ingredients.Select(value => value.Template).Distinct().Count() != Ingredients.Length)
            throw new ArgumentException($"Potion recipe {Key} has invalid identity, presentation or ingredient rows.");
        foreach (var ingredient in Ingredients)
            if (ingredient.Count <= 0 || ingredient.Item != $"template-{ingredient.Template}"
                || !templates.Templates.TryGetValue(ingredient.Template, out var template) || !template.IsIngredient)
                throw new ArgumentException($"Potion recipe {Key} names unavailable ingredient {ingredient.Item}.");
        if (RecipeKey(Ingredients.SelectMany(value => Enumerable.Repeat(value.Template, value.Count))) != Key)
            throw new ArgumentException($"Potion recipe {Key} does not match its exact ingredient identity.");
        if ((Effects.Length == 0) == (SpellPointRestore is null))
            throw new ArgumentException($"Potion recipe {Key} requires either spell payloads or named spell-point restoration.");
        foreach (var effect in Effects)
            if (string.IsNullOrWhiteSpace(effect.Key) || effect.Type < 0 || effect.SubType < -1
                || effect.DurationBase < 0 || effect.DurationMod < 0 || effect.DurationPerLevel <= 0
                || effect.ChanceBase < 0 || effect.ChanceMod < 0 || effect.ChancePerLevel <= 0
                || effect.MagnitudeBaseLow < 0 || effect.MagnitudeBaseHigh < effect.MagnitudeBaseLow
                || effect.MagnitudeLevelBase < 0 || effect.MagnitudeLevelHigh < effect.MagnitudeLevelBase || effect.MagnitudePerLevel <= 0)
                throw new ArgumentException($"Potion recipe {Key} has invalid effect settings.");
        if (SpellPointRestore is { } restore && (restore.Effect != "heal-spell-points" || restore.BaseLow < 0
            || restore.BaseHigh < restore.BaseLow || restore.LevelBase < 0 || restore.LevelHigh < restore.LevelBase || restore.PerLevel <= 0))
            throw new ArgumentException($"Potion recipe {Key} has invalid spell-point restoration.");
    }

    internal void ValidateEffects(DaggerfallEffectCatalog catalog)
    {
        foreach (var effect in Effects)
            if (!catalog.TryResolveSpell(effect, out var compiled) || compiled.Key != effect.Key)
                throw new ArgumentException($"Potion recipe {Key} names unavailable compiled effect '{effect.Key}'.");
        if (SpellPointRestore is { } restore) _ = catalog.Require(restore.Effect);
    }

    internal static int RecipeKey(IEnumerable<int> ingredients)
    {
        int key = 17;
        foreach (int ingredient in ingredients.Order()) key = unchecked(key * 23 + ingredient);
        return key;
    }
}
