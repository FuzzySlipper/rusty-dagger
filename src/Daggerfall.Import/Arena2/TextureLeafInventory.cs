namespace Daggerfall.Import.Arena2;

/// <summary>What the enumeration could establish about one texture leaf.</summary>
public enum TextureLeafDisposition
{
    /// <summary>The archive parsed and every record's frames are addressable.</summary>
    Decoded,

    /// <summary>The archive is supplied and does not parse.</summary>
    Malformed,

    /// <summary>The leaf id is inside the documented range and no archive is supplied for it.</summary>
    NotSupplied,
}

/// <summary>
/// One documented texture leaf: its id, the archive supplied for it, and what the
/// enumeration established. A leaf that is not supplied is retained as a source fact
/// rather than dropped, because the id is part of the range the corpus documents.
/// </summary>
public sealed record TextureLeafRecord(
    int Id,
    string Path,
    TextureLeafDisposition Disposition,
    int Records,
    int Frames,
    string Note);

/// <summary>
/// The texture-leaf inventory: every documented leaf id with its disposition, the record
/// and frame counts of the archives that parsed, and the leaves that are not supplied.
/// </summary>
/// <remarks>
/// This records source facts only. It decodes each archive far enough to address its
/// records and frames; it publishes no image, and the geometry, dungeon and media tasks
/// resolve frames through the archive decoder rather than through a second copy of the
/// frame facts kept here.
/// </remarks>
public sealed class TextureLeafInventory
{
    /// <summary>The highest documented leaf id.</summary>
    public const int MaximumLeafId = 511;

    /// <summary>The number of leaves the documentation records as supplied.</summary>
    public const int PresentCount = 472;

    /// <summary>The number of leaves the documentation records as absent.</summary>
    public const int NotSuppliedCount = 40;

    private readonly Dictionary<int, TextureLeafRecord> byId;

    private TextureLeafInventory(string source, IReadOnlyList<TextureLeafRecord> leaves)
    {
        Source = source;
        Leaves = leaves;
        byId = leaves.ToDictionary(leaf => leaf.Id);
    }

    /// <summary>Logical source identity supplied to <see cref="Enumerate"/>.</summary>
    public string Source { get; }

    /// <summary>Every leaf id from 0 to <see cref="MaximumLeafId"/>, in ascending order.</summary>
    public IReadOnlyList<TextureLeafRecord> Leaves { get; }

    /// <summary>The leaves that parsed.</summary>
    public IEnumerable<TextureLeafRecord> Decoded => Leaves.Where(leaf => leaf.Disposition == TextureLeafDisposition.Decoded);

    /// <summary>The supplied leaves that do not parse.</summary>
    public IEnumerable<TextureLeafRecord> Malformed => Leaves.Where(leaf => leaf.Disposition == TextureLeafDisposition.Malformed);

    /// <summary>The documented leaves no archive is supplied for.</summary>
    public IEnumerable<TextureLeafRecord> NotSupplied => Leaves.Where(leaf => leaf.Disposition == TextureLeafDisposition.NotSupplied);

    /// <summary>Total records across the leaves that parsed.</summary>
    public int Records => Decoded.Sum(leaf => leaf.Records);

    /// <summary>Total frames across the leaves that parsed.</summary>
    public int Frames => Decoded.Sum(leaf => leaf.Frames);

    /// <summary>Enumerates the supplied texture leaves.</summary>
    /// <param name="sources">Leaf id and bytes for every supplied archive.</param>
    /// <param name="source">Logical source identity for error messages.</param>
    /// <param name="solidPalette">Palette for archives that carry none, as the corpus uses.</param>
    public static TextureLeafInventory Enumerate(
        IEnumerable<(int Id, string Path, ReadOnlyMemory<byte> Bytes)> sources,
        string source,
        TextureSolidPalette? solidPalette = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Dictionary<int, (string Path, ReadOnlyMemory<byte> Bytes)> supplied = [];
        foreach ((int id, string path, ReadOnlyMemory<byte> bytes) in sources)
        {
            if (id < 0 || id > MaximumLeafId)
            {
                throw new Arena2FormatException(source, 0, $"texture leaf id {id} is outside the documented 0..{MaximumLeafId} range");
            }

            if (!supplied.TryAdd(id, (path, bytes)))
            {
                throw new Arena2FormatException(source, 0, $"'{path}' and '{supplied[id].Path}' both claim texture leaf id {id}");
            }
        }

        List<TextureLeafRecord> leaves = new(MaximumLeafId + 1);
        for (int id = 0; id <= MaximumLeafId; id++)
        {
            if (!supplied.TryGetValue(id, out (string Path, ReadOnlyMemory<byte> Bytes) entry))
            {
                leaves.Add(new TextureLeafRecord(id, string.Empty, TextureLeafDisposition.NotSupplied, 0, 0, "No archive is supplied for this documented leaf id."));
                continue;
            }

            try
            {
                // The id decides the palette the decoder infers, not the caller's path: the
                // path is this record's identity, and letting it select a parse mode would
                // let one label change what the same bytes decode to.
                string label = $"TEXTURE.{id:000}";
                TextureArchive archive = TextureArchive.Parse(entry.Bytes.Span, label, solidPalette);
                if (archive.RecordCount == 0)
                {
                    // A header declaring no records is not a decoded leaf: a consumer
                    // receiving it would have no addressable frame.
                    leaves.Add(new TextureLeafRecord(id, entry.Path, TextureLeafDisposition.Malformed, 0, 0, "The archive declares no records."));
                    continue;
                }

                int frames = 0;
                for (int record = 0; record < archive.RecordCount; record++)
                {
                    frames += archive.GetRecordInfo(record).FrameCount;
                }

                leaves.Add(new TextureLeafRecord(id, entry.Path, TextureLeafDisposition.Decoded, archive.RecordCount, frames, $"{archive.RecordCount} records, {frames} frames."));
            }
            catch (Arena2FormatException failure)
            {
                // A supplied archive that does not parse is a source fact with the decoder's
                // own reason, not a reason to lose the other 471.
                leaves.Add(new TextureLeafRecord(id, entry.Path, TextureLeafDisposition.Malformed, 0, 0, failure.Message));
            }
        }

        return new TextureLeafInventory(source, leaves);
    }

    /// <summary>Gets one leaf by id.</summary>
    public bool TryGet(int id, out TextureLeafRecord? leaf) => byId.TryGetValue(id, out leaf);

    /// <summary>
    /// Requires a leaf to be supplied, naming the consumer that asked for it. A caller that
    /// cannot supply a disposition for a missing leaf fails here rather than publishing a
    /// reference to media the corpus does not carry.
    /// </summary>
    public TextureLeafRecord Require(int id, string consumer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        if (id < 0 || !TryGet(id, out TextureLeafRecord? leaf) || leaf!.Disposition == TextureLeafDisposition.NotSupplied)
        {
            throw new InvalidOperationException($"'{consumer}' references texture leaf {id}, which the supplied corpus does not carry.");
        }

        if (leaf.Disposition == TextureLeafDisposition.Malformed)
        {
            throw new InvalidOperationException($"'{consumer}' references texture leaf {id}, which is supplied and does not parse: {leaf.Note}");
        }

        return leaf;
    }
}
