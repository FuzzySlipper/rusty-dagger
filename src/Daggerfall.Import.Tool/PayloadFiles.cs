using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool;

/// <summary>
/// The payload files the import commands read and write. The import never reads the authored base payload
/// (<c>daggerfall.base</c>): the ruleset joins it to the imported sections when it loads, so editing it leaves
/// the imported data current. The imported payload (<c>daggerfall.imported</c>) and the import records are
/// generated: a full <c>import</c> clears the staging tree and the records and the commands rebuild them
/// section by section, so the first command to write one creates it.
/// </summary>
internal static class PayloadFiles
{
    /// <summary>A generated payload's text, or an empty object when no command has written it yet.</summary>
    internal static string ReadGeneratedText(string path) => File.Exists(path) ? File.ReadAllText(path) : "{\n}\n";

    /// <summary>A generated payload's root, or an empty object when no command has written it yet.</summary>
    internal static JsonObject ReadGenerated(string path) => JsonNode.Parse(ReadGeneratedText(path))!.AsObject();

    /// <summary>One section of a generated payload read through its published contract.</summary>
    internal static T ReadSection<T>(string path, string section) where T : class =>
        (ReadGenerated(path)[section] ?? throw new InvalidOperationException($"'{path}' carries no {section} section."))
            .Deserialize<T>(PublishedJson.SectionRead)
            ?? throw new InvalidOperationException($"The {section} section of '{path}' could not be read.");

    /// <summary>
    /// Replaces or appends the named sections of a generated payload, leaving every other section's bytes as
    /// they are, and creates the file when no command has written it yet.
    /// </summary>
    internal static void WriteSections(string path, IReadOnlyDictionary<string, string> sections)
    {
        Dictionary<string, string> trimmed = sections.ToDictionary(pair => pair.Key, pair => pair.Value.TrimEnd(), StringComparer.Ordinal);
        string updated = TopLevelJsonSectionRewriter.ReplaceOrAppend(ReadGeneratedText(path), trimmed);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, updated);
        Console.WriteLine($"pack: {string.Join(", ", sections.Keys)} updated in {path}");
    }

    /// <summary>Writes one section serialized in the published dialect.</summary>
    internal static void WriteSection<T>(string path, string section, T value) =>
        WriteSections(path, new Dictionary<string, string>(StringComparer.Ordinal) { [section] = JsonSerializer.Serialize(value, PublishedJson.Section) });

    /// <summary>
    /// Reads an operator file under the individual source quota, refusing one that changed while it was read.
    /// </summary>
    internal static byte[] ReadBounded(string path, string subject)
    {
        string fullPath = Path.GetFullPath(path);
        FileInfo info = new(fullPath);
        if (!info.Exists) throw new FileNotFoundException($"Required {subject} '{fullPath}' was not found.", fullPath);
        if (info.Length is <= 0 or > Arena2SiteSources.MaximumIndividualSourceBytes)
            throw new InvalidOperationException($"{subject} is outside the permitted byte range.");
        byte[] bytes = File.ReadAllBytes(fullPath);
        if (bytes.LongLength != info.Length) throw new IOException($"{subject} changed while it was being read.");
        return bytes;
    }

    /// <summary>Writes a group-relative artifact list under a content root.</summary>
    internal static void WriteArtifacts(string root, IEnumerable<ImportPublicationArtifact> artifacts)
    {
        foreach (ImportPublicationArtifact artifact in artifacts)
        {
            WriteFile(Path.Combine(root, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)), artifact.Bytes.Span);
        }
    }

    internal static void WriteFile(string path, ReadOnlySpan<byte> bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, bytes.ToArray());
    }
}
