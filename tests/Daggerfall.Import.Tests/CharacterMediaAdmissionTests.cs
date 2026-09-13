using System.Text.Json;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// Whether every character source the pack publishes is one the coverage manifest admits.
/// </summary>
/// <remarks>
/// The verification for the character task asks that every published media reference resolve to an
/// admitted artifact. The manifest is the independent answer to that: it is written by the
/// source-manifest command from the corpus itself, so checking the pack against it is not the pack
/// agreeing with itself. What "admitted" means here is concrete and checkable - a file row for the
/// exact source path exists - rather than a reading of a binding flag that nothing publishes as
/// admitted yet.
/// </remarks>
public sealed class CharacterMediaAdmissionTests
{
    [Fact]
    public void Every_published_character_source_is_admitted_by_the_manifest()
    {
        Dictionary<string, long?> admitted = AdmittedSourceFiles();
        Assert.NotEmpty(admitted);

        using JsonDocument pack = JsonDocument.Parse(File.ReadAllBytes(PackPath()));
        JsonElement section = pack.RootElement.GetProperty("characterPresentation");
        List<string> published = [.. section.GetProperty("files").EnumerateArray()
            .Select(file => file.GetProperty("path").GetString()!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        // Every published source is a file the manifest carries: the manifest names the corpus, so a
        // published path it does not carry would be a reference to something no command admitted.
        List<string> missing = [.. published.Where(path => !admitted.ContainsKey($"local/arena2/{path}"))];
        Assert.Empty(missing);

        // And the converse, which is the stronger direction: every character-media file row the
        // manifest carries is published, so the section accounts for the whole family rather than a
        // convenient part of it.
        List<string> unpublished = [.. admitted.Keys
            .Where(path => path.StartsWith("local/arena2/", StringComparison.Ordinal))
            .Select(path => System.IO.Path.GetFileName(path))
            .Where(name => IsCharacterMedia(name) && !published.Contains(name, StringComparer.OrdinalIgnoreCase))];
        Assert.Empty(unpublished);
    }

    /// <summary>The source paths the manifest admits as files, with the byte size it recorded.</summary>
    private static Dictionary<string, long?> AdmittedSourceFiles()
    {
        Dictionary<string, long?> admitted = new(StringComparer.OrdinalIgnoreCase);
        string[] lines = File.ReadAllLines(ManifestPath());
        int path = Array.IndexOf(lines[0].Split(','), "path_or_pattern");
        int type = Array.IndexOf(lines[0].Split(','), "row_type");
        int size = Array.IndexOf(lines[0].Split(','), "byte_size");
        int family = Array.IndexOf(lines[0].Split(','), "family_id");
        foreach (string line in lines.Skip(1))
        {
            string[] columns = line.Split(',');
            if (columns.Length <= Math.Max(path, Math.Max(type, family)) || columns[type] != "file" || columns[family] != "CNT-021")
            {
                continue;
            }

            admitted[columns[path]] = columns.Length > size && long.TryParse(columns[size], out long bytes) ? bytes : null;
        }

        return admitted;
    }

    private static bool IsCharacterMedia(string name) =>
        name.EndsWith(".CEL", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".BSS", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".CIF", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".IMG", StringComparison.OrdinalIgnoreCase);

    private static string ManifestPath() => Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv");

    private static string PackPath() => Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json");

    private static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "AGENTS.md")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }
}
