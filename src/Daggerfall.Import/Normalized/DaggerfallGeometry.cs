using System.Globalization;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>Whether a numeric mesh record's own bytes could be decoded.</summary>
public enum DaggerfallGeometryState
{
    /// <summary>The record decoded, and what is published about it describes its bytes.</summary>
    Read,

    /// <summary>The record could not be decoded, and the reason says why.</summary>
    Malformed,
}

/// <summary>What the corpus does with a mesh identity.</summary>
public enum DaggerfallGeometryDisposition
{
    /// <summary>A lookup reaches this record and at least one block names its number.</summary>
    Referenced,

    /// <summary>A lookup reaches this record and no published block names its number.</summary>
    Unused,

    /// <summary>An earlier record carries the same number, so a lookup reaches that one instead.</summary>
    Duplicate,
}

/// <summary>A texture archive and record a mesh's planes select.</summary>
/// <param name="Archive">The texture archive.</param>
/// <param name="Record">The record within it.</param>
public sealed record DaggerfallGeometryTexture(ushort Archive, ushort Record);

/// <summary>What a decoded mesh record states about itself, without its geometry.</summary>
/// <param name="Version">The version string the record states.</param>
/// <param name="DeclaredPoints">How many points the record declares.</param>
/// <param name="Planes">How many textured planes it carries.</param>
/// <param name="Textures">The distinct textures its planes select, in first-use order.</param>
public sealed record DaggerfallGeometryFacts(
    string Version,
    int DeclaredPoints,
    int Planes,
    IReadOnlyList<DaggerfallGeometryTexture> Textures);

/// <summary>One use site of a mesh number: a published block that names it.</summary>
/// <param name="MeshId">The mesh number, spelled as the block section spells it.</param>
/// <param name="Block">The source key of the block that names it.</param>
public sealed record DaggerfallGeometryUseSite(string MeshId, string Block);

/// <summary>One record of the numeric mesh archive.</summary>
/// <param name="Ordinal">The record's position in the archive directory, which is its stable identity.</param>
/// <param name="RecordId">The number the archive indexes the record by.</param>
/// <param name="Source">The logical path of the source that carries it.</param>
/// <param name="Offset">The byte its bytes begin at.</param>
/// <param name="ByteLength">The bytes it spans.</param>
/// <param name="State">Whether its own bytes could be decoded.</param>
/// <param name="Reason">Why they could not, empty when they could.</param>
/// <param name="Facts">What the mesh states about itself, when it could be decoded.</param>
/// <param name="DuplicateOf">The ordinal of the first record carrying this number, when an earlier one does.</param>
/// <param name="PayloadDuplicateOf">The ordinal of the first record with identical bytes, when an earlier one has them.</param>
/// <param name="UseSites">The blocks that name this record's number, in source-key order.</param>
/// <param name="Disposition">Whether a lookup reaches this record and whether anything names it.</param>
public sealed record DaggerfallGeometryRecord(
    int Ordinal,
    long RecordId,
    string Source,
    long Offset,
    int ByteLength,
    DaggerfallGeometryState State,
    string Reason,
    DaggerfallGeometryFacts? Facts,
    int? DuplicateOf,
    int? PayloadDuplicateOf,
    IReadOnlyList<string> UseSites,
    DaggerfallGeometryDisposition Disposition);

/// <summary>One mesh archive the published geometry was read from.</summary>
/// <param name="RecordId">The inventory's identity for the family.</param>
/// <param name="Path">The logical path the inventory documents it at.</param>
/// <param name="ByteLength">The bytes the archive occupies.</param>
/// <param name="DeclaredLength">The record count the archive's own header declares.</param>
/// <param name="Records">How many records the publication carries for it.</param>
public sealed record DaggerfallGeometrySource(string RecordId, string Path, long ByteLength, int DeclaredLength, int Records);

/// <summary>A mesh number a published block names that the archive cannot serve.</summary>
/// <param name="MeshId">The mesh number, spelled as the block section spells it.</param>
/// <param name="UseSites">The blocks that name it, in source-key order.</param>
/// <param name="Reason">Why the archive cannot serve it.</param>
public sealed record DaggerfallGeometryUnresolvedRecord(string MeshId, IReadOnlyList<string> UseSites, string Reason);

/// <summary>
/// The published mesh inventory: every record the supplied archive declares, what its own bytes say, and
/// which of its numbers a published block names.
/// </summary>
/// <param name="SchemaVersion">Shape version of this section.</param>
/// <param name="Sources">Every archive the records were read from.</param>
/// <param name="Records">Every record the archives carry, in directory order.</param>
/// <param name="UnresolvedUseSites">Every number a block names that no readable record answers.</param>
public sealed record DaggerfallGeometry(
    int SchemaVersion,
    IReadOnlyList<DaggerfallGeometrySource> Sources,
    IReadOnlyList<DaggerfallGeometryRecord> Records,
    IReadOnlyList<DaggerfallGeometryUnresolvedRecord> UnresolvedUseSites)
{
    /// <summary>Shape version this publication writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Checks that every claim this publication makes about the corpus holds together.</summary>
    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Geometry schema must be {CurrentSchemaVersion} but is {SchemaVersion}.");
        }

        ArgumentNullException.ThrowIfNull(Sources);
        ArgumentNullException.ThrowIfNull(Records);
        ArgumentNullException.ThrowIfNull(UnresolvedUseSites);
        if (Sources.Count == 0)
        {
            throw new InvalidOperationException("A published geometry section must name the archives it read.");
        }

        NormalizedImportDocument.ValidateUnique(Sources, source => source.Path, "geometry source");
        Dictionary<string, DaggerfallGeometrySource> byPath = Sources.ToDictionary(source => source.Path, StringComparer.Ordinal);
        Dictionary<string, int> perSource = new(StringComparer.Ordinal);
        Dictionary<long, int> firstByNumber = [];
        Dictionary<uint, int> readableByNumber = [];
        string previousSource = string.Empty;
        int previousOrdinal = -1;
        foreach (DaggerfallGeometryRecord record in Records)
        {
            if (!StringComparer.Ordinal.Equals(record.Source, previousSource))
            {
                if (perSource.ContainsKey(record.Source))
                {
                    throw new InvalidOperationException($"Published geometry source '{record.Source}' is interleaved with another source rather than grouped.");
                }

                previousSource = record.Source;
                previousOrdinal = -1;
                firstByNumber.Clear();
            }

            // A record's identity is its own archive's directory ordinal: the archive is not sorted by
            // number, so the ordinal is the only stable order the file states.
            if (record.Ordinal != previousOrdinal + 1)
            {
                throw new InvalidOperationException($"Published mesh ordinal {record.Ordinal} follows {previousOrdinal} where the section is ordered by ordinal.");
            }

            previousOrdinal = record.Ordinal;
            record.Validate();
            if (!byPath.TryGetValue(record.Source, out DaggerfallGeometrySource? source))
            {
                throw new InvalidOperationException($"Published mesh record {record.Ordinal} names source '{record.Source}', which the section does not carry.");
            }

            // The duplicate columns are re-derived from the records themselves rather than trusted: the
            // first record carrying a number is the one a lookup reaches, and a record whose bytes repeat
            // an earlier record's has to say so.
            int? expectedNumber = firstByNumber.TryGetValue(record.RecordId, out int firstNumber) ? firstNumber : null;
            if (record.DuplicateOf != expectedNumber)
            {
                throw new InvalidOperationException($"Published mesh record {record.Ordinal} states it duplicates {record.DuplicateOf?.ToString(CultureInfo.InvariantCulture) ?? "nothing"} where number {record.RecordId} first appears at {expectedNumber?.ToString(CultureInfo.InvariantCulture) ?? "no earlier record"}.");
            }

            // Which records' bytes are identical is the reader's fact and is not recoverable from these
            // numbers, so what is checked here is what the claim entails: the record it names is earlier,
            // and identical bytes span the same length.
            if (record.PayloadDuplicateOf is int payload
                && (payload >= record.Ordinal || payload < 0 || Records.First(value => value.Ordinal == payload).ByteLength != record.ByteLength))
            {
                throw new InvalidOperationException($"Published mesh record {record.Ordinal} states its bytes repeat record {payload}, which is not an earlier record of the same length.");
            }

            firstByNumber.TryAdd(record.RecordId, record.Ordinal);
            // Only the record a lookup reaches can answer a number: a later record carrying the same number
            // is unreachable, so its readability says nothing about whether the number is served.
            if (record.State == DaggerfallGeometryState.Read && record.DuplicateOf is null && record.RecordId <= uint.MaxValue)
            {
                readableByNumber.TryAdd((uint)record.RecordId, record.Ordinal);
            }

            perSource[record.Source] = perSource.GetValueOrDefault(record.Source) + 1;
        }

        foreach (DaggerfallGeometrySource source in Sources)
        {
            int published = perSource.GetValueOrDefault(source.Path);
            if (source.DeclaredLength != published || source.Records != published)
            {
                throw new InvalidOperationException($"Geometry source '{source.Path}' declares {source.DeclaredLength} records and publishes {source.Records}, where the section carries {published}.");
            }
        }

        // A number a block names is unresolved exactly when no readable record answers it, so a section
        // that listed one the archive serves, or omitted one it cannot, would be reporting the wrong
        // closure set.
        // The rule is about numbers, and a block spells a number as the dungeon source stores it — five
        // characters, so mesh 9004 appears as "09004". Comparing the spellings instead of the numbers would
        // let a section report a number unresolved in the spelling the corpus actually uses while the
        // record that answers it sits in the same section.
        HashSet<ulong> reported = [];
        foreach (DaggerfallGeometryUnresolvedRecord unresolved in UnresolvedUseSites)
        {
            if (!ulong.TryParse(unresolved.MeshId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong number))
            {
                throw new InvalidOperationException($"Published mesh number '{unresolved.MeshId}' is not a mesh number.");
            }

            if (!reported.Add(number))
            {
                throw new InvalidOperationException($"Published mesh number '{unresolved.MeshId}' is reported unresolved twice.");
            }

            if (number <= uint.MaxValue && readableByNumber.TryGetValue((uint)number, out int ordinal))
            {
                throw new InvalidOperationException($"Published mesh number '{unresolved.MeshId}' is reported unresolved where record {ordinal} answers it.");
            }

            if (string.IsNullOrWhiteSpace(unresolved.Reason) || unresolved.UseSites.Count == 0)
            {
                throw new InvalidOperationException($"Published mesh number '{unresolved.MeshId}' is reported unresolved with no reason or no use site.");
            }

            ValidateUseSites(unresolved.UseSites, unresolved.MeshId);
        }
    }

    /// <summary>Checks one number's use sites, which are block source keys in order.</summary>
    internal static void ValidateUseSitesFor(IReadOnlyList<string> useSites, string meshId) => ValidateUseSites(useSites, meshId);

    private static void ValidateUseSites(IReadOnlyList<string> useSites, string meshId)
    {
        NormalizedImportDocument.ValidateUnique(useSites, block => block, $"use site of mesh '{meshId}'");
        for (int index = 0; index < useSites.Count; index++)
        {
            NormalizedImportDocument.RequireLogicalId(useSites[index], nameof(useSites));
            if (index != 0 && StringComparer.Ordinal.Compare(useSites[index - 1], useSites[index]) >= 0)
            {
                throw new InvalidOperationException($"Mesh '{meshId}' lists its use sites out of order at '{useSites[index]}'.");
            }
        }
    }
}

/// <summary>One record of the mesh inventory checked against what its own numbers claim.</summary>
internal static class DaggerfallGeometryValidation
{
    /// <summary>Checks one record's own claims.</summary>
    internal static void Validate(this DaggerfallGeometryRecord record)
    {
        NormalizedImportDocument.RequireLogicalPath(record.Source, nameof(record.Source));
        // A record with no bytes is a shape the archive can state: nothing can be decoded from it, and it
        // is published as malformed with that reason rather than refusing the inventory of every other
        // record the file carries.
        if (record.Ordinal < 0 || record.Offset < 0 || record.ByteLength < 0)
        {
            throw new InvalidOperationException($"Published mesh record {record.Ordinal} carries offset {record.Offset} and length {record.ByteLength}, which cannot both describe a record.");
        }

        if (record.RecordId is < 0 or > uint.MaxValue)
        {
            throw new InvalidOperationException($"Published mesh record {record.Ordinal} carries number {record.RecordId}, which the source's four-byte number cannot state.");
        }

        if (record.State == DaggerfallGeometryState.Read)
        {
            if (record.Reason.Length != 0 || record.Facts is null)
            {
                throw new InvalidOperationException($"Published mesh record {record.Ordinal} is readable and states {(record.Reason.Length == 0 ? "no facts" : "a reason it is not")}.");
            }
        }
        else if (record.State == DaggerfallGeometryState.Malformed)
        {
            if (string.IsNullOrWhiteSpace(record.Reason) || record.Facts is not null)
            {
                throw new InvalidOperationException($"Published mesh record {record.Ordinal} is malformed with {(record.Facts is null ? "no reason" : "facts it could not read")}.");
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(record), record.State, $"Published mesh record {record.Ordinal} carries a state the contract does not declare.");
        }

        record.Facts?.Validate(record.Ordinal);

        // What a lookup reaches decides the disposition, and what names it decides the rest: a later record
        // carrying a number an earlier one already answers is unreachable by that number, so its use sites
        // belong to the record a lookup actually finds.
        if (record.DuplicateOf is not null && record.UseSites.Count != 0)
        {
            throw new InvalidOperationException($"Published mesh record {record.Ordinal} reuses a number an earlier record answers and still carries {record.UseSites.Count} use sites, which belong to the record a lookup reaches.");
        }

        DaggerfallGeometryDisposition expected = record.DuplicateOf is not null
            ? DaggerfallGeometryDisposition.Duplicate
            : record.UseSites.Count != 0 ? DaggerfallGeometryDisposition.Referenced : DaggerfallGeometryDisposition.Unused;
        if (record.Disposition != expected)
        {
            throw new InvalidOperationException($"Published mesh record {record.Ordinal} states the disposition {record.Disposition} where its duplicate column and {record.UseSites.Count} use sites make it {expected}.");
        }

        if (record.DuplicateOf is int duplicate && (duplicate >= record.Ordinal || duplicate < 0))
        {
            throw new InvalidOperationException($"Published mesh record {record.Ordinal} states it duplicates record {duplicate}, which is not an earlier ordinal.");
        }

        if (record.PayloadDuplicateOf is int payload && (payload >= record.Ordinal || payload < 0))
        {
            throw new InvalidOperationException($"Published mesh record {record.Ordinal} states its bytes repeat record {payload}, which is not an earlier ordinal.");
        }

        if (record.UseSites.Count != 0)
        {
            DaggerfallGeometry.ValidateUseSitesFor(record.UseSites, record.RecordId.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Checks what a decoded mesh states about itself.</summary>
    internal static void Validate(this DaggerfallGeometryFacts facts, int ordinal)
    {
        if (!Arch3dDecoder.Versions.Contains(facts.Version, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Published mesh record {ordinal} states the version '{facts.Version}', which the format does not declare.");
        }

        if (facts.DeclaredPoints < 0 || facts.Planes < 0)
        {
            throw new InvalidOperationException($"Published mesh record {ordinal} states negative counts.");
        }

        if (facts.Textures.Count > facts.Planes)
        {
            throw new InvalidOperationException($"Published mesh record {ordinal} names {facts.Textures.Count} distinct textures of its {facts.Planes} planes.");
        }

        NormalizedImportDocument.ValidateUnique(facts.Textures, texture => $"{texture.Archive}:{texture.Record}", $"texture of mesh record {ordinal}");
    }
}

/// <summary>Builds the published mesh inventory from the supplied archive, the inventory and the blocks.</summary>
/// <remarks>
/// The use sites come from the published block inventory rather than from this reader, because which blocks
/// name a mesh is a fact about the blocks: the caller reads them from the section that publishes them and
/// this builder attributes each number to the record a lookup would reach.
/// </remarks>
public static class DaggerfallGeometryBuilder
{
    /// <summary>The inventory family the mesh archive is documented under.</summary>
    public const string GeometryFamily = "CNT-006";

    /// <summary>Reads the supplied archive and publishes every record it declares.</summary>
    public static DaggerfallGeometry Build(
        byte[] bytes,
        string label,
        IReadOnlyList<SourceInventoryRow> inventory,
        IReadOnlyList<DaggerfallGeometryUseSite> useSites)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(useSites);
        SourceInventoryRow family = SourceInventoryRow.RequireFamily(inventory, GeometryFamily);
        if (!StringComparer.Ordinal.Equals(family.PathOrPattern, label))
        {
            throw new InvalidOperationException($"The mesh archive was read as '{label}', but the documented inventory places {GeometryFamily} at '{family.PathOrPattern}'.");
        }

        Arch3dMeshInventory catalog = Arch3dInventoryReader.Read(bytes, label);

        // The blocks spell a mesh number the way the dungeon source stores it — five characters of text,
        // so mesh 9004 appears as "09004" — while the mesh archive stores the number itself. Joining the
        // two by their spelling would report every number below ten thousand as missing from an archive
        // that carries it, so the block's spelling is read as the number it states and the spelling is
        // kept beside it for the answer a block consumer needs.
        Dictionary<uint, SortedSet<string>> byNumber = [];
        Dictionary<uint, string> spellingByNumber = [];
        Dictionary<ulong, SortedSet<string>> beyondSpace = [];
        Dictionary<ulong, string> beyondSpelling = [];
        foreach (DaggerfallGeometryUseSite useSite in useSites)
        {
            if (!uint.TryParse(useSite.MeshId, NumberStyles.None, CultureInfo.InvariantCulture, out uint number))
            {
                // A block can name a number the archive's own four-byte space cannot hold. No record carries
                // it, so it is an unanswerable use site like any other rather than a reason to abandon the
                // inventory of the ten thousand records beside it.
                if (!ulong.TryParse(useSite.MeshId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong beyond))
                {
                    throw new InvalidOperationException($"The block '{useSite.Block}' names the mesh '{useSite.MeshId}', which is not a mesh number.");
                }

                if (!beyondSpace.TryGetValue(beyond, out SortedSet<string>? outside))
                {
                    beyondSpace[beyond] = outside = new SortedSet<string>(StringComparer.Ordinal);
                    beyondSpelling[beyond] = useSite.MeshId;
                }

                outside.Add(useSite.Block);
                continue;
            }

            if (!byNumber.TryGetValue(number, out SortedSet<string>? blocks))
            {
                byNumber[number] = blocks = new SortedSet<string>(StringComparer.Ordinal);
                spellingByNumber[number] = useSite.MeshId;
            }

            blocks.Add(useSite.Block);
        }

        // A number a lookup reaches belongs to the first record carrying it: a later record with the same
        // number is unreachable by that number, so the blocks that name it are attributed to the first.
        Dictionary<uint, int> reachable = [];
        foreach (Arch3dMeshRecord record in catalog.Records.Where(static record => record.DuplicateOf is null))
        {
            reachable.TryAdd(record.RecordId, record.Ordinal);
        }

        List<DaggerfallGeometryRecord> records = [];
        foreach (Arch3dMeshRecord record in catalog.Records)
        {
            IReadOnlyList<string> sites = record.DuplicateOf is null && byNumber.TryGetValue(record.RecordId, out SortedSet<string>? blocks)
                ? [.. blocks]
                : [];
            records.Add(new DaggerfallGeometryRecord(
                record.Ordinal,
                record.RecordId,
                label,
                record.Offset,
                record.ByteLength,
                (DaggerfallGeometryState)record.State,
                record.Reason,
                Publish(record.Facts),
                record.DuplicateOf,
                record.PayloadDuplicateOf,
                sites,
                record.DuplicateOf is not null
                    ? DaggerfallGeometryDisposition.Duplicate
                    : sites.Count != 0 ? DaggerfallGeometryDisposition.Referenced : DaggerfallGeometryDisposition.Unused));
        }

        // A number a block names that no readable record answers is the closure gap this inventory exists
        // to make visible: either the archive carries no such number, or the record carrying it could not
        // be decoded, and the donor's lookup would answer with the first record either way.
        Dictionary<uint, Arch3dMeshRecord> firstByNumber = [];
        foreach (Arch3dMeshRecord record in catalog.Records)
        {
            firstByNumber.TryAdd(record.RecordId, record);
        }

        List<DaggerfallGeometryUnresolvedRecord> unresolved = [];
        foreach ((ulong beyond, SortedSet<string> outside) in beyondSpace.OrderBy(pair => pair.Key))
        {
            unresolved.Add(new DaggerfallGeometryUnresolvedRecord(
                beyondSpelling[beyond],
                [.. outside],
                $"the archive carries no record numbered {beyond}"));
        }

        foreach ((uint number, SortedSet<string> blocks) in byNumber.OrderBy(pair => pair.Key))
        {
            if (reachable.ContainsKey(number) && firstByNumber[number].State == Arch3dRecordState.Read)
            {
                continue;
            }

            unresolved.Add(new DaggerfallGeometryUnresolvedRecord(
                spellingByNumber[number],
                [.. blocks],
                firstByNumber.TryGetValue(number, out Arch3dMeshRecord? record)
                    ? $"record {record.Ordinal} carries number {number} and could not be decoded: {record.Reason}"
                    : $"the archive carries no record numbered {number}"));
        }

        // Published in number order so the list reads the same however the use sites arrived.
        unresolved.Sort((left, right) => ulong.Parse(left.MeshId, CultureInfo.InvariantCulture).CompareTo(ulong.Parse(right.MeshId, CultureInfo.InvariantCulture)));

        DaggerfallGeometry published = new(
            DaggerfallGeometry.CurrentSchemaVersion,
            [new DaggerfallGeometrySource(family.Id, label, bytes.LongLength, catalog.DeclaredRecords, catalog.Records.Count)],
            records,
            unresolved);
        published.Validate();
        return published;
    }

    private static DaggerfallGeometryFacts? Publish(Arch3dMeshFacts? facts) =>
        facts is null ? null : new DaggerfallGeometryFacts(
            facts.Version,
            facts.DeclaredPoints,
            facts.Planes,
            [.. facts.Textures.Select(texture => new DaggerfallGeometryTexture(texture.Archive, texture.Record))]);
}
