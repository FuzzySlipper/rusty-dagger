using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Travel;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>From which zoom of the atlas a kind of name is written: the whole bay, a region, or close in.</summary>
internal enum DaggerfallWorldNameZoom { World, Region, Close }

/// <summary>A kind of named feature (a sea, a range, a river) and the zoom its names are written from.</summary>
internal sealed record DaggerfallWorldNameKind(string Id, DaggerfallWorldNameZoom ShownFrom);

/// <summary>One named feature of the land or sea, written at a world-map pixel.</summary>
internal sealed record DaggerfallWorldName(string Id, string Name, string Kind, double X, double Y);

/// <summary>
/// The game's one list of the names of its land and sea: seas, ranges, rivers and the like, which the location data does
/// not name. The atlas writes them; anything else that needs a feature's name reads it here. The list grows as names are
/// authored, and may be empty.
/// </summary>
internal sealed record DaggerfallWorldNames(IReadOnlyList<DaggerfallWorldNameKind> Kinds, IReadOnlyList<DaggerfallWorldName> Names)
{
    internal static DaggerfallWorldNames None { get; } = new([], []);

    internal DaggerfallWorldNameKind KindOf(DaggerfallWorldName name) => Kinds.Single(kind => kind.Id == name.Kind);
}

internal static partial class DaggerfallBaseContent
{
    /// <summary>
    /// Reads the list of world names. A name of a kind the list does not declare, a repeated id, or a place off the map is
    /// reported, since the atlas would write it nowhere or twice.
    /// </summary>
    internal static DaggerfallWorldNames ReadWorldNames(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("worldNames", out JsonElement section)) return DaggerfallWorldNames.None;
        JsonElement list = Object(section, "worldNames", diagnostics);
        List<DaggerfallWorldNameKind> kinds = [];
        foreach (JsonElement kind in Array(list, "kinds", diagnostics))
        {
            string id = Text(kind, "id", diagnostics);
            string from = Text(kind, "shownFrom", diagnostics);
            if (!Enum.TryParse(from, ignoreCase: true, out DaggerfallWorldNameZoom zoom) || !Enum.IsDefined(zoom))
                diagnostics.Add($"World-name kind '{id}' is shown from '{from}', which is not world, region or close.");
            if (kinds.Any(known => known.Id == id)) diagnostics.Add($"World-name kind '{id}' is declared twice.");
            kinds.Add(new(id, zoom));
        }
        List<DaggerfallWorldName> names = [];
        foreach (JsonElement entry in Array(list, "names", diagnostics))
        {
            string id = Text(entry, "id", diagnostics), name = Text(entry, "name", diagnostics), kind = Text(entry, "kind", diagnostics);
            double x = Number(entry, "x", diagnostics), y = Number(entry, "y", diagnostics);
            if (name.Trim().Length == 0) diagnostics.Add($"World name '{id}' has no name.");
            if (kinds.All(known => known.Id != kind)) diagnostics.Add($"World name '{id}' is a '{kind}', a kind the list does not declare.");
            if (names.Any(known => known.Id == id)) diagnostics.Add($"World name '{id}' is listed twice.");
            if (x is < 0 or >= DaggerfallTravelMapPixel.Width || y is < 0 or >= DaggerfallTravelMapPixel.Height)
                diagnostics.Add($"World name '{id}' is written at ({x}, {y}), off the {DaggerfallTravelMapPixel.Width} by {DaggerfallTravelMapPixel.Height} world map.");
            names.Add(new(id, name, kind, x, y));
        }
        return new(kinds, names);
    }
}
