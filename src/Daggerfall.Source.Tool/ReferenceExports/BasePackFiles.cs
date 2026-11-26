using System.Text.Json;
using System.Text.Json.Nodes;

namespace Daggerfall.Source.Tool.ReferenceExports;

/// <summary>
/// Reads one named section from a <c>daggerfall.base</c> pack, supporting both the split manifest
/// (<c>payload.json</c> with a <c>sections</c> path map) and an inline single-file payload.
/// </summary>
internal static class BasePackFiles
{
    internal static JsonNode ReadSectionNode(string basePayloadPath, string sectionName)
    {
        byte[] bytes = ReadSectionBytes(basePayloadPath, sectionName);
        return JsonNode.Parse(bytes)
            ?? throw new InvalidOperationException($"'{basePayloadPath}' section '{sectionName}' parsed as null.");
    }

    internal static JsonDocument ReadSectionDocument(string basePayloadPath, string sectionName) =>
        JsonDocument.Parse(ReadSectionBytes(basePayloadPath, sectionName));

    private static byte[] ReadSectionBytes(string basePayloadPath, string sectionName)
    {
        byte[] manifestBytes = File.ReadAllBytes(basePayloadPath);
        using JsonDocument doc = JsonDocument.Parse(manifestBytes);
        JsonElement root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("sections", out JsonElement sections)
            && sections.ValueKind == JsonValueKind.Object)
        {
            if (!sections.TryGetProperty(sectionName, out JsonElement pathElement) || pathElement.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException($"'{basePayloadPath}' carries no '{sectionName}' section in its sections map.");
            string relPath = pathElement.GetString()!;
            string baseDir = Path.GetDirectoryName(Path.GetFullPath(basePayloadPath))
                ?? throw new InvalidOperationException($"'{basePayloadPath}' has no parent directory.");
            string target = Path.Combine(baseDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllBytes(target);
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(sectionName, out JsonElement inline))
            return System.Text.Encoding.UTF8.GetBytes(inline.GetRawText());
        throw new InvalidOperationException($"'{basePayloadPath}' carries no {sectionName} section.");
    }
}
