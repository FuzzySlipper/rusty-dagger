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
        Dictionary<string, string> familyPaths = inventory
            .Where(row => row.RowType == "family")
            .ToDictionary(row => row.Id, row => row.PathOrPattern, StringComparer.Ordinal);
        HashSet<string> imported = new(request.ImportedNames, StringComparer.Ordinal);
        HashSet<string> pending = new(request.RequiredPendingNames, StringComparer.Ordinal);
        HashSet<string> excluded = new(request.ExcludedNames, StringComparer.Ordinal);

        string[] entries = Directory.Exists(request.SourceDirectory)
            ? Directory.GetFileSystemEntries(request.SourceDirectory)
            : throw new DirectoryNotFoundException($"The admitted source directory '{request.SourceDirectory}' does not exist.");
        Dictionary<string, string> byExactName = new(StringComparer.Ordinal);
        Dictionary<string, string> byLooseName = new(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);
            byExactName.TryAdd(name, entry);
            byLooseName.TryAdd(name, entry);
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
                string[] matches = entries
                    .Select(entry => Path.GetFileName(entry))
                    .Where(name => name.Length != 0 && Matches(pattern, name))
                    .OrderBy(name => name, StringComparer.Ordinal)
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

            if (byExactName.TryGetValue(pattern, out string? exact))
            {
                records.Add(Describe(row.Id, familyId, familyPath, Path.GetFileName(exact)));
                continue;
            }

            if (byLooseName.TryGetValue(pattern, out string? loose))
            {
                // The inventory's casing is what a case-insensitive host reports; the
                // record keeps the casing the source tree actually uses.
                records.Add(Describe(row.Id, familyId, familyPath, Path.GetFileName(loose), $"Documented as '{pattern}'; supplied as '{Path.GetFileName(loose)}'."));
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
                ScanFamily(name),
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
            inventory.Where(row => row.RowType == "family").Select(row => row.Id).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => SourceManifestFamilyCount.From(id, records.Where(record => StringComparer.Ordinal.Equals(record.FamilyId, id))))
                .ToArray());

        SourceManifestRecord Gap(string id, string familyId, string familyPath, string sourcePath, string note) =>
            new(id, familyId, familyPath, sourcePath, 0, null, null, null, SourceRecordDisposition.SourceGap, note);

        SourceManifestRecord Describe(string id, string familyId, string familyPath, string name, string? note = null)
        {
            string path = Path.Combine(request.SourceDirectory, name);
            string logical = $"{request.SourceRoot}/{name}";
            if (Directory.Exists(path))
            {
                return new SourceManifestRecord(id, familyId, familyPath, logical, 0, null, null, null, SourceRecordDisposition.Excluded, "Documented entry is a supplied directory, not a source record.");
            }

            byte[] bytes = File.ReadAllBytes(path);
            SourceRecordDisposition disposition = excluded.Contains(name) ? SourceRecordDisposition.Excluded
                : imported.Contains(name) ? SourceRecordDisposition.Imported
                : pending.Contains(name) ? SourceRecordDisposition.RequiredPending
                : SourceRecordDisposition.Unused;
            if (!claimed.Add(name))
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
    public static IReadOnlyList<SourceManifestRecord> DecodeArchiveRecords(SourceManifestRecord archive, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(archive);
        BsaArchive parsed;
        try
        {
            parsed = BsaArchive.Parse(bytes, archive.SourcePath);
        }
        catch (Arena2FormatException)
        {
            return [];
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

        return records;
    }

    private static string DescribeDisposition(SourceRecordDisposition disposition) => disposition switch
    {
        SourceRecordDisposition.Imported => "Claimed by the publication plan's source set.",
        SourceRecordDisposition.RequiredPending => "Admitted and required by a named normalizer that has not consumed it yet.",
        SourceRecordDisposition.Excluded => "Documented as out of scope for this source tree.",
        _ => "Supplied and admitted, but no consumer claims it.",
    };

    private static string LeafName(string pathOrPattern) =>
        pathOrPattern.StartsWith("local/arena2/", StringComparison.Ordinal) ? pathOrPattern["local/arena2/".Length..] : pathOrPattern;

    private static string ScanFamily(string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        return stem.Length == 0 ? "scan" : $"scan.{stem}";
    }

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
