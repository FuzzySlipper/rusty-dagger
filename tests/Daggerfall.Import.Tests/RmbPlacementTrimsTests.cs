using System.Numerics;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>A streamed block's overlapping placements draw each patch of surface once.</summary>
public sealed class RmbPlacementTrimsTests
{
    [CorpusFact]
    public void A_streamed_graveyard_draws_no_patch_of_hedge_twice()
    {
        // The Hawkston Cemetery is one block, GRVEAS07. Streamed, its models are posed one by one the way the
        // runtime poses them; the trimmed placements draw their world media meshes in place of their models'.
        DungeonLogicalSourceSet sources = WorldBlockTests.Sources();
        WorldBlockNormalizer normalizer = new(sources);
        DaggerfallWorldBlock block = normalizer.RmbExterior("GRVEAS07.RMB").Block;
        BsaArchive arch = BsaArchive.Parse(sources.Require("ARCH3D.BSA").Bytes.Span, "arena2/ARCH3D.BSA");
        IReadOnlyList<RmbPlacementTrim> trims = RmbPlacementTrims.Read(normalizer.Blocks, arch, ["GRVEAS07.RMB"], (_, _) => default);
        Assert.Contains(trims, trim => trim.Faces.Any(face => face.TextureArchive == 33));
        Dictionary<string, RmbPlacementTrim> byPlacement = trims.ToDictionary(trim => trim.PlacementId, StringComparer.Ordinal);

        List<NormalizedVector3[]> drawn = [];
        foreach (DaggerfallWorldBlockModel model in block.Models)
        {
            IEnumerable<RmbTrimFace> faces;
            if (byPlacement.TryGetValue(model.Id, out RmbPlacementTrim? trim))
            {
                Assert.Equal(trim.MeshArtifactId, model.MeshArtifactId);
                faces = trim.Faces;
            }
            else
            {
                Assert.Equal(GeometryPublicationBuilder.MeshArtifactId(uint.Parse(model.ModelId)), model.MeshArtifactId);
                uint number = uint.Parse(model.ModelId);
                Assert.True(arch.TryGetByNumericId(number, out BsaRecord? record));
                faces = Arch3dDecoder.Decode(arch.GetPayload(record!).Span, arch.Source, number).Planes.Where(plane => plane.Points.Count >= 3)
                    .Select(plane => new RmbTrimFace(plane.TextureArchive, plane.TextureRecord,
                        [.. plane.Points.Select(point => MeshGeometry.ToRightHanded(Arena2SourceTransform.ToImportPoint(point)))], []));
            }

            Quaternion turn = model.Rotation.ToQuaternion();
            foreach (RmbTrimFace face in faces)
            {
                NormalizedVector3[] placed = [.. face.Polygon.Select(point =>
                {
                    Vector3 moved = Vector3.Transform(new Vector3(point.X, point.Y, point.Z), turn) + new Vector3(model.Position.X, model.Position.Y, model.Position.Z);
                    return new NormalizedVector3(moved.X, moved.Y, moved.Z);
                })];
                for (int index = 1; index < placed.Length - 1; index++)
                    drawn.Add([placed[0], placed[index], placed[index + 1]]);
            }
        }

        AssertNoOverlaps(drawn);
    }

    /// <summary>Refuses two drawn triangles sharing more than an edge in one plane and facing.</summary>
    private static void AssertNoOverlaps(IEnumerable<NormalizedVector3[]> drawn)
    {
        List<string> overlaps = [];
        foreach (IGrouping<(int, int, int, int), NormalizedVector3[]> plane in drawn.Where(triangle => Normal(triangle) != default).GroupBy(PlaneKey))
        {
            NormalizedVector3[][] triangles = [.. plane];
            for (int first = 0; first < triangles.Length; first++)
                for (int second = first + 1; second < triangles.Length; second++)
                    if (Overlap(triangles[first], triangles[second], Normal(triangles[first])))
                        overlaps.Add(string.Join(" ", triangles[first]) + " | " + string.Join(" ", triangles[second]));
        }
        Assert.True(overlaps.Count == 0, $"{overlaps.Count} overlapping coplanar pairs, first: {overlaps.FirstOrDefault()}");
    }

    private static (int, int, int, int) PlaneKey(NormalizedVector3[] triangle)
    {
        NormalizedVector3 normal = Normal(triangle);
        return ((int)MathF.Round(normal.X * 100F), (int)MathF.Round(normal.Y * 100F), (int)MathF.Round(normal.Z * 100F),
            (int)MathF.Round(((normal.X * triangle[0].X) + (normal.Y * triangle[0].Y) + (normal.Z * triangle[0].Z)) * 100F));
    }

    private static NormalizedVector3 Normal(NormalizedVector3[] triangle)
    {
        NormalizedVector3 a = new(triangle[1].X - triangle[0].X, triangle[1].Y - triangle[0].Y, triangle[1].Z - triangle[0].Z);
        NormalizedVector3 b = new(triangle[2].X - triangle[0].X, triangle[2].Y - triangle[0].Y, triangle[2].Z - triangle[0].Z);
        NormalizedVector3 cross = new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));
        float length = MathF.Sqrt((cross.X * cross.X) + (cross.Y * cross.Y) + (cross.Z * cross.Z));
        return length > 1E-6F ? new(cross.X / length, cross.Y / length, cross.Z / length) : default;
    }

    /// <summary>Whether two triangles in one plane share more than an edge, by the separating edges of either.</summary>
    private static bool Overlap(NormalizedVector3[] first, NormalizedVector3[] second, NormalizedVector3 normal)
    {
        int drop = MathF.Abs(normal.X) > MathF.Abs(normal.Y) ? (MathF.Abs(normal.X) > MathF.Abs(normal.Z) ? 0 : 2) : (MathF.Abs(normal.Y) > MathF.Abs(normal.Z) ? 1 : 2);
        (float, float) Flat(NormalizedVector3 point) => drop switch { 0 => (point.Y, point.Z), 1 => (point.X, point.Z), _ => (point.X, point.Y) };
        (float X, float Y)[] a = [.. first.Select(Flat)], b = [.. second.Select(Flat)];
        foreach ((float X, float Y)[] triangle in new[] { a, b })
            for (int edge = 0; edge < 3; edge++)
            {
                (float x0, float y0) = triangle[edge];
                (float x1, float y1) = triangle[(edge + 1) % 3];
                (float nx, float ny) = (y0 - y1, x1 - x0);
                float length = MathF.Sqrt((nx * nx) + (ny * ny));
                if (length < 1E-6F) continue;
                float[] pa = [.. a.Select(p => ((p.X * nx) + (p.Y * ny)) / length)], pb = [.. b.Select(p => ((p.X * nx) + (p.Y * ny)) / length)];
                if (pa.Max() <= pb.Min() + 0.002F || pb.Max() <= pa.Min() + 0.002F) return false;
            }
        return true;
    }
}
