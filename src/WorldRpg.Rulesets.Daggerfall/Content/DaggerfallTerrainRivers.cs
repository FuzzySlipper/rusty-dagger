using System.Buffers.Binary;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>The nearest river to a point: how far its centre line is, and its water there.</summary>
/// <param name="Distance">Metres from the centre line.</param>
/// <param name="Surface">The water's height at the nearest point of the line.</param>
/// <param name="HalfWidth">Half the channel's width there.</param>
internal readonly record struct DaggerfallRiverReach(double Distance, double Surface, double HalfWidth);

/// <summary>Where a section of river water ends: its edges, in metres from the heightmap's corner, and the surface between them.</summary>
internal readonly record struct DaggerfallRiverSpan(double LeftX, double LeftZ, double RightX, double RightZ, double Surface);

/// <summary>One section of river water between two stations.</summary>
internal readonly record struct DaggerfallRiverSection(DaggerfallRiverSpan From, DaggerfallRiverSpan To);

/// <summary>
/// The rivers the import bakes out of the relief's drainage. Each course is a line of stations in metres from the
/// heightmap's corner, its surface falling all the way from its head to the sea or the river it joins. The walked and
/// far ground stand on what <see cref="Carve"/> cuts, and the water draws from the same stations.
/// </summary>
internal sealed class DaggerfallTerrainRivers
{
    internal const string Path = "worldrpg/terrain/rivers.bin";
    private const int HeaderBytes = 8, StationFloats = 4, MapWidth = 1000, MapHeight = 500;
    private const double PixelMetres = 819.2;
    /// <summary>The ocean clamp, and a little over it.</summary>
    private const double SeaMetres = 27.2 * 1.5 + .05;
    /// <summary>How far above the water a bank's lip stands.</summary>
    private const double LipMetres = .6;
    /// <summary>How deep a channel stands for its width, and the least bank a sampling can hold.</summary>
    private const double DepthPerHalfWidth = .22, LeastDepth = 1.1, MostDepth = 5, LeastBank = 14;
    /// <summary>The share of a bank that rises to the lip; the rest blends into the ground.</summary>
    private const double LipShare = .3;
    /// <summary>How far past a bank the erosion filter's gullies fade back in.</summary>
    private const double GullyFadeMetres = 96;
    /// <summary>A sampling coarser than this many times a river's width doesn't hold it.</summary>
    private const double CoarsestPerWidth = 4;

    private readonly float[] _stations;
    private readonly int[] _courseEnds;
    private readonly int[] _pixelStarts, _pixelSegments;

    private DaggerfallTerrainRivers(float[] stations, int[] courseEnds)
    {
        _stations = stations;
        _courseEnds = courseEnds;
        (_pixelStarts, _pixelSegments) = Index(stations, courseEnds);
    }

    internal int Courses => _courseEnds.Length;

    internal static DaggerfallTerrainRivers Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes || !bytes[..4].SequenceEqual("RVR1"u8))
            throw new InvalidOperationException("The terrain rivers are not a rivers file.");
        int courses = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        int[] ends = new int[courses];
        int total = 0;
        for (int i = 0; i < courses; i++)
        {
            total = checked(total + BinaryPrimitives.ReadInt32LittleEndian(bytes[(HeaderBytes + (i * sizeof(int)))..]));
            ends[i] = total;
        }

        int body = HeaderBytes + (courses * sizeof(int));
        if (bytes.Length != body + (total * StationFloats * sizeof(float)))
            throw new InvalidOperationException($"The terrain rivers carry {bytes.Length} bytes for {total} stations in {courses} courses.");
        float[] stations = new float[total * StationFloats];
        for (int i = 0; i < stations.Length; i++) stations[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(body + (i * sizeof(float)))..]);
        return new(stations, ends);
    }

    /// <summary>The station nearest a point anywhere on the map: its offset east and south, surface and half width.</summary>
    internal (double East, double South, double Surface, double HalfWidth)? NearestStation(double x, double z)
    {
        double best = double.MaxValue;
        int found = -1;
        for (int a = 0; a < _stations.Length; a += StationFloats)
        {
            double dx = _stations[a] - x, dz = _stations[a + 1] - z, square = (dx * dx) + (dz * dz);
            if (square >= best) continue;
            best = square;
            found = a;
        }

        return found < 0 ? null : (_stations[found] - x, _stations[found + 1] - z, _stations[found + 2], _stations[found + 3]);
    }

    /// <summary>The nearest river whose bank reaches a point, or none.</summary>
    internal DaggerfallRiverReach? Near(double x, double z, double finestMetres)
    {
        int px = (int)Math.Floor(x / PixelMetres), py = (int)Math.Floor(z / PixelMetres);
        if (px is < 0 or >= MapWidth || py is < 0 or >= MapHeight) return null;
        int pixel = (py * MapWidth) + px;
        double best = double.MaxValue;
        DaggerfallRiverReach? found = null;
        for (int k = _pixelStarts[pixel]; k < _pixelStarts[pixel + 1]; k++)
        {
            (double distance, double half, double surface) = ToSegment(_pixelSegments[k] * StationFloats, x, z);
            if (half * 2 * CoarsestPerWidth < finestMetres) continue;
            half = Math.Max(half, finestMetres / 2);
            if (distance - half >= best || distance > half + Bank(half)) continue;
            best = distance - half;
            found = new(distance, surface, half);
        }

        return found;
    }

    /// <summary>A point's distance from the segment starting at station float <paramref name="a"/>, and the river's half width and surface at the nearest point.</summary>
    private (double Distance, double HalfWidth, double Surface) ToSegment(int a, double x, double z)
    {
        int b = a + StationFloats;
        double ax = _stations[a], az = _stations[a + 1], dx = _stations[b] - ax, dz = _stations[b + 1] - az;
        double t = Math.Clamp((((x - ax) * dx) + ((z - az) * dz)) / Math.Max((dx * dx) + (dz * dz), 1e-6), 0, 1);
        double ox = ax + (dx * t) - x, oz = az + (dz * t) - z;
        return (Math.Sqrt((ox * ox) + (oz * oz)), _stations[a + 3] + ((_stations[b + 3] - _stations[a + 3]) * t), _stations[a + 2] + ((_stations[b + 2] - _stations[a + 2]) * t));
    }

    /// <summary>The water sections a map pixel owns: each lies in the pixel its middle falls in, so a section is drawn once.</summary>
    internal IEnumerable<DaggerfallRiverSection> Sections(int pixelX, int pixelY)
    {
        if (pixelX is < 0 or >= MapWidth || pixelY is < 0 or >= MapHeight) yield break;
        int pixel = (pixelY * MapWidth) + pixelX;
        for (int k = _pixelStarts[pixel]; k < _pixelStarts[pixel + 1]; k++)
        {
            int segment = _pixelSegments[k], a = segment * StationFloats, b = a + StationFloats;
            if (Pixel((_stations[a] + _stations[b]) / 2, MapWidth) != pixelX || Pixel((_stations[a + 1] + _stations[b + 1]) / 2, MapHeight) != pixelY) continue;
            // The sea's own sheets fill a channel down at its surface.
            if (_stations[a + 2] <= SeaMetres && _stations[b + 2] <= SeaMetres) continue;
            // A tributary's last reach lies inside what it joins, whose own water covers it.
            if (Near((_stations[a] + _stations[b]) / 2, (_stations[a + 1] + _stations[b + 1]) / 2, 0) is { } wider
                && wider.HalfWidth > _stations[a + 3] + 1 && wider.Distance < wider.HalfWidth) continue;
            yield return new(Span(segment), Span(segment + 1));
        }
    }

    /// <summary>The water's edges at a station, square to the course there and running on under the banks.</summary>
    private DaggerfallRiverSpan Span(int station)
    {
        (int first, int last) = CourseOf(station);
        int before = Math.Max(station - 1, first) * StationFloats, after = Math.Min(station + 1, last) * StationFloats, at = station * StationFloats;
        double dx = _stations[after] - _stations[before], dz = _stations[after + 1] - _stations[before + 1], length = Math.Max(Math.Sqrt((dx * dx) + (dz * dz)), 1e-6);
        double half = _stations[at + 3], edge = half + (Bank(half) * LipShare), x = _stations[at], z = _stations[at + 1];
        double nx = dz / length * edge, nz = -dx / length * edge;
        return new(x + nx, z + nz, x - nx, z - nz, _stations[at + 2]);
    }

    private (int First, int Last) CourseOf(int station)
    {
        int course = Array.BinarySearch(_courseEnds, station + 1);
        course = course < 0 ? ~course : course;
        return (course == 0 ? 0 : _courseEnds[course - 1], _courseEnds[course] - 1);
    }

    /// <summary>
    /// How much of the erosion filter's gullies stand at a point, from 0 beside a channel to 1 well clear of it. A gully cut
    /// beside a river would leave the land below the water and the river up on a levee.
    /// </summary>
    internal double GullyShare(double x, double z)
    {
        int px = (int)Math.Floor(x / PixelMetres), py = (int)Math.Floor(z / PixelMetres);
        if (px is < 0 or >= MapWidth || py is < 0 or >= MapHeight) return 1;
        int pixel = (py * MapWidth) + px;
        double clear = double.MaxValue;
        for (int k = _pixelStarts[pixel]; k < _pixelStarts[pixel + 1]; k++)
        {
            (double distance, double half, _) = ToSegment(_pixelSegments[k] * StationFloats, x, z);
            clear = Math.Min(clear, distance - half - Bank(half));
        }

        double t = Math.Clamp(clear / GullyFadeMetres, 0, 1);
        return t * t * (3 - (2 * t));
    }

    /// <summary>Whether a point lies in a channel, as a sampling this coarse holds it.</summary>
    internal bool InChannel(double x, double z, double finestMetres) => Near(x, z, finestMetres) is { } river && river.Distance <= river.HalfWidth;

    /// <summary>Whether a point lies on a channel's bed or its lip, where nothing grows and the ground is shore.</summary>
    internal bool OnBank(double x, double z, double finestMetres) => Near(x, z, finestMetres) is { } river && river.Distance <= river.HalfWidth + (Bank(river.HalfWidth) * LipShare);

    /// <summary>The ground at a point with any river cut into it: a channel, and banks that blend back into the land.</summary>
    internal double Carve(double x, double z, double ground, double finestMetres)
    {
        if (Near(x, z, finestMetres) is not { } river) return ground;
        double surface = river.Surface;
        if (river.Distance <= river.HalfWidth)
        {
            double t = river.Distance / river.HalfWidth;
            return surface - (Depth(river.HalfWidth) * (1 - (t * t)));
        }

        double u = (river.Distance - river.HalfWidth) / Bank(river.HalfWidth);
        double lip = surface + (LipMetres * Math.Min(u / LipShare, 1));
        double blend = Math.Clamp((u - LipShare) / (1 - LipShare), 0, 1);
        return lip + ((ground - lip) * blend * blend * (3 - (2 * blend)));
    }

    private static double Depth(double half) => Math.Clamp(half * DepthPerHalfWidth, Math.Min(LeastDepth, half / 2), MostDepth);

    private static double Bank(double half) => Math.Max(half * 2, LeastBank);

    /// <summary>Each map pixel's segments, reaching out as far as a segment's widest bank can.</summary>
    private static (int[] Starts, int[] Segments) Index(float[] stations, int[] ends)
    {
        int[] counts = new int[(MapWidth * MapHeight) + 1];
        void Each(Action<int, int> visit)
        {
            int start = 0;
            foreach (int end in ends)
            {
                for (int s = start; s < end - 1; s++)
                {
                    int a = s * StationFloats, b = a + StationFloats;
                    // The widest a coarse sampling may draw it is a quarter of that sampling, so a far ring's own reach counts too.
                    double reach = Math.Max(stations[a + 3], stations[b + 3]) * (1 + (CoarsestPerWidth * 3)) + LeastBank + GullyFadeMetres;
                    int x0 = Pixel(Math.Min(stations[a], stations[b]) - reach, MapWidth), x1 = Pixel(Math.Max(stations[a], stations[b]) + reach, MapWidth);
                    int y0 = Pixel(Math.Min(stations[a + 1], stations[b + 1]) - reach, MapHeight), y1 = Pixel(Math.Max(stations[a + 1], stations[b + 1]) + reach, MapHeight);
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        visit((y * MapWidth) + x, s);
                }

                start = end;
            }
        }

        Each((pixel, _) => counts[pixel + 1]++);
        for (int i = 1; i < counts.Length; i++) counts[i] += counts[i - 1];
        int[] next = counts[..^1], segments = new int[counts[^1]];
        next = (int[])next.Clone();
        Each((pixel, segment) => segments[next[pixel]++] = segment);
        return (counts, segments);
    }

    private static int Pixel(double metres, int extent) => Math.Clamp((int)Math.Floor(metres / PixelMetres), 0, extent - 1);
}
