using System.Text.Json.Nodes;

namespace Daggerfall.Import.Arena2;

/// <summary>Classic potion recipes from the donor effect constructors, normalized offline.</summary>
internal static class Arena2PotionRecipes
{
    internal static JsonArray Build()
    {
        JsonArray rows = [];
        Add("stamina", "Stamina", 25, 11, "Restoration/HealFatigue", [59, 30, 27], [Effect("heal-fatigue", 10, 9, magnitude: [5, 5, 4, 4, 1])]);
        Add("orcStrength", "Orc Strength", 50, 13, "Restoration/FortifyStrength", [61, 71, 59], [Effect("fortify-strength", 9, 0, magnitude: [1, 1, 14, 14, 1])]);
        Add("healing", "Healing", 50, 15, "Restoration/HealHealth", [62, 16, 65, 42], [Effect("heal-health", 10, 8, magnitude: [5, 5, 9, 9, 1])]);
        Add("waterWalking", "Waterwalking", 50, 32, "Thaumaturgy/WaterWalking", [59, 29, 20, 69], [Effect("water-walking", 31, -1)]);
        Add("restorePower", "Restore Power", 75, 12, "Restoration/HealSpellPoints", [63, 73, 33, 54], [], new JsonObject
        {
            ["effect"] = "heal-spell-points", ["baseLow"] = 5, ["baseHigh"] = 5, ["levelBase"] = 4, ["levelHigh"] = 4, ["perLevel"] = 1,
        });
        Add("resistFire", "Resist Fire", 75, 34, "Alteration/ElementalResistance", [64, 7, 10, 35, 32], [Effect("resist-fire", 8, 0, chance: [100, 1, 1])]);
        Add("resistFrost", "Resist Frost", 75, 34, "Alteration/ElementalResistance", [64, 5, 14, 22], [Effect("resist-frost", 8, 1, chance: [100, 1, 1])]);
        Add("resistShock", "Resist Shock", 75, 34, "Alteration/ElementalResistance", [64, 68, 16], [Effect("resist-shock", 8, 3, chance: [100, 1, 1])]);
        Add("cureDisease", "Cure Disease", 100, 35, "Restoration/CureDisease", [62, 31, 56], [Effect("cure-disease", 3, 0, chance: [1, 10, 1])]);
        Add("slowFalling", "Slow Falling", 100, 11, "Alteration/Slowfall", [59, 26, 24], [Effect("slowfall", 25, -1)]);
        Add("waterBreathing", "Water Breathing", 100, 32, "Alteration/WaterBreathing", [60, 62, 76], [Effect("water-breathing", 30, -1)]);
        Add("healTrue", "Heal True", 100, 16, "Restoration/HealHealth", [62, 16, 14, 37], [Effect("heal-health", 10, 8, magnitude: [5, 5, 19, 19, 1])]);
        Add("levitation", "Levitation", 125, 11, "Thaumaturgy/Levitate", [59, 63, 39], [Effect("levitate", 14, -1)]);
        Add("resistPoison", "Resist Poison", 125, 14, "Alteration/ElementalResistance", [64, 43, 25], [Effect("resist-diseaseorpoison", 8, 2, chance: [5, 19, 1])]);
        Add("freeAction", "Free Action", 125, 14, "Restoration/FreeAction", [64, 41, 8, 28], [Effect("free-action", 26, -1, chance: [5, 19, 1])]);
        Add("curePoison", "Cure Poison", 200, 35, "Restoration/CurePoison", [64, 47, 58, 77], [Effect("cure-poison", 3, 1, chance: [5, 19, 1])]);
        Add("chameleonForm", "Chameleon Form", 200, 33, "Illusion/ChameleonNormal", [60, 63, 9, 11, 16], [Effect("chameleon-normal", 23, 0)]);
        Add("shadowForm", "Shadow Form", 200, 33, "Illusion/ShadowNormal", [60, 63, 6, 21], [Effect("shadow-normal", 24, 0)]);
        Add("invisibility", "Invisibility", 250, 33, "Illusion/InvisibilityNormal", [60, 63, 39, 3], [Effect("invisibility-normal", 13, 0)]);
        Add("purification", "Purification", 500, 35, "Restoration/CureDisease", [62, 63, 60, 31, 56, 39, 3, 49],
            [Effect("cure-disease", 3, 0, chance: [1, 10, 1], magnitude: [5, 5, 19, 19, 1]),
             Effect("heal-health", 10, 8, chance: [1, 10, 1], magnitude: [5, 5, 19, 19, 1]),
             Effect("invisibility-normal", 13, 0, chance: [1, 10, 1], magnitude: [5, 5, 19, 19, 1])]);
        return rows;

        void Add(string textKey, string name, int price, int texture, string source, int[] ingredients, JsonObject[] effects, JsonObject? spellPoints = null)
        {
            Array.Sort(ingredients);
            int key = 17;
            foreach (int ingredient in ingredients) key = unchecked(key * 23 + ingredient);
            rows.Add(new JsonObject
            {
                ["key"] = key, ["classicIndex"] = rows.Count, ["name"] = name, ["textKey"] = textKey,
                ["price"] = price, ["textureRecord"] = texture,
                ["sourceClass"] = $"daggerfall-unity/Assets/Scripts/Game/MagicAndEffects/Effects/{source}.cs#SetPotionProperties",
                ["ingredients"] = new JsonArray([.. ingredients.Select(id => (JsonNode?)new JsonObject { ["template"] = id, ["item"] = $"template-{id}", ["count"] = 1 })]),
                ["effects"] = new JsonArray([.. effects.Cast<JsonNode?>()]), ["spellPointRestore"] = spellPoints,
            });
        }
    }

    private static JsonObject Effect(string key, int type, int subType, int[]? chance = null, int[]? magnitude = null)
    {
        chance ??= [1, 1, 1]; magnitude ??= [1, 1, 1, 1, 1];
        return new JsonObject
        {
            ["key"] = key, ["type"] = type, ["subType"] = subType,
            ["duration"] = Triple([1, 1, 1]), ["chance"] = Triple(chance),
            ["magnitude"] = new JsonObject { ["baseLow"] = magnitude[0], ["baseHigh"] = magnitude[1], ["levelBase"] = magnitude[2], ["levelHigh"] = magnitude[3], ["perLevel"] = magnitude[4] },
        };
    }
    private static JsonObject Triple(int[] values) => new() { ["base"] = values[0], ["mod"] = values[1], ["perLevel"] = values[2] };
}
