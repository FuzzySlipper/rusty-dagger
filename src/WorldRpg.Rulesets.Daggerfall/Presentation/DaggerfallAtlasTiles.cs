using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The atlas tiles granted to the page: each tile it asks for is handed to the Engine's UI image service, which serves it
/// at a URL the travel map draws, so no tile's bytes ride a snapshot. A tile is granted once and kept for the session.
/// </summary>
internal sealed class DaggerfallAtlasTiles(DaggerfallAtlas atlas, DaggerfallData data, IUiService ui) : IDisposable
{
    private readonly Dictionary<string, UiImage> _granted = new(StringComparer.Ordinal);
    private readonly List<(string Id, string Url)> _urls = [];

    internal DaggerfallAtlas Atlas => atlas;

    /// <summary>Every granted tile and the URL the page draws it from, in the order granted.</summary>
    internal IReadOnlyList<(string Id, string Url)> Urls => _urls;

    /// <summary>
    /// Grants the tiles the page asked for. A request naming a tile the atlas does not have is refused whole, since the
    /// page asked for a map it was never told of.
    /// </summary>
    internal bool Grant(IReadOnlyList<DaggerfallAtlasTileRequest> tiles)
    {
        if (tiles.Any(tile => !atlas.Has(tile.Level, tile.Column, tile.Row))) return false;
        foreach (DaggerfallAtlasTileRequest tile in tiles)
        {
            string id = DaggerfallAtlas.TileId(tile.Level, tile.Column, tile.Row);
            if (_granted.ContainsKey(id)) continue;
            using ContentReference reference = data.OpenReference(atlas.TilePaths[id]);
            UiImage image = ui.OpenImage(new UiImageRequest(reference));
            _granted.Add(id, image);
            _urls.Add((id, image.Url()));
        }
        return true;
    }

    public void Dispose()
    {
        foreach (UiImage image in _granted.Values) image.Dispose();
        _granted.Clear();
        _urls.Clear();
    }
}
