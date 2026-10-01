using System.Text.Json;

namespace Daggerfall.Import.Publication;

public sealed record SourceCoverageInputs(IReadOnlyList<SourceCoverageConsumer> Consumers,
    IReadOnlyList<SourceCoverageQuestHandoff> Quests, IReadOnlyList<SourceCoverageUnsupported> Unsupported);

/// <summary>Reads existing producer citations and ledgers; patterns remain context, not proof of individual records.</summary>
public static class SourceCoverageInputReader
{
    public static SourceCoverageInputs Read(string repositoryRoot, IEnumerable<string> artifactFiles,
        IReadOnlyList<SourceInventoryRow> inventory)
    {
        List<SourceCoverageConsumer> consumers = [];
        List<SourceCoverageQuestHandoff> quests = [];
        List<SourceCoverageUnsupported> unsupported = [];
        Dictionary<string, List<string>> corpora = new(StringComparer.Ordinal);
        foreach (string file in artifactFiles.Order(StringComparer.Ordinal))
        {
            string artifact = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
            bool offline = artifact.StartsWith("import-records/", StringComparison.Ordinal);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;
            if (root.TryGetProperty("families", out _) && root.TryGetProperty("records", out _)) continue;
            Walk(root, "root");
            if (root.TryGetProperty("questOriginalSources", out var original))
                foreach (JsonElement quest in original.GetProperty("quests").EnumerateArray())
                {
                    foreach (string field in new[] { "binaryPath", "resourcePath" })
                        if (Text(quest, field) is { } path && inventory.Any(row => row.RowType == "file" && row.PathOrPattern == "arena2/" + path))
                            AddReference("arena2/" + path, "questOriginalSources");
                    quests.Add(new(quest.GetProperty("stem").GetString()!, quest.GetProperty("selection").GetString()!,
                        quest.GetProperty("availability").GetString()!, Text(quest, "binaryPath"), Text(quest, "resourcePath"),
                        Text(quest, "rewrittenSourceFile"), []));
                }
            // The existing producer ledgers enumerate every raw block/model record, including unusable ones.
            if (root.TryGetProperty("geometry", out var geometry)) ReadLedger(geometry, "geometry");
            if (offline && root.TryGetProperty("records", out _) && root.TryGetProperty("sources", out var ledgerSources)
                && ledgerSources.ValueKind == JsonValueKind.Array
                && ledgerSources.EnumerateArray().Any(value => Text(value, "recordId") == "CNT-005")) ReadLedger(root, "blocks");
            if (root.TryGetProperty("unreadableFamilies", out var unreadable))
                foreach (JsonElement entry in unreadable.EnumerateArray())
                    unsupported.Add(new(artifact, entry.GetProperty("family").GetString()!,
                        [.. entry.GetProperty("files").EnumerateArray().Select(value => value.GetString()!)], entry.GetProperty("reason").GetString()!));
            if (root.TryGetProperty("catalogSource", out _) && root.TryGetProperty("quests", out _)) ReadCorpus(root);
            if (root.TryGetProperty("books", out var books) && books.TryGetProperty("books", out var bookRecords))
                foreach (JsonElement book in bookRecords.EnumerateArray())
                    if (Text(book, "disposition") != "notSupplied")
                        AddReference("arena2/books/" + book.GetProperty("fileName").GetString(), "books");

            void ReadCorpus(JsonElement corpus)
            {
                if (!corpus.TryGetProperty("quests", out var receipts)) return;
                foreach (JsonElement receipt in receipts.EnumerateArray())
                {
                    if (!receipt.TryGetProperty("sourceFingerprint", out _)) continue;
                    string name = receipt.GetProperty("name").GetString()!;
                    if (!corpora.TryGetValue(name, out var artifacts)) corpora[name] = artifacts = [];
                    artifacts.Add(artifact);
                }
            }
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
            void AddReference(string source, string section)
            {
                if (!source.StartsWith("arena2/", StringComparison.Ordinal))
                {
                    if (inventory.Any(row => row.RowType == "family" && row.PathOrPattern == source))
                        consumers.Add(new(artifact, section, source, OfflineOnly: offline));
                    return;
                }
                SourceInventoryRow[] files = [.. inventory.Where(row => row.RowType == "file" && row.PathOrPattern == source)];
                if (files.Length != 0) { consumers.Add(new(artifact, section, source, OfflineOnly: offline)); return; }
                // A family's aggregate citation (or a directory such as books/) is only context.
                string[] families = [.. inventory.Where(row => row.RowType == "family" && row.PathOrPattern == source).Select(row => row.FamilyId)];
                SourceInventoryRow[] members = [.. inventory.Where(row => row.RowType == "file"
                    && (families.Contains(row.FamilyId, StringComparer.Ordinal) || row.PathOrPattern.StartsWith(source.TrimEnd('/') + "/", StringComparison.Ordinal)))];
                if (members.Length == 0) consumers.Add(new(artifact, section, source, OfflineOnly: offline));
                foreach (var member in members) consumers.Add(new(artifact, section, member.PathOrPattern, OfflineOnly: offline, ContextOnly: true));
            }
            void ReadReference(JsonElement value, string section)
            {
                if (value.ValueKind == JsonValueKind.String) AddReference(value.GetString()!, section);
                else if (value.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement child in value.EnumerateArray()) ReadReference(child, section);
                else if (value.ValueKind == JsonValueKind.Object)
                {
                    if (Text(value, "path") is { } path) AddReference(path, section);
                    else if (Text(value, "sourcePath") is { } sourcePath) AddReference(sourcePath, section);
                }
            }
            void ReadSourceFile(JsonElement value, string section)
            {
                if (value.ValueKind != JsonValueKind.String) return;
                string source = value.GetString()!;
                if (source.StartsWith("arena2/", StringComparison.Ordinal)) { AddReference(source, section); return; }
                // Media producers cite bare Arena2 leaves; quest corpus sourceFile is donor .txt,
                // so only an unambiguous admitted inventory match can become an Arena2 citation.
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
                    if (property.Name is "source" or "sources" or "sourcePath" or "vidSource" or "flcSource")
                        ReadReference(property.Value, section);
                    Walk(property.Value, section == "root" ? property.Name : section);
                }
            }
        }
        return new([.. consumers.Distinct().OrderBy(value => value.SourcePath, StringComparer.Ordinal)
                .ThenBy(value => value.Artifact, StringComparer.Ordinal).ThenBy(value => value.Section, StringComparer.Ordinal).ThenBy(value => value.ArchiveOrdinal)],
            [.. quests.Select(quest => quest with { CorpusArtifacts = corpora.TryGetValue(quest.Stem, out var artifacts)
                ? [.. artifacts.Distinct().Order(StringComparer.Ordinal)] : [] }).OrderBy(value => value.Stem, StringComparer.Ordinal)],
            unsupported);
    }
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
}
