using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>How a native template target is accounted for.</summary>
public enum DaggerfallItemTemplateDisposition
{
    /// <summary>The template resolved through the substitute table.</summary>
    Substitute,
    /// <summary>The template names no group any donor enum or the substitute states.</summary>
    Unresolved,
}

/// <summary>One published item template: substitute fields with group, stack and reference facts.</summary>
/// <param name="Index">The template index.</param>
/// <param name="Name">The template name.</param>
/// <param name="Groups">The donor groups that claim the index, primary first.</param>
/// <param name="Disposition">How the target resolved.</param>
/// <param name="BaseWeight">The base weight.</param>
/// <param name="HitPoints">The base condition.</param>
/// <param name="CapacityOrTarget">The capacity or target value.</param>
/// <param name="BasePrice">The base price.</param>
/// <param name="EnchantmentPoints">The enchantment points.</param>
/// <param name="Rarity">The rarity.</param>
/// <param name="Variants">The texture variants.</param>
/// <param name="IsBluntWeapon">Whether the template is a blunt weapon.</param>
/// <param name="IsLiquid">Whether the template is liquid.</param>
/// <param name="IsOneHanded">Whether the template is one-handed.</param>
/// <param name="IsIngredient">Whether the template is an ingredient.</param>
/// <param name="Stackable">Whether instances stack by the donor's rule.</param>
/// <param name="WorldTextureArchive">The world texture archive.</param>
/// <param name="WorldTextureRecord">The world texture record.</param>
/// <param name="PlayerTextureArchive">The player texture archive.</param>
/// <param name="PlayerTextureRecord">The player texture record.</param>
/// <param name="WeaponMediaId">The 7943 weapon media for weapon templates, empty otherwise.</param>
/// <param name="DrawOrderOrEffect">The draw order or effect value.</param>
public sealed record DaggerfallItemTemplate(
    int Index,
    string Name,
    IReadOnlyList<string> Groups,
    DaggerfallItemTemplateDisposition Disposition,
    double BaseWeight,
    int HitPoints,
    int CapacityOrTarget,
    int BasePrice,
    int EnchantmentPoints,
    int Rarity,
    int Variants,
    bool IsBluntWeapon,
    bool IsLiquid,
    bool IsOneHanded,
    bool IsIngredient,
    bool Stackable,
    int WorldTextureArchive,
    int WorldTextureRecord,
    int PlayerTextureArchive,
    int PlayerTextureRecord,
    string WeaponMediaId,
    int DrawOrderOrEffect)
{
    public void Validate()
    {
        if (Index is < 0 or > 287)
        {
            throw new ArgumentOutOfRangeException(nameof(Index), Index, "A published item template names no native index.");
        }

        if (string.IsNullOrWhiteSpace(Name) || Groups.Count == 0)
        {
            throw new ArgumentException($"Item template {Index} states no name or no group.", nameof(Name));
        }

        if (!Enum.IsDefined(Disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(Disposition), Disposition, "A published item template names a disposition the contract does not declare.");
        }
    }
}

/// <summary>One published magic template: its group placement, material and enchantments.</summary>
/// <param name="Index">The magic template index.</param>
/// <param name="Name">The magic template name.</param>
/// <param name="Type">The magic item type.</param>
/// <param name="Group">The group in item templates.</param>
/// <param name="GroupName">The donor group name, empty when outside its table.</param>
/// <param name="GroupIndex">The group index in item templates.</param>
/// <param name="Enchantments">The non-padding enchantments with their spell or effect params.</param>
/// <param name="Uses">The uses and item condition.</param>
/// <param name="Value">The value, used for artifacts.</param>
/// <param name="Material">The material.</param>
public sealed record DaggerfallMagicTemplate(
    int Index,
    string Name,
    string Type,
    int Group,
    string GroupName,
    int GroupIndex,
    IReadOnlyList<DaggerfallMagicEnchantment> Enchantments,
    int Uses,
    int Value,
    int Material)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Type))
        {
            throw new ArgumentException($"Magic template {Index} states no name or no type.", nameof(Name));
        }
    }
}

/// <summary>One non-padding enchantment: its type and spell or effect parameter.</summary>
/// <param name="Type">The enchantment type.</param>
/// <param name="Param">A spell ID, artifact effect identifier, or enemy/social parameter.</param>
public sealed record DaggerfallMagicEnchantment(string Type, int Param);

/// <summary>The normalized item template catalog.</summary>
/// <param name="Source">The substitute source the catalog was read from.</param>
/// <param name="Templates">The 288 templates in index order.</param>
/// <param name="Magic">The magic templates in index order.</param>
public sealed record DaggerfallItemTemplates(
    DaggerfallTextSource Source,
    IReadOnlyList<DaggerfallItemTemplate> Templates,
    IReadOnlyList<DaggerfallMagicTemplate> Magic)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        Source.Validate();
        if (Templates.Count != 288)
        {
            throw new InvalidOperationException($"The item catalog carries {Templates.Count} templates for 288 native targets.");
        }

        NormalizedImportDocument.ValidateUnique(Templates, template => template.Index.ToString(), "item templates");
        foreach (DaggerfallItemTemplate template in Templates)
        {
            template.Validate();
        }

        foreach (DaggerfallMagicTemplate magic in Magic)
        {
            magic.Validate();
        }
    }
}

/// <summary>
/// Builds the normalized item template catalog from the donor's exported tables. Groups follow
/// the donor's group enums with native order and names behind the nine the enums never state;
/// stackability follows the donor's rule; weapon templates reference the 7943 weapon media
/// through the donor's weapon-type mapping; magic templates carry their enchantment spell and
/// effect params for the spell catalog to resolve. Nothing here decodes native bytes: every
/// record carries substitute provenance, and the ledger targets resolve to it.
/// </summary>
public static class DaggerfallItemTemplatesBuilder
{
    /// <summary>The inventory family the native targets are documented under.</summary>
    public const string ItemsFamily = "CNT-011";

    /// <summary>Template indices that stack as arrows, oil, gold, bottles and books do.</summary>
    private const int ArrowTemplate = 131;
    private const int OilTemplate = 252;
    private const int GoldTemplate = 276;
    private const int BottleTemplate = 83;

    /// <summary>
    /// The donor groups that claim each flat template index, primary first: the group enums with
    /// native order and names behind the nine no enum states (99-101 Deeds; 246, 250, 251, 266,
    /// 272, 273 Furniture by native contiguity and names). Eleven indices sit in two groups on
    /// both sides of the donor, and both are published with the lower group first.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string[]> TemplateGroups = new Dictionary<int, string[]>
    {
        [0] = ["Gems"],
        [1] = ["Gems"],
        [2] = ["Gems"],
        [3] = ["Gems"],
        [4] = ["Gems"],
        [5] = ["Gems"],
        [6] = ["Gems"],
        [7] = ["Gems"],
        [8] = ["PlantIngredients1", "PlantIngredients2"],
        [9] = ["PlantIngredients1", "PlantIngredients2"],
        [10] = ["PlantIngredients1", "PlantIngredients2"],
        [11] = ["PlantIngredients1", "PlantIngredients2"],
        [12] = ["PlantIngredients1", "PlantIngredients2"],
        [13] = ["PlantIngredients1", "PlantIngredients2"],
        [14] = ["PlantIngredients1"],
        [15] = ["PlantIngredients1", "PlantIngredients2"],
        [16] = ["PlantIngredients1", "PlantIngredients2"],
        [17] = ["PlantIngredients1", "PlantIngredients2"],
        [18] = ["PlantIngredients1"],
        [19] = ["PlantIngredients1"],
        [20] = ["PlantIngredients1"],
        [21] = ["PlantIngredients2"],
        [22] = ["PlantIngredients2"],
        [23] = ["PlantIngredients1"],
        [24] = ["PlantIngredients2"],
        [25] = ["PlantIngredients1"],
        [26] = ["PlantIngredients2"],
        [27] = ["PlantIngredients2"],
        [28] = ["PlantIngredients2"],
        [29] = ["PlantIngredients2"],
        [30] = ["PlantIngredients2"],
        [31] = ["PlantIngredients2"],
        [32] = ["PlantIngredients2"],
        [33] = ["CreatureIngredients1"],
        [34] = ["CreatureIngredients3"],
        [35] = ["CreatureIngredients1"],
        [36] = ["CreatureIngredients3"],
        [37] = ["CreatureIngredients3"],
        [38] = ["CreatureIngredients1"],
        [39] = ["CreatureIngredients1"],
        [40] = ["CreatureIngredients1"],
        [41] = ["CreatureIngredients1"],
        [42] = ["CreatureIngredients1"],
        [43] = ["CreatureIngredients1"],
        [44] = ["CreatureIngredients1"],
        [45] = ["CreatureIngredients1"],
        [46] = ["CreatureIngredients2"],
        [47] = ["CreatureIngredients2"],
        [48] = ["CreatureIngredients2"],
        [49] = ["CreatureIngredients2"],
        [50] = ["CreatureIngredients1"],
        [51] = ["CreatureIngredients1"],
        [52] = ["CreatureIngredients2"],
        [53] = ["CreatureIngredients1"],
        [54] = ["CreatureIngredients1"],
        [55] = ["MiscellaneousIngredients1"],
        [56] = ["MiscellaneousIngredients1"],
        [57] = ["MiscellaneousIngredients1"],
        [58] = ["MiscellaneousIngredients1"],
        [59] = ["MiscellaneousIngredients1"],
        [60] = ["MiscellaneousIngredients1"],
        [61] = ["CreatureIngredients1"],
        [62] = ["MiscellaneousIngredients1"],
        [63] = ["MiscellaneousIngredients1"],
        [64] = ["MiscellaneousIngredients1"],
        [65] = ["MetalIngredients"],
        [66] = ["MetalIngredients"],
        [67] = ["MetalIngredients"],
        [68] = ["MetalIngredients"],
        [69] = ["MetalIngredients"],
        [70] = ["MetalIngredients"],
        [71] = ["MetalIngredients"],
        [72] = ["MetalIngredients"],
        [73] = ["MetalIngredients"],
        [74] = ["MetalIngredients"],
        [75] = ["MetalIngredients"],
        [76] = ["MiscellaneousIngredients2"],
        [77] = ["MiscellaneousIngredients2"],
        [78] = ["Drugs"],
        [79] = ["Drugs"],
        [80] = ["Drugs"],
        [81] = ["Drugs"],
        [82] = ["UselessItems1"],
        [83] = ["UselessItems1"],
        [84] = ["UselessItems1"],
        [85] = ["UselessItems1"],
        [86] = ["UselessItems1"],
        [87] = ["UselessItems1"],
        [88] = ["UselessItems1"],
        [89] = ["UselessItems1"],
        [90] = ["UselessItems1"],
        [91] = ["UselessItems1"],
        [92] = ["UselessItems1"],
        [93] = ["Transportation"],
        [94] = ["Transportation"],
        [95] = ["Transportation"],
        [96] = ["Transportation"],
        [97] = ["Transportation"],
        [98] = ["Transportation"],
        [99] = ["Deeds"],
        [100] = ["Deeds"],
        [101] = ["Deeds"],
        [102] = ["Armor"],
        [103] = ["Armor"],
        [104] = ["Armor"],
        [105] = ["Armor"],
        [106] = ["Armor"],
        [107] = ["Armor"],
        [108] = ["Armor"],
        [109] = ["Armor"],
        [110] = ["Armor"],
        [111] = ["Armor"],
        [112] = ["Armor"],
        [113] = ["Weapons"],
        [114] = ["Weapons"],
        [115] = ["Weapons"],
        [116] = ["Weapons"],
        [117] = ["Weapons"],
        [118] = ["Weapons"],
        [119] = ["Weapons"],
        [120] = ["Weapons"],
        [121] = ["Weapons"],
        [122] = ["Weapons"],
        [123] = ["Weapons"],
        [124] = ["Weapons"],
        [125] = ["Weapons"],
        [126] = ["Weapons"],
        [127] = ["Weapons"],
        [128] = ["Weapons"],
        [129] = ["Weapons"],
        [130] = ["Weapons"],
        [131] = ["Weapons"],
        [132] = ["MiscItems"],
        [133] = ["Jewellery"],
        [134] = ["Jewellery"],
        [135] = ["Jewellery"],
        [136] = ["Jewellery"],
        [137] = ["Jewellery"],
        [138] = ["Jewellery"],
        [139] = ["Jewellery"],
        [140] = ["Jewellery"],
        [141] = ["MensClothing"],
        [142] = ["MensClothing"],
        [143] = ["MensClothing"],
        [144] = ["MensClothing"],
        [145] = ["MensClothing"],
        [146] = ["MensClothing"],
        [147] = ["MensClothing"],
        [148] = ["MensClothing"],
        [149] = ["MensClothing"],
        [150] = ["MensClothing"],
        [151] = ["MensClothing"],
        [152] = ["MensClothing"],
        [153] = ["MensClothing"],
        [154] = ["MensClothing"],
        [155] = ["MensClothing"],
        [156] = ["MensClothing"],
        [157] = ["MensClothing"],
        [158] = ["MensClothing"],
        [159] = ["MensClothing"],
        [160] = ["MensClothing"],
        [161] = ["MensClothing"],
        [162] = ["MensClothing"],
        [163] = ["MensClothing"],
        [164] = ["MensClothing"],
        [165] = ["MensClothing"],
        [166] = ["MensClothing"],
        [167] = ["MensClothing"],
        [168] = ["MensClothing"],
        [169] = ["MensClothing"],
        [170] = ["MensClothing"],
        [171] = ["MensClothing"],
        [172] = ["MensClothing"],
        [173] = ["MensClothing"],
        [174] = ["MensClothing"],
        [175] = ["MensClothing"],
        [176] = ["MensClothing"],
        [177] = ["MensClothing"],
        [178] = ["MensClothing"],
        [179] = ["MensClothing"],
        [180] = ["MensClothing"],
        [181] = ["MensClothing"],
        [182] = ["WomensClothing"],
        [183] = ["WomensClothing"],
        [184] = ["WomensClothing"],
        [185] = ["WomensClothing"],
        [186] = ["WomensClothing"],
        [187] = ["WomensClothing"],
        [188] = ["WomensClothing"],
        [189] = ["WomensClothing"],
        [190] = ["WomensClothing"],
        [191] = ["WomensClothing"],
        [192] = ["WomensClothing"],
        [193] = ["WomensClothing"],
        [194] = ["WomensClothing"],
        [195] = ["WomensClothing"],
        [196] = ["WomensClothing"],
        [197] = ["WomensClothing"],
        [198] = ["WomensClothing"],
        [199] = ["WomensClothing"],
        [200] = ["WomensClothing"],
        [201] = ["WomensClothing"],
        [202] = ["WomensClothing"],
        [203] = ["WomensClothing"],
        [204] = ["WomensClothing"],
        [205] = ["WomensClothing"],
        [206] = ["WomensClothing"],
        [207] = ["WomensClothing"],
        [208] = ["WomensClothing"],
        [209] = ["WomensClothing"],
        [210] = ["WomensClothing"],
        [211] = ["WomensClothing"],
        [212] = ["WomensClothing"],
        [213] = ["WomensClothing"],
        [214] = ["WomensClothing"],
        [215] = ["WomensClothing"],
        [216] = ["WomensClothing"],
        [217] = ["Furniture"],
        [218] = ["Furniture"],
        [219] = ["Furniture"],
        [220] = ["Furniture"],
        [221] = ["Furniture"],
        [222] = ["Furniture"],
        [223] = ["Furniture"],
        [224] = ["Furniture"],
        [225] = ["Furniture"],
        [226] = ["Furniture"],
        [227] = ["Furniture"],
        [228] = ["Furniture"],
        [229] = ["Furniture"],
        [230] = ["Furniture"],
        [231] = ["Furniture"],
        [232] = ["Furniture"],
        [233] = ["Furniture"],
        [234] = ["Furniture"],
        [235] = ["Furniture"],
        [236] = ["Furniture"],
        [237] = ["Furniture"],
        [238] = ["Furniture"],
        [239] = ["Furniture"],
        [240] = ["Furniture"],
        [241] = ["Furniture"],
        [242] = ["Furniture"],
        [243] = ["Furniture"],
        [244] = ["Furniture"],
        [245] = ["Furniture"],
        [246] = ["Furniture"],
        [247] = ["UselessItems2"],
        [248] = ["UselessItems2"],
        [249] = ["UselessItems2"],
        [250] = ["Furniture"],
        [251] = ["Furniture"],
        [252] = ["UselessItems2"],
        [253] = ["UselessItems2"],
        [254] = ["QuestItems"],
        [255] = ["QuestItems"],
        [256] = ["QuestItems"],
        [257] = ["QuestItems"],
        [258] = ["ReligiousItems"],
        [259] = ["ReligiousItems"],
        [260] = ["ReligiousItems"],
        [261] = ["ReligiousItems"],
        [262] = ["ReligiousItems"],
        [263] = ["ReligiousItems"],
        [264] = ["ReligiousItems"],
        [265] = ["ReligiousItems"],
        [266] = ["Furniture"],
        [267] = ["ReligiousItems"],
        [268] = ["ReligiousItems"],
        [269] = ["ReligiousItems"],
        [270] = ["ReligiousItems"],
        [271] = ["ReligiousItems"],
        [272] = ["Furniture"],
        [273] = ["Furniture"],
        [274] = ["MiscItems"],
        [275] = ["MiscItems"],
        [276] = ["Currency"],
        [277] = ["Books"],
        [278] = ["MiscItems"],
        [279] = ["UselessItems2"],
        [280] = ["QuestItems"],
        [281] = ["QuestItems", "MiscItems"],
        [282] = ["QuestItems"],
        [283] = ["QuestItems"],
        [284] = ["Paintings"],
        [285] = ["MiscItems"],
        [286] = ["MiscItems"],
        [287] = ["Maps", "MiscItems"],
    };

    /// <summary>The 7943 weapon media per weapon template index.</summary>
    private static readonly IReadOnlyDictionary<int, string> WeaponMedia = new Dictionary<int, string>
    {
        [113] = "weapon.dagger.steel",
        [114] = "weapon.longblade",
        [115] = "weapon.staff",
        [116] = "weapon.longblade",
        [117] = "weapon.longblade",
        [118] = "weapon.longblade",
        [119] = "weapon.longblade",
        [120] = "weapon.longblade",
        [121] = "weapon.longblade",
        [122] = "weapon.longblade",
        [123] = "weapon.longblade",
        [124] = "weapon.mace",
        [125] = "weapon.flail",
        [126] = "weapon.warhammer",
        [127] = "weapon.axe",
        [128] = "weapon.axe",
        [129] = "weapon.bow",
        [130] = "weapon.bow",
    };

    /// <summary>The donor group names by numeric value.</summary>
    public static readonly IReadOnlyDictionary<int, string> GroupNames = new Dictionary<int, string>
    {
        [0] = "Drugs", [1] = "UselessItems1", [2] = "Armor", [3] = "Weapons", [4] = "MagicItems",
        [5] = "Artifacts", [6] = "MensClothing", [7] = "Books", [8] = "Furniture", [9] = "UselessItems2",
        [10] = "ReligiousItems", [11] = "Maps", [12] = "WomensClothing", [13] = "Paintings", [14] = "Gems",
        [15] = "PlantIngredients1", [16] = "PlantIngredients2", [17] = "CreatureIngredients1",
        [18] = "CreatureIngredients2", [19] = "CreatureIngredients3", [20] = "MiscellaneousIngredients1",
        [21] = "MetalIngredients", [22] = "MiscellaneousIngredients2", [23] = "Transportation",
        [24] = "Deeds", [25] = "Jewellery", [26] = "QuestItems", [27] = "MiscItems", [28] = "Currency",
    };

    /// <summary>Builds the item template catalog.</summary>
    public static DaggerfallItemTemplates Build(
        IReadOnlyList<SubstituteItemTemplate> substitutes,
        IReadOnlyList<SubstituteMagicTemplate> magic,
        string label,
        byte[] bytes,
        IReadOnlyList<SourceInventoryRow> inventory)
    {
        ArgumentNullException.ThrowIfNull(substitutes);
        ArgumentNullException.ThrowIfNull(magic);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(inventory);
        SourceInventoryRow family = SourceInventoryRow.RequireFamily(inventory, ItemsFamily);

        List<DaggerfallItemTemplate> templates = [];
        foreach (SubstituteItemTemplate substitute in substitutes)
        {
            if (!TemplateGroups.TryGetValue(substitute.Index, out string[]? groups))
            {
                throw new InvalidOperationException($"Item template {substitute.Index} names no donor group.");
            }

            templates.Add(new DaggerfallItemTemplate(
                substitute.Index,
                substitute.Name,
                groups,
                DaggerfallItemTemplateDisposition.Substitute,
                substitute.BaseWeight,
                substitute.HitPoints,
                substitute.CapacityOrTarget,
                substitute.BasePrice,
                substitute.EnchantmentPoints,
                substitute.Rarity,
                substitute.Variants,
                substitute.IsBluntWeapon,
                substitute.IsLiquid,
                substitute.IsOneHanded,
                substitute.IsIngredient,
                IsStackable(substitute, groups),
                substitute.WorldTextureArchive,
                substitute.WorldTextureRecord,
                substitute.PlayerTextureArchive,
                substitute.PlayerTextureRecord,
                WeaponMedia.GetValueOrDefault(substitute.Index, string.Empty),
                substitute.DrawOrderOrEffect));
        }

        List<DaggerfallMagicTemplate> magicTemplates = [];
        foreach (SubstituteMagicTemplate substitute in magic)
        {
            magicTemplates.Add(new DaggerfallMagicTemplate(
                substitute.Index,
                substitute.Name,
                substitute.Type,
                substitute.Group,
                GroupNames.GetValueOrDefault(substitute.Group, string.Empty),
                substitute.GroupIndex,
                [.. substitute.Enchantments
                    .Where(enchantment => enchantment.Type != "None" || enchantment.Param != -1)
                    .Select(enchantment => new DaggerfallMagicEnchantment(enchantment.Type, enchantment.Param))],
                substitute.Uses,
                substitute.Value,
                substitute.Material));
        }

        DaggerfallItemTemplates catalog = new(
            new DaggerfallTextSource(DaggerfallTextKind.Resource, family.Id, label, "en", bytes.LongLength, 0, templates.Count),
            [.. templates.OrderBy(template => template.Index)],
            [.. magicTemplates.OrderBy(template => template.Index)]);
        catalog.Validate();
        return catalog;
    }

    private static bool IsStackable(SubstituteItemTemplate substitute, string[] groups) =>
        substitute.IsIngredient
        || (substitute.Index == BottleTemplate && groups.Contains("UselessItems1"))
        || groups.Contains("Books")
        || (substitute.Index == GoldTemplate && groups.Contains("Currency"))
        || (substitute.Index == ArrowTemplate && groups.Contains("Weapons"))
        || (substitute.Index == OilTemplate && groups.Contains("UselessItems2"));
}
