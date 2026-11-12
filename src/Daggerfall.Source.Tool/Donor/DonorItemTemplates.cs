using System.Text.Json;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Source.Tool.Donor;

/// <summary>Reads the donor's exported item template tables, <c>ItemTemplates.txt</c> and <c>MagicItemTemplates.txt</c>.</summary>
internal static class DonorItemTemplates
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
