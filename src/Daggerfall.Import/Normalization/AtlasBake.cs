using System.Buffers.Binary;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

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

/// <summary>An atlas palette: how land of each climate, high ground, the sea, coast, rivers and borders are coloured.</summary>
public sealed record AtlasPalette(
    IReadOnlyDictionary<string, (byte R, byte G, byte B)> Climates, (byte R, byte G, byte B) Peak, (byte R, byte G, byte B) Shallows,
    (byte R, byte G, byte B) Deeps, (byte R, byte G, byte B) Coast, (byte R, byte G, byte B) River, (byte R, byte G, byte B) Border,
    double Shading, double Paper, double Saturation)
{
    /// <summary>
    /// The muted atlas: olive and stone land on parchment, a pale sea darkening offshore, inked coasts and rivers.
    /// </summary>
    public static AtlasPalette Atlas { get; } = new(
        new Dictionary<string, (byte, byte, byte)>(StringComparer.Ordinal)
        {
            ["Woodlands"] = (176, 178, 128), ["HauntedWoodlands"] = (160, 164, 124), ["Mountain"] = (182, 170, 146),
            ["MountainWoods"] = (160, 166, 122), ["Rainforest"] = (142, 166, 112), ["Subtropical"] = (196, 184, 134),
            ["Swamp"] = (146, 154, 118), ["Desert"] = (220, 198, 146), ["Desert2"] = (214, 190, 140), ["Ocean"] = (176, 178, 128),
        },
        Peak: (226, 218, 204), Shallows: (188, 206, 204), Deeps: (150, 178, 184), Coast: (70, 84, 90), River: (64, 98, 124),
        Border: (120, 80, 90), Shading: .55, Paper: .035, Saturation: .9);
}

/// <summary>One square tile of one level of the atlas, as RGBA8 rows.</summary>
public sealed record AtlasTile(int Level, int Column, int Row, int Size, byte[] Rgba);

/// <summary>One level of the atlas: its size in pixels and how many tiles it takes.</summary>
public sealed record AtlasLevel(int Level, int Width, int Height, int Columns, int Rows);

/// <summary>
/// Draws the travel atlas from the land itself: each climate's colour lightening with height, the relief shaded from the
/// north-west, the sea darkening away from land, inked coasts and rivers whose width follows their water, and faint
/// dashed region borders, over a parchment mottle. The finest level is the relief's own lattice, one pixel to a node;
/// each coarser level halves it. Names, places and routes are drawn by the page over the tiles, not baked in. All its
/// noise comes from coordinate hashes, so an atlas depends on its input alone.
/// </summary>
public static class AtlasBake
{
    public const int TileSize = 256;
    /// <summary>How many levels the atlas halves its finest level into, finest last.</summary>
    public const int LevelCount = 3;
    private const double OceanMetres = 27.2 * 1.5, PixelMetres = 819.2, HeightRange = 1400, Exaggeration = 2.5;
    private const double LightAzimuth = 315 * Math.PI / 180, LightAltitude = 40 * Math.PI / 180;

    /// <summary>The levels an atlas of the input's size takes, coarsest first.</summary>
    public static IReadOnlyList<AtlasLevel> Levels(int width, int height) => [.. Enumerable.Range(0, LevelCount).Select(level =>
    {
        int w = width >> (LevelCount - 1 - level), h = height >> (LevelCount - 1 - level);
        return new AtlasLevel(level, w, h, (w + TileSize - 1) / TileSize, (h + TileSize - 1) / TileSize);
    })];

    /// <summary>Draws the atlas at its finest level and halves it into the coarser ones, then cuts every level into tiles.</summary>
    public static IEnumerable<AtlasTile> Tiles(AtlasInput input, AtlasPalette palette)
    {
        byte[] finest = Draw(input, palette);
        (int width, int height, byte[] rgba) level = (input.Width, input.Height, finest);
        List<(int Width, int Height, byte[] Rgba)> levels = [level];
        for (int i = 1; i < LevelCount; i++) levels.Insert(0, level = Halve(level.width, level.height, level.rgba));
        for (int index = 0; index < levels.Count; index++)
        {
            (int width, int height, byte[] rgba) = levels[index];
            for (int row = 0; row * TileSize < height; row++)
            for (int column = 0; column * TileSize < width; column++)
                yield return new(index, column, row, TileSize, Cut(width, height, rgba, column, row));
        }
    }

    /// <summary>The atlas at its finest level, one pixel to a relief node, as RGBA8 rows.</summary>
    public static byte[] Draw(AtlasInput input, AtlasPalette palette)
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

        // Land: each climate's colour, met softly where climates meet, lightening and greying with height.
        float[] r = new float[count], g = new float[count], b = new float[count];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            string climate = input.Climate[(y / n * mapWidth) + (x / n)];
            (byte cr, byte cg, byte cb) = palette.Climates.TryGetValue(climate, out var tint) ? tint : palette.Climates["Woodlands"];
            int i = (y * w) + x;
            r[i] = cr; g[i] = cg; b[i] = cb;
        }
        foreach (float[] channel in new[] { r, g, b }) Blur(channel, w, h, 3);

        double spacing = PixelMetres / n, flat = Math.Sin(LightAltitude);
        float[] depth = SeaDistance(sea, w, h);
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
            double hi = Math.Clamp((input.Metres[i] - OceanMetres) / HeightRange, 0, 1) * .35;
            double lr = Mix(r[i], palette.Peak.R, hi), lg = Mix(g[i], palette.Peak.G, hi), lb = Mix(b[i], palette.Peak.B, hi);
            double grey = (lr + lg + lb) / 3;
            lr = grey + ((lr - grey) * palette.Saturation); lg = grey + ((lg - grey) * palette.Saturation); lb = grey + ((lb - grey) * palette.Saturation);
            double gx = (Height(input, x + 1, y) - Height(input, x - 1, y)) * Exaggeration / (2 * spacing);
            double gy = (Height(input, x, y + 1) - Height(input, x, y - 1)) * Exaggeration / (2 * spacing);
            double slope = Math.Atan(Math.Sqrt((gx * gx) + (gy * gy))), aspect = Math.Atan2(-gx, gy);
            double shade = Math.Clamp((flat * Math.Cos(slope)) + (Math.Cos(LightAltitude) * Math.Sin(slope) * Math.Cos(LightAzimuth - aspect)), 0, 1) / flat;
            double lit = (1 + (palette.Shading * (shade - 1))) * paper;
            r[i] = (float)(lr * lit); g[i] = (float)(lg * lit); b[i] = (float)(lb * lit);
        }

        // Coast: the sea's edge, inked.
        float[] coast = new float[count];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w) + x;
            if (sea[i] && ((x > 0 && !sea[i - 1]) || (x < w - 1 && !sea[i + 1]) || (y > 0 && !sea[i - w]) || (y < h - 1 && !sea[i + w]))) coast[i] = 1;
        }
        Blur(coast, w, h, 1);
        for (int i = 0; i < count; i++) Paint(r, g, b, i, palette.Coast, Math.Min(1, coast[i] * 1.6) * .85);

        // Region borders on land, faintly dashed.
        for (int y = 0; y < h - 1; y++)
        for (int x = 0; x < w - 1; x++)
        {
            int i = (y * w) + x;
            if (sea[i] || (x + y) % 6 >= 3) continue;
            int region = input.Region[(y / n * mapWidth) + (x / n)];
            if (region < 0) continue;
            int east = input.Region[(y / n * mapWidth) + ((x + 1) / n)], south = input.Region[((y + 1) / n * mapWidth) + (x / n)];
            if (east != region || south != region) Paint(r, g, b, i, palette.Border, .45);
        }

        // Rivers: width follows the water, as the walked river's does.
        foreach (AtlasRiver river in input.Rivers)
            for (int s = 0; s + 1 < river.X.Length; s++)
            {
                double radius = Math.Clamp(river.HalfWidthMetres[s] / 22, .5, 2.6) / 2;
                Stroke(r, g, b, sea, w, h, river.X[s], river.Y[s], river.X[s + 1], river.Y[s + 1], radius, palette.River, .92);
            }

        byte[] rgba = new byte[count * 4];
        for (int i = 0; i < count; i++)
        {
            rgba[i * 4] = (byte)Math.Clamp(Math.Round(r[i]), 0, 255);
            rgba[(i * 4) + 1] = (byte)Math.Clamp(Math.Round(g[i]), 0, 255);
            rgba[(i * 4) + 2] = (byte)Math.Clamp(Math.Round(b[i]), 0, 255);
            rgba[(i * 4) + 3] = 255;
        }
        return rgba;
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

    private static float Height(AtlasInput input, int x, int y) =>
        input.Metres[(Math.Clamp(y, 0, input.Height - 1) * input.Width) + Math.Clamp(x, 0, input.Width - 1)];

    private static double Mix(double a, double b, double t) => a + ((b - a) * t);

    private static void Paint(float[] r, float[] g, float[] b, int i, (byte R, byte G, byte B) ink, double cover)
    {
        if (cover <= 0) return;
        r[i] = (float)Mix(r[i], ink.R, cover); g[i] = (float)Mix(g[i], ink.G, cover); b[i] = (float)Mix(b[i], ink.B, cover);
    }

    /// <summary>An antialiased round-capped line, on land only: a river that reaches the sea ends at the coast's ink.</summary>
    private static void Stroke(float[] r, float[] g, float[] b, bool[] sea, int w, int h, double x0, double y0, double x1, double y1,
        double radius, (byte R, byte G, byte B) ink, double alpha)
    {
        int left = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - radius - 1)), right = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(x0, x1) + radius + 1));
        int top = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - radius - 1)), bottom = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(y0, y1) + radius + 1));
        double dx = x1 - x0, dy = y1 - y0, length = (dx * dx) + (dy * dy);
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            int i = (y * w) + x;
            if (sea[i]) continue;
            double px = x + .5 - x0, py = y + .5 - y0;
            double t = length == 0 ? 0 : Math.Clamp(((px * dx) + (py * dy)) / length, 0, 1);
            double ex = px - (t * dx), ey = py - (t * dy);
            double cover = Math.Clamp(radius + .5 - Math.Sqrt((ex * ex) + (ey * ey)), 0, 1) * alpha;
            Paint(r, g, b, i, ink, cover);
        }
    }

    /// <summary>A soft parchment mottle in [-1, 1]: hashed value noise on a 12-pixel cell, smoothly interpolated.</summary>
    private static double Mottle(int x, int y)
    {
        const int cell = 12;
        int cx = x / cell, cy = y / cell;
        double fx = (x % cell) / (double)cell, fy = (y % cell) / (double)cell;
        fx = fx * fx * (3 - (2 * fx)); fy = fy * fy * (3 - (2 * fy));
        double top = Mix(Hash(cx, cy), Hash(cx + 1, cy), fx), bottom = Mix(Hash(cx, cy + 1), Hash(cx + 1, cy + 1), fx);
        return Mix(top, bottom, fy);
    }

    private static double Hash(int x, int y)
    {
        uint h = unchecked(((uint)x * 374761393u) + ((uint)y * 668265263u) + 0x9E3779B9u);
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        h ^= h >> 16;
        return (h / (double)uint.MaxValue * 2) - 1;
    }

    /// <summary>Each sea node's distance in nodes to the nearest land, by a two-pass chamfer.</summary>
    private static float[] SeaDistance(bool[] sea, int w, int h)
    {
        const float straight = 1, diagonal = 1.4142135f;
        float[] d = new float[sea.Length];
        for (int i = 0; i < d.Length; i++) d[i] = sea[i] ? float.MaxValue / 2 : 0;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w) + x;
            if (x > 0) d[i] = Math.Min(d[i], d[i - 1] + straight);
            if (y > 0) d[i] = Math.Min(d[i], d[i - w] + straight);
            if (x > 0 && y > 0) d[i] = Math.Min(d[i], d[i - w - 1] + diagonal);
            if (x < w - 1 && y > 0) d[i] = Math.Min(d[i], d[i - w + 1] + diagonal);
        }
        for (int y = h - 1; y >= 0; y--)
        for (int x = w - 1; x >= 0; x--)
        {
            int i = (y * w) + x;
            if (x < w - 1) d[i] = Math.Min(d[i], d[i + 1] + straight);
            if (y < h - 1) d[i] = Math.Min(d[i], d[i + w] + straight);
            if (x < w - 1 && y < h - 1) d[i] = Math.Min(d[i], d[i + w + 1] + diagonal);
            if (x > 0 && y < h - 1) d[i] = Math.Min(d[i], d[i + w - 1] + diagonal);
        }
        return d;
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

    private static (int Width, int Height, byte[] Rgba) Halve(int width, int height, byte[] rgba)
    {
        int w = width / 2, h = height / 2;
        byte[] half = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        for (int c = 0; c < 4; c++)
        {
            int sum = rgba[((((2 * y) * width) + (2 * x)) * 4) + c] + rgba[((((2 * y) * width) + (2 * x) + 1) * 4) + c]
                + rgba[(((((2 * y) + 1) * width) + (2 * x)) * 4) + c] + rgba[(((((2 * y) + 1) * width) + (2 * x) + 1) * 4) + c];
            half[(((y * w) + x) * 4) + c] = (byte)((sum + 2) / 4);
        }
        return (w, h, half);
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
