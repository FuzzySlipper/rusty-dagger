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
    /// <summary>How many sampled near tiles are committed an update.</summary>
    private const int CommitsPerUpdate = 64;
    /// <summary>How many far tiles are: every lift step samples hundreds again, and a frame that takes them all at once shows as a hitch.</summary>
    private const int FarCommitsPerUpdate = 16;
    private const double CellSize = DaggerfallExteriorCellResidency.CellSize;
    /// <summary>The sea follows the light in steps this small, too fine to see.</summary>
    private const float TintStep = .01f;
    /// <summary>How many map pixels either side of the player the sea's sheets stand, all of them inside near ground's reach.</summary>
    private const int SheetReach = (int)(DaggerfallHorizonGround.NearReachMetres / CellSize) - 1;
    /// <summary>Past anything near ground's frame can reach.</summary>
    private const double NearEverywhere = 1e7;

    private readonly IEngineContext _engine;
    private readonly DaggerfallHorizonTuning _tuning;
    private readonly DaggerfallHorizonGround _ground;
    private readonly Material _sea;
    private readonly Material _water;
    private readonly MeshResource _sheet;
    private readonly Appearance _sheetLook;
    private readonly List<(ulong Id, Vector3 At)> _sheets = [];
    private readonly DaggerfallRiverWater? _rivers;
    private readonly DaggerfallFloraField _flora;
    private DaggerfallGroundCover? _cover;
    private readonly float _repeatMetres;
    private Func<DaggerfallGroundCover>? _growCover;
    private readonly DaggerfallWaterTuning _waterTuning;
    private readonly Dictionary<int, RenderResource> _waterArt = [];
    private int _waterArchive = -1;
    private Heightfield? _heightfield;
    private HeightfieldStreamer? _streamer;
    private Heightfield? _near;
    private HeightfieldStreamer? _nearStreamer;
    private HeightfieldMaterial[]? _bound;
    private (int MapX, int MapY) _nearFrame;
    private (SpatialSession Session, int MapX, int MapY)? _link;
    private Vector3 _sky = Vector3.One;
    private bool _seen = true;
    private double _lift;
    private float _fogDensity;
    private bool _nearSeen;
    private bool _disposed;

    internal DaggerfallHorizon(IEngineContext engine, DaggerfallHorizonGround ground, DaggerfallHorizonTuning tuning, DaggerfallWaterTuning water, float repeatMetres, DaggerfallTerrainRivers? rivers,
        DaggerfallFloraField flora)
    {
        _flora = flora ?? throw new ArgumentNullException(nameof(flora));
        ArgumentNullException.ThrowIfNull(water);
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _lift = tuning.Lift;
        _fogDensity = tuning.FogDensity;
        _sea = engine.Graphics.CreateMaterial(Sea(Vector3.One));
        _waterTuning = water;
        _water = engine.Graphics.CreateMaterial(water.Material(default, Vector3.One));
        _repeatMetres = repeatMetres;
        _sheet = engine.Graphics.CreateMeshResource(Sheet(_water, repeatMetres));
        _sheetLook = engine.Graphics.CreateMeshAppearance(_sheet);
        _rivers = rivers is null ? null : new(engine, rivers, _water, repeatMetres);
    }

    /// <summary>The clear-weather haze while the horizon stands, or none when the classic fog applies.</summary>
    internal float? FogDensity => _link is null ? null : _fogDensity;

    /// <summary>Stands the ground about a player at <paramref name="local"/> in the exterior frame <paramref name="origin"/>.</summary>
    /// <param name="seen">Whether the weather lets the far ground show. Thick weather closes within a kilometre or so, and
    /// ground that the fog has wholly taken would still stand out as a flat shape of the fog's colour against the sky.</param>
    /// <param name="sky">The sky's light as a share of clear noon's, which the sea takes in its own colours, near and far alike.</param>
    /// <param name="far">Whether far ground and refreshed flora stand at all: classic art shows the painted sky's horizon, and its nature in the donor's places.</param>
    /// <param name="layers">Opens a ground archive's layers.</param>
    /// <param name="flora">What a nature archive's record draws as flora.</param>
    internal void Show(SpatialSession session, DaggerfallExteriorWorldOrigin origin, Vector3 local, bool winter, Vector3 sky, bool seen, bool far,
        Func<int, DaggerfallGroundLayers> layers, Func<int, int, DaggerfallFloraLook?> flora)
    {
        sky = Vector3.Clamp(sky, Vector3.Zero, Vector3.One);
        if (Vector3.Abs(sky - _sky) is { } moved && Math.Max(moved.X, Math.Max(moved.Y, moved.Z)) >= TintStep)
        {
            _sky = sky;
            _engine.Graphics.UpdateMaterial(new(_sea, Sea(sky)));
            _waterArchive = -1;
        }

        _bound ??= Bind(layers);
        Vector3 engineLocal = local - origin.Compensation;
        double x = engineLocal.X + (origin.MapPixelX * CellSize), z = engineLocal.Z + (origin.MapPixelY * CellSize);
        Follow(x, z, winter, far);
        ShowNear(session, origin, x, z);
        // Flora stands in near ground's frame, on the same walked surface.
        if (far)
        {
            _flora.Show(session, (origin.MapPixelX, origin.MapPixelY), engineLocal, winter, flora);
            if (_growCover is not null) (_cover ??= _growCover()).Show(origin, x, z, winter);
        }
        else
        {
            _flora.Hide();
            _cover?.Hide();
        }
        ShowSea(origin, x, z, winter);
        _rivers?.Follow(origin, x, z);
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
            _streamer = new HeightfieldStreamer(_engine, _heightfield, _ground, FarCommitsPerUpdate);
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
            _waterArt[archive] = ground.Water;
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
        _flora.Drop();
        _streamer?.Dispose(); _streamer = null;
        _heightfield?.Dispose(); _heightfield = null;
        _bound = null;
        _waterArt.Clear();
        _waterArchive = -1;
        _ground.Current = _ground.Current with { CentreX = double.NaN, CentreZ = double.NaN };
    }

    /// <summary>The sea past the sheets: it glows its colour, so the world's lights can't take it away from the water it meets.</summary>
    private MaterialRequest Sea(Vector3 sky) => new(new Color(0, 0, 0, 1), default(RenderResourceReference), 1, new Color(1, 1, 1, 1),
        new Vector3(_tuning.Sea.R, _tuning.Sea.G, _tuning.Sea.B) * sky, 1, false);

    /// <summary>Stands a sheet of sea on every map pixel with water in it about the player, so inland nothing draws water at all.</summary>
    private void ShowSea(DaggerfallExteriorWorldOrigin origin, double x, double z, bool winter)
    {
        int archive = _ground.GroundArchive(x, z, winter);
        if (archive != _waterArchive)
        {
            _waterArchive = archive;
            _engine.Graphics.UpdateMaterial(new(_water, _waterTuning.Material(_waterArt[archive], _sky)));
        }

        _sheets.Clear();
        (int mapX, int mapY) = DaggerfallHorizonGround.MapPixel(x, z);
        for (int y = mapY - SheetReach; y <= mapY + SheetReach; y++)
        for (int column = mapX - SheetReach; column <= mapX + SheetReach; column++)
        {
            if (!_ground.HoldsSea(column, y)) continue;
            // A pixel spans x on from its X and z back from its Y; the sheet's corner is its least x and z.
            Vector3 corner = new((float)((column - origin.MapPixelX) * CellSize), (float)DaggerfallHorizonGround.OceanMetres, (float)((y - 1 - origin.MapPixelY) * CellSize));
            _sheets.Add((DaggerfallPresentationObjectIds.Sea(column, y), corner + origin.Compensation));
        }
    }

    /// <summary>Adds the sea to the world's snapshot while the ground stands.</summary>
    internal void AppendFacts(List<AppearanceFact> facts)
    {
        foreach ((ulong id, Vector3 at) in _sheets)
            facts.Add(new AppearanceFact(id, false, 0, new Transform(at, Quaternion.Identity, Vector3.One), _sheetLook, true, RenderLayer.Scene));
        _cover?.AppendFacts(facts);
        _rivers?.AppendFacts(facts);
    }

    /// <summary>A snapshot was accepted: river water that left it is let go.</summary>
    internal void CompleteSnapshot()
    {
        _rivers?.CompleteSnapshot();
        _cover?.CompleteSnapshot();
    }

    /// <summary>The ground cover, once grown outdoors in refreshed art.</summary>
    internal DaggerfallGroundCover? GroundCover => _cover;

    /// <summary>Grows the art set's grass over near ground, made the first time refreshed ground stands outdoors.</summary>
    internal void UseGroundCover(DaggerfallGroundCoverArt art, DaggerfallGroundCoverTuning tuning, IReadOnlyDictionary<int, int> tuftGrounds) =>
        _growCover = () => new DaggerfallGroundCover(_engine, _ground, _flora, art, tuning, tuftGrounds, _repeatMetres);

    /// <summary>
    /// One map pixel of flat water at the sea's surface. Land stands through it, so the shore is wherever ground meets the
    /// water. It is a mesh, not a heightfield: a heightfield's tiles hang skirts, which show through water. The art's repeats
    /// fill a pixel exactly, so it runs on from sheet to sheet.
    /// </summary>
    private static MeshResourceCreateRequest Sheet(Material water, float repeatMetres)
    {
        float side = (float)CellSize, repeats = side / repeatMetres;
        Vector3[] positions = [new(0, 0, 0), new(side, 0, 0), new(side, 0, side), new(0, 0, side)];
        Vector3[] normals = [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY];
        Vector2[] uvs = [new(0, 0), new(repeats, 0), new(repeats, repeats), new(0, repeats)];
        return new MeshResourceCreateRequest(positions, normals, uvs, new uint[] { 0, 2, 1, 0, 3, 2 }, new MeshGroup[] { new(0, 0, 6) }, new MeshMaterialBinding[] { new(0, water) });
    }

    /// <summary>Takes the ground down, keeping its tiles for the next time outdoors.</summary>
    internal void Hide()
    {
        _sheets.Clear();
        _flora.Hide();
        _cover?.Hide();
        _rivers?.Hide();
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

    /// <summary>For tuning by eye: flora at a new density, each climate keeping its share.</summary>
    internal void UseFloraDensity(float density) => _flora.UseDensity(density);

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

    /// <summary>
    /// The map point of the nearest ground to a player at <paramref name="local"/> that is mostly <paramref name="kind"/>,
    /// within <paramref name="radius"/> metres (<see cref="DaggerfallHorizonGround.Kind"/>).
    /// </summary>
    internal (double X, double Z)? FindGround(DaggerfallExteriorWorldOrigin origin, Vector3 local, string kind, double radius, bool winter)
    {
        (double x, double z) = MapPoint(origin, local);
        return _ground.Find(x, z, kind, radius, winter);
    }

    /// <summary>The map point under <paramref name="local"/> in near ground's frame at <paramref name="origin"/>.</summary>
    internal static (double X, double Z) MapPoint(DaggerfallExteriorWorldOrigin origin, Vector3 local)
    {
        Vector3 engineLocal = local - origin.Compensation;
        return (engineLocal.X + (origin.MapPixelX * CellSize), engineLocal.Z + (origin.MapPixelY * CellSize));
    }

    /// <summary>Where the nearest river lies from a player at <paramref name="local"/>, for finding water by hand.</summary>
    internal string NearestRiver(DaggerfallExteriorWorldOrigin origin, Vector3 local, DaggerfallTerrainSet terrain)
    {
        Vector3 engineLocal = local - origin.Compensation;
        double x = engineLocal.X + (origin.MapPixelX * CellSize) - CellSize, z = engineLocal.Z + (origin.MapPixelY * CellSize);
        return terrain.Rivers?.NearestStation(x, z) is { } river
            ? string.Create(CultureInfo.InvariantCulture, $"River: {river.East:F0} m east, {river.South:F0} m south, surface {river.Surface:F1} m, {river.HalfWidth * 2:F0} m wide; ground there {_ground.Height(x + CellSize + river.East, z + river.South):F1} m, frame {origin.Compensation.Y:F1} m.")
            : "This terrain carries no rivers.";
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
            $"{state}: lift {frame.Lift:F2} about ({frame.CentreX:F0}, {frame.CentreZ:F0}), haze {_fogDensity:F6}/m; {tiles}; flora: {_flora.Readout()}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Flora goes before it would be hidden: the Engine applies a field's removal ahead of the call's other changes,
        // so hiding a field and removing it in one call updates a node already gone.
        _flora.Dispose();
        Hide();
        DropNear();
        _streamer?.Dispose();
        _heightfield?.Dispose();
    }

    /// <summary>Lets go of the sea, once a snapshot without it has been accepted and before the water art it wears is released.</summary>
    internal void ReleaseSea()
    {
        _cover?.Dispose();
        _cover = null;
        _rivers?.Release();
        _sheetLook.Dispose();
        _sheet.Dispose();
        _water.Dispose();
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
