using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// A nature mesh (<see cref="DaggerfallTreeArt"/>) as the flora field draws it: a prop mesh of bark, leaf and stone parts,
/// each with positions, normals, uvs, colours (alpha the share a vertex flutters) and triangles, drawn at its sprite's
/// drawn height with its foot where the sprite's stands. Its normals are taken as written, which authoring holds to unit
/// length (trees.py refuses any other by name): normalizing one of no length here made it no number at all, which the
/// Engine refuses, faulting the update that grew the mesh (#10871).
/// </summary>
internal static class DaggerfallTreeMesh
{
    /// <param name="spriteHeight">The sprite's world height, of which the art's height and foot are shares.</param>
    /// <param name="bark">The bark's material, none for a rock.</param>
    /// <param name="leaves">The leaves' material, none for a bare tree.</param>
    /// <param name="stone">The stone's material, none for a tree.</param>
    internal static MeshResourceCreateRequest Read(IContentService content, DaggerfallTreeArt art, float spriteHeight, Material? bark, Material? leaves,
        Material? stone = null)
    {
        using ContentReference reference = content.ResolveReference(new ContentResolveRequest(art.Mesh.Path, art.Mesh.Sha256));
        using JsonDocument document = JsonDocument.Parse(ReadAll(content, reference));
        float scale = art.Height * spriteHeight / art.MeshHeight;
        Vector3 lift = new(0, art.Foot * spriteHeight, 0);
        List<Vector3> positions = [], normals = [];
        List<Vector2> uvs = [];
        List<Color> colours = [];
        List<uint> indices = [];
        List<MeshGroup> groups = [];
        List<MeshMaterialBinding> bindings = [];
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
            float[] p = Floats(part, "positions"), n = Floats(part, "normals"), uv = Floats(part, "uvs"), c = Floats(part, "colors");
            for (int i = 0; i < p.Length / 3; i++)
            {
                positions.Add((new Vector3(p[3 * i], p[(3 * i) + 1], p[(3 * i) + 2]) * scale) + lift);
                normals.Add(new Vector3(n[3 * i], n[(3 * i) + 1], n[(3 * i) + 2]));
                uvs.Add(new Vector2(uv[2 * i], uv[(2 * i) + 1]));
                colours.Add(new Color(c[4 * i], c[(4 * i) + 1], c[(4 * i) + 2], c[(4 * i) + 3]));
            }

            foreach (JsonElement index in part.GetProperty("indices").EnumerateArray()) indices.Add(first + index.GetUInt32());
            uint slot = (uint)bindings.Count;
            groups.Add(new MeshGroup(slot, start, (uint)indices.Count - start));
            bindings.Add(new MeshMaterialBinding(slot, material));
        }

        return new MeshResourceCreateRequest(positions.ToArray(), normals.ToArray(), uvs.ToArray(), colours.ToArray(), indices.ToArray(),
            groups.ToArray(), bindings.ToArray(), ReadOnlyMemory<Vector4>.Empty);
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
