using System.Globalization;
using System.Numerics;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Source.Tool.Remodels;

/// <summary>What a remodel is held inside: the classic's convex hull, or for an open classic, its collision shape.</summary>
internal enum RemodelFit { Hull, Collision }

/// <summary>
/// The volume a classic ARCH3D model fills, which its remodel must stay inside. A remodel is render-only: the classic's
/// faces still collide and carry navigation, so what is drawn outside them the player walks into.
/// <list type="bullet">
/// <item>Its convex hull, for a classic that closes its own volume. A bounding box would let a remodel fill corners the
/// classic leaves open, as a generated wardrobe's cornice and plinth stood out of its sloped classic.</item>
/// <item>Its collision shape, for a classic that is open where the player can reach, as a wardrobe standing away from a
/// wall has no back: on its faces, or inside one of its parts whose faces close.</item>
/// </list>
/// </summary>
internal sealed class ClassicVolume
{
    /// <summary>How far a remodel's vertex may stand out of the classic: the hand-built prototypes' fit, below what is seen.</summary>
    internal const float Tolerance = 0.005F;

    // Points nearer than this are one corner: the classic stores each face's corners apart.
    private const float Weld = 0.0001F;

    private readonly (Vector3 A, Vector3 B, Vector3 C)[] _triangles;
    // Null for a flat classic, which has no inside.
    private readonly Lazy<(Vector3 Normal, float Offset)[]?> _hull;
    private readonly (Vector3 A, Vector3 B, Vector3 C)[][] _closedParts;

    /// <param name="faces">The classic's faces in its own frame, each a convex polygon.</param>
    internal ClassicVolume(IReadOnlyList<IReadOnlyList<Vector3>> faces)
    {
        _triangles = [.. faces.Where(face => face.Count >= 3).SelectMany(face => Enumerable.Range(1, face.Count - 2).Select(at => (face[0], face[at], face[at + 1])))];
        if (_triangles.Length == 0) throw new InvalidOperationException("A classic model with no face has no volume to hold a remodel.");
        Vector3[] corners = Welded(_triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }));
        _hull = new(() => Hull(corners));
        _closedParts = ClosedParts(_triangles);
    }

    /// <summary>The classic model <paramref name="model"/> in the frame its world media mesh draws it in.</summary>
    internal static ClassicVolume Read(string arena2, uint model)
    {
        string label = $"arena2/{Arch3dInventoryReader.FileName}";
        BsaArchive archive = BsaArchive.Parse(File.ReadAllBytes(Path.Combine(arena2, Arch3dInventoryReader.FileName)), label);
        if (!archive.TryGetByNumericId(model, out BsaRecord? record))
            throw new InvalidOperationException($"{label} carries no ARCH3D model {model.ToString(CultureInfo.InvariantCulture)}.");
        Arch3dMesh mesh = Arch3dDecoder.Decode(archive.GetPayload(record!).Span, label, model);
        return new([.. mesh.Planes.Select(plane => (IReadOnlyList<Vector3>)[.. plane.Points.Select(point =>
            MeshGeometry.ToRightHanded(Arena2SourceTransform.ToImportPoint(point))).Select(point => new Vector3(point.X, point.Y, point.Z))])]);
    }

    /// <summary>How far <paramref name="point"/> stands out of the volume, in metres; zero or less inside it.</summary>
    internal float Outside(Vector3 point, RemodelFit fit) => fit switch
    {
        RemodelFit.Hull => _hull.Value!.Max(plane => Vector3.Dot(plane.Normal, point) - plane.Offset),
        _ => _closedParts.Any(part => Inside(part, point)) ? 0F : _triangles.Min(triangle => Distance(point, triangle)),
    };

    /// <summary>
    /// Refuses a remodel that stands out of the volume by more than <see cref="Tolerance"/> anywhere. Against the convex hull
    /// its vertices are enough; the collision shape is not convex, so a face can span an opening between corners on its
    /// edges, and points across every face are tried, no further apart than <see cref="Spacing"/>.
    /// </summary>
    internal void Require(IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> remodel, RemodelFit fit, string model)
    {
        if (fit == RemodelFit.Hull && _hull.Value is null)
            throw new InvalidOperationException($"ARCH3D model {model} is flat, so it has no hull to hold a remodel in: promote it with fit collision, which holds it to the classic's faces.");
        Vector3[] points = [.. (fit == RemodelFit.Hull ? remodel.SelectMany(face => new[] { face.A, face.B, face.C }) : remodel.SelectMany(Samples)).Distinct()];
        (Vector3 Point, float Out)[] outside = [.. points.Select(point => (point, Outside(point, fit))).Where(value => value.Item2 > Tolerance)];
        if (outside.Length == 0) return;
        (Vector3 at, float farthest) = outside.MaxBy(value => value.Out);
        (string shape, string tried) = fit == RemodelFit.Hull ? ("convex hull", "vertices") : ("collision shape", "surface points");
        throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
            $"The remodel of ARCH3D model {model} pokes out of the classic's {shape}: {outside.Length} of its {points.Length} {tried} stand more than {Tolerance} m out, the farthest {farthest:0.000} m at ({at.X:0.###}, {at.Y:0.###}, {at.Z:0.###})."));
    }

    /// <summary>How far apart the points tried across a remodel's face are, against the collision shape.</summary>
    internal const float Spacing = 0.02F;

    /// <summary>Points across a face on a grid no coarser than <see cref="Spacing"/>, its corners among them.</summary>
    private static IEnumerable<Vector3> Samples((Vector3 A, Vector3 B, Vector3 C) face)
    {
        float longest = MathF.Max(Vector3.Distance(face.A, face.B), MathF.Max(Vector3.Distance(face.B, face.C), Vector3.Distance(face.C, face.A)));
        int steps = Math.Max(1, (int)MathF.Ceiling(longest / Spacing));
        for (int i = 0; i <= steps; i++)
            for (int j = 0; j <= steps - i; j++)
                yield return face.A + (face.B - face.A) * ((float)i / steps) + (face.C - face.A) * ((float)j / steps);
    }

    private static Vector3[] Welded(IEnumerable<Vector3> points)
    {
        List<Vector3> kept = [];
        foreach (Vector3 point in points)
            if (!kept.Any(other => Vector3.DistanceSquared(other, point) < Weld * Weld)) kept.Add(point);
        return [.. kept];
    }

    /// <summary>
    /// The hull's face planes, outward: every plane through three corners that has no corner in front of it. A classic
    /// has tens of corners, so trying every three is quick and has no degenerate case to mishandle.
    /// </summary>
    private static (Vector3 Normal, float Offset)[]? Hull(Vector3[] corners)
    {
        List<(Vector3, float)> planes = [];
        for (int a = 0; a < corners.Length; a++)
            for (int b = a + 1; b < corners.Length; b++)
                for (int c = b + 1; c < corners.Length; c++)
                {
                    Vector3 normal = Vector3.Cross(corners[b] - corners[a], corners[c] - corners[a]);
                    if (normal.LengthSquared() < 1e-12F) continue;
                    normal = Vector3.Normalize(normal);
                    float offset = Vector3.Dot(normal, corners[a]);
                    bool front = false, back = false;
                    foreach (Vector3 corner in corners)
                    {
                        float side = Vector3.Dot(normal, corner) - offset;
                        front |= side > Weld;
                        back |= side < -Weld;
                        if (front && back) break;
                    }
                    if (!front) planes.Add((normal, offset));
                    else if (!back) planes.Add((-normal, -offset));
                }

        // A classic in one plane bounds nothing.
        return planes.Count == 0 || planes.All(plane => MathF.Abs(Vector3.Dot(plane.Item1, planes[0].Item1)) > 1 - 1e-6F) ? null : [.. planes];
    }

    /// <summary>
    /// The classic's parts (faces joined by shared edges) whose every edge joins exactly two faces, so they close a volume.
    /// A part that shares an edge with another face, as a box set flush on an open one, counts as open: the gate errs
    /// toward refusing.
    /// </summary>
    private static (Vector3 A, Vector3 B, Vector3 C)[][] ClosedParts((Vector3 A, Vector3 B, Vector3 C)[] triangles)
    {
        Vector3[] corners = Welded(triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }));
        int Corner(Vector3 point) => Array.FindIndex(corners, corner => Vector3.DistanceSquared(corner, point) < Weld * Weld);
        int[][] indexed = [.. triangles.Select(triangle => new[] { Corner(triangle.A), Corner(triangle.B), Corner(triangle.C) })];
        Dictionary<(int, int), List<int>> edges = [];
        for (int face = 0; face < indexed.Length; face++)
            for (int side = 0; side < 3; side++)
            {
                int from = indexed[face][side], to = indexed[face][(side + 1) % 3];
                (int, int) key = (Math.Min(from, to), Math.Max(from, to));
                if (!edges.TryGetValue(key, out List<int>? faces)) edges[key] = faces = [];
                faces.Add(face);
            }

        int[] part = [.. Enumerable.Range(0, indexed.Length)];
        int Root(int face) => part[face] == face ? face : part[face] = Root(part[face]);
        foreach (List<int> faces in edges.Values)
            foreach (int face in faces.Skip(1)) part[Root(face)] = Root(faces[0]);
        HashSet<int> open = [.. edges.Values.Where(faces => faces.Count != 2).SelectMany(faces => faces).Select(Root)];
        return [.. Enumerable.Range(0, indexed.Length).GroupBy(Root).Where(group => !open.Contains(group.Key))
            .Select(group => group.Select(face => triangles[face]).ToArray())];
    }

    /// <summary>Whether a point is inside a closed part: a ray from it crosses the part's faces an odd number of times.</summary>
    private static bool Inside((Vector3 A, Vector3 B, Vector3 C)[] part, Vector3 point)
    {
        // A direction no classic face or edge lies along.
        Vector3 ray = Vector3.Normalize(new Vector3(0.5773F, 0.6142F, 0.5381F));
        int crossings = 0;
        foreach ((Vector3 a, Vector3 b, Vector3 c) in part)
        {
            Vector3 ab = b - a, ac = c - a, p = Vector3.Cross(ray, ac);
            float determinant = Vector3.Dot(ab, p);
            if (MathF.Abs(determinant) < 1e-12F) continue;
            Vector3 t = point - a;
            float u = Vector3.Dot(t, p) / determinant;
            if (u < 0 || u > 1) continue;
            Vector3 q = Vector3.Cross(t, ab);
            float v = Vector3.Dot(ray, q) / determinant;
            if (v < 0 || u + v > 1) continue;
            if (Vector3.Dot(ac, q) / determinant > 0) crossings++;
        }

        return crossings % 2 == 1;
    }

    /// <summary>The distance from a point to the nearest point of a triangle.</summary>
    private static float Distance(Vector3 point, (Vector3 A, Vector3 B, Vector3 C) triangle)
    {
        (Vector3 a, Vector3 b, Vector3 c) = triangle;
        Vector3 ab = b - a, ac = c - a, ap = point - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return Vector3.Distance(point, a);
        Vector3 bp = point - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return Vector3.Distance(point, b);
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return Vector3.Distance(point, a + ab * (d1 / (d1 - d3)));
        Vector3 cp = point - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return Vector3.Distance(point, c);
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return Vector3.Distance(point, a + ac * (d2 / (d2 - d6)));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return Vector3.Distance(point, b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))));
        float denominator = 1 / (va + vb + vc);
        return Vector3.Distance(point, a + ab * (vb * denominator) + ac * (vc * denominator));
    }
}
