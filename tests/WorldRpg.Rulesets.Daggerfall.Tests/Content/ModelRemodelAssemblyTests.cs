using System.Security.Cryptography;
using System.Text;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The art pack's <c>models</c> section, and an assembled location's use of it: each model a block places is tagged with its
/// ARCH3D id, and a remodelled one's remodel draws the location's own textures (its climate, season and dungeon remaps) at
/// its classic texture slots.
/// </summary>
public sealed class ModelRemodelAssemblyTests
{
    private static readonly DaggerfallSiteId Charing = new(17, 4);
    private static readonly DaggerfallSiteId PrivateersHold = new(17, 179);

    private const string HashHex = "0000000000000001000000000000000200000000000000030000000000000004";

    private static DaggerfallArtReplacements Art(string models) =>
        DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes("""{"artReplacements": {"images": [], "models": """ + models + "}}"), "test.art");

    private static string Entry(string slots) =>
        """{"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 160, "slots": [""" + slots + "]}";

    /// <summary>
    /// The trough's remodel, drawing the classic's two textures the other way round, a texture of its own and a cut-out
    /// one, and a lever's.
    /// </summary>
    private static readonly DaggerfallArtReplacements Trough = Art(("""{"41209": """
        + Entry("""{"slot": 0, "material": "material/texture-85-0"}, {"slot": 1, "material": "material/texture-67-0"}, {"slot": 2, "texture": {"path": "remodel/41209-0.png", "sha256": "HASH"}}, """
            + """{"slot": 3, "texture": {"path": "remodel/41209-1.png", "sha256": "HASH"}, "alpha": "mask"}""")
        + """, "61027": """ + Entry("""{"slot": 0, "material": "material/texture-67-0"}""") + "}").Replace("HASH", HashHex, StringComparison.Ordinal));

    private static readonly Lazy<DaggerfallLocationAssembly> Assembly = new(() => AssemblyWith(Trough));

    private static DaggerfallLocationAssembly AssemblyWith(DaggerfallArtReplacements art)
    {
        ProductContent content = FullContent(TestData.RepositoryRoot);
        DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(TestSessions.Data(content));
        Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(TestSessions.Data(content), media, TestPayload.Definitions));
        Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(TestSessions.Data(content), media));
        return new DaggerfallLocationAssembly(TestPayload.Definitions, new DaggerfallWorldBlocks(TestSessions.Data(content)), () => product.Value, () => meshes.Value,
            art.Models);
    }

    [Fact]
    public void A_remodel_states_its_mesh_triangles_and_what_each_slot_draws()
    {
        DaggerfallArtModel model = Trough.Models["41209"];
        Assert.Equal("remodel/model.rstatmsh", model.File.Path);
        Assert.Equal(["material/texture-85-0", "material/texture-67-0", null, null], model.Slots.Select(slot => slot.Material));
        Assert.Equal("remodel/41209-0.png", model.Slots[2].Texture!.Path);
        Assert.Equal([false, false, false, true], model.Slots.Select(slot => slot.Mask));
    }

    [Fact]
    public void A_remodel_slot_may_be_a_strip_of_frames_drawn_unlit()
    {
        DaggerfallArtModel hearth = Art(("""{"41116": """ + Entry("""{"slot": 0, "material": "material/texture-87-1"}, """
            + """{"slot": 1, "texture": {"path": "remodel/41116-0.png", "sha256": "HASH"}, "alpha": "mask", "frames": 4, "framesPerSecond": 8, "unlit": true}""") + "}")
            .Replace("HASH", HashHex, StringComparison.Ordinal)).Models["41116"];
        Assert.Equal([null, new DaggerfallArtFlipbook(4, 8F)], hearth.Slots.Select(slot => slot.Flipbook));
        Assert.Equal([false, true], hearth.Slots.Select(slot => slot.Unlit));
    }

    [Theory]
    [InlineData("""{"trough": ENTRY}""", "without an ARCH3D record id")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": []}}""", "without its slots, each once")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "material/texture-67-0"}, {"slot": 0, "material": "material/texture-85-0"}]}}""", "without its slots, each once")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0}]}}""", "names neither one classic texture identity nor one texture of its own")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "wood"}]}}""", "names neither one classic texture identity nor one texture of its own")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "material/texture-67-0", "alpha": "mask"}]}}""", "only a texture of its own is cut out")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "texture": {"path": "remodel/a.png", "sha256": "HASH"}, "alpha": "blend"}]}}""", "only a texture of its own is cut out")]
    [InlineData("""{"750": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "texture": {"path": "remodel/a.png", "sha256": "HASH"}, "shell": {"layer": 0, "layers": 2}}]}}""", "as no layer of a shell stack of 2 to 16 drawing a classic texture")]
    [InlineData("""{"750": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "material/texture-33-0", "shell": {"layer": 2, "layers": 2}}]}}""", "as no layer of a shell stack of 2 to 16 drawing a classic texture")]
    [InlineData("""{"750": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "material/texture-33-0", "shell": {"layer": 0, "layers": 1}}]}}""", "as no layer of a shell stack of 2 to 16 drawing a classic texture")]
    [InlineData("""{"41116": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "material/texture-87-0", "frames": 4, "framesPerSecond": 8}]}}""", "only a texture of its own is a strip")]
    [InlineData("""{"41116": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "material/texture-87-0", "texture": {"path": "remodel/a.png", "sha256": "HASH"}, "frames": 4, "framesPerSecond": 8}]}}""", "only a texture of its own is a strip")]
    [InlineData("""{"41116": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "texture": {"path": "remodel/a.png", "sha256": "HASH"}, "frames": 4}]}}""", "of two frames or more at a rate above zero")]
    [InlineData("""{"41116": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "texture": {"path": "remodel/a.png", "sha256": "HASH"}, "frames": 1, "framesPerSecond": 8}]}}""", "of two frames or more at a rate above zero")]
    [InlineData("""{"41116": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "material/texture-87-0", "unlit": true}]}}""", "only a texture of its own is, as a classic identity draws as the location's lit surface")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "texture": {"path": "remodel/a.png", "sha256": "HASH"}, "drawn": {"material/texture-41-2": {"path": "remodel/b.png", "sha256": "HASH"}}}]}}""", "not painted for a classic identity")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "material/texture-141-2", "texture": {"path": "remodel/a.png", "sha256": "HASH"}, "drawn": {"stone": {"path": "remodel/b.png", "sha256": "HASH"}}}]}}""", "which is no classic texture identity")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "material/texture-141-2", "texture": {"path": "remodel/a.png", "sha256": "HASH"}, "shell": {"layer": 1, "layers": 3}}]}}""", "no layer of a shell stack")]
    public void A_remodel_without_a_usable_id_or_slot_is_refused_by_name(string models, string reason)
    {
        string json = models.Replace("ENTRY", Entry("""{"slot": 0, "material": "material/texture-67-0"}"""), StringComparison.Ordinal).Replace("HASH", HashHex, StringComparison.Ordinal);
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Art(json));
        Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The shipped trough is the mesh the pack states, at its digest, drawing classic textures at its slots.</summary>
    [Fact]
    public void The_shipped_trough_is_the_static_mesh_its_entry_states()
    {
        string content = Path.Combine(TestData.RepositoryRoot, "content");
        DaggerfallArtModel trough = DaggerfallArtReplacements.Read(TestContentFiles.Read(Path.Combine(content, DaggerfallArtReplacements.Folder, "payload.json")),
            "daggerfall.art-replacements").Models["41209"];
        byte[] bytes = File.ReadAllBytes(Path.Combine(content, trough.File.Path));
        Assert.Equal(trough.File.Sha256, DaggerfallContentHash.Parse(Convert.ToHexStringLower(SHA256.HashData(bytes)), trough.File.Path));
        Assert.All(trough.Slots, slot => Assert.StartsWith("material/texture-", slot.Material, StringComparison.Ordinal));
    }

    [Fact]
    public void An_assembled_town_tags_each_model_it_draws_and_binds_its_remodel_to_the_towns_own_textures()
    {
        DaggerfallSiteProfile charing = Assembly.Value.Assemble(DaggerfallWorldProfileIds.Exterior(Charing));
        Assert.All(charing.Geometry.Meshes, mesh => Assert.NotNull(mesh.ModelId));
        DaggerfallSiteMesh[] troughs = [.. charing.Geometry.Meshes.Where(mesh => mesh.ModelId == "41209")];
        Assert.Equal(3, troughs.Length);
        // Only what the town places is remodelled: it places no lever.
        DaggerfallSiteRemodel remodel = Assert.Single(charing.Geometry.Remodels).Value;
        Assert.Equal("remodel/model.rstatmsh", remodel.Path);

        // The classic draws texture 67-0 at its slot 0 and 85-0 at its slot 1, in the profile slots its climate gives them;
        // the remodel draws the same profile slots at its own slots for the same identities.
        IReadOnlyList<DaggerfallMeshMaterialBinding> classic = troughs[0].Materials!;
        Assert.Equal(classic.Single(binding => binding.MeshSlot == 1).WorldMaterialSlot, remodel.Materials.Single(binding => binding.MeshSlot == 0).WorldMaterialSlot);
        Assert.Equal(classic.Single(binding => binding.MeshSlot == 0).WorldMaterialSlot, remodel.Materials.Single(binding => binding.MeshSlot == 1).WorldMaterialSlot);
        Assert.Equal([(2U, "remodel/41209-0.png", false), (3U, "remodel/41209-1.png", true)], remodel.Textures.Select(own => (own.MeshSlot, own.Path, own.Mask)));
    }

    /// <summary>
    /// A remodel's shell layers reach the town's remodel, each bound to the profile slot the town draws its classic texture
    /// at, beside a plain slot that carries no layer.
    /// </summary>
    [Fact]
    public void An_assembled_towns_remodel_carries_its_shell_layers_on_its_classic_textures_slot()
    {
        DaggerfallArtReplacements art = Art(("""{"41209": """ + Entry("""{"slot": 0, "material": "material/texture-67-0", "shell": {"layer": 0, "layers": 2}}, """
            + """{"slot": 1, "material": "material/texture-67-0", "shell": {"layer": 1, "layers": 2}}, {"slot": 2, "material": "material/texture-85-0"}""")
            + "}").Replace("HASH", HashHex, StringComparison.Ordinal));
        DaggerfallSiteProfile charing = AssemblyWith(art).Assemble(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteRemodel trough = charing.Geometry.Remodels["41209"];
        Assert.Equal([(0U, new DaggerfallArtShell(0, 2)), (1U, new DaggerfallArtShell(1, 2))],
            trough.Shells.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)));
        // The classic draws 67-0 at its slot 0, in the profile slot the town's climate gives it; both layers stack that one.
        uint stacked = charing.Geometry.Meshes.First(mesh => mesh.ModelId == "41209").Materials!.Single(binding => binding.MeshSlot == 0).WorldMaterialSlot;
        Assert.Equal([stacked, stacked], trough.Materials.Where(binding => binding.MeshSlot < 2).OrderBy(binding => binding.MeshSlot).Select(binding => binding.WorldMaterialSlot));
    }

    /// <summary>
    /// Charing's walls end in city gates, drawn by their own part rather than as static meshes: each gate's open and closed
    /// states carry their ARCH3D ids, and the profile carries the remodels of both.
    /// </summary>
    [Fact]
    public void An_assembled_towns_city_gates_carry_their_states_ids_and_remodels()
    {
        DaggerfallArtReplacements walls = Art(("""{"446": """ + Entry("""{"slot": 0, "material": "material/texture-317-0"}""")
            + """, "447": """ + Entry("""{"slot": 0, "material": "material/texture-317-0"}""") + "}").Replace("HASH", HashHex, StringComparison.Ordinal));
        DaggerfallSiteProfile charing = AssemblyWith(walls).Assemble(DaggerfallWorldProfileIds.Exterior(Charing));
        Assert.NotEmpty(charing.CityGates);
        Assert.All(charing.CityGates, gate => Assert.Equal(("446", "447"), (gate.Open.ModelId, gate.Closed.ModelId)));
        Assert.Equal(["446", "447"], charing.Geometry.Remodels.Keys.Order(StringComparer.Ordinal));
        // The gates are not drawn as static meshes, so the remodels reach them only through their states.
        Assert.DoesNotContain(charing.Geometry.Meshes, mesh => mesh.ModelId is "446" or "447");
    }

    /// <summary>
    /// A generated rock painted for classic stone 141-2, with another texture for each stone the classic's climate swap
    /// draws it as: temperate Charing draws 141-2 as 341-2, so its trough wears that one; Privateer's Hold draws 141-2 as
    /// itself, so there it wears its own.
    /// </summary>
    [Fact]
    public void A_painted_slot_draws_the_texture_for_the_identity_the_location_draws_it_as()
    {
        string painted = """{"slot": 0, "material": "material/texture-141-2", "texture": {"path": "remodel/stone.png", "sha256": "HASH"}, "drawn": {"""
            + """ "material/texture-41-2": {"path": "remodel/stone-desert.png", "sha256": "HASH"}, "material/texture-341-2": {"path": "remodel/stone-temperate.png", "sha256": "HASH"}}}""";
        DaggerfallArtReplacements art = Art(("{\"41209\": " + Entry(painted) + ", \"61027\": " + Entry(painted) + "}").Replace("HASH", HashHex, StringComparison.Ordinal));
        DaggerfallArtModelSlot slot = art.Models["41209"].Slots[0];
        Assert.Equal("material/texture-141-2", slot.Material);
        Assert.Equal("remodel/stone-desert.png", slot.Painted("material/texture-41-2").Path);
        Assert.Equal("remodel/stone.png", slot.Painted("material/texture-19-2").Path);

        DaggerfallLocationAssembly assembly = AssemblyWith(art);
        DaggerfallSiteRemodel trough = assembly.Assemble(DaggerfallWorldProfileIds.Exterior(Charing)).Geometry.Remodels["41209"];
        Assert.Empty(trough.Materials);
        Assert.Equal([(0U, "remodel/stone-temperate.png")], trough.Textures.Select(own => (own.MeshSlot, own.Path)));
        DaggerfallSiteRemodel lever = assembly.Assemble(DaggerfallWorldProfileIds.Dungeon(PrivateersHold)).Geometry.Remodels["61027"];
        Assert.Equal([(0U, "remodel/stone.png")], lever.Textures.Select(own => (own.MeshSlot, own.Path)));
    }

    [Fact]
    public void An_assembled_dungeon_s_moving_model_carries_its_id_and_its_remodel()
    {
        DaggerfallSiteProfile hold = Assembly.Value.Assemble(DaggerfallWorldProfileIds.Dungeon(PrivateersHold));
        DaggerfallDungeonActionModelDefinition[] levers = [.. hold.DungeonActionModels.Where(model => model.ModelId == "61027")];
        Assert.NotEmpty(levers);
        Assert.All(hold.DungeonActionModels, model => Assert.NotNull(model.ModelId));
        Assert.Contains("61027", hold.Geometry.Remodels.Keys);
    }

    /// <summary>
    /// Privateer's Hold, where a new game starts, places none of the shipped remodels. Its static models are tagged as any
    /// location's are, so a remodel of the one its blocks place most is drawn there, bound to the dungeon's own textures.
    /// </summary>
    [Fact]
    public void Privateers_hold_draws_the_remodel_of_a_static_model_its_blocks_place()
    {
        DaggerfallSiteProfile classic = Assembly.Value.Assemble(DaggerfallWorldProfileIds.Dungeon(PrivateersHold));
        Assert.All(classic.Geometry.Meshes, mesh => Assert.NotNull(mesh.ModelId));
        string model = classic.Geometry.Meshes.GroupBy(mesh => mesh.ModelId!).MaxBy(group => group.Count())!.Key;
        DaggerfallArtReplacements art = Art(("{\"" + model + "\": " + Entry("""{"slot": 0, "material": "material/texture-67-0"}""") + "}")
            .Replace("HASH", HashHex, StringComparison.Ordinal));

        DaggerfallSiteProfile hold = AssemblyWith(art).Assemble(DaggerfallWorldProfileIds.Dungeon(PrivateersHold));
        DaggerfallSiteRemodel remodel = Assert.Single(hold.Geometry.Remodels).Value;
        Assert.Equal("remodel/model.rstatmsh", remodel.Path);
        DaggerfallMeshMaterialBinding bound = Assert.Single(remodel.Materials);
        Assert.Equal(0U, bound.MeshSlot);
        Assert.Contains(hold.Materials, material => material.Slot == bound.WorldMaterialSlot);
    }

    /// <summary>
    /// The shipped art pack's remodels reach the place a new game starts and the first town: Charing draws its troughs
    /// remodelled, and Privateer's Hold the remodelled containers its blocks place, standing or moving.
    /// </summary>
    [Fact]
    public void The_shipped_remodels_draw_in_charing_and_privateers_hold()
    {
        DaggerfallArtReplacements shipped = DaggerfallArtReplacements.Read(TestContentFiles.Read(Path.Combine(TestData.RepositoryRoot, "content",
            DaggerfallArtReplacements.Folder, "payload.json")), "daggerfall.art-replacements");
        DaggerfallLocationAssembly assembly = AssemblyWith(shipped);
        DaggerfallSiteProfile charing = assembly.Assemble(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile hold = assembly.Assemble(DaggerfallWorldProfileIds.Dungeon(PrivateersHold));
        Assert.Contains("41209", charing.Geometry.Remodels.Keys);
        Assert.NotEmpty(hold.Geometry.Remodels);
        Assert.All(hold.Geometry.Remodels.Keys, model => Assert.True(hold.Geometry.Meshes.Any(mesh => mesh.ModelId == model)
            || hold.DungeonActionModels.Any(moving => moving.ModelId == model)));
    }
}
