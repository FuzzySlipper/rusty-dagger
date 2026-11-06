using System.Globalization;
using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Heightfields;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The exterior ground, <see cref="DaggerfallHorizonGround"/> as two heightfields. Near ground stands in the world across the
/// walked window, and far ground in the backdrop past it, so the ranges stand where the map has them. The world draws over
/// the backdrop, so inside the window only near ground shows. Near ground stands while the player is outdoors, and far
/// ground too unless they have chosen classic art.
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
    /// <summary>Past anything near ground's frame can reach.</summary>
    private const double NearEverywhere = 1e7;

    private readonly IEngineContext _engine;
    private readonly DaggerfallHorizonTuning _tuning;
    private readonly DaggerfallHorizonGround _ground;
    private readonly Material _sea;
    private Heightfield? _heightfield;
    private HeightfieldStreamer? _streamer;
    private Heightfield? _near;
    private HeightfieldStreamer? _nearStreamer;
    private HeightfieldMaterial[]? _bound;
    private (int MapX, int MapY) _nearFrame;
    private (SpatialSession Session, int MapX, int MapY)? _link;
    private float _tint = 1;
    private bool _seen = true;
    private double _lift;
    private float _fogDensity;
    private bool _nearSeen;
    private bool _disposed;

    internal DaggerfallHorizon(IEngineContext engine, DaggerfallHorizonGround ground, DaggerfallHorizonTuning tuning)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _lift = tuning.Lift;
        _fogDensity = tuning.FogDensity;
        _sea = engine.Graphics.CreateMaterial(Sea(1));
    }

    /// <summary>The clear-weather haze while the horizon stands, or none when the classic fog applies.</summary>
    internal float? FogDensity => _link is null ? null : _fogDensity;

    /// <summary>Stands the ground about a player at <paramref name="local"/> in the exterior frame <paramref name="origin"/>.</summary>
    /// <param name="seen">Whether the weather lets the far ground show. Thick weather closes within a kilometre or so, and
    /// ground that the fog has wholly taken would still stand out as a flat shape of the fog's colour against the sky.</param>
    /// <param name="daylight">The day's light from 0 to 1: the far sea takes it as a tint, its colour being its own.</param>
    /// <param name="far">Whether far ground stands at all: classic art shows the painted sky's horizon instead.</param>
    /// <param name="layers">Opens a ground archive's layers.</param>
    internal void Show(SpatialSession session, DaggerfallExteriorWorldOrigin origin, Vector3 local, bool winter, float daylight, bool seen, bool far,
        Func<int, DaggerfallGroundLayers> layers)
    {
        float tint = _tuning.NightTint + ((1 - _tuning.NightTint) * Math.Clamp(daylight, 0, 1));
        if (Math.Abs(tint - _tint) >= TintStep)
        {
            _tint = tint;
            _engine.Graphics.UpdateMaterial(new(_sea, Sea(tint)));
        }

        _bound ??= Bind(layers);
        Vector3 engineLocal = local - origin.Compensation;
        double x = engineLocal.X + (origin.MapPixelX * CellSize), z = engineLocal.Z + (origin.MapPixelY * CellSize);
        Follow(x, z, winter, far);
        ShowNear(session, origin, x, z);
        if (far) ShowFar(session, origin, seen);
        else HideFar();
    }

    private void ShowFar(SpatialSession session, DaggerfallExteriorWorldOrigin origin, bool seen)
    {
        if (_heightfield is null)
        {
            _heightfield = _engine.Heightfield.Create(new HeightfieldCreateRequest(DaggerfallHorizonGround.TileCells, DaggerfallHorizonGround.Rings,
                _bound!, RenderLayer.Backdrop, Vector3.Zero, 1 / Scale, HeightfieldFocus.At(_ground.Current.CentreX, _ground.Current.CentreZ))
                with { ShadowCasting = ShadowCasting.None });
            _streamer = new HeightfieldStreamer(_engine, _heightfield, _ground, CommitsPerUpdate);
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

        _streamer!.Update();
    }

    /// <summary>
    /// Binds the far sea and every ground archive's layers, so a vertex can name its climate's. A heightfield's materials are
    /// fixed when it is made, so all of them are bound at once.
    /// </summary>
    private HeightfieldMaterial[] Bind(Func<int, DaggerfallGroundLayers> layers)
    {
        List<HeightfieldMaterial> bound = [new(0, _sea)];
        uint Index(Material material)
        {
            bound.Add(new((uint)bound.Count, material));
            return (uint)bound.Count - 1;
        }

        Dictionary<int, DaggerfallHorizonGround.ArchiveMaterials> archives = [];
        foreach (int archive in DaggerfallExteriorEnvironment.GroundArchives)
        {
            DaggerfallGroundLayers ground = layers(archive);
            uint open = Index(ground.Open);
            archives.Add(archive, new(open, ground.HasLush ? Index(ground.Lush) : open, Index(ground.Town), Index(ground.Shore), ground.NoiseStone, ground.HasLush));
        }

        _ground.Materials = new(0, archives);
        return [.. bound];
    }

    /// <summary>Near ground stands in the world, in the origin's own frame, and draws what the player walks on.</summary>
    private void ShowNear(SpatialSession session, DaggerfallExteriorWorldOrigin origin, double x, double z)
    {
        if (_near is not null && _nearFrame != (origin.MapPixelX, origin.MapPixelY)) DropNear();
        double offsetX = origin.MapPixelX * CellSize, offsetZ = origin.MapPixelY * CellSize;
        if (_near is null)
        {
            _near = _engine.Heightfield.Create(new HeightfieldCreateRequest(DaggerfallHorizonGround.TileCells, DaggerfallHorizonGround.NearRings, _bound!, RenderLayer.Scene,
                Vector3.Zero, 1, HeightfieldFocus.At(x - offsetX, z - offsetZ))
                with { Session = session, Anchor = Global(0, 0, 0), ShadowCasting = ShadowCasting.None });
            _nearStreamer = new HeightfieldStreamer(_engine, _near, new NearGround(_ground, offsetX, offsetZ), CommitsPerUpdate);
            _nearFrame = (origin.MapPixelX, origin.MapPixelY);
            _nearSeen = true;
        }

        if (!_nearSeen)
        {
            _nearSeen = true;
            _engine.Heightfield.SetVisible(new(_near, true));
        }

        _engine.Heightfield.SetFocus(new HeightfieldFocusRequest(_near, HeightfieldFocus.At(x - offsetX, z - offsetZ)));
        _nearStreamer!.Update();
    }

    private sealed class NearGround(DaggerfallHorizonGround ground, double offsetX, double offsetZ) : IHeightfieldSampler
    {
        public void Sample(HeightfieldTileRequest tile, HeightfieldTileSamples samples) => ground.SampleNear(tile, samples, offsetX, offsetZ);
    }

    private void DropNear()
    {
        _nearStreamer?.Dispose(); _nearStreamer = null;
        _near?.Dispose(); _near = null;
    }

    /// <summary>How far the ground around an arrival has come: a loading screen holds until it stands, so nobody watches it stream in.</summary>
    internal LoadingProgress GroundProgress()
    {
        if (_near is null || !_nearSeen) return LoadingProgress.Finished;
        HeightfieldReadout read = _engine.Heightfield.Read(_near);
        return read.Settled ? LoadingProgress.Finished
            : LoadingProgress.Measured(read.ResidentTiles / Math.Max(1d, read.ResidentTiles + read.PendingTiles));
    }

    /// <summary>Lets go of the heightfields, whose materials are about to change; the next showing makes them anew.</summary>
    internal void Rebuild()
    {
        HideFar();
        DropNear();
        _streamer?.Dispose(); _streamer = null;
        _heightfield?.Dispose(); _heightfield = null;
        _bound = null;
        _ground.Current = _ground.Current with { CentreX = double.NaN, CentreZ = double.NaN };
    }

    private MaterialRequest Sea(float tint) => new(new Color(_tuning.Sea.R * tint, _tuning.Sea.G * tint, _tuning.Sea.B * tint, 1), default(RenderResourceReference), 1,
        new Color(1, 1, 1, 1), Vector3.Zero, 0, false);

    /// <summary>Takes the ground down, keeping its tiles for the next time outdoors.</summary>
    internal void Hide()
    {
        HideFar();
        if (_near is not null && _nearSeen)
        {
            _nearSeen = false;
            _engine.Heightfield.SetVisible(new(_near, false));
        }
    }

    private void HideFar()
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
        return string.Join(" | ", Survey("relief", terrain, _ground), Survey("classic", classic, new(classic, (_, _) => 231, _tuning, _ground.GroundTuning, new Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior>())));
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
        DropNear();
        _streamer?.Dispose();
        _heightfield?.Dispose();
    }

    /// <summary>
    /// Moves the lift's centre once the player has left it, and the rings with it, in steps small enough that tiles sampled
    /// either side of one barely differ. The frame changes first, so nothing invalidated is sampled by the old one. A move
    /// changes only what lies inside the fade about either centre; a new lift or season changes every tile.
    /// </summary>
    private void Follow(double x, double z, bool winter, bool far)
    {
        DaggerfallHorizonGround.Frame frame = _ground.Current;
        bool everywhere = !double.IsFinite(frame.CentreX) || frame.Lift != _lift || frame.Winter != winter;
        bool moved = Math.Max(Math.Abs(x - frame.CentreX), Math.Abs(z - frame.CentreZ)) >= _tuning.FollowMetres;
        if (!everywhere && !moved) return;
        _ground.Current = new(x, z, _lift, winter);
        // A season changes near ground too; the lift's centre only moves far ground.
        if (frame.Winter != winter && _near is not null) _engine.Heightfield.Invalidate(new HeightfieldInvalidateRequest(_near, -NearEverywhere, -NearEverywhere, NearEverywhere, NearEverywhere));
        if (_heightfield is null || !far) return;
        _engine.Heightfield.SetFocus(new HeightfieldFocusRequest(_heightfield, HeightfieldFocus.At(x, z)));
        double reach = everywhere ? DaggerfallHorizonGround.CoarseReachMetres * 2 : _tuning.LiftFullMetres;
        double fromX = double.IsFinite(frame.CentreX) ? frame.CentreX : x, fromZ = double.IsFinite(frame.CentreZ) ? frame.CentreZ : z;
        _engine.Heightfield.Invalidate(new HeightfieldInvalidateRequest(_heightfield, Math.Min(x, fromX) - reach, Math.Min(z, fromZ) - reach,
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
