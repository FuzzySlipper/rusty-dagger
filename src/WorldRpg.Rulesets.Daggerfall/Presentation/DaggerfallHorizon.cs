using System.Globalization;
using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Heightfields;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The map on the skyline: <see cref="DaggerfallHorizonGround"/> drawn in the Engine's backdrop past the walked window,
/// so the ranges stand where the map has them. The world draws over the backdrop, so inside the window only the walked
/// ground shows. It lives only while the player is outdoors and has not chosen classic art.
/// </summary>
internal sealed class DaggerfallHorizon : IDisposable
{
    /// <summary>World metres per backdrop unit: the map fits in a few hundred units.</summary>
    private const float Scale = 1_000;
    /// <summary>How many sampled tiles are committed an update.</summary>
    private const int CommitsPerUpdate = 64;
    private const double CellSize = DaggerfallExteriorCellResidency.CellSize;

    private readonly IEngineContext _engine;
    private readonly DaggerfallHorizonTuning _tuning;
    private readonly DaggerfallHorizonGround _ground;
    private readonly Material _material;
    private Heightfield? _heightfield;
    private HeightfieldStreamer? _streamer;
    private (SpatialSession Session, int MapX, int MapY)? _link;
    private double _lift;
    private float _fogDensity;
    private bool _disposed;

    internal DaggerfallHorizon(IEngineContext engine, DaggerfallHorizonGround ground, DaggerfallHorizonTuning tuning)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _lift = tuning.Lift;
        _fogDensity = tuning.FogDensity;
        _material = engine.Graphics.CreateMaterial(new MaterialRequest(new Color(1, 1, 1, 1), default(RenderResourceReference), 1,
            new Color(1, 1, 1, 1), Vector3.Zero, 0, false));
    }

    /// <summary>The clear-weather haze while the horizon stands, or none when the classic fog applies.</summary>
    internal float? FogDensity => _link is null ? null : _fogDensity;

    /// <summary>Stands the horizon about a player at <paramref name="local"/> in the exterior frame <paramref name="origin"/>.</summary>
    internal void Show(SpatialSession session, DaggerfallExteriorWorldOrigin origin, Vector3 local, bool winter)
    {
        if (_heightfield is null)
        {
            _heightfield = _engine.Heightfield.Create(new HeightfieldCreateRequest(DaggerfallHorizonGround.TileCells, DaggerfallHorizonGround.Rings,
                new HeightfieldMaterial[] { new(0, _material) }, RenderLayer.Backdrop, Vector3.Zero, 1 / Scale, HeightfieldFocus.At(0, 0))
                with { ShadowCasting = ShadowCasting.None });
            _streamer = new HeightfieldStreamer(_engine, _heightfield, _ground, CommitsPerUpdate);
            _ground.Current = _ground.Current with { BaseMetres = double.NaN };
        }

        if (_link is not { } link || link.Session != session || link.MapX != origin.MapPixelX || link.MapY != origin.MapPixelY)
        {
            // The ground's (0, 0) is map pixel (0, 0): the frame's own pixel stands at the Engine's origin.
            _engine.CameraView.SetBackdrop(new(default, session, Global(-origin.MapPixelX * CellSize, 0, -origin.MapPixelY * CellSize), Vector3.Zero, Scale));
            _engine.Heightfield.SetVisible(new(_heightfield, true));
            _link = (session, origin.MapPixelX, origin.MapPixelY);
        }

        Vector3 engineLocal = local - origin.Compensation;
        Follow(engineLocal.X + (origin.MapPixelX * CellSize), engineLocal.Z + (origin.MapPixelY * CellSize), engineLocal.Y, winter);
        _streamer!.Update();
    }

    /// <summary>Takes the horizon down, keeping its tiles for the next time outdoors.</summary>
    internal void Hide()
    {
        if (_link is null) return;
        _engine.CameraView.ClearBackdrop(new(default));
        _engine.Heightfield.SetVisible(new(_heightfield!, false));
        _link = null;
    }

    /// <summary>For tuning by eye: a new lift and haze, every tile sampled again.</summary>
    internal void Adjust(double lift, double fogDensity)
    {
        _lift = lift;
        _fogDensity = (float)fogDensity;
    }

    internal string Readout()
    {
        DaggerfallHorizonGround.Frame frame = _ground.Current;
        string state = _link is null ? "hidden" : "shown";
        string tiles = _heightfield is null ? "not built" : Describe(_engine.Heightfield.Read(_heightfield));
        return string.Create(CultureInfo.InvariantCulture,
            $"{state}: lift {frame.Lift:F2} above {frame.BaseMetres:F0} m about ({frame.CentreX:F0}, {frame.CentreZ:F0}), haze {_fogDensity:F6}/m; {tiles}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Hide();
        _streamer?.Dispose();
        _heightfield?.Dispose();
    }

    /// <summary>
    /// Moves the lift's centre and base once the player has left them, and the rings with them. The frame changes first,
    /// so nothing invalidated is sampled by the old one. A new base or lift changes every lifted height; a new centre
    /// only what lies inside the fade.
    /// </summary>
    private void Follow(double x, double z, double feet, bool winter)
    {
        DaggerfallHorizonGround.Frame frame = _ground.Current;
        bool rebased = !(Math.Abs(feet - frame.BaseMetres) < _tuning.BaseStepMetres) || frame.Lift != _lift || frame.Winter != winter;
        bool moved = Math.Max(Math.Abs(x - frame.CentreX), Math.Abs(z - frame.CentreZ)) >= _tuning.FollowMetres;
        if (!rebased && !moved) return;
        _ground.Current = new(x, z, rebased ? feet : frame.BaseMetres, _lift, winter);
        _engine.Heightfield.SetFocus(new HeightfieldFocusRequest(_heightfield!, HeightfieldFocus.At(x, z)));
        double extent = rebased ? DaggerfallHorizonGround.CoarseReachMetres * 2 : _tuning.LiftFullMetres + _tuning.FollowMetres;
        _engine.Heightfield.Invalidate(new HeightfieldInvalidateRequest(_heightfield!, x - extent, z - extent, x + extent, z + extent));
    }

    private static string Describe(HeightfieldReadout read) => string.Create(CultureInfo.InvariantCulture,
        $"{read.ResidentTiles} tiles, {read.PendingTiles} pending, {read.SampleBytes / 1_048_576.0:F1} MiB of samples, settled {read.Settled}");

    private static WorldOriginGlobalPosition Global(double x, double y, double z)
    {
        double cellX = Math.Floor(x), cellY = Math.Floor(y), cellZ = Math.Floor(z);
        return new((long)cellX, (long)cellY, (long)cellZ, x - cellX, y - cellY, z - cellZ);
    }
}
