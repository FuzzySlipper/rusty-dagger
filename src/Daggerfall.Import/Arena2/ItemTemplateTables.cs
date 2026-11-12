using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Arena2;

/// <summary>One substitute template record: every field the donor's exported table states.</summary>
/// <param name="Index">The template index.</param>
/// <param name="Name">The template name.</param>
/// <param name="BaseWeight">The base weight.</param>
/// <param name="HitPoints">The base condition.</param>
/// <param name="CapacityOrTarget">The capacity or target value.</param>
/// <param name="BasePrice">The base price.</param>
/// <param name="EnchantmentPoints">The enchantment points.</param>
/// <param name="Rarity">The rarity.</param>
/// <param name="Variants">The texture variants.</param>
/// <param name="DrawOrderOrEffect">The draw order or effect value.</param>
/// <param name="IsBluntWeapon">Whether the template is a blunt weapon.</param>
/// <param name="IsLiquid">Whether the template is liquid.</param>
/// <param name="IsOneHanded">Whether the template is one-handed.</param>
/// <param name="IsIngredient">Whether the template is an ingredient.</param>
/// <param name="WorldTextureArchive">The world texture archive.</param>
/// <param name="WorldTextureRecord">The world texture record.</param>
/// <param name="PlayerTextureArchive">The player texture archive.</param>
/// <param name="PlayerTextureRecord">The player texture record.</param>
public sealed record SubstituteItemTemplate(
    int Index,
    string Name,
    double BaseWeight,
    int HitPoints,
    int CapacityOrTarget,
    int BasePrice,
    int EnchantmentPoints,
    int Rarity,
    int Variants,
    int DrawOrderOrEffect,
    bool IsBluntWeapon,
    bool IsLiquid,
    bool IsOneHanded,
    bool IsIngredient,
    int WorldTextureArchive,
    int WorldTextureRecord,
    int PlayerTextureArchive,
    int PlayerTextureRecord);

/// <summary>One substitute enchantment: its type and spell or effect parameter.</summary>
/// <param name="Type">The enchantment type.</param>
/// <param name="Param">A spell ID, artifact effect identifier, or -1.</param>
public sealed record SubstituteEnchantment(string Type, int Param);

/// <summary>One substitute magic template: its group placement, material and enchantments.</summary>
/// <param name="Index">The magic template index.</param>
/// <param name="Name">The magic template name.</param>
/// <param name="Type">The magic item type.</param>
/// <param name="Group">The group in item templates.</param>
/// <param name="GroupIndex">The group index in item templates.</param>
/// <param name="Enchantments">The legacy enchantments.</param>
/// <param name="Uses">The uses and item condition.</param>
/// <param name="Value">The value, used for artifacts.</param>
/// <param name="Material">The material.</param>
public sealed record SubstituteMagicTemplate(
    int Index,
    string Name,
    string Type,
    int Group,
    int GroupIndex,
    IReadOnlyList<SubstituteEnchantment> Enchantments,
    int Uses,
    int Value,
    int Material);

/// <summary>Our item template table, taken once from the donor's <c>ItemTemplates.txt</c>, its export of FALL.EXE's 288 templates.</summary>
public sealed record ItemTemplateTable(TakenFrom Source, IReadOnlyList<SubstituteItemTemplate> Templates);

/// <summary>Our magic item template table, taken once from the donor's <c>MagicItemTemplates.txt</c>.</summary>
public sealed record MagicItemTemplateTable(TakenFrom Source, IReadOnlyList<SubstituteMagicTemplate> Templates);
