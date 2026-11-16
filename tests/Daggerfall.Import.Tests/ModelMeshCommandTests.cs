using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Daggerfall.Import.Normalization;
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
        // An 8-bit RGB texture, as image tools often write one: the Engine admits only 8-bit RGBA.
        byte[] png = Png(2, [200, 120, 40, 10, 20, 30]);
        byte[] glb = Glb([("texture-67-0", null, Square(0), null, null), ("planks", png, Square(1), null, null), ("material/texture-85-0", null, Square(2), null, null), ("goods", png, Square(3), "MASK", null)]);
        IReadOnlyList<RemodelPart> parts = RemodelGlb.Read(glb, "41209.glb");
        Assert.Equal(["material/texture-67-0", null, "material/texture-85-0", null], parts.Select(part => part.ClassicMaterial));
        Assert.Equal([false, false, false, true], parts.Select(part => part.Mask));
        // The node's translation places the faces.
        Assert.Equal(new Vector3(0, 1, 0), parts[0].Corners[0].Position);

        (byte[] bytes, IReadOnlyList<(string Material, int Slot, RemodelShell? Shell, (string Identity, IReadOnlyDictionary<string, string> Drawn)? Paint)> slots, int triangles,
            IReadOnlyDictionary<string, (byte[] Png, bool Mask, RemodelStrip? Strip, bool Unlit)> textures) = ModelMeshCommand.Write("41209", parts, "packs/art/media/models");
        Assert.Equal(8, triangles);
        Assert.Equal([("material/texture-67-0", 0, null), ("material/texture-85-0", 1, null), ("packs/art/media/models/41209-0.png", 2, null), ("packs/art/media/models/41209-1.png", 3, null)],
            slots.Select(slot => (slot.Material, slot.Slot, slot.Shell)));
        Assert.All(slots, slot => Assert.Null(slot.Paint));
        // One PNG drawn opaque and cut out is published once for each, written again as 8-bit RGBA with its colours kept.
        Assert.Equal([false, true], textures.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value.Mask));
        Assert.All(textures.Values, texture =>
        {
            Assert.Equal([8, 6, 0, 0, 0], texture.Png[24..29]);
            Assert.Equal([200, 120, 40, 255, 10, 20, 30, 255], DeterministicPngReader.ReadRgba8(texture.Png, "written").Rgba);
        });
        using EngineBinaryContent.StaticMesh mesh = EngineBinaryContent.ReadStaticMesh(bytes);
        Assert.Equal(24, mesh.Indices.Length);
        Assert.Equal(slots.Select(slot => slot.Material), mesh.Root.GetProperty("materialSlots").EnumerateArray().Select(slot => slot.GetProperty("material").GetString()));
        Assert.Equal("remodel-41209", mesh.Root.GetProperty("asset").GetString());
    }

    [Fact]
    public void A_classic_texture_drawn_in_shells_is_a_slot_per_layer_and_a_stack_missing_a_layer_is_refused()
    {
        // A hedge's stack of three: its core and two layers inset over it, beside a plain classic slot.
        byte[] glb = Glb([("texture-33-0", null, Square(0), null, new RemodelShell(0, 3)), ("texture-33-0", null, Square(1), null, new RemodelShell(1, 3)),
            ("material/texture-33-0", null, Square(2), null, new RemodelShell(2, 3)), ("texture-29-0", null, Square(3), null, null)]);
        IReadOnlyList<RemodelPart> parts = RemodelGlb.Read(glb, "750.glb");
        Assert.All(parts, part => Assert.StartsWith("material/texture-", part.ClassicMaterial, StringComparison.Ordinal));
        Assert.Equal([new RemodelShell(0, 3), new RemodelShell(1, 3), new RemodelShell(2, 3), null], parts.Select(part => part.Shell));

        (byte[] bytes, IReadOnlyList<(string Material, int Slot, RemodelShell? Shell, (string Identity, IReadOnlyDictionary<string, string> Drawn)? Paint)> slots, _, IReadOnlyDictionary<string, (byte[] Png, bool Mask, RemodelStrip? Strip, bool Unlit)> textures) =
            ModelMeshCommand.Write("750", parts, "packs/art/media/models");
        Assert.Empty(textures);
        // Every layer is a slot of its own drawing the same classic texture, the mesh naming each by its layer.
        Assert.Equal([("material/texture-29-0", null), ("material/texture-33-0", new RemodelShell(0, 3)), ("material/texture-33-0", new RemodelShell(1, 3)),
            ("material/texture-33-0", new RemodelShell(2, 3))], slots.OrderBy(slot => slot.Slot).Select(slot => (slot.Material, slot.Shell)));
        using EngineBinaryContent.StaticMesh mesh = EngineBinaryContent.ReadStaticMesh(bytes);
        Assert.Contains("material/texture-33-0/shell-1-of-3", mesh.Root.GetProperty("materialSlots").EnumerateArray().Select(slot => slot.GetProperty("material").GetString()));

        IReadOnlyList<RemodelPart> holed = RemodelGlb.Read(Glb([("texture-33-0", null, Square(0), null, new RemodelShell(0, 3)), ("texture-33-0", null, Square(2), null, new RemodelShell(2, 3))]), "750.glb");
        Assert.Equal("The remodel of ARCH3D model 750 draws material/texture-33-0 in shell layers 0 of 3, 2 of 3: a stack has each of its layers.",
            Assert.Throws<InvalidOperationException>(() => ModelMeshCommand.Write("750", holed, "packs/art/media/models")).Message);
    }

    /// <summary>
    /// A generated rock is painted for the classic stone it stands in for, and once more for each stone a location may draw
    /// that one as, so it matches the town's stone where the classic's climate swap would change it.
    /// </summary>
    [Fact]
    public void A_glb_material_painted_for_a_classic_identity_carries_a_texture_for_each_identity_it_may_be_drawn_as()
    {
        byte[] grey = Png(2, [90, 90, 90, 120, 120, 120]), pale = Png(2, [180, 180, 180, 200, 200, 200]), dark = Png(2, [60, 60, 60, 80, 80, 80]);
        byte[] glb = Glb([("texture-141-2", grey, Square(0), null, null), ("texture-122-2", null, Square(1), null, null)],
            new Dictionary<string, IReadOnlyDictionary<string, byte[]>> { ["texture-141-2"] = new Dictionary<string, byte[]> { ["texture-41-2"] = pale, ["texture-341-2"] = dark } });
        IReadOnlyList<RemodelPart> parts = RemodelGlb.Read(glb, "41723.glb");
        Assert.Equal(["material/texture-141-2", "material/texture-122-2"], parts.Select(part => part.ClassicMaterial));
        Assert.NotNull(parts[0].Png);
        Assert.Null(parts[1].Png);
        Assert.Equal(["material/texture-341-2", "material/texture-41-2"], parts[0].Drawn!.Keys.Order(StringComparer.Ordinal));

        (_, IReadOnlyList<(string Material, int Slot, RemodelShell? Shell, (string Identity, IReadOnlyDictionary<string, string> Drawn)? Paint)> slots, _, IReadOnlyDictionary<string, (byte[] Png, bool Mask, RemodelStrip? Strip, bool Unlit)> textures) =
            ModelMeshCommand.Write("41723", parts, "media/models");
        // The painted slot is named by its own PNG, which the location draws for 141-2 itself; the classic one by its identity.
        Assert.Equal([("material/texture-122-2", 0), ("media/models/41723-0.png", 1)], slots.Select(slot => (slot.Material, slot.Slot)));
        (string identity, IReadOnlyDictionary<string, string> drawn) = slots[1].Paint!.Value;
        Assert.Equal("material/texture-141-2", identity);
        Assert.Null(slots[0].Paint);
        Assert.Equal(3, textures.Count);
        Assert.Equal([180, 180, 180, 255, 200, 200, 200, 255], DeterministicPngReader.ReadRgba8(textures[drawn["material/texture-41-2"]].Png, "pale").Rgba);
    }

    /// <summary>The same pixels painted for one classic identity, for another, or drawn as the remodel's own are a slot each.</summary>
    [Fact]
    public void Pixels_painted_for_a_classic_identity_are_a_slot_apart_from_the_same_pixels_drawn_any_other_way()
    {
        byte[] grey = Png(2, [90, 90, 90, 120, 120, 120]);
        byte[] glb = Glb([("texture-141-2", grey, Square(0), null, null), ("rock", grey, Square(1), null, null), ("texture-0-75", grey, Square(2), null, null)]);
        (_, var slots, _, var textures) = ModelMeshCommand.Write("41723", RemodelGlb.Read(glb, "41723.glb"), "media/models");
        Assert.Equal(3, slots.Select(slot => slot.Material).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal([null, "material/texture-0-75", "material/texture-141-2"], slots.Select(slot => slot.Paint?.Identity).Order(StringComparer.Ordinal));
        Assert.Equal(3, textures.Count);
    }

    [Theory]
    [InlineData("texture-141-2", false, """{"texture-41-2": 0}""", "states textures by drawn identity but carries no base colour texture of its own")]
    [InlineData("rock", true, """{"texture-41-2": 0}""", "states textures by drawn identity but names no classic texture")]
    [InlineData("texture-141-2", true, """[0]""", "states its drawn textures as no map")]
    [InlineData("texture-141-2", true, """{"texture-41-2": {"index": 0}}""", "names its texture for 'texture-41-2' by no texture index")]
    [InlineData("texture-141-2", true, """{"texture-41-2": 9}""", "names texture 9, which the GLB does not carry")]
    [InlineData("texture-141-2", true, """{"texture-41-2": 0, "material/texture-41-2": 0}""", "is painted twice for 'material/texture-41-2'")]
    public void A_glb_material_s_drawn_textures_stated_in_a_way_the_game_could_not_draw_are_refused_by_name(string name, bool png, string drawn, string reason)
    {
        byte[] glb = Glb([(name, png ? Png(6, [1, 2, 3, 4, 5, 6, 7, 8]) : null, Square(0), null, null)],
            more: new Dictionary<string, Dictionary<string, object>> { [name] = new() { ["extras"] = new { drawn = JsonDocument.Parse(drawn).RootElement } } });
        Assert.Contains(reason, Assert.Throws<InvalidDataException>(() => RemodelGlb.Read(glb, "41723.glb")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_glb_flipbook_strip_carries_its_frames_rate_and_unlit_light_into_its_slot()
    {
        byte[] png = Png(6, [255, 140, 20, 255, 250, 200, 60, 128]);
        byte[] glb = Glb([("flames", png, Square(0), "MASK", null), ("still", png, Square(1), "MASK", null)], more: new Dictionary<string, Dictionary<string, object>>
        {
            ["flames"] = new() { ["extras"] = new { frames = 2, framesPerSecond = 8F }, ["extensions"] = new { KHR_materials_unlit = new { } } },
        });
        IReadOnlyList<RemodelPart> parts = RemodelGlb.Read(glb, "41116.glb");
        Assert.Equal((new RemodelStrip(2, 8F), true), (parts[0].Strip, parts[0].Unlit));
        Assert.Equal((null, false), (parts[1].Strip, parts[1].Unlit));

        (_, IReadOnlyList<(string Material, int Slot, RemodelShell? Shell, (string Identity, IReadOnlyDictionary<string, string> Drawn)? Paint)> slots, _,
            IReadOnlyDictionary<string, (byte[] Png, bool Mask, RemodelStrip? Strip, bool Unlit)> textures) = ModelMeshCommand.Write("41116", parts, "media/models");
        // The same pixels drawn as a strip and as a still texture are two slots, each published once with how it is drawn.
        Assert.Equal([(true, new RemodelStrip(2, 8F), true), (true, null, false)],
            slots.OrderBy(slot => slot.Slot).Select(slot => (textures[slot.Material].Mask, textures[slot.Material].Strip, textures[slot.Material].Unlit)));
    }

    [Theory]
    [InlineData("flames", 2, 0F, "a strip is two frames or more at a rate above zero")]
    [InlineData("flames", 1, 8F, "a strip is two frames or more at a rate above zero")]
    [InlineData("flames", 3, 8F, "a strip of 3 frames 2 pixels wide, which they do not divide")]
    [InlineData("texture-87-0", 2, 8F, "animates or unlights a classic texture")]
    public void A_glb_flipbook_the_game_cannot_step_through_is_refused(string name, int frames, float rate, string reason)
    {
        bool classic = name.StartsWith("texture-", StringComparison.Ordinal);
        byte[] glb = Glb([(name, classic ? null : Png(6, [255, 140, 20, 255, 250, 200, 60, 128]), Square(0), null, null)],
            more: new Dictionary<string, Dictionary<string, object>> { [name] = new() { ["extras"] = new { frames, framesPerSecond = rate } } });
        Assert.Contains(reason, Assert.Throws<InvalidDataException>(() => RemodelGlb.Read(glb, "41116.glb")).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A normal of no length or not finite is refused by name (#10871): it would come out of its normalizing as no number,
    /// which the Engine refuses in the static mesh, faulting the location that draws it.
    /// </summary>
    [Theory]
    [InlineData(0F)]
    [InlineData(float.NaN)]
    public void A_glb_normal_of_no_length_or_not_finite_is_refused_by_name(float z)
    {
        byte[] glb = Glb([("texture-67-0", null, Square(0), null, null)], normal: new Vector3(0, 0, z));
        Assert.Contains("41000.glb draws mesh 0 with a normal of no length or not finite, at vertex 0",
            Assert.Throws<InvalidDataException>(() => RemodelGlb.Read(glb, "41000.glb")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_glb_texture_that_is_not_8_bit_truecolour_is_refused_with_its_format()
    {
        byte[] glb = Glb([("planks", Png(3, [0, 1]), Square(0), null, null)]);
        Assert.Equal("'41214.glb's material 'planks' texture' is not eight-bit non-interlaced truecolour PNG (bit depth 8, colour type 3).",
            Assert.Throws<InvalidOperationException>(() => RemodelGlb.Read(glb, "41214.glb")).Message);
    }

    [Theory]
    [InlineData("wood", false, null, "names no classic texture")]
    [InlineData("texture-67-0", false, "MASK", "cuts out a classic texture")]
    [InlineData("glass", true, "BLEND", "a remodel is opaque, or cut out at half its alpha")]
    [InlineData("texture-33-0", false, null, "is no layer of a shell stack of 2 to 16", 3, 3)]
    [InlineData("texture-33-0", false, null, "is no layer of a shell stack of 2 to 16", 0, 1)]
    [InlineData("texture-33-0", false, "MASK", "cuts out a classic texture", 1, 3)]
    [InlineData("leaves", true, null, "a shell stack draws a classic texture's relief", 1, 3)]
    [InlineData("texture-141-2", true, null, "a shell stack draws a classic texture's relief", 1, 3)]
    public void A_glb_material_the_game_cannot_draw_is_refused(string name, bool png, string? alpha, string reason, int layer = 0, int layers = 0)
    {
        byte[] glb = Glb([(name, png ? Png(6, [1, 2, 3, 4, 5, 6, 7, 8]) : null, Square(0), alpha, layers == 0 ? null : new RemodelShell(layer, layers))]);
        Assert.Contains(reason, Assert.Throws<InvalidDataException>(() => RemodelGlb.Read(glb, "41209.glb")).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A two-pixel-wide, one-row PNG of colour type <paramref name="colourType"/> (2 RGB, 3 palette, 6 RGBA) holding
    /// <paramref name="samples"/>, written by hand since the importer's encoder writes only RGBA; its checksums are left
    /// zero, which the importer's reader does not check.
    /// </summary>
    private static byte[] Png(byte colourType, byte[] samples)
    {
        static byte[] Chunk(string kind, byte[] data)
        {
            byte[] chunk = new byte[12 + data.Length];
            BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
            Encoding.ASCII.GetBytes(kind).CopyTo(chunk, 4);
            data.CopyTo(chunk, 8);
            return chunk;
        }
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, colourType == 3 ? samples.Length : 2);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1);
        (header[8], header[9]) = (8, colourType);
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write([0, .. samples]);
        return [137, 80, 78, 71, 13, 10, 26, 10, .. Chunk("IHDR", header), .. Chunk("IDAT", compressed.ToArray()), .. Chunk("IEND", [])];
    }

    /// <summary>Two triangles of a unit square at height <paramref name="z"/>, in its own frame.</summary>
    private static Vector3[] Square(float z) => [new(0, 0, z), new(1, 0, z), new(1, 1, z), new(0, 0, z), new(1, 1, z), new(0, 1, z)];

    /// <summary>
    /// A GLB of one node, lifted 1 m, with a primitive per material; a material with a PNG carries it as its base colour,
    /// one with a shell layer states it in its extras, and one <paramref name="drawn"/> names carries its PNGs by identity
    /// in its <c>extras.drawn</c>; each takes whatever else <paramref name="more"/> states for it by name. Every corner
    /// faces <paramref name="normal"/>, up the square's z by default.
    /// </summary>
    private static byte[] Glb(IReadOnlyList<(string Name, byte[]? Png, Vector3[] Corners, string? Alpha, RemodelShell? Shell)> materials,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, byte[]>>? drawn = null, IReadOnlyDictionary<string, Dictionary<string, object>>? more = null,
        Vector3? normal = null)
    {
        Vector3 facing = normal ?? Vector3.UnitZ;
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
        foreach ((string name, byte[]? png, Vector3[] corners, string? alpha, RemodelShell? shell) in materials)
        {
            Dictionary<string, object> material = new() { ["name"] = name };
            if (alpha is not null) material["alphaMode"] = alpha;
            if (shell is not null) material["extras"] = new { shell = new { layer = shell.Layer, layers = shell.Layers } };
            foreach ((string key, object value) in more?.GetValueOrDefault(name) ?? []) material[key] = value;
            if (png is not null)
            {
                images.Add(new { bufferView = View(png), mimeType = "image/png" });
                textures.Add(new { source = images.Count - 1 });
                material["pbrMetallicRoughness"] = new { baseColorTexture = new { index = textures.Count - 1 } };
            }
            if (drawn?.GetValueOrDefault(name) is { } others)
                material["extras"] = new
                {
                    drawn = others.ToDictionary(pair => pair.Key, pair =>
                    {
                        images.Add(new { bufferView = View(pair.Value), mimeType = "image/png" });
                        textures.Add(new { source = images.Count - 1 });
                        return textures.Count - 1;
                    }),
                };
            materialList.Add(material);
            primitives.Add(new
            {
                attributes = new Dictionary<string, int>
                {
                    ["POSITION"] = Floats(corners.SelectMany(corner => new[] { corner.X, corner.Y, corner.Z }), corners.Length, "VEC3"),
                    ["NORMAL"] = Floats(corners.SelectMany(_ => new[] { facing.X, facing.Y, facing.Z }), corners.Length, "VEC3"),
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
