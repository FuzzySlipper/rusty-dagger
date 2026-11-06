using System.Globalization;
using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Heightfields;
using WorldRpg.Rulesets.Daggerfall.Content;
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
    /// <summary>The far ground's tint follows the light in steps this small, too fine to see.</summary>
    private const float TintStep = .01f;

    private readonly IEngineContext _engine;
    private readonly DaggerfallHorizonTuning _tuning;
    private readonly DaggerfallHorizonGround _ground;
    private readonly Material _material;
    private Heightfield? _heightfield;
    private HeightfieldStreamer? _streamer;
    private (SpatialSession Session, int MapX, int MapY)? _link;
    private float _tint = 1;
    private bool _seen = true;
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
        _material = engine.Graphics.CreateMaterial(Tinted(1));
    }

    /// <summary>The clear-weather haze while the horizon stands, or none when the classic fog applies.</summary>
    internal float? FogDensity => _link is null ? null : _fogDensity;

    /// <summary>Stands the horizon about a player at <paramref name="local"/> in the exterior frame <paramref name="origin"/>.</summary>
    /// <param name="seen">Whether the weather lets the far ground show. Thick weather closes within a kilometre or so, and
    /// ground that the fog has wholly taken would still stand out as a flat shape of the fog's colour against the sky.</param>
    /// <param name="daylight">The day's light from 0 to 1: far ground takes it as a tint, since the world's night lights keep the ground's own colours bright.</param>
    internal void Show(SpatialSession session, DaggerfallExteriorWorldOrigin origin, Vector3 local, bool winter, float daylight, bool seen)
    {
        float tint = _tuning.NightTint + ((1 - _tuning.NightTint) * Math.Clamp(daylight, 0, 1));
        if (Math.Abs(tint - _tint) >= TintStep)
        {
            _tint = tint;
            _engine.Graphics.UpdateMaterial(new(_material, Tinted(tint)));
        }

        if (_heightfield is null)
        {
            _heightfield = _engine.Heightfield.Create(new HeightfieldCreateRequest(DaggerfallHorizonGround.TileCells, DaggerfallHorizonGround.Rings,
                new HeightfieldMaterial[] { new(0, _material) }, RenderLayer.Backdrop, Vector3.Zero, 1 / Scale, HeightfieldFocus.At(0, 0))
                with { ShadowCasting = ShadowCasting.None });
            _streamer = new HeightfieldStreamer(_engine, _heightfield, _ground, CommitsPerUpdate);
            _ground.Current = _ground.Current with { CentreX = double.NaN, CentreZ = double.NaN };
        }

        if (_link is not { } link || link.Session != session || link.MapX != origin.MapPixelX || link.MapY != origin.MapPixelY)
        {
            // The ground's (0, 0) is map pixel (0, 0): the frame's own pixel stands at the Engine's origin.
            _engine.CameraView.SetBackdrop(new(default, session, Global(-origin.MapPixelX * CellSize, 0, -origin.MapPixelY * CellSize), Vector3.Zero, Scale));
            _engine.Heightfield.SetVisible(new(_heightfield, true));
            _link = (session, origin.MapPixelX, origin.MapPixelY);
            _seen = true;
        }

        if (seen != _seen)
        {
            _seen = seen;
            _engine.Heightfield.SetVisible(new(_heightfield, seen));
        }

        Vector3 engineLocal = local - origin.Compensation;
        Follow(engineLocal.X + (origin.MapPixelX * CellSize), engineLocal.Z + (origin.MapPixelY * CellSize), winter);
        _streamer!.Update();
    }

    private static MaterialRequest Tinted(float tint) => new(new Color(tint, tint, tint, 1), default(RenderResourceReference), 1,
        new Color(1, 1, 1, 1), Vector3.Zero, 0, false);

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

    /// <summary>How much higher the ground stands at an offset from a player at <paramref name="local"/>.</summary>
    internal double GroundRise(DaggerfallExteriorWorldOrigin origin, Vector3 local, double east, double south)
    {
        Vector3 engineLocal = local - origin.Compensation;
        double x = engineLocal.X + (origin.MapPixelX * CellSize), z = engineLocal.Z + (origin.MapPixelY * CellSize);
        return _ground.Height(x + east, z + south) - _ground.Height(x, z);
    }

    /// <summary>How steep the land is at walking scale, on this ground and the classic's, and where it is worst.</summary>
    internal string GroundSurvey(DaggerfallTerrainSet terrain)
    {
        DaggerfallTerrainSet classic = new(terrain.Width, terrain.Height, terrain.Heightmap, terrain.Samples);
        return string.Join(" | ", Survey("relief", terrain, _ground), Survey("classic", classic, new(classic, (_, _) => 231, _tuning, _ground.GroundTuning)));
    }

    private static string Survey(string name, DaggerfallTerrainSet terrain, DaggerfallHorizonGround ground)
    {
        const int points = 40_000;
        const double spacing = DaggerfallTerrainSurfaceBuilder.SampleSpacing;
        Random random = new(10_502);
        List<double> slopes = new(points);
        (double Slope, double X, double Z, double Here) worst = default;
        while (slopes.Count < points)
        {
            double x = random.NextDouble() * terrain.Width * CellSize, z = random.NextDouble() * terrain.Height * CellSize;
            double here = ground.Height(x, z);
            if (here <= 42) continue;
            double east = (ground.Height(x + spacing, z) - here) / spacing, south = (ground.Height(x, z + spacing) - here) / spacing;
            double slope = Math.Sqrt((east * east) + (south * south));
            slopes.Add(slope);
            if (slope > worst.Slope) worst = (slope, x, z, here);
        }

        slopes.Sort();
        double Share(double slope) => slopes.Count(value => value > slope) / (double)points;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int cell = 0; cell < 8; cell++) DaggerfallTerrainSurfaceBuilder.Build(terrain, 300 + cell, 200);
        double buildMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds / 8;
        return string.Create(CultureInfo.InvariantCulture,
            $"{name}: rise/run p50 {slopes[points / 2]:F3}, p90 {slopes[points * 9 / 10]:F3}, p99 {slopes[points * 99 / 100]:F3}, over 30 deg {Share(.577):P2}, over 50 deg {Share(1.192):P2}, worst {worst.Slope:F2} at map ({worst.X / CellSize:F1}, {worst.Z / CellSize:F1}) {worst.Here:F0} m; build {buildMs:F1} ms");
    }

    internal string Readout()
    {
        DaggerfallHorizonGround.Frame frame = _ground.Current;
        string state = _link is null ? "hidden" : "shown";
        string tiles = _heightfield is null ? "not built" : Describe(_engine.Heightfield.Read(_heightfield));
        return string.Create(CultureInfo.InvariantCulture,
            $"{state}: lift {frame.Lift:F2} about ({frame.CentreX:F0}, {frame.CentreZ:F0}), haze {_fogDensity:F6}/m; {tiles}");
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
    /// Moves the lift's centre once the player has left it, and the rings with it, in steps small enough that tiles sampled
    /// either side of one barely differ. The frame changes first, so nothing invalidated is sampled by the old one. A move
    /// changes only what lies inside the fade about either centre; a new lift or season changes every tile.
    /// </summary>
    private void Follow(double x, double z, bool winter)
    {
        DaggerfallHorizonGround.Frame frame = _ground.Current;
        bool everywhere = !double.IsFinite(frame.CentreX) || frame.Lift != _lift || frame.Winter != winter;
        bool moved = Math.Max(Math.Abs(x - frame.CentreX), Math.Abs(z - frame.CentreZ)) >= _tuning.FollowMetres;
        if (!everywhere && !moved) return;
        _ground.Current = new(x, z, _lift, winter);
        _engine.Heightfield.SetFocus(new HeightfieldFocusRequest(_heightfield!, HeightfieldFocus.At(x, z)));
        double reach = everywhere ? DaggerfallHorizonGround.CoarseReachMetres * 2 : _tuning.LiftFullMetres;
        double fromX = double.IsFinite(frame.CentreX) ? frame.CentreX : x, fromZ = double.IsFinite(frame.CentreZ) ? frame.CentreZ : z;
        _engine.Heightfield.Invalidate(new HeightfieldInvalidateRequest(_heightfield!, Math.Min(x, fromX) - reach, Math.Min(z, fromZ) - reach,
            Math.Max(x, fromX) + reach, Math.Max(z, fromZ) + reach));
    }

    private static string Describe(HeightfieldReadout read) => string.Create(CultureInfo.InvariantCulture,
        $"{read.ResidentTiles} tiles, {read.PendingTiles} pending, {read.SampleBytes / 1_048_576.0:F1} MiB of samples, settled {read.Settled}");

    private static WorldOriginGlobalPosition Global(double x, double y, double z)
    {
        double cellX = Math.Floor(x), cellY = Math.Floor(y), cellZ = Math.Floor(z);
        return new((long)cellX, (long)cellY, (long)cellZ, x - cellX, y - cellY, z - cellZ);
    }
}
