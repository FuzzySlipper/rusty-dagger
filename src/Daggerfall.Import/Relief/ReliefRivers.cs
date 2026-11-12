using System.Buffers.Binary;

namespace Daggerfall.Import.Relief;

/// <summary>One point along a river, in metres from the heightmap's corner.</summary>
/// <param name="X">Metres east.</param>
/// <param name="Z">Metres south.</param>
/// <param name="Surface">The water's height.</param>
/// <param name="HalfWidth">Half the channel's width.</param>
public readonly record struct DaggerfallRiverStation(float X, float Z, float Surface, float HalfWidth);

/// <summary>The rivers the bake drained the map into, each running from its head to the sea or the river it joins.</summary>
public sealed record DaggerfallRivers(IReadOnlyList<DaggerfallRiverStation[]> Courses)
{
    /// <summary>Where the rivers are published, below the content root.</summary>
    public const string RelativePath = "worldrpg/terrain/rivers.bin";
    private const int HeaderBytes = 8, StationBytes = 16;

    /// <summary>The published form: a magic, how many courses, each one's station count, then every station in order.</summary>
    public byte[] Encode()
    {
        int stations = Courses.Sum(course => course.Length);
        byte[] bytes = new byte[HeaderBytes + (Courses.Count * sizeof(int)) + (stations * StationBytes)];
        "RVR1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), Courses.Count);
        int at = HeaderBytes;
        foreach (DaggerfallRiverStation[] course in Courses)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), course.Length);
            at += sizeof(int);
        }

        foreach (DaggerfallRiverStation[] course in Courses)
        foreach (DaggerfallRiverStation station in course)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), station.X);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 4), station.Z);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 8), station.Surface);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 12), station.HalfWidth);
            at += StationBytes;
        }

        return bytes;
    }
}

/// <summary>
/// Reads rivers from a baked landscape's own drainage. Water is routed down the finished lattice with every location
/// walled off, since a location levels its map pixel and would level a channel with it. Wherever a catchment passes
/// <see cref="DefaultSourceSquareKilometres"/> of ordinary rain, a river starts, and the largest donor at each confluence
/// carries the trunk. Each course is smoothed off the lattice, its surface runs only downhill from its head, and a
/// tributary ends on the surface of what it joins. Rivers are read before the locations' pads calm the ground: a pad
/// blurs a valley's floor up into a dam.
/// </summary>
public static partial class ReliefBake
{
    /// <summary>The catchment of ordinary rain a river starts at.</summary>
    public const double DefaultSourceSquareKilometres = 150;
    private const double StationMetres = 48, LeastCourseMetres = 4_000, CourseSmoothingNodes = 2.5, SurfaceSmoothingMetres = 160;
    private const double LeastFall = 2e-5, WidthPerRootSquareKilometre = 1, LeastHalfWidth = 6, HeadHalfWidth = 1, HeadMetres = 3_000;
    private const double LocationWall = 1e4;
    private const double FloodplainPerHalfWidth = 6, FloodplainNodes = 2, FloodplainRise = 1;
    /// <summary>How far past the coast a mouth runs on, as the classic's own shore can stand short of the lattice's blocky sea.</summary>
    private const double MouthMetres = 900;

    private static DaggerfallRivers TraceRivers(Lattice lattice, double[] height, bool[] sea, double[] rain, IReadOnlyList<ReliefPlace> places, double sourceSquareKilometres, Action<string>? report)
    {
        int cols = lattice.Cols, count = height.Length;
        double[] routed = (double[])height.Clone();
        foreach (ReliefPlace place in places)
        {
            // A location levels map pixel (X, Y): the heightmap's square between pixels (X - 1, Y - 1) and (X, Y).
            int x0 = (place.MapPixelX - 1) * lattice.PerPixel, y0 = (place.MapPixelY - 1) * lattice.PerPixel;
            for (int y = Math.Max(y0, 0); y <= y0 + lattice.PerPixel && y < lattice.Rows; y++)
            for (int x = Math.Max(x0, 0); x <= x0 + lattice.PerPixel && x < cols; x++)
                if (!sea[(y * cols) + x]) routed[(y * cols) + x] = height[(y * cols) + x] + LocationWall;
        }

        Evolution drainage = new(lattice, sea, [], rain);
        drainage.Drain(routed);
        int[] receiver = drainage.Receiver;
        ReadOnlySpan<int> order = drainage.Order;
        double[] area = drainage.Area;
        double source = sourceSquareKilometres * 1e6;
        bool River(int i) => i >= 0 && !sea[i] && area[i] >= source && routed[i] < LocationWall;

        // The longest run of river above each node, so a stub can be told from a branch.
        double[] reach = new double[count];
        List<int>[] donors = new List<int>[count];
        for (int c = order.Length - 1; c >= 0; c--)
        {
            int i = order[c], r = receiver[i];
            if (!River(i) || r < 0) continue;
            double step = Step(i, r, cols) * lattice.NodeMetres;
            if (reach[i] + step > reach[r]) reach[r] = reach[i] + step;
            if (River(r)) (donors[r] ??= []).Add(i);
        }

        List<int> mouths = [.. Enumerable.Range(0, count).Where(i => River(i) && !River(receiver[i]) && reach[i] >= LeastCourseMetres)];
        mouths.Sort((a, b) => area[b] != area[a] ? area[b].CompareTo(area[a]) : a.CompareTo(b));
        List<DaggerfallRiverStation[]> courses = [];
        Stack<(int Node, int Course, int Join)> open = new(Enumerable.Reverse(mouths).Select(mouth => (mouth, -1, -1)));
        while (open.TryPop(out (int Node, int Course, int Join) start))
        {
            List<int> nodes = [];
            List<(int Node, int At)> branches = [];
            for (int i = start.Node; ;)
            {
                nodes.Add(i);
                if (donors[i] is not { } above) break;
                int trunk = above.MaxBy(donor => (area[donor], -donor));
                foreach (int branch in above)
                    if (branch != trunk && reach[branch] >= LeastCourseMetres) branches.Add((branch, nodes.Count - 1));
                i = trunk;
            }

            nodes.Reverse();
            DaggerfallRiverStation? joins = start.Course >= 0 ? courses[start.Course][start.Join] : null;
            (DaggerfallRiverStation[] course, int[] stationOf) = Stations(lattice, height, area, nodes, receiver[start.Node], sea, joins);
            courses.Add(course);
            foreach ((int node, int at) in branches.OrderByDescending(branch => area[branch.Node]).ThenBy(branch => branch.Node))
                open.Push((node, courses.Count - 1, stationOf[nodes.Count - 1 - at]));
        }

        report?.Invoke($"rivers: {courses.Count} courses, {courses.Sum(course => course.Length) * StationMetres / 1000:F0} km, {mouths.Count} mouths");
        return new(courses);
    }

    private static double Step(int a, int b, int cols)
    {
        int ax = a % cols, ay = a / cols, bx = b % cols, by = b / cols;
        return ax != bx && ay != by ? Math.Sqrt(2) : 1;
    }

    /// <summary>A course's stations from its lattice nodes, head first, and the station each node became.</summary>
    private static (DaggerfallRiverStation[] Stations, int[] StationOf) Stations(Lattice lattice, double[] height, double[] area, List<int> nodes, int outlet, bool[] sea,
        DaggerfallRiverStation? joins)
    {
        int cols = lattice.Cols, n = nodes.Count;
        double[] x = new double[n + 1], z = new double[n + 1];
        for (int i = 0; i < n; i++)
        {
            x[i] = nodes[i] % cols * lattice.NodeMetres;
            z[i] = nodes[i] / cols * lattice.NodeMetres;
        }

        if (joins is { } join) { x[n] = join.X; z[n] = join.Z; }
        else
        {
            // Run the mouth on past the coast, the way the river was already heading.
            int from = Math.Max(n - 6, 0);
            double ex = outlet >= 0 ? outlet % cols * lattice.NodeMetres : x[n - 1], ez = outlet >= 0 ? outlet / cols * lattice.NodeMetres : z[n - 1];
            double dx = ex - x[from], dz = ez - z[from], length = Math.Max(Math.Sqrt((dx * dx) + (dz * dz)), 1);
            x[n] = ex + (dx / length * MouthMetres);
            z[n] = ez + (dz / length * MouthMetres);
        }

        Smooth(x, CourseSmoothingNodes);
        Smooth(z, CourseSmoothingNodes);
        double[] along = new double[n + 1];
        for (int i = 1; i <= n; i++) along[i] = along[i - 1] + Math.Sqrt(Math.Pow(x[i] - x[i - 1], 2) + Math.Pow(z[i] - z[i - 1], 2));
        int stations = Math.Max((int)Math.Ceiling(along[n] / StationMetres), 1) + 1;
        double spacing = along[n] / (stations - 1);
        double[] sx = new double[stations], sz = new double[stations], surface = new double[stations], half = new double[stations];
        int[] stationOf = new int[n];
        for (int s = 0, k = 0; s < stations; s++)
        {
            double at = s * spacing;
            while (k < n - 1 && along[k + 1] < at) k++;
            double t = Math.Clamp((at - along[k]) / Math.Max(along[k + 1] - along[k], 1e-9), 0, 1);
            sx[s] = x[k] + ((x[k + 1] - x[k]) * t);
            sz[s] = z[k] + ((z[k + 1] - z[k]) * t);
            int node = nodes[Math.Min(k, n - 1)];
            // The water follows the thalweg's own nodes: the smoothed line can stand on a valley's side.
            double next = k + 1 < n ? height[nodes[k + 1]] : joins?.Surface ?? OceanMetres;
            surface[s] = height[node] + ((next - height[node]) * t);
            // A catchment reaches the source all at once, so the head tapers in or the river would start at full width from nothing.
            double grown = Math.Min(Math.Sqrt(at / HeadMetres), 1);
            half[s] = Math.Max(HeadHalfWidth, grown * Math.Max(LeastHalfWidth, .5 * WidthPerRootSquareKilometre * Math.Sqrt(area[node] / 1e6)));
        }

        for (int i = 0; i < n; i++) stationOf[i] = (int)Math.Round(Math.Min(along[i], along[n]) / spacing);
        // Only ever downhill from the head, and on down to what the course ends on: ground that stands in the way is cut through.
        for (int s = 1; s < stations; s++) surface[s] = Math.Min(surface[s], surface[s - 1]);
        Fall(surface, joins?.Surface ?? OceanMetres, spacing);
        // Smoothing rounds the profile's knees but may only lower it: raised, the water would stand over its own bed.
        double[] raw = (double[])surface.Clone();
        Smooth(surface, SurfaceSmoothingMetres / spacing);
        for (int s = 0; s < stations; s++) surface[s] = Math.Min(surface[s], raw[s]);
        Smooth(half, SurfaceSmoothingMetres / spacing);
        for (int s = 1; s < stations; s++) surface[s] = Math.Min(surface[s], surface[s - 1]);
        Fall(surface, joins?.Surface ?? OceanMetres, spacing);
        DaggerfallRiverStation[] course = new DaggerfallRiverStation[stations];
        for (int s = 0; s < stations; s++) course[s] = new((float)sx[s], (float)sz[s], (float)surface[s], (float)half[s]);
        return (course, stationOf);
    }

    /// <summary>Lifts a falling profile so that it keeps a least fall all the way to its end's height.</summary>
    private static void Fall(double[] surface, double end, double spacing)
    {
        surface[^1] = end;
        for (int s = surface.Length - 2; s >= 0; s--) surface[s] = Math.Max(surface[s], surface[s + 1] + (LeastFall * spacing));
    }

    /// <summary>A Gaussian along a line whose ends stay put.</summary>
    private static void Smooth(double[] line, double sigma)
    {
        int n = line.Length, radius = (int)Math.Ceiling(sigma * 3);
        if (n < 3 || radius < 1) return;
        double[] kernel = new double[radius + 1], result = new double[n];
        for (int i = 0; i <= radius; i++) kernel[i] = Math.Exp(-.5 * i * i / (sigma * sigma));
        for (int i = 1; i < n - 1; i++)
        {
            // The window shrinks toward an end, so the end's value holds.
            int reach = Math.Min(radius, Math.Min(i, n - 1 - i));
            double sum = line[i] * kernel[0], weight = kernel[0];
            for (int k = 1; k <= reach; k++)
            {
                sum += kernel[k] * (line[i - k] + line[i + k]);
                weight += 2 * kernel[k];
            }

            result[i] = sum / weight;
        }

        result[0] = line[0];
        result[n - 1] = line[n - 1];
        Array.Copy(result, line, n);
    }

    /// <summary>
    /// Lays a floor along every river, from its lip out to as far as its width carries: ground is only ever lowered, toward
    /// the water, so a large river opens a flat valley and the runtime's channel meets the lattice rather than a slope.
    /// </summary>
    private static void LayFloodplains(Lattice lattice, double[] height, bool[] sea, DaggerfallRivers rivers)
    {
        int cols = lattice.Cols, rows = lattice.Rows;
        // Each node takes the floor of the station that reaches it best: a steep river's lower stations must not drag down the ground under its upper ones.
        float[] best = new float[height.Length], floor = new float[height.Length];
        foreach (DaggerfallRiverStation[] course in rivers.Courses)
        foreach (DaggerfallRiverStation station in course)
        {
            double reach = (station.HalfWidth * FloodplainPerHalfWidth) + (lattice.NodeMetres * FloodplainNodes);
            int x0 = Math.Max((int)((station.X - reach) / lattice.NodeMetres), 0), x1 = Math.Min((int)((station.X + reach) / lattice.NodeMetres) + 1, cols - 1);
            int y0 = Math.Max((int)((station.Z - reach) / lattice.NodeMetres), 0), y1 = Math.Min((int)((station.Z + reach) / lattice.NodeMetres) + 1, rows - 1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int at = (y * cols) + x;
                double dx = (x * lattice.NodeMetres) - station.X, dz = (y * lattice.NodeMetres) - station.Z;
                float t = (float)(1 - (Math.Sqrt((dx * dx) + (dz * dz)) / reach));
                if (t <= best[at]) continue;
                best[at] = t;
                floor[at] = station.Surface + (float)FloodplainRise;
            }
        }

        for (int i = 0; i < height.Length; i++)
        {
            if (best[i] <= 0 || sea[i]) continue;
            double t = best[i], lowered = height[i] + ((floor[i] - height[i]) * t * t * (3 - (2 * t)));
            if (lowered < height[i]) height[i] = lowered;
        }
    }
}
