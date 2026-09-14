using System.Security.Cryptography;
using System.Text.Json;
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
public sealed record CharacterMediaPassResult(
    IReadOnlyList<CharacterMediaArtifact> Artifacts,
    IReadOnlyList<string> Refusals,
    IReadOnlyList<CharacterMediaUnreadableFamily> UnreadableFamilies)
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

    /// <summary>
    /// Why an RCI grid's cells carry no artifact here. The cells are enumerated from the file's byte
    /// arithmetic and their shape is known, but nothing in this repository slices a cell's pixels.
    /// </summary>
    private const string RciGridReason =
        "the RCI reader enumerates the grid's cells by shape from the file's own byte arithmetic, and nothing in this repository slices a cell's pixels, so the cells are addressable and unpublishable";

    /// <summary>
    /// Where an artifact's palette comes from. A classic animation's frames are painted in the palette
    /// inside the container while every other family here is read with a supplied palette file, and the
    /// difference is what tells a consumer whether re-encoding may lose it.
    /// </summary>
    private static string PaletteSource(CharacterCanvasReference reference) =>
        reference.Path.EndsWith(".CEL", StringComparison.OrdinalIgnoreCase)
            ? "embedded-in-source-file"
            : "supplied-palette-file";

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

        return new(artifacts, refusals, UnreadableFamilies(inventory, refusedSources));
    }

    /// <summary>
    /// The families and shapes whose canvases this repository cannot publish, derived from the canvases
    /// the pass refused and from what the supplied files were read as.
    /// </summary>
    /// <remarks>
    /// The subject is the pass's own result rather than a second reading of it: a supplied file is named
    /// here when it carries canvases and none of them produced an artifact, and the reason is the refusal
    /// the readers gave. Deriving this from the decode shapes alone would have to re-decide which shapes
    /// publish, which is the decision that just went wrong.
    /// </remarks>
    /// <param name="inventory">The supplied files, so a file no reader read is still accounted for.</param>
    /// <param name="refusedSources">The supplied files whose canvases produced no artifact.</param>
    private static IReadOnlyList<CharacterMediaUnreadableFamily> UnreadableFamilies(
        CharacterMediaInventory inventory,
        IReadOnlySet<string> refusedSources)
    {
        Dictionary<string, List<string>> unread = new(StringComparer.Ordinal);
        List<string> rciGrid = [];
        foreach (CharacterMediaRecord file in inventory.Files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            string name = System.IO.Path.GetFileName(file.Path);
            // A canvas the pass refused takes its file out of the published set; a file whose format no
            // reader read was never a candidate. Both are canvases that are not here.
            if (!refusedSources.Contains(name) && file.Decode != Arena2CanvasKind.Unread) continue;
            if (file.Decode == Arena2CanvasKind.RciGrid)
            {
                rciGrid.Add(name);
                continue;
            }

            if (!unread.TryGetValue(file.Family, out List<string>? families)) unread[file.Family] = families = [];
            families.Add(name);
        }

        List<CharacterMediaUnreadableFamily> unreadable = [];
        foreach ((string family, List<string> files) in unread.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            string reader = DonorReaders.GetValueOrDefault(family, string.Empty);
            unreadable.Add(new CharacterMediaUnreadableFamily(
                family,
                $"{family} container",
                files,
                reader.Length == 0
                    ? $"nothing in this repository reads the {family} format, and no donor reader is recorded for it, so its files carry no canvas here"
                    : $"nothing in this repository reads the {family} format: the donor reads it with {reader}, and without it the file's canvases have no pixels here rather than an approximation",
                reader));
        }

        if (rciGrid.Count != 0)
        {
            unreadable.Add(new CharacterMediaUnreadableFamily(
                "FACE",
                "fixed-cell RCI grid",
                rciGrid,
                RciGridReason,
                string.Empty));
        }

        return unreadable;
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
    public static byte[] WriteIndex(CharacterMediaPassResult pass, CharacterMediaReferenceSet set)
    {
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
                reference.Palette,
                PaletteSource(reference),
                reference.Binding,
                reference.Consumer));
        }

        JsonSerializerOptions options = new(PublishedJson.Section);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            new CharacterMediaIndexDocument(
                IndexSchemaVersion,
                IndexGenerator,
                [.. pass.UnreadableFamilies.Select(family => new CharacterMediaUnreadableFamily(
                    family.Family,
                    family.Kind,
                    [.. family.Files.Order(StringComparer.Ordinal)],
                    family.Reason,
                    family.DonorAnchor))],
                entries),
            options);
        return [.. bytes, (byte)'\n'];
    }

    /// <summary>The published index document: its shape, its producer, what it could not publish, and its canvases.</summary>
    private sealed record CharacterMediaIndexDocument(
        int SchemaVersion,
        string Generator,
        IReadOnlyList<CharacterMediaUnreadableFamily> UnreadableFamilies,
        IReadOnlyList<CharacterMediaIndexEntry> Artifacts);
}
