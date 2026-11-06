using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The location's surfaces: each material draws its classic texture record in the current season (summer, winter or
/// rain, as the climate swap published them), or while refreshed art is drawn, that record's refreshed surface where the
/// set has one. A season or art change updates only the materials whose texture changes. A window's glass mask stays
/// bound to its material, so nightfall changes only the emission's level, in either art set: refreshed walls keep the
/// classic's window layout, so the classic's mask covers their glass.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private readonly List<SurfaceMaterial> surfaceMaterials = [];
    private DaggerfallArtReplacements? surfaceArt;
    private bool surfacesRefreshed;
    private Vector3 windowColor;
    private float windowLevel;

    /// <summary>
    /// One location material: its classic texture path and resource per season (summer, winter, rain; a season without a
    /// variant repeats summer), the refreshed resource per season once the art set is admitted (the classic one where it
    /// has none), the resource it draws now, and on a window the glass mask per season and the one it holds now.
    /// </summary>
    private sealed class SurfaceMaterial(Material material, string[] paths, RenderResource[] classic, RenderResource current, RenderResource?[] glass)
    {
        internal Material Material { get; } = material;
        internal string[] Paths { get; } = paths;
        internal RenderResource[] Classic { get; } = classic;
        internal RenderResource[]? Refreshed { get; set; }
        internal RenderResource Current { get; set; } = current;
        internal RenderResource?[] Glass { get; } = glass;
        internal RenderResource? CurrentGlass { get; set; } = glass[0];
        internal float Level { get; set; }
    }

    /// <summary>Lights the location's window glass in a colour at a level, which is zero while the windows are dark.</summary>
    internal void UpdateWindowGlass(Vector3 color, float level)
    {
        if (level == windowLevel && color == windowColor) return;
        (windowColor, windowLevel) = (color, level);
        ApplySurfaces();
    }

    /// <summary>A surface's material: its texture, and its glass lit at the current level.</summary>
    private MaterialRequest SurfaceRequest(RenderResource texture, RenderResource? glass)
    {
        MaterialRequest request = new(new Color(1F, 1F, 1F, 1F), texture, 1F, new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false);
        return glass is null ? request : request with { EmissionColor = windowColor, EmissionIntensity = windowLevel, EmissionMap = glass };
    }

    /// <summary>Admits the refreshed surfaces for the materials already drawn and every location admitted later.</summary>
    private void UseSurfaceArt(DaggerfallArtReplacements art)
    {
        surfaceArt = art;
        foreach (SurfaceMaterial surface in surfaceMaterials) AttachRefreshedSurface(surface);
        foreach (DecorationVisual flat in decorations) AttachDecorationFlats(flat);
        ApplySurfaces();
    }

    private void AttachRefreshedSurface(SurfaceMaterial surface)
    {
        if (surfaceArt is null || surface.Paths.All(path => surfaceArt.Surface(path) is null)) return;
        Dictionary<string, RenderResource> opened = new(StringComparer.Ordinal);
        surface.Refreshed = [.. surface.Paths.Select((path, season) =>
        {
            if (surfaceArt.Surface(path) is not { } refreshed) return surface.Classic[season];
            if (!opened.TryGetValue(refreshed.File.Path, out RenderResource? resource))
            {
                resource = appearance.OpenTexture(content, refreshed.File.Path, refreshed.File.Sha256, TextureFilter.Nearest, TextureWrap.Repeat).Handle;
                locationResources.Add(resource);
                opened.Add(refreshed.File.Path, resource);
            }
            return resource;
        })];
    }

    /// <summary>Draws each surface in the current season and art set.</summary>
    private void ApplySurfaces()
    {
        surfacesRefreshed = drawRefreshedEffects();
        int season = materialSeason switch { DaggerfallExteriorSeason.Winter => 1, DaggerfallExteriorSeason.Rain => 2, _ => 0 };
        foreach (SurfaceMaterial surface in surfaceMaterials)
        {
            RenderResource wanted = (surfacesRefreshed && surface.Refreshed is { } refreshed ? refreshed : surface.Classic)[season];
            RenderResource? glass = surface.Glass[season];
            float level = glass is null ? 0F : windowLevel;
            if (wanted == surface.Current && glass == surface.CurrentGlass && level == surface.Level) continue;
            appearance.UpdateMaterial(new MaterialUpdateRequest(surface.Material, SurfaceRequest(wanted, glass)));
            (surface.Current, surface.CurrentGlass, surface.Level) = (wanted, glass, level);
        }
    }
}
