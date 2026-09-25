using System.Collections.ObjectModel;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Materializes the retained classic template records into the same typed item shape that Engine
/// inventory and save boundaries consume. Template instances keep material, condition, appearance,
/// and enchantment in <see cref="DaggerfallItemInstanceMetadata"/>; this definition carries only
/// stable template facts and the equipment policy already implemented by the reference product.
/// </summary>
internal static class DaggerfallTemplateItemDefinitions
{
    private const ulong StackMaximum = 1_000_000_000;

    private static readonly IReadOnlyDictionary<int, string> WeaponSources = new Dictionary<int, string>
    {
        [113] = "iron-dagger", [114] = "iron-tanto", [115] = "iron-staff", [116] = "iron-shortsword",
        [117] = "iron-wakazashi", [118] = "iron-broadsword", [119] = "iron-saber", [120] = "iron-longsword",
        [121] = "iron-katana", [122] = "iron-claymore", [123] = "iron-dai-katana", [124] = "iron-mace",
        [125] = "iron-flail", [126] = "iron-warhammer", [127] = "iron-battle-axe", [128] = "iron-war-axe",
        [129] = "iron-short-bow", [130] = "iron-long-bow",
    };

    private static readonly IReadOnlyDictionary<int, string> ArmorSources = new Dictionary<int, string>
    {
        [102] = "iron-cuirass", [103] = "iron-gauntlets", [104] = "iron-greaves", [105] = "iron-left-pauldron",
        [106] = "iron-right-pauldron", [107] = "iron-helm", [108] = "iron-boots", [109] = "buckler",
        [110] = "round-shield", [111] = "kite-shield", [112] = "tower-shield",
    };

    /// <summary>
    /// The native template an authored weapon, armor or ammunition item materializes from. An authored
    /// item carries the interpreted shape the product publishes; its native record carries the hit
    /// points and material scaling that become the instance's condition, so a caller that needs the
    /// authored condition asks for the template rather than inventing one from the item's name.
    /// </summary>
    internal static int? TemplateIndexForAuthoredItem(DaggerfallItemId id)
    {
        foreach ((int index, string authored) in WeaponSources)
            if (string.Equals(id.Value, authored, StringComparison.Ordinal)) return index;
        foreach ((int index, string authored) in ArmorSources)
            if (string.Equals(id.Value, authored, StringComparison.Ordinal)) return index;
        return string.Equals(id.Value, "arrow", StringComparison.Ordinal) ? 131 : null;
    }

    internal static IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> Create(
        DaggerfallItemTemplateSet templates,
        IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> authored,
        DaggerfallMagicCatalogSet magic)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(authored);
        Dictionary<DaggerfallItemId, DaggerfallItemDefinition> result = [];
        foreach (DaggerfallItemTemplateDefinition template in templates.Templates.Values.OrderBy(value => value.Index))
        {
            DaggerfallItemDefinition? source = SourceFor(template, authored);
            Add(result, template, source, "none");
            foreach (string material in MaterialsFor(template))
                Add(result, template, source, material);
        }
        AddMagicDefinitions(result, magic);
        return new ReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition>(result);
    }

    private static void AddMagicDefinitions(Dictionary<DaggerfallItemId, DaggerfallItemDefinition> result, DaggerfallMagicCatalogSet magic)
    {
        foreach (DaggerfallMagicItemDefinition magicItem in magic.MagicItems.Values.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            string[] categories = MagicCategories(magicItem);
            foreach (DaggerfallItemDefinition baseItem in result.Values.ToArray()
                .Where(item => item.Template is not null && item.Template.Index != 131 && !item.Id.Value.Contains("-magic-", StringComparison.Ordinal)
                    && categories.Any(category => item.Template.Groups.Contains(category, StringComparer.Ordinal))))
            {
                DaggerfallItemId id = new(DaggerfallMagicItemIds.For(baseItem.Id.Value, magicItem.Key));
                if (!result.TryAdd(id, new DaggerfallItemDefinition(id, DaggerfallItemKind.Unique, 1,
                        baseItem.Weight, magicItem.Value, baseItem.Weapon, baseItem.Armor, baseItem.Shield, baseItem.Equipment, baseItem.Template)))
                    throw new InvalidOperationException($"Magic definition '{id.Value}' is duplicated.");
            }
        }
    }

    private static string[] MagicCategories(DaggerfallMagicItemDefinition magicItem) => magicItem.Type == 0
        ? magicItem.Group switch
        {
            0 => ["Armor", "Weapons", "MensClothing", "ReligiousItems", "WomensClothing", "Gems", "Jewellery"],
            1 => ["Armor", "Weapons", "MensClothing", "WomensClothing", "Jewellery"],
            2 => ["Weapons"],
            _ => throw new InvalidOperationException($"Regular magic template '{magicItem.Key}' has unknown base-group selector {magicItem.Group}."),
        }
        : [MagicCategory(magicItem.Group)];

    private static string MagicCategory(int group) => group switch
    {
        2 => "Armor", 3 => "Weapons", 6 => "MensClothing", 7 => "Books", 10 => "ReligiousItems",
        12 => "WomensClothing", 14 => "Gems", 15 => "PlantIngredients1", 25 => "Jewellery",
        _ => throw new InvalidOperationException($"Magic template group {group} has no retained ItemBuilder category."),
    };

    private static void Add(Dictionary<DaggerfallItemId, DaggerfallItemDefinition> result,
        DaggerfallItemTemplateDefinition template, DaggerfallItemDefinition? source, string material)
    {
        DaggerfallItemId id = new(material == "none" ? $"template-{template.Index}" : $"template-{template.Index}-{material}");
        DaggerfallMaterializedItemProperties properties = material == "none"
            ? new(Weight(template.BaseWeight), template.BasePrice, template.HitPoints)
            : DaggerfallItemMaterialPolicy.Apply(template, material);
        DaggerfallWeaponDefinition? weapon = source?.Weapon is { } sourceWeapon
            ? sourceWeapon with { Material = material == "none" ? sourceWeapon.Material : material, Value = properties.Value, Weight = properties.Weight }
            : null;
        DaggerfallArmorDefinition? armor = source?.Armor is { } sourceArmor
            ? sourceArmor with { Material = material }
            : null;
        DaggerfallItemKind kind = template.Stackable ? DaggerfallItemKind.Fungible : DaggerfallItemKind.Unique;
        DaggerfallEquipmentDefinition? equipment = source?.Equipment ?? EquipmentFor(template);
        if (!result.TryAdd(id, new DaggerfallItemDefinition(id, kind, template.Stackable ? StackMaximum : 1,
                properties.Weight, properties.Value, weapon, armor, source?.Shield, equipment, template)))
            throw new InvalidOperationException($"Classic template definition '{id.Value}' is duplicated.");
    }

    private static DaggerfallEquipmentDefinition? EquipmentFor(DaggerfallItemTemplateDefinition template)
    {
        string? classification = template.Groups.Contains("Gems", StringComparer.Ordinal) ? "crystal"
            : template.Groups.Contains("Jewellery", StringComparer.Ordinal) ? template.Index switch
            {
                133 or 138 or 139 => "amulet", 134 => "bracer", 135 => "ring", 136 => "bracelet", 137 => "mark", _ => null,
            }
            : ClothingClassification(template);
        return classification is null ? null : new([classification], 1, null);
    }

    private static string? ClothingClassification(DaggerfallItemTemplateDefinition template)
    {
        if (template.Groups.Contains("MensClothing", StringComparer.Ordinal))
            return template.Index is >= 147 and <= 150 ? "feet"
                : template.Index is >= 151 and <= 153 or 156 or 162 or 174 or 175 ? "legs-clothes"
                : template.Index is 154 or 155 ? "cloak" : "chest-clothes";
        if (template.Groups.Contains("WomensClothing", StringComparer.Ordinal))
            return template.Index is >= 186 and <= 189 ? "feet"
                : template.Index is 190 or 193 or 199 or >= 211 and <= 213 ? "legs-clothes"
                : template.Index is 191 or 192 ? "cloak" : "chest-clothes";
        return null;
    }

    private static IEnumerable<string> MaterialsFor(DaggerfallItemTemplateDefinition template)
    {
        if (template.Groups.Contains("Weapons", StringComparer.Ordinal) && template.Index != 131)
            return DaggerfallItemMaterialPolicy.WeaponMaterials;
        if (template.Groups.Contains("Armor", StringComparer.Ordinal))
            return ["leather", "chain", .. DaggerfallItemMaterialPolicy.WeaponMaterials];
        return [];
    }

    private static DaggerfallItemDefinition? SourceFor(
        DaggerfallItemTemplateDefinition template,
        IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> authored)
    {
        if (WeaponSources.TryGetValue(template.Index, out string? weapon)) return Require(authored, weapon);
        if (ArmorSources.TryGetValue(template.Index, out string? armor)) return Require(authored, armor);
        if (template.Index == 131) return Require(authored, "arrow");
        if (template.Index == 276) return Require(authored, "gold-piece");
        return null;
    }

    private static DaggerfallItemDefinition Require(IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> authored, string id) =>
        authored.TryGetValue(new DaggerfallItemId(id), out DaggerfallItemDefinition? definition)
            ? definition
            : throw new InvalidOperationException($"Template materialization requires authored item '{id}'.");

    // ItemTemplates.txt expresses weights in the donor's 0.25 kg units. The current Engine
    // capacity convention is integral quarter-kilograms, matching the authored dagger (0.5 -> 2).
    private static int Weight(double kilograms) => checked((int)Math.Round(kilograms * 4, MidpointRounding.AwayFromZero));
}
