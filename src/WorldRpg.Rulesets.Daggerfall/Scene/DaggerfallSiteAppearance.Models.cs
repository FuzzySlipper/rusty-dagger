using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The remodels of classic ARCH3D models. Each model the profile places that the art pack remodels has one appearance of
/// its remodel, shared by every placement and moving model of it, created beside the classic's. While refreshed art is
/// drawn the remodel is drawn in the classic's place at the classic's pose, so <c>world.classic-art</c> switches them live;
/// the automap, collision and navigation read the classic mesh and never these.
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private readonly Dictionary<string, Appearance> remodels = new(StringComparer.Ordinal);

    // A cut-out texture's texels under this alpha are not drawn, as the ground cover and flora cut theirs.
    private const float RemodelAlphaCutoff = .5f;

    /// <summary>
    /// The remodel of a placed model, created once for the location: its classic texture slots draw the location's
    /// materials for them, which follow the season and art set, and its own textures their own materials, opaque or cut
    /// out by their alpha. Null where the profile has none.
    /// </summary>
    private Appearance? Remodel(string? modelId)
    {
        if (modelId is null || !inputs.Geometry.Remodels.TryGetValue(modelId, out DaggerfallSiteRemodel? remodel)) return null;
        if (remodels.TryGetValue(modelId, out Appearance? shown)) return shown;
        Appearance created = appearance.CreateStaticMesh(content, remodel.Path, remodel.Sha256, worldAppearance.Tint, admittingOffUpdate);
        // Held from creation, so a refused binding releases it with the location.
        remodels.Add(modelId, created);
        List<MeshMaterialBinding> bindings = [.. remodel.Materials.Select(binding => materialsBySlot.TryGetValue(binding.WorldMaterialSlot, out Material? material)
            ? new MeshMaterialBinding(binding.MeshSlot, material)
            : throw new InvalidOperationException($"The remodel of model {modelId} refers to missing world material slot {binding.WorldMaterialSlot}."))];
        foreach (DaggerfallRemodelTexture texture in remodel.Textures)
        {
            RenderResource resource = appearance.OpenTexture(content, texture.Path, texture.Sha256, TextureFilter.Nearest, TextureWrap.Repeat, admittingOffUpdate).Handle;
            locationResources.Add(resource);
            MaterialRequest request = SurfaceRequest(resource, null);
            Material material = appearance.CreateMaterial(texture.Mask ? request with { AlphaMode = MaterialAlphaMode.Mask, AlphaCutoff = RemodelAlphaCutoff } : request);
            locationMaterials.Add(material);
            materials.Add(material);
            bindings.Add(new MeshMaterialBinding(texture.MeshSlot, material));
        }
        appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(created, bindings.ToArray()));
        return created;
    }

    /// <summary>What a placed model shows now: its remodel while refreshed art is drawn and it has one, else the classic.</summary>
    private static Appearance Drawn(Appearance classic, Appearance? remodel, bool refreshed) => refreshed && remodel is not null ? remodel : classic;
}
