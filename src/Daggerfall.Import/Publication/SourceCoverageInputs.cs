using System.Text.Json;

namespace Daggerfall.Import.Publication;

/// <summary>One artifact the coverage reads: the label it is reported under and the file holding it.</summary>
public sealed record SourceCoverageArtifact(string Label, string File)
{
    /// <summary>
    /// Every artifact whose citations the coverage joins: the content the product sees and the import records.
    /// </summary>
    /// <remarks>
    /// The product sees the repository's tracked content with the import's generated content over it, so a file the
    /// generated root holds replaces the repository's file at the same path. Each artifact is labelled by where the
    /// product or the importer finds it, whichever root it was read from: <c>content/</c> and its content-root-relative
    /// path, or <c>import-records/</c> and its record-relative path.
    /// </remarks>
    public static IReadOnlyList<SourceCoverageArtifact> Collect(string repositoryContent, string generatedContent, string importRecords)
    {
        Dictionary<string, string> content = Json(repositoryContent, "worldrpg");
        foreach ((string path, string file) in Json(generatedContent, "worldrpg")) content[path] = file;
        return [.. content.Select(pair => new SourceCoverageArtifact("content/" + pair.Key, pair.Value)),
            .. Json(importRecords, "").Select(pair => new SourceCoverageArtifact("import-records/" + pair.Key, pair.Value))];
    }

    /// <summary>Every JSON file below one directory of a root, by its root-relative path.</summary>
    private static Dictionary<string, string> Json(string root, string directory)
    {
        string start = Path.Combine(root, directory);
        return Directory.Exists(start)
            ? Directory.EnumerateFiles(start, "*.json", SearchOption.AllDirectories)
                .ToDictionary(file => Path.GetRelativePath(root, file).Replace('\\', '/'), file => file, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }
}

public sealed record SourceCoverageInputs(IReadOnlyList<SourceCoverageConsumer> Consumers, IReadOnlyList<SourceCoverageUnsupported> Unsupported);

/// <summary>Reads existing producer citations and ledgers; patterns remain context, not proof of individual records.</summary>
public static class SourceCoverageInputReader
{
    /// <summary>Reads artifacts labelled by their path relative to a repository root.</summary>
    public static SourceCoverageInputs Read(string repositoryRoot, IEnumerable<string> artifactFiles,
        IReadOnlyList<SourceInventoryRow> inventory) =>
        Read(artifactFiles.Select(file => new SourceCoverageArtifact(Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/'), file)), inventory);

    /// <summary>
    /// Reads artifacts by the label each is reported under; a label below <c>import-records/</c> is an offline
    /// record rather than runtime content.
    /// </summary>
    public static SourceCoverageInputs Read(IEnumerable<SourceCoverageArtifact> artifacts, IReadOnlyList<SourceInventoryRow> inventory)
    {
        List<SourceCoverageConsumer> consumers = [];
        List<SourceCoverageUnsupported> unsupported = [];
        foreach ((string artifact, string file) in artifacts.OrderBy(value => value.Label, StringComparer.Ordinal))
        {
            bool offline = artifact.StartsWith("import-records/", StringComparison.Ordinal);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;
            if (root.TryGetProperty("families", out _) && root.TryGetProperty("records", out _)) continue;
            Walk(root, "root");
            // These are normalized publication shapes, not the importer inventory snapshots.
            if (!offline && root.TryGetProperty("locations", out var locations)
                && locations.ValueKind == JsonValueKind.Object && locations.TryGetProperty("regions", out var regions))
                foreach (var region in regions.EnumerateArray())
                    foreach (var table in region.GetProperty("tables").EnumerateArray())
                        consumers.Add(new(artifact, "locations", "arena2/MAPS.BSA", table.GetProperty("ordinal").GetInt32(),
                            Text(table, "state") == "Empty" ? "unused" : null));
            if (!offline && root.TryGetProperty("sourceKey", out _) && root.TryGetProperty("sourceOrdinal", out var blockOrdinal)
                && root.TryGetProperty("models", out _) && root.TryGetProperty("kind", out _))
                consumers.Add(new(artifact, "world-block", "arena2/BLOCKS.BSA", blockOrdinal.GetInt32()));
            if (!offline && Text(root, "inventorySource") is { } meshSource && root.TryGetProperty("meshes", out var meshes))
                foreach (var mesh in meshes.EnumerateArray())
                    if (Text(mesh, "relativePath") is not null && mesh.TryGetProperty("sourceOrdinal", out var meshOrdinal))
                        consumers.Add(new(artifact, "meshes", meshSource, meshOrdinal.GetInt32()));
            if (!offline && root.TryGetProperty("cinematics", out var cinematics) && cinematics.ValueKind == JsonValueKind.Object
                && cinematics.TryGetProperty("cinematics", out var movies))
                foreach (var movie in movies.EnumerateArray())
                    if (movie.TryGetProperty("artifact", out var media) && media.ValueKind == JsonValueKind.Object)
                        AddReference("arena2/" + movie.GetProperty("fileName").GetString(), "cinematics");
            // The existing producer ledgers enumerate every raw block/model record, including unusable ones.
            if (root.TryGetProperty("geometry", out var geometry)) ReadLedger(geometry, "geometry");
            if (offline && root.TryGetProperty("records", out _) && root.TryGetProperty("sources", out var ledgerSources)
                && ledgerSources.ValueKind == JsonValueKind.Array
                && ledgerSources.EnumerateArray().Any(value => Text(value, "recordId") == "CNT-005")) ReadLedger(root, "blocks");
            if (root.TryGetProperty("unreadableFamilies", out var unreadable))
                foreach (JsonElement entry in unreadable.EnumerateArray())
                    unsupported.Add(new(artifact, entry.GetProperty("family").GetString()!,
                        [.. entry.GetProperty("files").EnumerateArray().Select(value => value.GetString()!)], entry.GetProperty("reason").GetString()!));
            if (root.TryGetProperty("books", out var books) && books.TryGetProperty("books", out var bookRecords))
                foreach (JsonElement book in bookRecords.EnumerateArray())
                    if (Text(book, "disposition") != "notSupplied")
                        AddReference("arena2/books/" + book.GetProperty("fileName").GetString(), "books");

            void ReadLedger(JsonElement ledger, string section)
            {
                foreach (JsonElement record in ledger.GetProperty("records").EnumerateArray())
                {
                    string source = record.GetProperty("source").GetString()!;
                    string disposition = record.GetProperty("disposition").GetString()!;
                    if (Text(record, "state") != "read") disposition = "malformed";
                    consumers.Add(new(artifact, section, source, record.GetProperty("ordinal").GetInt32(), disposition, OfflineOnly: true));
                }
            }
            void AddReference(string source, string section, int? ordinal = null)
            {
                if (!source.StartsWith("arena2/", StringComparison.Ordinal))
                {
                    if (inventory.Any(row => row.RowType == "family" && row.PathOrPattern == source))
                        consumers.Add(new(artifact, section, source, ordinal, OfflineOnly: offline));
                    return;
                }
                SourceInventoryRow[] files = [.. inventory.Where(row => row.RowType == "file" && row.PathOrPattern == source)];
                if (files.Length != 0) { consumers.Add(new(artifact, section, source, ordinal, OfflineOnly: offline)); return; }
                // A family's aggregate citation (or a directory such as books/) is only context.
                string[] families = [.. inventory.Where(row => row.RowType == "family" && row.PathOrPattern == source).Select(row => row.FamilyId)];
                SourceInventoryRow[] members = [.. inventory.Where(row => row.RowType == "file"
                    && (families.Contains(row.FamilyId, StringComparer.Ordinal) || row.PathOrPattern.StartsWith(source.TrimEnd('/') + "/", StringComparison.Ordinal)))];
                if (members.Length == 0) consumers.Add(new(artifact, section, source, ordinal, OfflineOnly: offline));
                foreach (var member in members) consumers.Add(new(artifact, section, member.PathOrPattern, OfflineOnly: offline, ContextOnly: true));
            }
            void ReadReference(JsonElement value, string section)
            {
                if (value.ValueKind == JsonValueKind.String) AddReference(value.GetString()!, section);
                else if (value.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement child in value.EnumerateArray()) ReadReference(child, section);
                else if (value.ValueKind == JsonValueKind.Object)
                {
                    int? ordinal = value.TryGetProperty("archiveOrdinal", out var number) && number.ValueKind == JsonValueKind.Number ? number.GetInt32() : null;
                    if (Text(value, "path") is { } path) AddReference(path, section, ordinal);
                    else if (Text(value, "sourcePath") is { } sourcePath) AddReference(sourcePath, section, ordinal);
                }
            }
            void ReadSourceFile(JsonElement value, string section)
            {
                if (value.ValueKind != JsonValueKind.String) return;
                string source = value.GetString()!;
                if (source.StartsWith("arena2/", StringComparison.Ordinal)) { AddReference(source, section); return; }
                // Media producers cite bare Arena2 leaves, so only an unambiguous admitted inventory match
                // can become an Arena2 citation.
                string[] matches = [.. inventory.Where(row => row.RowType == "file"
                    && row.PathOrPattern.StartsWith("arena2/", StringComparison.Ordinal)
                    && string.Equals(row.PathOrPattern.Split('/')[^1], source, StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.PathOrPattern).Distinct(StringComparer.Ordinal).ToArray()];
                if (matches.Length > 1) throw new InvalidOperationException($"{artifact}/{section}: sourceFile '{source}' matches multiple admitted Arena2 paths.");
                if (matches.Length == 1) AddReference(matches[0], section);
            }
            void Walk(JsonElement value, string section)
            {
                if (value.ValueKind == JsonValueKind.Array)
                { foreach (JsonElement child in value.EnumerateArray()) Walk(child, section); return; }
                if (value.ValueKind != JsonValueKind.Object) return;
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (property.Name == "sourceFile") ReadSourceFile(property.Value, section);
                    if (property.Name is "source" or "sources" or "sourcePath" or "paletteSourcePath" or "vidSource" or "flcSource")
                        ReadReference(property.Value, section);
                    Walk(property.Value, section == "root" ? property.Name : section);
                }
            }
        }
        return new([.. consumers.Distinct().OrderBy(value => value.SourcePath, StringComparer.Ordinal)
                .ThenBy(value => value.Artifact, StringComparer.Ordinal).ThenBy(value => value.Section, StringComparer.Ordinal).ThenBy(value => value.ArchiveOrdinal)],
            unsupported);
    }
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
}
