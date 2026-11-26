using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Deterministic, lightweight regional trade-road network for the 3D overland travel map (#11114):
/// connects cities, towns, villages and roadside taverns over land using slope-aware local A* (avoiding ocean
/// pixels and extreme cliffs), smooths paths into gentle winding tracks, and indexes segments into a flat bucket
/// grid for O(1) per-vertex queries during <see cref="DaggerfallHorizonGround.SampleMap"/> and forest clearings.
/// </summary>
internal sealed class DaggerfallOverlandRoads
{
    private const int OceanClimate = 223;
    private const double Cell = DaggerfallWorldGrid.HorizontalSize;
    /// <summary>Map pixels per spatial index bucket (2x2 pixels = 1638 m square).</summary>
    private const int BucketPixels = 2;
    private const int MajorReachPixels = 26;
    private const int CapitalReachPixels = 44;
    private const int SpurReachPixels = 14;
    private const int MaxAStarExpanded = 320;
    private const int MaxCliffDelta = 26;

    private static readonly (int Dx, int Dy, float Len)[] Neighbours8 =
    [
        (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
        (1, 1, 1.4142135f), (1, -1, 1.4142135f), (-1, 1, 1.4142135f), (-1, -1, 1.4142135f),
    ];

    internal readonly record struct RoadSegment(float Ax, float Az, float Bx, float Bz, float HalfWidth, float Strength);

    private readonly record struct BuiltNetwork(int BucketWidth, int BucketHeight, int[] BucketStart, int[] BucketCount, int[] SegmentIndices, RoadSegment[] Segments);

    private readonly Lazy<BuiltNetwork> _network;

    internal DaggerfallOverlandRoads(DaggerfallTerrainSet terrain, Func<int, int, int> climate, IReadOnlyList<DaggerfallSiteRecord> sites)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(climate);
        ArgumentNullException.ThrowIfNull(sites);
        _network = new(() => Build(terrain, climate, sites), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Number of indexed smoothed road segments across the world map (triggers lazy build when read).</summary>
    internal int SegmentCount => _network.Value.Segments.Length;

    /// <summary>
    /// Road coverage at map point (<paramref name="x"/>, <paramref name="z"/>) in [0, 1] for a terrain ring whose
    /// vertex spacing is <paramref name="cellMetres"/>: strongest on major trade highways, subtler on village lanes,
    /// anti-aliased across cells and fading out on coarse strategic rings.
    /// </summary>
    internal float Sample(double x, double z, double cellMetres)
    {
        if (cellMetres > 260) return 0f;
        BuiltNetwork net = _network.Value;
        if (net.Segments.Length == 0) return 0f;

        int bx = (int)Math.Floor(x / (Cell * BucketPixels));
        int bz = (int)Math.Floor((z / Cell + 1) / BucketPixels);
        if ((uint)bx >= (uint)net.BucketWidth || (uint)bz >= (uint)net.BucketHeight) return 0f;

        int bucket = (bz * net.BucketWidth) + bx;
        int count = net.BucketCount[bucket];
        if (count == 0) return 0f;

        int start = net.BucketStart[bucket];
        float px = (float)x, pz = (float)z;
        float ringScale = cellMetres > 120 ? .66f : 1f;
        float minHalf = (float)(cellMetres * .62);
        float feather = (float)Math.Max(26.0, cellMetres * .68);

        float best = 0f;
        for (int i = 0; i < count; i++)
        {
            RoadSegment seg = net.Segments[net.SegmentIndices[start + i]];
            float dist = PointSegmentDistance(px, pz, seg.Ax, seg.Az, seg.Bx, seg.Bz);
            float half = MathF.Max(seg.HalfWidth, minHalf);
            float outer = half + feather;
            if (dist >= outer) continue;
            float inner = half * .32f;
            float u = Math.Clamp((outer - dist) / MathF.Max(outer - inner, 1e-3f), 0f, 1f);
            float cover = u * u * (3f - (2f * u)) * seg.Strength * ringScale;
            if (cover > best) best = cover;
        }
        return best;
    }

    private static BuiltNetwork Build(DaggerfallTerrainSet terrain, Func<int, int, int> climate, IReadOnlyList<DaggerfallSiteRecord> sites)
    {
        int width = terrain.Width, height = terrain.Height;
        if (width <= 0 || height <= 0 || sites.Count == 0)
            return new(0, 0, [], [], [], []);

        byte[] heightmap = terrain.Heightmap;
        List<(int X, int Y, DaggerfallSiteKind Kind)> major = [];
        List<(int X, int Y, DaggerfallSiteKind Kind)> minor = [];

        bool IsPassableLand(int x, int y) =>
            (uint)x < (uint)width && (uint)y < (uint)height && climate(x, y) != OceanClimate;

        int HeightAt(int x, int y)
        {
            // Heightmap pixel (hx, y) aligns with world map pixel (hx + 1, y), as in DaggerfallHorizonGround.Floor.
            int hx = Math.Clamp(x - 1, 0, width - 1);
            int idx = (y * width) + hx;
            return (uint)idx < (uint)heightmap.Length ? heightmap[idx] : 0;
        }

        // Deduplicate by map pixel, preferring cities over towns over villages/taverns.
        Dictionary<(int X, int Y), DaggerfallSiteKind> byPixel = [];
        foreach (DaggerfallSiteRecord site in sites)
        {
            if (site.Exterior is null) continue;
            if (site.Kind is not (DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.TownVillage or DaggerfallSiteKind.Tavern))
                continue;
            int px = site.MapPixelX, py = site.MapPixelY;
            if (!IsPassableLand(px, py)) continue;
            if (!byPixel.TryGetValue((px, py), out DaggerfallSiteKind existing) || Priority(site.Kind) > Priority(existing))
                byPixel[(px, py)] = site.Kind;
        }

        foreach (((int px, int py), DaggerfallSiteKind kind) in byPixel.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X))
        {
            if (kind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet) major.Add((px, py, kind));
            else minor.Add((px, py, kind));
        }

        // Spatial buckets of major hubs for O(1) neighbor search.
        const int HubBlock = 16;
        Dictionary<(int, int), List<int>> majorBlocks = [];
        for (int i = 0; i < major.Count; i++)
        {
            (int bx, int by) = (major[i].X / HubBlock, major[i].Y / HubBlock);
            if (!majorBlocks.TryGetValue((bx, by), out List<int>? list)) majorBlocks[(bx, by)] = list = [];
            list.Add(i);
        }

        HashSet<(int, int)> addedMajor = [];
        List<(int Ax, int Ay, int Bx, int By, bool Highway)> edges = [];

        // Tier 1: Gabriel-pruned regional highway graph between cities & towns.
        List<int> nearby = [];
        for (int i = 0; i < major.Count; i++)
        {
            (int ax, int ay, DaggerfallSiteKind aKind) = major[i];
            int reach = aKind == DaggerfallSiteKind.TownCity ? CapitalReachPixels : MajorReachPixels;
            int span = (reach / HubBlock) + 1;
            int bx0 = ax / HubBlock, by0 = ay / HubBlock;
            nearby.Clear();
            for (int dy = -span; dy <= span; dy++)
            for (int dx = -span; dx <= span; dx++)
                if (majorBlocks.TryGetValue((bx0 + dx, by0 + dy), out List<int>? list))
                    nearby.AddRange(list);

            foreach (int j in nearby)
            {
                if (j <= i) continue;
                (int bx, int by, DaggerfallSiteKind bKind) = major[j];
                int ddx = bx - ax, ddy = by - ay;
                int distSq = (ddx * ddx) + (ddy * ddy);
                int maxDist = (aKind == DaggerfallSiteKind.TownCity && bKind == DaggerfallSiteKind.TownCity) ? CapitalReachPixels : MajorReachPixels;
                if (distSq == 0 || distSq > maxDist * maxDist) continue;

                // Gabriel check: skip direct edge A-B if another major hub C lies inside the A-B diameter circle.
                bool shadowed = false;
                double limitSq = distSq * .92;
                foreach (int k in nearby)
                {
                    if (k == i || k == j) continue;
                    (int cx, int cy, _) = major[k];
                    int ac = ((cx - ax) * (cx - ax)) + ((cy - ay) * (cy - ay));
                    int bc = ((cx - bx) * (cx - bx)) + ((cy - by) * (cy - by));
                    if (ac + bc < limitSq) { shadowed = true; break; }
                }
                if (!shadowed && addedMajor.Add((i, j)))
                    edges.Add((ax, ay, bx, by, Highway: true));
            }
        }

        // Tier 2: Single short spur lane from each village/tavern to its nearest major hub.
        for (int i = 0; i < minor.Count; i++)
        {
            (int mx, int my, _) = minor[i];
            int bx0 = mx / HubBlock, by0 = my / HubBlock;
            int bestIdx = -1, bestDistSq = (SpurReachPixels * SpurReachPixels) + 1;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!majorBlocks.TryGetValue((bx0 + dx, by0 + dy), out List<int>? list)) continue;
                foreach (int j in list)
                {
                    int ddx = major[j].X - mx, ddy = major[j].Y - my;
                    int distSq = (ddx * ddx) + (ddy * ddy);
                    if (distSq > 0 && distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestIdx = j;
                    }
                }
            }
            if (bestIdx >= 0)
                edges.Add((mx, my, major[bestIdx].X, major[bestIdx].Y, Highway: false));
        }

        // Route each edge across land via bounded slope-aware A* and smooth into winding segments.
        List<RoadSegment> segments = [];
        List<(int X, int Y)> pathBuf = [];
        List<Vector2> ptsA = [], ptsB = [];

        foreach ((int ax, int ay, int bx, int by, bool highway) in edges)
        {
            if (!TryRoute(ax, ay, bx, by, width, height, HeightAt, climate, pathBuf)) continue;
            if (pathBuf.Count < 2) continue;

            ptsA.Clear();
            for (int p = 0; p < pathBuf.Count; p++)
            {
                (int px, int py) = pathBuf[p];
                Vector2 world = PixelCentre(px, py);
                // Add subtle deterministic meander at interior waypoints, keeping endpoints pinned to settlements.
                if (p > 0 && p < pathBuf.Count - 1)
                {
                    Vector2 prev = PixelCentre(pathBuf[p - 1].X, pathBuf[p - 1].Y);
                    Vector2 next = PixelCentre(pathBuf[p + 1].X, pathBuf[p + 1].Y);
                    Vector2 dir = next - prev;
                    float len = dir.Length();
                    if (len > 1f)
                    {
                        Vector2 perp = new(-dir.Y / len, dir.X / len);
                        // Combine smooth sinusoidal sway with per-pixel jitter so cardinal stretches curve organically.
                        float sway = MathF.Sin((p * .72f) + (HashSigned(ax, ay) * MathF.PI)) * 62f;
                        float jitter = (HashSigned(px, py) * 58f) + sway;
                        Vector2 shifted = world + (perp * jitter);
                        int sx = Math.Clamp((int)Math.Floor(shifted.X / Cell), 0, width - 1);
                        int sy = Math.Clamp((int)Math.Floor((shifted.Y / Cell) + 1), 0, height - 1);
                        if (IsPassableLand(sx, sy)) world = shifted;
                    }
                }
                ptsA.Add(world);
            }

            // Two Chaikin corner-cutting passes for smooth, organic curves (rejecting any shift into ocean).
            SmoothPolyline(ptsA, ptsB, width, height, IsPassableLand);
            SmoothPolyline(ptsB, ptsA, width, height, IsPassableLand);

            float halfWidth = highway ? 50f : 34f;
            float strength = highway ? .80f : .58f;
            for (int p = 0; p < ptsA.Count - 1; p++)
            {
                Vector2 u = ptsA[p], v = ptsA[p + 1];
                if (Vector2.DistanceSquared(u, v) < 1f) continue;
                segments.Add(new(u.X, u.Y, v.X, v.Y, halfWidth, strength));
            }
        }

        // Index segments into a flat 2x2-pixel bucket grid for O(1) lookup (with 1-cell padding for wide road feathers).
        int bucketW = (width + BucketPixels - 1) / BucketPixels;
        int bucketH = (height + BucketPixels - 1) / BucketPixels;
        int bucketCount = bucketW * bucketH;
        int[] counts = new int[bucketCount];

        void ForBuckets(RoadSegment seg, Action<int> visit)
        {
            float pad = 260f;
            int minBx = Math.Clamp((int)Math.Floor((MathF.Min(seg.Ax, seg.Bx) - pad) / (Cell * BucketPixels)), 0, bucketW - 1);
            int maxBx = Math.Clamp((int)Math.Floor((MathF.Max(seg.Ax, seg.Bx) + pad) / (Cell * BucketPixels)), 0, bucketW - 1);
            int minBz = Math.Clamp((int)Math.Floor((((MathF.Min(seg.Az, seg.Bz) - pad) / Cell) + 1) / BucketPixels), 0, bucketH - 1);
            int maxBz = Math.Clamp((int)Math.Floor((((MathF.Max(seg.Az, seg.Bz) + pad) / Cell) + 1) / BucketPixels), 0, bucketH - 1);
            for (int bz = minBz; bz <= maxBz; bz++)
            for (int bx = minBx; bx <= maxBx; bx++)
                visit((bz * bucketW) + bx);
        }

        for (int i = 0; i < segments.Count; i++)
            ForBuckets(segments[i], b => counts[b]++);

        int[] starts = new int[bucketCount];
        int totalRefs = 0;
        for (int b = 0; b < bucketCount; b++)
        {
            starts[b] = totalRefs;
            totalRefs += counts[b];
        }

        int[] cursor = (int[])starts.Clone();
        int[] indices = new int[totalRefs];
        for (int i = 0; i < segments.Count; i++)
        {
            int segIdx = i;
            ForBuckets(segments[i], b => indices[cursor[b]++] = segIdx);
        }

        return new(bucketW, bucketH, starts, counts, indices, [.. segments]);
    }

    private static bool TryRoute(int ax, int ay, int bx, int by, int width, int height, Func<int, int, int> heightAt,
        Func<int, int, int> climate, List<(int X, int Y)> output)
    {
        output.Clear();
        const int Margin = 6;
        int minX = Math.Max(0, Math.Min(ax, bx) - Margin);
        int maxX = Math.Min(width - 1, Math.Max(ax, bx) + Margin);
        int minY = Math.Max(0, Math.Min(ay, by) - Margin);
        int maxY = Math.Min(height - 1, Math.Max(ay, by) + Margin);
        int bw = maxX - minX + 1, bh = maxY - minY + 1;
        int cells = bw * bh;

        float[] gScore = new float[cells];
        Array.Fill(gScore, float.PositiveInfinity);
        int[] cameFrom = new int[cells];
        Array.Fill(cameFrom, -1);
        bool[] closed = new bool[cells];

        int start = ((ay - minY) * bw) + (ax - minX);
        int goal = ((by - minY) * bw) + (bx - minX);
        gScore[start] = 0f;

        PriorityQueue<int, float> open = new();
        open.Enqueue(start, Octile(ax, ay, bx, by));

        int expanded = 0;
        while (open.Count > 0)
        {
            int cur = open.Dequeue();
            if (closed[cur]) continue;
            closed[cur] = true;
            if (cur == goal)
            {
                int node = goal;
                while (node >= 0)
                {
                    output.Add((minX + (node % bw), minY + (node / bw)));
                    if (node == start) break;
                    node = cameFrom[node];
                }
                output.Reverse();
                return true;
            }
            if (++expanded > MaxAStarExpanded) return false;

            int cx = minX + (cur % bw), cy = minY + (cur / bw);
            int curH = heightAt(cx, cy);
            float curG = gScore[cur];

            for (int n = 0; n < Neighbours8.Length; n++)
            {
                (int dx, int dy, float stepLen) = Neighbours8[n];
                int nx = cx + dx, ny = cy + dy;
                if (nx < minX || nx > maxX || ny < minY || ny > maxY) continue;
                int next = ((ny - minY) * bw) + (nx - minX);
                if (closed[next]) continue;

                int nextClimate = climate(nx, ny);
                if (nextClimate == OceanClimate) continue;
                // Prevent diagonal corner cuts across ocean coves.
                if (dx != 0 && dy != 0 && (climate(cx + dx, cy) == OceanClimate || climate(cx, cy + dy) == OceanClimate))
                    continue;

                int nextH = heightAt(nx, ny);
                int dh = Math.Abs(nextH - curH);
                if (dh > MaxCliffDelta) continue;

                float terrainPenalty = nextClimate switch
                {
                    226 => 1.38f, // Mountain
                    228 => 1.25f, // Swamp
                    _ => 1f,
                };
                float cost = stepLen * terrainPenalty * (1f + (.08f * dh) + (.012f * dh * dh));
                float tentative = curG + cost;
                if (tentative < gScore[next])
                {
                    gScore[next] = tentative;
                    cameFrom[next] = cur;
                    open.Enqueue(next, tentative + Octile(nx, ny, bx, by));
                }
            }
        }
        return false;
    }

    private static void SmoothPolyline(List<Vector2> src, List<Vector2> dst, int width, int height, Func<int, int, bool> isLand)
    {
        dst.Clear();
        if (src.Count < 3) { dst.AddRange(src); return; }
        dst.Add(src[0]);
        for (int i = 0; i < src.Count - 1; i++)
        {
            Vector2 a = src[i], b = src[i + 1];
            Vector2 q = Vector2.Lerp(a, b, .25f);
            Vector2 r = Vector2.Lerp(a, b, .75f);
            dst.Add(Safe(q, a, width, height, isLand));
            dst.Add(Safe(r, b, width, height, isLand));
        }
        dst.Add(src[^1]);
    }

    private static Vector2 Safe(Vector2 candidate, Vector2 fallback, int width, int height, Func<int, int, bool> isLand)
    {
        int px = Math.Clamp((int)Math.Floor(candidate.X / Cell), 0, width - 1);
        int py = Math.Clamp((int)Math.Floor((candidate.Y / Cell) + 1), 0, height - 1);
        return isLand(px, py) ? candidate : fallback;
    }

    private static Vector2 PixelCentre(int px, int py) => new((float)((px + .5) * Cell), (float)((py - .5) * Cell));

    private static int Priority(DaggerfallSiteKind kind) => kind switch
    {
        DaggerfallSiteKind.TownCity => 4,
        DaggerfallSiteKind.TownHamlet => 3,
        DaggerfallSiteKind.TownVillage => 2,
        _ => 1,
    };

    private static float Octile(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        return (Math.Min(dx, dy) * .4142135f) + Math.Max(dx, dy);
    }

    private static float HashSigned(int x, int y)
    {
        uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263) ^ 0x5bf03635U;
        h = (h ^ (h >> 13)) * 1274126177U;
        return (((h ^ (h >> 16)) & 0xFFFFU) / 32767.5f) - 1f;
    }

    internal static float PointSegmentDistance(float px, float pz, float ax, float az, float bx, float bz)
    {
        float abx = bx - ax, abz = bz - az;
        float lenSq = (abx * abx) + (abz * abz);
        if (lenSq <= 1e-6f)
        {
            float dx0 = px - ax, dz0 = pz - az;
            return MathF.Sqrt((dx0 * dx0) + (dz0 * dz0));
        }
        float t = Math.Clamp((((px - ax) * abx) + ((pz - az) * abz)) / lenSq, 0f, 1f);
        float dx = px - (ax + (abx * t)), dz = pz - (az + (abz * t));
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }
}
