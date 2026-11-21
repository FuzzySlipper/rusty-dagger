using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// A nature mesh (<see cref="DaggerfallTreeArt"/>) as the flora field draws it: a prop mesh of bark, leaf and stone parts,
/// each with positions, normals, uvs, colours (alpha the share a vertex flutters) and triangles, drawn at the height and
/// foot its art states in metres. Its normals are taken as written, which authoring holds to unit
/// length (trees.py refuses any other by name): normalizing one of no length here made it no number at all, which the
/// Engine refuses, faulting the update that grew the mesh (#10871). It reads the body the mesh stands as in collision too
/// (<see cref="DaggerfallNatureBody"/>).
/// </summary>
internal static class DaggerfallTreeMesh
{
    /// <summary>How high above its foot a trunk is cut to measure it, in metres: above the root flare, below the branches.</summary>
    private const float TrunkCut = .6F;
    /// <summary>How far up its height a rock is cut to measure it: through its bulk, below where it narrows to its top.</summary>
    private const float RockCut = 1F / 3;
    /// <summary>A tree shorter than this is a shrub, which bodies push through.</summary>
    private const float ShrubMetres = 2.2F;
    /// <summary>A rock lower than this is a pebble, which bodies step over without a body of its own.</summary>
    private const float PebbleMetres = .3F;

    /// <param name="bark">The bark's material, none for a rock.</param>
    /// <param name="leaves">The leaves' material, none for a bare tree.</param>
    /// <param name="stone">The stone's material, none for a tree.</param>
    internal static (MeshResourceCreateRequest Mesh, DaggerfallNatureBody? Body) Read(IContentService content, DaggerfallTreeArt art, Material? bark,
        Material? leaves, Material? stone = null)
    {
        using ContentReference reference = content.ResolveReference(new ContentResolveRequest(art.Mesh.Path, art.Mesh.Sha256));
        using JsonDocument document = JsonDocument.Parse(ReadAll(content, reference));
        float scale = art.Height / art.MeshHeight;
        Vector3 lift = new(0, art.Foot, 0);
        List<Vector3> positions = [], normals = [];
        List<Vector2> uvs = [];
        List<Color> colours = [];
        List<uint> indices = [];
        List<MeshGroup> groups = [];
        List<MeshMaterialBinding> bindings = [];
        List<(Vector3, Vector3, Vector3)> barkFaces = [], stoneFaces = [];
        foreach (JsonElement part in document.RootElement.GetProperty("parts").EnumerateArray())
        {
            string role = part.GetProperty("role").GetString()!;
            Material material = role switch
            {
                "bark" => bark ?? throw new InvalidDataException($"Tree mesh {art.Mesh.Path} has bark, but its art names no bark texture."),
                "leaves" => leaves ?? throw new InvalidDataException($"Tree mesh {art.Mesh.Path} has leaves, but its art names no leaf texture."),
                "stone" => stone ?? throw new InvalidDataException($"Tree mesh {art.Mesh.Path} has stone, but its art names no stone texture."),
                _ => throw new InvalidDataException($"Tree mesh {art.Mesh.Path} has a part of role '{role}', which is neither bark, leaves nor stone."),
            };
            uint first = (uint)positions.Count, start = (uint)indices.Count;
            List<(Vector3, Vector3, Vector3)>? measured = role switch { "bark" => barkFaces, "stone" => stoneFaces, _ => null };
            float[] p = Floats(part, "positions"), n = Floats(part, "normals"), uv = Floats(part, "uvs"), c = Floats(part, "colors");
            for (int i = 0; i < p.Length / 3; i++)
            {
                positions.Add((new Vector3(p[3 * i], p[(3 * i) + 1], p[(3 * i) + 2]) * scale) + lift);
                normals.Add(new Vector3(n[3 * i], n[(3 * i) + 1], n[(3 * i) + 2]));
                uvs.Add(new Vector2(uv[2 * i], uv[(2 * i) + 1]));
                colours.Add(new Color(c[4 * i], c[(4 * i) + 1], c[(4 * i) + 2], c[(4 * i) + 3]));
            }

            foreach (JsonElement index in part.GetProperty("indices").EnumerateArray()) indices.Add(first + index.GetUInt32());
            for (int i = (int)start; measured is not null && i + 2 < indices.Count; i += 3)
                measured.Add((positions[(int)indices[i]], positions[(int)indices[i + 1]], positions[(int)indices[i + 2]]));
            uint slot = (uint)bindings.Count;
            groups.Add(new MeshGroup(slot, start, (uint)indices.Count - start));
            bindings.Add(new MeshMaterialBinding(slot, material));
        }

        return (new MeshResourceCreateRequest(positions.ToArray(), normals.ToArray(), uvs.ToArray(), colours.ToArray(), indices.ToArray(),
            groups.ToArray(), bindings.ToArray(), ReadOnlyMemory<Vector4>.Empty), Body(art, barkFaces, stoneFaces));
    }

    /// <summary>
    /// The body a nature mesh stands as, about its foot: a rock's as tall as its stone, a tree's as tall as the tree, each
    /// as wide as the middle of where its stone or bark crosses the cut (<see cref="RockCut"/>, <see cref="TrunkCut"/>) lies
    /// from its foot. None for a shrub, a pebble, or a mesh nothing of which crosses the cut.
    /// </summary>
    internal static DaggerfallNatureBody? Body(DaggerfallTreeArt art, IReadOnlyList<(Vector3, Vector3, Vector3)> bark,
        IReadOnlyList<(Vector3, Vector3, Vector3)> stone)
    {
        if (stone.Count > 0)
        {
            float top = stone.Max(face => MathF.Max(face.Item1.Y, MathF.Max(face.Item2.Y, face.Item3.Y)));
            return top < PebbleMetres ? null : Cut(stone, top * RockCut) is { } rock ? new DaggerfallNatureBody(rock, top) : null;
        }
        return art.Height < ShrubMetres ? null : Cut(bark, TrunkCut) is { } trunk ? new DaggerfallNatureBody(trunk, art.Height) : null;
    }

    /// <summary>The middle distance from the foot's axis at which the faces' edges cross a height, or none where none does.</summary>
    private static float? Cut(IReadOnlyList<(Vector3, Vector3, Vector3)> faces, float height)
    {
        List<float> across = [];
        void Edge(Vector3 a, Vector3 b)
        {
            if ((a.Y - height) * (b.Y - height) >= 0) return;
            Vector3 at = Vector3.Lerp(a, b, (height - a.Y) / (b.Y - a.Y));
            across.Add(MathF.Sqrt((at.X * at.X) + (at.Z * at.Z)));
        }
        foreach ((Vector3 a, Vector3 b, Vector3 c) in faces)
        {
            Edge(a, b);
            Edge(b, c);
            Edge(c, a);
        }
        if (across.Count == 0) return null;
        across.Sort();
        return across[across.Count / 2];
    }

    /// <summary>The Engine serves an admitted read a mebibyte at a time, so a mesh larger than that is read in parts.</summary>
    private const uint ReadBytes = 1024 * 1024;

    private static byte[] ReadAll(IContentService content, ContentReference reference)
    {
        ulong length = content.ReadReferenceInfo(reference).Span[0].ByteLength;
        byte[] bytes = new byte[checked((int)length)];
        for (int at = 0; at < bytes.Length;)
        {
            ReadOnlyMemory<byte> part = content.ReadBytes(new ContentReadBytesRequest(reference, (ulong)at, ReadBytes));
            if (part.IsEmpty) throw new InvalidDataException($"A tree mesh ended at {at} of its {length} bytes.");
            part.CopyTo(bytes.AsMemory(at));
            at += part.Length;
        }
        return bytes;
    }

    private static float[] Floats(JsonElement part, string name) => [.. part.GetProperty(name).EnumerateArray().Select(value => value.GetSingle())];
}

/// <summary>The upright cylinder a nature mesh stands as in collision, about its foot, in metres at the drawn size.</summary>
internal readonly record struct DaggerfallNatureBody(float Radius, float Height);
