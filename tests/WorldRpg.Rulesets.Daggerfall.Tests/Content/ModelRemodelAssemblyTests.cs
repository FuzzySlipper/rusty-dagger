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
/// its classic texture slots. A site closure's pre-combined mesh names no model, so what it would remodel is reported.
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

    private static readonly Lazy<DaggerfallLocationAssembly> Assembly = new(() =>
    {
        ProductContent content = FullContent(TestData.RepositoryRoot);
        DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(TestSessions.Data(content));
        Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(TestSessions.Data(content), media, TestPayload.Definitions));
        Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(TestSessions.Data(content), media));
        return new DaggerfallLocationAssembly(TestPayload.Definitions, new DaggerfallWorldBlocks(TestSessions.Data(content)), () => product.Value, () => meshes.Value,
            Trough.Models);
    });

    [Fact]
    public void A_remodel_states_its_mesh_triangles_and_what_each_slot_draws()
    {
        DaggerfallArtModel model = Trough.Models["41209"];
        Assert.Equal("remodel/model.rstatmsh", model.File.Path);
        Assert.Equal(["material/texture-85-0", "material/texture-67-0", null, null], model.Slots.Select(slot => slot.Material));
        Assert.Equal("remodel/41209-0.png", model.Slots[2].Texture!.Path);
        Assert.Equal([false, false, false, true], model.Slots.Select(slot => slot.Mask));
    }

    [Theory]
    [InlineData("""{"trough": ENTRY}""", "without an ARCH3D record id")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": []}}""", "without its slots, each once")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "material/texture-67-0"}, {"slot": 0, "material": "material/texture-85-0"}]}}""", "without its slots, each once")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0}]}}""", "names neither one classic texture identity nor one texture of its own")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "wood"}]}}""", "names neither one classic texture identity nor one texture of its own")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "material": "material/texture-67-0", "alpha": "mask"}]}}""", "only a texture of its own is cut out")]
    [InlineData("""{"41209": {"path": "remodel/model.rstatmsh", "sha256": "HASH", "triangles": 1, "slots": [{"slot": 0, "texture": {"path": "remodel/a.png", "sha256": "HASH"}, "alpha": "blend"}]}}""", "only a texture of its own is cut out")]
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
    /// Charing's exterior is published as a site closure, whose combined mesh names no model: the remodelled models its
    /// own blocks place are what that closure draws classic.
    /// </summary>
    [Fact]
    public void What_a_closure_would_remodel_is_the_remodelled_models_its_location_s_blocks_place()
    {
        Assert.Equal(new Dictionary<string, int> { ["41209"] = 3 }, Assembly.Value.Remodelled(DaggerfallWorldProfileIds.Exterior(Charing)));
        IReadOnlyDictionary<string, int> hold = Assembly.Value.Remodelled(DaggerfallWorldProfileIds.Dungeon(PrivateersHold));
        Assert.Equal(["61027"], hold.Keys);
        Assert.Equal(Assembly.Value.Assemble(DaggerfallWorldProfileIds.Dungeon(PrivateersHold)).DungeonActionModels.Count(model => model.ModelId == "61027"), hold["61027"]);
    }
}
