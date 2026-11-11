using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Travel;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>One level of the travel atlas: its size in pixels and its tiles, coarsest level first.</summary>
internal sealed record DaggerfallAtlasLevel(int Level, int Width, int Height, int Columns, int Rows);

/// <summary>
/// The travel atlas the import drew from the land: a pyramid of square tiles over the whole world map, each level halving
/// the next. The page asks for the tiles it shows and draws them under the places and names the ruleset publishes; a data
/// container from before the atlas has none, and the travel map then draws the classic map art.
/// </summary>
internal sealed record DaggerfallAtlas(int TileSize, IReadOnlyList<DaggerfallAtlasLevel> Levels, IReadOnlyDictionary<string, string> TilePaths)
{
    internal const string ManifestPath = "worldrpg/media/atlas/atlas.json";
    internal const string InventoryPath = "worldrpg/media/atlas-inventory.json";
    private const string TilePrefix = "atlas.";

    /// <summary>The most tiles one request may ask for: a few screens' worth, so a request stays a bounded read.</summary>
    internal const int MaximumTilesPerRequest = 64;

    internal static string TileId(int level, int column, int row) => $"{TilePrefix}{level}.{column}.{row}";

    internal bool Has(int level, int column, int row) =>
        level >= 0 && level < Levels.Count && column >= 0 && column < Levels[level].Columns && row >= 0 && row < Levels[level].Rows;

    /// <summary>The atlas the data container carries, or null when it carries none.</summary>
    /// <exception cref="InvalidOperationException">A manifest that is not the published shape, or that does not cover the world map.</exception>
    internal static DaggerfallAtlas? Read(DaggerfallData content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.TryReadFile(ManifestPath, out ReadOnlyMemory<byte> bytes)) return null;
        int tileSize, worldWidth, worldHeight;
        DaggerfallAtlasLevel[] levels;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            tileSize = root.GetProperty("tileSize").GetInt32();
            worldWidth = root.GetProperty("worldWidth").GetInt32();
            worldHeight = root.GetProperty("worldHeight").GetInt32();
            levels = [.. root.GetProperty("levels").EnumerateArray().Select(level => new DaggerfallAtlasLevel(
                level.GetProperty("level").GetInt32(), level.GetProperty("width").GetInt32(), level.GetProperty("height").GetInt32(),
                level.GetProperty("columns").GetInt32(), level.GetProperty("rows").GetInt32()))];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new InvalidOperationException($"The atlas manifest '{ManifestPath}' is not the published shape.", exception);
        }
        if (worldWidth != DaggerfallTravelMapPixel.Width || worldHeight != DaggerfallTravelMapPixel.Height)
            throw new InvalidOperationException($"The atlas manifest '{ManifestPath}' does not cover the {DaggerfallTravelMapPixel.Width} by {DaggerfallTravelMapPixel.Height} world map.");
        if (tileSize <= 0 || levels.Length == 0 || levels.Where((level, index) => level.Level != index
                || level.Columns * tileSize < level.Width || level.Rows * tileSize < level.Height).Any())
            throw new InvalidOperationException($"The atlas manifest '{ManifestPath}' states levels that do not tile its map.");
        // Every tile the levels call for is in the inventory, so a tile the page asks for is always there to grant.
        Dictionary<string, string> paths = ReadTilePaths(content);
        string[] absent = [.. levels.SelectMany(level => Enumerable.Range(0, level.Columns * level.Rows)
            .Select(i => TileId(level.Level, i % level.Columns, i / level.Columns))).Where(id => !paths.ContainsKey(id))];
        if (absent.Length != 0) throw new InvalidOperationException($"The atlas inventory '{InventoryPath}' lacks {absent.Length} tiles its manifest states, such as '{absent[0]}'.");
        return new(tileSize, levels, paths);
    }

    private static Dictionary<string, string> ReadTilePaths(DaggerfallData content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content.ReadBytes(InventoryPath));
            return document.RootElement.GetProperty("artifacts").EnumerateArray()
                .Where(artifact => artifact.TryGetProperty("mediaId", out _))
                .ToDictionary(artifact => artifact.GetProperty("mediaId").GetString()!, artifact => artifact.GetProperty("path").GetString()!, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException($"The atlas inventory '{InventoryPath}' is not the published shape.", exception);
        }
    }
}
