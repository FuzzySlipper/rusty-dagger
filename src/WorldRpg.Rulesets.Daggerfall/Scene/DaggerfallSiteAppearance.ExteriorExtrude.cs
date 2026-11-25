using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Outward 3D architectural silhouette-POM overlays for exterior buildings (<c>exterior-extrude.wgsl</c>, #11075).
/// Overlays each building model's drawn mesh (its 3D remodel where remodeled, skipping remodel-owned textures and
/// shell-stacked hedge/low-wall slots so remodels never clip and hedges never double-extrude), only on material
/// slots whose authored relief declares a positive <see cref="DaggerfallGroundRelief.Extrude"/>.
/// <para>
/// Street-distance culling emits overlay facts only for buildings within <c>fadeEnd + 32 m</c> of the active
/// viewpoint so large cities like Daggerfall or Wayrest keep GPU instance overhead tiny with zero pop-in.
/// </para>
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private const float ExteriorExtrudeCutoff = 0.35f;
    private const float ExteriorExtrudeCullMarginMetres = 32f;
    // Matches the default SurfaceRelief strength (0.6) so default video settings render 100% of authored
    // outward metres while still scaling proportionally with the player's SurfaceRelief slider (0 disables).
    private const float ReferenceSurfaceReliefStrength = 0.6f;

    private RenderResource? exteriorExtrudeShader;
    private bool exteriorExtrudeEnabled;
    private bool exteriorExtrudeOverlaysBuilt;
    private Vector3? exteriorExtrudeViewpoint;
    private Material? exteriorExtrudeDiscardMaterial;
    private readonly Dictionary<SurfaceMaterial, ExteriorExtrudeMaterial> exteriorExtrudeMaterials = [];
    private readonly Dictionary<ModelVisual, Appearance> exteriorExtrudeOverlays = [];
    private readonly List<(ModelVisual Model, Transform Pose, ulong Ordinal)> exteriorExtrudePlacements = [];

    private sealed class ExteriorExtrudeMaterial(Material material, SurfaceMaterial surface)
    {
        internal Material Material { get; } = material;
        internal SurfaceMaterial Surface { get; } = surface;
        internal RenderResource? Current { get; set; }
        internal MaterialShader Shader { get; set; }
    }

    internal void UpdateExteriorExtrude(
        string? shaderPath,
        ContentSha256 shaderSha256,
        bool enabled,
        Vector3 viewpoint)
    {
        if (disposed || inputs.ProfileKind != DaggerfallWorldProfileKind.Exterior) return;
        exteriorExtrudeViewpoint = viewpoint;
        if (enabled && exteriorExtrudeShader is null && shaderPath is not null)
        {
            exteriorExtrudeShader = appearance.OpenShader(content, shaderPath, shaderSha256);
            ownedResources.Add(exteriorExtrudeShader);
        }
        bool effective = enabled && exteriorExtrudeShader is not null;
        if (exteriorExtrudeEnabled == effective) return;
        exteriorExtrudeEnabled = effective;
        if (effective)
        {
            EnsureExteriorExtrudeOverlays();
            ApplyExteriorExtrude(SurfaceSeason, surfacesRefreshed);
        }
    }

    private void EnsureExteriorExtrudeOverlays()
    {
        if (!exteriorExtrudeEnabled
            || exteriorExtrudeShader is null
            || inputs.ProfileKind != DaggerfallWorldProfileKind.Exterior
            || world.Count == 0
            || exteriorExtrudeOverlaysBuilt)
            return;

        Dictionary<Material, SurfaceMaterial> surfaceByMaterial = new(surfaceMaterials.Count);
        foreach (SurfaceMaterial sm in surfaceMaterials) surfaceByMaterial[sm.Material] = sm;

        int season = SurfaceSeason;
        bool refreshed = surfacesRefreshed;

        foreach ((ModelVisual model, Transform pose, int _) in world)
        {
            if (!exteriorExtrudeOverlays.TryGetValue(model, out Appearance? overlay))
            {
                overlay = TryCreateExteriorExtrudeOverlay(model, surfaceByMaterial, season, refreshed);
                if (overlay is null) continue;
                exteriorExtrudeOverlays.Add(model, overlay);
            }
            exteriorExtrudePlacements.Add((model, pose, NextLocationVisualOrdinal()));
        }
        exteriorExtrudeOverlaysBuilt = true;
    }

    private Appearance? TryCreateExteriorExtrudeOverlay(
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
                if (HasAnyExteriorExtrude(sm)) { anyExtrudable = true; break; }
            }
            if (!anyExtrudable) return null;

            Appearance created = appearance.CreateStaticMesh(content, remodel.Path, remodel.Sha256, worldAppearance.Tint, admittingOffUpdate);
            List<MeshMaterialBinding> bindings = new(remodel.Materials.Count + remodel.Textures.Count);
            foreach (DaggerfallMeshMaterialBinding binding in remodel.Materials)
            {
                Material mat = !remodel.Shells.ContainsKey(binding.MeshSlot)
                    && materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? worldMat)
                    && surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)
                    && HasAnyExteriorExtrude(sm)
                    ? GetOrCreateExteriorExtrudeMaterial(sm, season, refreshed)
                    : GetOrCreateExteriorExtrudeDiscardMaterial();
                bindings.Add(new MeshMaterialBinding(binding.MeshSlot, mat));
            }
            foreach (DaggerfallRemodelTexture texture in remodel.Textures)
                bindings.Add(new MeshMaterialBinding(texture.MeshSlot, GetOrCreateExteriorExtrudeDiscardMaterial()));

            appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(created, bindings.ToArray()));
            return created;
        }

        bool anyClassicExtrudable = false;
        foreach (DaggerfallMeshMaterialBinding binding in model.Mesh.Materials)
        {
            if (!materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? worldMat)) continue;
            if (!surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)) continue;
            if (HasAnyExteriorExtrude(sm)) { anyClassicExtrudable = true; break; }
        }
        if (!anyClassicExtrudable) return null;

        Appearance classicOverlay = appearance.CreateStaticMesh(content, model.Mesh.Path, model.Mesh.Sha256, worldAppearance.Tint, admittingOffUpdate);
        MeshMaterialBinding[] classicBindings = new MeshMaterialBinding[model.Mesh.Materials.Count];
        for (int i = 0; i < model.Mesh.Materials.Count; i++)
        {
            DaggerfallMeshMaterialBinding src = model.Mesh.Materials[i];
            Material mat = materialsBySlot.TryGetValue(src.WorldMaterialSlot, out Material? worldMat)
                && surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)
                && HasAnyExteriorExtrude(sm)
                ? GetOrCreateExteriorExtrudeMaterial(sm, season, refreshed)
                : GetOrCreateExteriorExtrudeDiscardMaterial();
            classicBindings[i] = new MeshMaterialBinding(src.MeshSlot, mat);
        }
        appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(classicOverlay, classicBindings));
        return classicOverlay;
    }

    private static bool HasAnyExteriorExtrude(SurfaceMaterial sm)
    {
        if (sm.Source.Frames is not null) return false;
        for (int s = 0; s < sm.Relief.Length; s++)
            if (sm.Relief[s] is { Extrude: > 0f }) return true;
        return false;
    }

    private Material GetOrCreateExteriorExtrudeDiscardMaterial()
    {
        if (exteriorExtrudeDiscardMaterial is not null) return exteriorExtrudeDiscardMaterial;
        RenderResource fallbackTex = surfaceMaterials[0].Current;
        MaterialRequest request = new MaterialRequest(new Color(1F, 1F, 1F, 1F), fallbackTex, 1F, new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false) with
        {
            AlphaMode = MaterialAlphaMode.Mask,
            AlphaCutoff = DaggerfallShellLayer.AlphaCutoff,
            Shader = BuildExteriorExtrudeDiscardShader(exteriorExtrudeShader!),
        };
        exteriorExtrudeDiscardMaterial = appearance.CreateMaterial(request);
        locationMaterials.Add(exteriorExtrudeDiscardMaterial);
        materials.Add(exteriorExtrudeDiscardMaterial);
        return exteriorExtrudeDiscardMaterial;
    }

    private Material GetOrCreateExteriorExtrudeMaterial(SurfaceMaterial surface, int season, bool refreshed)
    {
        if (exteriorExtrudeMaterials.TryGetValue(surface, out ExteriorExtrudeMaterial? known)) return known.Material;
        RenderResource tex = DrawnSurface(surface, season, refreshed);
        MaterialShader shader = ExteriorExtrudeShader(surface, season, refreshed);
        MaterialRequest request = ExteriorExtrudeRequest(tex, shader);
        Material created = appearance.CreateMaterial(request);
        locationMaterials.Add(created);
        materials.Add(created);
        exteriorExtrudeMaterials.Add(surface, new(created, surface) { Current = tex, Shader = shader });
        return created;
    }

    private static MaterialRequest ExteriorExtrudeRequest(RenderResource texture, MaterialShader shader) =>
        new MaterialRequest(new Color(1F, 1F, 1F, 1F), texture, 1F, new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false) with
        {
            AlphaMode = MaterialAlphaMode.Mask,
            AlphaCutoff = DaggerfallShellLayer.AlphaCutoff,
            Shader = shader,
        };

    private static MaterialShader BuildExteriorExtrudeDiscardShader(RenderResource shader) =>
        new(new RenderResourceReference(shader), Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero, default, default);

    private MaterialShader ExteriorExtrudeShader(SurfaceMaterial surface, int season, bool refreshed)
    {
        if (!refreshed
            || surfacesRelief.Strength <= 0f
            || surface.Relief[season] is not { Extrude: > 0f } relief)
            return BuildExteriorExtrudeDiscardShader(exteriorExtrudeShader!);

        float lift = Math.Clamp(relief.Extrude.Value * (surfacesRelief.Strength / ReferenceSurfaceReliefStrength), 0.005f, 0.22f);
        float steps = Math.Clamp(reliefTuning.Steps, 1, 48);
        float fadeStart = reliefTuning.FadeStart * surfacesRelief.Distance;
        float fadeEnd = reliefTuning.FadeEnd * surfacesRelief.Distance;
        return new MaterialShader(
            new RenderResourceReference(exteriorExtrudeShader!),
            new Vector4(1f, lift, steps, ExteriorExtrudeCutoff),
            new Vector4(fadeStart, fadeEnd, reliefTuning.Cavity, reliefTuning.Slope),
            new Vector4(0.88f, 0.68f, 0f, 0f),
            Vector4.Zero,
            new RenderResourceReference(relief.Image),
            default,
            lift + 0.01f);
    }

    private void ApplyExteriorExtrude(int season, bool refreshed)
    {
        if (exteriorExtrudeShader is null) return;
        if (exteriorExtrudeEnabled && !exteriorExtrudeOverlaysBuilt && refreshed && surfacesRelief.Strength > 0f)
            EnsureExteriorExtrudeOverlays();
        if (exteriorExtrudeMaterials.Count == 0) return;
        foreach (ExteriorExtrudeMaterial entry in exteriorExtrudeMaterials.Values)
        {
            RenderResource wanted = DrawnSurface(entry.Surface, season, refreshed);
            MaterialShader shader = ExteriorExtrudeShader(entry.Surface, season, refreshed);
            if (entry.Current == wanted && entry.Shader == shader) continue;
            appearance.UpdateMaterial(new MaterialUpdateRequest(entry.Material, ExteriorExtrudeRequest(wanted, shader)));
            entry.Current = wanted;
            entry.Shader = shader;
        }
    }

    private void AppendExteriorExtrudeFacts(List<AppearanceFact> facts, int slot)
    {
        if (!exteriorExtrudeEnabled
            || !surfacesRefreshed
            || surfacesRelief.Strength <= 0f
            || exteriorExtrudePlacements.Count == 0)
            return;

        float fadeEnd = reliefTuning.FadeEnd * surfacesRelief.Distance;
        float cullRadius = fadeEnd + ExteriorExtrudeCullMarginMetres;
        float cullRadiusSq = cullRadius * cullRadius;
        Vector3? viewpoint = exteriorExtrudeViewpoint;

        foreach ((ModelVisual model, Transform pose, ulong ordinal) in exteriorExtrudePlacements)
        {
            if (!exteriorExtrudeOverlays.TryGetValue(model, out Appearance? overlay)) continue;
            Transform worldPose = Compose(worldAppearance.Transform, pose);
            if (viewpoint is { } eye && Vector3.DistanceSquared(worldPose.Translation, eye) > cullRadiusSq)
                continue;
            facts.Add(new AppearanceFact(DaggerfallPresentationObjectIds.LocationVisual(slot, ordinal), false, 0,
                worldPose, overlay, worldAppearance.Visible, worldAppearance.Layer, ShadowCasting.None));
        }
    }

    private void TakeExteriorExtrudeResources(List<IDisposable> retired)
    {
        foreach (Appearance overlay in exteriorExtrudeOverlays.Values)
            retired.Add(overlay);
        exteriorExtrudeOverlays.Clear();
        exteriorExtrudePlacements.Clear();
        exteriorExtrudeMaterials.Clear();
        exteriorExtrudeDiscardMaterial = null;
        exteriorExtrudeOverlaysBuilt = false;
    }
}
