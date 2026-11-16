using System.Collections.ObjectModel;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>
/// One immutable, caller-supplied Arena2 logical source.  Import never opens a
/// host path: labels are portable source identities and bytes are copied when
/// the source set is created.
/// </summary>
public sealed class DungeonLogicalSource
{
    private readonly byte[] bytes;

    public DungeonLogicalSource(string label, ReadOnlySpan<byte> bytes)
    {
        NormalizedImportDocument.RequireLogicalPath(label, nameof(label));
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("A dungeon logical source cannot be empty.", nameof(bytes));
        }

        Label = label;
        this.bytes = bytes.ToArray();
    }

    public string Label { get; }

    public ReadOnlyMemory<byte> Bytes => bytes;
}

/// <summary>
/// Immutable lookup of the Arena2 sources the per-block publication normalizes
/// blocks from.  Labels may contain a portable prefix, but archive leaf names must
/// be unique so source selection cannot depend on a host directory.
/// </summary>
public sealed class DungeonLogicalSourceSet
{
    private readonly IReadOnlyList<DungeonLogicalSource> sources;
    private readonly IReadOnlyDictionary<string, DungeonLogicalSource> byLeafName;

    public DungeonLogicalSourceSet(IEnumerable<DungeonLogicalSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        DungeonLogicalSource[] materialized = sources.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("A dungeon source set must contain at least one source.", nameof(sources));
        }

        Dictionary<string, DungeonLogicalSource> lookup = new(StringComparer.OrdinalIgnoreCase);
        foreach (DungeonLogicalSource source in materialized)
        {
            ArgumentNullException.ThrowIfNull(source);
            string leaf = LeafName(source.Label);
            if (!lookup.TryAdd(leaf, source))
            {
                throw new ArgumentException($"The dungeon source set contains multiple logical sources named '{leaf}'.", nameof(sources));
            }
        }

        this.sources = Array.AsReadOnly(materialized.OrderBy(source => source.Label, StringComparer.Ordinal).ToArray());
        byLeafName = new ReadOnlyDictionary<string, DungeonLogicalSource>(lookup);
    }

    public IReadOnlyList<DungeonLogicalSource> Sources => sources;

    public DungeonLogicalSource Require(string leafName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leafName);
        return byLeafName.TryGetValue(leafName, out DungeonLogicalSource? source)
            ? source
            : throw new MissingArena2SourceException(leafName, $"Dungeon normalization requires logical source '{leafName}'.");
    }

    public bool TryGet(string leafName, out DungeonLogicalSource? source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leafName);
        return byLeafName.TryGetValue(leafName, out source);
    }

    private static string LeafName(string label) => label[(label.LastIndexOf('/') + 1)..];
}
