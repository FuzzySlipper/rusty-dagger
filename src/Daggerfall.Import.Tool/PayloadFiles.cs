using System.Text.Json;
using System.Text.Json.Nodes;

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
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>A generated payload's root, or an empty object when no command has written it yet.</summary>
    internal static JsonObject ReadGenerated(string path) =>
        File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : [];

    /// <summary>A generated payload's text, or an empty object when no command has written it yet.</summary>
    internal static string ReadGeneratedText(string path) => File.Exists(path) ? File.ReadAllText(path) : "{\n}\n";

    /// <summary>Writes a generated payload, creating its directory.</summary>
    internal static void Write(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, root.ToJsonString(Indented) + "\n");
    }

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
}
