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
/// A building's walls draw their record's relief near the camera (#10859): outdoors, while refreshed art is drawn, a
/// location material whose record's relief has a depth draws its refreshed surface with the set's surface shader, the
/// relief as its product map and the depth scaled by the player's strength, which a video setting changes live. Each
/// season draws its own record's relief. Classic art, a strength of 0, a relief with no depth (a remodel's shells alone)
/// and every interior draw the plain surface.
/// </summary>
public sealed class SurfaceReliefPresentationTests
{
    private const string HashHex = "0000000000000001000000000000000200000000000000030000000000000004";

    private static DaggerfallSiteProfile Profile(DaggerfallWorldProfileKind kind)
    {
        DaggerfallSiteGeometry geometry = new(1, [new DaggerfallSiteSpatialPart("block", "spatial/block.rspatial", Hash)],
            [new DaggerfallSiteMesh("mesh/house.rstatmsh", Hash, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), [new(0, 0), new(1, 1)])]);
        // House 125's plaster, snowed in winter; and the low wall's stones, whose relief only a remodel's shells draw.
        NormalizedMaterial plaster = new(0, "texture/texture-312-1.png", Hash, "material/texture-12-1") { WinterTexture = new("texture/texture-313-1.png", Hash) };
        NormalizedMaterial stones = new(1, "texture/texture-329-0.png", Hash, "material/texture-29-0");
        return new DaggerfallSiteProfile(new ProjectFacts(null, new Dictionary<long, AuthoredActor>()), geometry,
            new AuthoredWorldAppearance(new Color(1, 1, 1, 1), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene),
            new PlayerInitialLook(0, 0), [plaster, stones], new Dictionary<long, NormalizedActorSprite>(), profileKind: kind);
    }

    private static string Relief(string record, string depth) => $$"""
        "{{record}}": {"path": "relief/relief-{{record}}.png", "sha256": "HASH", "width": 128, "height": 256, "tallest": 1, "kind": "plaster", "sway": 0,
          "shade": [0.55, 0.55, 0.55], "glints": 0, "sinksUnderSnow": false{{depth}}}
        """;

    private static readonly string ArtJson = ("""
        {"artReplacements": {"images": [],
          "surfaces": [
            {"replaces": {"archive": 312, "record": 1}, "path": "surfaces/texture-312-1.png", "sha256": "HASH", "width": 128, "height": 256},
            {"replaces": {"archive": 313, "record": 1}, "path": "surfaces/texture-313-1.png", "sha256": "HASH", "width": 128, "height": 256},
            {"replaces": {"archive": 329, "record": 0}, "path": "surfaces/texture-329-0.png", "sha256": "HASH", "width": 128, "height": 64}],
          "groundCover": {"shell": {"path": "cover/shell.png", "sha256": "HASH", "width": 96, "height": 96, "texelsPerMetre": 24},
            "tuft": {"path": "cover/tuft.png", "sha256": "HASH"}, "shader": {"path": "cover/shells.wgsl", "sha256": "HASH"}, "grass": {},
            "surfaceShader": {"path": "relief/relief.wgsl", "sha256": "HASH"},
            "relief": {
        """ + Relief("312-1", ", \"depth\": 0.02") + "," + Relief("313-1", ", \"depth\": 0.03") + "," + Relief("329-0", "") + """
        }}}}
        """).Replace("HASH", HashHex, StringComparison.Ordinal);

    private static (DaggerfallSiteAppearance Presentation, AppearanceFake Graphics) Compose(DaggerfallWorldProfileKind kind, Func<bool> refreshed, Func<float> strength)
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        foreach (string path in new[] { "mesh/house.rstatmsh", "texture/texture-312-1.png", "texture/texture-313-1.png", "texture/texture-329-0.png",
                     "surfaces/texture-312-1.png", "surfaces/texture-313-1.png", "surfaces/texture-329-0.png", "relief/relief.wgsl",
                     "relief/relief-312-1.png", "relief/relief-313-1.png", "relief/relief-329-0.png" })
            content.Add(path, Hash);
        AppearanceFake graphics = new(releases);
        DaggerfallSiteAppearance presentation = new(content, graphics, Profile(kind));
        presentation.UseSurfaceRelief(DaggerfallSurfaceReliefTuning.Default, strength);
        presentation.UseEffectArt(PresentationRecorder.Create().Service, AnimationRecorder.Create().Service,
            DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes(ArtJson), "test.art"), refreshed);
        return (presentation, graphics);
    }

    private static Material[] Surfaces(AppearanceFake graphics) =>
        [.. graphics.StaticMeshMaterials[graphics.StaticMeshByPath["mesh/house.rstatmsh"]].OrderBy(binding => binding.MaterialSlot).Select(binding => binding.Material)];

    private static MaterialRequest Now(AppearanceFake graphics, Material material) =>
        graphics.MaterialUpdates.Where(update => update.Material == material).Select(update => (MaterialRequest?)update.Replacement).LastOrDefault()
            ?? graphics.MaterialRequests[material];

    private static string? Opened(AppearanceFake graphics, RenderResourceReference resource) =>
        graphics.OpenedResourcePaths[Enumerable.Range(0, graphics.OpenedResourcePaths.Count).Single(at => resource == new RenderResourceReference((ulong)at + 1))];

    [Fact]
    public void An_outdoor_wall_draws_its_seasons_relief_with_the_surface_shader_at_the_players_strength_and_the_tuned_fade()
    {
        float strength = .6F;
        var (presentation, graphics) = Compose(DaggerfallWorldProfileKind.Exterior, () => true, () => strength);
        using DaggerfallSiteAppearance owned = presentation;
        using var actors = EmptyActors();
        Material plaster = Surfaces(graphics)[0];

        MaterialRequest drawn = Now(graphics, plaster);
        Assert.Equal("surfaces/texture-312-1.png", Opened(graphics, drawn.Texture));
        Assert.Equal("relief/relief.wgsl", Opened(graphics, drawn.Shader.Shader));
        Assert.Equal("relief/relief-312-1.png", Opened(graphics, drawn.Shader.TextureA));
        DaggerfallSurfaceReliefTuning tuning = DaggerfallSurfaceReliefTuning.Default;
        Assert.Equal(new Vector4(.02F * .6F, tuning.Steps, tuning.FadeStart, tuning.FadeEnd), drawn.Shader.Parameter0);
        Assert.Equal(new Vector4(tuning.Cavity, tuning.Slope, 0, 0), drawn.Shader.Parameter1);

        // Winter draws its own record's relief, as deep as that record's states.
        presentation.SetExteriorSeason(DaggerfallExteriorSeason.Winter);
        drawn = Now(graphics, plaster);
        Assert.Equal("relief/relief-313-1.png", Opened(graphics, drawn.Shader.TextureA));
        Assert.Equal(.03F * .6F, drawn.Shader.Parameter0.X, 6);

        // A new strength in the video settings shows at the next update, deeper; 0 draws the plain surface.
        strength = 2F;
        presentation.Publish(actors);
        Assert.Equal(.03F * 2F, Now(graphics, plaster).Shader.Parameter0.X, 6);
        strength = 0F;
        presentation.Publish(actors);
        Assert.Equal(default, Now(graphics, plaster).Shader);
        Assert.Equal("surfaces/texture-313-1.png", Opened(graphics, Now(graphics, plaster).Texture));
    }

    [Fact]
    public void Classic_art_a_relief_without_depth_and_interiors_draw_the_plain_surface()
    {
        bool refreshed = true;
        var (presentation, graphics) = Compose(DaggerfallWorldProfileKind.Exterior, () => refreshed, () => 1F);
        using DaggerfallSiteAppearance owned = presentation;
        using var actors = EmptyActors();
        Material[] surfaces = Surfaces(graphics);

        // The low wall's stones have relief for a remodel's shells, but no depth under a face.
        Assert.Equal(default, Now(graphics, surfaces[1]).Shader);
        Assert.Equal("surfaces/texture-329-0.png", Opened(graphics, Now(graphics, surfaces[1]).Texture));

        refreshed = false;
        presentation.Publish(actors);
        Assert.All(surfaces, surface => Assert.Equal(default, Now(graphics, surface).Shader));
        Assert.Equal("texture/texture-312-1.png", Opened(graphics, Now(graphics, surfaces[0]).Texture));
        refreshed = true;
        presentation.Publish(actors);
        Assert.Equal("relief/relief-312-1.png", Opened(graphics, Now(graphics, surfaces[0]).Shader.TextureA));

        // Interiors draw the same records flat, and never open the shader or a relief.
        var (inside, indoors) = Compose(DaggerfallWorldProfileKind.Interior, () => true, () => 1F);
        using DaggerfallSiteAppearance ownedInside = inside;
        Assert.All(Surfaces(indoors), surface => Assert.Equal(default, Now(indoors, surface).Shader));
        Assert.Equal("surfaces/texture-312-1.png", Opened(indoors, Now(indoors, Surfaces(indoors)[0]).Texture));
        Assert.DoesNotContain(indoors.OpenedResourcePaths, path => path is not null && path.StartsWith("relief/", StringComparison.Ordinal));
    }

    [Fact]
    public void A_set_giving_relief_a_depth_but_no_surface_shader_is_refused()
    {
        string withoutShader = ArtJson.Replace("\"surfaceShader\": {\"path\": \"relief/relief.wgsl\", \"sha256\": \"" + HashHex + "\"},", "", StringComparison.Ordinal);
        Assert.NotEqual(ArtJson, withoutShader);
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes(withoutShader), "test.art"));
        Assert.Contains("surfaceShader", refused.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => DaggerfallArtReplacements.Read(
            Encoding.UTF8.GetBytes(ArtJson.Replace("\"depth\": 0.02", "\"depth\": 0", StringComparison.Ordinal)), "test.art"));
    }
}
