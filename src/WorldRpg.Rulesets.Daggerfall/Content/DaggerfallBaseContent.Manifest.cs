using System.Text;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// One authored <c>daggerfall.base</c> pack as the loader takes it: its <c>payload.json</c> manifest and every
/// section JSON file declared under its <c>sections</c> map, keyed by pack-relative forward-slash path.
/// </summary>
internal sealed record DaggerfallBasePackFiles(
    string PackId,
    ReadOnlyMemory<byte> Payload,
    IReadOnlyDictionary<string, ReadOnlyMemory<byte>> SectionFiles);

internal static partial class DaggerfallBaseContent
{
    /// <summary>
    /// Reads the section files declared by a base pack's <c>payload.json</c> manifest from <paramref name="content"/>.
    /// A legacy inline payload without a <c>sections</c> map returns an empty file dictionary and reads verbatim.
    /// </summary>
    internal static DaggerfallBasePackFiles Files(ProductContent content, string packId, string payloadPath, ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(content);
        string root = payloadPath[..(payloadPath.LastIndexOf('/') + 1)];
        Dictionary<string, ReadOnlyMemory<byte>> files = new(StringComparer.Ordinal);
        foreach (string relativePath in DeclaredSectionPaths(payload))
        {
            if (IsSafeSectionPath(relativePath) && content.TryReadFile($"{root}{relativePath}", out ProductContentFile file))
                files[relativePath] = file.Bytes;
        }
        return new(packId, payload, files);
    }

    /// <summary>Reads a base pack manifest and its declared section files from disk.</summary>
    internal static DaggerfallBasePackFiles FilesFromDisk(string packId, string payloadPath)
    {
        ReadOnlyMemory<byte> payload = File.ReadAllBytes(payloadPath);
        string root = Path.GetDirectoryName(Path.GetFullPath(payloadPath))
            ?? throw new InvalidOperationException($"Base pack payload '{payloadPath}' has no parent directory.");
        Dictionary<string, ReadOnlyMemory<byte>> files = new(StringComparer.Ordinal);
        foreach (string relativePath in DeclaredSectionPaths(payload))
        {
            if (!IsSafeSectionPath(relativePath)) continue;
            string fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
                files[relativePath] = File.ReadAllBytes(fullPath);
        }
        return new(packId, payload, files);
    }

    /// <summary>Reads the authored base pack (manifest + section files) joined to the imported payload.</summary>
    internal static DaggerfallDefinitions Read(DaggerfallBasePackFiles authored, ReadOnlyMemory<byte> imported, DaggerfallData? data)
    {
        ArgumentNullException.ThrowIfNull(authored);
        DaggerfallContentDiagnostics diagnostics = new();
        ReadOnlyMemory<byte> assembled = AssembleAuthored(authored, diagnostics);
        diagnostics.ThrowIfAny();
        return Read(assembled, imported, data);
    }

    /// <summary>Assembles the authored base pack and joins it with the imported payload as one UTF-8 JSON root.</summary>
    internal static byte[] Combine(DaggerfallBasePackFiles authored, ReadOnlyMemory<byte> imported)
    {
        ArgumentNullException.ThrowIfNull(authored);
        DaggerfallContentDiagnostics diagnostics = new();
        ReadOnlyMemory<byte> assembled = AssembleAuthored(authored, diagnostics);
        diagnostics.ThrowIfAny();
        return Combine(assembled, imported);
    }

    /// <summary>Assembles an authored base pack manifest and its section files into one UTF-8 JSON root object.</summary>
    internal static byte[] AssembleAuthored(DaggerfallBasePackFiles authored)
    {
        ArgumentNullException.ThrowIfNull(authored);
        DaggerfallContentDiagnostics diagnostics = new();
        ReadOnlyMemory<byte> assembled = AssembleAuthored(authored, diagnostics);
        diagnostics.ThrowIfAny();
        return assembled.ToArray();
    }

    internal static ReadOnlyMemory<byte> AssembleAuthored(DaggerfallBasePackFiles authored, DaggerfallContentDiagnostics diagnostics)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(authored.Payload);
            JsonElement root = Object(document.RootElement, $"base pack '{authored.PackId}' payload", diagnostics);
            if (diagnostics.HasErrors) return ReadOnlyMemory<byte>.Empty;
            RejectDuplicateProperties(root, $"base pack '{authored.PackId}' payload", diagnostics);
            if (!root.TryGetProperty("sections", out JsonElement sectionsElement))
                return authored.Payload;

            string ruleset = Text(root, "ruleset", diagnostics);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Name is not ("ruleset" or "sections"))
                    diagnostics.Add($"Base pack '{authored.PackId}' manifest may not mix inline section '{property.Name}' with a 'sections' map.");
            }

            JsonElement sections = Object(sectionsElement, $"base pack '{authored.PackId}' sections", diagnostics);
            if (diagnostics.HasErrors) return ReadOnlyMemory<byte>.Empty;
            RejectDuplicateProperties(sections, $"base pack '{authored.PackId}' sections", diagnostics);

            List<(string Name, ReadOnlyMemory<byte> Bytes)> entries = [];
            HashSet<string> usedPaths = new(StringComparer.Ordinal);
            foreach (JsonProperty property in sections.EnumerateObject())
            {
                string name = property.Name;
                if (string.IsNullOrWhiteSpace(name) || name is "ruleset" or "sections")
                {
                    diagnostics.Add($"Base pack '{authored.PackId}' declares reserved or empty section name '{name}'.");
                    continue;
                }
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    diagnostics.Add($"Base pack '{authored.PackId}' section '{name}' must map to a relative file path string.");
                    continue;
                }
                string relativePath = property.Value.GetString() ?? "";
                if (!IsSafeSectionPath(relativePath))
                {
                    diagnostics.Add($"Base pack '{authored.PackId}' section '{name}' has invalid relative path '{relativePath}'.");
                    continue;
                }
                if (!usedPaths.Add(relativePath))
                {
                    diagnostics.Add($"Base pack '{authored.PackId}' maps multiple sections to '{relativePath}'.");
                    continue;
                }
                if (!authored.SectionFiles.TryGetValue(relativePath, out ReadOnlyMemory<byte> raw))
                {
                    diagnostics.Add($"Base pack '{authored.PackId}' section '{name}' refers to missing file '{relativePath}'.");
                    continue;
                }
                ReadOnlyMemory<byte> trimmed = TrimJsonBytes(raw);
                if (trimmed.IsEmpty)
                {
                    diagnostics.Add($"Base pack '{authored.PackId}' section '{name}' file '{relativePath}' is empty.");
                    continue;
                }
                entries.Add((name, trimmed));
            }

            if (entries.Count == 0 && !diagnostics.HasErrors)
                diagnostics.Add($"Base pack '{authored.PackId}' sections map must declare at least one section.");
            if (diagnostics.HasErrors) return ReadOnlyMemory<byte>.Empty;

            return SpliceAuthoredRoot(ruleset, entries);
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Base pack '{authored.PackId}' payload is not valid JSON: {exception.Message}");
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private static ReadOnlyMemory<byte> SpliceAuthoredRoot(string ruleset, List<(string Name, ReadOnlyMemory<byte> Bytes)> entries)
    {
        byte[] header = Encoding.UTF8.GetBytes($"{{\"ruleset\":{JsonSerializer.Serialize(ruleset)}");
        byte[][] prefixes = new byte[entries.Count][];
        int total = header.Length + 1; // closing '}'
        for (int i = 0; i < entries.Count; i++)
        {
            prefixes[i] = Encoding.UTF8.GetBytes($",{JsonSerializer.Serialize(entries[i].Name)}:");
            total = checked(total + prefixes[i].Length + entries[i].Bytes.Length);
        }

        byte[] buffer = new byte[total];
        int offset = 0;
        header.CopyTo(buffer, offset);
        offset += header.Length;
        for (int i = 0; i < entries.Count; i++)
        {
            prefixes[i].CopyTo(buffer, offset);
            offset += prefixes[i].Length;
            entries[i].Bytes.Span.CopyTo(buffer.AsSpan(offset));
            offset += entries[i].Bytes.Length;
        }
        buffer[offset] = (byte)'}';
        return buffer;
    }

    private static IEnumerable<string> DeclaredSectionPaths(ReadOnlyMemory<byte> payload)
    {
        List<string> paths = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sections", out JsonElement sections)
                || sections.ValueKind != JsonValueKind.Object)
                return paths;
            foreach (JsonProperty property in sections.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } path)
                    paths.Add(path);
            }
        }
        catch (JsonException)
        {
            // Malformed manifest JSON is reported with full diagnostics by AssembleAuthored.
        }
        return paths;
    }

    private static bool IsSafeSectionPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith('/')
            || path.EndsWith('/')
            || path.Contains('\\', StringComparison.Ordinal))
            return false;
        foreach (string segment in path.Split('/'))
            if (segment.Length == 0 || segment is "." or "..")
                return false;
        return true;
    }

    private static ReadOnlyMemory<byte> TrimJsonBytes(ReadOnlyMemory<byte> payload)
    {
        ReadOnlySpan<byte> span = payload.Span;
        int start = span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        while (start < span.Length && span[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') start++;
        int end = span.Length;
        while (end > start && span[end - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') end--;
        return payload[start..end];
    }
}
