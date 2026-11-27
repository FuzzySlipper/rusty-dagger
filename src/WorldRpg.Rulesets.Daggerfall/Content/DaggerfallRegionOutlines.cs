using System.Globalization;
using System.Text;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>One classic region's shape on the world map: an SVG path in world-map pixels (filled nonzero).</summary>
internal sealed record DaggerfallRegionOutline(int Region, string Path);

/// <summary>
/// The classic regions' shapes, traced once from the politic grid (which region each world-map pixel belongs to), for
/// the map's Realms view (#11144). Each region's edge pixels are walked into closed loops, clockwise around land and
/// the other way around a hole, then simplified to within <see cref="Tolerance"/> pixels, so the page fills a region
/// with one path.
/// </summary>
internal static class DaggerfallRegionOutlines
{
    /// <summary>How far, in world-map pixels, a simplified outline may stray from the grid's edge.</summary>
    internal const double Tolerance = 0.75;

    /// <summary>Traces every region of <paramref name="grid"/> over a world map <paramref name="width"/> by <paramref name="height"/> pixels.</summary>
    internal static IReadOnlyList<DaggerfallRegionOutline> Trace(DaggerfallPoliticGridDefinition grid, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(grid);
        int[] owner = new int[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // A world-map pixel's politic cell is one column on: the grid keeps the height map's sentinel column.
                DaggerfallPoliticCell cell = grid.GetCell(x + 1, y);
                owner[y * width + x] = cell.Disposition == DaggerfallPoliticDisposition.Region ? cell.Region : -1;
            }
        int At(int x, int y) => x < 0 || y < 0 || x >= width || y >= height ? -1 : owner[y * width + x];

        Dictionary<int, Dictionary<(int, int), List<(int, int)>>> edges = [];
        void Edge(int region, (int, int) from, (int, int) to)
        {
            if (!edges.TryGetValue(region, out var next)) edges[region] = next = [];
            if (!next.TryGetValue(from, out var list)) next[from] = list = [];
            list.Add(to);
        }
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int region = owner[y * width + x];
                if (region < 0) continue;
                if (At(x, y - 1) != region) Edge(region, (x, y), (x + 1, y));
                if (At(x + 1, y) != region) Edge(region, (x + 1, y), (x + 1, y + 1));
                if (At(x, y + 1) != region) Edge(region, (x + 1, y + 1), (x, y + 1));
                if (At(x - 1, y) != region) Edge(region, (x, y + 1), (x, y));
            }

        List<DaggerfallRegionOutline> outlines = [];
        foreach ((int region, var next) in edges.OrderBy(pair => pair.Key))
        {
            StringBuilder path = new();
            foreach ((int, int) start in next.Keys.OrderBy(point => point.Item2).ThenBy(point => point.Item1).ToArray())
                while (next.TryGetValue(start, out var outgoing) && outgoing.Count > 0)
                {
                    // Every corner has as many edges in as out, so the walk can only end back at its start.
                    List<(int X, int Y)> loop = [];
                    (int, int) at = start;
                    do
                    {
                        loop.Add(at);
                        List<(int, int)> from = next[at];
                        (int, int) to = from[^1];
                        from.RemoveAt(from.Count - 1);
                        if (from.Count == 0) next.Remove(at);
                        at = to;
                    }
                    while (at != start);
                    Write(path, Simplify(loop));
                }
            outlines.Add(new(region, path.ToString()));
        }
        return outlines;
    }

    private static void Write(StringBuilder path, IReadOnlyList<(int X, int Y)> loop)
    {
        if (loop.Count < 3) return;
        for (int index = 0; index < loop.Count; index++)
            path.Append(index == 0 ? 'M' : 'L').Append(loop[index].X.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(loop[index].Y.ToString(CultureInfo.InvariantCulture));
        path.Append('Z');
    }

    /// <summary>A closed loop with its straight runs merged and its stairs eased to within <see cref="Tolerance"/>.</summary>
    private static IReadOnlyList<(int X, int Y)> Simplify(List<(int X, int Y)> loop)
    {
        // Split the loop at its farthest point from the start so each half is an open run with fixed ends.
        int far = 0;
        long farthest = -1;
        for (int index = 1; index < loop.Count; index++)
        {
            long dx = loop[index].X - loop[0].X, dy = loop[index].Y - loop[0].Y, distance = dx * dx + dy * dy;
            if (distance > farthest) { farthest = distance; far = index; }
        }
        List<(int, int)> kept = [];
        Run(loop, 0, far, kept);
        List<(int X, int Y)> back = [.. loop.Skip(far), loop[0]];
        List<(int, int)> rest = [];
        Run(back, 0, back.Count - 1, rest);
        kept.AddRange(rest.Skip(1).Take(rest.Count - 2));
        return kept;
    }

    /// <summary>Ramer–Douglas–Peucker over <paramref name="points"/>[from..to], appending the kept points (both ends included).</summary>
    private static void Run(List<(int X, int Y)> points, int from, int to, List<(int, int)> kept)
    {
        Stack<(int From, int To)> pending = new();
        SortedSet<int> keep = [from, to];
        pending.Push((from, to));
        while (pending.Count > 0)
        {
            (int a, int b) = pending.Pop();
            double dx = points[b].X - points[a].X, dy = points[b].Y - points[a].Y, length = Math.Sqrt(dx * dx + dy * dy);
            int worst = -1;
            double worstDistance = Tolerance;
            for (int index = a + 1; index < b; index++)
            {
                double px = points[index].X - points[a].X, py = points[index].Y - points[a].Y;
                double distance = length == 0 ? Math.Sqrt(px * px + py * py) : Math.Abs(px * dy - py * dx) / length;
                if (distance > worstDistance) { worstDistance = distance; worst = index; }
            }
            if (worst < 0) continue;
            keep.Add(worst);
            pending.Push((a, worst));
            pending.Push((worst, b));
        }
        foreach (int index in keep) kept.Add(points[index]);
    }
}
