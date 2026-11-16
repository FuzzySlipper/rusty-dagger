using System.Numerics;
using System.Text;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A remodel that draws its classic texture as a shell stack, as the hedges and low walls do: each layer of a location
/// material is one material, shared by every remodel stacking it, that draws the relief of the texture the location draws
/// in the season with the ground cover's shader, the core whole and each layer above it cut at its height; in classic art,
/// or for a texture the set has no relief for, each layer draws the surface whole.
/// </summary>
public sealed class ModelShellPresentationTests
{
    private const string HashHex = "0000000000000001000000000000000200000000000000030000000000000004";

    private static readonly Dictionary<uint, DaggerfallArtShell> Stack = new() { [0] = new(0, 3), [1] = new(1, 3), [2] = new(2, 3) };

    private static DaggerfallSiteProfile Profile()
    {
        Transform At(float x) => new(new Vector3(x, 0, 0), Quaternion.Identity, Vector3.One);
        DaggerfallSiteGeometry geometry = new(1, [new DaggerfallSiteSpatialPart("block", "spatial/block.rspatial", Hash)],
        [
            new DaggerfallSiteMesh("mesh/518.rstatmsh", Hash, At(0), [new(0, 0)]) { ModelId = "518" },
            new DaggerfallSiteMesh("mesh/520.rstatmsh", Hash, At(5), [new(0, 0)]) { ModelId = "520" },
        ])
        {
            Remodels = new Dictionary<string, DaggerfallSiteRemodel>(StringComparer.Ordinal)
            {
                ["518"] = new("remodel/518.rstatmsh", Hash, [new(0, 0), new(1, 0), new(2, 0)], []) { Shells = Stack },
                ["520"] = new("remodel/520.rstatmsh", Hash, [new(0, 0), new(1, 0), new(2, 0)], []) { Shells = Stack },
            },
        };
        // The temperate wall: its summer stones, and in winter its snowy ones, which the set has no relief for.
        NormalizedMaterial wall = new(0, "texture/texture-329-0.png", Hash, "material/texture-29-0")
        {
            WinterTexture = new("texture/texture-330-0.png", Hash),
        };
        return new DaggerfallSiteProfile(new ProjectFacts(null, new Dictionary<long, AuthoredActor>()), geometry,
            new AuthoredWorldAppearance(new Color(1, 1, 1, 1), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene),
            new PlayerInitialLook(0, 0), [wall], new Dictionary<long, NormalizedActorSprite>(), profileKind: DaggerfallWorldProfileKind.Exterior);
    }

    private static readonly string ArtJson = """
        {"artReplacements": {"images": [],
          "groundCover": {"shell": {"path": "cover/shell.png", "sha256": "HASH", "width": 96, "height": 96, "texelsPerMetre": 24},
            "tuft": {"path": "cover/tuft.png", "sha256": "HASH"}, "shader": {"path": "cover/shells.wgsl", "sha256": "HASH"}, "grass": {},
            "relief": {"329-0": {"path": "cover/relief-329-0.png", "sha256": "HASH", "width": 128, "height": 64, "tallest": 1, "kind": "stones", "sway": 0,
              "shade": [0.5, 0.5, 0.5], "glints": 0.25, "sinksUnderSnow": false}}},
          "models": {"518": {"path": "remodel/518.rstatmsh", "sha256": "HASH", "triangles": 9, "slots": [
            {"slot": 0, "material": "material/texture-29-0", "shell": {"layer": 0, "layers": 3}},
            {"slot": 1, "material": "material/texture-29-0", "shell": {"layer": 1, "layers": 3}},
            {"slot": 2, "material": "material/texture-29-0", "shell": {"layer": 2, "layers": 3}}]}}}}
        """.Replace("HASH", HashHex, StringComparison.Ordinal);

    private static (DaggerfallSiteAppearance Presentation, AppearanceFake Graphics) Compose(Func<bool> refreshed)
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        foreach (string path in new[] { "mesh/518.rstatmsh", "mesh/520.rstatmsh", "remodel/518.rstatmsh", "remodel/520.rstatmsh", "texture/texture-329-0.png",
                     "texture/texture-330-0.png", "cover/shells.wgsl", "cover/relief-329-0.png" })
            content.Add(path, Hash);
        AppearanceFake graphics = new(releases);
        DaggerfallSiteAppearance presentation = new(content, graphics, Profile());
        presentation.UseEffectArt(PresentationRecorder.Create().Service, AnimationRecorder.Create().Service,
            DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes(ArtJson), "test.art"), refreshed);
        return (presentation, graphics);
    }

    /// <summary>What a material draws now: its last update, or the request it was made with.</summary>
    private static MaterialRequest Now(AppearanceFake graphics, Material material) =>
        graphics.MaterialUpdates.Where(update => update.Material == material).Select(update => (MaterialRequest?)update.Replacement).LastOrDefault()
            ?? graphics.MaterialRequests[material];

    /// <summary>The content path of the resource a reference names: the fake numbers what it opens from 1, in order.</summary>
    private static string? Opened(AppearanceFake graphics, RenderResourceReference resource) =>
        graphics.OpenedResourcePaths[Enumerable.Range(0, graphics.OpenedResourcePaths.Count).Single(at => resource == new RenderResourceReference((ulong)at + 1))];

    private static string? Texture(AppearanceFake graphics, MaterialRequest request) => Opened(graphics, request.Texture);

    [Fact]
    public void Every_remodel_stacking_a_surface_shares_its_layers_which_draw_its_relief_cut_at_their_height_with_the_ground_covers_shader()
    {
        var (presentation, graphics) = Compose(() => true);
        using DaggerfallSiteAppearance owned = presentation;
        MeshMaterialBinding[] wall = graphics.StaticMeshMaterials[graphics.StaticMeshByPath["remodel/518.rstatmsh"]];
        MeshMaterialBinding[] other = graphics.StaticMeshMaterials[graphics.StaticMeshByPath["remodel/520.rstatmsh"]];
        Material[] layers = [.. wall.OrderBy(binding => binding.MaterialSlot).Select(binding => binding.Material)];
        Assert.Equal(layers, other.OrderBy(binding => binding.MaterialSlot).Select(binding => binding.Material));
        // Each layer is a material of its own, none the location's surface the classic draws.
        Material surface = Assert.Single(graphics.StaticMeshMaterials[graphics.StaticMeshByPath["mesh/518.rstatmsh"]]).Material;
        Assert.Equal(3, layers.Distinct().Count());
        Assert.DoesNotContain(surface, layers);

        MaterialRequest[] drawn = [.. layers.Select(layer => Now(graphics, layer))];
        Assert.All(drawn, request => Assert.Equal("cover/relief-329-0.png", Texture(graphics, request)));
        Assert.All(drawn, request => Assert.Equal("cover/shells.wgsl", Opened(graphics, request.Shader.Shader)));
        // The core draws every texel; a layer above it the texels at least its share of the stack tall, as the ground's do.
        Assert.Equal([MaterialAlphaMode.Opaque, MaterialAlphaMode.Mask, MaterialAlphaMode.Mask], drawn.Select(request => request.AlphaMode));
        Assert.Equal([0F, 1F / 3, 2F / 3], drawn.Select(request => request.Shader.Parameter1.X));
        Assert.Equal([0F, .5F, 1F], drawn.Select(request => request.Shader.Parameter0.W));
        // Shaded from the relief's root colour at the core to full at the top, the top half glinting, standing still.
        Assert.Equal([.5F, .75F, 1F], drawn.Select(request => request.Color.R));
        Assert.Equal([0F, .25F, .25F], drawn.Select(request => request.Shader.Parameter1.Z));
        Assert.All(drawn, request => Assert.Equal(0F, request.Shader.Parameter0.Y));
    }

    [Fact]
    public void A_stack_draws_its_surface_whole_in_classic_art_and_in_a_season_whose_texture_the_set_has_no_relief_for()
    {
        bool refreshed = true;
        var (presentation, graphics) = Compose(() => refreshed);
        using DaggerfallSiteAppearance owned = presentation;
        using var actors = EmptyActors();
        Material[] layers = [.. graphics.StaticMeshMaterials[graphics.StaticMeshByPath["remodel/518.rstatmsh"]].Select(binding => binding.Material)];

        // Winter's snowy stones have no relief: every layer draws them whole, so the stack closes into the classic's faces.
        presentation.SetExteriorSeason(DaggerfallExteriorSeason.Winter);
        Assert.All(layers, layer => Assert.Equal("texture/texture-330-0.png", Texture(graphics, Now(graphics, layer))));
        Assert.All(layers, layer => Assert.Equal(MaterialAlphaMode.Opaque, Now(graphics, layer).AlphaMode));
        Assert.All(layers, layer => Assert.Equal(default, Now(graphics, layer).Shader));

        presentation.SetExteriorSeason(DaggerfallExteriorSeason.Summer);
        Assert.All(layers, layer => Assert.Equal("cover/relief-329-0.png", Texture(graphics, Now(graphics, layer))));

        // Classic art draws the classic model, and the stack the classic surface whole.
        refreshed = false;
        presentation.Publish(actors);
        Assert.All(layers, layer => Assert.Equal("texture/texture-329-0.png", Texture(graphics, Now(graphics, layer))));
        Assert.All(layers, layer => Assert.Equal(default, Now(graphics, layer).Shader));
    }

    [Fact]
    public void A_set_that_stacks_shells_without_a_ground_cover_is_refused()
    {
        string json = """
            {"artReplacements": {"images": [], "models": {"518": {"path": "remodel/518.rstatmsh", "sha256": "HASH", "triangles": 9, "slots": [
              {"slot": 0, "material": "material/texture-29-0", "shell": {"layer": 0, "layers": 2}}, {"slot": 1, "material": "material/texture-29-0", "shell": {"layer": 1, "layers": 2}}]}}}}
            """.Replace("HASH", HashHex, StringComparison.Ordinal);
        Assert.Equal("Content pack 'test.art' draws the remodel of ARCH3D model 518 in shells but has no ground cover to draw them with.",
            Assert.Throws<InvalidOperationException>(() => DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes(json), "test.art")).Message);
    }
}
