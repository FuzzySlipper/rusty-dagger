using Rusty.Engine;
using Rusty.Engine.Heightfields;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The exterior ground, as the Engine's heightfield rings sample it on the streamer's workers: one function for what the
/// ground is at a point. Near ground (<see cref="SampleNear"/>) draws the walked surface itself, levelled under locations
/// and painted from their tiles; far ground (<see cref="Sample"/>) draws the same surface past the walked window. Coordinates
/// are x east and z south in metres, map pixel (X, Y) spanning x from X and z back from Y, in
/// <see cref="DaggerfallExteriorCellResidency.CellSize"/> steps. Far ground above its own valley floor is lifted with
/// distance: the map's ranges stand a degree or two over the skyline at true height, where the classic's painted
/// ones stood near thirty. The floor is fixed on the map, so a player climbing changes nothing out on the horizon: a
/// lift measured from the player would move every tile at once, and tiles sampled either side of the change tear the
/// ranges until they are all sampled again. It reads only the terrain and climate, which nothing changes, and the
/// latest <see cref="Frame"/>, and calls no Engine service.
/// </summary>
internal sealed class DaggerfallHorizonGround : IHeightfieldSampler
{
    /// <summary>Cells along a tile's side: the finest ring's tile is one map pixel, each coarser tile four of the last.</summary>
    internal const uint TileCells = 16;
    internal const double FineCellMetres = DaggerfallTerrainSurfaceBuilder.HorizontalSize / TileCells;
    internal const double MiddleCellMetres = FineCellMetres * 4, CoarseCellMetres = FineCellMetres * 16;
    internal const double FineReachMetres = 8_192, MiddleReachMetres = 49_152, CoarseReachMetres = 204_800;
    private const double Spacing = DaggerfallTerrainSurfaceBuilder.SampleSpacing;
    /// <summary>Near ground's finest cells, which the collider shares.</summary>
    internal const double FinestNearCellMetres = Spacing / 2;
    private const int TileDimension = DaggerfallTerrainSurfaceBuilder.SampleDimension - 1;
    private const double OceanMetres = DaggerfallTerrainSurfaceBuilder.OceanElevation * DaggerfallTerrainSurfaceBuilder.TerrainScale;
    /// <summary>Ground this close over the ocean clamp is the sea: the donor's noise never stands land so low.</summary>
    private const double SeaMarginMetres = .5;
    /// <summary>Ground this close over the sea is beach, which the classic lays bare whatever its climate.</summary>
    private const double BeachMetres = (DaggerfallExteriorEnvironment.BeachElevation - DaggerfallTerrainSurfaceBuilder.OceanElevation) * DaggerfallTerrainSurfaceBuilder.TerrainScale;

    /// <summary>The map pixels either side a valley floor is the lowest ground of, about twenty kilometres.</summary>
    private const int FloorReach = 24;
    /// <summary>The map pixels either side the floor is then smoothed over, so it has no edges of its own.</summary>
    private const int FloorSmoothing = 12;

    /// <summary>Where the lift fades in about, as the latest update left it.</summary>
    internal sealed record Frame(double CentreX, double CentreZ, double Lift, bool Winter);

    /// <summary>The material indices a ground archive draws by.</summary>
    internal readonly record struct ArchiveMaterials(uint Open, uint Lush, uint Town, uint Shore, bool NoiseStone, bool HasLush);

    /// <summary>What a vertex draws: a material, and the weights of its layers.</summary>
    internal readonly record struct Paint(uint Material, System.Numerics.Vector4 Weights);

    /// <summary>What a heightfield draws by: the far sea's material index, and each ground archive's.</summary>
    internal sealed record GroundMaterials(uint Sea, IReadOnlyDictionary<int, ArchiveMaterials> Archives);

    private readonly DaggerfallTerrainSet _terrain;
    private readonly Func<int, int, int> _climate;
    private readonly DaggerfallHorizonTuning _tuning;
    private readonly DaggerfallGroundTuning _groundTuning;
    private readonly float[] _floor;
    private Frame _frame;
    private GroundMaterials? _materials;
    private readonly IReadOnlyDictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> _locations;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int X, int Y), float> _cellMeans = new();

    /// <param name="climate">The climate value at an in-bounds map pixel.</param>
    /// <param name="locations">The locations by the map pixel they stand on: all ground levels under them and near ground wears their tiles.</param>
    internal DaggerfallHorizonGround(DaggerfallTerrainSet terrain, Func<int, int, int> climate, DaggerfallHorizonTuning tuning, DaggerfallGroundTuning groundTuning,
        IReadOnlyDictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> locations)
    {
        _locations = locations ?? throw new ArgumentNullException(nameof(locations));
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _climate = climate ?? throw new ArgumentNullException(nameof(climate));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _groundTuning = groundTuning ?? throw new ArgumentNullException(nameof(groundTuning));
        _frame = new(0, 0, tuning.Lift, false);
        // The samples are read on first use, which is content work no worker may do.
        _ = terrain.Samples;
        _ = terrain.Relief;
        _floor = ValleyFloor(terrain);
    }

    /// <summary>The rings, finest first, in metres; the coarsest reaches past where the haze has closed.</summary>
    internal static HeightfieldRing[] Rings { get; } =
        [new(FineCellMetres, FineReachMetres), new(MiddleCellMetres, MiddleReachMetres), new(CoarseCellMetres, CoarseReachMetres)];

    /// <summary>What ground draws by, set before any tile is asked for.</summary>
    internal GroundMaterials Materials
    {
        get => Volatile.Read(ref _materials) ?? throw new InvalidOperationException("Ground is sampled before its materials are bound.");
        set => Volatile.Write(ref _materials, value);
    }

    /// <summary>
    /// Near ground's rings. Half a tile near the player, so every tile has a sample of its own and a path a tile wide keeps
    /// its shape; the walked spacing as far as a tree's foot shows; and coarse out past the window, where far ground takes over.
    /// </summary>
    internal static HeightfieldRing[] NearRings { get; } =
        [new(FinestNearCellMetres, DaggerfallExteriorGroundCollider.ReachMetres), new(Spacing, 1_228.8), new(Spacing * 4, 3_276.8)];

    /// <summary>The ground values the far ground shares with the walked ground.</summary>
    internal DaggerfallGroundTuning GroundTuning => _groundTuning;

    /// <summary>The frame the workers sample by; set before the tiles it changes are invalidated.</summary>
    internal Frame Current
    {
        get => Volatile.Read(ref _frame);
        set => Volatile.Write(ref _frame, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Far ground: lifted with distance and sunk under the window.</summary>
    public void Sample(HeightfieldTileRequest tile, HeightfieldTileSamples samples)
    {
        Frame now = Current;
        GroundMaterials materials = Materials;
        int heightSide = tile.HeightSide, side = tile.Side;
        double[] ground = new double[heightSide * heightSide];
        Dictionary<(int X, int Y), DaggerfallTerrainWindow> windows = [];
        for (int j = -1; j <= side; j++)
        for (int i = -1; i <= side; i++)
        {
            double x = tile.SampleX(i), z = tile.SampleZ(j);
            double height = Ground(x, z, tile.CellSize * 2, windows);
            ground[((j + 1) * heightSide) + i + 1] = height;
            samples.SetHeight(i, j, (float)(Lifted(height, x, z, now) - Sink(x, z, now)));
        }

        for (int j = 0; j < side; j++)
        for (int i = 0; i < side; i++)
        {
            double x = tile.SampleX(i), z = tile.SampleZ(j), height = ground[((j + 1) * heightSide) + i + 1];
            // Far cells smooth a slope below the walked ground's, so rock begins at less of one here.
            Draw(samples, i, j, height <= OceanMetres + SeaMarginMetres ? new(materials.Sea, new(1, 0, 0, 0))
                : PaintOpen(x, z, height, Slope(ground, heightSide, i, j, tile.CellSize) * _groundTuning.RockFromSlope / _tuning.RockFromSlope, now.Winter, materials));
        }
    }

    /// <summary>Near ground: the walked surface, in a frame whose (0, 0) stands at this offset on the map, so its numbers stay small enough to keep every texel.</summary>
    internal void SampleNear(HeightfieldTileRequest tile, HeightfieldTileSamples samples, double offsetX, double offsetZ)
    {
        Frame now = Current;
        GroundMaterials materials = Materials;
        int heightSide = tile.HeightSide, side = tile.Side;
        double[] ground = WalkedHeights(tile, samples, offsetX, offsetZ);
        for (int j = 0; j < side; j++)
        for (int i = 0; i < side; i++)
        {
            double x = tile.SampleX(i) + offsetX, z = tile.SampleZ(j) + offsetZ, height = ground[((j + 1) * heightSide) + i + 1];
            Draw(samples, i, j, PaintNear(x, z, height, Slope(ground, heightSide, i, j, tile.CellSize), now.Winter, materials));
        }
    }

    /// <summary>The collider's ground: near ground's heights alone, which need no materials bound.</summary>
    internal void SampleHeights(HeightfieldTileRequest tile, HeightfieldTileSamples samples, double offsetX, double offsetZ) =>
        _ = WalkedHeights(tile, samples, offsetX, offsetZ);

    private double[] WalkedHeights(HeightfieldTileRequest tile, HeightfieldTileSamples samples, double offsetX, double offsetZ)
    {
        int heightSide = tile.HeightSide, side = tile.Side;
        double[] ground = new double[heightSide * heightSide];
        Dictionary<(int X, int Y), DaggerfallTerrainWindow> windows = [];
        Dictionary<(long X, long Z), double> lattice = [];
        double finest = Math.Max(tile.CellSize, Spacing) * 2;
        for (int j = -1; j <= side; j++)
        for (int i = -1; i <= side; i++)
        {
            double height = Walked(tile.SampleX(i) + offsetX, tile.SampleZ(j) + offsetZ, finest, windows, lattice);
            ground[((j + 1) * heightSide) + i + 1] = height;
            samples.SetHeight(i, j, (float)height);
        }

        return ground;
    }

    private static float Slope(double[] ground, int heightSide, int i, int j, double cell)
    {
        int at = ((j + 1) * heightSide) + i + 1;
        double slopeX = (ground[at + 1] - ground[at - 1]) / (2 * cell), slopeZ = (ground[at + heightSide] - ground[at - heightSide]) / (2 * cell);
        return (float)Math.Sqrt((slopeX * slopeX) + (slopeZ * slopeZ));
    }

    private static void Draw(HeightfieldTileSamples samples, int i, int j, Paint paint)
    {
        samples.SetMaterial(i, j, paint.Material);
        samples.SetLayerWeights(i, j, paint.Weights);
    }

    /// <summary>The walked ground's height at a point, before any location flattens it.</summary>
    internal double Height(double x, double z) => Height(x, z, Spacing * 2, []);

    /// <summary>A height as the horizon draws it: ground is lifted above its own valley floor, more with distance.</summary>
    internal double Lifted(double height, double x, double z, Frame frame)
    {
        double floor = Floor(x, z), above = height - floor;
        if (above <= 0 || frame.Lift == 1) return height;
        double away = Math.Max(Math.Abs(x - frame.CentreX), Math.Abs(z - frame.CentreZ));
        // A straight fade, because an eased one takes height off a range faster than walking toward it adds it, and the range shrinks on approach.
        double share = Math.Clamp((away - _tuning.LiftFromMetres) / (_tuning.LiftFullMetres - _tuning.LiftFromMetres), 0, 1);
        return floor + (above * (1 + ((frame.Lift - 1) * share)));
    }

    /// <summary>The valley floor at a point: the lowest ground about it, smoothed, read between map pixels.</summary>
    internal double Floor(double x, double z)
    {
        // Heightmap pixel (X, Y) is vertex (0, 0) of map pixel (X + 1, Y).
        double mapX = Math.Clamp((x / DaggerfallTerrainSurfaceBuilder.HorizontalSize) - 1, 0, _terrain.Width - 1.001);
        double mapY = Math.Clamp(z / DaggerfallTerrainSurfaceBuilder.HorizontalSize, 0, _terrain.Height - 1.001);
        int ix = (int)mapX, iy = (int)mapY, at = (iy * _terrain.Width) + ix;
        double fx = mapX - ix, fy = mapY - iy;
        double top = _floor[at] + ((_floor[at + 1] - _floor[at]) * fx);
        double bottom = _floor[at + _terrain.Width] + ((_floor[at + _terrain.Width + 1] - _floor[at + _terrain.Width]) * fx);
        return top + ((bottom - top) * fy);
    }

    /// <summary>
    /// How far ground is sunk at a point. Under the walked window it isn't wanted, and at the walked ground's own height
    /// it would poke through wherever the two differ, showing as slivers hung in the air. It eases out past the window.
    /// </summary>
    internal double Sink(double x, double z, Frame frame)
    {
        double away = Math.Max(Math.Abs(x - frame.CentreX), Math.Abs(z - frame.CentreZ));
        double share = Math.Clamp((_tuning.SinkGoneMetres - away) / (_tuning.SinkGoneMetres - _tuning.SinkFromMetres), 0, 1);
        return _tuning.SinkMetres * share * share * (3 - (2 * share));
    }

    /// <summary>A vertex of open ground, by the same noise, slope and height wherever it is drawn.</summary>
    internal Paint PaintOpen(double x, double z, double height, float slope, bool winter, GroundMaterials materials)
    {
        ArchiveMaterials ground = Archive(x, z, winter, materials);
        (int vertexX, int vertexZ) = ((int)Math.Round(x / Spacing), (int)Math.Round(z / Spacing));
        float noise = DaggerfallTerrainSurfaceBuilder.DonorTerrainWeight(vertexX, 64_000 - vertexZ);
        System.Numerics.Vector4 weights = DaggerfallGroundBlend.Weights(_groundTuning, noise, slope, (float)height, ground.NoiseStone);
        if (!ground.HasLush || weights.W > 0) return new(ground.Open, weights);
        float lush = DaggerfallGroundBlend.Lush(_groundTuning, noise), keep = 1 - lush;
        return new(ground.Lush, new(weights.X * keep, weights.Y * keep, weights.Z, (weights.X + weights.Y) * lush));
    }

    /// <summary>A vertex of near ground: a location's tiles where it authored them, the water and beach, and otherwise open ground.</summary>
    internal Paint PaintNear(double x, double z, double height, float slope, bool winter, GroundMaterials materials)
    {
        ArchiveMaterials ground = Archive(x, z, winter, materials);
        if (Authored(x, z) is { } kinds)
        {
            float water = kinds[DaggerfallGroundKinds.Water], grass = kinds[DaggerfallGroundKinds.Grass], dirt = kinds[DaggerfallGroundKinds.Dirt];
            float stone = kinds[DaggerfallGroundKinds.Stone], paving = kinds[DaggerfallGroundKinds.Paving], ruts = kinds[DaggerfallGroundKinds.Ruts];
            float sum = Math.Max(water + grass + dirt + stone + paving + ruts, .001F);
            return water > 0 ? new(ground.Shore, new System.Numerics.Vector4(grass, dirt + ruts, stone + paving, water) / sum)
                : new(ground.Town, new System.Numerics.Vector4(grass, dirt, stone + paving, ruts) / sum);
        }

        if (height <= OceanMetres + SeaMarginMetres) return new(ground.Shore, new(0, 0, 0, 1));
        return height <= OceanMetres + BeachMetres ? new(ground.Shore, new(0, 1, 0, 0)) : PaintOpen(x, z, height, slope, winter, materials);
    }

    private ArchiveMaterials Archive(double x, double z, bool winter, GroundMaterials materials)
    {
        (int mapX, int mapY) = MapPixel(x, z);
        int archive = DaggerfallExteriorEnvironment.GroundTextureArchive(Climate(mapX, mapY), winter ? DaggerfallExteriorSeason.Winter : DaggerfallExteriorSeason.Summer);
        return materials.Archives[archive];
    }

    /// <summary>The walked surface at a point: its vertices flattened under locations, and between them a straight line.</summary>
    private double Walked(double x, double z, double finestMetres, Dictionary<(int X, int Y), DaggerfallTerrainWindow> windows, Dictionary<(long X, long Z), double> lattice)
    {
        double at(long vx, long vz)
        {
            if (lattice.TryGetValue((vx, vz), out double known)) return known;
            double px = vx * Spacing, pz = vz * Spacing;
            return lattice[(vx, vz)] = Ground(px, pz, finestMetres, windows);
        }

        double fx = x / Spacing, fz = z / Spacing;
        long ix = (long)Math.Floor(fx + 1e-6), iz = (long)Math.Floor(fz + 1e-6);
        double u = Math.Max(fx - ix, 0), v = Math.Max(fz - iz, 0);
        if (u < 1e-6 && v < 1e-6) return at(ix, iz);
        double near = at(ix, iz) + ((at(ix + 1, iz) - at(ix, iz)) * u);
        double far = at(ix, iz + 1) + ((at(ix + 1, iz + 1) - at(ix, iz + 1)) * u);
        return near + ((far - near) * v);
    }

    /// <summary>Where a point falls: its map pixel and the vertex within it.</summary>
    private static (int MapX, int MapY, int X, int Y) Vertex(double x, double z)
    {
        long vertexX = (long)Math.Round(x / Spacing), vertexZ = (long)Math.Round(z / Spacing);
        int mapX = (int)Math.Floor(vertexX / (double)TileDimension), mapY = (int)Math.Ceiling(vertexZ / (double)TileDimension);
        return (mapX, mapY, (int)(vertexX - ((long)mapX * TileDimension)), (int)(((long)mapY * TileDimension) - vertexZ));
    }

    /// <summary>The ground at a vertex, near or far: its height with its location's flattening.</summary>
    private double Ground(double x, double z, double finestMetres, Dictionary<(int X, int Y), DaggerfallTerrainWindow> windows) =>
        Flattened(Height(x, z, finestMetres, windows), x, z);

    /// <summary>A height with its location's flattening: a location levels its whole map pixel, not only its own footprint.</summary>
    private double Flattened(double height, double x, double z)
    {
        (int mapX, int mapY, int vx, int vy) = Vertex(x, z);
        if (!_locations.TryGetValue(new(mapX, mapY), out DaggerfallSiteExterior? location)) return height;
        float mean = _cellMeans.GetOrAdd((mapX, mapY), cell => DaggerfallTerrainSurfaceBuilder.Build(_terrain, cell.X, cell.Y).NormalizedHeights.Average());
        float share = DaggerfallTerrainSurfaceBuilder.FlatteningShare(DaggerfallLocationTerrainFrame.Flattening(location), vx / (float)TileDimension, vy / (float)TileDimension);
        return height + (((mean * DaggerfallTerrainSurfaceBuilder.TerrainVerticalSize) - height) * share);
    }

    /// <summary>
    /// The share of each kind at a point, from the authored tiles it touches, or null where no location authored its ground.
    /// A vertex on a tile's corner shares it with three others; a sample inside a tile reads that tile alone.
    /// </summary>
    private float[]? Authored(double x, double z)
    {
        (int mapX, int mapY, int _, int _) = Vertex(x, z);
        if (!_locations.TryGetValue(new(mapX, mapY), out DaggerfallSiteExterior? location)) return null;
        double fx = (x / Spacing) - ((long)mapX * TileDimension), fy = ((long)mapY * TileDimension) - (z / Spacing);
        int tx0 = (int)Math.Floor(fx + 1e-6), ty0 = (int)Math.Floor(fy + 1e-6);
        float u = (float)Math.Max(fx - tx0, 0), v = (float)Math.Max(fy - ty0, 0);
        float[]? kinds = null;
        for (int ty = v < 1e-5F ? ty0 - 1 : ty0; ty <= ty0; ty++)
        for (int tx = u < 1e-5F ? tx0 - 1 : tx0; tx <= tx0; tx++)
        {
            if (tx is < 0 or >= TileDimension || ty is < 0 or >= TileDimension) continue;
            int index = (ty * TileDimension) + tx;
            if (!location.GroundTiles.Contains(index)) continue;
            byte tile = location.GroundTiles.At(index);
            float tu = tx == tx0 ? u : 1, tv = ty == ty0 ? v : 1;
            kinds ??= new float[6];
            // A paving edge's art runs a diagonal through the tile: one corner, but half its area.
            float cut = DaggerfallGroundKinds.CutsDiagonally(tile) && tu is > 0 and < 1 && tv is > 0 and < 1 ? 3 : 1;
            void Add(int u, int v, float share)
            {
                byte kind = DaggerfallGroundKinds.At(tile, u, v);
                kinds[kind] += share * (kind == DaggerfallGroundKinds.Paving ? 1 : cut);
            }

            Add(0, 0, (1 - tu) * (1 - tv));
            Add(1, 0, tu * (1 - tv));
            Add(0, 1, (1 - tu) * tv);
            Add(1, 1, tu * tv);
        }

        return kinds;
    }

    private double Height(double x, double z, double finestMetres, Dictionary<(int X, int Y), DaggerfallTerrainWindow> windows)
    {
        // The vertex the point falls on: a pixel's rows run north from its y, so its row 0 is the pixel's own z.
        long vertexX = (long)Math.Round(x / Spacing), vertexZ = (long)Math.Round(z / Spacing);
        int mapX = (int)Math.Floor(vertexX / (double)TileDimension);
        int mapY = (int)Math.Ceiling(vertexZ / (double)TileDimension);
        if (!windows.TryGetValue((mapX, mapY), out DaggerfallTerrainWindow? window))
            windows.Add((mapX, mapY), window = DaggerfallTerrainSurfaceBuilder.ReadWindow(_terrain, mapX, mapY));
        return DaggerfallTerrainSurfaceBuilder.NormalizedHeight(window, (int)(vertexX - ((long)mapX * TileDimension)),
            (int)(((long)mapY * TileDimension) - vertexZ), finestMetres) * DaggerfallTerrainSurfaceBuilder.TerrainVerticalSize;
    }

    private static float[] ValleyFloor(DaggerfallTerrainSet terrain)
    {
        int width = terrain.Width, height = terrain.Height;
        float[] ground = new float[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            ground[(y * width) + x] = (float)Math.Max(OceanMetres, ((terrain.GetHeight(x, y) * DaggerfallTerrainSurfaceBuilder.BaseHeightScale)
                + (terrain.Samples[(((y * width) + x) * 25) + 12] * DaggerfallTerrainSurfaceBuilder.NoiseMapScale)) * DaggerfallTerrainSurfaceBuilder.TerrainScale);
        return Pass(Pass(Pass(Pass(ground, width, height, 1, 0, FloorReach, Math.Min), width, height, 0, 1, FloorReach, Math.Min),
            width, height, 1, 0, FloorSmoothing, null), width, height, 0, 1, FloorSmoothing, null);
    }

    /// <summary>One direction of a separable filter: each cell the <paramref name="pick"/> of the cells about it, or their mean.</summary>
    private static float[] Pass(float[] source, int width, int height, int stepX, int stepY, int reach, Func<float, float, float>? pick)
    {
        float[] result = new float[source.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float value = source[(y * width) + x], sum = 0;
            for (int k = -reach; k <= reach; k++)
            {
                float other = source[(Math.Clamp(y + (k * stepY), 0, height - 1) * width) + Math.Clamp(x + (k * stepX), 0, width - 1)];
                if (pick is null) sum += other;
                else value = pick(value, other);
            }

            result[(y * width) + x] = pick is null ? sum / ((reach * 2) + 1) : value;
        }

        return result;
    }

    private static (int X, int Y) MapPixel(double x, double z) => (
        (int)Math.Floor(x / DaggerfallTerrainSurfaceBuilder.HorizontalSize),
        (int)Math.Ceiling(z / DaggerfallTerrainSurfaceBuilder.HorizontalSize));

    private int Climate(int mapX, int mapY) =>
        _climate(Math.Clamp(mapX, 0, _terrain.Width - 1), Math.Clamp(mapY, 0, _terrain.Height - 1));

}
