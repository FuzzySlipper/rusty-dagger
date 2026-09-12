using System.Security.Cryptography;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>One row of the documented source inventory, read strictly.</summary>
public sealed record SourceInventoryRow(
    string Id,
    string RowType,
    string FamilyId,
    string Kind,
    string PathOrPattern,
    string RecordOrStem,
    string Disposition,
    string Notes);

/// <summary>Decoded archive identities, or why the archive could not be read.</summary>
public sealed record SourceArchiveDecode(IReadOnlyList<SourceManifestRecord> Records, string? Failure);

/// <summary>
/// What to scan: the logical root the records are cited under, the logical name of
/// the inventory they reconcile against, the directory that supplies the bytes, and
/// the source names each consumer already claims.
/// </summary>
public sealed record SourceManifestRequest(
    string SourceRoot,
    string InventoryPath,
    string SourceDirectory,
    IReadOnlyCollection<string> ImportedNames,
    IReadOnlyCollection<string> RequiredPendingNames,
    IReadOnlyCollection<string> ExcludedNames);

/// <summary>
/// Reconciles the documented source inventory against a real source tree: every
/// documented file and every record decoded out of an admitted archive becomes one
/// record with exactly one disposition, and anything the tree supplies without a
/// documented row is reported rather than skipped.
/// </summary>
public static class SourceManifestBuilder
{
    /// <summary>Family for content the source tree supplies without any documented row.</summary>
    public const string UndocumentedFamily = "scan.undocumented";

    private static readonly string[] Header =
    [
        "id", "row_type", "family_id", "kind", "path_or_pattern", "available_count", "byte_size", "record_or_stem", "current_scope", "disposition", "notes",
    ];

    /// <summary>Reads the inventory, rejecting a row it cannot interpret instead of guessing.</summary>
    public static IReadOnlyList<SourceInventoryRow> ReadInventory(ReadOnlySpan<byte> csv)
    {
        string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(csv);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || lines[0].Length == 0)
        {
            throw new InvalidOperationException("The source inventory is empty.");
        }

        string[] header = lines[0].Split(',');
        if (!header.SequenceEqual(Header, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"The source inventory header is '{lines[0]}', which is not the documented column order.");
        }

        List<SourceInventoryRow> rows = [];
        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = line.Split(',');
            if (fields.Length != Header.Length)
            {
                throw new InvalidOperationException($"Source inventory line {index + 1} has {fields.Length} fields where {Header.Length} are documented.");
            }

            if (fields[1] is not ("family" or "file"))
            {
                throw new InvalidOperationException($"Source inventory line {index + 1} has row type '{fields[1]}', which is neither 'family' nor 'file'.");
            }

            rows.Add(new SourceInventoryRow(fields[0], fields[1], fields[2], fields[3], fields[4], fields[7], fields[9], fields[10]));
        }

        return rows;
    }

    public static SourceManifest Build(SourceManifestRequest request, ReadOnlySpan<byte> inventoryCsv)
    {
        ArgumentNullException.ThrowIfNull(request);
        NormalizedImportDocument.RequireLogicalPath(request.SourceRoot, nameof(request.SourceRoot));
        NormalizedImportDocument.RequireLogicalPath(request.InventoryPath, nameof(request.InventoryPath));

        IReadOnlyList<SourceInventoryRow> inventory = ReadInventory(inventoryCsv);
        // A family row's own id can differ from the family_id its file rows cite
        // (the quest families do exactly that), so membership is the family_id column.
        Dictionary<string, string> familyPaths = inventory
            .Where(row => row.RowType == "family")
            .GroupBy(row => row.FamilyId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().PathOrPattern, StringComparer.Ordinal);
        // A consumer claims a source by the path it reads, which is relative to the
        // source root; a leaf name is accepted too so a caller can name either.
        HashSet<string> imported = new(request.ImportedNames, StringComparer.Ordinal);
        HashSet<string> pending = new(request.RequiredPendingNames, StringComparer.Ordinal);
        HashSet<string> excluded = new(request.ExcludedNames, StringComparer.Ordinal);
        static bool Claimed(HashSet<string> names, string relative) =>
            names.Contains(relative) || names.Contains(relative.Split('/')[^1]);

        if (!Directory.Exists(request.SourceDirectory))
        {
            throw new DirectoryNotFoundException($"The admitted source directory '{request.SourceDirectory}' does not exist.");
        }

        string[] entries = Directory.GetFileSystemEntries(request.SourceDirectory);
        // Documented paths are relative to the source root, so a family that lives in
        // a subdirectory (the book texts do) must match there rather than being
        // reported as unsupplied.
        Dictionary<string, string> byExactPath = new(StringComparer.Ordinal);
        Dictionary<string, string> byLoosePath = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(request.SourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(request.SourceDirectory, file).Replace('\\', '/');
            byExactPath.TryAdd(relative, relative);
            byLoosePath.TryAdd(relative, relative);
        }

        List<SourceManifestRecord> records = [];
        HashSet<string> claimed = new(StringComparer.Ordinal);
        foreach (SourceInventoryRow row in inventory.Where(row => row.RowType == "file"))
        {
            string pattern = LeafName(row.PathOrPattern);
            string familyId = row.FamilyId;
            string familyPath = familyPaths.TryGetValue(familyId, out string? documented) ? documented : request.SourceRoot;
            if (pattern.Contains('*') || pattern.Contains('?'))
            {
                string[] matches = byExactPath.Keys
                    .Where(relative => Matches(pattern, relative))
                    .OrderBy(relative => relative, StringComparer.Ordinal)
                    .ToArray();
                if (matches.Length == 0)
                {
                    records.Add(Gap($"{row.Id}.unmatched", familyId, familyPath, $"{request.SourceRoot}/{pattern}", $"Documented pattern '{row.PathOrPattern}' matched no supplied file."));
                }

                foreach (string match in matches)
                {
                    records.Add(Describe($"{row.Id}.{match}", familyId, familyPath, match));
                }

                continue;
            }

            if (byExactPath.TryGetValue(pattern, out string? exact))
            {
                records.Add(Describe(row.Id, familyId, familyPath, exact));
                continue;
            }

            if (byLoosePath.TryGetValue(pattern, out string? loose))
            {
                // The inventory's casing is what a case-insensitive host reports; the
                // record keeps the casing the source tree actually uses.
                records.Add(Describe(row.Id, familyId, familyPath, loose, $"Documented as '{pattern}'; supplied as '{loose}'."));
                continue;
            }

            records.Add(Gap(row.Id, familyId, familyPath, $"{request.SourceRoot}/{pattern}", "Documented by the inventory; this source tree does not supply it."));
        }

        // Anything supplied without a documented row is reported, not skipped: this is
        // where the scan refuses to let unexamined content pass as covered.
        foreach (string entry in entries.OrderBy(entry => Path.GetFileName(entry), StringComparer.Ordinal))
        {
            string name = Path.GetFileName(entry);
            if (claimed.Contains(name))
            {
                continue;
            }

            bool directory = Directory.Exists(entry);
            records.Add(new SourceManifestRecord(
                $"scan.{name}",
                UndocumentedFamily,
                request.SourceRoot,
                $"{request.SourceRoot}/{name}",
                directory ? 0 : new FileInfo(entry).Length,
                directory ? null : ContentDigest.Compute(File.ReadAllBytes(entry)),
                null,
                null,
                directory ? SourceRecordDisposition.Excluded : SourceRecordDisposition.Unresolved,
                directory ? "Supplied directory, not a source record." : "Supplied by the source tree with no documented inventory row."));
        }

        return new SourceManifest(
            SourceManifest.CurrentSchemaVersion,
            request.SourceRoot,
            request.InventoryPath,
            records,
            inventory.Where(row => row.RowType == "family").Select(row => row.FamilyId)
                .Concat(inventory.Where(row => row.RowType == "file").Select(row => row.FamilyId))
                .Concat(records.Where(record => StringComparer.Ordinal.Equals(record.FamilyId, UndocumentedFamily)).Select(record => record.FamilyId))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => SourceManifestFamilyCount.From(id, records.Where(record => StringComparer.Ordinal.Equals(record.FamilyId, id))))
                .ToArray());

        SourceManifestRecord Gap(string id, string familyId, string familyPath, string sourcePath, string note) =>
            new(id, familyId, familyPath, sourcePath, 0, null, null, null, SourceRecordDisposition.SourceGap, note);

        SourceManifestRecord Describe(string id, string familyId, string familyPath, string relative, string? note = null)
        {
            string path = Path.Combine(request.SourceDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            string logical = $"{request.SourceRoot}/{relative}";
            if (Directory.Exists(path))
            {
                return new SourceManifestRecord(id, familyId, familyPath, logical, 0, null, null, null, SourceRecordDisposition.Excluded, "Documented entry is a supplied directory, not a source record.");
            }

            byte[] bytes = File.ReadAllBytes(path);
            SourceRecordDisposition disposition = Claimed(excluded, relative) ? SourceRecordDisposition.Excluded
                : Claimed(imported, relative) ? SourceRecordDisposition.Imported
                : Claimed(pending, relative) ? SourceRecordDisposition.RequiredPending
                : SourceRecordDisposition.Unused;
            if (!claimed.Add(relative))
            {
                disposition = SourceRecordDisposition.Duplicate;
                note = $"Supplied file already recorded under another inventory row; content is not counted twice.";
            }

            return new SourceManifestRecord(
                id,
                familyId,
                familyPath,
                logical,
                bytes.Length,
                ContentDigest.Compute(bytes),
                ArchiveKey: null,
                ArchiveOrdinal: null,
                disposition,
                note ?? DescribeDisposition(disposition));
        }
    }

    /// <summary>Decodes one archive's records so their identities are recorded too.</summary>
    public static SourceArchiveDecode DecodeArchiveRecords(SourceManifestRecord archive, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(archive);
        BsaArchive parsed;
        try
        {
            parsed = BsaArchive.Parse(bytes, archive.SourcePath);
        }
        catch (Arena2FormatException failure)
        {
            // The record's disposition has to say the archive could not be read; an
            // empty record list would look exactly like an archive with no records.
            return new SourceArchiveDecode([], failure.Message);
        }

        List<SourceManifestRecord> records = [];
        foreach (BsaRecord record in parsed.Records)
        {
            string key = record.Name ?? record.NumericId?.ToString() ?? record.Ordinal.ToString();
            records.Add(new SourceManifestRecord(
                $"{archive.Id}.record.{record.Ordinal:D4}",
                archive.FamilyId,
                archive.FamilyPath,
                archive.SourcePath,
                record.Length,
                ContentDigest.Compute(parsed.GetPayload(record).Span),
                key,
                record.Ordinal,
                SourceRecordDisposition.RequiredPending,
                $"Decoded from '{archive.SourcePath}'; no normalizer cites this record individually yet."));
        }

        return new SourceArchiveDecode(records, Failure: null);
    }

    /// <summary>
    /// The whole scan a consumer needs: the inventory reconciled against the tree,
    /// archive identities decoded, a corrupt archive recorded as malformed, and the
    /// family counts recomputed from what was actually found. One entry point keeps
    /// the standalone command and the publication closure from drifting apart.
    /// </summary>
    public static SourceManifest Scan(SourceManifestRequest request, ReadOnlySpan<byte> inventoryCsv)
    {
        SourceManifest manifest = Build(request, inventoryCsv);
        List<SourceManifestRecord> records = [];
        foreach (SourceManifestRecord record in manifest.Records)
        {
            if (record.Disposition is SourceRecordDisposition.SourceGap or SourceRecordDisposition.Excluded
                || !record.SourcePath.EndsWith(".BSA", StringComparison.OrdinalIgnoreCase))
            {
                records.Add(record);
                continue;
            }

            SourceArchiveDecode decoded = DecodeArchiveRecords(
                record,
                File.ReadAllBytes(Path.Combine(request.SourceDirectory, RelativePath(request.SourceRoot, record.SourcePath))));
            if (decoded.Failure is string failure)
            {
                records.Add(record with { Disposition = SourceRecordDisposition.Malformed, Note = failure });
                continue;
            }

            records.Add(record);
            records.AddRange(decoded.Records);
        }

        return manifest with
        {
            Records = records,
            Families = manifest.Families
                .Select(family => SourceManifestFamilyCount.From(
                    family.FamilyId,
                    records.Where(record => StringComparer.Ordinal.Equals(record.FamilyId, family.FamilyId))))
                .ToArray(),
        };
    }

    private static string RelativePath(string sourceRoot, string sourcePath) =>
        sourcePath.StartsWith($"{sourceRoot}/", StringComparison.Ordinal) ? sourcePath[(sourceRoot.Length + 1)..] : sourcePath;

    private static string DescribeDisposition(SourceRecordDisposition disposition) => disposition switch
    {
        SourceRecordDisposition.Imported => "Claimed by the publication plan's source set.",
        SourceRecordDisposition.RequiredPending => "Admitted and required by a named normalizer that has not consumed it yet.",
        SourceRecordDisposition.Excluded => "Documented as out of scope for this source tree.",
        _ => "Supplied and admitted, but no consumer claims it.",
    };

    private static string LeafName(string pathOrPattern) =>
        pathOrPattern.StartsWith("local/arena2/", StringComparison.Ordinal) ? pathOrPattern["local/arena2/".Length..] : pathOrPattern;

    private static bool Matches(string pattern, string name)
    {
        int star = pattern.IndexOf('*');
        if (star < 0)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(pattern, name);
        }

        string prefix = pattern[..star];
        string suffix = pattern[(star + 1)..];
        return name.Length >= prefix.Length + suffix.Length
            && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
