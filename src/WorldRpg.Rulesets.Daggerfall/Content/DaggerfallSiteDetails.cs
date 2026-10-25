using System.Collections.ObjectModel;
using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>The part of one published exterior location read only once the location is used: its buildings and building references.</summary>
internal sealed record DaggerfallSiteDetail(
    IReadOnlyDictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource> Buildings,
    IReadOnlyList<DaggerfallSiteBuildingReference> BuildingReferences)
{
    internal static DaggerfallSiteDetail Empty { get; } = new(new ReadOnlyDictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource>(new Dictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource>()), []);
}

/// <summary>Where one exterior location's detail is read from: the published details and the location's place in them.</summary>
/// <param name="Details">The published details, or null when the catalog was read without the data container.</param>
internal sealed record DaggerfallSiteDetailSource(DaggerfallSiteDetails? Details, DaggerfallSiteId Site, int Width, int Height)
{
    internal DaggerfallSiteDetail Read() => (Details ?? throw Unavailable()).Read(Site, Width, Height);

    internal DaggerfallGroundTileGrid ReadGround() => (Details ?? throw Unavailable()).ReadGround(Site);

    private InvalidOperationException Unavailable() =>
        new($"Location {Site}'s buildings and ground are read from {DaggerfallData.DataContainerName}, and this location catalog was read without it.");
}

/// <summary>
/// The published detail of every exterior location: for each, a record of its buildings and building references
/// and the raw bytes of its 128-by-128 ground grid, below one root of the data container. The location catalog
/// keeps what every location is asked for; a location's detail is read the first time it is used.
/// </summary>
/// <remarks>
/// The most recently read locations are kept, so the site the player is in, and the few a quest or the map is
/// looking at, are read once rather than on every question, while a pass over many locations (a block variant
/// selected for every location that places a block) does not keep all of them.
/// </remarks>
internal sealed class DaggerfallSiteDetails
{
    /// <summary>How many locations' details are kept once read; each is a few kilobytes.</summary>
    private const int Retained = 64;

    private readonly DaggerfallData _data;
    private readonly string _root;
    private readonly Dictionary<(DaggerfallSiteId Site, bool Ground), LinkedListNode<(DaggerfallSiteId Site, bool Ground, object Value)>> _kept = [];
    private readonly LinkedList<(DaggerfallSiteId Site, bool Ground, object Value)> _recent = new();

    internal DaggerfallSiteDetails(DaggerfallData data, string root)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
    }

    internal string DetailPath(DaggerfallSiteId site) => $"{_root}/{site.Region}/{site.Index}.json";

    internal string GroundPath(DaggerfallSiteId site) => $"{_root}/{site.Region}/{site.Index}.ground";

    /// <summary>The detail files of these locations the data container does not carry.</summary>
    internal IEnumerable<string> Missing(IEnumerable<DaggerfallSiteId> sites) =>
        sites.SelectMany(site => new[] { DetailPath(site), GroundPath(site) }).Where(path => !_data.Contains(path));

    internal DaggerfallSiteDetail Read(DaggerfallSiteId site, int width, int height) =>
        (DaggerfallSiteDetail)Kept(site, ground: false, () => ReadDetail(site, width, height));

    internal DaggerfallGroundTileGrid ReadGround(DaggerfallSiteId site) =>
        (DaggerfallGroundTileGrid)Kept(site, ground: true, () => ReadGroundGrid(site));

    private object Kept(DaggerfallSiteId site, bool ground, Func<object> read)
    {
        lock (_kept)
        {
            if (_kept.TryGetValue((site, ground), out var found))
            {
                _recent.Remove(found);
                _recent.AddFirst(found);
                return found.Value.Value;
            }

            object value = read();
            _kept.Add((site, ground), _recent.AddFirst((site, ground, value)));
            if (_recent.Count > Retained)
            {
                var oldest = _recent.Last!;
                _recent.RemoveLast();
                _kept.Remove((oldest.Value.Site, oldest.Value.Ground));
            }

            return value;
        }
    }

    private DaggerfallSiteDetail ReadDetail(DaggerfallSiteId site, int width, int height)
    {
        string path = DetailPath(site);
        DaggerfallContentDiagnostics diagnostics = new();
        try
        {
            using JsonDocument document = JsonDocument.Parse(_data.ReadBytes(path));
            JsonElement detail = DaggerfallBaseContent.Object(document.RootElement, $"location {site} detail '{path}'", diagnostics);
            diagnostics.ThrowIfAny();
            DaggerfallSiteDetail result = new(
                DaggerfallBaseContent.ReadSiteBuildings(detail, site.Region, site.Index, width, height, diagnostics),
                DaggerfallBaseContent.ReadBuildingReferences(detail, diagnostics));
            diagnostics.ThrowIfAny();
            return result;
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Location {site} detail '{path}' is not valid JSON: {exception.Message}");
            throw diagnostics.Exception();
        }
    }

    private DaggerfallGroundTileGrid ReadGroundGrid(DaggerfallSiteId site)
    {
        string path = GroundPath(site);
        ReadOnlyMemory<byte> bytes = _data.ReadBytes(path);
        if (bytes.Length != DaggerfallGroundTileGrid.CellCount)
            throw new DaggerfallContentException([$"Location {site} ground '{path}' carries {bytes.Length} bytes instead of the donor 128-by-128 logical grid."]);
        try
        {
            return DaggerfallGroundTileGrid.FromBytes(bytes.Span, $"{site} ground '{path}'");
        }
        catch (InvalidOperationException exception)
        {
            throw new DaggerfallContentException([exception.Message]);
        }
    }
}
