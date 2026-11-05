using System.Buffers.Binary;
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

/// <summary>
/// The baked landscape: a lattice of heights in metres, and how much of them stands at each node, from 0 (the classic
/// height) to 255. Node (i, j) is heightmap pixel (i / <see cref="ReliefBake.NodesPerPixel"/>, j / the same).
/// </summary>
public sealed record DaggerfallRelief(int Width, int Height, float[] Metres, byte[] Share)
{
    /// <summary>Where the relief is published, below the content root.</summary>
    public const string RelativePath = "worldrpg/terrain/relief.bin";
    private const int HeaderBytes = 12;

    /// <summary>The published form: a magic, the lattice's size, its heights row by row, then its shares.</summary>
    public byte[] Encode()
    {
        int count = checked(Width * Height);
        byte[] bytes = new byte[HeaderBytes + (count * 5)];
        "RLF1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), Width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), Height);
        for (int i = 0; i < count; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(HeaderBytes + (i * 4)), Metres[i]);
        Share.CopyTo(bytes, HeaderBytes + (count * 4));
        return bytes;
    }
}

/// <summary>
/// Bakes WOODS into a drained, eroded landscape. Stream-power landscape evolution (Braun and Willett's implicit solver)
/// runs against an uplift calibrated back to the classic heights, with the coast fixed, so the Iliac Bay keeps its shape
/// and gains valleys and ridges. Climate sets the rain, every location gets calmer ground, and the classic height
/// returns along the coast so shores stay where they are. Flow leaves down a random slope weighted by its steepness: a
/// fixed steepest neighbour lines valleys up on the lattice. All noise comes from coordinate hashes, so a bake depends
/// on its input alone.
/// </summary>
public static class ReliefBake
{
    /// <summary>Lattice nodes along one map pixel.</summary>
    public const int NodesPerPixel = 2;
    public const int DefaultRounds = 6, DefaultSteps = 45;
    private const double PixelMetres = 819.2;
    private const double NodeMetres = PixelMetres / NodesPerPixel;
    private const double HeightScale = 8, SampleScale = 4, OceanSource = 27.2, MaximumSource = 1539, TerrainScale = 1.5;
    /// <summary>The classic ocean clamp in metres.</summary>
    public const double OceanMetres = OceanSource * TerrainScale;
    /// <summary>A height this close above the clamp is the sea.</summary>
    private const double SeaMargin = .01;
    private const double YearsPerStep = 2e4, Erodibility = 2e-5, HillslopeRate = .06, FillRise = 1e-3;
    /// <summary>Metres of classic height a node's uplift stands for, in metres of uplift a year.</summary>
    private const double UpliftMetres = 1_500, UpliftPerYear = 1e-3, LeastUpliftHeight = 10;
    /// <summary>The nodes either side the calibration compares broad heights over, and its calming of the comparison.</summary>
    private const double CalibrationSigma = 6, CalibrationFloor = 40, LeastCorrection = .5, MostCorrection = 2.5;
    private const double ClassicSmoothing = 1.5, RainSmoothing = 3, LeastRain = .2, MostRain = 2;
    private const double HardnessContrast = .35, LeastHardness = .4, MostHardness = 1.8;
    private const double PadMetres = 900, PadCalmSpread = 1.5;
    private const int PadBlockCap = 16;
    private const double ShoreFromMetres = 300, ShoreFullMetres = 1_800;
    private const ulong HardnessSalt = 0x51D7_33A8_C6E0_9B4FUL, TextureSalt = 0x3A1C_75E9_02B4_D86FUL, FlowSalt = 0x9F5E_2C83_61A7_04BDUL;
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
    public static DaggerfallRelief Bake(ReliefInput input, int rounds = DefaultRounds, int steps = DefaultSteps, Action<string>? report = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        int cols = input.Width * NodesPerPixel, rows = input.Height * NodesPerPixel, count = cols * rows;
        Lattice lattice = new(cols, rows);
        bool[] sea = new bool[count];
        for (int j = 0; j < rows; j++)
        for (int i = 0; i < cols; i++)
            sea[(j * cols) + i] = input.Classic[((j / NodesPerPixel) * input.Width) + (i / NodesPerPixel)] <= OceanMetres + SeaMargin;

        double[] target = Refine(lattice, Gaussian(input.Classic, input.Width, input.Height, ClassicSmoothing), input.Width, input.Height);
        for (int i = 0; i < count; i++) target[i] = sea[i] ? OceanMetres : Math.Max(target[i], OceanMetres);
        double[] rain = Refine(lattice, Gaussian(input.Rain, input.Width, input.Height, RainSmoothing), input.Width, input.Height);
        for (int i = 0; i < count; i++) rain[i] = Math.Clamp(rain[i], LeastRain, MostRain);

        double[] hardness = Noise(cols, rows, HardnessSalt, [60, 25, 10]), texture = Noise(cols, rows, TextureSalt, [12, 5, 2]);
        double[] erodibility = new double[count], height = new double[count], uplift = new double[count];
        for (int i = 0; i < count; i++)
        {
            erodibility[i] = Erodibility / Math.Clamp(1 + (HardnessContrast * hardness[i]), LeastHardness, MostHardness);
            height[i] = sea[i] ? OceanMetres : (target[i] * .5) + 5 + (6 * Math.Abs(texture[i]));
            uplift[i] = sea[i] ? 0 : Math.Max(target[i], LeastUpliftHeight) / UpliftMetres * UpliftPerYear;
        }

        double[] wanted = Gaussian(target, cols, rows, CalibrationSigma);
        Evolution evolution = new(lattice, sea, erodibility, rain);
        for (int round = 0; round < rounds; round++)
        {
            for (int step = 0; step < steps; step++) evolution.Step(height, uplift, (ulong)((round * steps) + step));
            double[] smooth = Gaussian(height, cols, rows, CalibrationSigma);
            double square = 0, land = 0;
            for (int i = 0; i < count; i++)
            {
                if (sea[i]) continue;
                double correction = Math.Clamp((wanted[i] + CalibrationFloor) / (smooth[i] + CalibrationFloor), LeastCorrection, MostCorrection);
                // A little over the plain ratio, so the rounds close on the classic heights sooner.
                uplift[i] *= correction * Math.Sqrt(Math.Sqrt(correction));
                square += (smooth[i] - wanted[i]) * (smooth[i] - wanted[i]);
                land++;
            }

            report?.Invoke($"relief round {round + 1} of {rounds}: peak {height.Max():F0} m, {Math.Sqrt(square / Math.Max(land, 1)):F0} m from the classic's broad heights");
        }

        CalmPads(lattice, height, input.Places);
        float[] metres = new float[count];
        for (int i = 0; i < count; i++) metres[i] = (float)height[i];
        return new(cols, rows, metres, Shares(lattice, sea));
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
            int i = (int)((place.MapPixelX - .5) * NodesPerPixel), j = (int)((place.MapPixelY - .5) * NodesPerPixel);
            if (i < 0 || j < 0 || i >= lattice.Cols || j >= lattice.Rows) continue;
            int at = (j * lattice.Cols) + i;
            weight[at] = Math.Max(weight[at], 1 + (Math.Min(place.Blocks, PadBlockCap) / 4.0));
        }

        double reach = PadMetres / NodeMetres;
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
            double t = Math.Clamp(((distance[i] * NodeMetres) - ShoreFromMetres) / (ShoreFullMetres - ShoreFromMetres), 0, 1);
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

    /// <summary>A coarse field on the lattice, by a Catmull-Rom surface through its pixels.</summary>
    private static double[] Refine(Lattice lattice, double[] field, int width, int height)
    {
        double[] fine = new double[lattice.Cols * lattice.Rows];
        Parallel.For(0, lattice.Rows, j =>
        {
            int iy = j / NodesPerPixel;
            double fy = (j % NodesPerPixel) / (double)NodesPerPixel;
            Span<double> column = stackalloc double[4];
            for (int i = 0; i < lattice.Cols; i++)
            {
                int ix = i / NodesPerPixel;
                double fx = (i % NodesPerPixel) / (double)NodesPerPixel;
                for (int r = 0; r < 4; r++)
                {
                    int row = Math.Clamp(iy + r - 1, 0, height - 1) * width;
                    column[r] = Curve(field[row + Math.Clamp(ix - 1, 0, width - 1)], field[row + ix],
                        field[row + Math.Min(ix + 1, width - 1)], field[row + Math.Min(ix + 2, width - 1)], fx);
                }

                fine[(j * lattice.Cols) + i] = Curve(column[0], column[1], column[2], column[3], fy);
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

    private readonly record struct Lattice(int Cols, int Rows);

    /// <summary>One landscape's evolution: its working arrays, kept across steps.</summary>
    private sealed class Evolution
    {
        private readonly Lattice _lattice;
        private readonly bool[] _sea, _root;
        private readonly double[] _erodibility, _rain, _area, _next;
        private readonly int[] _receiver, _donors, _order, _shore;
        private readonly bool[] _done;

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
            _donors = new int[count];
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

        /// <summary>One step: fill, route, then erode downstream first, and let the hillslopes creep.</summary>
        internal void Step(double[] height, double[] uplift, ulong step)
        {
            Fill(height);
            ChooseReceivers(height, step);
            Accumulate();
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
            while (hollow.TryDequeue(out int i) || open.TryDequeue(out i, out _))
            {
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
        }

        private void ChooseReceivers(double[] height, ulong step)
        {
            int cols = _lattice.Cols, rows = _lattice.Rows;
            ulong seed = FlowSalt + (step * 0xD1B5_4A32_D192_ED03UL);
            Parallel.For(0, rows, y =>
            {
                for (int x = 0; x < cols; x++)
                {
                    int i = (y * cols) + x;
                    if (_receiver[i] < 0) continue;
                    double total = 0, here = height[i];
                    foreach ((int dx, int dy, double length) in Neighbours)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                        double drop = here - height[(ny * cols) + nx];
                        if (drop > 0) total += drop / length;
                    }

                    if (total <= 0) continue;
                    double pick = Unit(seed, (ulong)i) * total;
                    foreach ((int dx, int dy, double length) in Neighbours)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                        double drop = here - height[(ny * cols) + nx];
                        if (drop <= 0) continue;
                        pick -= drop / length;
                        if (pick > 0) continue;
                        _receiver[i] = (ny * cols) + nx;
                        break;
                    }
                }
            });
        }

        /// <summary>Sums each node's rain-weighted catchment, leaving <see cref="_order"/> upstream first.</summary>
        private void Accumulate()
        {
            Array.Clear(_donors);
            for (int i = 0; i < _receiver.Length; i++)
            {
                _area[i] = NodeMetres * NodeMetres * _rain[i];
                if (_receiver[i] >= 0) _donors[_receiver[i]]++;
            }

            int count = 0;
            for (int i = 0; i < _donors.Length; i++)
                if (_donors[i] == 0) _order[count++] = i;
            for (int c = 0; c < count; c++)
            {
                int i = _order[c], r = _receiver[i];
                if (r < 0) continue;
                _area[r] += _area[i];
                if (--_donors[r] == 0) _order[count++] = r;
            }

            if (count != _order.Length) throw new InvalidOperationException("Relief flow routing left a closed loop, so a catchment would be lost.");
        }

        private void Erode(double[] height, double[] uplift)
        {
            int cols = _lattice.Cols;
            for (int c = _order.Length - 1; c >= 0; c--)
            {
                int i = _order[c], r = _receiver[i];
                if (r < 0 || _sea[i]) continue;
                bool diagonal = i / cols != r / cols && i % cols != r % cols;
                double f = _erodibility[i] * YearsPerStep * Math.Sqrt(_area[i]) / (NodeMetres * (diagonal ? Math.Sqrt(2) : 1));
                double below = height[r];
                double lowered = (height[i] + (uplift[i] * YearsPerStep) + (f * below)) / (1 + f);
                height[i] = lowered > below ? lowered : below + FillRise;
            }
        }

        private void Creep(double[] height)
        {
            int cols = _lattice.Cols, rows = _lattice.Rows;
            Parallel.For(0, rows, y =>
            {
                int up = Math.Max(y - 1, 0) * cols, down = Math.Min(y + 1, rows - 1) * cols, row = y * cols;
                for (int x = 0; x < cols; x++)
                {
                    int i = row + x;
                    double here = height[i];
                    _next[i] = _sea[i] ? here : here + (HillslopeRate * (height[up + x] + height[down + x]
                        + height[row + Math.Max(x - 1, 0)] + height[row + Math.Min(x + 1, cols - 1)] - (4 * here)));
                }
            });
            Array.Copy(_next, height, height.Length);
        }
    }
}
