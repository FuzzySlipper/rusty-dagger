namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>How a native template target is accounted for: the ledger's vocabulary.</summary>
internal enum DaggerfallItemTemplateDisposition
{
    Substitute,
    Unresolved,
    Decoded,
    Malformed,
    Unsupported,
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
internal sealed record DaggerfallItemTemplateDefinition(
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
    int DrawOrderOrEffect);

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
internal sealed record DaggerfallMagicTemplateDefinition(
    int Index,
    string Name,
    string Type,
    int Group,
    string GroupName,
    int GroupIndex,
    IReadOnlyList<DaggerfallTemplateEnchantmentDefinition> Enchantments,
    int Uses,
    int Value,
    int Material);

/// <summary>One non-padding enchantment: its type and spell or effect parameter.</summary>
/// <param name="Type">The enchantment type.</param>
/// <param name="Param">A spell ID, artifact effect identifier, or enemy/social parameter.</param>
internal sealed record DaggerfallTemplateEnchantmentDefinition(string Type, int Param);

/// <summary>The normalized item template catalog, loaded from the pack alone.</summary>
/// <param name="Templates">The templates by index.</param>
/// <param name="Magic">The magic templates by index.</param>
internal sealed record DaggerfallItemTemplateSet(
    IReadOnlyDictionary<int, DaggerfallItemTemplateDefinition> Templates,
    IReadOnlyDictionary<int, DaggerfallMagicTemplateDefinition> Magic)
{
    /// <summary>
    /// Resolves a native template index to the record inventory coordination reads: its stack
    /// rule, base condition and presentation references. Instance condition and enchantment
    /// policy stay with their owners; this answers what the template states.
    /// </summary>
    internal DaggerfallItemTemplateDefinition Resolve(int index) => Templates[index];
}
