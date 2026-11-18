using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The location's surfaces: each material draws its classic texture record in the current season (summer, winter or
/// rain, as the climate swap published them), or while refreshed art is drawn, that record's refreshed surface where the
/// set has one. A season or art change updates only the materials whose texture changes. A window glows at night through
/// the glass mask of the texture it draws: a refreshed window's own, which the art set must state, and the mask the import
/// cut from a classic window only while that is drawn, so nightfall changes only the emission's level. A classic texture of several frames, as a hearth's
/// fire, draws each in turn, stepped on the remodel strips' clock.
/// <para>
/// Outdoors, a refreshed surface whose record's relief has a depth (<see cref="DaggerfallGroundRelief.Depth"/>), a
/// building's wall, door or roof, draws it with the set's surface shader near the camera, as deep as the player's relief
/// strength asks, fading to the flat face with distance (<see cref="DaggerfallSurfaceReliefTuning"/>). Classic art, a
/// strength of 0 and a record without such relief draw the plain surface. Interiors and dungeons draw the same records
/// flat: their relief waits on Patch's decision (#10859).
/// </para>
/// <para>
/// Outdoors, lit window glass flickers as the fires behind it do, each window on its own phase, in the same shader with no
/// relief where its surface draws none, in classic art too (#10906).
/// </para>
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private readonly List<SurfaceMaterial> surfaceMaterials = [];
    private DaggerfallArtReplacements? surfaceArt;
    private bool surfacesRefreshed;
    private Vector3 windowColor;
    private float windowLevel;
    // How far lit glass flickers about its level, as a share of it, and how fast, in cycles a second.
    private Vector2 windowFlicker;
    // The set's surface shader, once an art set with one is admitted outdoors, the tuning its relief draws by, and the
    // player's strength, read live, with the strength the surfaces draw now.
    private RenderResource? surfaceShader;
    private DaggerfallSurfaceReliefTuning reliefTuning = DaggerfallSurfaceReliefTuning.Default;
    private Func<float> reliefStrength = () => 0F;
    private float surfacesStrength;

    /// <summary>
    /// One location material: its classic texture path per season (summer, winter, rain; a season without a variant repeats
    /// summer) and its classic resource per season, opened the first time classic art draws it; the refreshed resource per
    /// season once the art set is admitted (none where it has none); the resource it draws now, on a window the classic glass
    /// mask per season, opened the first time classic art draws it, the refreshed one per season, and the one it holds now,
    /// and outdoors the relief image and depth per season where that season's record
    /// has building relief, and the shader it draws now. A classic texture of several frames holds each frame's resource,
    /// opened the first time classic art draws it, which it draws in every season, and the frame it shows.
    /// </summary>
    private sealed class SurfaceMaterial(NormalizedMaterial source)
    {
        internal NormalizedMaterial Source { get; } = source;
        internal RenderResource?[]? Frames { get; } = source.Frames is { } frames ? new RenderResource?[frames.Textures.Count] : null;
        internal int Frame { get; set; }
        internal Material Material { get; set; } = null!;
        internal string[] Paths { get; } = [source.TexturePath, source.WinterTexture?.Path ?? source.TexturePath, source.RainTexture?.Path ?? source.TexturePath];
        internal RenderResource?[] Classic { get; } = new RenderResource?[3];
        internal RenderResource?[]? Refreshed { get; set; }
        internal RenderResource Current { get; set; } = null!;
        internal ContentArtifact?[] ClassicGlassFiles { get; } = [source.SummerGlass, source.WinterGlass, source.RainGlass];
        internal RenderResource?[] ClassicGlass { get; } = new RenderResource?[3];
        internal RenderResource?[]? RefreshedGlass { get; set; }
        internal RenderResource? CurrentGlass { get; set; }
        internal float Level { get; set; }
        internal (RenderResource Image, float Depth)?[] Relief { get; } = new (RenderResource, float)?[3];
        internal MaterialShader Shader { get; set; }
    }

    /// <summary>
    /// A season's classic texture, opened the first time it is drawn: only classic art, or a record the art set has no
    /// surface for, draws it. A season without a variant draws summer's.
    /// </summary>
    private RenderResource ClassicSurface(SurfaceMaterial surface, int season)
    {
        ContentArtifact? variant = season switch { 1 => surface.Source.WinterTexture, 2 => surface.Source.RainTexture, _ => null };
        if (variant is null || variant.Path == surface.Source.TexturePath)
            return surface.Classic[0] ??= OpenSurface(new ContentArtifact(surface.Source.TexturePath, surface.Source.TextureSha256));
        return surface.Classic[season] ??= OpenSurface(variant);
    }

    /// <summary>One frame of a classic texture of several, opened the first time it is drawn; the first is the material's own texture.</summary>
    private RenderResource ClassicFrame(SurfaceMaterial surface, int frame) => frame == 0 ? ClassicSurface(surface, 0)
        : surface.Frames![frame] ??= OpenSurface(surface.Source.Frames!.Textures[frame]);

    private RenderResource OpenSurface(ContentArtifact file)
    {
        RenderResource resource = appearance.OpenTexture(content, file.Path, file.Sha256, TextureFilter.Nearest, TextureWrap.Repeat, admittingOffUpdate).Handle;
        locationResources.Add(resource);
        return resource;
    }

    /// <summary>
    /// A season's classic glass mask, opened the first time classic art draws it (a season without its own repeats summer's,
    /// as the import publishes it); null where the classic texture is no window.
    /// </summary>
    private RenderResource? ClassicGlass(SurfaceMaterial surface, int season)
    {
        if (surface.ClassicGlassFiles[season] is not { } mask) return null;
        if (surface.ClassicGlass[season] is { } opened) return opened;
        int same = Array.FindIndex(surface.ClassicGlassFiles, file => file?.Path == mask.Path);
        return surface.ClassicGlass[season] = surface.ClassicGlass[same] ??= OpenSurface(mask);
    }

    /// <summary>The glass a surface glows through in a season: the refreshed surface's own where it draws one, else the classic texture's.</summary>
    private RenderResource? DrawnGlass(SurfaceMaterial surface, int season, bool refreshed) =>
        refreshed && surface.Refreshed?[season] is not null ? surface.RefreshedGlass![season] : ClassicGlass(surface, season);

    /// <summary>The texture a surface draws in a season: its refreshed one in refreshed art where the set has it, else its classic one.</summary>
    private RenderResource DrawnSurface(SurfaceMaterial surface, int season, bool refreshed) =>
        refreshed && surface.Refreshed is { } own ? own[season] ?? ClassicSurface(surface, season)
            : surface.Frames is not null ? ClassicFrame(surface, surface.Frame) : ClassicSurface(surface, season);

    /// <summary>
    /// Draws building relief by <paramref name="tuning"/> at the player's <paramref name="strength"/>, read each update, so a
    /// change in the video settings shows at once.
    /// </summary>
    internal void UseSurfaceRelief(DaggerfallSurfaceReliefTuning tuning, Func<float> strength)
    {
        reliefTuning = tuning.Validate();
        reliefStrength = strength;
        ApplySurfaces();
    }

    /// <summary>
    /// Lights the location's window glass in a colour at a level, which is zero while the windows are dark, flickering by a
    /// share at a rate (<paramref name="flicker"/>).
    /// </summary>
    internal void UpdateWindowGlass(Vector3 color, float level, Vector2 flicker)
    {
        if (level == windowLevel && color == windowColor && flicker == windowFlicker) return;
        (windowColor, windowLevel, windowFlicker) = (color, level, flicker);
        ApplySurfaces();
    }

    /// <summary>A surface's material: its texture, its glass lit at the current level, and its relief's shader, if it draws one.</summary>
    private MaterialRequest SurfaceRequest(RenderResource texture, RenderResource? glass, MaterialShader shader = default)
    {
        MaterialRequest request = new(new Color(1F, 1F, 1F, 1F), texture, 1F, new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false) { Shader = shader };
        return glass is null ? request : request with { EmissionColor = windowColor, EmissionIntensity = windowLevel, EmissionMap = glass };
    }

    /// <summary>Admits the refreshed surfaces for the materials already drawn and every location admitted later.</summary>
    private void UseSurfaceArt(DaggerfallArtReplacements art)
    {
        surfaceArt = art;
        if (inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior && art.GroundCover is { SurfaceShader: { } shader } && surfaceShader is null)
        {
            surfaceShader = appearance.OpenShader(content, shader.Path, shader.Sha256);
            ownedResources.Add(surfaceShader);
        }
        foreach (SurfaceMaterial surface in surfaceMaterials) AttachRefreshedSurface(surface);
        UseShellArt(art);
        foreach (DecorationVisual flat in decorations) AttachDecorationFlats(flat);
        foreach (ActorVisual visual in actors.Values) AttachRefreshedPerson(visual);
        ApplySurfaces();
    }

    private void AttachRefreshedSurface(SurfaceMaterial surface)
    {
        if (surfaceArt is null || surface.Paths.All(path => surfaceArt.Surface(path) is null)) return;
        Dictionary<string, RenderResource> opened = new(StringComparer.Ordinal);
        RenderResource Open(DaggerfallArtFile file)
        {
            if (!opened.TryGetValue(file.Path, out RenderResource? resource))
            {
                resource = appearance.OpenTexture(content, file.Path, file.Sha256, TextureFilter.Nearest, TextureWrap.Repeat, admittingOffUpdate).Handle;
                locationResources.Add(resource);
                opened.Add(file.Path, resource);
            }
            return resource;
        }
        surface.Refreshed = [.. surface.Paths.Select(path => surfaceArt.Surface(path) is { } refreshed ? Open(refreshed.File) : null)];
        // A refreshed window glows through its own glass; one over a classic window that states none is refused, never lit by the classic's.
        surface.RefreshedGlass = [.. surface.Paths.Select<string, RenderResource?>((path, season) =>
            surfaceArt.Surface(path) is not { } refreshed || surface.ClassicGlassFiles[season] is null ? null
                : refreshed.Glass is { } glass ? Open(glass)
                : throw new InvalidOperationException($"The refreshed surface for '{path}' draws over a window but states no glass of its own."))];
        if (surfaceShader is null) return;
        for (int season = 0; season < surface.Relief.Length; season++)
            if (surfaceArt.Relief(surface.Paths[season]) is { Depth: { } depth } relief && surfaceArt.Surface(surface.Paths[season]) is not null)
                surface.Relief[season] = (ReliefImage(relief), depth);
    }

    /// <summary>Draws each surface in the current season, art set and relief strength.</summary>
    private void ApplySurfaces()
    {
        surfacesRefreshed = drawRefreshedEffects();
        surfacesStrength = reliefStrength();
        int season = SurfaceSeason;
        foreach (SurfaceMaterial surface in surfaceMaterials) ApplySurface(surface, season);
        ApplyShells(season, surfacesRefreshed);
    }

    private int SurfaceSeason => materialSeason switch { DaggerfallExteriorSeason.Winter => 1, DaggerfallExteriorSeason.Rain => 2, _ => 0 };

    /// <summary>Draws one surface in a season, the current art set and relief strength, updating its material only where that changes it.</summary>
    private void ApplySurface(SurfaceMaterial surface, int season)
    {
        RenderResource wanted = DrawnSurface(surface, season, surfacesRefreshed);
        RenderResource? glass = DrawnGlass(surface, season, surfacesRefreshed);
        float level = glass is null ? 0F : windowLevel;
        Vector2 flicker = level > 0 ? windowFlicker : default;
        MaterialShader shader = surfaceShader is not { } surfaces ? default
            : surfacesRefreshed && surfacesStrength > 0 && surface.Relief[season] is { } height
                ? reliefTuning.Shader(surfaces, height.Image, height.Depth * surfacesStrength, flicker)
                : flicker.X > 0 ? reliefTuning.Shader(surfaces, null, 0, flicker) : default;
        if (wanted == surface.Current && glass == surface.CurrentGlass && level == surface.Level && shader == surface.Shader) return;
        appearance.UpdateMaterial(new MaterialUpdateRequest(surface.Material, SurfaceRequest(wanted, glass, shader)));
        (surface.Current, surface.CurrentGlass, surface.Level, surface.Shader) = (wanted, glass, level, shader);
    }
}
