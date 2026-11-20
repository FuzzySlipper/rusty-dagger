using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Heightfields;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The travel map's land: the exterior ground (<see cref="DaggerfallHorizonGround.SampleMap"/>) as one heightfield in the
/// backdrop, in far ground's frame of map metres at 1:1000, so the land, the sea, the coast and the rivers are where the
/// world streams them. Its rings centre on the point the map is looked at, fine where the eye is and coarse out to where
/// the haze closes. It is made hidden the first time ground is drawn and streams then, so the map finds it standing when
/// it opens, and a hidden heightfield keeps its tiles, so it shows again at once.
/// </summary>
internal sealed class DaggerfallOverlandTerrain : IDisposable
{
    /// <summary>Backdrop units per map metre, as far ground's: the map fits in a few hundred units.</summary>
    internal const float Scale = 1 / 1_000f;
    /// <summary>Cells along a tile's side; the finest tile is two map pixels.</summary>
    private const uint TileCells = 16;
    /// <summary>The finest cells, an eighth of a map pixel: a ridge or a river's bend still shows from the nearest zoom.</summary>
    internal const double FineCellMetres = DaggerfallWorldGrid.HorizontalSize / 8.0;
    /// <summary>How many sampled tiles are committed an update: the first showing samples a few hundred, and all at once would hitch.</summary>
    private const int CommitsPerUpdate = 24;

    /// <summary>The rings, finest first, in map metres: fine across the nearest view, coarse past where the haze closes.</summary>
    internal static HeightfieldRing[] Rings { get; } =
        [new(FineCellMetres, 10_000), new(FineCellMetres * 4, 40_000), new(FineCellMetres * 16, 160_000)];

    private readonly IEngineContext _engine;
    private readonly DaggerfallHorizonGround _ground;
    private readonly double _relief;
    private Heightfield? _field;
    private HeightfieldStreamer? _streamer;
    private bool _winter;
    private bool _shown;
    private bool _disposed;

    /// <param name="relief">How far land is raised above the sea, so ranges read from high above.</param>
    internal DaggerfallOverlandTerrain(IEngineContext engine, DaggerfallHorizonGround ground, double relief)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        if (!double.IsFinite(relief) || relief <= 0) throw new ArgumentOutOfRangeException(nameof(relief));
        _relief = relief;
    }

    /// <summary>Whether the map's land is drawn now.</summary>
    internal bool Shown => _shown;

    /// <summary>
    /// Stands the land about map point (<paramref name="x"/>, <paramref name="z"/>), drawn or not, and commits what its
    /// workers have sampled. A change of season samples every tile again, each drawing its old look until its new one lands.
    /// </summary>
    internal void Follow(double x, double z, bool winter, bool shown, HeightfieldMaterial[] materials)
    {
        if (_disposed) return;
        if (_field is null)
        {
            _winter = winter;
            _field = _engine.Heightfield.Create(new HeightfieldCreateRequest(TileCells, Rings, materials, RenderLayer.Backdrop,
                Vector3.Zero, Scale, HeightfieldFocus.At(x, z)));
            _streamer = new HeightfieldStreamer(_engine, _field, new Sampler(this), CommitsPerUpdate);
            _shown = true;
        }
        else if (winter != _winter)
        {
            _winter = winter;
            _engine.Heightfield.Invalidate(new HeightfieldInvalidateRequest(_field, -1e9, -1e9, 1e9, 1e9));
        }

        if (shown != _shown)
        {
            _shown = shown;
            _engine.Heightfield.SetVisible(new(_field, shown));
        }

        _engine.Heightfield.SetFocus(new HeightfieldFocusRequest(_field, HeightfieldFocus.At(x, z)));
        _streamer!.Update();
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
    }

    private sealed class Sampler(DaggerfallOverlandTerrain terrain) : IHeightfieldSampler
    {
        public void Sample(HeightfieldTileRequest tile, HeightfieldTileSamples samples) =>
            terrain._ground.SampleMap(tile, samples, terrain._relief, Volatile.Read(ref terrain._winter));
    }
}
