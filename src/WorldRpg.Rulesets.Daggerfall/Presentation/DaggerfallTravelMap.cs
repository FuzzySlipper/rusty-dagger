using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// One published map image and the world-map rectangle it draws, in world-map pixels. The image's top
/// left corner shows (<see cref="Left"/>, <see cref="Top"/>) and its bottom right corner
/// (<see cref="Right"/>, <see cref="Bottom"/>), so a reader places a world pixel on the image by
/// proportion alone.
/// </summary>
internal sealed record DaggerfallTravelMapImage(string MediaId, int Width, int Height, double Left, double Top, double Right, double Bottom)
{
    /// <summary>Whether this image draws the world pixel.</summary>
    internal bool Shows(DaggerfallTravelMapPixel pixel) => pixel.X >= Left && pixel.X < Right && pixel.Y >= Top && pixel.Y < Bottom;
}

/// <summary>One region the world view offers: where it sits on the world map and how many of its destinations play has revealed.</summary>
internal sealed record DaggerfallTravelMapRegion(int Region, string Name, double X, double Y, int Discovered);

/// <summary>The region sheet the travel map shows and the discovered destinations it draws.</summary>
internal sealed record DaggerfallTravelMapSheetView(
    int Region, string Name, int Page, int Pages, DaggerfallTravelMapImage Image, IReadOnlyList<DaggerfallTravelDestination> Destinations);

/// <summary>What the travel map shows: the world view, the player's map pixel, the regions it offers and the open region sheet.</summary>
internal sealed record DaggerfallTravelMapView(
    DaggerfallTravelMapImage World, DaggerfallTravelMapPixel? Player, IReadOnlyList<DaggerfallTravelMapRegion> Regions, DaggerfallTravelMapSheetView? Sheet);

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
            .Where(destination => Sheets.ContainsKey(destination.Id.Region))
            .GroupBy(destination => destination.Id.Region)
            .ToDictionary(group => group.Key, group => (group.Average(item => (double)item.MapPixel.X), group.Average(item => (double)item.MapPixel.Y)));
    }

    /// <summary>The number of sheets a region's map spans, or zero when the travel map has no art for it.</summary>
    internal static int PageCount(int region) => Sheets.TryGetValue(region, out DaggerfallTravelMapImage[]? sheets) ? sheets.Length : 0;

    /// <summary>
    /// Reads the travel map's view. <paramref name="discovered"/> are the travel owner's discovered
    /// destinations; the open sheet draws those inside it.
    /// </summary>
    internal DaggerfallTravelMapView Read(IReadOnlyList<DaggerfallTravelDestination> discovered, IReadOnlyList<string> regionNames,
        DaggerfallTravelMapPixel? player, int? region, int page)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(regionNames);
        Dictionary<int, int> counts = discovered.GroupBy(destination => destination.Id.Region).ToDictionary(group => group.Key, group => group.Count());
        DaggerfallTravelMapRegion[] regions = [.. Sheets.Keys.Order().Select(id =>
        {
            (double x, double y) = _anchors.TryGetValue(id, out (double X, double Y) anchor) ? anchor : Center(Sheets[id][0]);
            return new DaggerfallTravelMapRegion(id, DaggerfallRegionNames.Name(regionNames, id), x, y, counts.GetValueOrDefault(id));
        })];
        DaggerfallTravelMapSheetView? sheet = null;
        if (region is int open && Sheets.TryGetValue(open, out DaggerfallTravelMapImage[]? sheets) && page >= 0 && page < sheets.Length)
        {
            DaggerfallTravelMapImage image = sheets[page];
            sheet = new(open, DaggerfallRegionNames.Name(regionNames, open), page, sheets.Length, image,
                [.. discovered.Where(destination => destination.Id.Region == open && image.Shows(destination.MapPixel))]);
        }
        return new(World, player, regions, sheet);
    }

    private static (double X, double Y) Center(DaggerfallTravelMapImage image) => ((image.Left + image.Right) / 2, (image.Top + image.Bottom) / 2);

    private static DaggerfallTravelMapImage Sheet(string mediaId, double originX, double originY, int scale = 1) =>
        new(mediaId, SheetWidth, SheetHeight, originX, originY, originX + (double)SheetWidth / scale, originY + (double)SheetHeight / scale);

    private static DaggerfallTravelMapImage Overview(string mediaId, int width, int height, int mapTop, int mapHeight)
    {
        double pixelsPerWorldPixel = (double)mapHeight / DaggerfallTravelMapPixel.Height;
        return new(mediaId, width, height, 0, -mapTop / pixelsPerWorldPixel,
            width / pixelsPerWorldPixel, (height - mapTop) / pixelsPerWorldPixel);
    }
}
