using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool;

/// <summary>
/// The payload files the import commands read and write. The authored base payload
/// (<c>daggerfall.base</c>) is tracked and hand-edited; a command reads the sections it needs from it
/// and never writes it. The imported payload (<c>daggerfall.imported</c>) and the import records are
/// generated: <c>scripts/regenerate-content.sh</c> removes them and the commands rebuild them section by
/// section, so the first command to write one creates it.
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

    /// <summary>The authored base payload's text; it is tracked, so its absence is refused.</summary>
    internal static string ReadAuthoredText(string path) => File.Exists(path)
        ? File.ReadAllText(path)
        : throw new FileNotFoundException($"The authored base payload is not at '{path}'.", path);

    /// <summary>The authored base payload's root.</summary>
    internal static JsonObject ReadAuthored(string path) => JsonNode.Parse(ReadAuthoredText(path))!.AsObject();

    /// <summary>
    /// The authored and imported sections as one root, for a reader that joins the two. Each section has
    /// one owner, so a section both files carry is refused rather than resolved by precedence.
    /// </summary>
    internal static string CombinedText(string authoredPath, string importedPath)
    {
        JsonObject authored = ReadAuthored(authoredPath);
        JsonObject imported = JsonNode.Parse(File.ReadAllText(importedPath))!.AsObject();
        foreach ((string name, JsonNode? value) in imported.ToArray())
        {
            if (authored.ContainsKey(name))
                throw new InvalidOperationException($"Section '{name}' is in both '{authoredPath}' and '{importedPath}'; each section has one owner.");
            imported.Remove(name);
            authored[name] = value;
        }

        return authored.ToJsonString();
    }

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
