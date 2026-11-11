using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// One published map image and the world-map rectangle it draws, in world-map pixels. The image's top
/// left corner shows (<see cref="Left"/>, <see cref="Top"/>) and its bottom right corner
/// (<see cref="Right"/>, <see cref="Bottom"/>), so a reader places a world pixel on the image by
/// proportion alone.
/// </summary>
internal sealed record DaggerfallTravelMapImage(string MediaId, int Width, int Height, double Left, double Top, double Right, double Bottom);

/// <summary>One region as the travel map names it: where its name sits on the world map, and how many of its places play has revealed.</summary>
internal sealed record DaggerfallTravelMapRegion(int Region, string Name, double X, double Y, int Discovered);

/// <summary>One classic region sheet and the region it draws, for the classic map.</summary>
internal sealed record DaggerfallTravelMapSheet(int Region, DaggerfallTravelMapImage Image);

/// <summary>
/// What the travel map is drawn on: the atlas the import drew from the land when there is one and the player has not chosen
/// classic art, else the classic world image with each region's sheet at the rectangle it draws.
/// </summary>
internal sealed record DaggerfallTravelMapBase(DaggerfallAtlasTiles? Atlas, DaggerfallTravelMapImage ClassicWorld, IReadOnlyList<DaggerfallTravelMapSheet> ClassicSheets);

/// <summary>
/// What the travel map shows: its base, the player's map pixel and heading (degrees clockwise from north), the regions
/// it names, every place play has revealed, and the names of the land and sea.
/// </summary>
internal sealed record DaggerfallTravelMapView(
    DaggerfallTravelMapBase Base, DaggerfallTravelMapPixel? Player, double? Heading, IReadOnlyList<DaggerfallTravelMapRegion> Regions,
    IReadOnlyList<DaggerfallTravelDestination> Places, DaggerfallWorldNames Names)
{
    /// <summary>
    /// Which places the map draws, as one token that changes whenever they do. Thousands of places are known from the
    /// start, so the list travels only when this changes or the page asks for it again.
    /// </summary>
    internal string PlacesRevision { get; } = Revision(Places);

    private static string Revision(IReadOnlyList<DaggerfallTravelDestination> places)
    {
        ulong hash = 14695981039346656037UL;
        foreach (DaggerfallTravelDestination place in places)
            hash = unchecked((hash ^ (ulong)((place.Id.Region << 16) ^ place.Id.Index)) * 1099511628211UL);
        return $"{places.Count}.{hash:x16}";
    }
}

/// <summary>
/// The travel map's placement of the published map art over the world map, and the views it reads
/// from the travel owner's destinations.
/// </summary>
/// <remarks>
/// The world view is the travel window's overview canvas, whose 320 by 160 middle band draws the whole
/// 1000 by 500 world map below a 12-pixel title strip. Each region sheet is a 320 by 160 canvas whose
/// top left corner the donor's travel window places at a fixed world pixel; most sheets draw one world
/// pixel per image pixel. Three regions span several sheets, and two island sheets draw four image
/// pixels per world pixel, placed where the donor's pointer mapping reads them. These are layout facts
/// of the art the travel map draws, so they stay beside it; the player never sees an undiscovered
/// destination here, because the sheet view reads only the travel owner's discovered destinations.
/// </remarks>
internal sealed class DaggerfallTravelMap
{
    private const int SheetWidth = 320;
    private const int SheetHeight = 160;

    /// <summary>The world overview: a 320 by 200 canvas whose band from row 12 draws the world at 0.32 image pixels per world pixel.</summary>
    internal static DaggerfallTravelMapImage World { get; } = Overview("map.trav0i00", width: 320, height: 200, mapTop: 12, mapHeight: 160);

    private static readonly IReadOnlyDictionary<int, DaggerfallTravelMapImage[]> Sheets = new Dictionary<int, DaggerfallTravelMapImage[]>
    {
        [0] = [Sheet("map.fmapai00", 212, 340), Sheet("map.fmapbi00", 322, 340)],
        [1] = [Sheet("map.fmapai01", 583, 279), Sheet("map.fmapbi01", 680, 279), Sheet("map.fmapci01", 583, 340), Sheet("map.fmapdi01", 680, 340)],
        [5] = [Sheet("map.fmap0i05", 381, 4)],
        [9] = [Sheet("map.fmap0i09", 525, 114)],
        [11] = [Sheet("map.fmap0i11", 437, 340)],
        [16] = [Sheet("map.fmapai16", 578, 0), Sheet("map.fmapbi16", 680, 0), Sheet("map.fmapci16", 578, 52), Sheet("map.fmapdi16", 680, 52)],
        [17] = [Sheet("map.fmap0i17", 39, 106)],
        [18] = [Sheet("map.fmap0i18", 20, 29)],
        // Betony's sheet is drawn at four times scale; the donor's pointer mapping reads its origin
        // pixel (80, 123) at a quarter and then shifts it by (60, 212).
        [19] = [Sheet("map.fmap0i19", 80, 123 / 4.0 + 212, scale: 4)],
        [20] = [Sheet("map.fmap0i20", 217, 293)],
        [21] = [Sheet("map.fmap0i21", 263, 79)],
        [22] = [Sheet("map.fmap0i22", 548, 219)],
        [23] = [Sheet("map.fmap0i23", 680, 146)],
        [26] = [Sheet("map.fmap0i26", 680, 80)],
        [32] = [Sheet("map.fmap0i32", 41, 0)],
        [33] = [Sheet("map.fmap0i33", 660, 101)],
        [34] = [Sheet("map.fmap0i34", 578, 40)],
        [35] = [Sheet("map.fmap0i35", 525, 3)],
        [36] = [Sheet("map.fmap0i36", 440, 40)],
        [37] = [Sheet("map.fmap0i37", 448, 0)],
        [38] = [Sheet("map.fmap0i38", 366, 0)],
        [39] = [Sheet("map.fmap0i39", 300, 8)],
        [40] = [Sheet("map.fmap0i40", 202, 0)],
        [41] = [Sheet("map.fmap0i41", 223, 6)],
        [42] = [Sheet("map.fmap0i42", 148, 76)],
        [43] = [Sheet("map.fmap0i43", 15, 340)],
        [44] = [Sheet("map.fmap0i44", 61, 340)],
        [45] = [Sheet("map.fmap0i45", 86, 338)],
        [46] = [Sheet("map.fmap0i46", 132, 340)],
        [47] = [Sheet("map.fmap0i47", 344, 309)],
        [48] = [Sheet("map.fmap0i48", 381, 251)],
        [49] = [Sheet("map.fmap0i49", 553, 255)],
        [50] = [Sheet("map.fmap0i50", 661, 217)],
        [51] = [Sheet("map.fmap0i51", 672, 275)],
        [52] = [Sheet("map.fmap0i52", 680, 256)],
        [53] = [Sheet("map.fmap0i53", 680, 340)],
        [54] = [Sheet("map.fmap0i54", 491, 340)],
        [55] = [Sheet("map.fmap0i55", 293, 340)],
        [56] = [Sheet("map.fmap0i56", 263, 340)],
        [57] = [Sheet("map.fmap0i57", 680, 157)],
        [58] = [Sheet("map.fmap0i58", 17, 53)],
        [59] = [Sheet("map.fmap0i59", 0, 0)],
        [60] = [Sheet("map.fmap0i60", 107, 11)],
        // Cybiades' sheet is drawn at four times scale about the corner (440, 340) it shares with
        // Sentinel's sheet, so its origin pixel (255, 275) reads a quarter of the way from that corner.
        [61] = [Sheet("map.fmap0i61", 440 + (255 - 440) / 4.0, 340 + (275 - 340) / 4.0, scale: 4)],
    };

    /// <summary>Every published image the travel map draws, which the session's art block therefore carries.</summary>
    internal static IReadOnlyList<string> MediaIds { get; } =
        [World.MediaId, .. Sheets.Values.SelectMany(sheets => sheets).Select(sheet => sheet.MediaId)];

    private readonly IReadOnlyDictionary<int, (double X, double Y)> _anchors;

    /// <param name="destinations">
    /// Every destination the location set carries, discovered or not. Only their mean position per
    /// region is kept: it places the region's name on the world view and names no site.
    /// </param>
    internal DaggerfallTravelMap(IReadOnlyList<DaggerfallTravelDestination> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        _anchors = destinations
            .GroupBy(destination => destination.Id.Region)
            .ToDictionary(group => group.Key, group => (group.Average(item => (double)item.MapPixel.X), group.Average(item => (double)item.MapPixel.Y)));
    }

    /// <summary>Every classic region sheet with the region it draws, in region order.</summary>
    private static IReadOnlyList<DaggerfallTravelMapSheet> ClassicSheets { get; } =
        [.. Sheets.OrderBy(entry => entry.Key).SelectMany(entry => entry.Value.Select(image => new DaggerfallTravelMapSheet(entry.Key, image)))];

    /// <summary>
    /// Reads the travel map's view. <paramref name="discovered"/> are the travel owner's discovered destinations, the only
    /// places it draws; <paramref name="atlas"/> is the base drawn from the land, or null for the classic art.
    /// </summary>
    internal DaggerfallTravelMapView Read(IReadOnlyList<DaggerfallTravelDestination> discovered, IReadOnlyList<string> regionNames,
        DaggerfallTravelMapPixel? player, double? heading, DaggerfallAtlasTiles? atlas, DaggerfallWorldNames names)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(regionNames);
        ArgumentNullException.ThrowIfNull(names);
        Dictionary<int, int> counts = discovered.GroupBy(destination => destination.Id.Region).ToDictionary(group => group.Key, group => group.Count());
        DaggerfallTravelMapRegion[] regions = [.. _anchors.Keys.Order().Select(id =>
            new DaggerfallTravelMapRegion(id, DaggerfallRegionNames.Name(regionNames, id), _anchors[id].X, _anchors[id].Y, counts.GetValueOrDefault(id)))];
        return new(new(atlas, World, ClassicSheets), player, heading, regions, discovered, names);
    }

    private static DaggerfallTravelMapImage Sheet(string mediaId, double originX, double originY, int scale = 1) =>
        new(mediaId, SheetWidth, SheetHeight, originX, originY, originX + (double)SheetWidth / scale, originY + (double)SheetHeight / scale);

    private static DaggerfallTravelMapImage Overview(string mediaId, int width, int height, int mapTop, int mapHeight)
    {
        double pixelsPerWorldPixel = (double)mapHeight / DaggerfallTravelMapPixel.Height;
        return new(mediaId, width, height, 0, -mapTop / pixelsPerWorldPixel,
            width / pixelsPerWorldPixel, (height - mapTop) / pixelsPerWorldPixel);
    }
}
