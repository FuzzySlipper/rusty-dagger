using Rusty.Engine;
using Rusty.Engine.Heightfields;
using Rusty.Engine.VegetationTiles;
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
/// ranges until they are all sampled again. It reads only the terrain and climate, which nothing changes, the
/// latest <see cref="Frame"/>, and the locations' ground the update has read (<see cref="ReadGroundsAbout"/>), and
/// calls no Engine service.
/// </summary>
internal sealed class DaggerfallHorizonGround : IHeightfieldSampler
{
    /// <summary>Cells along a tile's side: the finest ring's tile is one map pixel, each coarser tile four of the last.</summary>
    internal const uint TileCells = 16;
    internal const double FineCellMetres = DaggerfallTerrainSurfaceBuilder.HorizontalSize / TileCells;
    internal const double MiddleCellMetres = FineCellMetres * 4, CoarseCellMetres = FineCellMetres * 16;
    internal const double FineReachMetres = 8_192, MiddleReachMetres = 49_152, CoarseReachMetres = 204_800;
    private const double Spacing = DaggerfallTerrainSurfaceBuilder.SampleSpacing;
    /// <summary>How far near ground reaches at the least, and the sea with it.</summary>
    internal const double NearReachMetres = 3_276.8;
    /// <summary>Near ground's finest cells, which the collider shares.</summary>
    internal const double FinestNearCellMetres = Spacing / 2;
    private const int TileDimension = DaggerfallTerrainSurfaceBuilder.SampleDimension - 1;
    /// <summary>The sea's surface, everywhere the same.</summary>
    internal const double OceanMetres = DaggerfallTerrainSurfaceBuilder.OceanElevation * DaggerfallTerrainSurfaceBuilder.TerrainScale;
    /// <summary>Ground this close over the ocean clamp is the sea: the donor's noise never stands land so low.</summary>
    private const double SeaMarginMetres = .5;
    /// <summary>The seabed's lattice, coarser than the ground's, since each node looks for land about it.</summary>
    private const double SeabedNodeMetres = Spacing * 4;
    /// <summary>How the seabed falls away from land, rise over run.</summary>
    private const double SeabedSlope = .06;
    /// <summary>How many nodes away a node looks for land; the seabed levels off past the last.</summary>
    private static readonly int[] SeabedProbes = [1, 2, 3, 5, 8];
    /// <summary>Ground this close over the ocean clamp stands on it, whatever its rounding.</summary>
    private const double ClampMetres = .01;
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
    /// <summary>The locations' authored ground read so far, which the update replaces whole and the workers only read.</summary>
    private Dictionary<DaggerfallExteriorCellId, DaggerfallGroundTileGrid> _grounds = [];
    private (int X, int Y)? _groundsAbout;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int X, int Y), float> _cellMeans = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int X, int Y), byte> _seaPixels = new();

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
        _ = terrain.Rivers;
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
        [new(FinestNearCellMetres, DaggerfallExteriorGroundCollider.ReachMetres), new(Spacing, 1_228.8), new(Spacing * 4, NearReachMetres)];

    /// <summary>
    /// Reads the authored ground of every location within near ground's reach of map point (x, z), and a map pixel more.
    /// A location's ground is read from the data container, which is Engine content work only the update may do, while
    /// near ground, flora and ground cover paint from it on the workers about the focus the update gives them: the update
    /// calls this before it moves a focus. A ground once read is kept, since it is a few kilobytes and a worker may still
    /// be sampling a tile it asked for before the focus moved.
    /// </summary>
    internal void ReadGroundsAbout(double x, double z)
    {
        (int mapX, int mapY) = MapPixel(x, z);
        if (_groundsAbout == (mapX, mapY)) return;
        _groundsAbout = (mapX, mapY);
        Dictionary<DaggerfallExteriorCellId, DaggerfallGroundTileGrid> held = Volatile.Read(ref _grounds);
        Dictionary<DaggerfallExteriorCellId, DaggerfallGroundTileGrid>? read = null;
        int reach = (int)Math.Ceiling(NearReachMetres / DaggerfallTerrainSurfaceBuilder.HorizontalSize) + 1;
        for (int cellY = mapY - reach; cellY <= mapY + reach; cellY++)
        for (int cellX = mapX - reach; cellX <= mapX + reach; cellX++)
        {
            DaggerfallExteriorCellId cell = new(cellX, cellY);
            if (held.ContainsKey(cell) || !_locations.TryGetValue(cell, out DaggerfallSiteExterior? location)) continue;
            (read ??= new(held))[cell] = location.GroundTiles;
        }

        if (read is not null) Volatile.Write(ref _grounds, read);
    }

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
            Draw(samples, i, j, height <= OceanMetres + SeaMarginMetres || InChannel(x, z, tile.CellSize * 2) ? new(materials.Sea, new(1, 0, 0, 0))
                : PaintOpen(x, z, height, Slope(ground, heightSide, i, j, tile.CellSize) * _groundTuning.RockFromSlope / _tuning.RockFromSlope, now.Winter, materials));
        }
    }

    /// <summary>
    /// The far forest's ground, in far ground's own frame (map metres): far ground's lifted and sunk heights, so copies stand
    /// on its triangles; each point's climate's nature archive, counted from the first; and flora's cover there.
    /// </summary>
    internal void SampleFarFlora(VegetationTileRequest tile, VegetationTileSamples samples, bool winter, DaggerfallFloraTuning flora)
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

        DaggerfallExteriorSeason season = winter ? DaggerfallExteriorSeason.Winter : DaggerfallExteriorSeason.Summer;
        for (int j = 0; j < side; j++)
        for (int i = 0; i < side; i++)
        {
            double x = tile.SampleX(i), z = tile.SampleZ(j);
            (int mapX, int mapY) = MapPixel(x, z);
            int climate = Climate(mapX, mapY);
            samples.SetGround(i, j, (uint)(DaggerfallExteriorEnvironment.NatureTextureArchive(climate, season) - DaggerfallExteriorEnvironment.FirstNatureArchive));
            samples.SetCover(i, j, FloraCover(x, z, ground[((j + 1) * heightSide) + i + 1], Slope(ground, heightSide, i, j, tile.CellSize),
                climate, winter, materials, flora));
        }
    }

    /// <summary>Near ground: the walked surface, in a frame whose (0, 0) stands at this offset on the map, so its numbers stay small enough to keep every texel.</summary>
    internal void SampleNear(HeightfieldTileRequest tile, HeightfieldTileSamples samples, double offsetX, double offsetZ)
    {
        Frame now = Current;
        GroundMaterials materials = Materials;
        int heightSide = tile.HeightSide, side = tile.Side;
        double[] ground = WalkedHeights(tile, offsetX, offsetZ);
        SetHeights(samples, ground, heightSide, side);
        for (int j = 0; j < side; j++)
        for (int i = 0; i < side; i++)
        {
            double x = tile.SampleX(i) + offsetX, z = tile.SampleZ(j) + offsetZ, height = ground[((j + 1) * heightSide) + i + 1];
            // Past the sea's sheet and the rivers' ribbons the outer ring's bed shows, so it wears far ground's sea.
            Draw(samples, i, j, tile.CellSize > Spacing && (height < OceanMetres || InChannel(x, z, tile.CellSize * 2)) ? new(materials.Sea, new(1, 0, 0, 0))
                : PaintNear(x, z, height, Slope(ground, heightSide, i, j, tile.CellSize), now.Winter, materials));
        }
    }

    /// <summary>The collider's ground: near ground's heights alone, which need no materials bound.</summary>
    internal void SampleHeights(HeightfieldTileRequest tile, HeightfieldTileSamples samples, double offsetX, double offsetZ) =>
        SetHeights(samples, WalkedHeights(tile, offsetX, offsetZ), tile.HeightSide, tile.Side);

    private static void SetHeights(HeightfieldTileSamples samples, double[] ground, int heightSide, int side)
    {
        for (int j = -1; j <= side; j++)
        for (int i = -1; i <= side; i++)
            samples.SetHeight(i, j, (float)ground[((j + 1) * heightSide) + i + 1]);
    }

    private double[] WalkedHeights(HeightfieldTileRequest tile, double offsetX, double offsetZ) =>
        WalkedHeights(tile.OriginX + offsetX, tile.OriginZ + offsetZ, tile.CellSize, tile.Side);

    /// <summary>The walked surface over a tile of <paramref name="side"/> vertices a side from map point (x, z), with a ring more about them.</summary>
    private double[] WalkedHeights(double originX, double originZ, double cellSize, int side)
    {
        int heightSide = side + 2;
        double[] ground = new double[heightSide * heightSide];
        Dictionary<(int X, int Y), DaggerfallTerrainWindow> windows = [];
        Dictionary<(long X, long Z), double> lattice = [], depths = [];
        double finest = Math.Max(cellSize, Spacing) * 2;
        for (int j = -1; j <= side; j++)
        for (int i = -1; i <= side; i++)
        {
            double x = originX + (i * cellSize), z = originZ + (j * cellSize), height = Walked(x, z, finest, windows, lattice, depths);
            ground[((j + 1) * heightSide) + i + 1] = height;
            if (height < OceanMetres) _seaPixels.TryAdd(MapPixel(x, z), 0);
        }

        return ground;
    }

    /// <summary>
    /// Flora's ground, in map metres: the walked surface's heights; each vertex's class, its climate's nature archive
    /// counted from the first; and its cover, the share of the donor's chance its ground's look keeps (<see cref="FloraCover"/>),
    /// or for a field of <paramref name="grass"/> the ground's grass, thinned in towns (<see cref="CoverLook"/>).
    /// </summary>
    internal void SampleFlora(VegetationTileRequest tile, VegetationTileSamples samples, bool winter,
        DaggerfallFloraTuning flora, (float TownShare, double TownRamp)? grass = null)
    {
        GroundMaterials materials = Materials;
        int heightSide = tile.HeightSide, side = tile.Side;
        double[] ground = WalkedHeights(tile.OriginX, tile.OriginZ, tile.CellSize, side);
        for (int j = -1; j <= side; j++)
        for (int i = -1; i <= side; i++)
            samples.SetHeight(i, j, (float)ground[((j + 1) * heightSide) + i + 1]);
        DaggerfallExteriorSeason season = winter ? DaggerfallExteriorSeason.Winter : DaggerfallExteriorSeason.Summer;
        for (int j = 0; j < side; j++)
        for (int i = 0; i < side; i++)
        {
            double x = tile.SampleX(i), z = tile.SampleZ(j);
            (int mapX, int mapY) = MapPixel(x, z);
            int climate = Climate(mapX, mapY);
            samples.SetGround(i, j, (uint)(DaggerfallExteriorEnvironment.NatureTextureArchive(climate, season) - DaggerfallExteriorEnvironment.FirstNatureArchive));
            double height = ground[((j + 1) * heightSide) + i + 1];
            float slope = Slope(ground, heightSide, i, j, tile.CellSize);
            samples.SetCover(i, j, grass is { } g
                ? CoverLook(x, z, height, slope, winter, g.TownShare, g.TownRamp)?.Weights.X ?? 0
                : FloraCover(x, z, height, slope, climate, winter, materials, flora));
        }
    }

    /// <summary>
    /// The walked surface on a lattice of <paramref name="side"/> vertices a side from map point (x, z), with a ring more
    /// about them, row by row from the ring's corner: what near ground draws at those vertices.
    /// </summary>
    internal double[] WalkedGrid(double originX, double originZ, double cellSize, int side) => WalkedHeights(originX, originZ, cellSize, side);

    /// <summary>
    /// Open ground's look at a point: its ground archive, its layers' weights (grass, dirt, rock, and snow or a swamp's
    /// lush ground), and whether the last is lush ground. None on a beach or bank, or in a location's clearing.
    /// </summary>
    internal (int Archive, System.Numerics.Vector4 Weights, bool Lush)? OpenLook(double x, double z, double height, float slope, bool winter)
    {
        if (height <= OceanMetres + BeachMetres || OnBank(x, z) || InClearing(x, z)) return null;
        int archive = GroundArchive(x, z, winter);
        ArchiveMaterials materials = Materials.Archives[archive];
        Paint paint = PaintOpen(x, z, height, slope, winter, Materials);
        return (archive, paint.Weights, materials.HasLush && paint.Material == materials.Lush);
    }

    /// <summary>
    /// What ground cover grows on at a point: its ground archive, the weights of the four layers it draws there and
    /// which archive and record each layer is. Open ground's are grass, dirt, rock, and snow or a swamp's lush ground; a
    /// location's authored ground's are its grass, dirt, paving and ruts. A location thins it to
    /// <paramref name="townShare"/> on its flattened ground, rising to full over <paramref name="rampMetres"/> beyond.
    /// None on a beach, a bank or the water's edge.
    /// </summary>
    internal (int Archive, System.Numerics.Vector4 Weights, (int Archive, int Record)[] Layers)? CoverLook(double x, double z, double height, float slope,
        bool winter, float townShare, double rampMetres)
    {
        if (height <= OceanMetres + BeachMetres || OnBank(x, z)) return null;
        int archive = GroundArchive(x, z, winter);
        (int Archive, int Record)[] records = DaggerfallGroundLayers.Records(archive);
        (int mapX, int mapY, int vx, int vy) = Vertex(x, z);
        float share = 1;
        if (_locations.TryGetValue(new(mapX, mapY), out DaggerfallSiteExterior? location)
            && DaggerfallExteriorEnvironment.TilesOutsideFlattening(location, vx, vy) is { } outside)
        {
            float t = (float)Math.Clamp(outside * Spacing / Math.Max(rampMetres, 1e-3), 0, 1);
            share = townShare + ((1 - townShare) * t * t * (3 - (2 * t)));
        }

        if (Authored(x, z) is { } kinds)
        {
            if (kinds[DaggerfallGroundKinds.Water] > 0) return null;
            float grass = kinds[DaggerfallGroundKinds.Grass], dirt = kinds[DaggerfallGroundKinds.Dirt];
            float paving = kinds[DaggerfallGroundKinds.Stone] + kinds[DaggerfallGroundKinds.Paving], ruts = kinds[DaggerfallGroundKinds.Ruts];
            float sum = Math.Max(grass + dirt + paving + ruts, .001F);
            return (archive, new System.Numerics.Vector4(grass, dirt, paving, ruts) * (share / sum), [records[0], records[1], records[^3], records[^2]]);
        }

        ArchiveMaterials materials = Materials.Archives[archive];
        Paint paint = PaintOpen(x, z, height, slope, winter, Materials);
        bool lush = materials.HasLush && paint.Material == materials.Lush;
        return (archive, paint.Weights * share, [records[0], records[1], records[2], lush ? records[4] : records[3]]);
    }

    /// <summary>The kinds of ground <see cref="Kind"/> tells apart, for a developer to find one.</summary>
    internal static IReadOnlyList<string> Kinds { get; } = ["grass", "dirt", "rock", "sand", "snow", "lush", "beach"];

    /// <summary>
    /// What a point of open ground mostly is, by the layer that weighs most there and only where it weighs at least
    /// <paramref name="share"/>: grass, dirt, rock, a desert's sand, snow (a winter set's ground, or the snow on high ground)
    /// or a swamp's lush ground; or beach. None on a bank, in a river, in a location's clearing, or where no layer leads.
    /// </summary>
    internal string? Kind(double x, double z, bool winter, float share = .7f)
    {
        double height = Height(x, z);
        if (height <= OceanMetres || InChannel(x, z, Spacing * 2)) return null;
        if (height <= OceanMetres + BeachMetres) return OnBank(x, z) || InClearing(x, z) ? null : "beach";
        double east = (Height(x + Spacing, z) - height) / Spacing, south = (Height(x, z + Spacing) - height) / Spacing;
        if (OpenLook(x, z, height, (float)Math.Sqrt((east * east) + (south * south)), winter) is not { } look) return null;
        int layer = 0;
        for (int i = 1; i < 4; i++) if (look.Weights[i] > look.Weights[layer]) layer = i;
        if (look.Weights[layer] < share) return null;
        (int mapX, int mapY) = MapPixel(x, z);
        if (DaggerfallExteriorEnvironment.IsDesertClimate(Climate(mapX, mapY))) return "sand";
        return layer switch
        {
            0 => look.Archive % 100 == 3 ? "snow" : "grass",
            1 => look.Archive % 100 == 3 ? "snow" : "dirt",
            2 => "rock",
            _ => look.Lush ? "lush" : "snow",
        };
    }

    /// <summary>
    /// The nearest point to map point (x, z), within <paramref name="radius"/> metres, whose ground is mostly
    /// <paramref name="kind"/>; searched ring by ring, finer near the start.
    /// </summary>
    internal (double X, double Z)? Find(double x, double z, string kind, double radius, bool winter)
    {
        double step = Math.Max(Spacing * 4, radius / 400);
        for (double ring = 0; ring <= radius; ring += step)
        {
            int points = Math.Max(1, (int)Math.Ceiling(2 * Math.PI * ring / step));
            for (int i = 0; i < points; i++)
            {
                double angle = 2 * Math.PI * i / points, px = x + (ring * Math.Cos(angle)), pz = z + (ring * Math.Sin(angle));
                if (px < Spacing || pz < Spacing || px >= _terrain.Width * DaggerfallTerrainSurfaceBuilder.HorizontalSize
                    || pz >= _terrain.Height * DaggerfallTerrainSurfaceBuilder.HorizontalSize) continue;
                // A spot in the middle of its kind, not on its edge, so the whole view about it shows it.
                if (Kind(px, pz, winter) == kind && Kind(px + 12, pz, winter) == kind && Kind(px, pz + 12, winter) == kind
                    && Kind(px - 12, pz, winter) == kind && Kind(px, pz - 12, winter) == kind) return (px, pz);
            }
        }

        return null;
    }

    /// <summary>How much of open ground is grass by its look, none on a beach or bank, under snow or in a location's clearing.</summary>
    internal float GrassCover(double x, double z, double height, float slope, bool winter) => OpenLook(x, z, height, slope, winter)?.Weights.X ?? 0;

    /// <summary>
    /// How much of the donor's chance a point of open ground keeps: by its look, grass, dirt and rock at their chances and
    /// snow at none; by its map pixel's height and its climate, as the donor thins them; and none on a beach or bank, or
    /// in a location's clearing.
    /// </summary>
    private float FloraCover(double x, double z, double height, float slope, int climate, bool winter, GroundMaterials materials, DaggerfallFloraTuning flora)
    {
        if (height <= OceanMetres + BeachMetres || OnBank(x, z) || InClearing(x, z)) return 0;
        (int vertexX, int vertexZ) = ((int)Math.Round(x / Spacing), (int)Math.Round(z / Spacing));
        float noise = DaggerfallTerrainSurfaceBuilder.DonorTerrainWeight(vertexX, 64_000 - vertexZ);
        System.Numerics.Vector4 look = DaggerfallGroundBlend.Weights(_groundTuning, noise, slope, (float)height, Archive(x, z, winter, materials).NoiseStone);
        (int mapX, int mapY) = MapPixel(x, z);
        float elevation = Math.Clamp(_terrain.Heightmap[(Math.Clamp(mapY, 0, _terrain.Height - 1) * _terrain.Width) + Math.Clamp(mapX, 0, _terrain.Width - 1)] / 128F, .4F, 1F);
        float chance = (look.X * flora.GrassChance) + (look.Y * flora.DirtChance) + (look.Z * flora.StoneChance);
        return Math.Clamp(chance * elevation * (DaggerfallExteriorEnvironment.IsDesertClimate(climate) ? flora.DesertShare : 1), 0, 1);
    }

    /// <summary>Whether a point lies in a location's clearing: its flattened rectangle and the donor's clearance about it.</summary>
    private bool InClearing(double x, double z)
    {
        (int mapX, int mapY, int vx, int vy) = Vertex(x, z);
        return _locations.TryGetValue(new(mapX, mapY), out DaggerfallSiteExterior? location)
            && DaggerfallExteriorEnvironment.InNatureClearing(location, vx, vy);
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

        return height <= OceanMetres + BeachMetres || OnBank(x, z) ? new(ground.Shore, new(0, 1, 0, 0)) : PaintOpen(x, z, height, slope, winter, materials);
    }

    /// <summary>Whether a point of the map lies in a river, as a sampling this coarse holds it. The rivers are measured from the heightmap's corner, a map pixel west of the ground's.</summary>
    private bool InChannel(double x, double z, double finestMetres) => _terrain.Rivers?.InChannel(x - DaggerfallTerrainSurfaceBuilder.HorizontalSize, z, finestMetres) ?? false;

    private bool OnBank(double x, double z) => _terrain.Rivers?.OnBank(x - DaggerfallTerrainSurfaceBuilder.HorizontalSize, z, Spacing * 2) ?? false;

    private ArchiveMaterials Archive(double x, double z, bool winter, GroundMaterials materials) => materials.Archives[GroundArchive(x, z, winter)];

    /// <summary>Whether near ground has found seabed on a map pixel, which is where the sea stands.</summary>
    internal bool HoldsSea(int mapX, int mapY) => _seaPixels.ContainsKey((mapX, mapY));

    /// <summary>The nature archive at a point of the map.</summary>
    internal int NatureArchive(double x, double z, bool winter)
    {
        (int mapX, int mapY) = MapPixel(x, z);
        return DaggerfallExteriorEnvironment.NatureTextureArchive(Climate(mapX, mapY), winter ? DaggerfallExteriorSeason.Winter : DaggerfallExteriorSeason.Summer);
    }

    /// <summary>The ground archive at a point.</summary>
    internal int GroundArchive(double x, double z, bool winter)
    {
        (int mapX, int mapY) = MapPixel(x, z);
        return DaggerfallExteriorEnvironment.GroundTextureArchive(Climate(mapX, mapY), winter ? DaggerfallExteriorSeason.Winter : DaggerfallExteriorSeason.Summer);
    }

    /// <summary>The walked surface at a point: its vertices flattened under locations, and between them a straight line.</summary>
    private double Walked(double x, double z, double finestMetres, Dictionary<(int X, int Y), DaggerfallTerrainWindow> windows, Dictionary<(long X, long Z), double> lattice, Dictionary<(long X, long Z), double> depths)
    {
        double at(long vx, long vz)
        {
            if (lattice.TryGetValue((vx, vz), out double known)) return known;
            double px = vx * Spacing, pz = vz * Spacing;
            return lattice[(vx, vz)] = Flattened(Bed(px, pz, finestMetres, windows, depths), px, pz);
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
        DaggerfallExteriorCellId cell = new(mapX, mapY);
        if (!_locations.ContainsKey(cell)) return null;
        // Reading the ground here would read the data container on a worker, which corrupts the Engine's heap.
        if (!Volatile.Read(ref _grounds).TryGetValue(cell, out DaggerfallGroundTileGrid? tiles))
            throw new InvalidOperationException($"Ground at map pixel ({mapX}, {mapY}) is painted before the update read its location's ground (ReadGroundsAbout).");
        double fx = (x / Spacing) - ((long)mapX * TileDimension), fy = ((long)mapY * TileDimension) - (z / Spacing);
        int tx0 = (int)Math.Floor(fx + 1e-6), ty0 = (int)Math.Floor(fy + 1e-6);
        float u = (float)Math.Max(fx - tx0, 0), v = (float)Math.Max(fy - ty0, 0);
        float[]? kinds = null;
        for (int ty = v < 1e-5F ? ty0 - 1 : ty0; ty <= ty0; ty++)
        for (int tx = u < 1e-5F ? tx0 - 1 : tx0; tx <= tx0; tx++)
        {
            if (tx is < 0 or >= TileDimension || ty is < 0 or >= TileDimension) continue;
            int index = (ty * TileDimension) + tx;
            if (!tiles.Contains(index)) continue;
            byte tile = tiles.At(index);
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

    /// <summary>Walked ground at a vertex, which runs on down under the sea where far ground stops at its surface.</summary>
    private double Bed(double x, double z, double finestMetres, Dictionary<(int X, int Y), DaggerfallTerrainWindow> windows, Dictionary<(long X, long Z), double> depths)
    {
        double height = Height(x, z, finestMetres, windows);
        if (height > OceanMetres + ClampMetres) return height;
        double fx = x / SeabedNodeMetres, fz = z / SeabedNodeMetres;
        long ix = (long)Math.Floor(fx), iz = (long)Math.Floor(fz);
        double u = fx - ix, v = fz - iz;
        double at(long nodeX, long nodeZ) => depths.TryGetValue((nodeX, nodeZ), out double known) ? known
            : depths[(nodeX, nodeZ)] = SeaDepth(nodeX, nodeZ, finestMetres, windows);
        double near = at(ix, iz) + ((at(ix + 1, iz) - at(ix, iz)) * u), far = at(ix, iz + 1) + ((at(ix + 1, iz + 1) - at(ix, iz + 1)) * u);
        return OceanMetres - (near + ((far - near) * v));
    }

    /// <summary>
    /// How deep the sea stands at a seabed node, from how far it is to land. The classic's heights under its ocean clamp
    /// can't serve: nothing ever showed them, and they jump metres at map pixel edges.
    /// </summary>
    private double SeaDepth(long nodeX, long nodeZ, double finestMetres, Dictionary<(int X, int Y), DaggerfallTerrainWindow> windows)
    {
        double x = nodeX * SeabedNodeMetres, z = nodeZ * SeabedNodeMetres;
        foreach (int reach in SeabedProbes)
        {
            double metres = reach * SeabedNodeMetres, diagonal = metres * .7071;
            foreach ((double east, double south) in new[] { (metres, 0d), (-metres, 0d), (0d, metres), (0d, -metres), (diagonal, diagonal), (diagonal, -diagonal), (-diagonal, diagonal), (-diagonal, -diagonal) })
                if (Height(x + east, z + south, finestMetres, windows) > OceanMetres + ClampMetres) return metres * SeabedSlope;
        }

        return SeabedProbes[^1] * SeabedNodeMetres * SeabedSlope;
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

    internal static (int X, int Y) MapPixel(double x, double z) => (
        (int)Math.Floor(x / DaggerfallTerrainSurfaceBuilder.HorizontalSize),
        (int)Math.Ceiling(z / DaggerfallTerrainSurfaceBuilder.HorizontalSize));

    private int Climate(int mapX, int mapY) =>
        _climate(Math.Clamp(mapX, 0, _terrain.Width - 1), Math.Clamp(mapY, 0, _terrain.Height - 1));

}
