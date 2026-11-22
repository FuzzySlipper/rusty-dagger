using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.VegetationTiles;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>The appearances used by both wilderness placement and the isolated flora review.</summary>
internal sealed class DaggerfallFloraArt(IEngineContext engine, DaggerfallSpritesTuning sprites)
{
    /// <summary>How far a tree's leaf cards circle in the wind at unit strength, times their vertex alpha, as rusty-craftsurvive's do.</summary>
    private const float LeafFlutter = .12f;

    internal DaggerfallFloraAsset Sprite(DaggerfallFloraLook look)
    {
        RenderResource image = engine.Graphics.OpenTexture(engine.Content, look.Image.Path, look.Image.Sha256, TextureFilter.NearestMipmapped).Handle;
        try
        {
            VegetationSprite sprite = new(engine, new RenderResourceReference(image), look.UvMin, look.UvMax, look.Size, look.Pivot,
                sprites.Flora(DaggerfallSiteAppearance.LitSprite), sprites.Nature);
            return new(sprite.Look, [sprite, image], look.Size.Y, false);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A record's nature mesh (<see cref="DaggerfallTreeMesh"/>): bark that bends as the sprites do, leaves that flutter too,
    /// and stone that stands still; a winter tree's leaves and a winter rock's stone carry snow where they face up.
    /// </summary>
    internal DaggerfallFloraAsset Tree(DaggerfallTreeArt tree, DaggerfallFloraLook look)
    {
        List<IDisposable> owned = [];
        try
        {
            Material? barkMaterial = null;
            if (tree.Bark is { } barkArt)
            {
                RenderResource bark = engine.Graphics.OpenTexture(engine.Content, barkArt.Path, barkArt.Sha256, TextureFilter.Nearest, TextureWrap.Repeat).Handle;
                owned.Add(bark);
                barkMaterial = engine.Graphics.CreateMaterial(new MaterialRequest(new Color(1, 1, 1, 1), new RenderResourceReference(bark), .95f,
                    new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with { WindBend = sprites.FloraWindBend });
                owned.Add(barkMaterial);
            }
            Material? stoneMaterial = null;
            if (tree.Stone is { } stoneArt)
            {
                RenderResource stone = engine.Graphics.OpenTexture(engine.Content, stoneArt.Path, stoneArt.Sha256, TextureFilter.Nearest, TextureWrap.Repeat).Handle;
                owned.Add(stone);
                stoneMaterial = engine.Graphics.CreateMaterial(Snowed(new MaterialRequest(new Color(1, 1, 1, 1), new RenderResourceReference(stone), .95f,
                    new Color(1, 1, 1, 1), Vector3.Zero, 0, false), tree, look));
                owned.Add(stoneMaterial);
            }
            // A bare tree has no leaves.
            Material? leafMaterial = null;
            if (tree.Leaves is { } leafArt)
            {
                RenderResource leaves = engine.Graphics.OpenTexture(engine.Content, leafArt.Path, leafArt.Sha256, TextureFilter.NearestMipmapped).Handle;
                owned.Add(leaves);
                leafMaterial = engine.Graphics.CreateMaterial(Snowed(new MaterialRequest(new Color(1, 1, 1, 1), new RenderResourceReference(leaves), .9f,
                    new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with
                {
                    AlphaMode = MaterialAlphaMode.Mask, AlphaCutoff = .5f, WindBend = sprites.FloraWindBend, WindFlutter = LeafFlutter,
                }, tree, look));
                owned.Add(leafMaterial);
            }
            (MeshResourceCreateRequest read, DaggerfallNatureBody? body) = DaggerfallTreeMesh.Read(engine.Content, tree, barkMaterial, leafMaterial, stoneMaterial);
            MeshResource mesh = engine.Graphics.CreateMeshResource(read);
            owned.Add(mesh);
            Appearance appearance = engine.Graphics.CreateMeshAppearance(mesh);
            owned.Add(appearance);
            // Held last-made first, so the appearance goes before its mesh and the mesh before its materials.
            owned.Reverse();
            return new(appearance, [.. owned], look.Size.Y, true, body);
        }
        catch
        {
            owned.Reverse();
            foreach (IDisposable held in owned) held.Dispose();
            throw;
        }
    }

    /// <summary>A winter tree's leaves or a winter rock's stone with snow where they face up, as its sprite paints it.</summary>
    private MaterialRequest Snowed(MaterialRequest request, DaggerfallTreeArt tree, DaggerfallFloraLook look) =>
        tree.Snow is { } snow && look.TreeSnow is { } shader
            ? request with
            {
                Shader = new MaterialShader(new RenderResourceReference(SnowShader(shader)), new Vector4(snow.Share, snow.Texels, SnowClumpTexels, 0),
                    new Vector4(snow.Colour, 0), default, default, default, default, 0),
            }
            : request;

    /// <summary>The snow shader, opened the first time a winter tree grows and held while the field is.</summary>
    private RenderResource SnowShader(DaggerfallArtFile shader) =>
        _snowShader ??= engine.Graphics.OpenShader(engine.Content, shader.Path, shader.Sha256);

    private RenderResource? _snowShader;
    /// <summary>How many of the leaf texture's texels a drift of snow spans.</summary>
    private const float SnowClumpTexels = 12;

    internal void ReleaseSnowShader()
    {
        _snowShader?.Dispose();
        _snowShader = null;
    }
}

/// <summary>
/// A record as a layer grows it: what draws, what it holds, its height, which sets its reach, whether it is a mesh, and
/// the body a mesh stands as near the player, if any.
/// </summary>
internal sealed record DaggerfallFloraAsset(Appearance Look, IDisposable[] Owned, float Height, bool Mesh, DaggerfallNatureBody? Body = null) : IDisposable
{
    public void Dispose()
    {
        foreach (IDisposable held in Owned) held.Dispose();
    }
}
