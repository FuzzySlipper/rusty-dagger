using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Normalized;

/// <summary>
/// Publishes Daggerfall Unity's localized Internal_Strings table into the shared text contract.
/// This is a distinct donor text family: it supplements classic TEXT.RSC without replacing it.
/// </summary>
public static class DaggerfallInternalStringsBuilder
{
    /// <summary>The stable provenance identity for the donor's managed localization table.</summary>
    public const string SourceRecordId = "DFU-Internal-Strings";

    /// <summary>Reads and merges the supplied table, refusing a second internal table or key collision.</summary>
    public static DaggerfallText Merge(DaggerfallText existing, byte[] bytes, string label, string language)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        existing.Validate();
        if (existing.Sources.Any(source => source.Kind == DaggerfallTextKind.Internal))
        {
            throw new InvalidOperationException("Published text already carries an Internal source, so a second localized table would leave the family ambiguous.");
        }

        InternalStringsCatalog catalog = InternalStringsReader.Read(bytes, label);
        DaggerfallTextSource source = new(
            DaggerfallTextKind.Internal,
            SourceRecordId,
            label,
            language,
            bytes.LongLength,
            catalog.HeaderLength,
            catalog.Records.Count);
        List<DaggerfallTextRecord> records = [.. existing.Records];
        foreach (InternalStringRecord entry in catalog.Records)
        {
            records.Add(FamilyTextRecords.Plain(
                DaggerfallTextKind.Internal,
                entry.Key,
                label,
                entry.Index,
                checked((int)entry.Offset),
                entry.ByteLength,
                entry.Value));
        }

        DaggerfallText published = new(
            [.. existing.Sources, source],
            existing.PendingKinds,
            [.. records.OrderBy(record => record.Source, StringComparer.Ordinal).ThenBy(record => record.Index)],
            [.. DaggerfallTextBuilder.MacroIndex(records)]);
        published.Validate();
        return published;
    }
}
