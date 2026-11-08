using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Refreshed grass over open ground near the player, two ways at once:
/// <list type="bullet">
/// <item><b>Shells:</b> a stack of layers over near ground about the player, on near ground's own finest vertices and
/// diagonals, each cut out where the shell texture's blade height falls below its layer's. A vertex wears its ground
/// archive's grass colour, darker toward the roots, and the grass's share of the ground's look thins it, so the stack grows
/// where the ground reads as grass and nowhere its archive has none. A shader leans the higher layers in the wind by whole
/// texels.</item>
/// <item><b>Tufts:</b> three crossed quads of pixel grass, a flora layer for each climate's nature archive in its grass
/// colour, their tips fluttering in the wind.</item>
/// </list>
/// Every other layer of open ground (dirt, rock, sand, snow, a swamp's lush ground) rises in relief by the same shells: a
/// stack of its own texture, laid on the ground's texels exactly, cut out by each texel's height, thinned by the layer's
/// weight. The ground itself seems to stand up in pixels: clods, pebbles, ripples, drifts.
/// The art set's ground cover (<see cref="DaggerfallGroundCoverArt"/>) holds the images, the shader, each archive's grass
/// and each layer's relief.
/// </summary>
internal sealed class DaggerfallGroundCover : IDisposable
{
    /// <summary>Shells stand on near ground's finest vertices.</summary>
    private const double Cell = DaggerfallHorizonGround.FinestNearCellMetres;
    /// <summary>The shells are made again once the player is this many cells from their centre.</summary>
    private const int Rebuild = 2;
    /// <summary>The lowest layer's brightness, rising to full at the top: the blades darken toward their roots.</summary>
    private const float Floor = .55f;
    /// <summary>Relief's lowest layer's brightness: the ground's own colours, a little shaded at their foot.</summary>
    private const float ReliefFloor = .6f;
    /// <summary>How fast the shells' gusts rise and fall.</summary>
    private const float GustRate = 1.3f;
    /// <summary>The texture's place on the map wraps this many metres, so its coordinates keep their precision.</summary>
    private const double TextureWrapMetres = 4096;
    /// <summary>Tufts take flora layers past every nature record's, one for each nature archive.</summary>
    private const uint TuftLayerBase = 0x1000;
    /// <summary>A tuft's tint varies this much either side of its grass colour, so a lawn is not one green.</summary>
    private const float TuftTintSpread = .15f;
    private const float TuftFlutter = .05f, TuftBend = .03f;

    private readonly IEngineContext _engine;
    private readonly DaggerfallHorizonGround _ground;
    private readonly DaggerfallFloraField _flora;
    private readonly DaggerfallGroundCoverArt _art;
    private readonly DaggerfallGroundCoverTuning _tuning;
    private readonly IReadOnlyDictionary<int, int> _tuftGrounds;
    private readonly int _cells;
    private readonly RenderResource _shellImage;
    private readonly RenderResource _shader;
    private readonly Material[] _shellMaterials;
    private readonly float _groundRepeat;
    private readonly Dictionary<(int Archive, int Record), (RenderResource Image, Material[] Layers)> _relief = [];
    private readonly RenderResource _tuftImage;
    private readonly Material _tuftMaterial;
    private readonly MeshResource _tuftMesh;
    private readonly Appearance _tuftLook;
    private readonly List<(MeshResource Mesh, Appearance Look)> _retired = [];
    private (MeshResource Mesh, Appearance Look, Vector3 At)? _shells;
    private (long X, long Z, (int, int) Frame, bool Winter)? _built;
    private bool _tufting;

    /// <param name="groundRepeat">How far a ground texture spans before it repeats, which relief matches.</param>
    /// <param name="tuftGrounds">Each summer nature archive's ground archive, whose grass its tufts wear (<see cref="TuftGrounds"/>).</param>
    internal DaggerfallGroundCover(IEngineContext engine, DaggerfallHorizonGround ground, DaggerfallFloraField flora, DaggerfallGroundCoverArt art,
        DaggerfallGroundCoverTuning tuning, IReadOnlyDictionary<int, int> tuftGrounds, float groundRepeat)
    {
        _groundRepeat = groundRepeat;
        _engine = engine;
        _ground = ground;
        _flora = flora;
        _art = art;
        _tuning = tuning;
        _tuftGrounds = tuftGrounds;
        _cells = (int)Math.Ceiling(tuning.ShellReach / Cell);
        _shellImage = engine.Graphics.OpenTexture(engine.Content, art.Shell.Path, art.Shell.Sha256, TextureFilter.Nearest, TextureWrap.Repeat).Handle;
        _shader = engine.Graphics.OpenShader(engine.Content, art.Shader.Path, art.Shader.Sha256);
        _shellMaterials = [.. Enumerable.Range(0, tuning.ShellLayers).Select(layer => engine.Graphics.CreateMaterial(
            new MaterialRequest(new Color(1, 1, 1, 1), new RenderResourceReference(_shellImage), .9f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with
            {
                AlphaMode = MaterialAlphaMode.Mask,
                AlphaCutoff = (layer + 1f) / (tuning.ShellLayers + 1f),
                SinksUnderSnow = true,
                Shader = new MaterialShader(new RenderResourceReference(_shader),
                    new Vector4(art.ShellTexelsPerMetre, tuning.ShellSway, GustRate, layer / (tuning.ShellLayers - 1f)),
                    default, default, default, default, default, tuning.ShellSway),
            }))];
        _tuftImage = engine.Graphics.OpenTexture(engine.Content, art.Tuft.Path, art.Tuft.Sha256, TextureFilter.Nearest).Handle;
        _tuftMaterial = engine.Graphics.CreateMaterial(
            new MaterialRequest(new Color(1, 1, 1, 1), new RenderResourceReference(_tuftImage), .9f, new Color(1, 1, 1, 1), Vector3.Zero, 0, true) with
            {
                AlphaMode = MaterialAlphaMode.Mask,
                AlphaCutoff = .5f,
                WindFlutter = TuftFlutter,
                WindBend = TuftBend,
                SinksUnderSnow = true,
            });
        _tuftMesh = engine.Graphics.CreateMeshResource(Tuft(_tuftMaterial, tuning.TuftWidth, tuning.TuftHeight));
        _tuftLook = engine.Graphics.CreateMeshAppearance(_tuftMesh);
    }

    /// <summary>Each summer nature archive's ground archive, from the climates the grid names, leaving out the open sea's.</summary>
    internal static IReadOnlyDictionary<int, int> TuftGrounds(IEnumerable<DaggerfallClimateValueDefinition> climates)
    {
        Dictionary<int, int> grounds = [];
        foreach (DaggerfallClimateValueDefinition climate in climates.Where(climate => !DaggerfallExteriorEnvironment.IsOceanClimate(climate.Value)))
            grounds.TryAdd(DaggerfallExteriorEnvironment.NatureTextureArchive(climate.Value, DaggerfallExteriorSeason.Summer),
                DaggerfallExteriorEnvironment.GroundTextureArchive(climate.Value, DaggerfallExteriorSeason.Summer));
        return grounds;
    }

    /// <summary>A nature archive's tuft layer in the flora field.</summary>
    internal static uint TuftLayer(int natureArchive) => TuftLayerBase + (uint)(natureArchive - DaggerfallExteriorEnvironment.FirstNatureArchive);

    private bool _grass = true, _reliefShown = true;

    /// <summary>Whether tufts draw, which a developer may change for a run to compare them.</summary>
    internal bool Tufts { get; private set; } = true;

    /// <summary>Draws only these kinds of cover, which a developer may choose for a run to compare them.</summary>
    internal void Show(bool grass, bool relief, bool tufts)
    {
        (_grass, _reliefShown, Tufts) = (grass, relief, tufts);
        Retire();
    }

    /// <summary>Grows the cover about a player at map point (x, z) in near ground's frame at <paramref name="origin"/>.</summary>
    internal void Show(DaggerfallExteriorWorldOrigin origin, double x, double z, bool winter)
    {
        if (Tufts != _tufting)
        {
            _tufting = Tufts;
            foreach ((int nature, int ground) in _tuftGrounds)
            {
                // A tuft layer grows on its summer archive's ground; the winter set is another class and grows none.
                if (Tufts && _art.Grass.TryGetValue(ground, out (Vector3 Shells, Vector3 Tufts) grass))
                    _flora.GrowOn(nature, TuftLayer(nature), _tuftLook, _tuning.TuftDensity, _tuning.TuftReach, _tuning.TuftFade,
                        _tuning.TuftScaleMin, _tuning.TuftScaleMax, grass.Tufts * (1 - TuftTintSpread), grass.Tufts * (1 + TuftTintSpread));
                else _flora.StopGrowing(TuftLayer(nature));
            }
        }

        if (!_grass && !_reliefShown)
        {
            Retire();
            return;
        }

        long cellX = (long)Math.Round(x / Cell), cellZ = (long)Math.Round(z / Cell);
        (int, int) frame = (origin.MapPixelX, origin.MapPixelY);
        if (_built is { } built && built.Frame == frame && built.Winter == winter
            && Math.Abs(built.X - cellX) < Rebuild && Math.Abs(built.Z - cellZ) < Rebuild) return;
        Retire();
        double centreX = cellX * Cell, centreZ = cellZ * Cell;
        _built = (cellX, cellZ, frame, winter);
        // Ground with nothing to lift, a town's or the sea's, stands no shells.
        MeshResourceCreateRequest request = ShellMesh(centreX, centreZ, winter);
        if (request.Groups.Length == 0) return;
        MeshResource mesh = _engine.Graphics.CreateMeshResource(request);
        Appearance look = _engine.Graphics.CreateMeshAppearance(mesh);
        double offsetX = origin.MapPixelX * DaggerfallExteriorCellResidency.CellSize, offsetZ = origin.MapPixelY * DaggerfallExteriorCellResidency.CellSize;
        _shells = (mesh, look, new Vector3((float)(centreX - offsetX), 0, (float)(centreZ - offsetZ)) + origin.Compensation);
    }

    /// <summary>Takes the shells down, outdoors left behind; the tufts go with the flora field.</summary>
    internal void Hide() => Retire();

    /// <summary>Adds the shells to the world's snapshot while they stand.</summary>
    internal void AppendFacts(List<AppearanceFact> facts)
    {
        if (_shells is { } shells)
            facts.Add(new AppearanceFact(DaggerfallPresentationObjectIds.GroundCover, false, 0, new Transform(shells.At, Quaternion.Identity, Vector3.One),
                shells.Look, true, RenderLayer.Scene));
    }

    /// <summary>A snapshot without the retired shells was accepted: they are let go.</summary>
    internal void CompleteSnapshot()
    {
        foreach ((MeshResource mesh, Appearance look) in _retired)
        {
            look.Dispose();
            mesh.Dispose();
        }
        _retired.Clear();
    }

    private void Retire()
    {
        if (_shells is { } shells) _retired.Add((shells.Mesh, shells.Look));
        _shells = null;
        _built = null;
    }

    /// <summary>
    /// The shells over a square of near ground's finest vertices about (x, z): a stack for the grass and one for each
    /// layer's relief found there, each layer a lattice lifted above the ground, split on the diagonal near ground splits
    /// on. A vertex carries its layer's brightness (in its archive's grass colour, for grass) and as alpha its layer's
    /// share of the ground there, fading over the last of the reach. A stack draws only the triangles its texels reach.
    /// </summary>
    internal MeshResourceCreateRequest ShellMesh(double centreX, double centreZ, bool winter)
    {
        int cells = _cells, side = (cells * 2) + 1, heightSide = side + 2, count = side * side;
        double cornerX = centreX - (cells * Cell), cornerZ = centreZ - (cells * Cell);
        double[] ground = _ground.WalkedGrid(cornerX, cornerZ, Cell, side);
        double At(int i, int j) => ground[((j + 1) * heightSide) + i + 1];
        Vector3[] up = new Vector3[count];
        Stack grass = new(_shellMaterials, 1, Floor, false, count);
        Dictionary<(int, int), Stack> relief = [];
        for (int j = 0; j < side; j++)
        for (int i = 0; i < side; i++)
        {
            int vertex = (j * side) + i;
            double x = cornerX + (i * Cell), z = cornerZ + (j * Cell), height = At(i, j);
            double slopeX = (At(i + 1, j) - At(i - 1, j)) / (2 * Cell), slopeZ = (At(i, j + 1) - At(i, j - 1)) / (2 * Cell);
            up[vertex] = Vector3.Normalize(new Vector3((float)-slopeX, 1, (float)-slopeZ));
            if (_ground.OpenLook(x, z, height, (float)Math.Sqrt((slopeX * slopeX) + (slopeZ * slopeZ)), winter) is not { } look) continue;
            double away = Math.Sqrt(((i - cells) * (i - cells)) + ((j - cells) * (j - cells))) * Cell;
            float fade = (float)Math.Clamp((_tuning.ShellReach - away) / Math.Max(_tuning.ShellFade, 1e-3), 0, 1);
            (int Archive, int Record)[] records = DaggerfallGroundLayers.Records(look.Archive);
            for (int layer = 0; layer < 4; layer++)
            {
                float weight = look.Weights[layer] * fade;
                if (weight <= 0) continue;
                if (layer == 0 && _art.Grass.TryGetValue(look.Archive, out (Vector3 Shells, Vector3 Tufts) colour))
                {
                    if (!_grass) continue;
                    grass.Cover[vertex] += weight;
                    grass.Tint[vertex] = colour.Shells;
                    continue;
                }

                (int, int) key = records[layer == 3 && look.Lush ? 4 : layer];
                if (!_reliefShown || !_art.Relief.TryGetValue(key, out DaggerfallGroundRelief? art)) continue;
                if (!relief.TryGetValue(key, out Stack? stack))
                    relief[key] = stack = new(ReliefMaterials(key, art), art.Height, ReliefFloor, true, count);
                stack.Cover[vertex] += weight;
                stack.Tint[vertex] = Vector3.One;
            }
        }

        List<Vector3> positions = [], normals = [];
        List<Vector2> uvs = [];
        List<Color> colours = [];
        List<uint> indices = [];
        List<MeshGroup> groups = [];
        List<MeshMaterialBinding> bindings = [];
        double shellRepeat = _art.ShellTexels / _art.ShellTexelsPerMetre;
        foreach (Stack stack in relief.Values.Prepend(grass))
        {
            int layers = stack.Materials.Length;
            for (int layer = 0; layer < layers; layer++)
            {
                // A layer shows a texel whose height beats its cutoff, so where no vertex's share could, it has nothing to draw.
                float cutoff = (layer + 1f) / (_tuning.ShellLayers + 1f) / stack.Height;
                uint first = (uint)positions.Count;
                float brightness = stack.Floor + ((1 - stack.Floor) * layer / Math.Max(layers - 1f, 1));
                int start = indices.Count;
                for (int j = 0; j < cells * 2; j++)
                for (int i = 0; i < cells * 2; i++)
                {
                    int a = (j * side) + i, b = a + 1, c = a + side, d = c + 1;
                    if (Math.Max(Math.Max(stack.Cover[a], stack.Cover[b]), Math.Max(stack.Cover[c], stack.Cover[d])) <= cutoff) continue;
                    // Near ground splits each cell on the diagonal whose ends are nearer in height; so do the shells.
                    uint ua = first + (uint)a, ub = first + (uint)b, uc = first + (uint)c, ud = first + (uint)d;
                    if (Math.Abs(At(i, j) - At(i + 1, j + 1)) <= Math.Abs(At(i + 1, j) - At(i, j + 1))) indices.AddRange([ua, uc, ud, ua, ud, ub]);
                    else indices.AddRange([ua, uc, ub, ub, uc, ud]);
                }

                if (indices.Count == start) break;
                for (int j = 0; j < side; j++)
                for (int i = 0; i < side; i++)
                {
                    int vertex = (j * side) + i;
                    double x = cornerX + (i * Cell), z = cornerZ + (j * Cell);
                    positions.Add(new Vector3((float)((i - cells) * Cell), (float)At(i, j) + _tuning.ShellLift + (layer * _tuning.ShellGap), (float)((j - cells) * Cell)));
                    normals.Add(up[vertex]);
                    // The texture lies on the map, so it runs on unchanged as the shells are made again. Relief lies as
                    // the ground's own texture does, from above: across the map's z, down its x.
                    uvs.Add(stack.OnGround ? new Vector2((float)(Wrap(z) / _groundRepeat), (float)(Wrap(x) / _groundRepeat))
                        : new Vector2((float)(Wrap(x) / shellRepeat), (float)(Wrap(z) / shellRepeat)));
                    Vector3 tint = stack.Tint[vertex] * brightness;
                    colours.Add(new Color(tint.X, tint.Y, tint.Z, Math.Min(stack.Cover[vertex], 1)));
                }

                uint slot = (uint)bindings.Count;
                bindings.Add(new MeshMaterialBinding(slot, stack.Materials[layer]));
                groups.Add(new MeshGroup(slot, (uint)start, (uint)(indices.Count - start)));
            }
        }

        return new MeshResourceCreateRequest(positions.ToArray(), normals.ToArray(), uvs.ToArray(), colours.ToArray(), indices.ToArray(), groups.ToArray(),
            bindings.ToArray(), ReadOnlyMemory<Vector4>.Empty);

        static double Wrap(double metres) => ((metres % TextureWrapMetres) + TextureWrapMetres) % TextureWrapMetres;
    }

    /// <summary>A stack's layers' materials, each vertex's share of it and its tint, its tallest texel and its roots' brightness.</summary>
    private sealed class Stack(Material[] materials, float height, float floor, bool onGround, int count)
    {
        internal Material[] Materials { get; } = materials;
        internal float Height { get; } = height;
        internal float Floor { get; } = floor;
        internal bool OnGround { get; } = onGround;
        internal float[] Cover { get; } = new float[count];
        internal Vector3[] Tint { get; } = new Vector3[count];
    }

    /// <summary>A relief layer's materials, opened the first time it is found: the shell stack's layers up to its tallest texel.</summary>
    private Material[] ReliefMaterials((int Archive, int Record) key, DaggerfallGroundRelief art)
    {
        if (_relief.TryGetValue(key, out (RenderResource Image, Material[] Layers) known)) return known.Layers;
        RenderResource image = _engine.Graphics.OpenTexture(_engine.Content, art.File.Path, art.File.Sha256, TextureFilter.Nearest, TextureWrap.Repeat).Handle;
        int layers = Math.Max(1, Enumerable.Range(0, _tuning.ShellLayers).Count(layer => (layer + 1f) / (_tuning.ShellLayers + 1f) < art.Height));
        float sway = art.Sways ? _tuning.ShellSway : 0;
        Material[] materials = [.. Enumerable.Range(0, layers).Select(layer => _engine.Graphics.CreateMaterial(
            new MaterialRequest(new Color(1, 1, 1, 1), new RenderResourceReference(image), .9f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with
            {
                AlphaMode = MaterialAlphaMode.Mask,
                AlphaCutoff = (layer + 1f) / (_tuning.ShellLayers + 1f),
                SinksUnderSnow = true,
                Shader = new MaterialShader(new RenderResourceReference(_shader),
                    new Vector4(art.Texels / _groundRepeat, sway, GustRate, layer / Math.Max(_tuning.ShellLayers - 1f, 1)),
                    default, default, default, default, default, sway),
            }))];
        _relief[key] = (image, materials);
        return materials;
    }

    /// <summary>Three quads crossed at a third of a turn, standing on their foot, their tips (vertex alpha 1) free to flutter.</summary>
    private static MeshResourceCreateRequest Tuft(Material material, float width, float height)
    {
        List<Vector3> positions = [];
        List<Vector2> uvs = [];
        List<Color> colours = [];
        List<uint> indices = [];
        for (int quad = 0; quad < 3; quad++)
        {
            float angle = quad * MathF.PI / 3;
            Vector3 across = new(MathF.Cos(angle) * width / 2, 0, MathF.Sin(angle) * width / 2);
            uint first = (uint)positions.Count;
            positions.AddRange([-across, across, across + (Vector3.UnitY * height), -across + (Vector3.UnitY * height)]);
            uvs.AddRange([new(0, 1), new(1, 1), new(1, 0), new(0, 0)]);
            colours.AddRange([new(1, 1, 1, 0), new(1, 1, 1, 0), new(1, 1, 1, 1), new(1, 1, 1, 1)]);
            indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }

        // Upright normals light the tuft as the ground under it is lit, whichever way a quad faces.
        Vector3[] normals = [.. positions.Select(_ => Vector3.UnitY)];
        return new MeshResourceCreateRequest(positions.ToArray(), normals, uvs.ToArray(), colours.ToArray(), indices.ToArray(),
            new MeshGroup[] { new(0, 0, (uint)indices.Count) }, new MeshMaterialBinding[] { new(0, material) }, ReadOnlyMemory<Vector4>.Empty);
    }

    /// <summary>Lets go of everything, once the session's last snapshot without the shells has been accepted.</summary>
    public void Dispose()
    {
        Retire();
        CompleteSnapshot();
        _tuftLook.Dispose();
        _tuftMesh.Dispose();
        _tuftMaterial.Dispose();
        _tuftImage.Dispose();
        foreach (Material material in _shellMaterials) material.Dispose();
        foreach ((RenderResource image, Material[] layers) in _relief.Values)
        {
            foreach (Material material in layers) material.Dispose();
            image.Dispose();
        }
        _shader.Dispose();
        _shellImage.Dispose();
    }
}
