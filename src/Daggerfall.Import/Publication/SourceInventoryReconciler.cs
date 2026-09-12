namespace Daggerfall.Import.Publication;

/// <summary>
/// Keeps the documented inventory's disposition column honest against a scan. Drift
/// is reported rather than refused, and only the disposition column is ever written:
/// row order, stable ids and every other documented field are preserved.
/// </summary>
public static class SourceInventoryReconciler
{
    public const string DispositionHeader = "disposition";

    /// <summary>Reports each documented file row whose disposition disagrees with the scan.</summary>
    public static IReadOnlyList<string> Reconcile(string inventoryFile, IReadOnlyList<SourceManifestRecord> records, bool update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inventoryFile);
        ArgumentNullException.ThrowIfNull(records);
        Dictionary<string, SourceRecordDisposition> computed = records
            .Where(record => !record.Id.StartsWith("scan.", StringComparison.Ordinal))
            .ToDictionary(record => record.Id, record => record.Disposition, StringComparer.Ordinal);
        string text = File.ReadAllText(inventoryFile);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string[] lines = text.Split(newline);
        List<string> drift = [];
        for (int index = 1; index < lines.Length; index++)
        {
            string[] fields = lines[index].Split(',');
            if (fields.Length != 11 || fields[1] != "file" || !computed.TryGetValue(fields[0], out SourceRecordDisposition disposition))
            {
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

        if (update && drift.Count != 0)
        {
            File.WriteAllText(inventoryFile, string.Join(newline, lines));
        }

        return drift;
    }

    /// <summary>The hyphenated spelling the inventory already uses for its dispositions.</summary>
    public static string ToDocumented(SourceRecordDisposition disposition) => disposition switch
    {
        SourceRecordDisposition.RequiredPending => "required-pending",
        SourceRecordDisposition.SourceGap => "source-gap",
        _ => disposition.ToString().ToLowerInvariant(),
    };
}
