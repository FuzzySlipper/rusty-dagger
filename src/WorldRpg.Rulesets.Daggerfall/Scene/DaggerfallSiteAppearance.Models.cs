using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The remodels of classic ARCH3D models. Each model the profile places that the art pack remodels has one appearance of
/// its remodel, shared by every placement, moving model and city gate state of it, created beside the classic's. While
/// refreshed art is drawn the remodel is drawn in the classic's place at the classic's pose, so <c>world.classic-art</c>
/// switches them live; the automap, collision and navigation read the classic mesh and never these.
/// <para>
/// A remodel's slot may draw its classic texture as a layer of a shell stack (<see cref="DaggerfallArtShell"/>), as hedges
/// do: the layer draws the relief of the texture the location draws there now, in the season and art set, with the ground
/// cover's shader, so a hedge's leaves rise out of it in pixels as blades rise out of the ground. Where the set has no
/// relief for that texture, or classic art is drawn, each layer draws the surface whole, and the stack closes into the
/// classic's faces.
/// </para>
/// <para>
/// A remodel's own texture that is a strip of frames, as a hearth's flames, shows one frame at a time, stepped on the held
/// art's clock, so it pauses with the world; one drawn unlit keeps its own colours whatever the light.
/// </para>
/// </summary>
internal sealed partial class DaggerfallSiteAppearance
{
    private readonly Dictionary<string, Appearance> remodels = new(StringComparer.Ordinal);
    private readonly List<RemodelFlipbook> flipbooks = [];
    private double flipbookSeconds;
    // One material for each layer of each location material a remodel stacks, shared by every remodel stacking it.
    private readonly Dictionary<(Material Surface, DaggerfallArtShell Shell), ShellMaterial> shellMaterials = [];
    // The location's relief images, by path: one texture's relief serves every layer of its stacks.
    private readonly Dictionary<string, RenderResource> reliefImages = new(StringComparer.Ordinal);
    // The ground cover's shader, which draws every shell stack, once an art set that stacks shells is admitted.
    private RenderResource? shellShader;

    // A cut-out texture's texels under this alpha are not drawn, as the ground cover and flora cut theirs.
    private const float RemodelAlphaCutoff = .5f;
    // A shell layer's own alpha cut leaves the shader to make the layer's cut, as the ground cover's shells do.
    private const float ShellAlphaCutoff = 1f / 512;

    /// <summary>
    /// One shell layer's material: the location surface it stacks, its layer, the relief and its image per season (summer,
    /// winter, rain; null where the set has none for that season's texture), and what it draws now.
    /// </summary>
    private sealed class ShellMaterial(Material material, SurfaceMaterial surface, DaggerfallArtShell shell)
    {
        internal Material Material { get; } = material;
        internal SurfaceMaterial Surface { get; } = surface;
        internal DaggerfallArtShell Shell { get; } = shell;
        internal (RenderResource Image, DaggerfallGroundRelief Art)?[] Relief { get; } = new (RenderResource, DaggerfallGroundRelief)?[3];
        internal (RenderResource Texture, DaggerfallGroundRelief? Relief)? Current { get; set; }
    }

    /// <summary>A remodel material that steps through a strip: the request it was made with, and the frame it shows.</summary>
    private sealed class RemodelFlipbook(Material material, MaterialRequest request, DaggerfallArtFlipbook flipbook)
    {
        internal Material Material { get; } = material;
        internal MaterialRequest Request { get; } = request;
        internal DaggerfallArtFlipbook Flipbook { get; } = flipbook;
        internal int Frame { get; set; }
    }

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
            ? new MeshMaterialBinding(binding.MeshSlot, remodel.Shells.TryGetValue(binding.MeshSlot, out DaggerfallArtShell? shell) ? Shell(material, shell) : material)
            : throw new InvalidOperationException($"The remodel of model {modelId} refers to missing world material slot {binding.WorldMaterialSlot}."))];
        foreach (DaggerfallRemodelTexture texture in remodel.Textures)
        {
            RenderResource resource = appearance.OpenTexture(content, texture.Path, texture.Sha256, TextureFilter.Nearest, TextureWrap.Repeat, admittingOffUpdate).Handle;
            locationResources.Add(resource);
            MaterialRequest request = SurfaceRequest(resource, null) with { Unlit = texture.Unlit };
            if (texture.Mask) request = request with { AlphaMode = MaterialAlphaMode.Mask, AlphaCutoff = RemodelAlphaCutoff };
            // A strip's faces cover one frame's width of [0, 1]: the texture is scaled to one frame, and stepping moves it along.
            if (texture.Flipbook is { } strip) request = request with { TextureScale = new Vector2(1F / strip.Frames, 1F) };
            Material material = appearance.CreateMaterial(request);
            locationMaterials.Add(material);
            materials.Add(material);
            if (texture.Flipbook is { } book) flipbooks.Add(new RemodelFlipbook(material, request, book));
            bindings.Add(new MeshMaterialBinding(texture.MeshSlot, material));
        }
        appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(created, bindings.ToArray()));
        return created;
    }

    /// <summary>One state of a city gate as drawn: its classic model and that model's remodel, where the profile has one.</summary>
    private readonly record struct GateVisual(Appearance Classic, Appearance? Remodel);

    /// <summary>The material drawing one layer of a location material's shell stack, made the first time a remodel stacks it.</summary>
    private Material Shell(Material surface, DaggerfallArtShell shell)
    {
        if (shellMaterials.TryGetValue((surface, shell), out ShellMaterial? known)) return known.Material;
        SurfaceMaterial stacked = surfaceMaterials.First(candidate => candidate.Material == surface);
        Material created = appearance.CreateMaterial(SurfaceRequest(stacked.Current, null));
        locationMaterials.Add(created);
        materials.Add(created);
        ShellMaterial layer = new(created, stacked, shell);
        AttachShellRelief(layer);
        shellMaterials.Add((surface, shell), layer);
        return created;
    }

    /// <summary>Opens the ground cover's shader for a set that stacks shells, and each shell layer's relief, once the art set is admitted.</summary>
    private void UseShellArt(DaggerfallArtReplacements art)
    {
        if (art.GroundCover is { } cover && shellShader is null && art.Models.Values.Any(model => model.Slots.Any(slot => slot.Shell is not null)))
        {
            shellShader = appearance.OpenShader(content, cover.Shader.Path, cover.Shader.Sha256);
            ownedResources.Add(shellShader);
        }
        foreach (ShellMaterial layer in shellMaterials.Values) AttachShellRelief(layer);
    }

    private void AttachShellRelief(ShellMaterial layer)
    {
        if (surfaceArt is null) return;
        for (int season = 0; season < layer.Relief.Length; season++)
        {
            if (surfaceArt.Relief(layer.Surface.Paths[season]) is not { } relief) continue;
            if (!reliefImages.TryGetValue(relief.File.Path, out RenderResource? image))
            {
                image = appearance.OpenTexture(content, relief.File.Path, relief.File.Sha256, TextureFilter.Nearest, TextureWrap.Repeat, admittingOffUpdate).Handle;
                locationResources.Add(image);
                reliefImages.Add(relief.File.Path, image);
            }
            layer.Relief[season] = (image, relief);
        }
    }

    /// <summary>
    /// Draws each shell layer in the current season and art set: in refreshed art, the relief of the texture its surface
    /// draws, cut at its height (the core whole) and shaded from the relief's root colour at the core to its own at the top;
    /// otherwise, or where the set has no relief for it, that surface whole.
    /// </summary>
    private void ApplyShells(int season, bool refreshed)
    {
        foreach (ShellMaterial layer in shellMaterials.Values)
        {
            (RenderResource Texture, DaggerfallGroundRelief? Relief) wanted = refreshed && shellShader is not null && layer.Relief[season] is { } relief
                ? (relief.Image, relief.Art)
                : ((refreshed && layer.Surface.Refreshed is { } surface ? surface : layer.Surface.Classic)[season], null);
            if (layer.Current == wanted) continue;
            appearance.UpdateMaterial(new MaterialUpdateRequest(layer.Material, ShellRequest(wanted.Texture, wanted.Relief, layer.Shell)));
            layer.Current = wanted;
        }
    }

    private MaterialRequest ShellRequest(RenderResource texture, DaggerfallGroundRelief? relief, DaggerfallArtShell shell)
    {
        MaterialRequest request = SurfaceRequest(texture, null);
        if (relief is null) return request;
        Vector3 shade = Vector3.Lerp(relief.Shade, Vector3.One, shell.Height);
        // A remodel's shells stand still: the shader's wind slides a layer across the ground, which on a wall would slide
        // the layers through each other. Only the top half of a stack glints, as the ground's does.
        return request with
        {
            Color = new Color(shade.X, shade.Y, shade.Z, 1F),
            AlphaMode = shell.Layer == 0 ? MaterialAlphaMode.Opaque : MaterialAlphaMode.Mask,
            AlphaCutoff = shell.Layer == 0 ? request.AlphaCutoff : ShellAlphaCutoff,
            Shader = new MaterialShader(new RenderResourceReference(shellShader!), new Vector4(relief.Width, 0, 0, shell.Height),
                new Vector4(shell.Cutoff, 0, shell.Layer >= shell.Layers / 2 ? relief.Glints : 0, relief.Width), default, default),
        };
    }

    /// <summary>
    /// Moves every remodel strip to the frame its rate reaches after this update's admitted steps, updating only a material
    /// whose frame changes, and only while refreshed art, which draws the remodels, is drawn.
    /// </summary>
    private void StepFlipbooks(ProductUpdateFacts update)
    {
        flipbookSeconds += update.FixedDeltaSeconds * update.AdmittedStepCount;
        if (!drawRefreshedEffects()) return;
        foreach (RemodelFlipbook book in flipbooks)
        {
            int frame = (int)(flipbookSeconds * book.Flipbook.FramesPerSecond % book.Flipbook.Frames);
            if (frame == book.Frame) continue;
            book.Frame = frame;
            appearance.UpdateMaterial(new MaterialUpdateRequest(book.Material, book.Request with { TextureOffset = new Vector2((float)frame / book.Flipbook.Frames, 0F) }));
        }
    }

    /// <summary>What a placed model shows now: its remodel while refreshed art is drawn and it has one, else the classic.</summary>
    private static Appearance Drawn(Appearance classic, Appearance? remodel, bool refreshed) => refreshed && remodel is not null ? remodel : classic;
}
