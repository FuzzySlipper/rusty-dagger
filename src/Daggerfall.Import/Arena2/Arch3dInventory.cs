using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Arena2;

/// <summary>Whether a numeric mesh record's own bytes could be decoded.</summary>
public enum Arch3dRecordState
{
    /// <summary>The record decoded, and what is published about it describes its bytes.</summary>
    Read,

    /// <summary>The record could not be decoded, and the reason says why.</summary>
    Malformed,
}

/// <summary>
/// What the corpus does with a mesh identity: whether a lookup reaches this record, and whether anything
/// in the product names it.
/// </summary>
public enum Arch3dUsageDisposition
{
    /// <summary>A lookup reaches this record and at least one block names its id.</summary>
    Referenced,

    /// <summary>A lookup reaches this record and no published block names its id.</summary>
    Unused,

    /// <summary>
    /// An earlier record carries the same id, so a lookup reaches that one instead of this record: the
    /// donor's numeric lookup answers with the first directory-order match.
    /// </summary>
    Duplicate,
}

/// <summary>A texture archive and record a mesh's planes select.</summary>
/// <param name="Archive">The texture archive.</param>
/// <param name="Record">The record within it.</param>
public sealed record Arch3dTextureReference(ushort Archive, ushort Record);

/// <summary>What a decoded mesh record states about itself, without its geometry.</summary>
/// <param name="Version">The version string the record states.</param>
/// <param name="DeclaredPoints">How many points the record declares.</param>
/// <param name="Planes">How many textured planes it carries.</param>
/// <param name="Textures">The distinct textures its planes select, in first-use order.</param>
public sealed record Arch3dMeshFacts(
    string Version,
    int DeclaredPoints,
    int Planes,
    IReadOnlyList<Arch3dTextureReference> Textures);

/// <summary>One record of the numeric mesh archive, identified by its own number rather than a name.</summary>
/// <param name="Ordinal">The record's position in the archive directory, which is its stable identity.</param>
/// <param name="RecordId">The number the archive indexes the record by.</param>
/// <param name="Offset">The byte its bytes begin at.</param>
/// <param name="ByteLength">The bytes it spans.</param>
/// <param name="State">Whether its own bytes could be decoded.</param>
/// <param name="Reason">Why they could not, empty when they could.</param>
/// <param name="Facts">What the mesh states about itself, when it could be decoded.</param>
/// <param name="DuplicateOf">
/// The ordinal of the first record carrying this number, when an earlier one does. The archive's own
/// directory is allowed to reuse a number, and the donor's numeric lookup answers with the first match,
/// so a later record with the same number is reachable only by its ordinal.
/// </param>
/// <param name="PayloadDuplicateOf">
/// The ordinal of the first record whose bytes are identical, when an earlier one's are. A mesh stored
/// twice under one number, or under two, is the same geometry either way, and which identities share it
/// is what makes a closure decision possible.
/// </param>
public sealed record Arch3dMeshRecord(
    int Ordinal,
    uint RecordId,
    long Offset,
    int ByteLength,
    Arch3dRecordState State,
    string Reason,
    Arch3dMeshFacts? Facts,
    int? DuplicateOf,
    int? PayloadDuplicateOf);

/// <summary>Every record a numeric mesh archive declares, in the order its directory lists them.</summary>
/// <param name="Source">Logical source identity supplied to the reader.</param>
/// <param name="DeclaredRecords">The count the archive's own header declares.</param>
/// <param name="Records">The records, in directory order.</param>
public sealed record Arch3dMeshInventory(string Source, int DeclaredRecords, IReadOnlyList<Arch3dMeshRecord> Records);

/// <summary>
/// Enumerates a numeric mesh archive: every record it carries, what its own bytes say, which numbers it
/// reuses, and which of its bytes appear more than once.
/// </summary>
/// <remarks>
/// <para>
/// The archive is the numeric BSA variant, whose directory entries carry a number and a length instead of
/// a name. The supplied corpus is not sorted by number — it descends 1840 times — so directory order is
/// the only stable order the file states, and the ordinal is what identifies a record here. A record's
/// number is retained whatever it is, including the ten numbers the corpus carries more than once and the
/// one record numbered zero.
/// </para>
/// <para>
/// What each record states about itself comes from the existing mesh decoder, so the geometry rules stay
/// in one place: this publishes the version, the counts and the textures, never the points. A record
/// whose bytes cannot be decoded is published as malformed with its reason rather than dropped, because a
/// block that names it still has to be answerable, and an identity nothing references is still an
/// identity the corpus carries.
/// </para>
/// </remarks>
public static class Arch3dInventoryReader
{
    /// <summary>Where the documented inventory places the mesh archive.</summary>
    public const string FileName = "ARCH3D.BSA";

    /// <summary>Reads every record the supplied archive declares.</summary>
    public static Arch3dMeshInventory Read(byte[] bytes, string label)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        BsaArchive archive = BsaArchive.Parse(bytes, label);

        // A named archive's records carry no number to look a mesh up by, so pointing this reader at one
        // would publish an inventory whose identities are names the mesh format does not have.
        BsaRecord? named = archive.Records.FirstOrDefault(static record => record.Name is not null);
        if (named is not null)
        {
            throw new Arena2FormatException(label, named.Offset, $"record {named.Ordinal} is named '{named.Name}', but a mesh archive is the numeric variant whose records carry numbers");
        }

        List<Arch3dMeshRecord> records = new(archive.Records.Count);
        Dictionary<ContentDigest, int> firstByPayload = [];
        foreach (BsaRecord record in archive.Records)
        {
            uint recordId = record.NumericId!.Value;

            // Which record a number reaches is the archive's own answer, so it is asked rather than
            // re-derived: the container already resolves a lookup to the first directory-order match.
            int? duplicateOf = archive.TryGetByNumericId(recordId, out BsaRecord? first) && first!.Ordinal != record.Ordinal
                ? first.Ordinal
                : null;
            ContentDigest payload = ContentDigest.Compute(archive.GetPayload(record).Span);
            int? payloadDuplicateOf = firstByPayload.TryGetValue(payload, out int firstPayload) ? firstPayload : null;
            firstByPayload.TryAdd(payload, record.Ordinal);

            Arch3dMeshFacts? facts = null;
            string reason = string.Empty;
            Arch3dRecordState state = Arch3dRecordState.Malformed;
            try
            {
                Arch3dMesh mesh = Arch3dDecoder.Decode(archive.GetPayload(record).Span, label, recordId);
                HashSet<(ushort Archive, ushort Record)> textures = [];
                List<Arch3dTextureReference> references = [];
                foreach (Arch3dPlane plane in mesh.Planes)
                {
                    if (textures.Add((plane.TextureArchive, plane.TextureRecord)))
                    {
                        references.Add(new Arch3dTextureReference(plane.TextureArchive, plane.TextureRecord));
                    }
                }

                facts = new Arch3dMeshFacts(mesh.Version, mesh.DeclaredPointCount, mesh.Planes.Count, references);
                state = Arch3dRecordState.Read;
            }
            catch (Arena2FormatException error)
            {
                reason = error.Message;
            }

            records.Add(new Arch3dMeshRecord(
                record.Ordinal,
                recordId,
                record.Offset,
                record.Length,
                state,
                reason,
                facts,
                duplicateOf,
                payloadDuplicateOf));
        }

        return new Arch3dMeshInventory(label, archive.Records.Count, records);
    }
}
