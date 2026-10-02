using System.Text.Json.Nodes;

namespace Daggerfall.Import.Arena2;

/// <summary>
/// Authored transcription of the retained effect classes' GetEnchantmentSettings tables. These settings
/// are absent from MAGIC.DEF. English display text is from the donor's Internal_Strings.csv; skill text
/// keys follow Utility/TextProvider.GetSkillName. Each published row names its effect-class source.
/// </summary>
internal static class Arena2EnchantmentSettings
{
    internal static JsonArray Build(JsonArray? spells = null)
    {
        JsonArray settings = [];
        AddFamily(10, "EnhancesSkill", "Enhances skill",
        [
            (0, 900, "medical", "medical", "Medical"),
            (1, 900, "etiquette", "etiquette", "Etiquette"),
            (2, 900, "streetwise", "streetwise", "Streetwise"),
            (3, 900, "jumping", "jumping", "Jumping"),
            (4, 900, "orcish", "orcishSkill", "Orcish"),
            (5, 900, "harpy", "harpy", "Harpy"),
            (6, 900, "giantish", "giantish", "Giantish"),
            (7, 900, "dragonish", "dragonish", "Dragonish"),
            (8, 900, "nymph", "nymph", "Nymph"),
            (9, 900, "daedric", "daedricSkill", "Daedric"),
            (10, 900, "spriggan", "spriggan", "Spriggan"),
            (11, 900, "centaurian", "centaurian", "Centaurian"),
            (12, 900, "impish", "impish", "Impish"),
            (13, 900, "lockpicking", "lockpicking", "Lockpicking"),
            (14, 900, "mercantile", "mercantile", "Mercantile"),
            (15, 900, "pickpocket", "pickpocket", "Pickpocketing"),
            (16, 900, "stealth", "stealth", "Stealth"),
            (17, 900, "swimming", "swimming", "Swimming"),
            (18, 900, "climbing", "climbing", "Climbing"),
            (19, 900, "backstabbing", "backstabbing", "Backstabbing"),
            (20, 900, "dodging", "dodging", "Dodging"),
            (21, 900, "running", "running", "Running"),
            (22, 900, "destruction", "destruction", "Destruction"),
            (23, 900, "restoration", "restoration", "Restoration"),
            (24, 900, "illusion", "illusion", "Illusion"),
            (25, 900, "alteration", "alteration", "Alteration"),
            (26, 900, "thaumaturgy", "thaumaturgy", "Thaumaturgy"),
            (27, 900, "mysticism", "mysticism", "Mysticism"),
            (28, 900, "short-blade", "shortBlade", "Short Blade"),
            (29, 900, "long-blade", "longBlade", "Long Blade"),
            (30, 900, "hand-to-hand", "handToHand", "Hand-to-Hand"),
            (31, 900, "axe", "axe", "Axe"),
            (32, 900, "blunt-weapon", "bluntWeapon", "Blunt Weapon"),
            (33, 900, "archery", "archery", "Archery"),
            (34, 900, "critical-strike", "criticalStrike", "Critical Strike"),
        ]);
        AddFamily(3, "ExtraSpellPts", "Extra spell pts",
        [
            (0, 500, "during-winter", "duringWinter", "during Winter"),
            (1, 500, "during-spring", "duringSpring", "during Spring"),
            (2, 500, "during-summer", "duringSummer", "during Summer"),
            (3, 500, "during-fall", "duringFall", "during Fall"),
            (4, 200, "during-full-moon", "duringFullMoon", "during Full Moon"),
            (5, 200, "during-half-moon", "duringHalfMoon", "during Half Moon"),
            (6, 200, "during-new-moon", "duringNewMoon", "during New Moon"),
            (7, 700, "near-undead", "nearUndead", "near undead"),
            (8, 800, "near-daedra", "nearDaedra", "near Daedra"),
            (9, 900, "near-humanoids", "nearHumanoids", "near humanoids"),
            (10, 1000, "near-animals", "nearAnimals", "near animals"),
        ]);
        AddFamily(5, "RegensHealth", "Regens health",
        [
            (0, 4000, "all-the-time", "allTheTime", "all the time"),
            (1, 3000, "in-sunlight", "inSunlight", "in sunlight"),
            (2, 3000, "in-darkness", "inDarknessLower", "in darkness"),
        ]);
        AddFamily(12, "StrengthensArmor", "Strengthens armor",
        [
            (-1, 700, "strengthened-armor", null, null),
        ]);
        AddFamily(9, "AbsorbsSpells", "Absorbs spells", [(-1, 1500, "spell-absorption", null, null)]);
        AddFamily(8, "RepairsObjects", "Repairs objects",
        [
            (-1, 900, "repairs-objects", null, null),
        ]);
        AddFamily(24, "WeakensArmor", "Weakens armor",
        [
            (-1, -700, "weakened-armor", null, null),
        ]);
        AddFamily(16, "ItemDeteriorates", "Item deteriorates",
        [
            (0, -3000, "all-the-time", "allTheTime", "all the time"),
            (1, -1500, "in-sunlight", "inSunlight", "in sunlight"),
            (2, -500, "in-holy-places", "inHolyPlaces", "in holy places"),
        ]);
        AddFamily(17, "UserTakesDamage", "User takes damage",
        [
            (0, -6000, "in-sunlight", "inSunlight", "in sunlight"),
            (1, -1000, "in-holy-places", "inHolyPlaces", "in holy places"),
        ]);
        AddFamily(7, "IncreasedWeightAllowance", "Increased Weight Allowance",
        [
            (0, 400, "one-quarter-more", "add25Percent", "25% additional"),
            (1, 600, "one-half-more", "add50Percent", "50% additional"),
        ]);
        AddFamily(13, "ImprovesTalents", "Improves talents",
        [
            (0, 500, "improved-acute-hearing", "hearing", "hearing"),
            (1, 600, "improved-athleticism", "athleticismLower", "athleticism"),
            (2, 600, "improved-adrenaline-rush", "adrenalineRushLower", "adrenaline rush"),
        ]);
        AddFamily(14, "GoodRepWith", "Good rep with",
        [
            (0, 1000, "commoners", "commoners", "Commoners"),
            (1, 1000, "merchants", "merchants", "Merchants"),
            (2, 1000, "scholars", "scholars", "Scholars"),
            (3, 1000, "nobility", "nobility", "Nobility"),
            (4, 1000, "underworld", "underworld", "Underworld"),
            (5, 5000, "all", "all", "All"),
        ]);
        AddFamily(25, "BadRepWith", "Bad rep with",
        [
            (0, -1000, "commoners", "commoners", "Commoners"),
            (1, -1000, "merchants", "merchants", "Merchants"),
            (2, -1000, "scholars", "scholars", "Scholars"),
            (3, -1000, "nobility", "nobility", "Nobility"),
            (4, -1000, "underworld", "underworld", "Underworld"),
            (5, -5000, "all", "all", "All"),
        ]);
        AddFamily(22, "BadReactionsFrom", "Bad reactions from",
        [
            (0, -120, "from-humanoids", "fromHumanoids", "from humanoids"),
            (1, -80, "from-animals", "fromAnimals", "from animals"),
            (2, -120, "from-daedra", "fromDaedra", "from Daedra"),
        ]);
        AddSpellFamily(0, "CastWhenUsed", "cast-when-used",
            [4, 5, 6, 7, 8, 9, 10, 18, 11, 12, 13, 19, 14, 15, 16, 17, 22, 23, 24, 20, 25, 26, 33, 27, 28, 29, 34, 30, 31, 35, 36, 32, 40, 64, 60, 94],
            [330, 250, 540, 480, 380, 480, 1650, 900, 1560, 1560, 1560, 1740, 470, 1020, 990, 1040, 1980, 1530, 920, 1420, 840, 1650, 1020, 1300, 2290, 1020, 1610, 1930, 760, 2140, 3030, 1750, 130, 360, 930, 480]);
        AddSpellFamily(1, "CastWhenHeld", "cast-when-held",
            [37, 39, 41, 10, 42, 11, 12, 26, 13, 6, 44, 45, 46, 24, 47, 4, 49, 82, 83, 84, 85, 86, 87, 88, 89],
            [240, 1230, 170, 1650, 170, 1560, 1560, 1560, 1560, 540, 210, 150, 1720, 920, 1720, 330, 1590, 1020, 1200, 1200, 1200, 1200, 1200, 1200, 1200]);
        AddSpellFamily(2, "CastWhenStrikes", "cast-when-strikes",
            [50, 53, 52, 54, 56, 33, 20, 25, 16, 7, 55, 67],
            [1620, 780, 1380, 930, 1830, 1020, 840, 840, 990, 480, 4230, 1260]);
        return settings;

        void AddSpellFamily(int type, string sourceClass, string meaning, int[] identities, int[] costs)
        {
            for (int index = 0; index < identities.Length; index++)
            {
                int identity = identities[index];
                JsonNode? spell = spells?.FirstOrDefault(value => value!["identity"]!.GetValue<int>() == identity);
                settings.Add(new JsonObject
                {
                    ["key"] = $"enchantment.{type}.{identity}", ["type"] = type, ["param"] = identity,
                    ["cost"] = costs[index], ["meaning"] = meaning,
                    ["displayName"] = $"{sourceClass}: {spell?["name"]?.GetValue<string>() ?? $"classic spell {identity}"}",
                    ["textKey"] = sourceClass, ["parameterTextKey"] = $"spell.{identity}",
                    ["sourceClass"] = $"donor:Assets/Scripts/Game/MagicAndEffects/Effects/Enchanting/{sourceClass}.cs#GetEnchantmentSettings",
                    ["parameterVariants"] = new JsonArray([.. identities.Select(value => (JsonNode?)JsonValue.Create(value))]),
                    ["spell"] = spell?["key"]?.GetValue<string>(),
                    ["spellIdentityShared"] = spell?["identityShared"]?.GetValue<bool>() ?? false,
                });
            }
        }


        void AddFamily(int type, string sourceClass, string primaryDisplayName,
            (int Param, int Cost, string Meaning, string? TextKey, string? DisplayName)[] variants)
        {
            foreach (var variant in variants)
                settings.Add(new JsonObject
                {
                    ["key"] = $"enchantment.{type}.{variant.Param}",
                    ["type"] = type,
                    ["param"] = variant.Param,
                    ["cost"] = variant.Cost,
                    ["meaning"] = variant.Meaning,
                    ["displayName"] = variant.DisplayName is null ? primaryDisplayName : $"{primaryDisplayName}: {variant.DisplayName}",
                    ["textKey"] = sourceClass,
                    ["parameterTextKey"] = variant.TextKey,
                    ["sourceClass"] = $"donor:Assets/Scripts/Game/MagicAndEffects/Effects/Enchanting/{sourceClass}.cs#GetEnchantmentSettings",
                    ["parameterVariants"] = new JsonArray([.. variants.Select(value => (JsonNode?)JsonValue.Create(value.Param))]),
                });
        }
    }
}
