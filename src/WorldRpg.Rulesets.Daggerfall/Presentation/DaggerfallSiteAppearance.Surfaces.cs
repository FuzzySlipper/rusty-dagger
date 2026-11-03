using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The location's surfaces: each material draws its classic texture record in the current season (summer, winter or
/// rain, as the climate swap published them), or while refreshed art is drawn, that record's refreshed surface where the
/// set has one. A season or art change updates only the materials whose texture changes.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private readonly List<SurfaceMaterial> surfaceMaterials = [];
    private DaggerfallArtReplacements? surfaceArt;
    private bool surfacesRefreshed;

    /// <summary>
    /// One location material: its classic texture path and resource per season (summer, winter, rain; a season without a
    /// variant repeats summer), the refreshed resource per season once the art set is admitted (the classic one where it
    /// has none), and the resource it draws now.
    /// </summary>
    private sealed class SurfaceMaterial(Material material, string[] paths, RenderResource[] classic, RenderResource current)
    {
        internal Material Material { get; } = material;
        internal string[] Paths { get; } = paths;
        internal RenderResource[] Classic { get; } = classic;
        internal RenderResource[]? Refreshed { get; set; }
        internal RenderResource Current { get; set; } = current;
    }

    /// <summary>Admits the refreshed surfaces for the materials already drawn and every location admitted later.</summary>
    private void UseSurfaceArt(DaggerfallArtReplacements art)
    {
        surfaceArt = art;
        foreach (SurfaceMaterial surface in surfaceMaterials) AttachRefreshedSurface(surface);
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
            if (wanted == surface.Current) continue;
            appearance.UpdateMaterial(new MaterialUpdateRequest(surface.Material, new MaterialRequest(new Color(1F, 1F, 1F, 1F), wanted, 1F,
                new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false)));
            surface.Current = wanted;
        }
    }
}
