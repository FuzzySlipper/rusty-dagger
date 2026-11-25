using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Experimental outward 3D architectural extrusion overlays (<c>interior-extrude.wgsl</c>, #11043):
/// compares single-envelope outward silhouette-POM (<c>silhouette-pom</c>) against grass-FX-style 14-layer
/// stacked extruded wall shells (<c>wall-shells</c> and shadow-casting <c>wall-shells-shadow</c>).
/// Leaves normal Pass 3 defaults completely untouched when <see cref="DaggerfallInteriorExtrudeMode.Off"/>.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private const int ExtrudeWallShellLayers = 14;
    private RenderResource? interiorExtrudeShader;
    private DaggerfallInteriorExtrudeMode interiorExtrudeMode = DaggerfallInteriorExtrudeMode.Off;
    private DaggerfallInteriorExtrudeMode builtExtrudeMode = DaggerfallInteriorExtrudeMode.Off;
    private Material? interiorExtrudeDiscardMaterial;
    private readonly Dictionary<(SurfaceMaterial Surface, int Layer, int Layers, DaggerfallInteriorExtrudeMode Mode), InteriorExtrudeMaterial> interiorExtrudeMaterials = [];
    private readonly Dictionary<ModelVisual, Appearance[]> interiorExtrudeOverlays = [];
    private readonly List<Appearance> retiredExtrudeOverlays = [];
    private readonly List<(ModelVisual Model, Transform Pose, ulong[] Ordinals)> interiorExtrudePlacements = [];

    private sealed class InteriorExtrudeMaterial(Material material, SurfaceMaterial surface, int layer, int layers, DaggerfallInteriorExtrudeMode mode)
    {
        internal Material Material { get; } = material;
        internal SurfaceMaterial Surface { get; } = surface;
        internal int Layer { get; } = layer;
        internal int Layers { get; } = layers;
        internal DaggerfallInteriorExtrudeMode Mode { get; } = mode;
        internal RenderResource? Current { get; set; }
    }

    internal void UpdateInteriorExtrude(string? shaderPath, ContentSha256 shaderSha256, DaggerfallInteriorExtrudeMode mode)
    {
        if (disposed || inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior) return;
        bool wantsOverlay = mode is DaggerfallInteriorExtrudeMode.SilhouettePom
            or DaggerfallInteriorExtrudeMode.WallShells
            or DaggerfallInteriorExtrudeMode.WallShellsShadow;
        if (wantsOverlay && interiorExtrudeShader is null && shaderPath is not null)
        {
            interiorExtrudeShader = appearance.OpenShader(content, shaderPath, shaderSha256);
            ownedResources.Add(interiorExtrudeShader);
        }
        DaggerfallInteriorExtrudeMode effective = wantsOverlay && interiorExtrudeShader is not null
            ? mode
            : DaggerfallInteriorExtrudeMode.Off;
        if (interiorExtrudeMode == effective) return;
        interiorExtrudeMode = effective;
        if (effective != DaggerfallInteriorExtrudeMode.Off)
            EnsureInteriorExtrudeOverlays();
    }

    private void EnsureInteriorExtrudeOverlays()
    {
        if (interiorExtrudeMode == DaggerfallInteriorExtrudeMode.Off
            || interiorExtrudeShader is null
            || inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior
            || world.Count == 0)
            return;

        bool needRebuild = interiorExtrudeOverlays.Count == 0
            || LayerCountFor(builtExtrudeMode) != LayerCountFor(interiorExtrudeMode);
        if (!needRebuild) return;

        // If switching live between 1-layer SilhouettePom and 14-layer WallShells, drop previous overlays cleanly.
        if (interiorExtrudeOverlays.Count > 0)
        {
            foreach (Appearance[] existing in interiorExtrudeOverlays.Values)
                foreach (Appearance layerApp in existing)
                    retiredExtrudeOverlays.Add(layerApp);
            interiorExtrudeOverlays.Clear();
            interiorExtrudePlacements.Clear();
            interiorExtrudeMaterials.Clear();
            interiorExtrudeDiscardMaterial = null;
        }

        Dictionary<Material, SurfaceMaterial> surfaceByMaterial = new(surfaceMaterials.Count);
        foreach (SurfaceMaterial sm in surfaceMaterials) surfaceByMaterial[sm.Material] = sm;

        int season = SurfaceSeason;
        bool refreshed = surfacesRefreshed;
        int targetLayers = LayerCountFor(interiorExtrudeMode);

        foreach ((ModelVisual model, Transform pose, int _) in world)
        {
            if (!interiorExtrudeOverlays.TryGetValue(model, out Appearance[]? layers))
            {
                bool anyExtrudable = false;
                foreach (DaggerfallMeshMaterialBinding binding in model.Mesh.Materials)
                {
                    if (!materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? worldMat)) continue;
                    if (!surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)) continue;
                    if (IsExtrudable(sm)) { anyExtrudable = true; break; }
                }
                if (!anyExtrudable) continue;

                layers = new Appearance[targetLayers];
                for (int layer = 1; layer <= targetLayers; layer++)
                {
                    Appearance overlay = appearance.CreateStaticMesh(content, model.Mesh.Path, model.Mesh.Sha256, worldAppearance.Tint, admittingOffUpdate);
                    MeshMaterialBinding[] bindings = new MeshMaterialBinding[model.Mesh.Materials.Count];
                    for (int i = 0; i < model.Mesh.Materials.Count; i++)
                    {
                        DaggerfallMeshMaterialBinding src = model.Mesh.Materials[i];
                        Material mat = materialsBySlot.TryGetValue(src.WorldMaterialSlot, out Material? worldMat)
                            && surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)
                            && IsExtrudable(sm)
                            ? GetOrCreateInteriorExtrudeMaterial(sm, layer, targetLayers, interiorExtrudeMode, season, refreshed)
                            : GetOrCreateInteriorExtrudeDiscardMaterial();
                        bindings[i] = new MeshMaterialBinding(src.MeshSlot, mat);
                    }
                    appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(overlay, bindings));
                    layers[layer - 1] = overlay;
                }
                interiorExtrudeOverlays.Add(model, layers);
            }

            ulong[] ordinals = new ulong[layers.Length];
            for (int i = 0; i < layers.Length; i++) ordinals[i] = NextLocationVisualOrdinal();
            interiorExtrudePlacements.Add((model, pose, ordinals));
        }
        builtExtrudeMode = interiorExtrudeMode;
    }

    private static int LayerCountFor(DaggerfallInteriorExtrudeMode mode) => mode switch
    {
        DaggerfallInteriorExtrudeMode.SilhouettePom => 1,
        DaggerfallInteriorExtrudeMode.WallShells or DaggerfallInteriorExtrudeMode.WallShellsShadow => ExtrudeWallShellLayers,
        _ => 0,
    };

    private static bool IsExtrudable(SurfaceMaterial sm) =>
        sm.Source.Frames is null
        && sm.ClassicGlassFiles[0] is null
        && sm.ClassicGlassFiles[1] is null
        && sm.ClassicGlassFiles[2] is null
        && DaggerfallInteriorSurfaceMaterials.ExtrudeScale(sm.SurfaceClass) > 0f;

    private Material GetOrCreateInteriorExtrudeDiscardMaterial()
    {
        if (interiorExtrudeDiscardMaterial is not null) return interiorExtrudeDiscardMaterial;
        RenderResource fallbackTex = surfaceMaterials[0].Current;
        MaterialRequest request = new MaterialRequest(new Color(1F, 1F, 1F, 1F), fallbackTex, 1F, new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false) with
        {
            AlphaMode = MaterialAlphaMode.Mask,
            AlphaCutoff = DaggerfallShellLayer.AlphaCutoff,
            Shader = DaggerfallInteriorSurfaceMaterials.BuildExtrudeDiscardShader(interiorExtrudeShader!),
        };
        interiorExtrudeDiscardMaterial = appearance.CreateMaterial(request);
        locationMaterials.Add(interiorExtrudeDiscardMaterial);
        materials.Add(interiorExtrudeDiscardMaterial);
        return interiorExtrudeDiscardMaterial;
    }

    private Material GetOrCreateInteriorExtrudeMaterial(
        SurfaceMaterial surface, int layer, int layers, DaggerfallInteriorExtrudeMode mode, int season, bool refreshed)
    {
        var key = (surface, layer, layers, mode);
        if (interiorExtrudeMaterials.TryGetValue(key, out InteriorExtrudeMaterial? known)) return known.Material;
        RenderResource tex = DrawnSurface(surface, season, refreshed);
        Material created = appearance.CreateMaterial(InteriorExtrudeRequest(surface, tex, layer, layers, mode));
        locationMaterials.Add(created);
        materials.Add(created);
        InteriorExtrudeMaterial entry = new(created, surface, layer, layers, mode) { Current = tex };
        interiorExtrudeMaterials.Add(key, entry);
        return created;
    }

    private MaterialRequest InteriorExtrudeRequest(
        SurfaceMaterial surface, RenderResource texture, int layer, int layers, DaggerfallInteriorExtrudeMode mode)
    {
        DaggerfallInteriorReliefSettings baseSettings = interiorRelief
            ?? DaggerfallInteriorLook.ResolveRelief("voxel-pulp", inputs.ProfileKind == DaggerfallWorldProfileKind.Dungeon)!;
        return new MaterialRequest(new Color(1F, 1F, 1F, 1F), texture, 1F, new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false) with
        {
            AlphaMode = MaterialAlphaMode.Mask,
            AlphaCutoff = DaggerfallShellLayer.AlphaCutoff,
            Shader = DaggerfallInteriorSurfaceMaterials.BuildExtrudeShader(
                interiorExtrudeShader!,
                baseSettings,
                surface.SurfaceClass,
                mode,
                layer,
                layers,
                interiorDungeonDampness,
                interiorWaterLevelY),
        };
    }

    private void ApplyInteriorExtrude(int season, bool refreshed)
    {
        if (interiorExtrudeShader is null || interiorExtrudeMaterials.Count == 0) return;
        foreach (InteriorExtrudeMaterial entry in interiorExtrudeMaterials.Values)
        {
            RenderResource wanted = DrawnSurface(entry.Surface, season, refreshed);
            appearance.UpdateMaterial(new MaterialUpdateRequest(
                entry.Material,
                InteriorExtrudeRequest(entry.Surface, wanted, entry.Layer, entry.Layers, entry.Mode)));
            entry.Current = wanted;
        }
    }

    private void AppendInteriorExtrudeFacts(List<AppearanceFact> facts, int slot)
    {
        if (interiorExtrudeMode == DaggerfallInteriorExtrudeMode.Off || interiorExtrudePlacements.Count == 0) return;
        bool castShadows = interiorExtrudeMode == DaggerfallInteriorExtrudeMode.WallShellsShadow;
        foreach ((ModelVisual model, Transform pose, ulong[] ordinals) in interiorExtrudePlacements)
        {
            if (!interiorExtrudeOverlays.TryGetValue(model, out Appearance[]? layers)) continue;
            Transform worldPose = Compose(worldAppearance.Transform, pose);
            for (int i = 0; i < layers.Length; i++)
            {
                ShadowCasting shadow = castShadows && i >= 3 && (i % 2 == 1) ? ShadowCasting.Cast : ShadowCasting.None;
                facts.Add(new AppearanceFact(DaggerfallPresentationObjectIds.LocationVisual(slot, ordinals[i]), false, 0,
                    worldPose, layers[i], worldAppearance.Visible, worldAppearance.Layer, shadow));
            }
        }
    }

    private void TakeInteriorExtrudeResources(List<IDisposable> retired)
    {
        foreach (Appearance[] layers in interiorExtrudeOverlays.Values)
            for (int i = layers.Length - 1; i >= 0; i--)
                retired.Add(layers[i]);
        for (int i = retiredExtrudeOverlays.Count - 1; i >= 0; i--)
            retired.Add(retiredExtrudeOverlays[i]);
        retiredExtrudeOverlays.Clear();
        interiorExtrudeOverlays.Clear();
        interiorExtrudePlacements.Clear();
        interiorExtrudeMaterials.Clear();
        interiorExtrudeDiscardMaterial = null;
        builtExtrudeMode = DaggerfallInteriorExtrudeMode.Off;
    }
}
