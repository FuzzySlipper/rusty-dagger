using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Pass 3 volumetric 3D pixel-shell overlays for building interiors and dungeons (<c>interior-shells.wgsl</c>, #11043):
/// spawns lightweight stacked shell <see cref="Appearance"/> layers ONLY for distinct static world models that bind a
/// shell-eligible material (moss/vine dungeon walls, plush damask carpets, or heavy heraldic tapestries). Overlays each
/// model's drawn mesh (its 3D remodel where remodeled, skipping remodel-owned textures and shell-stacked slots so
/// classic meshes are never opened under refreshed art and remodels never clip); non-shell slots bind one shared
/// fast-discard material and shells cast no shadow.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private RenderResource? interiorShellShader;
    private bool interiorShellsEnabled;
    private bool interiorShellsBuilt;
    private Material? interiorShellDiscardMaterial;
    private readonly Dictionary<(SurfaceMaterial Surface, int Layer, int Layers), InteriorShellMaterial> interiorShellMaterials = [];
    private readonly Dictionary<ModelVisual, Appearance[]> interiorShellOverlays = [];
    private readonly List<(ModelVisual Model, Transform Pose, ulong[] Ordinals)> interiorShellPlacements = [];

    private sealed class InteriorShellMaterial(Material material, SurfaceMaterial surface, DaggerfallInteriorShellKind kind, int layer, int layers)
    {
        internal Material Material { get; } = material;
        internal SurfaceMaterial Surface { get; } = surface;
        internal DaggerfallInteriorShellKind Kind { get; } = kind;
        internal int Layer { get; } = layer;
        internal int Layers { get; } = layers;
        internal RenderResource? Current { get; set; }
    }

    internal void UpdateInteriorShells(string? shaderPath, ContentSha256 shaderSha256, bool enabled)
    {
        if (disposed || inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior) return;
        if (enabled && interiorShellShader is null && shaderPath is not null)
        {
            interiorShellShader = appearance.OpenShader(content, shaderPath, shaderSha256);
            ownedResources.Add(interiorShellShader);
        }
        bool want = enabled && interiorShellShader is not null;
        if (interiorShellsEnabled == want) return;
        interiorShellsEnabled = want;
        if (want) EnsureInteriorShellOverlays();
    }

    private void EnsureInteriorShellOverlays()
    {
        if (!interiorShellsEnabled
            || !surfacesRefreshed
            || interiorShellShader is null
            || inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior
            || world.Count == 0
            || interiorShellsBuilt)
            return;

        Dictionary<Material, SurfaceMaterial> surfaceByMaterial = new(surfaceMaterials.Count);
        foreach (SurfaceMaterial sm in surfaceMaterials) surfaceByMaterial[sm.Material] = sm;

        int season = SurfaceSeason;
        bool refreshed = surfacesRefreshed;

        foreach ((ModelVisual model, Transform pose, int _) in world)
        {
            if (!interiorShellOverlays.TryGetValue(model, out Appearance[]? layers))
            {
                layers = TryCreateInteriorShellOverlay(model, surfaceByMaterial, season, refreshed);
                if (layers is null) continue;
                interiorShellOverlays.Add(model, layers);
            }

            ulong[] ordinals = new ulong[layers.Length];
            for (int i = 0; i < layers.Length; i++) ordinals[i] = NextLocationVisualOrdinal();
            interiorShellPlacements.Add((model, pose, ordinals));
        }
        interiorShellsBuilt = true;
    }

    private Appearance[]? TryCreateInteriorShellOverlay(
        ModelVisual model,
        Dictionary<Material, SurfaceMaterial> surfaceByMaterial,
        int season,
        bool refreshed)
    {
        if (model.ModelId is not null && inputs.Geometry.Remodels.TryGetValue(model.ModelId, out DaggerfallSiteRemodel? remodel))
        {
            int maxLayers = 0;
            foreach (DaggerfallMeshMaterialBinding binding in remodel.Materials)
            {
                if (remodel.Shells.ContainsKey(binding.MeshSlot)) continue;
                if (!materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? worldMat)) continue;
                if (!surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)) continue;
                int count = DaggerfallInteriorSurfaceMaterials.ShellLayerCount(sm.ShellKind);
                if (count > maxLayers) maxLayers = count;
            }
            if (maxLayers == 0) return null;

            Appearance[] layers = new Appearance[maxLayers];
            for (int layer = 1; layer <= maxLayers; layer++)
            {
                Appearance overlay = appearance.CreateStaticMesh(content, remodel.Path, remodel.Sha256, worldAppearance.Tint, admittingOffUpdate);
                List<MeshMaterialBinding> bindings = new(remodel.Materials.Count + remodel.Textures.Count);
                foreach (DaggerfallMeshMaterialBinding src in remodel.Materials)
                {
                    Material mat = !remodel.Shells.ContainsKey(src.MeshSlot)
                        && materialsBySlot.TryGetValue(src.WorldMaterialSlot, out Material? worldMat)
                        && surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)
                        && sm.ShellKind != DaggerfallInteriorShellKind.None
                        && layer <= DaggerfallInteriorSurfaceMaterials.ShellLayerCount(sm.ShellKind)
                        ? GetOrCreateInteriorShellMaterial(sm, layer, DaggerfallInteriorSurfaceMaterials.ShellLayerCount(sm.ShellKind), season, refreshed)
                        : GetOrCreateInteriorShellDiscardMaterial();
                    bindings.Add(new MeshMaterialBinding(src.MeshSlot, mat));
                }
                foreach (DaggerfallRemodelTexture texture in remodel.Textures)
                    bindings.Add(new MeshMaterialBinding(texture.MeshSlot, GetOrCreateInteriorShellDiscardMaterial()));
                appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(overlay, bindings.ToArray()));
                layers[layer - 1] = overlay;
            }
            return layers;
        }

        int classicMaxLayers = 0;
        foreach (DaggerfallMeshMaterialBinding binding in model.Mesh.Materials)
        {
            if (!materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? worldMat)) continue;
            if (!surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)) continue;
            int count = DaggerfallInteriorSurfaceMaterials.ShellLayerCount(sm.ShellKind);
            if (count > classicMaxLayers) classicMaxLayers = count;
        }
        if (classicMaxLayers == 0) return null;

        Appearance[] classicLayers = new Appearance[classicMaxLayers];
        for (int layer = 1; layer <= classicMaxLayers; layer++)
        {
            Appearance overlay = appearance.CreateStaticMesh(content, model.Mesh.Path, model.Mesh.Sha256, worldAppearance.Tint, admittingOffUpdate);
            MeshMaterialBinding[] bindings = new MeshMaterialBinding[model.Mesh.Materials.Count];
            for (int i = 0; i < model.Mesh.Materials.Count; i++)
            {
                DaggerfallMeshMaterialBinding src = model.Mesh.Materials[i];
                Material mat = materialsBySlot.TryGetValue(src.WorldMaterialSlot, out Material? worldMat)
                    && surfaceByMaterial.TryGetValue(worldMat, out SurfaceMaterial? sm)
                    && sm.ShellKind != DaggerfallInteriorShellKind.None
                    && layer <= DaggerfallInteriorSurfaceMaterials.ShellLayerCount(sm.ShellKind)
                    ? GetOrCreateInteriorShellMaterial(sm, layer, DaggerfallInteriorSurfaceMaterials.ShellLayerCount(sm.ShellKind), season, refreshed)
                    : GetOrCreateInteriorShellDiscardMaterial();
                bindings[i] = new MeshMaterialBinding(src.MeshSlot, mat);
            }
            appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(overlay, bindings));
            classicLayers[layer - 1] = overlay;
        }
        return classicLayers;
    }

    private Material GetOrCreateInteriorShellDiscardMaterial()
    {
        if (interiorShellDiscardMaterial is not null) return interiorShellDiscardMaterial;
        RenderResource fallbackTex = surfaceMaterials[0].Current;
        MaterialRequest request = new MaterialRequest(new Color(1F, 1F, 1F, 1F), fallbackTex, 1F, new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false) with
        {
            AlphaMode = MaterialAlphaMode.Mask,
            AlphaCutoff = DaggerfallShellLayer.AlphaCutoff,
            Shader = DaggerfallInteriorSurfaceMaterials.BuildShellShader(interiorShellShader!, DaggerfallInteriorShellKind.None, 0, 0),
        };
        interiorShellDiscardMaterial = appearance.CreateMaterial(request);
        locationMaterials.Add(interiorShellDiscardMaterial);
        materials.Add(interiorShellDiscardMaterial);
        return interiorShellDiscardMaterial;
    }

    private Material GetOrCreateInteriorShellMaterial(SurfaceMaterial surface, int layer, int layers, int season, bool refreshed)
    {
        var key = (surface, layer, layers);
        if (interiorShellMaterials.TryGetValue(key, out InteriorShellMaterial? known)) return known.Material;
        RenderResource tex = DrawnSurface(surface, season, refreshed);
        Material created = appearance.CreateMaterial(InteriorShellRequest(tex, surface.ShellKind, layer, layers));
        locationMaterials.Add(created);
        materials.Add(created);
        InteriorShellMaterial entry = new(created, surface, surface.ShellKind, layer, layers) { Current = tex };
        interiorShellMaterials.Add(key, entry);
        return created;
    }

    private MaterialRequest InteriorShellRequest(RenderResource texture, DaggerfallInteriorShellKind kind, int layer, int layers) =>
        new MaterialRequest(new Color(1F, 1F, 1F, 1F), texture, 1F, new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false) with
        {
            AlphaMode = MaterialAlphaMode.Mask,
            AlphaCutoff = DaggerfallShellLayer.AlphaCutoff,
            Shader = DaggerfallInteriorSurfaceMaterials.BuildShellShader(interiorShellShader!, kind, layer, layers),
        };

    private void ApplyInteriorShells(int season, bool refreshed)
    {
        if (interiorShellShader is null) return;
        if (interiorShellsEnabled && !interiorShellsBuilt && refreshed)
            EnsureInteriorShellOverlays();
        foreach (InteriorShellMaterial entry in interiorShellMaterials.Values)
        {
            RenderResource wanted = DrawnSurface(entry.Surface, season, refreshed);
            if (entry.Current == wanted) continue;
            appearance.UpdateMaterial(new MaterialUpdateRequest(entry.Material, InteriorShellRequest(wanted, entry.Kind, entry.Layer, entry.Layers)));
            entry.Current = wanted;
        }
    }

    private void AppendInteriorShellFacts(List<AppearanceFact> facts, int slot)
    {
        if (!interiorShellsEnabled || !surfacesRefreshed || interiorShellPlacements.Count == 0) return;
        foreach ((ModelVisual model, Transform pose, ulong[] ordinals) in interiorShellPlacements)
        {
            if (!interiorShellOverlays.TryGetValue(model, out Appearance[]? layers)) continue;
            Transform worldPose = Compose(worldAppearance.Transform, pose);
            for (int i = 0; i < layers.Length; i++)
                facts.Add(new AppearanceFact(DaggerfallPresentationObjectIds.LocationVisual(slot, ordinals[i]), false, 0,
                    worldPose, layers[i], worldAppearance.Visible, worldAppearance.Layer, ShadowCasting.None));
        }
    }

    private void TakeInteriorShellResources(List<IDisposable> retired)
    {
        foreach (Appearance[] layers in interiorShellOverlays.Values)
            for (int i = layers.Length - 1; i >= 0; i--) retired.Add(layers[i]);
        interiorShellOverlays.Clear();
        interiorShellPlacements.Clear();
        interiorShellMaterials.Clear();
        interiorShellDiscardMaterial = null;
        interiorShellsBuilt = false;
    }
}
