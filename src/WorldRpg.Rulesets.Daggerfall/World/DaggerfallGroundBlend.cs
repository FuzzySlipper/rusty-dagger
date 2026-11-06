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
    /// <param name="noise">The classic ground noise at the vertex, from 0 to 1.</param>
    /// <param name="slope">The ground's slope there, rise over run.</param>
    /// <param name="heightMetres">The ground's height there.</param>
    /// <param name="noiseStone">Whether the noise bares stone as well as the slope: ground wearing borrowed rock bares it only by slope.</param>
    internal static Vector4 Weights(DaggerfallGroundTuning tuning, float noise, float slope, float heightMetres, bool noiseStone = true)
    {
        float grass = Smooth(tuning.DirtToGrassFrom, tuning.DirtToGrassFull, noise);
        float rock = Smooth(tuning.RockFromSlope, tuning.RockFullSlope, slope);
        float stone = noiseStone ? Math.Max(Smooth(tuning.StoneFrom, tuning.StoneFull, noise), rock) : rock;
        float snow = Snow(tuning, heightMetres) * (1 - (stone * (1 - tuning.RockHoldsSnow)));
        float soil = (1 - stone) * (1 - snow);
        return new(soil * grass, soil * (1 - grass), stone * (1 - snow), snow);
    }

    /// <summary>How much of the ground at this height lies under snow, from 0 to 1.</summary>
    internal static float Snow(DaggerfallGroundTuning tuning, float heightMetres) =>
        Smooth(tuning.SnowFromMetres, tuning.SnowFullMetres, heightMetres);

    /// <summary>Whether a texture repeating this often fills a map pixel a whole number of times, so it runs on from cell to cell.</summary>
    internal static bool FillsCell(float repeatMetres)
    {
        if (!float.IsFinite(repeatMetres) || repeatMetres <= 0) return false;
        double repeats = DaggerfallExteriorCellResidency.CellSize / repeatMetres;
        return Math.Abs(repeats - Math.Round(repeats)) < .001;
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
    private static readonly Color White = new(1, 1, 1, 1);
    private readonly AuthoredCatalog _catalog;
    private readonly List<RenderResource> _textures = [];
    private readonly List<Material> _materials = [];

    /// <param name="surfaces">The grass, dirt, stone and snow textures, in layer order.</param>
    internal DaggerfallGroundLayers(IAuthoredContentService authored, IGraphicsService graphics, IContentService content, int archive,
        IReadOnlyList<DaggerfallArtSurface> surfaces, DaggerfallGroundBlendSettings settings)
    {
        NoiseStone = BaresNoiseStone(archive);
        List<AuthoredCatalogEntryInput> entries = [];
        List<AuthoredCatalogDependencyInput> dependencies = [];
        List<AuthoredMaterialInput> definitions = [];
        List<AuthoredTextureInput> textures = [];
        List<AuthoredVoxelSurfaceInput> voxelSurfaces = [];
        for (int layer = 0; layer < surfaces.Count; layer++)
        {
            string name = Name(archive, layer, settings), texture = $"texture/ground/{name}", material = $"material/ground/{name}";
            ContentSha256 digest = surfaces[layer].File.Sha256;
            string hash = $"{digest.Word0:x16}{digest.Word1:x16}{digest.Word2:x16}{digest.Word3:x16}";
            entries.Add(new(texture, Version, true, hash, true, surfaces[layer].File.Path, true, name));
            entries.Add(new(material, Version, false, string.Empty, false, string.Empty, true, name));
            dependencies.Add(new(material, texture, AssetVersionRequirementKind.Exact, Version, true, hash));
            textures.Add(new(texture, (uint)surfaces[layer].Width, (uint)surfaces[layer].Height, AuthoredTextureFilter.Nearest, AuthoredTextureWrap.Repeat));
            definitions.Add(new(material, true, true, true, AuthoredStructuralClass.Solid, White, true, texture, AssetVersionRequirementKind.Exact,
                Version, true, hash, 1, White, new Color(0, 0, 0, 1), 0, AuthoredUvStrategy.Planar));
            voxelSurfaces.Add(new(material, Version, AuthoredVoxelSurfaceMappingKind.Repeat, texture, AssetVersionRequirementKind.Exact, Version, true, hash,
                string.Empty, AssetVersionRequirementKind.Any, 0, false, string.Empty, string.Empty,
                settings.RepeatMetres, settings.RepeatMetres, 0, 0, AuthoredVoxelAlphaModeKind.Opaque, 0));
        }

        _catalog = authored.AdmitCatalogPayload(new(entries.ToArray(), dependencies.ToArray(), definitions.ToArray(), textures.ToArray(),
            ReadOnlyMemory<AuthoredVoxelAtlasInput>.Empty, ReadOnlyMemory<AuthoredAtlasRegionInput>.Empty, voxelSurfaces.ToArray()));
        try
        {
            for (int layer = 0; layer < surfaces.Count; layer++)
            {
                RenderResource texture = graphics.OpenTexture(content, surfaces[layer].File.Path, surfaces[layer].File.Sha256, TextureFilter.Nearest, TextureWrap.Repeat).Handle;
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

    /// <summary>Whether the noise bares this archive's stone, or only the slope does.</summary>
    internal bool NoiseStone { get; }

    /// <summary>
    /// The archive and record of each layer of a ground archive: its grass and dirt, its rock, and the climate's snow,
    /// which is the grass of its winter archive, ending in 3. A desert has no winter archive, so its heights go to rock.
    /// </summary>
    internal static (int Archive, int Record)[] Records(int archive)
    {
        int climate = archive / 100 * 100;
        return [(archive, 2), (archive, 1), (RockArchive(archive), 3), climate == 0 ? (archive, 3) : (climate + 3, 2)];
    }

    /// <summary>
    /// The archive whose third record is this ground's bare rock. The temperate and mountain winters are snow on every
    /// record, so they wear the summer's; the swamp's third record is lush grass, so it wears the mountains'.
    /// </summary>
    private static int RockArchive(int archive) => archive switch
    {
        103 or 402 or 403 => 102,
        303 => 302,
        404 => 104,
        _ => archive,
    };

    /// <summary>The noise bares only an archive's own rock: a borrowed one shows only where the slope has earned it.</summary>
    private static bool BaresNoiseStone(int archive) => RockArchive(archive) == archive;

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
