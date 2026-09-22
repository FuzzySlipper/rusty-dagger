using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalization;

/// <summary>
/// What one whole publication pass produced: every family's artifacts, the refusals that keep a
/// missing canvas visible, and the families nothing here can read at all.
/// </summary>
/// <param name="Artifacts">Every published canvas, ordered by media identity.</param>
/// <param name="Refusals">Every canvas that could not be published, with its identity and the reason.</param>
/// <param name="UnreadableFamilies">The supplied formats whose canvases are absent here, with the reason.</param>
/// <param name="UnpublishableFiles">The supplied files those entries account for, by file name, with the reason.</param>
public sealed record CharacterMediaPassResult(
    IReadOnlyList<CharacterMediaArtifact> Artifacts,
    IReadOnlyList<string> Refusals,
    IReadOnlyList<CharacterMediaUnreadableFamily> UnreadableFamilies,
    IReadOnlyDictionary<string, string> UnpublishableFiles)
{
    /// <summary>The media identities this pass actually published.</summary>
    public IReadOnlySet<string> PublishedMediaIds { get; } =
        Artifacts.Select(artifact => artifact.MediaId).ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// Publishes the character and face canvases every reference names, in one pass over the supplied
/// corpus, and states the families whose canvases this repository cannot read.
/// </summary>
/// <remarks>
/// The wire from the enumerated references to admitted content: <see cref="CharacterMediaPublication"/>
/// emits one family, this runs it over every family the references carry, sorts the canvases a consumer
/// binds away from the ones no consumer resolves yet, and accounts for the formats nothing here reads.
/// A canvas whose file the corpus does not carry is refused with both named, so a gap stays a stated gap
/// rather than an artifact that quietly does not exist.
/// </remarks>
public static class CharacterMediaPublisher
{
    /// <summary>The relative path the generated character-media index is written to.</summary>
    public const string IndexRelativePath = "media/character/character-media-inventory.json";

    /// <summary>The index's shape version, which a consumer states before it reads one.</summary>
    public const int IndexSchemaVersion = 1;

    /// <summary>The index's generator identity, so an index that drifted from its producer is visible.</summary>
    public const string IndexGenerator = "daggerfall-import-tool character-presentation";

    /// <summary>The palette source of a canvas painted in the palette its own container carries.</summary>
    public const string EmbeddedPaletteSource = "embedded-in-source-file";

    /// <summary>The palette source of a canvas painted in a supplied palette file.</summary>
    public const string SuppliedPaletteSource = "supplied-palette-file";

    /// <summary>The documented family id the character-media inventory rows cite.</summary>
    public const string CharacterMediaFamilyId = "CNT-021";

    /// <summary>
    /// Checks the documented character-media inventory against the supplied corpus, in both directions.
    /// </summary>
    /// <remarks>
    /// The inventory is the independent record of what this family is supposed to hold, so it is what makes
    /// the corpus checkable rather than self-describing: a documented file the corpus lacks is a source gap
    /// worth refusing over, while a supplied file with no row is content this publication would emit without
    /// a record, which is reported and kept. The documented paths are cited root-relative
    /// (<c>local/arena2/BODY00I0.IMG</c>) while the publication sees bare names, so membership is by file
    /// name - the same rule the inventory's own family enumeration uses.
    /// </remarks>
    /// <param name="inventoryCsv">The documented inventory, as its published bytes.</param>
    /// <param name="supplied">The supplied corpus, by path or file name.</param>
    /// <returns>The supplied files no documented row carries, ordered by name.</returns>
    /// <exception cref="InvalidOperationException">
    /// The inventory documents no character-media file, or documents one the corpus does not supply.
    /// </exception>
    public static IReadOnlyList<string> ReconcileDocumentedInventory(
        ReadOnlySpan<byte> inventoryCsv,
        IEnumerable<string> supplied)
    {
        ArgumentNullException.ThrowIfNull(supplied);
        IReadOnlyList<SourceInventoryRow> rows = SourceManifestBuilder.ReadInventory(inventoryCsv);
        HashSet<string> documented = [.. rows
            .Where(row => row.RowType == "file" && StringComparer.Ordinal.Equals(row.FamilyId, CharacterMediaFamilyId))
            .Select(row => System.IO.Path.GetFileName(row.PathOrPattern))
            .Where(name => name is { Length: > 0 })
            .Select(name => name!)];
        if (documented.Count == 0)
        {
            throw new InvalidOperationException(
                $"The inventory documents no {CharacterMediaFamilyId} character media files, so it cannot be the record this publication is reconciled against.");
        }

        string[] names = [.. supplied
            .Select(System.IO.Path.GetFileName)
            .Where(name => name is { Length: > 0 })
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        string[] missing = [.. documented.Where(name => !names.Contains(name, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)];
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                $"{missing.Length} documented character media file(s) are not in the supplied corpus, so the publication would account for a family the source does not carry: {string.Join(", ", missing)}.");
        }

        return [.. names.Where(name => !documented.Contains(name, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The donor class that reads each format nothing here reads.</summary>
    private static readonly Dictionary<string, string> DonorReaders = new(StringComparer.Ordinal)
    {
        ["CEL"] = "Assets/Scripts/API/FlcFile.cs",
        ["BSS"] = "Assets/Scripts/API/BssFile.cs",
    };

    /// <summary>Publishes every canvas of every family an enumerated reference set carries.</summary>
    /// <param name="set">The enumerated canvas references, which are the whole publication's subjects.</param>
    /// <param name="sources">The supplied corpus, by file name.</param>
    /// <param name="palettes">The supplied palettes by file name.</param>
    /// <param name="inventory">
    /// The supplied files, which is how a format nothing reads is accounted for rather than omitted.
    /// </param>
    public static CharacterMediaPassResult PublishAll(
        CharacterMediaReferenceSet set,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> sources,
        IReadOnlyDictionary<string, Arena2Palette> palettes,
        CharacterMediaInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(palettes);
        ArgumentNullException.ThrowIfNull(inventory);

        // Two references with one identity would emit and index the same artifact twice, which a consumer
        // could not tell apart; the enumeration derives unique identities, so this is a guard against a
        // caller that hand-builds a set rather than a case the derivation produces.
        string[] duplicateIdentities = [.. set.Canvases
            .GroupBy(canvas => canvas.MediaId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)];
        if (duplicateIdentities.Length != 0)
        {
            throw new InvalidOperationException(
                $"{duplicateIdentities.Length} character media identity(ies) are claimed by more than one canvas reference, so one artifact would be published under one name twice: {string.Join(", ", duplicateIdentities)}.");
        }

        List<CharacterMediaArtifact> artifacts = [];
        List<string> refusals = [];
        HashSet<string> refusedSources = new(StringComparer.OrdinalIgnoreCase);
        // One family at a time, over the families the references actually carry: a documented family with
        // no reference has no canvas to publish, and publishing "the documented families" instead would
        // be a second list of subjects that could drift from the references.
        foreach (string family in set.Canvases.Select(canvas => canvas.Family).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            CharacterMediaPublicationResult published = CharacterMediaPublication.Publish(family, set.Canvases, sources, palettes);
            artifacts.AddRange(published.Artifacts);
            refusals.AddRange(published.Refusals);
            foreach (CharacterCanvasReference canvas in set.Canvases.Where(canvas => string.Equals(canvas.Family, family, StringComparison.OrdinalIgnoreCase)))
            {
                // Which files produced no artifact is what the unreadable families are derived from, so
                // it is taken from the identities the pass published rather than assumed from a shape.
                if (!published.PublishedMediaIds.Contains(canvas.MediaId))
                {
                    refusedSources.Add(System.IO.Path.GetFileName(canvas.Path));
                }
            }
        }

        artifacts.Sort((left, right) => string.CompareOrdinal(left.MediaId, right.MediaId));
        refusals.Sort(StringComparer.Ordinal);

        // A reference a consumer binds has to resolve to an artifact: a bound canvas the pass refused
        // would let the pack claim a live consumer draws art that was never emitted, which is the one
        // failure the binding is supposed to make impossible rather than hide.
        string[] unpublished = [.. set.Canvases
            .Where(canvas => canvas.Binding == MediaBinding.Admitted)
            .Select(canvas => canvas.MediaId)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !artifacts.Any(artifact => string.Equals(artifact.MediaId, id, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)];
        if (unpublished.Length != 0)
        {
            throw new InvalidOperationException(
                $"A published consumer binds {unpublished.Length} character canvas(es) this pass could not publish, so the reference would resolve to nothing: {string.Join(", ", unpublished)}. Reasons: {string.Join(" | ", refusals)}");
        }

        return new(artifacts, refusals, UnreadableFamilies(inventory, refusedSources), UnpublishableFiles(inventory, refusedSources, refusals));
    }

    /// <summary>
    /// Every supplied file whose canvases produced no artifact, with the reason the reader gave.
    /// </summary>
    /// <remarks>
    /// This is the per-file half of the same fact the unreadable families state in aggregate, and the pack's
    /// file records need it: a format this repository reads but cannot take pixels from - a BSS frame, a
    /// grid cell - reads successfully and refuses later, at decode, so a record that only asked whether the
    /// file was read would call it readable and unused while its canvases do not exist.
    /// </remarks>
    /// <param name="inventory">The supplied files.</param>
    /// <param name="refusedSources">The supplied files whose canvases produced no artifact.</param>
    /// <param name="refusals">The refusal each canvas gave, which is where the reason comes from.</param>
    private static IReadOnlyDictionary<string, string> UnpublishableFiles(
        CharacterMediaInventory inventory,
        IReadOnlySet<string> refusedSources,
        IReadOnlyList<string> refusals)
    {
        Dictionary<string, string> reasons = new(StringComparer.Ordinal);
        foreach (CharacterMediaRecord file in inventory.Files)
        {
            string name = System.IO.Path.GetFileName(file.Path);
            if (!refusedSources.Contains(name)) continue;
            // The reader's own refusal is the reason, and it names the file and the gap in it: the first
            // mention is enough, because the rest are its siblings from the same file.
            reasons[name] = refusals.FirstOrDefault(refusal => refusal.Contains(name, StringComparison.Ordinal))
                ?? $"The file supplies {file.CanvasCount} canvas(es) and none of them could be published here.";
        }

        return reasons;
    }

    /// <summary>Why a supplied file's canvases produced no artifact.</summary>
    private enum UnpublishableCause
    {
        /// <summary>No reader here opened the file at all.</summary>
        NoReader,

        /// <summary>The container read and refused, or its pixels have no decoder: the gap is past the reader.</summary>
        NoPixelDecoder,

    }

    /// <summary>
    /// The families and shapes whose canvases this repository cannot publish, derived from the canvases
    /// the pass refused and from what the supplied files were read as.
    /// </summary>
    /// <remarks>
    /// The subject is the pass's own result rather than a second reading of it: a supplied file is named
    /// here when it carries canvases and none of them produced an artifact, and the reason is the refusal
    /// the readers gave. Entries are keyed by the family and the cause, so a file no reader could open is
    /// never described with the reason that belongs to a container that read and refused - the two are
    /// different gaps, and one of them would be a false claim about a family this run publishes from.
    /// </remarks>
    /// <param name="inventory">The supplied files, so a file no reader read is still accounted for.</param>
    /// <param name="refusedSources">The supplied files whose canvases produced no artifact.</param>
    private static IReadOnlyList<CharacterMediaUnreadableFamily> UnreadableFamilies(
        CharacterMediaInventory inventory,
        IReadOnlySet<string> refusedSources)
    {
        Dictionary<(string Family, UnpublishableCause Cause), List<string>> grouped = [];
        foreach (CharacterMediaRecord file in inventory.Files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            string name = System.IO.Path.GetFileName(file.Path);
            // A canvas the pass refused takes its file out of the published set; a file whose format no
            // reader read was never a candidate. Both are canvases that are not here.
            if (!refusedSources.Contains(name) && file.Decode != Arena2CanvasKind.Unread) continue;
            UnpublishableCause cause = file.Decode switch
            {
                Arena2CanvasKind.Unread => UnpublishableCause.NoReader,
                _ => UnpublishableCause.NoPixelDecoder,
            };
            (string Family, UnpublishableCause Cause) key = (file.Family, cause);
            if (!grouped.TryGetValue(key, out List<string>? files)) grouped[key] = files = [];
            files.Add(name);
        }

        List<CharacterMediaUnreadableFamily> unreadable = [];
        foreach (((string family, UnpublishableCause cause), List<string> files) in grouped
            .OrderBy(entry => entry.Key.Family, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key.Cause))
        {
            string reader = DonorReaders.GetValueOrDefault(family, string.Empty);
            unreadable.Add(cause switch
            {
                UnpublishableCause.NoPixelDecoder => new CharacterMediaUnreadableFamily(
                    family,
                    Kind(family, files, inventory),
                    files,
                    $"these supplied file(s) read as a {Kind(family, files, inventory)} and none of their canvases could be decoded into pixels here, so the gap is a missing pixel decoder rather than a missing reader; the donor's own reader is where that behaviour lives",
                    reader),
                _ => NoReaderEntry(family, files, inventory, reader),
            });
        }

        return unreadable;
    }

    /// <summary>
    /// The entry for files no reader here opened, with a reason that is true of the family it names.
    /// </summary>
    /// <remarks>
    /// A file no reader opens in a family this run publishes from is a file-level gap, not a format nothing
    /// reads: saying the latter would be false in the same run that reads its siblings, which is the kind of
    /// claim this publication exists to avoid.
    /// </remarks>
    private static CharacterMediaUnreadableFamily NoReaderEntry(
        string family,
        IReadOnlyList<string> files,
        CharacterMediaInventory inventory,
        string reader)
    {
        bool familyReads = inventory.Files.Any(file =>
            string.Equals(file.Family, family, StringComparison.Ordinal) && file.Decode != Arena2CanvasKind.Unread);
        string kind = Kind(family, files, inventory);
        string reason = familyReads
            ? $"no reader here opened these supplied file(s): the {family} family's other files read, so this is a file-level gap - the file is not the shape this repository's reader accepts - rather than a format nothing reads"
            : reader.Length == 0
                ? $"nothing in this repository reads the {family} format, and no donor reader is recorded for it, so its files carry no canvas here"
                : $"nothing in this repository reads the {family} format: the donor reads it with {reader}, and without it the file's canvases have no pixels here rather than an approximation";
        return new CharacterMediaUnreadableFamily(family, kind, files, reason, familyReads ? string.Empty : reader);
    }

    /// <summary>What the files of one entry are, named by the kind the reader established.</summary>
    private static string Kind(string family, IReadOnlyList<string> files, CharacterMediaInventory inventory)
    {
        Arena2CanvasKind? kind = inventory.Files
            .Where(file => files.Contains(System.IO.Path.GetFileName(file.Path), StringComparer.OrdinalIgnoreCase))
            .Select(file => (Arena2CanvasKind?)file.Decode)
            .FirstOrDefault();
        return kind switch
        {
            Arena2CanvasKind.RciGrid => "fixed-cell RCI grid",
            Arena2CanvasKind.BssFrames => "BSS sprite container",
            Arena2CanvasKind.Unread => $"{family} file no reader here opens",
            null => $"{family} container",
            _ => $"{family} container read as {kind}",
        };
    }

    /// <summary>
    /// The generated index as its published bytes: its shape, its producer, the canvases it indexes with
    /// the binding each one carries, and the families whose canvases are absent.
    /// </summary>
    /// <remarks>
    /// The index resolves a published reference to bytes, so it lists exactly what the pass published and
    /// takes each entry's binding from the reference set as it now stands. Those are two different facts:
    /// the pass says what exists, the set says who claims it, and a canvas the pass refused is not indexed
    /// as a name that leads nowhere.
    /// </remarks>
    /// <param name="pass">The pass, which is where the artifacts and the absent families come from.</param>
    /// <param name="set">The reference set with its bindings established, which is what the index states.</param>
    /// <param name="contentGroup">
    /// The content group the index is published under, or null for the group-relative paths the publication
    /// itself states. A consumer resolves an artifact by its content-relative name, and the classic media
    /// index states that name, so the group is applied here rather than by each caller.
    /// </param>
    public static byte[] WriteIndex(CharacterMediaPassResult pass, CharacterMediaReferenceSet set, string? contentGroup = null)
    {
        if (contentGroup is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(contentGroup);
        }

        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(set);
        Dictionary<string, CharacterCanvasReference> references = new(StringComparer.Ordinal);
        foreach (CharacterCanvasReference canvas in set.Canvases)
        {
            references[canvas.MediaId] = canvas;
        }

        List<CharacterMediaIndexEntry> entries = [];
        foreach (CharacterMediaArtifact artifact in pass.Artifacts.OrderBy(artifact => artifact.MediaId, StringComparer.Ordinal))
        {
            // A published artifact is an emission of one reference, so the identity resolves to the
            // reference whether or not a consumer binds it.
            CharacterCanvasReference reference = references.TryGetValue(artifact.MediaId, out CharacterCanvasReference? known)
                ? known
                : artifact.Reference;
            entries.Add(new CharacterMediaIndexEntry(
                artifact.MediaId,
                artifact.RelativePath,
                artifact.Bytes.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(artifact.Bytes)),
                artifact.Width,
                artifact.Height,
                reference.Family,
                System.IO.Path.GetFileName(reference.Path),
                reference.CanvasIndex,
                // The palette the pixels were painted in: a container that carries its own is painted in
                // that one, and stating the reference's instead would send a consumer repainting the
                // canvas to the wrong colours while the bytes looked right.
                artifact.PaletteIdentity,
                artifact.PaletteSource,
                reference.Binding,
                reference.Consumer));
        }

        // The document is built rather than serialized from a record so the entry keys are the classic
        // index's own: it publishes the same facts under 'path', 'byteLength' and 'sha256', and a reader
        // that resolves one group should not have to special-case the other's key names.
        JsonSerializerOptions options = new(PublishedJson.Section);
        JsonArray artifacts = [];
        foreach (CharacterMediaIndexEntry entry in entries)
        {
            artifacts.Add(new JsonObject
            {
                ["mediaId"] = entry.MediaId,
                ["path"] = contentGroup is null ? entry.Path : $"{contentGroup}/{entry.Path}",
                ["byteLength"] = entry.ByteLength,
                ["sha256"] = entry.Sha256,
                ["width"] = entry.Width,
                ["height"] = entry.Height,
                ["family"] = entry.Family,
                ["sourceFile"] = entry.SourceFile,
                ["canvasIndex"] = entry.CanvasIndex,
                ["palette"] = entry.Palette,
                ["paletteSource"] = entry.PaletteSource,
                ["binding"] = entry.Binding == MediaBinding.Admitted ? "admitted" : "requiredPending",
                ["consumer"] = entry.Consumer,
            });
        }

        JsonObject document = new()
        {
            ["schemaVersion"] = IndexSchemaVersion,
            ["generator"] = IndexGenerator,
            ["unreadableFamilies"] = JsonSerializer.SerializeToNode(
                pass.UnreadableFamilies.Select(family => new CharacterMediaUnreadableFamily(
                    family.Family,
                    family.Kind,
                    [.. family.Files.Order(StringComparer.Ordinal)],
                    family.Reason,
                    family.DonorAnchor)).ToArray(),
                options),
            ["artifacts"] = artifacts,
        };
        return [.. System.Text.Encoding.UTF8.GetBytes(document.ToJsonString(options)), (byte)'\n'];
    }
}
