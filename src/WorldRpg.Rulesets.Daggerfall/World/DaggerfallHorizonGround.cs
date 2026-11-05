using Rusty.Engine;
using Rusty.Engine.Heightfields;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The ground past the walked window, as the Engine's heightfield rings sample it on the streamer's workers. Every
/// sample is a vertex of the walked ground's own surface (ring cells are whole multiples of its spacing) in the
/// frame's coordinates: x east and z south in metres, map pixel (X, Y) spanning x from X and z back from Y, in
/// <see cref="DaggerfallExteriorCellResidency.CellSize"/> steps. Ground above the player's is then lifted with
/// distance: the map's ranges stand a degree or two over the skyline at true height, where the classic's painted
/// ones stood near thirty. It reads only the terrain and climate, which nothing changes, and the latest
/// <see cref="Frame"/>, and calls no Engine service.
/// </summary>
internal sealed class DaggerfallHorizonGround : IHeightfieldSampler
{
    /// <summary>Cells along a tile's side: the finest ring's tile is one map pixel, each coarser tile four of the last.</summary>
    internal const uint TileCells = 16;
    internal const double FineCellMetres = DaggerfallTerrainSurfaceBuilder.HorizontalSize / TileCells;
    internal const double MiddleCellMetres = FineCellMetres * 4, CoarseCellMetres = FineCellMetres * 16;
    internal const double FineReachMetres = 8_192, MiddleReachMetres = 49_152, CoarseReachMetres = 204_800;
    private const double Spacing = DaggerfallTerrainSurfaceBuilder.SampleSpacing;
    private const int TileDimension = DaggerfallTerrainSurfaceBuilder.SampleDimension - 1;
    private const double OceanMetres = DaggerfallTerrainSurfaceBuilder.OceanElevation * DaggerfallTerrainSurfaceBuilder.TerrainScale;
    /// <summary>Ground this close over the ocean clamp is the sea: the donor's noise never stands land so low.</summary>
    private const double SeaMarginMetres = .5;
    /// <summary>In winter this share of the ground off the deserts is snow, whatever its height.</summary>
    private const float WinterSnow = .75F;

    /// <summary>Where the lift is measured from, as the latest update left it.</summary>
    internal sealed record Frame(double CentreX, double CentreZ, double BaseMetres, double Lift, bool Winter);

    private readonly DaggerfallTerrainSet _terrain;
    private readonly Func<int, int, int> _climate;
    private readonly DaggerfallHorizonTuning _tuning;
    private Frame _frame;

    /// <param name="climate">The climate value at an in-bounds map pixel.</param>
    internal DaggerfallHorizonGround(DaggerfallTerrainSet terrain, Func<int, int, int> climate, DaggerfallHorizonTuning tuning)
    {
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _climate = climate ?? throw new ArgumentNullException(nameof(climate));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _frame = new(0, 0, 0, tuning.Lift, false);
        // The samples are read on first use, which is content work no worker may do.
        _ = terrain.Samples;
    }

    /// <summary>The rings, finest first, in metres; the coarsest reaches past where the haze has closed.</summary>
    internal static HeightfieldRing[] Rings { get; } =
        [new(FineCellMetres, FineReachMetres), new(MiddleCellMetres, MiddleReachMetres), new(CoarseCellMetres, CoarseReachMetres)];

    /// <summary>The frame the workers sample by; set before the tiles it changes are invalidated.</summary>
    internal Frame Current
    {
        get => Volatile.Read(ref _frame);
        set => Volatile.Write(ref _frame, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public void Sample(HeightfieldTileRequest tile, HeightfieldTileSamples samples)
    {
        Frame now = Current;
        int heightSide = tile.HeightSide, side = tile.Side;
        double[] ground = new double[heightSide * heightSide];
        Dictionary<(int X, int Y), DaggerfallTerrainWindow> windows = [];
        for (int j = -1; j <= side; j++)
        for (int i = -1; i <= side; i++)
        {
            double x = tile.SampleX(i), z = tile.SampleZ(j);
            double height = Height(x, z, windows);
            ground[((j + 1) * heightSide) + i + 1] = height;
            samples.SetHeight(i, j, (float)Lifted(height, x, z, now));
        }

        for (int j = 0; j < side; j++)
        for (int i = 0; i < side; i++)
        {
            int at = ((j + 1) * heightSide) + i + 1;
            double slopeX = (ground[at + 1] - ground[at - 1]) / (2 * tile.CellSize);
            double slopeZ = (ground[at + heightSide] - ground[at - heightSide]) / (2 * tile.CellSize);
            (int mapX, int mapY) = MapPixel(tile.SampleX(i), tile.SampleZ(j));
            samples.SetColor(i, j, Paint(Climate(mapX, mapY), ground[at], Math.Sqrt((slopeX * slopeX) + (slopeZ * slopeZ)), now.Winter));
        }
    }

    /// <summary>The walked ground's height at a point, before any location flattens it.</summary>
    internal double Height(double x, double z) => Height(x, z, []);

    /// <summary>A height as the horizon draws it: only ground above the player's is lifted, more with distance.</summary>
    internal double Lifted(double height, double x, double z, Frame frame)
    {
        double above = height - frame.BaseMetres;
        if (above <= 0 || frame.Lift == 1) return height;
        double away = Math.Max(Math.Abs(x - frame.CentreX), Math.Abs(z - frame.CentreZ));
        double share = Math.Clamp((away - _tuning.LiftFromMetres) / (_tuning.LiftFullMetres - _tuning.LiftFromMetres), 0, 1);
        share = share * share * (3 - (2 * share));
        return frame.BaseMetres + (above * (1 + ((frame.Lift - 1) * share)));
    }

    /// <summary>The colour of far ground: the climate's own, bare where it is steep, white where it is high or wintry.</summary>
    internal Color Paint(int climate, double height, double slope, bool winter)
    {
        if (height <= OceanMetres + SeaMarginMetres) return _tuning.Sea;
        if (!_tuning.Climates.TryGetValue(climate, out Color ground))
            throw new InvalidOperationException($"The horizon has no colour for climate {climate}.");
        Color painted = Mix(ground, _tuning.Rock, Share(slope, _tuning.RockFromSlope, _tuning.RockFullSlope));
        float snow = Share(height, _tuning.SnowFromMetres, _tuning.SnowFullMetres);
        if (winter && climate is not (224 or 225)) snow = Math.Max(snow, WinterSnow);
        return Mix(painted, _tuning.Snow, snow);
    }

    private double Height(double x, double z, Dictionary<(int X, int Y), DaggerfallTerrainWindow> windows)
    {
        // The vertex the point falls on: a pixel's rows run north from its y, so its row 0 is the pixel's own z.
        long vertexX = (long)Math.Round(x / Spacing), vertexZ = (long)Math.Round(z / Spacing);
        int mapX = (int)Math.Floor(vertexX / (double)TileDimension);
        int mapY = (int)Math.Ceiling(vertexZ / (double)TileDimension);
        if (!windows.TryGetValue((mapX, mapY), out DaggerfallTerrainWindow? window))
            windows.Add((mapX, mapY), window = DaggerfallTerrainSurfaceBuilder.ReadWindow(_terrain, mapX, mapY));
        return DaggerfallTerrainSurfaceBuilder.NormalizedHeight(window, (int)(vertexX - ((long)mapX * TileDimension)),
            (int)(((long)mapY * TileDimension) - vertexZ)) * DaggerfallTerrainSurfaceBuilder.TerrainVerticalSize;
    }

    private static (int X, int Y) MapPixel(double x, double z) => (
        (int)Math.Floor(x / DaggerfallTerrainSurfaceBuilder.HorizontalSize),
        (int)Math.Ceiling(z / DaggerfallTerrainSurfaceBuilder.HorizontalSize));

    private int Climate(int mapX, int mapY) =>
        _climate(Math.Clamp(mapX, 0, _terrain.Width - 1), Math.Clamp(mapY, 0, _terrain.Height - 1));

    private static float Share(double value, double from, double full) => (float)Math.Clamp((value - from) / (full - from), 0, 1);

    private static Color Mix(Color from, Color to, float share) => new(
        from.R + ((to.R - from.R) * share), from.G + ((to.G - from.G) * share), from.B + ((to.B - from.B) * share), 1);
}
