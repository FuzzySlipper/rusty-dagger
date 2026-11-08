using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>One face of a trimmed placement's mesh in the model's own frame, with the texture it draws.</summary>
public sealed record RmbTrimFace(ushort TextureArchive, ushort TextureRecord, IReadOnlyList<NormalizedVector3> Polygon, IReadOnlyList<NormalizedVector2> Uvs);

/// <summary>
/// One placement of an RMB exterior whose static faces an earlier placement of the same block already covers,
/// drawn by its own mesh: what is left of its faces in its model's frame, so it keeps the model's pose.
/// </summary>
/// <param name="PlacementId">The block's identity of the placement.</param>
/// <param name="ModelNumber">The ARCH3D model it trims.</param>
/// <param name="Faces">
/// Every face the mesh draws: the uncovered faces whole, the covered ones cut to what is left. None when
/// earlier placements cover all of it, as an exact repeat of a placement is.
/// </param>
public sealed record RmbPlacementTrim(string PlacementId, uint ModelNumber, IReadOnlyList<RmbTrimFace> Faces)
{
    /// <summary>The identity the world media publication gives the trimmed mesh, or null when nothing of the placement is left to draw.</summary>
    public string? MeshArtifactId => Faces.Count == 0 ? null : RmbPlacementTrims.MeshArtifactId(PlacementId);
}

/// <summary>
/// Finds the placements of an RMB exterior that would fight an earlier placement for the same pixels.
/// </summary>
/// <remarks>
/// Blocks lay models over each other: a graveyard's hedge runs are short and long hedge pieces along one
/// line, and some town blocks set a building's trim into another's wall. A streamed location draws each
/// placement from the model's shared mesh, so two faces in one plane and facing, with different texture
/// offsets, reach the same depth at every pixel and shimmer in motion. Taking the placements in source
/// order, as a site closure merges them (<see cref="CoplanarSurfaces"/>), a placement any of whose static
/// faces an earlier one covers is drawn by a mesh of its own with the covered part cut away. Building door
/// faces are drawn whole and cover nothing, as the closure keeps them; a city gate swaps its model by the time
/// of day and is left out.
/// </remarks>
public static class RmbPlacementTrims
{
    /// <summary>How far a model's point may land from the rigid turn and shift fitted to it, in metres.</summary>
    private const float FitTolerance = 0.002F;

    /// <summary>The block's identity of an exterior placement, which the world block publication states for it.</summary>
    public static string PlacementId(string blockSourceKey, int? buildingIndex, int ordinal) => buildingIndex is int building
        ? $"model/{PublishedIds.Slug(blockSourceKey)}/{building}/{ordinal}"
        : $"model/{PublishedIds.Slug(blockSourceKey)}/misc/{ordinal}";

    /// <summary>The world media identity of a placement's trimmed mesh.</summary>
    public static string MeshArtifactId(string placementId) => $"geometry/placement/{placementId["model/".Length..]}";

    /// <summary>
    /// The trimmed placements of every named RMB exterior, in the order named: each block read from the block
    /// archive and its models' meshes from the mesh archive, decoded once each.
    /// </summary>
    /// <param name="uv">A source point's texture coordinate on its plane, as the published meshes state it.</param>
    public static IReadOnlyList<RmbPlacementTrim> Read(BsaArchive blocks, BsaArchive arch, IEnumerable<string> blockKeys,
        Func<Arch3dPlane, Arch3dPoint, NormalizedVector2> uv)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(arch);
        ArgumentNullException.ThrowIfNull(blockKeys);
        ArgumentNullException.ThrowIfNull(uv);
        Dictionary<string, Arch3dMesh> meshes = new(StringComparer.Ordinal);
        Arch3dMesh Mesh(RmbBlockModel model) => meshes.TryGetValue(model.ModelId, out Arch3dMesh? mesh) ? mesh : meshes[model.ModelId] = RmbBlockContent.Mesh(arch, model);
        return [.. blockKeys.SelectMany(key => Read(RmbBlockContent.Read(blocks, key), Mesh, uv))];
    }

    /// <summary>The placements of one RMB exterior that draw a trimmed mesh, in source order.</summary>
    /// <param name="content">The block's content.</param>
    /// <param name="mesh">The decoded mesh a placement's model names.</param>
    /// <param name="uv">A source point's texture coordinate on its plane, as the published meshes state it.</param>
    internal static IReadOnlyList<RmbPlacementTrim> Read(RmbBlockContent content, Func<RmbBlockModel, Arch3dMesh> mesh,
        Func<Arch3dPlane, Arch3dPoint, NormalizedVector2> uv)
    {
        CoplanarSurfaces surfaces = new();
        List<RmbPlacementTrim> trims = [];
        foreach (RmbBlockModel model in content.ExteriorModels.Where(model => !RmbBlockContent.IsCityGate(model.ModelId)))
        {
            List<(Arch3dPlane Plane, NormalizedVector3[] Local, NormalizedVector3[] Placed)> planes = [.. mesh(model).Planes
                .Where(plane => plane.Points.Count >= 3)
                .Select(plane => (plane,
                    plane.Points.Select(point => MeshGeometry.ToRightHanded(Arena2SourceTransform.ToImportPoint(point))).ToArray(),
                    plane.Points.Select(point => MeshGeometry.ToRightHanded(model.Frame.Place(Arena2SourceTransform.ToImportPoint(point)))).ToArray()))];
            List<(Arch3dPlane Plane, IReadOnlyList<NormalizedVector3> Polygon, IReadOnlyList<NormalizedVector2> Uvs, bool Placed)> faces = [];
            bool trimmed = false;
            foreach ((Arch3dPlane plane, NormalizedVector3[] local, NormalizedVector3[] placed) in planes)
            {
                NormalizedVector2[] uvs = [.. plane.Points.Select(point => uv(plane, point))];
                if (RmbBlockContent.IsBuildingDoor(plane.TextureArchive))
                {
                    faces.Add((plane, local, uvs, false));
                    continue;
                }

                var kept = surfaces.Add(placed, uvs, MeshGeometry.Normal(placed));
                if (kept.Count == 1 && kept[0].Polygon.SequenceEqual(placed)) faces.Add((plane, local, uvs, false));
                else
                {
                    trimmed = true;
                    faces.AddRange(kept.Select(piece => (plane, piece.Polygon, piece.Uvs, true)));
                }
            }

            if (!trimmed) continue;
            Func<NormalizedVector3, NormalizedVector3> toLocal = Unplace(model, planes.SelectMany(plane => plane.Local.Zip(plane.Placed)));
            trims.Add(new(PlacementId(content.SourceKey, model.BuildingIndex, model.Ordinal), uint.Parse(model.ModelId, System.Globalization.CultureInfo.InvariantCulture),
                [.. faces.Select(face => new RmbTrimFace(face.Plane.TextureArchive, face.Plane.TextureRecord,
                    face.Placed ? [.. face.Polygon.Select(toLocal)] : face.Polygon, face.Uvs))]));
        }

        return trims;
    }

    /// <summary>
    /// The inverse of a placement's turn about the vertical and shift, fitted to its own points: the turn from
    /// the two points farthest apart across the ground, the shift from the first, every point then checked.
    /// </summary>
    private static Func<NormalizedVector3, NormalizedVector3> Unplace(RmbBlockModel model, IEnumerable<(NormalizedVector3 Local, NormalizedVector3 Placed)> pairs)
    {
        (NormalizedVector3 Local, NormalizedVector3 Placed)[] points = [.. pairs];
        var (a, b) = points.SelectMany(first => points.Select(second => (first, second)))
            .MaxBy(pair => Flat(pair.first.Local, pair.second.Local));
        float turn = MathF.Atan2(b.Placed.Z - a.Placed.Z, b.Placed.X - a.Placed.X) - MathF.Atan2(b.Local.Z - a.Local.Z, b.Local.X - a.Local.X);
        (float cos, float sin) = (MathF.Cos(turn), MathF.Sin(turn));
        NormalizedVector3 Turn(NormalizedVector3 p, float s) => new((cos * p.X) - (s * p.Z), p.Y, (s * p.X) + (cos * p.Z));
        NormalizedVector3 turned = Turn(a.Local, sin);
        NormalizedVector3 shift = new(a.Placed.X - turned.X, a.Placed.Y - turned.Y, a.Placed.Z - turned.Z);
        foreach ((NormalizedVector3 local, NormalizedVector3 placed) in points)
        {
            NormalizedVector3 moved = Turn(local, sin);
            if (MathF.Abs(moved.X + shift.X - placed.X) > FitTolerance || MathF.Abs(moved.Y + shift.Y - placed.Y) > FitTolerance
                || MathF.Abs(moved.Z + shift.Z - placed.Z) > FitTolerance)
                throw new InvalidOperationException($"RMB placement '{model.Identity}' of model {model.ModelId} is not a turn about the vertical and a shift; its trimmed mesh cannot keep its pose.");
        }

        return placed => Turn(new(placed.X - shift.X, placed.Y - shift.Y, placed.Z - shift.Z), -sin);
    }

    private static float Flat(NormalizedVector3 a, NormalizedVector3 b) => ((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z));
}
