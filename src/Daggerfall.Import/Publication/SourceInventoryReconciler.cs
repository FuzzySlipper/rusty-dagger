namespace Daggerfall.Import.Publication;

/// <summary>
/// What reconciliation found and what it did: disagreeing dispositions, rows it could
/// not resolve, rows it refuses to mark unused, and whether the inventory was actually
/// rewritten. A caller reporting an update has to read <see cref="Updated"/> rather than
/// infer it from the request, or it will announce a write that was refused.
/// </summary>
public sealed record SourceInventoryReconciliation(
    IReadOnlyList<string> Drift,
    IReadOnlyList<string> Unreconciled,
    IReadOnlyList<string> Refused,
    bool Updated,
    bool UpdateRequested)
{
    public bool IsClean => Drift.Count == 0 && Unreconciled.Count == 0 && Refused.Count == 0;

    /// <summary>
    /// True when an update was asked for and refused, because a row was unreadable or would
    /// mark unused a source a publication reads. A report-only run refuses nothing, so it
    /// must not claim to have been blocked.
    /// </summary>
    public bool UpdateBlocked => UpdateRequested && !Updated && (Unreconciled.Count != 0 || Refused.Count != 0);
}

/// <summary>
/// Keeps the documented inventory's disposition column honest against a scan. Drift
/// is reported rather than refused, and only the disposition column is ever written:
/// row order, stable ids and every other documented field are preserved.
/// </summary>
public static class SourceInventoryReconciler
{
    public const string DispositionHeader = "disposition";

    /// <summary>Reconciles the inventory with the coverage of every publication the product ships.</summary>
    public static SourceInventoryReconciliation Reconcile(string inventoryFile, SourceCoverageReport coverage, bool update)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        return Reconcile(inventoryFile, [.. coverage.Records.Select(record => record.Source with { Disposition = record.Disposition })],
            [.. coverage.Records.SelectMany(record => record.Consumers)], update);
    }

    /// <summary>
    /// Reports each documented file row whose disposition disagrees with the scan, and
    /// every documented row the scan never resolved to a record. A row that silently
    /// falls out of reconciliation would let the inventory and the tree disagree while
    /// the tool still reports that they match.
    /// </summary>
    /// <remarks>
    /// <paramref name="citations"/> are what each artifact says it read. A row the records
    /// would write <c>unused</c> while a published artifact cites its source is refused,
    /// naming the source and the artifact: records drawn from fewer publications than the
    /// product ships would otherwise write a used source down as unused. An offline record
    /// is not a publication, and a producer ledger that itself calls the source unused is
    /// not a read.
    /// </remarks>
    public static SourceInventoryReconciliation Reconcile(string inventoryFile, IReadOnlyList<SourceManifestRecord> records,
        IReadOnlyList<SourceCoverageConsumer> citations, bool update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inventoryFile);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(citations);
        ILookup<string, string> readers = citations
            .Where(citation => !citation.OfflineOnly && citation.LedgerDisposition != "unused")
            .ToLookup(citation => citation.SourcePath, citation => citation.Artifact, StringComparer.Ordinal);
        Dictionary<string, SourceManifestRecord> computed = records
            .Where(record => !record.Id.StartsWith("scan.", StringComparison.Ordinal))
            .ToDictionary(record => record.Id, StringComparer.Ordinal);
        string text = File.ReadAllText(inventoryFile);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        // Mixed terminators would merge two rows into one unparseable line, and a row
        // that cannot be parsed must be reported rather than skipped as if it agreed.
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<string> drift = [];
        List<string> unresolved = [];
        List<string> refused = [];
        for (int index = 1; index < lines.Length; index++)
        {
            if (lines[index].Length == 0)
            {
                continue;
            }

            string[] fields = lines[index].Split(',');
            if (fields.Length != 11)
            {
                unresolved.Add($"line {index + 1}: {fields.Length} fields where 11 are documented");
                continue;
            }

            if (fields[1] != "file")
            {
                continue;
            }

            if (!computed.TryGetValue(fields[0], out SourceManifestRecord? record))
            {
                unresolved.Add($"{fields[0]}: the scan produced no record for this documented row");
                continue;
            }

            string documented = fields[9];
            string actual = ToDocumented(record.Disposition);
            if (record.Disposition == SourceRecordDisposition.Unused
                && readers[record.SourcePath].Distinct().Order(StringComparer.Ordinal).ToArray() is { Length: > 0 } reading)
            {
                refused.Add($"{fields[0]}: '{record.SourcePath}' would be written unused, but {string.Join(", ", reading)} reads it");
                continue;
            }

            if (StringComparer.Ordinal.Equals(documented, actual))
            {
                continue;
            }

            drift.Add($"{fields[0]}: documented '{documented}', supplied '{actual}'");
            if (update)
            {
                fields[9] = actual;
                lines[index] = string.Join(',', fields);
            }
        }

        // An unparseable row is not a disposition to rewrite, and a used source is not an
        // unused one, so either blocks an update rather than being silently dropped by one.
        bool updated = update && drift.Count != 0 && unresolved.Count == 0 && refused.Count == 0;
        if (updated)
        {
            File.WriteAllText(inventoryFile, string.Join(newline, lines));
        }

        return new SourceInventoryReconciliation(drift, unresolved, refused, updated, update);
    }

    /// <summary>The hyphenated spelling the inventory already uses for its dispositions.</summary>
    public static string ToDocumented(SourceRecordDisposition disposition) => disposition switch
    {
        SourceRecordDisposition.RequiredPending => "required-pending",
        SourceRecordDisposition.SourceGap => "source-gap",
        _ => disposition.ToString().ToLowerInvariant(),
    };
}
