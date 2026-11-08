using System.Numerics;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>A streamed block's overlapping placements draw each patch of surface once, as its site closure does.</summary>
public sealed class RmbPlacementTrimsTests
{
    [CorpusFact]
    public void A_streamed_graveyard_draws_what_its_closure_draws_with_no_patch_of_hedge_twice()
    {
        // The Hawkston Cemetery is one block, GRVEAS07. Streamed, its models are posed one by one the way the
        // runtime poses them; the trimmed placements draw their world media meshes in place of their models'.
        DungeonLogicalSourceSet sources = RmbExteriorNormalizerTests.Sources();
        WorldBlockNormalizer normalizer = new(sources);
        DaggerfallWorldBlock block = normalizer.RmbExterior("GRVEAS07.RMB").Block;
        BsaArchive arch = BsaArchive.Parse(sources.Require("ARCH3D.BSA").Bytes.Span, "arena2/ARCH3D.BSA");
        IReadOnlyList<RmbPlacementTrim> trims = RmbPlacementTrims.Read(normalizer.Blocks, arch, ["GRVEAS07.RMB"], (_, _) => default);
        Assert.Contains(trims, trim => trim.Faces.Any(face => face.TextureArchive == 33));
        Dictionary<string, RmbPlacementTrim> byPlacement = trims.ToDictionary(trim => trim.PlacementId, StringComparer.Ordinal);

        Dictionary<string, double> streamedArea = new(StringComparer.Ordinal);
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

            Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -model.RotationDegrees.Y * MathF.PI / 180F);
            foreach (RmbTrimFace face in faces)
            {
                NormalizedVector3[] placed = [.. face.Polygon.Select(point =>
                {
                    Vector3 moved = Vector3.Transform(new Vector3(point.X, point.Y, point.Z), turn) + new Vector3(model.Position.X, model.Position.Y, model.Position.Z);
                    return new NormalizedVector3(moved.X, moved.Y, moved.Z);
                })];
                for (int index = 1; index < placed.Length - 1; index++)
                {
                    NormalizedVector3[] triangle = [placed[0], placed[index], placed[index + 1]];
                    drawn.Add(triangle);
                    string material = $"texture-{face.TextureArchive}-{face.TextureRecord}";
                    streamedArea[material] = streamedArea.GetValueOrDefault(material) + Area(triangle);
                }
            }
        }

        RmbExteriorNormalizerTests.AssertNoOverlaps(drawn);
        RmbExteriorNormalizationResult closure = RmbExteriorNormalizer.Normalize(new(sources, 17, "The Hawkston Cemetery", RmbWorldProfileKind.Exterior));
        Dictionary<string, double> closureArea = new(StringComparer.Ordinal);
        foreach (NormalizedMesh mesh in closure.Document.Meshes.Where(mesh => mesh.MaterialGroups.All(group => !group.CollisionOnly)))
        {
            string material = mesh.Id[(mesh.Id.LastIndexOf('/') + 1)..];
            foreach (NormalizedTriangle triangle in mesh.Triangles)
                closureArea[material] = closureArea.GetValueOrDefault(material)
                    + Area([mesh.Vertices[triangle.FirstVertex], mesh.Vertices[triangle.SecondVertex], mesh.Vertices[triangle.ThirdVertex]]);
        }

        Assert.Equal(closureArea.Keys.Order(), streamedArea.Keys.Order());
        Assert.All(closureArea, pair => Assert.True(Math.Abs(pair.Value - streamedArea[pair.Key]) < 0.01 + (pair.Value * 1E-4),
            $"{pair.Key}: the closure draws {pair.Value:F3} m², the streamed block {streamedArea[pair.Key]:F3} m²"));
    }

    private static double Area(NormalizedVector3[] triangle)
    {
        Vector3 a = new(triangle[0].X, triangle[0].Y, triangle[0].Z), b = new(triangle[1].X, triangle[1].Y, triangle[1].Z), c = new(triangle[2].X, triangle[2].Y, triangle[2].Z);
        return Vector3.Cross(b - a, c - a).Length() / 2.0;
    }
}
