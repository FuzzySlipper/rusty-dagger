using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Daggerfall.Source.Tool.Commands;
using Daggerfall.Source.Tool.Remodels;
using WorldRpg.Tests.Support;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// Promoting an ARCH3D remodel: the GLB it is authored as becomes the importer's static mesh, its classic texture slots
/// named by their identities and the textures it carries by their published paths, and a remodel that stands out of the
/// classic's volume is refused with how far, since the classic's faces are what the player collides with.
/// </summary>
public sealed class ModelMeshCommandTests
{
    /// <summary>A tub like the trough: 0.5 m wide and 2 m long at its rim, 0.4 m and 1.6 m at its foot, open below.</summary>
    private static readonly ClassicVolume Tub = new(Box(new(-0.2F, -0.25F, -0.8F), new(0.2F, -0.25F, 0.8F), new(-0.25F, 0.25F, -1F), new(0.25F, 0.25F, 1F), bottom: false));

    /// <summary>The faces of a frustum between a foot and a rim rectangle, each a quad.</summary>
    private static IReadOnlyList<IReadOnlyList<Vector3>> Box(Vector3 footMin, Vector3 footMax, Vector3 rimMin, Vector3 rimMax, bool bottom = true, bool back = true)
    {
        Vector3 F(float x, float z) => new(x < 0 ? footMin.X : footMax.X, footMin.Y, z < 0 ? footMin.Z : footMax.Z);
        Vector3 R(float x, float z) => new(x < 0 ? rimMin.X : rimMax.X, rimMin.Y, z < 0 ? rimMin.Z : rimMax.Z);
        List<IReadOnlyList<Vector3>> faces =
        [
            [R(-1, -1), R(-1, 1), R(1, 1), R(1, -1)],
            [F(-1, -1), F(1, -1), R(1, -1), R(-1, -1)],
            [F(-1, -1), R(-1, -1), R(-1, 1), F(-1, 1)],
            [F(1, -1), F(1, 1), R(1, 1), R(1, -1)],
        ];
        if (back) faces.Add([F(-1, 1), R(-1, 1), R(1, 1), F(1, 1)]);
        if (bottom) faces.Add([F(-1, -1), F(-1, 1), F(1, 1), F(1, -1)]);
        return faces;
    }

    /// <summary>A box's faces as triangles, as a remodel is authored.</summary>
    private static (Vector3, Vector3, Vector3)[] Triangles(IReadOnlyList<IReadOnlyList<Vector3>> faces) =>
        [.. faces.SelectMany(face => Enumerable.Range(1, face.Count - 2).Select(at => (face[0], face[at], face[at + 1])))];

    [Fact]
    public void A_remodel_inside_the_classics_hull_passes_and_one_filling_its_bounds_is_refused_by_how_far_it_pokes_out()
    {
        // Tapered as the classic is, a shade inside it.
        Tub.Require(Triangles(Box(new(-0.19F, -0.25F, -0.79F), new(0.19F, -0.25F, 0.79F), new(-0.24F, 0.24F, -0.99F), new(0.24F, 0.24F, 0.99F))), RemodelFit.Hull, "41209");

        // A straight-sided box filling the classic's bounding box stands out of its sloped ends and sides near the foot.
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            Tub.Require(Triangles(Box(new(-0.25F, -0.25F, -1F), new(0.25F, -0.25F, 1F), new(-0.25F, 0.25F, -1F), new(0.25F, 0.25F, 1F))), RemodelFit.Hull, "41209"));
        Assert.StartsWith("The remodel of ARCH3D model 41209 pokes out of the classic's convex hull: 4 of its 8 vertices stand more than 0.005 m out, the farthest 0.186 m",
            refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_remodel_of_an_open_classic_is_held_to_its_faces_and_closed_parts()
    {
        // A wardrobe with no back, which the player can walk into from behind, on a closed plinth set in under it.
        Vector3 min = new(-0.5F, 0.2F, -0.3F), max = new(0.5F, 2F, 0.3F);
        IReadOnlyList<IReadOnlyList<Vector3>> body = Box(min, max with { Y = 0.2F }, min with { Y = 2F }, max, back: false);
        IReadOnlyList<IReadOnlyList<Vector3>> plinth = Box(new(-0.48F, 0, -0.28F), new(0.48F, 0, 0.28F), new(-0.48F, 0.2F, -0.28F), new(0.48F, 0.2F, 0.28F));
        ClassicVolume wardrobe = new([.. body, .. plinth]);

        // Doors set into its front face's plane and a drawer inside its plinth stay inside.
        (Vector3, Vector3, Vector3)[] doors = [(new(-0.45F, 0.3F, -0.3F), new(0.45F, 0.3F, -0.3F), new(0.45F, 1.9F, -0.3F))];
        (Vector3, Vector3, Vector3)[] drawer = Triangles(Box(new(-0.4F, 0.05F, -0.25F), new(0.4F, 0.05F, 0.25F), new(-0.4F, 0.15F, -0.25F), new(0.4F, 0.15F, 0.25F)));
        wardrobe.Require([.. doors, .. drawer], RemodelFit.Collision, "41003");
        // Inside its hull too, so the hull would pass a back panel the player walks through.
        (Vector3, Vector3, Vector3)[] back = [(new(-0.5F, 0.2F, 0.3F), new(0.5F, 0.2F, 0.3F), new(0.5F, 2F, 0.3F))];
        wardrobe.Require(back, RemodelFit.Hull, "41003");
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => wardrobe.Require(back, RemodelFit.Collision, "41003"));
        Assert.StartsWith("The remodel of ARCH3D model 41003 pokes out of the classic's collision shape:", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flat_classic_has_no_hull_to_hold_a_remodel_in()
    {
        ClassicVolume board = new([[new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)]]);
        Assert.Equal("ARCH3D model 43000 is flat, so it has no hull to hold a remodel in: promote it with fit collision, which holds it to the classic's faces.",
            Assert.Throws<InvalidOperationException>(() => board.Require([(new(0.5F, 0.5F, 0), new(0.6F, 0.5F, 0), new(0.6F, 0.6F, 0))], RemodelFit.Hull, "43000")).Message);
        board.Require([(new(0.5F, 0.5F, 0.002F), new(0.6F, 0.5F, 0.002F), new(0.6F, 0.6F, 0.002F))], RemodelFit.Collision, "43000");
    }

    [Fact]
    public void A_glb_becomes_the_static_mesh_with_classic_slots_by_identity_and_carried_textures_by_path()
    {
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3];
        byte[] glb = Glb([("texture-67-0", null, Square(0), null), ("planks", png, Square(1), null), ("material/texture-85-0", null, Square(2), null), ("goods", png, Square(3), "MASK")]);
        IReadOnlyList<RemodelPart> parts = RemodelGlb.Read(glb, "41209.glb");
        Assert.Equal(["material/texture-67-0", null, "material/texture-85-0", null], parts.Select(part => part.ClassicMaterial));
        Assert.Equal([false, false, false, true], parts.Select(part => part.Mask));
        // The node's translation places the faces.
        Assert.Equal(new Vector3(0, 1, 0), parts[0].Corners[0].Position);

        (byte[] bytes, IReadOnlyList<(string Material, int Slot)> slots, int triangles, IReadOnlyDictionary<string, (byte[] Png, bool Mask)> textures) =
            ModelMeshCommand.Write("41209", parts, "packs/art/media/models");
        Assert.Equal(8, triangles);
        Assert.Equal([("material/texture-67-0", 0), ("material/texture-85-0", 1), ("packs/art/media/models/41209-0.png", 2), ("packs/art/media/models/41209-1.png", 3)], slots);
        // One PNG drawn opaque and cut out is published once for each.
        Assert.Equal([(png, false), (png, true)], textures.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value));
        using EngineBinaryContent.StaticMesh mesh = EngineBinaryContent.ReadStaticMesh(bytes);
        Assert.Equal(24, mesh.Indices.Length);
        Assert.Equal(slots.Select(slot => slot.Material), mesh.Root.GetProperty("materialSlots").EnumerateArray().Select(slot => slot.GetProperty("material").GetString()));
        Assert.Equal("remodel-41209", mesh.Root.GetProperty("asset").GetString());
    }

    [Theory]
    [InlineData("wood", false, null, "names no classic texture")]
    [InlineData("texture-67-0", false, "MASK", "cuts out a classic texture")]
    [InlineData("glass", true, "BLEND", "a remodel is opaque, or cut out at half its alpha")]
    public void A_glb_material_the_game_cannot_draw_is_refused(string name, bool png, string? alpha, string reason)
    {
        byte[] glb = Glb([(name, png ? [0x89, (byte)'P'] : null, Square(0), alpha)]);
        Assert.Contains(reason, Assert.Throws<InvalidDataException>(() => RemodelGlb.Read(glb, "41209.glb")).Message, StringComparison.Ordinal);
    }

    /// <summary>Two triangles of a unit square at height <paramref name="z"/>, in its own frame.</summary>
    private static Vector3[] Square(float z) => [new(0, 0, z), new(1, 0, z), new(1, 1, z), new(0, 0, z), new(1, 1, z), new(0, 1, z)];

    /// <summary>A GLB of one node, lifted 1 m, with a primitive per material; a material with a PNG carries it as its base colour.</summary>
    private static byte[] Glb(IReadOnlyList<(string Name, byte[]? Png, Vector3[] Corners, string? Alpha)> materials)
    {
        List<byte> binary = [];
        List<object> views = [], accessors = [], primitives = [], images = [], textures = [], materialList = [];
        int View(byte[] data)
        {
            while (binary.Count % 4 != 0) binary.Add(0);
            views.Add(new { buffer = 0, byteOffset = binary.Count, byteLength = data.Length });
            binary.AddRange(data);
            return views.Count - 1;
        }
        int Floats(IEnumerable<float> values, int count, string type)
        {
            byte[] data = [.. values.SelectMany(value => BitConverter.GetBytes(value))];
            accessors.Add(new { bufferView = View(data), componentType = 5126, count, type });
            return accessors.Count - 1;
        }
        foreach ((string name, byte[]? png, Vector3[] corners, string? alpha) in materials)
        {
            Dictionary<string, object> material = new() { ["name"] = name };
            if (alpha is not null) material["alphaMode"] = alpha;
            if (png is not null)
            {
                images.Add(new { bufferView = View(png), mimeType = "image/png" });
                textures.Add(new { source = images.Count - 1 });
                material["pbrMetallicRoughness"] = new { baseColorTexture = new { index = textures.Count - 1 } };
            }
            materialList.Add(material);
            primitives.Add(new
            {
                attributes = new Dictionary<string, int>
                {
                    ["POSITION"] = Floats(corners.SelectMany(corner => new[] { corner.X, corner.Y, corner.Z }), corners.Length, "VEC3"),
                    ["NORMAL"] = Floats(corners.SelectMany(_ => new[] { 0F, 0F, 1F }), corners.Length, "VEC3"),
                    ["TEXCOORD_0"] = Floats(corners.SelectMany(corner => new[] { corner.X, corner.Y }), corners.Length, "VEC2"),
                },
                material = materialList.Count - 1,
            });
        }
        while (binary.Count % 4 != 0) binary.Add(0);
        byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            asset = new { version = "2.0" }, scene = 0, scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[] { new { mesh = 0, translation = new[] { 0F, 1F, 0F } } }, meshes = new[] { new { primitives } },
            materials = materialList, images, textures, accessors, bufferViews = views, buffers = new[] { new { byteLength = binary.Count } },
        }));
        byte[] padded = [.. json, .. Enumerable.Repeat((byte)' ', (4 - json.Length % 4) % 4)];
        byte[] glb = new byte[12 + 8 + padded.Length + 8 + binary.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(glb, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8), (uint)glb.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12), (uint)padded.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16), 0x4E4F534A);
        padded.CopyTo(glb, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(20 + padded.Length), (uint)binary.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(24 + padded.Length), 0x004E4942);
        binary.CopyTo(glb, 28 + padded.Length);
        return glb;
    }
}
