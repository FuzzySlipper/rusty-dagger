using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>
/// The material-agnostic part of turning source mesh planes into normalized geometry: the right-handed
/// frame, the polygon normal, the fan a polygon triangulates to, and the bounds of a vertex list.
/// </summary>
/// <remarks>
/// One owner because the product has two views of the same mesh numbers — the dungeon's assembled static
/// mesh and the published per-mesh geometry artifact — and a frame, winding, normal rule or bounds rule
/// that differed between them would leave those two views disagreeing with nothing failing.
/// </remarks>
public static class MeshGeometry
{
    /// <summary>Places one source point into the product's right-handed frame.</summary>
    public static NormalizedVector3 ToRightHanded(Arena2ImportPoint point) => new(point.XMetres, point.YMetres, -point.ZMetres);

    /// <summary>
    /// The polygon's normal, taken from the first fan triangle that has area, so a polygon whose leading
    /// points happen to be collinear states its own plane rather than a substituted up vector.
    /// </summary>
    public static NormalizedVector3 Normal(IReadOnlyList<NormalizedVector3> polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        for (int index = 1; index < polygon.Count - 1; index++)
        {
            NormalizedVector3 normal = Cross(polygon[0], polygon[index], polygon[index + 1]);
            if (normal != default)
            {
                return normal;
            }
        }

        return new(0F, 1F, 0F);
    }

    /// <summary>
    /// Appends one polygon's vertices, per-vertex normals and coordinates, and its fan triangles.
    /// </summary>
    /// <returns>The first triangle index the polygon added and how many it added.</returns>
    public static (int FirstTriangle, int TriangleCount) AppendPolygon(
        List<NormalizedVector3> vertices,
        List<NormalizedVector3> normals,
        List<NormalizedVector2> textureCoordinates,
        List<NormalizedTriangle> triangles,
        IReadOnlyList<NormalizedVector3> polygon,
        IReadOnlyList<NormalizedVector2> uvs,
        NormalizedVector3 normal)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(textureCoordinates);
        ArgumentNullException.ThrowIfNull(triangles);
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentNullException.ThrowIfNull(uvs);
        if (polygon.Count != uvs.Count)
        {
            throw new InvalidOperationException($"A polygon of {polygon.Count} points carries {uvs.Count} texture coordinates.");
        }

        int firstVertex = vertices.Count;
        int firstTriangle = triangles.Count;
        vertices.AddRange(polygon);
        normals.AddRange(Enumerable.Repeat(normal, polygon.Count));
        textureCoordinates.AddRange(uvs);
        for (int index = 1; index < polygon.Count - 1; index++)
        {
            triangles.Add(new(firstVertex, firstVertex + index, firstVertex + index + 1));
        }

        return (firstTriangle, triangles.Count - firstTriangle);
    }

    /// <summary>The bounds of a vertex list, which every published geometry artifact states.</summary>
    public static NormalizedBounds Bounds(IReadOnlyList<NormalizedVector3> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Count == 0)
        {
            throw new InvalidOperationException("Normalized geometry has no vertices to bound.");
        }

        return new(
            NormalizedBounds.CurrentSchemaVersion,
            new(vertices.Min(vertex => vertex.X), vertices.Min(vertex => vertex.Y), vertices.Min(vertex => vertex.Z)),
            new(vertices.Max(vertex => vertex.X), vertices.Max(vertex => vertex.Y), vertices.Max(vertex => vertex.Z)));
    }

    private static NormalizedVector3 Cross(NormalizedVector3 first, NormalizedVector3 second, NormalizedVector3 third)
    {
        float ax = second.X - first.X;
        float ay = second.Y - first.Y;
        float az = second.Z - first.Z;
        float bx = third.X - first.X;
        float by = third.Y - first.Y;
        float bz = third.Z - first.Z;
        float x = (ay * bz) - (az * by);
        float y = (az * bx) - (ax * bz);
        float z = (ax * by) - (ay * bx);
        float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
        return length > 1E-12F ? new(x / length, y / length, z / length) : default;
    }
}
