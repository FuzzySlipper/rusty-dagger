using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>How blended ground draws.</summary>
/// <param name="Contrast">How hard layers meet: 1 blends softly, and higher narrows each transition toward a pixel edge.</param>
/// <param name="RepeatMetres">How far one texture spans before it repeats.</param>
/// <param name="TriplanarSharpness">Above 0, textures project from three planes, so steep faces don't stretch.</param>
internal readonly record struct DaggerfallGroundBlendSettings(float Contrast, float RepeatMetres, float TriplanarSharpness);

/// <summary>
/// What wilderness ground is made of at a vertex, as weights of its four layers: grass, dirt, stone and snow. The classic
/// picks a tile from noise alone; here the noise still lays out grass and dirt, while real slope bares the rock and real
/// height brings the snow.
/// </summary>
internal static class DaggerfallGroundBlend
{
    private const float DirtToGrassFrom = .42F, DirtToGrassFull = .58F, StoneFrom = .88F, StoneFull = 1F;
    private const float RockFromSlope = .5F, RockFullSlope = .9F, SnowFromMetres = 1_500, SnowFullMetres = 2_200, RockHoldsSnow = .3F;

    /// <param name="noise">The classic ground noise at the vertex, from 0 to 1.</param>
    /// <param name="slope">The ground's slope there, rise over run.</param>
    /// <param name="heightMetres">The ground's height there.</param>
    internal static Vector4 Weights(float noise, float slope, float heightMetres)
    {
        float grass = Smooth(DirtToGrassFrom, DirtToGrassFull, noise);
        float stone = Math.Max(Smooth(StoneFrom, StoneFull, noise), Smooth(RockFromSlope, RockFullSlope, slope));
        float snow = Smooth(SnowFromMetres, SnowFullMetres, heightMetres) * (1 - (stone * (1 - RockHoldsSnow)));
        float soil = (1 - stone) * (1 - snow);
        return new(soil * grass, soil * (1 - grass), stone * (1 - snow), snow);
    }

    private static float Smooth(float from, float full, float value)
    {
        float t = Math.Clamp((value - from) / (full - from), 0, 1);
        return t * t * (3 - (2 * t));
    }
}

/// <summary>One ground archive's four layers as one Engine terrain layer material.</summary>
internal sealed class DaggerfallGroundLayers : IDisposable
{
    private const uint Version = 1;
    private const uint TextureSize = 128;
    private static readonly Color White = new(1, 1, 1, 1);
    private readonly AuthoredCatalog _catalog;
    private readonly List<RenderResource> _textures = [];
    private readonly List<Material> _materials = [];

    /// <param name="maps">The grass, dirt, stone and snow textures, in layer order.</param>
    internal DaggerfallGroundLayers(IAuthoredContentService authored, IGraphicsService graphics, IContentService content, int archive,
        IReadOnlyList<(string Path, ContentSha256 Hash)> maps, DaggerfallGroundBlendSettings settings)
    {
        List<AuthoredCatalogEntryInput> entries = [];
        List<AuthoredCatalogDependencyInput> dependencies = [];
        List<AuthoredMaterialInput> definitions = [];
        List<AuthoredTextureInput> textures = [];
        List<AuthoredVoxelSurfaceInput> surfaces = [];
        for (int layer = 0; layer < maps.Count; layer++)
        {
            string name = Name(archive, layer, settings), texture = $"texture/ground/{name}", material = $"material/ground/{name}";
            ContentSha256 digest = maps[layer].Hash;
            string hash = $"{digest.Word0:x16}{digest.Word1:x16}{digest.Word2:x16}{digest.Word3:x16}";
            entries.Add(new(texture, Version, true, hash, true, maps[layer].Path, true, name));
            entries.Add(new(material, Version, false, string.Empty, false, string.Empty, true, name));
            dependencies.Add(new(material, texture, AssetVersionRequirementKind.Exact, Version, true, hash));
            textures.Add(new(texture, TextureSize, TextureSize, AuthoredTextureFilter.Nearest, AuthoredTextureWrap.Repeat));
            definitions.Add(new(material, true, true, true, AuthoredStructuralClass.Solid, White, true, texture, AssetVersionRequirementKind.Exact,
                Version, true, hash, 1, White, new Color(0, 0, 0, 1), 0, AuthoredUvStrategy.Planar));
            surfaces.Add(new(material, Version, AuthoredVoxelSurfaceMappingKind.Repeat, texture, AssetVersionRequirementKind.Exact, Version, true, hash,
                string.Empty, AssetVersionRequirementKind.Any, 0, false, string.Empty, string.Empty,
                settings.RepeatMetres, settings.RepeatMetres, 0, 0, AuthoredVoxelAlphaModeKind.Opaque, 0));
        }

        _catalog = authored.AdmitCatalogPayload(new(entries.ToArray(), dependencies.ToArray(), definitions.ToArray(), textures.ToArray(),
            ReadOnlyMemory<AuthoredVoxelAtlasInput>.Empty, ReadOnlyMemory<AuthoredAtlasRegionInput>.Empty, surfaces.ToArray()));
        try
        {
            for (int layer = 0; layer < maps.Count; layer++)
            {
                RenderResource texture = graphics.OpenTexture(content, maps[layer].Path, maps[layer].Hash, TextureFilter.Nearest, TextureWrap.Repeat).Handle;
                _textures.Add(texture);
                _materials.Add(graphics.CreateAuthoredMaterial(new AuthoredMaterialAppearanceRequest(_catalog,
                    $"material/ground/{Name(archive, layer, settings)}", texture) { TriplanarSharpness = settings.TriplanarSharpness }));
            }

            Layered = graphics.CreateTerrainLayerMaterial(new TerrainLayerMaterialRequest(_materials[0], _materials.Skip(1).ToArray(), settings.Contrast));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal Material? Layered { get; }

    /// <summary>An asset id is kebab-case, so the repeat goes in as whole centimetres.</summary>
    private static string Name(int archive, int layer, DaggerfallGroundBlendSettings settings) =>
        $"archive-{archive}-layer-{layer}-repeat-{(int)(settings.RepeatMetres * 100)}";

    public void Dispose()
    {
        Layered?.Dispose();
        foreach (Material material in _materials) material.Dispose();
        foreach (RenderResource texture in _textures) texture.Dispose();
        _catalog.Dispose();
    }
}
