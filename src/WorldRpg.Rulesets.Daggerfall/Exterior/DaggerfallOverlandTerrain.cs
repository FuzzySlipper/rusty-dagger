using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Heightfields;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The travel map's land: the exterior ground (<see cref="DaggerfallHorizonGround.SampleMap"/>) as one heightfield in the
/// backdrop, in far ground's frame of map metres at 1:1000, so the land, the sea, the coast and the rivers are where the
/// world streams them. It is painted per vertex in the palette's climate colours over two plain materials, the water and
/// the land; close in, the land takes the art set's detail over those colours, a mottle and its small relief repeating every
/// <see cref="DaggerfallMapPalette.DetailMetres"/>, filtered so it averages away to its tone farther out rather than
/// sparkling. Its rings centre on the point the map is looked at: cells of a sixteenth of a map pixel within a few
/// kilometres, coarse out to where the haze closes. It is made hidden the first time the player stands outdoors and
/// streams then, so the map finds it standing when it opens, and a hidden heightfield keeps its tiles, so it shows again
/// at once.
/// </summary>
internal sealed class DaggerfallOverlandTerrain : IDisposable
{
    /// <summary>Backdrop units per map metre, as far ground's: the map fits in a few hundred units.</summary>
    internal const float Scale = 1 / 1_000f;
    /// <summary>Cells along a tile's side; the finest tile is two map pixels.</summary>
    private const uint TileCells = 32;
    /// <summary>The finest cells, a sixteenth of a map pixel: a ridge, a river's bend or a cove still shows from the nearest zoom.</summary>
    internal const double FineCellMetres = DaggerfallWorldGrid.HorizontalSize / 16.0;
    /// <summary>How many sampled tiles are committed an update: the first showing samples a few hundred, and all at once would hitch.</summary>
    private const int CommitsPerUpdate = 24;

    /// <summary>The rings, finest first, in map metres: fine about the point looked at, coarse past where the haze closes.</summary>
    internal static HeightfieldRing[] Rings { get; } =
        [new(FineCellMetres, 8_000), new(FineCellMetres * 4, 32_000), new(FineCellMetres * 16, 110_000)];

    internal const string LandShaderPath = "worldrpg/packs/daggerfall.base/shaders/overland-land.wgsl";

    private readonly IEngineContext _engine;
    private readonly DaggerfallHorizonGround _ground;
    private readonly double _relief;
    private readonly DaggerfallMapPalette _palette;
    private readonly DaggerfallTravelMapArt? _art;
    private readonly DaggerfallArtFile? _landShaderFile;
    private MaterialShader? _waterShader;
    private Material? _water, _land;
    private RenderResource? _detail, _detailRelief, _landShader;
    private Heightfield? _field;
    private HeightfieldStreamer? _streamer;
    private Vector2 _windDirection = new(.8f, .6f);
    private float _cloudSpeed = .10f;
    private float _cloudCover = .22f;
    private float _daylight = 1f;
    private bool _winter;
    private bool _shown;
    private bool _disposed;

    /// <param name="relief">How far land is raised above the sea, so ranges read from high above.</param>
    /// <param name="palette">The colours the land and the water are painted in.</param>
    /// <param name="art">The art set's detail for the land, if it has it.</param>
    /// <param name="waterShader">The animated water shader over the sea's maps, or none to draw plain water.</param>
    /// <param name="landShader">The base pack's overland-land shader (anti-tiling, cliff crags, drifting cloud shadows), if present.</param>
    internal DaggerfallOverlandTerrain(IEngineContext engine, DaggerfallHorizonGround ground, double relief, DaggerfallMapPalette palette,
        DaggerfallTravelMapArt? art, MaterialShader? waterShader = null, DaggerfallArtFile? landShader = null)
    {
        _art = art;
        _waterShader = waterShader;
        _landShaderFile = landShader;
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        if (!double.IsFinite(relief) || relief <= 0) throw new ArgumentOutOfRangeException(nameof(relief));
        _relief = relief;
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
    }

    /// <summary>Updates the overland water's live shader parameters (wind bearing/speed and moon phase).</summary>
    internal void UpdateWater(MaterialShader shader)
    {
        _waterShader = shader;
        if (_water is not null && !_disposed) _engine.Graphics.UpdateMaterial(new(_water, Water()));
    }

    /// <summary>Updates wind bearing and speed for the land's drifting cloud shadows.</summary>
    internal void UpdateWind(float bearingRadians, float speedMetresPerSecond)
    {
        Vector2 dir = new(MathF.Cos(bearingRadians), MathF.Sin(bearingRadians));
        float speed = .07f + (Math.Clamp(speedMetresPerSecond / 20f, 0, 1) * .11f);
        if (Vector2.DistanceSquared(dir, _windDirection) < 1e-4f && MathF.Abs(speed - _cloudSpeed) < .005f) return;
        _windDirection = dir;
        _cloudSpeed = speed;
        RefreshLand();
    }

    /// <summary>Updates active weather cloud shadow density, daylight and season for the overland land shader.</summary>
    internal void UpdateAtmosphere(DaggerfallWeatherKind weather, float daylight, bool winter)
    {
        float cover = weather switch
        {
            DaggerfallWeatherKind.Sunny => .22f,
            DaggerfallWeatherKind.Cloudy => .42f,
            DaggerfallWeatherKind.Overcast => .64f,
            DaggerfallWeatherKind.Fog => .58f,
            DaggerfallWeatherKind.Rain => .72f,
            DaggerfallWeatherKind.Snow => .68f,
            _ => .78f,
        };
        float lit = MathF.Round(Math.Clamp(daylight, 0, 1) * 20) / 20;
        if (MathF.Abs(cover - _cloudCover) < .001f && MathF.Abs(lit - _daylight) < .001f && winter == _winter) return;
        _cloudCover = cover;
        _daylight = lit;
        RefreshLand();
    }

    private void RefreshLand()
    {
        if (_land is not null && _landShader is not null && !_disposed)
            _engine.Graphics.UpdateMaterial(new(_land, LandMaterial()));
    }

    /// <summary>Whether the map's land is drawn now.</summary>
    internal bool Shown => _shown;

    /// <summary>
    /// Stands the land about map point (<paramref name="x"/>, <paramref name="z"/>), drawn or not, and commits what its
    /// workers have sampled. A change of season samples every tile again, each drawing its old look until its new one lands.
    /// </summary>
    internal void Follow(double x, double z, bool winter, bool shown)
    {
        if (_disposed) return;
        if (_field is null)
        {
            _winter = winter;
            _water ??= _engine.Graphics.CreateMaterial(Water());
            _land ??= _engine.Graphics.CreateMaterial(Land());
            _field = _engine.Heightfield.Create(new HeightfieldCreateRequest(TileCells, Rings, new HeightfieldMaterial[] { new(0, _water), new(1, _land) }, RenderLayer.Backdrop,
                Vector3.Zero, Scale, HeightfieldFocus.At(x, z)));
            _streamer = new HeightfieldStreamer(_engine, _field, new Sampler(this), CommitsPerUpdate);
            _shown = true;
        }
        else if (winter != _winter)
        {
            _winter = winter;
            _engine.Heightfield.Invalidate(new HeightfieldInvalidateRequest(_field, -1e9, -1e9, 1e9, 1e9));
            RefreshLand();
        }

        if (shown != _shown)
        {
            _shown = shown;
            _engine.Heightfield.SetVisible(new(_field, shown));
        }

        _engine.Heightfield.SetFocus(new HeightfieldFocusRequest(_field, HeightfieldFocus.At(x, z)));
        _streamer!.Update();
    }

    /// <summary>The water's material: plain, or animated ripples, shoreline surf and sun/moon glints over the palette's depth colours.</summary>
    private MaterialRequest Water() => new MaterialRequest(new Color(1, 1, 1, 1), default(RenderResourceReference), _waterShader is null ? .3f : .2f,
        new Color(1, 1, 1, 1), Vector3.Zero, 0, false)
    {
        Shader = _waterShader ?? default,
    };

    /// <summary>The land's material: plain, detail maps alone, or the overland-land shader over the detail maps.</summary>
    private MaterialRequest Land()
    {
        if (_art is null)
            return new MaterialRequest(new Color(1, 1, 1, 1), default(RenderResourceReference), .95f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false);
        // Both maps are data: the mottle multiplies the colours as it is, and mipmaps average it to its tone at a distance.
        _detail ??= _engine.Graphics.OpenTexture(_engine.Content, _art.Detail.Path, _art.Detail.Sha256, TextureFilter.Linear, TextureWrap.Repeat,
            space: TextureColorSpace.Linear).Handle;
        _detailRelief ??= _engine.Graphics.OpenTexture(_engine.Content, _art.DetailNormal.Path, _art.DetailNormal.Sha256, TextureFilter.Linear, TextureWrap.Repeat,
            space: TextureColorSpace.Linear).Handle;
        if (_landShaderFile is { } shaderFile)
            _landShader ??= _engine.Graphics.OpenShader(_engine.Content, shaderFile.Path, shaderFile.Sha256);
        return LandMaterial();
    }

    private MaterialRequest LandMaterial()
    {
        if (_detail is null || _detailRelief is null)
            return new MaterialRequest(new Color(1, 1, 1, 1), default(RenderResourceReference), .95f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false);
        if (_landShader is not null)
        {
            return new MaterialRequest(new Color(1, 1, 1, 1), default(RenderResourceReference), .95f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false)
            {
                Shader = new MaterialShader(
                    new RenderResourceReference(_landShader),
                    new Vector4(_palette.DetailMetres * Scale, _palette.DetailRelief, (float)(DaggerfallHorizonGround.OceanMetres * Scale), _cloudCover),
                    new Vector4(_windDirection.X, _windDirection.Y, _cloudSpeed, _daylight),
                    new Vector4(_palette.Rock, _winter ? 1f : 0f),
                    Vector4.Zero,
                    new RenderResourceReference(_detail),
                    new RenderResourceReference(_detailRelief),
                    0),
            };
        }
        return new MaterialRequest(new Color(1, 1, 1, 1), new RenderResourceReference(_detail), .95f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false)
        {
            NormalMap = new RenderResourceReference(_detailRelief),
            NormalScale = _palette.DetailRelief,
            // Plain repeats: random offset tiles hid them, but tripled every sample across a far view seen at a grazing
            // angle, a millisecond of the map's frame on a 9070.
            TextureScale = new Vector2(1 / _palette.DetailMetres),
        };
    }

    /// <summary>Stops drawing the land; it keeps its tiles and its focus.</summary>
    internal void Hide()
    {
        if (_field is null || !_shown) return;
        _shown = false;
        _engine.Heightfield.SetVisible(new(_field, false));
    }

    /// <summary>
    /// Where a ray in the backdrop meets the land as drawn, in map metres, or none: the map's sky, or land not yet sampled.
    /// </summary>
    internal (double X, double Z)? Pick(Vector3 origin, Vector3 direction)
    {
        if (_field is null) return null;
        HeightfieldPickResult hit = _engine.Heightfield.Pick(new HeightfieldPickRequest(_field, origin, direction, 0));
        return hit.Hit ? (hit.X, hit.Z) : null;
    }

    /// <summary>The land's height at a map point, in map metres, as its finest cells draw it: what stands on the map stands on it.</summary>
    internal double Height(double x, double z) => _ground.MapGround(x, z, _relief, FineCellMetres);

    /// <summary>Whether a map point sits on wooded land suitable for an instanced miniature forest clump (#11113).</summary>
    internal bool TrySampleForest(double x, double z, out double drawnHeight, out int climate, out float density) =>
        _ground.TrySampleMapForest(x, z, _relief, FineCellMetres, _palette, out drawnHeight, out climate, out density);

    /// <summary>What the land holds and waits for, for the map's cost.</summary>
    internal HeightfieldReadout? Read() => _field is null ? null : _engine.Heightfield.Read(_field);

    /// <summary>Lets go of the land, whose materials are about to change; the next showing makes it anew.</summary>
    internal void Drop()
    {
        _streamer?.Dispose(); _streamer = null;
        _field?.Dispose(); _field = null;
        _shown = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Drop();
        _water?.Dispose();
        _land?.Dispose();
        _landShader?.Dispose();
        _detail?.Dispose();
        _detailRelief?.Dispose();
    }

    private sealed class Sampler(DaggerfallOverlandTerrain terrain) : IHeightfieldSampler
    {
        public void Sample(HeightfieldTileRequest tile, HeightfieldTileSamples samples) =>
            terrain._ground.SampleMap(tile, samples, terrain._relief, Volatile.Read(ref terrain._winter), terrain._palette);
    }
}
