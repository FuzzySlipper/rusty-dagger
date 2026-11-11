using System.Buffers.Binary;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Rgb = (byte R, byte G, byte B);

namespace Daggerfall.Import.Normalization;

/// <summary>One river as the atlas draws it: its stations in relief nodes, and each station's half width in metres.</summary>
public sealed record AtlasRiver(float[] X, float[] Y, float[] HalfWidthMetres);

/// <summary>What the atlas is drawn from, all on the relief's lattice or the world map's pixels.</summary>
/// <param name="Width">The relief's width in nodes.</param>
/// <param name="Height">The relief's height in nodes.</param>
/// <param name="Metres">Each node's height in metres; the ocean clamp marks the sea.</param>
/// <param name="NodesPerPixel">Relief nodes along one world-map pixel.</param>
/// <param name="Climate">Each world-map pixel's climate name, row by row.</param>
/// <param name="Region">Each world-map pixel's region, or -1 at sea, row by row.</param>
/// <param name="Rivers">The rivers the relief drained into.</param>
public sealed record AtlasInput(int Width, int Height, float[] Metres, int NodesPerPixel, string[] Climate, int[] Region, IReadOnlyList<AtlasRiver> Rivers);

/// <summary>What the atlas draws over a climate's land: stands of trees, marsh or dunes.</summary>
public enum AtlasCoverKind { None, Broadleaf, Conifer, Marsh, Dunes }

/// <summary>A climate's cover: its kind, how thickly it grows where it grows, and the colour it is drawn in.</summary>
public sealed record AtlasCover(AtlasCoverKind Kind, double Density, Rgb Colour);

/// <summary>An atlas palette: how land of each climate and height, its cover, mountains, the sea, coast, rivers and borders are coloured.</summary>
/// <param name="Climates">Each climate's lowland colour.</param>
/// <param name="Covers">Each climate's cover.</param>
/// <param name="Upland">The colour land takes as it climbs from its lowland.</param>
/// <param name="Rock">The colour of bare high ground.</param>
/// <param name="Snow">The colour of the highest ground and the mountains' caps.</param>
/// <param name="Halo">The pale water along a coast.</param>
/// <param name="Ripple">The lines that echo a coast offshore.</param>
/// <param name="Ink">The outline of mountains and hills.</param>
/// <param name="RockLit">A mountain's face turned to the light.</param>
/// <param name="RockShade">A mountain's face turned from it.</param>
/// <param name="Shadow">The shadow a tree casts.</param>
public sealed record AtlasPalette(
    IReadOnlyDictionary<string, Rgb> Climates, IReadOnlyDictionary<string, AtlasCover> Covers, Rgb Upland, Rgb Rock, Rgb Snow,
    Rgb Shallows, Rgb Deeps, Rgb Halo, Rgb Ripple, Rgb Coast, Rgb River, Rgb Border, Rgb Ink, Rgb RockLit, Rgb RockShade, Rgb Shadow,
    double Shading, double Paper, double Saturation)
{
    /// <summary>
    /// The painted atlas: green lowlands climbing through khaki uplands to stone and snow, forests, marshes and dunes in
    /// their climates, drawn mountains, and a deep sea paling and rippling along its coasts.
    /// </summary>
    public static AtlasPalette Painted { get; } = new(
        new Dictionary<string, Rgb>(StringComparer.Ordinal)
        {
            ["Woodlands"] = (104, 136, 74), ["HauntedWoodlands"] = (92, 112, 74), ["Mountain"] = (138, 124, 102),
            ["MountainWoods"] = (96, 122, 72), ["Rainforest"] = (72, 124, 66), ["Subtropical"] = (168, 158, 96),
            ["Swamp"] = (98, 112, 78), ["Desert"] = (206, 172, 110), ["Desert2"] = (196, 160, 100), ["Ocean"] = (104, 136, 74),
        },
        new Dictionary<string, AtlasCover>(StringComparer.Ordinal)
        {
            ["Woodlands"] = new(AtlasCoverKind.Broadleaf, .62, (62, 100, 46)), ["HauntedWoodlands"] = new(AtlasCoverKind.Conifer, .72, (58, 74, 58)),
            ["Mountain"] = new(AtlasCoverKind.Conifer, .2, (56, 86, 54)), ["MountainWoods"] = new(AtlasCoverKind.Conifer, .8, (48, 86, 50)),
            ["Rainforest"] = new(AtlasCoverKind.Broadleaf, .88, (38, 92, 42)), ["Subtropical"] = new(AtlasCoverKind.Broadleaf, .3, (96, 118, 58)),
            ["Swamp"] = new(AtlasCoverKind.Marsh, .6, (52, 72, 48)), ["Desert"] = new(AtlasCoverKind.Dunes, .4, (166, 122, 70)),
            ["Desert2"] = new(AtlasCoverKind.Dunes, .3, (156, 114, 66)), ["Ocean"] = new(AtlasCoverKind.None, 0, (0, 0, 0)),
        },
        Upland: (160, 150, 98), Rock: (150, 134, 112), Snow: (238, 236, 230),
        Shallows: (98, 150, 160), Deeps: (44, 88, 116), Halo: (150, 192, 192), Ripple: (196, 224, 218),
        Coast: (30, 44, 56), River: (52, 98, 140), Border: (150, 70, 70),
        Ink: (52, 42, 34), RockLit: (194, 182, 158), RockShade: (106, 92, 78), Shadow: (26, 40, 26),
        Shading: .5, Paper: .04, Saturation: 1.05);
}

/// <summary>One square tile of one level of the atlas, as RGBA8 rows.</summary>
public sealed record AtlasTile(int Level, int Column, int Row, int Size, byte[] Rgba);

/// <summary>One level of the atlas: its size in pixels and how many tiles it takes.</summary>
public sealed record AtlasLevel(int Level, int Width, int Height, int Columns, int Rows);

/// <summary>
/// Draws the travel atlas from the land itself, in two passes. The ground is drawn once at the finest level, one pixel to
/// a relief node: each climate's colour climbing through upland to stone and snow, the relief shaded from the north-west,
/// the sea deepening away from land, over a parchment mottle; each coarser level halves it. Then each level is marked at
/// its own scale, so a mark is as large on screen at every zoom and a coarser level simply has fewer: mountains and hills
/// on high ground, each climate's forests, marsh or dunes in clumps, the coast's halo and ripples, rivers whose width
/// follows their water, the coast's ink and faint dashed region borders. Names, places and routes are drawn by the page
/// over the tiles, not baked in. All its noise comes from coordinate hashes, so an atlas depends on its input alone.
/// </summary>
public static class AtlasBake
{
    public const int TileSize = 256;
    /// <summary>How many levels the atlas halves its finest level into, finest last.</summary>
    public const int LevelCount = 3;
    private const double OceanMetres = 27.2 * 1.5, PixelMetres = 819.2, Exaggeration = 2.5;
    private const double LightAzimuth = 315 * Math.PI / 180, LightAltitude = 40 * Math.PI / 180;

    /// <summary>The levels an atlas of the input's size takes, coarsest first.</summary>
    public static IReadOnlyList<AtlasLevel> Levels(int width, int height) => [.. Enumerable.Range(0, LevelCount).Select(level =>
    {
        int w = width >> (LevelCount - 1 - level), h = height >> (LevelCount - 1 - level);
        return new AtlasLevel(level, w, h, (w + TileSize - 1) / TileSize, (h + TileSize - 1) / TileSize);
    })];

    /// <summary>Draws the ground at its finest level and halves it into the coarser ones, marks every level, then cuts each into tiles.</summary>
    public static IEnumerable<AtlasTile> Tiles(AtlasInput input, AtlasPalette palette)
    {
        AtlasCanvas level = Ground(input, palette);
        List<AtlasCanvas> levels = [level];
        for (int i = 1; i < LevelCount; i++) levels.Insert(0, level = level.Halve());
        for (int index = 0; index < levels.Count; index++)
        {
            AtlasCanvas canvas = levels[index];
            Mark(canvas, input, palette, 1 << (LevelCount - 1 - index));
            byte[] rgba = canvas.ToRgba();
            for (int row = 0; row * TileSize < canvas.Height; row++)
            for (int column = 0; column * TileSize < canvas.Width; column++)
                yield return new(index, column, row, TileSize, Cut(canvas.Width, canvas.Height, rgba, column, row));
        }
    }

    /// <summary>The atlas at its finest level, one pixel to a relief node, as RGBA8 rows.</summary>
    public static byte[] Draw(AtlasInput input, AtlasPalette palette)
    {
        AtlasCanvas canvas = Ground(input, palette);
        Mark(canvas, input, palette, 1);
        return canvas.ToRgba();
    }

    /// <summary>The ground at the finest level: land coloured by climate and height and shaded, the sea deepening offshore.</summary>
    private static AtlasCanvas Ground(AtlasInput input, AtlasPalette palette)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(palette);
        int w = input.Width, h = input.Height, n = input.NodesPerPixel, count = checked(w * h);
        if (input.Metres.Length != count) throw new ArgumentException("The relief's heights do not fill its lattice.", nameof(input));
        int mapWidth = w / n, mapHeight = h / n;
        if (input.Climate.Length != mapWidth * mapHeight || input.Region.Length != mapWidth * mapHeight)
            throw new ArgumentException("The climate and region grids are not the world map's size.", nameof(input));

        bool[] sea = new bool[count];
        for (int i = 0; i < count; i++) sea[i] = input.Metres[i] <= OceanMetres + .01;

        // Each climate's colour, met softly where climates meet.
        AtlasCanvas canvas = new(w, h);
        float[] r = canvas.R, g = canvas.G, b = canvas.B;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            (byte cr, byte cg, byte cb) = ClimateColour(palette, input.Climate[(y / n * mapWidth) + (x / n)]);
            int i = (y * w) + x;
            r[i] = cr; g[i] = cg; b[i] = cb;
        }
        foreach (float[] channel in new[] { r, g, b }) Blur(channel, w, h, 3);

        double spacing = PixelMetres / n, flat = Math.Sin(LightAltitude);
        float[] depth = Distance(sea, w, h, toward: false);
        // The shading reads the relief a little softened, so the drawn marks rather than every gully carry the land's shape.
        float[] soft = (float[])input.Metres.Clone();
        Blur(soft, w, h, 1);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w) + x;
            double paper = 1 + (palette.Paper * Mottle(x, y));
            if (sea[i])
            {
                double d = Math.Min(1, depth[i] / 40);
                r[i] = (float)(Mix(palette.Shallows.R, palette.Deeps.R, d) * paper);
                g[i] = (float)(Mix(palette.Shallows.G, palette.Deeps.G, d) * paper);
                b[i] = (float)(Mix(palette.Shallows.B, palette.Deeps.B, d) * paper);
                continue;
            }
            // Land climbs from its climate's lowland through upland to stone, and the highest to snow.
            double metres = input.Metres[i];
            (double lr, double lg, double lb) = (r[i], g[i], b[i]);
            // A forest's floor darkens toward its trees, so a forest reads as a mass beneath its drawn trees.
            AtlasCover cover = CoverOf(palette, input.Climate[(y / n * mapWidth) + (x / n)]);
            if (cover.Kind is AtlasCoverKind.Broadleaf or AtlasCoverKind.Conifer)
                (lr, lg, lb) = Toward((lr, lg, lb), cover.Colour, .4 * cover.Density * Clump(x / (double)n, y / (double)n) * (1 - Smooth(1100, 1700, metres)));
            (lr, lg, lb) = Toward((lr, lg, lb), palette.Upland, Smooth(150, 800, metres) * .7);
            (lr, lg, lb) = Toward((lr, lg, lb), palette.Rock, Smooth(1000, 1800, metres) * .8);
            (lr, lg, lb) = Toward((lr, lg, lb), palette.Snow, Smooth(2000, 2400, metres) * .85);
            double grey = (lr + lg + lb) / 3;
            lr = grey + ((lr - grey) * palette.Saturation); lg = grey + ((lg - grey) * palette.Saturation); lb = grey + ((lb - grey) * palette.Saturation);
            double gx = (Height(soft, w, h, x + 1, y) - Height(soft, w, h, x - 1, y)) * Exaggeration / (2 * spacing);
            double gy = (Height(soft, w, h, x, y + 1) - Height(soft, w, h, x, y - 1)) * Exaggeration / (2 * spacing);
            double slope = Math.Atan(Math.Sqrt((gx * gx) + (gy * gy))), aspect = Math.Atan2(-gx, gy);
            double shade = Math.Clamp((flat * Math.Cos(slope)) + (Math.Cos(LightAltitude) * Math.Sin(slope) * Math.Cos(LightAzimuth - aspect)), 0, 1) / flat;
            double lit = (1 + (palette.Shading * (shade - 1))) * paper;
            r[i] = (float)(lr * lit); g[i] = (float)(lg * lit); b[i] = (float)(lb * lit);
        }
        return canvas;
    }

    /// <summary>
    /// Marks one level, whose pixel spans <paramref name="nodes"/> relief nodes along each side: the coast's halo and
    /// ripples, the land's drawn mountains, hills and cover, then rivers, the coast's ink and region borders over them.
    /// </summary>
    private static void Mark(AtlasCanvas canvas, AtlasInput input, AtlasPalette palette, int nodes)
    {
        int w = canvas.Width, h = canvas.Height, n = input.NodesPerPixel, mapWidth = input.Width / n, count = w * h;
        // A level pixel's height is the mean of its nodes', and it is sea where most of its nodes are.
        float[] metres = new float[count];
        bool[] sea = new bool[count];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double sum = 0;
            int flooded = 0;
            for (int dy = 0; dy < nodes; dy++)
            for (int dx = 0; dx < nodes; dx++)
            {
                float m = input.Metres[(((y * nodes) + dy) * input.Width) + (x * nodes) + dx];
                sum += m;
                if (m <= OceanMetres + .01) flooded++;
            }
            metres[(y * w) + x] = (float)(sum / (nodes * nodes));
            sea[(y * w) + x] = flooded * 2 > nodes * nodes;
        }
        int MapCell(double x, double y) => (Math.Clamp((int)(y * nodes / n), 0, (input.Height / n) - 1) * mapWidth) + Math.Clamp((int)(x * nodes / n), 0, mapWidth - 1);
        double perMap = n / (double)nodes;

        // The coast's halo and ripples, on the sea by its distance from land.
        float[] offshore = Distance(sea, w, h, toward: false);
        float[] inland = Distance(sea, w, h, toward: true);
        double ripple = 2.2 + perMap;
        for (int i = 0; i < count; i++)
        {
            if (!sea[i]) continue;
            double d = offshore[i];
            canvas.Paint(i, palette.Halo, Math.Clamp(1 - ((d - 1) / (ripple * .7)), 0, 1) * .55);
            canvas.Paint(i, palette.Ripple, Math.Clamp(1 - (Math.Abs(d - ripple) / .6), 0, 1) * .55);
            canvas.Paint(i, palette.Ripple, Math.Clamp(1 - (Math.Abs(d - (ripple * 1.75)) / .6), 0, 1) * .3);
        }

        // Where rivers run, nothing is drawn standing.
        bool[] wet = new bool[count];
        var strokes = RiverStrokes(input, nodes).ToList();
        foreach ((double x0, double y0, double x1, double y1, double radius, _) in strokes) Cover(wet, w, h, x0, y0, x1, y1, radius + 1.2);

        List<Symbol> symbols = [];
        PlaceRelief(symbols, input, palette, metres, sea, inland, wet, w, h, perMap, MapCell);
        PlaceCover(symbols, input, palette, metres, sea, inland, wet, w, h, perMap, MapCell);
        foreach (Symbol symbol in symbols.OrderBy(symbol => symbol.Y).ThenBy(symbol => symbol.X)) symbol.Draw(canvas, palette);

        foreach ((double x0, double y0, double x1, double y1, double radius, double alpha) in strokes)
            canvas.Stroke(x0, y0, x1, y1, radius, palette.River, alpha, skip: sea);

        // The coast: the sea's edge, inked.
        float[] coast = new float[count];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w) + x;
            if (sea[i] && ((x > 0 && !sea[i - 1]) || (x < w - 1 && !sea[i + 1]) || (y > 0 && !sea[i - w]) || (y < h - 1 && !sea[i + w]))) coast[i] = 1;
        }
        Blur(coast, w, h, 1);
        for (int i = 0; i < count; i++) canvas.Paint(i, palette.Coast, Math.Min(1, coast[i] * 1.6) * .85);

        // Region borders on land, faintly dashed.
        for (int y = 0; y < h - 1; y++)
        for (int x = 0; x < w - 1; x++)
        {
            int i = (y * w) + x;
            if (sea[i] || (x + y) % 6 >= 3) continue;
            int region = input.Region[MapCell(x, y)];
            if (region < 0) continue;
            if (input.Region[MapCell(x + 1, y)] != region || input.Region[MapCell(x, y + 1)] != region) canvas.Paint(i, palette.Border, .45);
        }
    }

    /// <summary>Each river segment at this level: its ends, its radius, and its ink's strength, a stream thinner than a pixel fading rather than thinning.</summary>
    private static IEnumerable<(double X0, double Y0, double X1, double Y1, double Radius, double Alpha)> RiverStrokes(AtlasInput input, int nodes)
    {
        foreach (AtlasRiver river in input.Rivers)
            for (int s = 0; s + 1 < river.X.Length; s++)
            {
                double radius = Math.Clamp(river.HalfWidthMetres[s] / 22, .5, 2.6) / 2 / nodes;
                double alpha = .92 * Math.Min(1, radius / .4);
                yield return (river.X[s] / nodes, river.Y[s] / nodes, river.X[s + 1] / nodes, river.Y[s + 1] / nodes, Math.Max(radius, .4), alpha);
            }
    }

    /// <summary>
    /// Mountains where the ground stands high, more readily in mountain climates, and hills on the uplands below them;
    /// neither in a valley's floor, on a river or at the coast.
    /// </summary>
    private static void PlaceRelief(List<Symbol> symbols, AtlasInput input, AtlasPalette palette, float[] metres, bool[] sea, float[] inland,
        bool[] wet, int w, int h, double perMap, Func<double, double, int> mapCell)
    {
        double size = 8 + (4 * perMap);
        float[] around = (float[])metres.Clone();
        Blur(around, w, h, Math.Max(1, (int)Math.Round(size * .35)));
        foreach ((double x, double y, int seed) in Lattice(w, h, size * .55, size * .32, 1))
        {
            int i = Index(x, y, w, h);
            if (sea[i] || wet[i] || inland[i] < size * .35 || metres[i] < around[i] - 30) continue;
            bool mountainClimate = input.Climate[mapCell(x, y)] == "Mountain";
            double high = Smooth(mountainClimate ? 750 : 950, mountainClimate ? 1400 : 1600, metres[i]);
            if (high > 0 && Hash01(seed, 1) < high * (.55 + (.45 * high)))
            {
                double prominence = Math.Clamp((metres[i] - around[i]) / 120, 0, 1);
                double width = size * (.6 + (.3 * high) + (.3 * prominence) + (.2 * Hash01(seed, 2)));
                if (Clear(x - (width / 2), y) && Clear(x + (width / 2), y) && !wet[Index(x, y - (width * .4), w, h)])
                    symbols.Add(new(x, y, width, Kind.Mountain, palette.Rock, metres[i] > 1900, seed));
                continue;
            }
            double hill = Smooth(450, 900, metres[i]) * (1 - (high * 2));
            if (hill > 0 && Hash01(seed, 3) < hill * .22 && ((int)(x / (size * .62)) + (int)(y / (size * .38))) % 2 == 0)
                symbols.Add(new(x, y, size * (.55 + (.15 * Hash01(seed, 4))), Kind.Hill, palette.Upland, false, seed));
        }

        bool Clear(double x, double y)
        {
            int i = Index(x, y, w, h);
            return !sea[i] && !wet[i];
        }
    }

    /// <summary>Each climate's cover in clumps, thinning as the ground climbs and giving way to mountains; none on a river or at the coast.</summary>
    private static void PlaceCover(List<Symbol> symbols, AtlasInput input, AtlasPalette palette, float[] metres, bool[] sea, float[] inland,
        bool[] wet, int w, int h, double perMap, Func<double, double, int> mapCell)
    {
        double radius = 1.2 + (.5 * perMap);
        foreach ((double x, double y, int seed) in Lattice(w, h, radius * 2, radius * 1.6, 2))
        {
            int i = Index(x, y, w, h);
            if (sea[i] || wet[i] || inland[i] < (radius * 1.2) + .5) continue;
            string climate = input.Climate[mapCell(x, y)];
            AtlasCover cover = CoverOf(palette, climate);
            if (cover.Kind == AtlasCoverKind.None) continue;
            // Clumps are laid in map pixels, so a forest stands in the same place at every level.
            double mx = x / perMap, my = y / perMap;
            // Trees stand in clumps; marsh and dunes spread more evenly over their ground.
            double clump = cover.Kind is AtlasCoverKind.Broadleaf or AtlasCoverKind.Conifer ? Clump(mx, my) : Smooth(-.5, .2, Noise(mx, my, 15, 13));
            double climb = 1 - Smooth(1100, 1700, metres[i]);
            double room = 1 - Smooth(750, 1000, metres[i]) * (climate == "Mountain" ? 0 : 1);
            if (Hash01(seed, 5) >= Math.Min(1, cover.Density * 1.3) * clump * climb * Math.Max(room, .35)) continue;
            Kind kind = cover.Kind switch
            {
                AtlasCoverKind.Broadleaf => Kind.Broadleaf,
                AtlasCoverKind.Conifer => Kind.Conifer,
                AtlasCoverKind.Marsh => Kind.Marsh,
                _ => Kind.Dunes,
            };
            // Marsh and dunes are sparser marks than trees.
            if (kind is Kind.Marsh or Kind.Dunes && (Hash01(seed, 6) < .55 || ((int)(x / (radius * 2)) + (int)(y / (radius * 1.6))) % 2 != 0)) continue;
            symbols.Add(new(x, y, radius * (.85 + (.3 * Hash01(seed, 7))), kind, cover.Colour, false, seed));
        }
    }

    /// <summary>Where cover stands thick, in [0, 1], by map pixel: clumps of a few dozen pixels with ragged edges.</summary>
    private static double Clump(double mapX, double mapY) => Smooth(-.12, .14, (Noise(mapX, mapY, 22, 11) * .7) + (Noise(mapX, mapY, 7, 12) * .3));

    /// <summary>A staggered lattice of points over a level, each moved about its cell by a hash, with a seed of its own.</summary>
    private static IEnumerable<(double X, double Y, int Seed)> Lattice(int w, int h, double across, double down, int salt)
    {
        int columns = (int)Math.Ceiling(w / across), rows = (int)Math.Ceiling(h / down);
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            int seed = (int)(Hash01(column + (salt * 7919), row) * int.MaxValue);
            double x = (column + .5 + (row % 2 == 0 ? 0 : .5) + ((Hash01(seed, 21) - .5) * .7)) * across;
            double y = (row + .5 + ((Hash01(seed, 22) - .5) * .7)) * down;
            if (x < 0 || y < 0 || x >= w || y >= h) continue;
            yield return (x, y, seed);
        }
    }

    private static int Index(double x, double y, int w, int h) => (Math.Clamp((int)y, 0, h - 1) * w) + Math.Clamp((int)x, 0, w - 1);

    /// <summary>Marks every pixel within <paramref name="radius"/> of a segment.</summary>
    private static void Cover(bool[] mask, int w, int h, double x0, double y0, double x1, double y1, double radius)
    {
        int left = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - radius)), right = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(x0, x1) + radius));
        int top = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - radius)), bottom = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(y0, y1) + radius));
        double dx = x1 - x0, dy = y1 - y0, length = (dx * dx) + (dy * dy);
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            double px = x + .5 - x0, py = y + .5 - y0;
            double t = length == 0 ? 0 : Math.Clamp(((px * dx) + (py * dy)) / length, 0, 1);
            double ex = px - (t * dx), ey = py - (t * dy);
            if ((ex * ex) + (ey * ey) <= radius * radius) mask[(y * w) + x] = true;
        }
    }

    private enum Kind { Mountain, Hill, Broadleaf, Conifer, Marsh, Dunes }

    /// <summary>One drawn mark, standing on its base point (<see cref="X"/>, <see cref="Y"/>); marks are drawn from the north down, so the nearer covers the farther.</summary>
    private readonly record struct Symbol(double X, double Y, double Size, Kind Kind, Rgb Colour, bool Snow, int Seed)
    {
        public void Draw(AtlasCanvas canvas, AtlasPalette palette)
        {
            switch (Kind)
            {
                case Kind.Mountain: Mountain(canvas, palette); break;
                case Kind.Hill: Hill(canvas, palette); break;
                case Kind.Broadleaf: Broadleaf(canvas, palette); break;
                case Kind.Conifer: Conifer(canvas, palette); break;
                case Kind.Marsh: Marsh(canvas); break;
                case Kind.Dunes: Dunes(canvas); break;
            }
        }

        /// <summary>A mountain: a peak, often with a lower shoulder standing behind it to one side.</summary>
        private void Mountain(AtlasCanvas canvas, AtlasPalette palette)
        {
            double height = Size * (.62 + (.3 * Hash01(Seed, 8)));
            if (Hash01(Seed, 10) < .6)
            {
                double side = Hash01(Seed, 11) < .5 ? -1 : 1;
                Peak(canvas, palette, X + (side * Size * .34), Y - (Size * .04), Size * .62, height * (.5 + (.15 * Hash01(Seed, 12))), Hash01(Seed, 13), false);
            }
            Peak(canvas, palette, X, Y, Size, height, Hash01(Seed, 9), Snow);
        }

        /// <summary>A peak: its face to the light pale, its far face dark and hatched, outlined in ink, capped with snow up high.</summary>
        private static void Peak(AtlasCanvas canvas, AtlasPalette palette, double x, double y, double width, double height, double lean, bool snow)
        {
            (double X, double Y) apex = (x + (width * (lean - .5) * .22), y - height);
            (double X, double Y) left = (x - (width / 2), y), right = (x + (width / 2), y), foot = (apex.X + (width * .13), y);
            canvas.Fill([left, apex, foot], palette.RockLit, 1);
            canvas.Fill([apex, right, foot], palette.RockShade, 1);
            if (snow)
            {
                canvas.Fill([apex, Lerp(apex, left, .32), Lerp(apex, foot, .3)], palette.Snow, .95);
                canvas.Fill([apex, Lerp(apex, foot, .3), Lerp(apex, right, .26)], Blend(palette.Snow, palette.RockShade, .35), .9);
            }
            double ink = Math.Max(.36, width * .032);
            for (int k = 1; k <= 4; k++)
            {
                (double X, double Y) from = Lerp(apex, foot, .1 + (.18 * k)), to = Lerp(apex, right, .2 + (.18 * k));
                canvas.Stroke(from.X, from.Y, Mix(from.X, to.X, .7), Mix(from.Y, to.Y, .7), ink * .55, palette.Ink, .42);
            }
            canvas.Stroke(apex.X, apex.Y, Mix(apex.X, foot.X, .7), Mix(apex.Y, foot.Y, .7), ink * .7, palette.Ink, .5);
            canvas.Stroke(left.X, left.Y, apex.X, apex.Y, ink, palette.Ink, .9);
            canvas.Stroke(apex.X, apex.Y, right.X, right.Y, ink, palette.Ink, .9);
        }

        /// <summary>A hill: a low dome of the upland's colour, its rim inked and its far side hatched.</summary>
        private void Hill(AtlasCanvas canvas, AtlasPalette palette)
        {
            double half = Size / 2, rise = Size * .36;
            Span<(double X, double Y)> dome = stackalloc (double, double)[11];
            for (int k = 0; k <= 10; k++) dome[k] = (X - (Math.Cos(k * Math.PI / 10) * half), Y - (Math.Sin(k * Math.PI / 10) * rise));
            int under = Index(X, Y - (rise / 2), canvas.Width, canvas.Height);
            (double r, double g, double b) = canvas[under];
            canvas.Fill(dome, ((byte)Math.Min(255, r * 1.06), (byte)Math.Min(255, g * 1.06), (byte)Math.Min(255, b * 1.04)), 1);
            double ink = Math.Max(.34, Size * .03);
            for (int k = 1; k < 10; k++) canvas.Stroke(dome[k - 1].X, dome[k - 1].Y, dome[k].X, dome[k].Y, ink, palette.Ink, .7);
            for (int k = 6; k <= 8; k++)
                canvas.Stroke(dome[k].X, dome[k].Y + (ink * .5), dome[k].X - (Size * .08), dome[k].Y + (rise * .45), ink * .6, palette.Ink, .4);
        }

        /// <summary>A broadleaf tree: a round crown lit from the north-west over the shadow it casts.</summary>
        private void Broadleaf(AtlasCanvas canvas, AtlasPalette palette)
        {
            double r = Size, cy = Y - r;
            canvas.Disc(X + (r * .35), cy + (r * .45), r, palette.Shadow, .5);
            canvas.Disc(X, cy, r, Colour, 1);
            canvas.Disc(X - (r * .3), cy - (r * .32), r * .55, Blend(Colour, (214, 226, 160), .3), .75);
        }

        /// <summary>A conifer: a narrow spire, its sunward half lit.</summary>
        private void Conifer(AtlasCanvas canvas, AtlasPalette palette)
        {
            double r = Size * .85;
            (double X, double Y) top = (X, Y - (Size * 2.5)), left = (X - r, Y), right = (X + r, Y), foot = (X, Y);
            canvas.Fill([(X + (r * .5), Y + (r * .3)), (X + (r * 1.4), Y + (r * .3)), (X + (r * .4), Y - (Size * 1.6))], palette.Shadow, .35);
            canvas.Fill([top, left, foot], Blend(Colour, (214, 226, 160), .22), 1);
            canvas.Fill([top, foot, right], Blend(Colour, palette.Shadow, .3), 1);
        }

        /// <summary>A tuft of marsh grass on a line of water.</summary>
        private void Marsh(AtlasCanvas canvas)
        {
            double s = Size, ink = Math.Max(.32, s * .14);
            canvas.Stroke(X - (s * 1.3), Y, X + (s * 1.3), Y, ink, Colour, .7);
            canvas.Stroke(X - (s * .5), Y, X - (s * .8), Y - (s * 1.1), ink * .8, Colour, .75);
            canvas.Stroke(X, Y, X, Y - (s * 1.5), ink * .8, Colour, .75);
            canvas.Stroke(X + (s * .5), Y, X + (s * .8), Y - (s * 1.1), ink * .8, Colour, .75);
        }

        /// <summary>A dune's crest: a long windward rise to a short lee fall.</summary>
        private void Dunes(AtlasCanvas canvas)
        {
            double s = Size * 2.3, ink = Math.Max(.36, Size * .16);
            (double X, double Y) a = (X - s, Y), crest = (X + (s * .25), Y - (s * .35)), b = (X + s, Y + (s * .05));
            canvas.Stroke(a.X, a.Y, Mix(a.X, crest.X, .5), Mix(a.Y, crest.Y, .5) - (s * .06), ink, Colour, .62);
            canvas.Stroke(Mix(a.X, crest.X, .5), Mix(a.Y, crest.Y, .5) - (s * .06), crest.X, crest.Y, ink, Colour, .62);
            canvas.Stroke(crest.X, crest.Y, b.X, b.Y, ink, Colour, .62);
        }

        private static (double X, double Y) Lerp((double X, double Y) a, (double X, double Y) b, double t) => (Mix(a.X, b.X, t), Mix(a.Y, b.Y, t));

        private static Rgb Blend(Rgb a, Rgb b, double t) => ((byte)Mix(a.R, b.R, t), (byte)Mix(a.G, b.G, t), (byte)Mix(a.B, b.B, t));
    }

    /// <summary>
    /// Builds the atlas's input from the published relief and rivers and the import's climate and politic grids. Each
    /// grid's first column is a sentinel, so its column x is world-map pixel x - 1.
    /// </summary>
    public static AtlasInput Input(ReadOnlySpan<byte> relief, ReadOnlySpan<byte> rivers, DaggerfallClimateGrid climate, DaggerfallPoliticGrid politic)
    {
        ArgumentNullException.ThrowIfNull(climate);
        ArgumentNullException.ThrowIfNull(politic);
        (int width, int height, float[] metres) = ReadRelief(relief);
        const int mapWidth = WoodsReader.MapWidth, mapHeight = WoodsReader.MapHeight;
        if (width % mapWidth != 0 || height != width / mapWidth * mapHeight)
            throw new InvalidDataException($"A {width} by {height} relief is no whole number of nodes to the {mapWidth} by {mapHeight} world map.");
        int nodes = width / mapWidth;
        Dictionary<int, string> climates = climate.Values.ToDictionary(value => value.Value, value => value.Name);
        Dictionary<int, int> regions = politic.Values.ToDictionary(value => value.Value, value => value.Region);
        string[] climateNames = new string[mapWidth * mapHeight];
        int[] regionIds = new int[mapWidth * mapHeight];
        Array.Fill(climateNames, "Ocean");
        Array.Fill(regionIds, -1);
        Spread(climate.Rows, (i, value) => climateNames[i] = climates.GetValueOrDefault(value, "Woodlands"));
        Spread(politic.Rows, (i, value) => regionIds[i] = regions.GetValueOrDefault(value, -1));
        return new(width, height, metres, nodes, climateNames, regionIds, ReadRivers(rivers, nodes));

        static void Spread(IReadOnlyList<DaggerfallGridRow> rows, Action<int, int> set)
        {
            foreach (DaggerfallGridRow row in rows)
            {
                int x = -1;
                foreach (DaggerfallGridRun run in row.Runs)
                {
                    for (int i = Math.Max(x, 0); i < Math.Min(x + run.Count, mapWidth); i++) set((row.Y * mapWidth) + i, run.Value);
                    x += run.Count;
                }
            }
        }
    }

    /// <summary>Reads the published relief: a magic, both lattices' sizes, then heights row by row in eighths of a metre.</summary>
    public static (int Width, int Height, float[] Metres) ReadRelief(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 20 || !bytes[..4].SequenceEqual("RLF2"u8)) throw new InvalidDataException("The relief is not an RLF2 file.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), height = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        float[] metres = new float[checked(width * height)];
        if (bytes.Length < 20 + (metres.Length * 2)) throw new InvalidDataException("The relief is shorter than its lattice.");
        for (int i = 0; i < metres.Length; i++) metres[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(20 + (i * 2))..]) / (float)DaggerfallRelief.StepsPerMetre;
        return (width, height, metres);
    }

    /// <summary>Reads the published rivers onto a lattice of <paramref name="nodesPerPixel"/> nodes to the world-map pixel.</summary>
    public static IReadOnlyList<AtlasRiver> ReadRivers(ReadOnlySpan<byte> bytes, int nodesPerPixel)
    {
        if (bytes.Length < 8 || !bytes[..4].SequenceEqual("RVR1"u8)) throw new InvalidDataException("The rivers are not an RVR1 file.");
        int courses = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        int at = 8 + (courses * 4);
        List<AtlasRiver> rivers = [];
        for (int c = 0; c < courses; c++)
        {
            int stations = BinaryPrimitives.ReadInt32LittleEndian(bytes[(8 + (c * 4))..]);
            float[] x = new float[stations], y = new float[stations], half = new float[stations];
            for (int s = 0; s < stations; s++, at += 16)
            {
                x[s] = (float)(BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]) / PixelMetres * nodesPerPixel);
                y[s] = (float)(BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 4)..]) / PixelMetres * nodesPerPixel);
                half[s] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 12)..]);
            }
            rivers.Add(new(x, y, half));
        }
        return rivers;
    }

    private static float Height(float[] metres, int w, int h, int x, int y) => metres[(Math.Clamp(y, 0, h - 1) * w) + Math.Clamp(x, 0, w - 1)];

    private static Rgb ClimateColour(AtlasPalette palette, string climate) =>
        palette.Climates.TryGetValue(climate, out Rgb tint) ? tint : palette.Climates["Woodlands"];

    private static AtlasCover CoverOf(AtlasPalette palette, string climate) =>
        palette.Covers.TryGetValue(climate, out AtlasCover? cover) ? cover : palette.Covers["Woodlands"];

    private static double Mix(double a, double b, double t) => a + ((b - a) * t);

    private static (double R, double G, double B) Toward((double R, double G, double B) colour, Rgb to, double t) =>
        (Mix(colour.R, to.R, t), Mix(colour.G, to.G, t), Mix(colour.B, to.B, t));

    /// <summary>0 below <paramref name="from"/>, 1 above <paramref name="to"/>, eased between.</summary>
    private static double Smooth(double from, double to, double value)
    {
        double t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - (2 * t));
    }

    /// <summary>A soft parchment mottle in [-1, 1]: hashed value noise on a 12-pixel cell, smoothly interpolated.</summary>
    private static double Mottle(int x, int y) => Noise(x, y, 12, 0);

    /// <summary>Hashed value noise in [-1, 1] on a square cell, smoothly interpolated; <paramref name="salt"/> makes another field.</summary>
    private static double Noise(double x, double y, double cell, int salt)
    {
        double fx = x / cell, fy = y / cell;
        int cx = (int)Math.Floor(fx), cy = (int)Math.Floor(fy);
        fx -= cx; fy -= cy;
        fx = fx * fx * (3 - (2 * fx)); fy = fy * fy * (3 - (2 * fy));
        cx += salt * 7919;
        double top = Mix(Hash(cx, cy), Hash(cx + 1, cy), fx), bottom = Mix(Hash(cx, cy + 1), Hash(cx + 1, cy + 1), fx);
        return Mix(top, bottom, fy);
    }

    private static double Hash01(int x, int y) => (Hash(x, y) + 1) / 2;

    private static double Hash(int x, int y)
    {
        uint h = unchecked(((uint)x * 374761393u) + ((uint)y * 668265263u) + 0x9E3779B9u);
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        h ^= h >> 16;
        return (h / (double)uint.MaxValue * 2) - 1;
    }

    /// <summary>
    /// Each pixel's distance in pixels to the nearest sea pixel (<paramref name="toward"/>) or the nearest land pixel
    /// (otherwise), by the exact two-pass squared-distance transform.
    /// </summary>
    private static float[] Distance(bool[] sea, int w, int h, bool toward)
    {
        const double far = 1e12;
        double[] d = new double[sea.Length];
        for (int i = 0; i < d.Length; i++) d[i] = sea[i] == toward ? 0 : far;
        double[] line = new double[Math.Max(w, h)], output = new double[Math.Max(w, h)];
        int[] hull = new int[Math.Max(w, h)];
        double[] bounds = new double[Math.Max(w, h) + 1];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++) line[y] = d[(y * w) + x];
            Transform(line, output, hull, bounds, h);
            for (int y = 0; y < h; y++) d[(y * w) + x] = output[y];
        }
        for (int y = 0; y < h; y++)
        {
            Array.Copy(d, y * w, line, 0, w);
            Transform(line, output, hull, bounds, w);
            Array.Copy(output, 0, d, y * w, w);
        }
        float[] distance = new float[d.Length];
        for (int i = 0; i < d.Length; i++) distance[i] = (float)Math.Sqrt(d[i]);
        return distance;

        // The lower envelope of the parabolas rooted at each sample, read back along the line.
        static void Transform(double[] f, double[] result, int[] v, double[] z, int length)
        {
            int k = 0;
            v[0] = 0; z[0] = double.NegativeInfinity; z[1] = double.PositiveInfinity;
            for (int q = 1; q < length; q++)
            {
                double s = Meet(f, q, v[k]);
                while (s <= z[k]) s = Meet(f, q, v[--k]);
                k++; v[k] = q; z[k] = s; z[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < length; q++)
            {
                while (z[k + 1] < q) k++;
                result[q] = ((q - v[k]) * (double)(q - v[k])) + f[v[k]];
            }
        }

        static double Meet(double[] f, int q, int p) => (f[q] + ((double)q * q) - (f[p] + ((double)p * p))) / (2.0 * (q - p));
    }


    /// <summary>A box blur of the given radius, three passes each way, which approaches a Gaussian.</summary>
    private static void Blur(float[] values, int w, int h, int radius)
    {
        float[] scratch = new float[values.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            for (int y = 0; y < h; y++)
            {
                double sum = 0;
                for (int x = -radius; x <= radius; x++) sum += values[(y * w) + Math.Clamp(x, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    scratch[(y * w) + x] = (float)(sum / ((2 * radius) + 1));
                    sum += values[(y * w) + Math.Min(w - 1, x + radius + 1)] - values[(y * w) + Math.Max(0, x - radius)];
                }
            }
            for (int x = 0; x < w; x++)
            {
                double sum = 0;
                for (int y = -radius; y <= radius; y++) sum += scratch[(Math.Clamp(y, 0, h - 1) * w) + x];
                for (int y = 0; y < h; y++)
                {
                    values[(y * w) + x] = (float)(sum / ((2 * radius) + 1));
                    sum += scratch[(Math.Min(h - 1, y + radius + 1) * w) + x] - scratch[(Math.Max(0, y - radius) * w) + x];
                }
            }
        }
    }

    /// <summary>One tile, padded past the level's edge with the edge's own pixels so a partial tile scales without a seam.</summary>
    private static byte[] Cut(int width, int height, byte[] rgba, int column, int row)
    {
        byte[] tile = new byte[TileSize * TileSize * 4];
        for (int y = 0; y < TileSize; y++)
        for (int x = 0; x < TileSize; x++)
        {
            int sx = Math.Min(width - 1, (column * TileSize) + x), sy = Math.Min(height - 1, (row * TileSize) + y);
            Array.Copy(rgba, ((sy * width) + sx) * 4, tile, ((y * TileSize) + x) * 4, 4);
        }
        return tile;
    }
}
