using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Outward 3D architectural silhouette-POM overlays for building interiors and dungeons (<c>interior-extrude.wgsl</c>,
/// #11043 / #11067). Overlays each model's drawn mesh (its 3D remodel where remodeled, skipping remodel-owned textures
/// and shell-stacked slots so classic meshes are never opened under refreshed art and remodels never clip) with a single
/// non-shadow-casting outer envelope.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private RenderResource? interiorExtrudeShader;
    private bool interiorExtrudeEnabled;
    private bool interiorExtrudeOverlaysBuilt;
    private DaggerfallInteriorExtrudeTuning interiorExtrudeTuning = DaggerfallInteriorExtrudeTuning.Default;
    private Material? interiorExtrudeDiscardMaterial;
    private readonly Dictionary<SurfaceMaterial, InteriorExtrudeMaterial> interiorExtrudeMaterials = [];
    private readonly Dictionary<ModelVisual, Appearance> interiorExtrudeOverlays = [];
    private readonly List<(ModelVisual Model, Transform Pose, ulong Ordinal)> interiorExtrudePlacements = [];

    private sealed class InteriorExtrudeMaterial(Material material, SurfaceMaterial surface)
    {
        internal Material Material { get; } = material;
        internal SurfaceMaterial Surface { get; } = surface;
        internal RenderResource? Current { get; set; }
    }

    internal void UpdateInteriorExtrude(
        string? shaderPath,
        ContentSha256 shaderSha256,
        bool enabled,
        DaggerfallInteriorExtrudeTuning? tuning = null)
    {
        if (disposed || inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior) return;
        DaggerfallInteriorExtrudeTuning nextTuning = tuning ?? DaggerfallInteriorExtrudeTuning.Default;
        if (enabled && interiorExtrudeShader is null && shaderPath is not null)
        {
            interiorExtrudeShader = appearance.OpenShader(content, shaderPath, shaderSha256);
            ownedResources.Add(interiorExtrudeShader);
        }
        bool effective = enabled && interiorExtrudeShader is not null;
        bool modeChanged = interiorExtrudeEnabled != effective;
        bool tuningChanged = interiorExtrudeTuning != nextTuning;
        if (!modeChanged && !tuningChanged) return;
        interiorExtrudeEnabled = effective;
        interiorExtrudeTuning = nextTuning;
        if (effective)
        {
            if (modeChanged) EnsureInteriorExtrudeOverlays();
            if (tuningChanged) ApplyInteriorExtrude(SurfaceSeason, surfacesRefreshed);
        }
    }

    private void EnsureInteriorExtrudeOverlays()
    {
        if (!interiorExtrudeEnabled
            || !surfacesRefreshed
            || interiorExtrudeShader is null
            || inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior
            || world.Count == 0
            || interiorExtrudeOverlaysBuilt)
            return;

        Dictionary<Material, SurfaceMaterial> surfaceByMaterial = new(surfaceMaterials.Count);
        foreach (SurfaceMaterial sm in surfaceMaterials) surfaceByMaterial[sm.Material] = sm;

        int season = SurfaceSeason;
        bool refreshed = surfacesRefreshed;

        foreach ((ModelVisual model, Transform pose, int _) in world)
        {
            if (!interiorExtrudeOverlays.TryGetValue(model, out Appearance? overlay))
            {
                overlay = TryCreateInteriorExtrudeOverlay(model, surfaceByMaterial, season, refreshed);
                if (overlay is null) continue;
                interiorExtrudeOverlays.Add(model, overlay);
            }
            interiorExtrudePlacements.Add((model, pose, NextLocationVisualOrdinal()));
        }
        interiorExtrudeOverlaysBuilt = true;
    }

    private Appearance? TryCreateInteriorExtrudeOverlay(
        ModelVisual model,
        Dictionary<Material, SurfaceMaterial> surfaceByMaterial,
        int season,
        bool refreshed)
    {
        if (model.ModelId is not null && inputs.Geometry.Remodels.TryGetValue(model.ModelId, out DaggerfallSiteRemodel? remodel))
        {
            bool anyExtrudable = false;
            foreach (DaggerfallMeshMaterialBinding binding in remodel.Materials)
            {
                if (remodel.Shells.ContainsKey(binding.MeshSlot)) continue;
                if (!materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? worldMat)) continue;
                if (!surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)) continue;
                if (IsExtrudable(sm, season)) { anyExtrudable = true; break; }
            }
            if (!anyExtrudable) return null;

            Appearance created = appearance.CreateStaticMesh(content, remodel.Path, remodel.Sha256, worldAppearance.Tint, admittingOffUpdate);
            List<MeshMaterialBinding> bindings = new(remodel.Materials.Count + remodel.Textures.Count);
            foreach (DaggerfallMeshMaterialBinding src in remodel.Materials)
            {
                Material mat = !remodel.Shells.ContainsKey(src.MeshSlot)
                    && materialsBySlot.TryGetValue(src.WorldMaterialSlot, out Material? worldMat)
                    && surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)
                    && IsExtrudable(sm, season)
                    ? GetOrCreateInteriorExtrudeMaterial(sm, season, refreshed)
                    : GetOrCreateInteriorExtrudeDiscardMaterial();
                bindings.Add(new MeshMaterialBinding(src.MeshSlot, mat));
            }
            foreach (DaggerfallRemodelTexture texture in remodel.Textures)
                bindings.Add(new MeshMaterialBinding(texture.MeshSlot, GetOrCreateInteriorExtrudeDiscardMaterial()));
            appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(created, bindings.ToArray()));
            return created;
        }

        bool anyClassicExtrudable = false;
        foreach (DaggerfallMeshMaterialBinding binding in model.Mesh.Materials)
        {
            if (!materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? worldMat)) continue;
            if (!surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)) continue;
            if (IsExtrudable(sm, season)) { anyClassicExtrudable = true; break; }
        }
        if (!anyClassicExtrudable) return null;

        Appearance overlay = appearance.CreateStaticMesh(content, model.Mesh.Path, model.Mesh.Sha256, worldAppearance.Tint, admittingOffUpdate);
        MeshMaterialBinding[] classicBindings = new MeshMaterialBinding[model.Mesh.Materials.Count];
        for (int i = 0; i < model.Mesh.Materials.Count; i++)
        {
            DaggerfallMeshMaterialBinding src = model.Mesh.Materials[i];
            Material mat = materialsBySlot.TryGetValue(src.WorldMaterialSlot, out Material? worldMat)
                && surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)
                && IsExtrudable(sm, season)
                ? GetOrCreateInteriorExtrudeMaterial(sm, season, refreshed)
                : GetOrCreateInteriorExtrudeDiscardMaterial();
            classicBindings[i] = new MeshMaterialBinding(src.MeshSlot, mat);
        }
        appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(overlay, classicBindings));
        return overlay;
    }

    private bool IsExtrudable(SurfaceMaterial sm, int season)
    {
        if (sm.Source.Frames is not null
            || sm.ClassicGlassFiles[0] is not null
            || sm.ClassicGlassFiles[1] is not null
            || sm.ClassicGlassFiles[2] is not null)
            return false;
        if (InteriorReliefEntry(sm, season) is { Extrude: { } authoredExtrude })
            return authoredExtrude > 0f;
        return DaggerfallInteriorSurfaceMaterials.ExtrudeScale(sm.SurfaceClass) > 0f;
    }

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

    private Material GetOrCreateInteriorExtrudeMaterial(SurfaceMaterial surface, int season, bool refreshed)
    {
        if (interiorExtrudeMaterials.TryGetValue(surface, out InteriorExtrudeMaterial? known)) return known.Material;
        RenderResource tex = DrawnSurface(surface, season, refreshed);
        Material created = appearance.CreateMaterial(InteriorExtrudeRequest(surface, tex));
        locationMaterials.Add(created);
        materials.Add(created);
        interiorExtrudeMaterials.Add(surface, new(created, surface) { Current = tex });
        return created;
    }

    private MaterialRequest InteriorExtrudeRequest(SurfaceMaterial surface, RenderResource texture)
    {
        DaggerfallInteriorReliefSettings baseSettings = interiorRelief
            ?? DaggerfallInteriorLook.ResolveRelief("voxel-pulp", inputs.ProfileKind == DaggerfallWorldProfileKind.Dungeon)!;
        var authored = InteriorReliefEntry(surface, SurfaceSeason);
        return new MaterialRequest(new Color(1F, 1F, 1F, 1F), texture, 1F, new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false) with
        {
            AlphaMode = MaterialAlphaMode.Mask,
            AlphaCutoff = DaggerfallShellLayer.AlphaCutoff,
            Shader = DaggerfallInteriorSurfaceMaterials.BuildExtrudeShader(
                interiorExtrudeShader!,
                authored?.Image,
                baseSettings,
                surface.SurfaceClass,
                interiorDungeonDampness,
                interiorWaterLevelY,
                interiorExtrudeTuning,
                authored?.Extrude),
        };
    }

    private void ApplyInteriorExtrude(int season, bool refreshed)
    {
        if (interiorExtrudeShader is null) return;
        if (interiorExtrudeEnabled && !interiorExtrudeOverlaysBuilt && refreshed)
            EnsureInteriorExtrudeOverlays();
        if (interiorExtrudeMaterials.Count == 0) return;
        foreach (InteriorExtrudeMaterial entry in interiorExtrudeMaterials.Values)
        {
            RenderResource wanted = DrawnSurface(entry.Surface, season, refreshed);
            appearance.UpdateMaterial(new MaterialUpdateRequest(
                entry.Material,
                InteriorExtrudeRequest(entry.Surface, wanted)));
            entry.Current = wanted;
        }
    }

    private void AppendInteriorExtrudeFacts(List<AppearanceFact> facts, int slot)
    {
        if (!interiorExtrudeEnabled || !surfacesRefreshed || interiorExtrudePlacements.Count == 0) return;
        foreach ((ModelVisual model, Transform pose, ulong ordinal) in interiorExtrudePlacements)
        {
            if (!interiorExtrudeOverlays.TryGetValue(model, out Appearance? overlay)) continue;
            Transform worldPose = Compose(worldAppearance.Transform, pose);
            facts.Add(new AppearanceFact(DaggerfallPresentationObjectIds.LocationVisual(slot, ordinal), false, 0,
                worldPose, overlay, worldAppearance.Visible, worldAppearance.Layer, ShadowCasting.None));
        }
    }

    private void TakeInteriorExtrudeResources(List<IDisposable> retired)
    {
        foreach (Appearance overlay in interiorExtrudeOverlays.Values)
            retired.Add(overlay);
        interiorExtrudeOverlays.Clear();
        interiorExtrudePlacements.Clear();
        interiorExtrudeMaterials.Clear();
        interiorExtrudeDiscardMaterial = null;
        interiorExtrudeOverlaysBuilt = false;
    }
}
