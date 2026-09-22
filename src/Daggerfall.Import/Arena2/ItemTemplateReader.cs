using System.Text.Json;

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

/// <summary>Reader for the donor's exported item template tables.</summary>
public static class ItemTemplateReader
{
    /// <summary>
    /// Reads the substitute template table: 288 records indexed 0 through 287. The file is the
    /// donor's export, not native bytes, so every record carries substitute provenance downstream
    /// and a short table, a repeated index, or a missing field is refused rather than padded.
    /// </summary>
    public static IReadOnlyList<SubstituteItemTemplate> ReadTemplates(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new Arena2FormatException(source, 0, $"Item substitute is no table: {exception.Message}.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new Arena2FormatException(source, 0, "Item substitute is no table.");
            }

            List<SubstituteItemTemplate> templates = [];
            foreach (JsonElement entry in document.RootElement.EnumerateArray())
            {
                templates.Add(new SubstituteItemTemplate(
                    RequiredInt(entry, "index", source),
                    RequiredText(entry, "name", source),
                    RequiredDouble(entry, "baseWeight", source),
                    RequiredInt(entry, "hitPoints", source),
                    RequiredInt(entry, "capacityOrTarget", source),
                    RequiredInt(entry, "basePrice", source),
                    RequiredInt(entry, "enchantmentPoints", source),
                    RequiredInt(entry, "rarity", source),
                    RequiredInt(entry, "variants", source),
                    RequiredInt(entry, "drawOrderOrEffect", source),
                    RequiredBool(entry, "isBluntWeapon", source),
                    RequiredBool(entry, "isLiquid", source),
                    RequiredBool(entry, "isOneHanded", source),
                    RequiredBool(entry, "isIngredient", source),
                    RequiredInt(entry, "worldTextureArchive", source),
                    RequiredInt(entry, "worldTextureRecord", source),
                    RequiredInt(entry, "playerTextureArchive", source),
                    RequiredInt(entry, "playerTextureRecord", source)));
            }

            if (templates.Count != 288 || templates.Select(template => template.Index).Order().SequenceEqual(Enumerable.Range(0, 288)) is false)
            {
                throw new Arena2FormatException(source, 0, $"Item substitute carries {templates.Count} records for 288 indexed templates.");
            }

            return templates;
        }
    }

    /// <summary>
    /// Reads the substitute magic table: every record names its group placement, material and
    /// enchantments. Enchantments of type None with param -1 are the file's padding, not effects.
    /// </summary>
    public static IReadOnlyList<SubstituteMagicTemplate> ReadMagic(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new Arena2FormatException(source, 0, $"Magic substitute is no table: {exception.Message}.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new Arena2FormatException(source, 0, "Magic substitute is no table.");
            }

            List<SubstituteMagicTemplate> templates = [];
            foreach (JsonElement entry in document.RootElement.EnumerateArray())
            {
                List<SubstituteEnchantment> enchantments = [];
                foreach (JsonElement enchantment in RequiredArray(entry, "enchantments", source).EnumerateArray())
                {
                    enchantments.Add(new SubstituteEnchantment(
                        RequiredText(enchantment, "type", source),
                        RequiredInt(enchantment, "param", source)));
                }

                templates.Add(new SubstituteMagicTemplate(
                    RequiredInt(entry, "index", source),
                    RequiredText(entry, "name", source),
                    RequiredText(entry, "type", source),
                    RequiredInt(entry, "group", source),
                    RequiredInt(entry, "groupIndex", source),
                    enchantments,
                    RequiredInt(entry, "uses", source),
                    RequiredInt(entry, "value", source),
                    RequiredInt(entry, "material", source)));
            }

            return templates;
        }
    }

    private static int RequiredInt(JsonElement entry, string name, string source) =>
        entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed)
            ? parsed
            : throw new Arena2FormatException(source, 0, $"Item substitute record states no '{name}'.");

    private static double RequiredDouble(JsonElement entry, string name, string source) =>
        entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double parsed)
            ? parsed
            : throw new Arena2FormatException(source, 0, $"Item substitute record states no '{name}'.");

    private static string RequiredText(JsonElement entry, string name, string source) =>
        entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is string text && text.Length > 0
            ? text
            : throw new Arena2FormatException(source, 0, $"Item substitute record states no '{name}'.");

    private static bool RequiredBool(JsonElement entry, string name, string source) =>
        entry.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new Arena2FormatException(source, 0, $"Item substitute record states no '{name}'.");

    private static JsonElement RequiredArray(JsonElement entry, string name, string source) =>
        entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new Arena2FormatException(source, 0, $"Item substitute record states no '{name}'.");
}
