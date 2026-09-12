using System.Text.Json;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>
/// What happened to one discovered source record. Every discovered record carries
/// exactly one of these; <see cref="None"/> exists so an undispositioned record is
/// representable and therefore rejectable rather than defaulting to "imported".
/// </summary>
public enum SourceRecordDisposition
{
    /// <summary>No disposition was decided. A manifest carrying this is invalid.</summary>
    None = 0,

    /// <summary>Decoded and published into the normalized pack.</summary>
    Imported,

    /// <summary>Admitted and required by a named normalizer that has not consumed it yet.</summary>
    RequiredPending,

    /// <summary>Supplied and admitted to a family, but no consumer claims it.</summary>
    Unused,

    /// <summary>Another inventory row already claimed the same supplied file.</summary>
    Duplicate,

    /// <summary>Deliberately out of scope, or not a source record at all (for example a directory).</summary>
    Excluded,

    /// <summary>Discovered, and reading or decoding it failed.</summary>
    Malformed,

    /// <summary>Discovered, but nothing determines what it is for.</summary>
    Unresolved,

    /// <summary>Documented by the inventory and not supplied by this source tree.</summary>
    SourceGap,
}

/// <summary>
/// One discovered source record: a supplied file, a record decoded out of an
/// archive, or an inventory entry this source tree does not supply. The identity is
/// stable across runs and is what later normalizers cite.
/// </summary>
public sealed record SourceManifestRecord(
    string Id,
    string FamilyId,
    string FamilyPath,
    string SourcePath,
    long ByteLength,
    ContentDigest? Digest,
    string? ArchiveKey,
    int? ArchiveOrdinal,
    SourceRecordDisposition Disposition,
    string Note)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireLogicalId(FamilyId, nameof(FamilyId));
        NormalizedImportDocument.RequireLogicalPath(FamilyPath, nameof(FamilyPath));
        NormalizedImportDocument.RequireLogicalPath(SourcePath, nameof(SourcePath));
        if (!Enum.IsDefined(Disposition) || Disposition == SourceRecordDisposition.None)
        {
            throw new InvalidOperationException($"Source record '{Id}' has no disposition.");
        }

        if (Note is null)
        {
            throw new ArgumentNullException(nameof(Note), $"Source record '{Id}' must state why it carries its disposition.");
        }

        if (ByteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ByteLength), ByteLength, $"Source record '{Id}' has a negative byte length.");
        }

        if (ArchiveOrdinal is int ordinal && ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ArchiveOrdinal), ordinal, $"Source record '{Id}' has a negative archive ordinal.");
        }

        if (ArchiveKey is not null && string.IsNullOrWhiteSpace(ArchiveKey))
        {
            throw new ArgumentException($"Source record '{Id}' has an empty archive key.", nameof(ArchiveKey));
        }

        // A record that states a byte length states the digest of exactly those bytes.
        // A record with no bytes to state — a documented gap, or a supplied directory
        // that is not a source record — carries neither.
        if (Digest is null && ByteLength != 0)
        {
            throw new InvalidOperationException($"Source record '{Id}' states {ByteLength} bytes without the digest of what was read.");
        }

        Digest?.Validate();
    }
}

/// <summary>Per-family reconciliation counts, recomputed from the records on read.</summary>
public sealed record SourceManifestFamilyCount(
    string FamilyId,
    string DocumentedDisposition,
    int Discovered,
    int Imported,
    int RequiredPending,
    int Unused,
    int Duplicate,
    int Excluded,
    int Malformed,
    int Unresolved,
    int SourceGap)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(FamilyId, nameof(FamilyId));
        if (string.IsNullOrWhiteSpace(DocumentedDisposition))
        {
            throw new InvalidOperationException($"Family '{FamilyId}' must state the disposition the inventory documents for it.");
        }

        if (Discovered != Imported + RequiredPending + Unused + Duplicate + Excluded + Malformed + Unresolved + SourceGap)
        {
            throw new InvalidOperationException($"Family '{FamilyId}' counts {Discovered} discovered records but its dispositions total {Imported + RequiredPending + Unused + Duplicate + Excluded + Malformed + Unresolved + SourceGap}.");
        }
    }

    public static SourceManifestFamilyCount From(string familyId, string documentedDisposition, IEnumerable<SourceManifestRecord> records)
    {
        SourceManifestRecord[] values = records.ToArray();
        return new(
            familyId,
            documentedDisposition,
            values.Length,
            values.Count(record => record.Disposition == SourceRecordDisposition.Imported),
            values.Count(record => record.Disposition == SourceRecordDisposition.RequiredPending),
            values.Count(record => record.Disposition == SourceRecordDisposition.Unused),
            values.Count(record => record.Disposition == SourceRecordDisposition.Duplicate),
            values.Count(record => record.Disposition == SourceRecordDisposition.Excluded),
            values.Count(record => record.Disposition == SourceRecordDisposition.Malformed),
            values.Count(record => record.Disposition == SourceRecordDisposition.Unresolved),
            values.Count(record => record.Disposition == SourceRecordDisposition.SourceGap));
    }
}

/// <summary>
/// The admitted source inventory as this source tree actually stands: one record per
/// discovered file or decoded archive record, one disposition each, and the counts
/// that reconcile them. This is provenance for normalizers, not a runtime reader.
/// </summary>
public sealed record SourceManifest(
    int SchemaVersion,
    string SourceRoot,
    string InventoryPath,
    IReadOnlyList<SourceManifestRecord> Records,
    IReadOnlyList<SourceManifestFamilyCount> Families)
{
    public const int CurrentSchemaVersion = 1;

    public SourceManifest Canonicalize() => this with
    {
        Records = Records.OrderBy(record => record.FamilyId, StringComparer.Ordinal)
            .ThenBy(record => record.SourcePath, StringComparer.Ordinal)
            .ThenBy(record => record.ArchiveOrdinal ?? -1)
            .ThenBy(record => record.Id, StringComparer.Ordinal)
            .ToArray(),
        Families = Families.OrderBy(family => family.FamilyId, StringComparer.Ordinal).ToArray(),
    };

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"Only source manifest schema version {CurrentSchemaVersion} is supported.");
        }

        NormalizedImportDocument.RequireLogicalPath(SourceRoot, nameof(SourceRoot));
        NormalizedImportDocument.RequireLogicalPath(InventoryPath, nameof(InventoryPath));
        ArgumentNullException.ThrowIfNull(Records);
        ArgumentNullException.ThrowIfNull(Families);

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> identities = new(StringComparer.Ordinal);
        HashSet<string> duplicates = new(StringComparer.Ordinal);
        foreach (SourceManifestRecord record in Records)
        {
            ArgumentNullException.ThrowIfNull(record);
            record.Validate();
            if (!ids.Add(record.Id))
            {
                throw new InvalidOperationException($"The source manifest contains duplicate record id '{record.Id}'.");
            }

            // The same supplied path may legitimately carry several archive records, so
            // identity is the path plus the archive key or ordinal it was decoded from.
            // A record dispositioned as a duplicate is expected to collide with the
            // record it duplicates; every other collision is an error.
            string identity = $"{record.FamilyId}\u001f{record.SourcePath}\u001f{record.ArchiveKey ?? string.Empty}\u001f{record.ArchiveOrdinal?.ToString() ?? string.Empty}";
            if (record.Disposition == SourceRecordDisposition.Duplicate)
            {
                // A duplicate duplicates a supplied file: the same path and archive
                // coordinates, regardless of which family its inventory row sits in.
                duplicates.Add(SourceRecordIdentity.Supplied(record));
            }
            else if (!identities.Add(identity))
            {
                throw new InvalidOperationException($"The source manifest contains duplicate source record '{record.SourcePath}' in family '{record.FamilyId}'.");
            }

            if (!Families.Any(family => StringComparer.Ordinal.Equals(family.FamilyId, record.FamilyId)))
            {
                throw new InvalidOperationException($"Source record '{record.Id}' belongs to family '{record.FamilyId}', which the manifest does not count.");
            }
        }

        HashSet<string> supplied = Records
            .Where(record => record.Disposition != SourceRecordDisposition.Duplicate)
            .Select(SourceRecordIdentity.Supplied)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string duplicate in duplicates)
        {
            if (!supplied.Contains(duplicate))
            {
                throw new InvalidOperationException("The source manifest dispositions a record as a duplicate of a record it does not match.");
            }
        }

        HashSet<string> families = new(StringComparer.Ordinal);
        foreach (SourceManifestFamilyCount family in Families)
        {
            ArgumentNullException.ThrowIfNull(family);
            family.Validate();
            if (!families.Add(family.FamilyId))
            {
                throw new InvalidOperationException($"The source manifest contains duplicate family '{family.FamilyId}'.");
            }

            SourceManifestFamilyCount counted = SourceManifestFamilyCount.From(family.FamilyId, family.DocumentedDisposition, Records.Where(record => StringComparer.Ordinal.Equals(record.FamilyId, family.FamilyId)));
            if (counted != family)
            {
                throw new InvalidOperationException($"Family '{family.FamilyId}' counts do not reconcile with its records: manifest says {family.Discovered} discovered, records total {counted.Discovered}.");
            }
        }
    }
}

/// <summary>What a record duplicates: the supplied path and the archive record it came from.</summary>
file static class SourceRecordIdentity
{
    public static string Supplied(SourceManifestRecord record) =>
        $"{record.SourcePath}\u001f{record.ArchiveKey ?? string.Empty}\u001f{record.ArchiveOrdinal?.ToString() ?? string.Empty}";
}

/// <summary>Canonical JSON for <see cref="SourceManifest"/>.</summary>
public static class SourceManifestSerializer
{
    public const string ManifestRelativePath = "sources/manifest.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static byte[] Serialize(SourceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        SourceManifest canonical = manifest.Canonicalize();
        canonical.Validate();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(canonical, Options);
        return [.. bytes, (byte)'\n'];
    }

    /// <summary>
    /// Reads a manifest and rejects one that is internally inconsistent. Malformed
    /// bytes fail as <see cref="FormatException"/> so a caller's normal failure path
    /// handles them, matching how the publication manifest is read.
    /// </summary>
    public static SourceManifest Deserialize(ReadOnlySpan<byte> bytes)
    {
        SourceManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SourceManifest>(bytes, Options)
                ?? throw new FormatException("The source manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new FormatException("The source manifest is not a supported strict JSON document.", exception);
        }

        manifest.Validate();
        return manifest;
    }
}
