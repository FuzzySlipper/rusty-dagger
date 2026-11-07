using System.Buffers.Binary;
using System.Diagnostics;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>One location the relief calms the ground under.</summary>
/// <param name="MapPixelX">Its map pixel's column.</param>
/// <param name="MapPixelY">Its map pixel's row.</param>
/// <param name="Blocks">How many RMB blocks it covers.</param>
public sealed record ReliefPlace(int MapPixelX, int MapPixelY, int Blocks);

/// <summary>What the relief is baked from.</summary>
/// <param name="Width">The map's width in pixels.</param>
/// <param name="Height">The map's height in pixels.</param>
/// <param name="Classic">Each pixel's classic height in metres; the ocean clamp marks the sea.</param>
/// <param name="Rain">How much rain each pixel takes, 1 being the map's ordinary.</param>
/// <param name="Places">The locations to calm the ground under.</param>
public sealed record ReliefInput(int Width, int Height, double[] Classic, double[] Rain, IReadOnlyList<ReliefPlace> Places);

/// <summary>One lattice of the bake's cascade.</summary>
/// <param name="NodesPerPixel">Lattice nodes along one map pixel.</param>
/// <param name="Rounds">How many times the uplift is calibrated back to the classic heights.</param>
/// <param name="Steps">Evolution steps in each round.</param>
public sealed record ReliefStage(int NodesPerPixel, int Rounds, int Steps);

/// <summary>
/// The baked landscape: a lattice of heights in metres, and how much of them stands at each node of a coarser lattice, from
/// 0 (the classic height) to 255. Node (i, j) of either lattice is heightmap pixel (i / its nodes per pixel, j / the same).
/// </summary>
public sealed record DaggerfallRelief(int Width, int Height, float[] Metres, int ShareWidth, int ShareHeight, byte[] Share)
{
    /// <summary>The rivers its drainage became.</summary>
    public DaggerfallRivers Rivers { get; init; } = new([]);

    /// <summary>Where the relief is published, below the content root.</summary>
    public const string RelativePath = "worldrpg/terrain/relief.bin";
    /// <summary>A height's published steps to the metre: an eighth of a metre, finer than any lattice's cell can show.</summary>
    public const double StepsPerMetre = 8;
    private const int HeaderBytes = 20;

    /// <summary>The published form: a magic, both lattices' sizes, the heights row by row in steps, then the shares.</summary>
    public byte[] Encode()
    {
        int count = checked(Width * Height);
        byte[] bytes = new byte[HeaderBytes + (count * sizeof(ushort)) + Share.Length];
        "RLF2"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), Width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), Height);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), ShareWidth);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), ShareHeight);
        for (int i = 0; i < count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(HeaderBytes + (i * sizeof(ushort))), checked((ushort)Math.Round(Metres[i] * StepsPerMetre)));
        Share.CopyTo(bytes, HeaderBytes + (count * sizeof(ushort)));
        return bytes;
    }
}

/// <summary>
/// Bakes WOODS into a drained, eroded landscape. Stream-power landscape evolution (Braun and Willett's implicit solver)
/// runs against an uplift calibrated back to the classic heights, with the coast fixed, so the Iliac Bay keeps its shape
/// and gains valleys and ridges. Climate sets the rain, every location gets calmer ground, and the classic height
/// returns along the coast so shores stay where they are. Each node's water is shared among all its lower neighbours by
/// slope: a single receiver cuts every hillside into ribs along the lattice. The landscape is evolved on a coarse lattice,
/// where the calibration closes on the classic heights, then carried onto finer ones, each cutting detail the last could
/// not hold. All noise comes from coordinate hashes, so a bake depends on its input alone.
/// </summary>
public static partial class ReliefBake
{
    /// <summary>Lattice nodes along one map pixel of the coarsest stage.</summary>
    public const int NodesPerPixel = 2;
    public const int DefaultRounds = 6, DefaultSteps = 45;
    public static readonly IReadOnlyList<ReliefStage> DefaultStages = [new(NodesPerPixel, DefaultRounds, DefaultSteps), new(NodesPerPixel * 2, 2, 60)];
    private const double PixelMetres = 819.2;
    private const double HeightScale = 8, SampleScale = 4, OceanSource = 27.2, MaximumSource = 1539, TerrainScale = 1.5;
    /// <summary>The classic ocean clamp in metres.</summary>
    public const double OceanMetres = OceanSource * TerrainScale;
    /// <summary>A height this close above the clamp is the sea.</summary>
    private const double SeaMargin = .01;
    /// <summary>The lattice <see cref="HillslopeRate"/> is stated at, and the most of it a stable pass can take.</summary>
    private const int CreepNodesPerPixel = 4;
    private const double MostCreepPerPass = .2;
    private const double YearsPerStep = 2e4, Erodibility = 2e-5, HillslopeRate = .1, FillRise = 1e-3;
    /// <summary>Metres of classic height a node's uplift stands for, in metres of uplift a year.</summary>
    private const double UpliftMetres = 1_500, UpliftPerYear = 1e-3, LeastUpliftHeight = 10;
    /// <summary>The nodes either side the calibration compares broad heights over, and its calming of the comparison.</summary>
    private const double CalibrationSigma = 6, CalibrationFloor = 40, LeastCorrection = .5, MostCorrection = 2.5;
    private const double ClassicSmoothing = 1.5, RainSmoothing = 3, LeastRain = .2, MostRain = 2;
    private const double HardnessContrast = .35, LeastHardness = .4, MostHardness = 1.8;
    private const double PadMetres = 900, PadCalmSpread = 1.5;
    private const int PadBlockCap = 16;
    private const double ShoreFromMetres = 300, ShoreFullMetres = 1_800;
    private const ulong HardnessSalt = 0x51D7_33A8_C6E0_9B4FUL, TextureSalt = 0x3A1C_75E9_02B4_D86FUL;
    private static readonly (int X, int Y, double Length)[] Neighbours =
    [
        (-1, -1, Math.Sqrt(2)), (0, -1, 1), (1, -1, Math.Sqrt(2)), (-1, 0, 1), (1, 0, 1), (-1, 1, Math.Sqrt(2)), (0, 1, 1), (1, 1, Math.Sqrt(2)),
    ];

    /// <summary>A pixel's classic height in metres, from its heightmap value and its cell's centre sample.</summary>
    public static double ClassicMetres(byte heightmap, byte centreSample) =>
        Math.Clamp((heightmap * HeightScale) + (centreSample * SampleScale), OceanSource, MaximumSource) * TerrainScale;

    /// <summary>The bake's input from the source wilderness, the published climate grid and the locations.</summary>
    public static ReliefInput Input(WoodsFile woods, DaggerfallClimateGrid climate, DaggerfallLocations locations)
    {
        ArgumentNullException.ThrowIfNull(woods);
        ArgumentNullException.ThrowIfNull(climate);
        ArgumentNullException.ThrowIfNull(locations);
        const int width = WoodsReader.MapWidth, height = WoodsReader.MapHeight, centre = WoodsReader.CellSamples / 2;
        double[] classic = new double[width * height], rain = new double[width * height];
        for (int i = 0; i < classic.Length; i++) classic[i] = ClassicMetres(woods.Heightmap[i], woods.Cells[i].Samples[centre]);
        Array.Fill(rain, 1);
        foreach (DaggerfallGridRow row in climate.Rows)
        {
            // The grid's first column is a sentinel, so its column x is map pixel x - 1.
            int x = -1;
            foreach (DaggerfallGridRun run in row.Runs)
            {
                for (int i = Math.Max(x, 0); i < Math.Min(x + run.Count, width); i++) rain[(row.Y * width) + i] = Rain(run.Value);
                x += run.Count;
            }
        }

        ReliefPlace[] places = [.. locations.Locations.Where(location => location.Exterior is not null)
            .Select(location => new ReliefPlace(location.Exterior!.MapPixelX, location.Exterior.MapPixelY, location.Exterior.Width * location.Exterior.Height))];
        return new(width, height, classic, rain, places);
    }

    /// <summary>Bakes a map; <paramref name="report"/> hears each calibration round.</summary>
    public static DaggerfallRelief Bake(ReliefInput input, int rounds = DefaultRounds, int steps = DefaultSteps, double sourceSquareKilometres = DefaultSourceSquareKilometres, Action<string>? report = null) =>
        Bake(input, [new(NodesPerPixel, rounds, steps)], sourceSquareKilometres, report);

    /// <summary>Bakes a map through a cascade of lattices, each starting from the last one's landscape.</summary>
    public static DaggerfallRelief Bake(ReliefInput input, IReadOnlyList<ReliefStage> stages, double sourceSquareKilometres = DefaultSourceSquareKilometres, Action<string>? report = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        double[]? height = null, uplift = null, hardness = null;
        Lattice lattice = default;
        bool[] sea = [];
        double[] rain = [];
        foreach (ReliefStage stage in stages)
        {
            Stopwatch clock = Stopwatch.StartNew();
            Lattice coarse = lattice;
            lattice = new(input.Width * stage.NodesPerPixel, input.Height * stage.NodesPerPixel, stage.NodesPerPixel);
            int cols = lattice.Cols, rows = lattice.Rows, count = cols * rows;
            sea = SeaOf(lattice, input);

            double[] target = RefinePixels(lattice, Gaussian(input.Classic, input.Width, input.Height, ClassicSmoothing), input.Width, input.Height);
            for (int i = 0; i < count; i++) target[i] = sea[i] ? OceanMetres : Math.Max(target[i], OceanMetres);
            rain = RefinePixels(lattice, Gaussian(input.Rain, input.Width, input.Height, RainSmoothing), input.Width, input.Height);
            for (int i = 0; i < count; i++) rain[i] = Math.Clamp(rain[i], LeastRain, MostRain);

            if (height is null)
            {
                hardness = Noise(cols, rows, HardnessSalt, [60, 25, 10]);
                double[] texture = Noise(cols, rows, TextureSalt, [12, 5, 2]);
                height = new double[count];
                uplift = new double[count];
                for (int i = 0; i < count; i++)
                {
                    height[i] = sea[i] ? OceanMetres : (target[i] * .5) + 5 + (6 * Math.Abs(texture[i]));
                    uplift[i] = sea[i] ? 0 : Math.Max(target[i], LeastUpliftHeight) / UpliftMetres * UpliftPerYear;
                }
            }
            else
            {
                height = Finer(coarse, lattice, height);
                uplift = Finer(coarse, lattice, uplift!);
                hardness = Finer(coarse, lattice, hardness!);
                for (int i = 0; i < count; i++)
                {
                    if (!sea[i]) continue;
                    height[i] = OceanMetres;
                    uplift[i] = 0;
                }
            }

            double[] erodibility = new double[count];
            for (int i = 0; i < count; i++) erodibility[i] = Erodibility / Math.Clamp(1 + (HardnessContrast * hardness![i]), LeastHardness, MostHardness);
            double sigma = CalibrationSigma * stage.NodesPerPixel / NodesPerPixel;
            double[] wanted = Gaussian(target, cols, rows, sigma);
            Evolution evolution = new(lattice, sea, erodibility, rain);
            report?.Invoke($"relief lattice {cols} by {rows}: set up in {clock.Elapsed.TotalSeconds:F1} s");
            for (int round = 0; round < stage.Rounds; round++)
            {
                for (int step = 0; step < stage.Steps; step++) evolution.Step(height, uplift!);
                double[] smooth = Gaussian(height, cols, rows, sigma);
                double square = 0, land = 0;
                for (int i = 0; i < count; i++)
                {
                    if (sea[i]) continue;
                    double correction = Math.Clamp((wanted[i] + CalibrationFloor) / (smooth[i] + CalibrationFloor), LeastCorrection, MostCorrection);
                    // A little over the plain ratio, so the rounds close on the classic heights sooner.
                    uplift![i] *= correction * Math.Sqrt(Math.Sqrt(correction));
                    square += (smooth[i] - wanted[i]) * (smooth[i] - wanted[i]);
                    land++;
                }

                report?.Invoke($"relief round {round + 1} of {stage.Rounds}: peak {height.Max():F0} m, {Math.Sqrt(square / Math.Max(land, 1)):F0} m from the classic's broad heights, {clock.Elapsed.TotalSeconds:F0} s");
            }
        }

        DaggerfallRivers rivers = TraceRivers(lattice, height!, sea, rain, input.Places, sourceSquareKilometres, report);
        CalmPads(lattice, height!, input.Places);
        LayFloodplains(lattice, height!, sea, rivers);
        float[] metres = new float[height!.Length];
        for (int i = 0; i < metres.Length; i++) metres[i] = (float)height[i];
        Lattice shares = new(input.Width * NodesPerPixel, input.Height * NodesPerPixel, NodesPerPixel);
        return new(lattice.Cols, lattice.Rows, metres, shares.Cols, shares.Rows, Shares(shares, SeaOf(shares, input))) { Rivers = rivers };
    }

    private static bool[] SeaOf(Lattice lattice, ReliefInput input)
    {
        bool[] sea = new bool[lattice.Cols * lattice.Rows];
        for (int j = 0; j < lattice.Rows; j++)
        for (int i = 0; i < lattice.Cols; i++)
            sea[(j * lattice.Cols) + i] = input.Classic[((j / lattice.PerPixel) * input.Width) + (i / lattice.PerPixel)] <= OceanMetres + SeaMargin;
        return sea;
    }

    private static double Rain(int climate) => climate switch
    {
        224 => .35, 225 => .5, 226 => .9, 227 => 1.7, 228 => 1.5, 230 or 231 => 1.2, 232 => 1.1, _ => 1,
    };

    /// <summary>Calms the ground under every location, so its flattening doesn't stand on a cliff.</summary>
    private static void CalmPads(Lattice lattice, double[] height, IReadOnlyList<ReliefPlace> places)
    {
        double[] weight = new double[height.Length];
        foreach (ReliefPlace place in places)
        {
            // A location stands in the surface of map pixel (X, Y): between heightmap pixels (X - 1, Y - 1) and (X, Y).
            int i = (int)((place.MapPixelX - .5) * lattice.PerPixel), j = (int)((place.MapPixelY - .5) * lattice.PerPixel);
            if (i < 0 || j < 0 || i >= lattice.Cols || j >= lattice.Rows) continue;
            int at = (j * lattice.Cols) + i;
            weight[at] = Math.Max(weight[at], 1 + (Math.Min(place.Blocks, PadBlockCap) / 4.0));
        }

        double reach = PadMetres / lattice.NodeMetres;
        double[] spread = Gaussian(weight, lattice.Cols, lattice.Rows, reach);
        double[] calm = Gaussian(height, lattice.Cols, lattice.Rows, reach * PadCalmSpread);
        double gain = Math.Tau * reach * reach;
        for (int i = 0; i < height.Length; i++) height[i] += (calm[i] - height[i]) * Math.Clamp(spread[i] * gain, 0, 1);
    }

    /// <summary>How much of the relief stands at each node: none near the sea, all of it inland.</summary>
    private static byte[] Shares(Lattice lattice, bool[] sea)
    {
        double[] distance = DistanceToSea(lattice, sea);
        byte[] share = new byte[sea.Length];
        for (int i = 0; i < share.Length; i++)
        {
            double t = Math.Clamp(((distance[i] * lattice.NodeMetres) - ShoreFromMetres) / (ShoreFullMetres - ShoreFromMetres), 0, 1);
            share[i] = (byte)Math.Round(t * t * (3 - (2 * t)) * 255);
        }

        return share;
    }

    /// <summary>Each node's exact distance to the sea in nodes, by two passes of the lower envelope of parabolas.</summary>
    private static double[] DistanceToSea(Lattice lattice, bool[] sea)
    {
        const double far = 1e12;
        int cols = lattice.Cols, rows = lattice.Rows;
        double[] square = new double[sea.Length];
        for (int i = 0; i < sea.Length; i++) square[i] = sea[i] ? 0 : far;
        Parallel.For(0, cols, x =>
        {
            double[] line = new double[rows];
            for (int y = 0; y < rows; y++) line[y] = square[(y * cols) + x];
            Parabolas(line);
            for (int y = 0; y < rows; y++) square[(y * cols) + x] = line[y];
        });
        Parallel.For(0, rows, y =>
        {
            double[] line = new double[cols];
            Array.Copy(square, y * cols, line, 0, cols);
            Parabolas(line);
            Array.Copy(line, 0, square, y * cols, cols);
        });
        for (int i = 0; i < square.Length; i++) square[i] = Math.Sqrt(square[i]);
        return square;
    }

    private static void Parabolas(double[] line)
    {
        int n = line.Length, k = 0;
        int[] vertex = new int[n];
        double[] boundary = new double[n + 1], result = new double[n];
        boundary[0] = double.NegativeInfinity;
        boundary[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s;
            while (true)
            {
                int p = vertex[k];
                s = ((line[q] + ((double)q * q)) - (line[p] + ((double)p * p))) / (2.0 * (q - p));
                if (s > boundary[k] || k == 0) break;
                k--;
            }

            k++;
            vertex[k] = q;
            boundary[k] = s;
            boundary[k + 1] = double.PositiveInfinity;
        }

        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (boundary[k + 1] < q) k++;
            double d = q - vertex[k];
            result[q] = (d * d) + line[vertex[k]];
        }

        Array.Copy(result, line, n);
    }

    /// <summary>A field on map pixels, on the lattice.</summary>
    private static double[] RefinePixels(Lattice lattice, double[] field, int width, int height) => Refine(lattice.Cols, lattice.Rows, lattice.PerPixel, field, width, height);

    /// <summary>A coarser lattice's field on a finer one.</summary>
    private static double[] Finer(Lattice coarse, Lattice fine, double[] field) => Refine(fine.Cols, fine.Rows, fine.PerPixel / coarse.PerPixel, field, coarse.Cols, coarse.Rows);

    /// <summary>A coarse field on a finer grid, by a Catmull-Rom surface through its samples.</summary>
    private static double[] Refine(int cols, int rows, int per, double[] field, int width, int height)
    {
        double[] fine = new double[cols * rows];
        Parallel.For(0, rows, j =>
        {
            int iy = j / per;
            double fy = (j % per) / (double)per;
            Span<double> column = stackalloc double[4];
            for (int i = 0; i < cols; i++)
            {
                int ix = i / per;
                double fx = (i % per) / (double)per;
                for (int r = 0; r < 4; r++)
                {
                    int row = Math.Clamp(iy + r - 1, 0, height - 1) * width;
                    column[r] = Curve(field[row + Math.Clamp(ix - 1, 0, width - 1)], field[row + ix],
                        field[row + Math.Min(ix + 1, width - 1)], field[row + Math.Min(ix + 2, width - 1)], fx);
                }

                fine[(j * cols) + i] = Curve(column[0], column[1], column[2], column[3], fy);
            }
        });
        return fine;
    }

    private static double Curve(double a, double b, double c, double d, double t) =>
        b + (.5 * t * (c - a + (t * ((2 * a) - (5 * b) + (4 * c) - d + (t * ((3 * (b - c)) + d - a))))));

    /// <summary>Fractal noise: hashed white noise blurred at each spread, coarse first, scaled to a deviation of one.</summary>
    private static double[] Noise(int cols, int rows, ulong salt, int[] spreads)
    {
        double[] total = new double[cols * rows], white = new double[cols * rows];
        double amplitude = 1;
        foreach (int spread in spreads)
        {
            ulong seed = salt + (ulong)spread;
            for (int i = 0; i < white.Length; i++) white[i] = (Unit(seed, (ulong)i) * 2) - 1;
            double[] blurred = Gaussian(white, cols, rows, spread);
            for (int i = 0; i < total.Length; i++) total[i] += amplitude * blurred[i] * spread;
            amplitude *= .6;
        }

        double mean = 0, square = 0;
        foreach (double value in total) mean += value;
        mean /= total.Length;
        foreach (double value in total) square += (value - mean) * (value - mean);
        double deviation = Math.Sqrt(square / total.Length);
        for (int i = 0; i < total.Length; i++) total[i] /= deviation;
        return total;
    }

    /// <summary>A separable Gaussian blur, reflecting at the field's edges.</summary>
    private static double[] Gaussian(double[] field, int cols, int rows, double sigma)
    {
        int radius = (int)((sigma * 4) + .5);
        double[] kernel = new double[radius + 1];
        double sum = 0;
        for (int i = 0; i <= radius; i++)
        {
            kernel[i] = Math.Exp(-.5 * i * i / (sigma * sigma));
            sum += i == 0 ? kernel[i] : 2 * kernel[i];
        }

        for (int i = 0; i <= radius; i++) kernel[i] /= sum;
        double[] across = new double[field.Length], result = new double[field.Length];
        Parallel.For(0, rows, y =>
        {
            int row = y * cols;
            for (int x = 0; x < cols; x++)
            {
                double value = field[row + x] * kernel[0];
                for (int k = 1; k <= radius; k++) value += kernel[k] * (field[row + Reflect(x - k, cols)] + field[row + Reflect(x + k, cols)]);
                across[row + x] = value;
            }
        });
        Parallel.For(0, rows, y =>
        {
            for (int x = 0; x < cols; x++)
            {
                double value = across[(y * cols) + x] * kernel[0];
                for (int k = 1; k <= radius; k++)
                    value += kernel[k] * (across[(Reflect(y - k, rows) * cols) + x] + across[(Reflect(y + k, rows) * cols) + x]);
                result[(y * cols) + x] = value;
            }
        });
        return result;
    }

    private static int Reflect(int at, int extent)
    {
        while (at < 0 || at >= extent) at = at < 0 ? -at - 1 : (2 * extent) - at - 1;
        return at;
    }

    /// <summary>A value from 0 up to 1 for a seed and a position.</summary>
    private static double Unit(ulong seed, ulong position)
    {
        ulong value = seed + (position * 0x9E37_79B9_7F4A_7C15UL);
        value = (value ^ (value >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D0_49BB_1331_11EBUL;
        return ((value ^ (value >> 31)) >> 11) * (1.0 / (1UL << 53));
    }

    private readonly record struct Lattice(int Cols, int Rows, int PerPixel)
    {
        internal double NodeMetres => PixelMetres / PerPixel;
    }

    /// <summary>One landscape's evolution: its working arrays, kept across steps.</summary>
    private sealed class Evolution
    {
        private readonly Lattice _lattice;
        private readonly bool[] _sea, _root;
        private readonly double[] _erodibility, _rain, _area, _next;
        private readonly int[] _receiver, _order, _shore;
        private readonly bool[] _done;
        private int _orderCount;

        internal Evolution(Lattice lattice, bool[] sea, double[] erodibility, double[] rain)
        {
            _lattice = lattice;
            _sea = sea;
            _erodibility = erodibility;
            _rain = rain;
            int count = sea.Length;
            _root = new bool[count];
            _done = new bool[count];
            _area = new double[count];
            _next = new double[count];
            _receiver = new int[count];
            _order = new int[count];
            int cols = lattice.Cols, rows = lattice.Rows;
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                _root[(y * cols) + x] = sea[(y * cols) + x] || x == 0 || y == 0 || x == cols - 1 || y == rows - 1;
            // Only roots beside land start the flood: the rest of the sea has nothing to reach.
            List<int> shore = [];
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                if (!_root[(y * cols) + x]) continue;
                foreach ((int dx, int dy, _) in Neighbours)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows || _root[(ny * cols) + nx]) continue;
                    shore.Add((y * cols) + x);
                    break;
                }
            }

            _shore = [.. shore];
        }

        internal int[] Receiver => _receiver;
        /// <summary>The nodes the flood reached, lowest first.</summary>
        internal ReadOnlySpan<int> Order => _order.AsSpan(0, _orderCount);
        internal double[] Area => _area;

        /// <summary>Routes water down a finished landscape without changing it: each node to its steepest way down, and the catchments summed.</summary>
        internal void Drain(double[] height)
        {
            Fill(height);
            int cols = _lattice.Cols, rows = _lattice.Rows;
            Parallel.For(0, rows, y =>
            {
                for (int x = 0; x < cols; x++)
                {
                    int i = (y * cols) + x;
                    if (_receiver[i] < 0) continue;
                    double steepest = 0;
                    foreach ((int dx, int dy, double length) in Neighbours)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                        double slope = (height[i] - height[(ny * cols) + nx]) / length;
                        if (slope <= steepest) continue;
                        steepest = slope;
                        _receiver[i] = (ny * cols) + nx;
                    }
                }
            });
            Accumulate();
        }

        /// <summary>One step: fill, route, then erode downstream first, and let the hillslopes creep.</summary>
        internal void Step(double[] height, double[] uplift)
        {
            Fill(height);
            SpreadFlow(height);
            Erode(height, uplift);
            Creep(height);
        }

        /// <summary>Priority-flood: every closed hollow is filled to its spill, and each node gets a way out lower than it.</summary>
        private void Fill(double[] height)
        {
            int cols = _lattice.Cols, rows = _lattice.Rows;
            Array.Copy(_root, _done, _root.Length);
            Array.Fill(_receiver, -1);
            PriorityQueue<int, double> open = new(_shore.Length * 2);
            Queue<int> hollow = new();
            foreach (int at in _shore) open.Enqueue(at, height[at]);
            int popped = 0;
            while (hollow.TryDequeue(out int i) || open.TryDequeue(out i, out _))
            {
                _order[popped++] = i;
                double level = height[i];
                int y = i / cols, x = i - (y * cols);
                foreach ((int dx, int dy, _) in Neighbours)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                    int j = (ny * cols) + nx;
                    if (_done[j]) continue;
                    _done[j] = true;
                    _receiver[j] = i;
                    if (height[j] <= level + FillRise)
                    {
                        height[j] = level + FillRise;
                        hollow.Enqueue(j);
                    }
                    else open.Enqueue(j, height[j]);
                }
            }

            _orderCount = popped;
        }

        /// <summary>Sums each node's rain-weighted catchment down its one receiver, from the top of the flood's order.</summary>
        private void Accumulate()
        {
            for (int i = 0; i < _receiver.Length; i++) _area[i] = _lattice.NodeMetres * _lattice.NodeMetres * _rain[i];
            for (int c = _orderCount - 1; c >= 0; c--)
            {
                int i = _order[c], r = _receiver[i];
                if (r >= 0) _area[r] += _area[i];
            }
        }

        /// <summary>Sums the catchments with each node's water shared among every lower neighbour by slope, which keeps a smooth hillside smooth.</summary>
        private void SpreadFlow(double[] height)
        {
            int cols = _lattice.Cols, rows = _lattice.Rows;
            for (int i = 0; i < _area.Length; i++) _area[i] = _lattice.NodeMetres * _lattice.NodeMetres * _rain[i];
            Span<double> share = stackalloc double[8];
            for (int c = _orderCount - 1; c >= 0; c--)
            {
                int i = _order[c], y = i / cols, x = i - (y * cols);
                if (_root[i]) continue;
                double total = 0, here = height[i];
                for (int n = 0; n < 8; n++)
                {
                    (int dx, int dy, double length) = Neighbours[n];
                    share[n] = Share(here - height[((y + dy) * cols) + x + dx], length);
                    total += share[n];
                }

                if (total <= 0) continue;
                for (int n = 0; n < 8; n++)
                    if (share[n] > 0) _area[((y + Neighbours[n].Y) * cols) + x + Neighbours[n].X] += _area[i] * share[n] / total;
            }
        }

        /// <summary>How much of a node's water a neighbour takes, by the slope down to it to the power of one and a half.</summary>
        private static double Share(double drop, double length)
        {
            if (drop <= 0) return 0;
            double slope = drop / length;
            return slope * Math.Sqrt(slope);
        }

        /// <summary>The implicit stream-power step over every lower neighbour, each taking its share of the node's water, from the bottom of the flood's order.</summary>
        private void Erode(double[] height, double[] uplift)
        {
            int cols = _lattice.Cols;
            Span<double> share = stackalloc double[8];
            for (int c = 0; c < _orderCount; c++)
            {
                int i = _order[c], y = i / cols, x = i - (y * cols);
                if (_root[i]) continue;
                double total = 0, here = height[i];
                for (int n = 0; n < 8; n++)
                {
                    (int dx, int dy, double length) = Neighbours[n];
                    share[n] = Share(here - height[((y + dy) * cols) + x + dx], length);
                    total += share[n];
                }

                double raised = here + (uplift[i] * YearsPerStep);
                if (total <= 0)
                {
                    height[i] = raised;
                    continue;
                }

                double rate = _erodibility[i] * YearsPerStep * Math.Sqrt(_area[i]) / _lattice.NodeMetres, pull = 0, sum = 0;
                for (int n = 0; n < 8; n++)
                {
                    if (share[n] <= 0) continue;
                    (int dx, int dy, double length) = Neighbours[n];
                    double f = rate * (share[n] / total) / length;
                    pull += f;
                    sum += f * height[((y + dy) * cols) + x + dx];
                }

                height[i] = (raised + sum) / (1 + pull);
            }
        }

        private void Creep(double[] height)
        {
            int cols = _lattice.Cols, rows = _lattice.Rows;
            // Creep is a rate in square metres, so finer nodes take more of it: as many passes as keep each one stable.
            double rate = HillslopeRate * Math.Max(Math.Pow(_lattice.PerPixel / (double)CreepNodesPerPixel, 2), 1);
            int passes = (int)Math.Ceiling(rate / MostCreepPerPass);
            rate /= passes;
            for (int pass = 0; pass < passes; pass++)
            {
                Parallel.For(0, rows, y =>
                {
                    int up = Math.Max(y - 1, 0) * cols, down = Math.Min(y + 1, rows - 1) * cols, row = y * cols;
                    for (int x = 0; x < cols; x++)
                    {
                        int i = row + x;
                        double here = height[i];
                        _next[i] = _sea[i] ? here : here + (rate * (height[up + x] + height[down + x]
                            + height[row + Math.Max(x - 1, 0)] + height[row + Math.Min(x + 1, cols - 1)] - (4 * here)));
                    }
                });
                Array.Copy(_next, height, height.Length);
            }
        }
    }
}
