namespace Daggerfall.Import.Publication;

/// <summary>What reconciliation found: disagreeing dispositions, and rows it could not resolve at all.</summary>
public sealed record SourceInventoryReconciliation(IReadOnlyList<string> Drift, IReadOnlyList<string> Unreconciled)
{
    public bool IsClean => Drift.Count == 0 && Unreconciled.Count == 0;
}

/// <summary>
/// Keeps the documented inventory's disposition column honest against a scan. Drift
/// is reported rather than refused, and only the disposition column is ever written:
/// row order, stable ids and every other documented field are preserved.
/// </summary>
public static class SourceInventoryReconciler
{
    public const string DispositionHeader = "disposition";

    /// <summary>
    /// Reports each documented file row whose disposition disagrees with the scan, and
    /// every documented row the scan never resolved to a record. A row that silently
    /// falls out of reconciliation would let the inventory and the tree disagree while
    /// the tool still reports that they match.
    /// </summary>
    public static SourceInventoryReconciliation Reconcile(string inventoryFile, IReadOnlyList<SourceManifestRecord> records, bool update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inventoryFile);
        ArgumentNullException.ThrowIfNull(records);
        Dictionary<string, SourceRecordDisposition> computed = records
            .Where(record => !record.Id.StartsWith("scan.", StringComparison.Ordinal))
            .ToDictionary(record => record.Id, record => record.Disposition, StringComparer.Ordinal);
        string text = File.ReadAllText(inventoryFile);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        // Mixed terminators would merge two rows into one unparseable line, and a row
        // that cannot be parsed must be reported rather than skipped as if it agreed.
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<string> drift = [];
        List<string> unresolved = [];
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

            if (!computed.TryGetValue(fields[0], out SourceRecordDisposition disposition))
            {
                unresolved.Add($"{fields[0]}: the scan produced no record for this documented row");
                continue;
            }

            string documented = fields[9];
            string actual = ToDocumented(disposition);
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

        // An unparseable row is not a disposition to rewrite, so it blocks an update
        // rather than being silently dropped by one.
        if (update && drift.Count != 0 && unresolved.Count == 0)
        {
            File.WriteAllText(inventoryFile, string.Join(newline, lines));
        }

        return new SourceInventoryReconciliation(drift, unresolved);
    }

    /// <summary>The hyphenated spelling the inventory already uses for its dispositions.</summary>
    public static string ToDocumented(SourceRecordDisposition disposition) => disposition switch
    {
        SourceRecordDisposition.RequiredPending => "required-pending",
        SourceRecordDisposition.SourceGap => "source-gap",
        _ => disposition.ToString().ToLowerInvariant(),
    };
}
