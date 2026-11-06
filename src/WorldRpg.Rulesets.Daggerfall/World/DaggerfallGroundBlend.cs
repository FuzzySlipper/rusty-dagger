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

    /// <summary>How much of a swamp's soil the noise turns to its lush third ground, from 0 to 1.</summary>
    internal static float Lush(DaggerfallGroundTuning tuning, float noise) => Smooth(tuning.StoneFrom, tuning.StoneFull, noise);

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

/// <summary>
/// One ground archive's layers as Engine terrain layer materials. A vertex weighs four layers, so ground picks the
/// material that holds what it needs: open ground, a swamp's lush ground, a location's paving and ruts, or a shore's water.
/// </summary>
internal sealed class DaggerfallGroundLayers : IDisposable
{
    /// <summary>The side of a classic ground texture in texels.</summary>
    internal const int ClassicSide = 64;
    private const uint Version = 1;
    private const int Grass = 0, Dirt = 1, Rock = 2, Snow = 3;
    /// <summary>The records of a location's flagstones and rutted road, which only its authored ground wears.</summary>
    private const int PavingRecord = 46, RutsRecord = 54;
    private static readonly Color White = new(1, 1, 1, 1);
    private readonly AuthoredCatalog _catalog;
    private readonly List<RenderResource> _textures = [];
    private readonly List<Material> _materials = [];
    private readonly List<Material> _layered = [];

    /// <param name="surfaces">An archive's <see cref="Records"/>, in that order.</param>
    internal DaggerfallGroundLayers(IAuthoredContentService authored, IGraphicsService graphics, IContentService content, int archive,
        IReadOnlyList<DaggerfallArtSurface> surfaces, DaggerfallGroundBlendSettings settings)
    {
        NoiseStone = BaresNoiseStone(archive);
        HasLush = archive / 100 == 4;
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

            int last = surfaces.Count - 1;
            Material Layered(params int[] upper)
            {
                Material material = graphics.CreateTerrainLayerMaterial(new TerrainLayerMaterialRequest(_materials[Grass], upper.Select(layer => _materials[layer]).ToArray(), settings.Contrast));
                _layered.Add(material);
                return material;
            }

            Open = Layered(Dirt, Rock, Snow);
            Lush = HasLush ? Layered(Dirt, Rock, Snow + 1) : Open;
            Town = Layered(Dirt, last - 2, last - 1);
            Shore = Layered(Dirt, Rock, last);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Open ground: grass, dirt, rock and snow.</summary>
    internal Material Open { get; } = null!;

    /// <summary>A swamp's ground below the snow: grass, dirt, rock and lush ground.</summary>
    internal Material Lush { get; } = null!;

    /// <summary>A location's own ground: grass, dirt, paving and ruts.</summary>
    internal Material Town { get; } = null!;

    /// <summary>Ground at the water: grass, dirt, rock and water.</summary>
    internal Material Shore { get; } = null!;

    /// <summary>Whether the noise bares this archive's stone, or only the slope does.</summary>
    internal bool NoiseStone { get; }

    /// <summary>Whether this archive has a lush ground, drawn wherever no snow reaches.</summary>
    internal bool HasLush { get; }

    /// <summary>
    /// The archive and record of each layer of a ground archive: its grass and dirt, its rock, and the climate's snow,
    /// which is the grass of its winter archive, ending in 3. A desert has no winter archive, so its heights go to rock.
    /// The swamp's own third record is lush ground. Then a location's paving and ruts, and water.
    /// </summary>
    internal static (int Archive, int Record)[] Records(int archive)
    {
        int climate = archive / 100 * 100;
        (int, int)[] shared = [(archive, 2), (archive, 1), (RockArchive(archive), 3), climate == 0 ? (archive, 3) : (climate + 3, 2)];
        return [.. shared, .. climate == 400 ? [(archive, 3)] : Array.Empty<(int, int)>(), (archive, PavingRecord), (archive, RutsRecord), (archive, 0)];
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
        foreach (Material material in _layered) material.Dispose();
        foreach (Material material in _materials) material.Dispose();
        foreach (RenderResource texture in _textures) texture.Dispose();
        _catalog.Dispose();
    }
}

/// <summary>
/// What a location's authored ground is at each corner of a tile. Every record of a ground archive is a pattern of six
/// kinds, so a location's tiles read as ground at its vertices.
/// </summary>
internal static class DaggerfallGroundKinds
{
    internal const byte Water = 0, Dirt = 1, Grass = 2, Stone = 3, Paving = 4, Ruts = 5;
    private const byte W = Water, D = Dirt, G = Grass, S = Stone, P = Paving, R = Ruts;

    /// <summary>Each record's kinds at the image's corners: upper left, upper right, lower left, lower right.</summary>
    private static readonly byte[][] Corners = Table();

    private static byte[][] Table()
    {
        byte[][] table = new byte[64][];
        void Set(int record, byte ul, byte ur, byte ll, byte lr) => table[record] = [ul, ur, ll, lr];
        void All(byte kind, params int[] records) { foreach (int record in records) Set(record, kind, kind, kind, kind); }
        void Run(int first, byte a, byte b) { Set(first, a, b, b, b); Set(first + 1, a, b, a, b); Set(first + 2, a, a, a, b); }
        All(W, 0, 4, 19, 29); All(D, 1, 8, 9, 24); All(G, 2, 13, 14, 23); All(S, 3, 18, 28, 33); All(P, 46); All(R, 54);
        Run(5, D, W); Run(10, G, D); Run(15, S, G); Run(20, G, W); Run(25, S, D); Run(30, S, W);
        Set(47, D, P, P, P); Set(55, G, P, P, P);
        Set(34, D, W, G, W); Set(35, G, W, S, W); Set(36, D, W, S, W); Set(37, D, G, D, W); Set(38, D, S, D, W); Set(39, D, S, D, G);
        Set(40, G, D, G, W); Set(41, G, S, G, W); Set(42, G, S, G, D); Set(43, S, D, S, W); Set(44, S, G, S, W); Set(45, S, G, S, D);
        Set(48, D, W, W, D); Set(49, G, W, W, G); Set(50, S, W, W, S); Set(51, G, D, D, G); Set(52, S, D, D, S); Set(53, S, G, G, S);
        return table;
    }

    /// <summary>Whether a tile is a paving edge, whose art divides it along a diagonal.</summary>
    internal static bool CutsDiagonally(byte bitfield) => (bitfield & 0x3F) is 47 or 55;

    /// <summary>
    /// The kind at a corner of a tile. The terrain lookup fixes the frame: a record's image has x running right and y running
    /// up, rotation is a quarter turn anticlockwise and a flip a half turn.
    /// </summary>
    /// <param name="bitfield">The tile's record, rotation and flip.</param>
    /// <param name="u">0 at the tile's first column of vertices, 1 at its next.</param>
    /// <param name="v">0 at its first row of vertices, 1 at its next.</param>
    internal static byte At(byte bitfield, int u, int v)
    {
        byte[] corners = Corners[bitfield & 0x3F] ?? Corners[1];
        (byte ul, byte ur, byte ll, byte lr) = (corners[0], corners[1], corners[2], corners[3]);
        if ((bitfield & 0x40) != 0) (ul, ur, lr, ll) = (ur, lr, ll, ul);
        if ((bitfield & 0x80) != 0) (ul, ur, ll, lr) = (lr, ll, ur, ul);
        return v == 0 ? u == 0 ? ll : lr : u == 0 ? ul : ur;
    }
}
