using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>
/// Keeps one drawn copy of every patch of surface where placed models lie face to face in the same plane.
/// </summary>
/// <remarks>
/// Blocks place models that overlap: a cemetery's hedge runs are short and long hedge pieces laid over each
/// other along one line, so two faces of the same hedge share a plane and a facing with differently offset
/// texture coordinates. Merged into one static mesh they reach the same depth at every pixel and the GPU
/// picks between them frame to frame, which reads as a shimmer in motion. The classic renderer drew them
/// one after the other and never showed it. Each polygon is cut by the convex polygons placed before it in
/// its plane and facing, so the first placed keeps the shared patch and the later keeps only what it adds.
/// Polygons facing apart are both kept, since back-face culling never draws both from one side.
/// </remarks>
public sealed class CoplanarSurfaces
{
    /// <summary>How far apart two planes or a point and an edge may be and still count as touching, in metres.</summary>
    private const float Tolerance = 0.001F;

    /// <summary>The width of the plane-offset buckets a polygon is filed under; any value well above <see cref="Tolerance"/>.</summary>
    private const float BucketWidth = 0.05F;

    private readonly Dictionary<long, List<Face>> faces = [];

    /// <summary>
    /// Records one planar convex polygon and returns the convex pieces of it that no polygon recorded before
    /// it already covers in its plane and facing: the polygon itself when nothing overlaps it, none when it
    /// is wholly covered.
    /// </summary>
    public IReadOnlyList<(IReadOnlyList<NormalizedVector3> Polygon, IReadOnlyList<NormalizedVector2> Uvs)> Add(
        IReadOnlyList<NormalizedVector3> polygon, IReadOnlyList<NormalizedVector2> uvs, NormalizedVector3 normal)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentNullException.ThrowIfNull(uvs);
        if (polygon.Count != uvs.Count)
            throw new InvalidOperationException($"A polygon of {polygon.Count} points carries {uvs.Count} texture coordinates.");

        Face face = new([.. polygon], normal);
        long bucket = (long)MathF.Round(face.Offset / BucketWidth);
        List<Vertex[]> pieces = [[.. polygon.Select((point, index) => new Vertex(point, uvs[index]))]];
        for (long neighbour = bucket - 1; neighbour <= bucket + 1 && pieces.Count != 0; neighbour++)
        {
            if (!faces.TryGetValue(neighbour, out List<Face>? placed)) continue;
            foreach (Face earlier in placed.Where(earlier => face.SharesPlane(earlier)))
            {
                pieces = [.. pieces.SelectMany(piece => Subtract(piece, earlier))];
                if (pieces.Count == 0) break;
            }
        }

        if (!faces.TryGetValue(bucket, out List<Face>? filed)) faces.Add(bucket, filed = []);
        filed.Add(face);
        return [.. pieces.Select(piece => ((IReadOnlyList<NormalizedVector3>)[.. piece.Select(vertex => vertex.Point)],
            (IReadOnlyList<NormalizedVector2>)[.. piece.Select(vertex => vertex.Uv)]))];
    }

    /// <summary>
    /// The convex pieces of <paramref name="piece"/> outside the convex <paramref name="cover"/>: unchanged when
    /// the two only touch, otherwise cut along each of the cover's edges in turn, the part inside them all dropped.
    /// </summary>
    private static IEnumerable<Vertex[]> Subtract(Vertex[] piece, Face cover)
    {
        if (cover.Separates(piece) || Face.SeparatedBy(piece, cover)) return [piece];
        List<Vertex[]> outside = [];
        Vertex[] remaining = piece;
        for (int edge = 0; edge < cover.EdgeCount && remaining.Length >= 3; edge++)
        {
            (Vertex[] beyond, Vertex[] within) = Split(remaining, point => cover.Inside(edge, point));
            if (Area(beyond) > Tolerance * Tolerance) outside.Add(beyond);
            remaining = within;
        }
        return outside;
    }

    /// <summary>Splits a convex polygon by a signed distance, interpolating position and texture coordinate where an edge crosses.</summary>
    private static (Vertex[] Negative, Vertex[] Positive) Split(Vertex[] polygon, Func<NormalizedVector3, float> distance)
    {
        List<Vertex> negative = [], positive = [];
        for (int index = 0; index < polygon.Length; index++)
        {
            Vertex current = polygon[index], next = polygon[(index + 1) % polygon.Length];
            float a = distance(current.Point), b = distance(next.Point);
            if (a <= Tolerance) negative.Add(current);
            if (a >= -Tolerance) positive.Add(current);
            if ((a < -Tolerance && b > Tolerance) || (a > Tolerance && b < -Tolerance))
            {
                Vertex crossing = Vertex.Lerp(current, next, a / (a - b));
                negative.Add(crossing);
                positive.Add(crossing);
            }
        }
        return ([.. negative], [.. positive]);
    }

    private readonly record struct Vertex(NormalizedVector3 Point, NormalizedVector2 Uv)
    {
        public static Vertex Lerp(Vertex from, Vertex to, float t) => new(
            new(from.Point.X + ((to.Point.X - from.Point.X) * t), from.Point.Y + ((to.Point.Y - from.Point.Y) * t), from.Point.Z + ((to.Point.Z - from.Point.Z) * t)),
            new(from.Uv.X + ((to.Uv.X - from.Uv.X) * t), from.Uv.Y + ((to.Uv.Y - from.Uv.Y) * t)));
    }

    /// <summary>A recorded polygon with its plane and the inward direction of each edge within it.</summary>
    private sealed class Face
    {
        private readonly NormalizedVector3[] points;
        private readonly NormalizedVector3 normal;
        private readonly List<(NormalizedVector3 Start, NormalizedVector3 Inward)> edges = [];

        public Face(NormalizedVector3[] points, NormalizedVector3 normal)
        {
            this.points = points;
            this.normal = normal;
            Offset = Dot(normal, points[0]);
            NormalizedVector3 centre = new(points.Average(point => point.X), points.Average(point => point.Y), points.Average(point => point.Z));
            for (int index = 0; index < points.Length; index++)
            {
                NormalizedVector3 from = points[index], to = points[(index + 1) % points.Length];
                NormalizedVector3 across = Unit(Cross(normal, Subtract(to, from)));
                if (across == default) continue;
                edges.Add((from, Dot(across, Subtract(centre, from)) >= 0F ? across : Scale(across, -1F)));
            }
        }

        public float Offset { get; }

        public int EdgeCount => edges.Count;

        public bool SharesPlane(Face other) =>
            Dot(normal, other.normal) > 1F - 1E-4F && MathF.Abs(Offset - other.Offset) <= Tolerance;

        /// <summary>How far inside this face's edge a point lies, in metres; negative outside it.</summary>
        public float Inside(int edge, NormalizedVector3 point) => Dot(edges[edge].Inward, Subtract(point, edges[edge].Start));

        /// <summary>Whether one of this face's edges has all of <paramref name="polygon"/> on or outside it.</summary>
        public bool Separates(Vertex[] polygon)
        {
            for (int edge = 0; edge < edges.Count; edge++)
                if (polygon.All(vertex => Inside(edge, vertex.Point) <= Tolerance)) return true;
            return false;
        }

        /// <summary>Whether one of <paramref name="polygon"/>'s own edges has all of <paramref name="face"/> on or outside it.</summary>
        public static bool SeparatedBy(Vertex[] polygon, Face face) =>
            new Face([.. polygon.Select(vertex => vertex.Point)], face.normal).Separates([.. face.points.Select(point => new Vertex(point, default))]);
    }

    private static float Area(Vertex[] polygon)
    {
        NormalizedVector3 sum = default;
        for (int index = 1; index < polygon.Length - 1; index++)
        {
            NormalizedVector3 cross = Cross(Subtract(polygon[index].Point, polygon[0].Point), Subtract(polygon[index + 1].Point, polygon[0].Point));
            sum = new(sum.X + cross.X, sum.Y + cross.Y, sum.Z + cross.Z);
        }
        return MathF.Sqrt(Dot(sum, sum)) / 2F;
    }

    private static NormalizedVector3 Subtract(NormalizedVector3 a, NormalizedVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static NormalizedVector3 Scale(NormalizedVector3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);

    private static float Dot(NormalizedVector3 a, NormalizedVector3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static NormalizedVector3 Cross(NormalizedVector3 a, NormalizedVector3 b) =>
        new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    private static NormalizedVector3 Unit(NormalizedVector3 a)
    {
        float length = MathF.Sqrt(Dot(a, a));
        return length > 1E-9F ? Scale(a, 1F / length) : default;
    }
}
