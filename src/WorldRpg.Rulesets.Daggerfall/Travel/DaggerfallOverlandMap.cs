using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>A name the travel map shows over a place, the party or its destination, where the view puts it: across and
/// down the map's view, from its top left, 0 to 1.</summary>
internal sealed record DaggerfallOverlandLabel(string Name, string Kind, double Across, double Down);

/// <summary>
/// The expedition's travel map as a world the Engine draws: the land (<see cref="DaggerfallOverlandTerrain"/>) in the
/// backdrop, the places on it as small models, the party's and the bands' standards, the ring of the party's sight, the
/// way it goes, where it heads and the pin, under a camera of its own that orbits, tilts, pans and zooms
/// (<see cref="DaggerfallOverlandView"/>). It draws what the expedition holds and decides nothing.
/// <para>
/// The map camera stands at the player's eye, so everything measured from the eye stays put, and draws nothing of the
/// world: its far plane is a few centimetres off. Its own backdrop link puts its backdrop eye on the orbit, anchored at
/// that eye, so the land shows behind an empty world. It becomes the active camera while the map is open, drawn into the
/// map's place on the page (<see cref="ViewAnchor"/>), and the player's camera is active again when it closes.
/// </para>
/// </summary>
internal sealed class DaggerfallOverlandMap : IDisposable
{
    /// <summary>The name the page anchors the map's view under.</summary>
    internal const string ViewAnchor = "travel-map";
    /// <summary>World metres per backdrop unit, as far ground's link.</summary>
    private const double LinkScale = 1 / DaggerfallOverlandTerrain.Scale;
    private const double Cell = DaggerfallWorldGrid.HorizontalSize;
    /// <summary>The world the map camera draws ends this close: none of it shows.</summary>
    private const double WorldNear = .01, WorldFar = .02;
    /// <summary>How far past the view's distance places are drawn, and at most how many of their parts and names.</summary>
    private const double PlaceReach = 2.2;
    private const int MostPlaceParts = 1_200, MostLabels = 60;
    /// <summary>How many dots ring the party's sight.</summary>
    private const int SightRingDots = 72;
    private static readonly CameraViewport FullViewport = new(0d, 0d, 1d, 1d);

    private readonly IEngineContext _engine;
    private readonly DaggerfallOverlandTerrain _terrain;
    private readonly DaggerfallOverlandTuning _tuning;
    private readonly IReadOnlyList<DaggerfallTravelDestination> _destinations;
    private readonly Action _playerCamera;
    private readonly Dictionary<(int X, int Y), List<DaggerfallTravelDestination>> _blocks = [];
    private readonly Dictionary<(int X, int Z), double> _heights = [];
    private readonly List<AppearanceFact> _facts = [];
    private readonly List<DaggerfallOverlandLabel> _labels = [];
    private DaggerfallOverlandModels? _models;
    private Material? _sea;
    private HeightfieldMaterial[]? _materials, _materialsFrom;
    private Camera? _camera;
    private CameraDescriptor _backdropView;
    private double _aspect = 16d / 9;
    private bool _open;
    private bool _disposed;

    /// <param name="destinations">Every place on the world map.</param>
    /// <param name="playerCamera">Makes the player's camera the active one again.</param>
    internal DaggerfallOverlandMap(IEngineContext engine, DaggerfallOverlandTerrain terrain, DaggerfallOverlandTuning tuning,
        IReadOnlyList<DaggerfallTravelDestination> destinations, Action playerCamera)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _destinations = destinations ?? throw new ArgumentNullException(nameof(destinations));
        _playerCamera = playerCamera ?? throw new ArgumentNullException(nameof(playerCamera));
        View = new(tuning);
    }

    internal DaggerfallOverlandView View { get; }

    /// <summary>Whether the map draws now.</summary>
    internal bool Open => _open;

    /// <summary>What the map draws this update, which the session's snapshot carries; none while it is closed.</summary>
    internal IReadOnlyList<AppearanceFact>? Facts => _open ? _facts : null;

    /// <summary>The names over the places, the party and where it heads, where the view puts them.</summary>
    internal IReadOnlyList<DaggerfallOverlandLabel> Labels => _open ? _labels : [];

    /// <summary>
    /// Streams the land, hidden, about map point (<paramref name="x"/>, <paramref name="z"/>) while the map is closed, so
    /// it stands ready when it opens.
    /// </summary>
    internal void Prepare(double x, double z, bool winter, HeightfieldMaterial[] materials)
    {
        if (!_open) _terrain.Follow(x, z, winter, shown: false, MapMaterials(materials));
    }

    /// <summary>
    /// The ground's materials with the map's own sea: far ground's sea glows and ripples for a horizon seen across it,
    /// and from high above reads as grey speckle, so the map's water is a plain deep blue the sun glints on.
    /// </summary>
    private HeightfieldMaterial[] MapMaterials(HeightfieldMaterial[] ground)
    {
        if (_materials is { } made && _materialsFrom == ground) return made;
        _sea ??= _engine.Graphics.CreateMaterial(new MaterialRequest(Color.FromSrgb(.13f, .27f, .40f), default(RenderResourceReference), .25f,
            new Color(1, 1, 1, 1), Vector3.Zero, 0, false));
        _materialsFrom = ground;
        return _materials = [.. ground.Select(bound => bound.Index == 0 ? new HeightfieldMaterial(0, _sea) : bound)];
    }

    /// <summary>
    /// Draws the map for this update: the view follows the party unless panned; the land, the places, the party, the
    /// bands, the sight ring, the way and the pin stand where the expedition has them, under the map's camera at the
    /// player's <paramref name="eye"/>.
    /// </summary>
    internal void Show(DaggerfallExpedition expedition, Vector3 eye, bool winter, HeightfieldMaterial[] materials)
    {
        if (_disposed) return;
        (double partyX, double partyZ) = DaggerfallOverlandView.Metres(expedition.Position.X, expedition.Position.Y);
        bool opening = !_open;
        if (opening) View.Open(partyX, partyZ);
        View.Track(partyX, partyZ);
        _terrain.Follow(View.TargetX, View.TargetZ, winter, shown: true, MapMaterials(materials));
        Vector3 target = View.Target(Height(View.TargetX, View.TargetZ));
        Vector3 viewEye = View.Eye(target.Y);
        if (!CameraQueries.TryLookAtPose(Backdrop(viewEye), Backdrop(target), View.Heading, out CameraPose pose)) return;
        _aspect = Aspect();
        _backdropView = Descriptor(pose, 1e-3, 1e4);
        CameraDescriptor world = Descriptor(pose with { Position = eye }, WorldNear, WorldFar);
        if (_camera is null)
        {
            _camera = _engine.CameraView.CreateCamera(world);
            _engine.CameraView.SetViewportAnchor(new(_camera, ViewAnchor));
        }
        else _engine.CameraView.UpdateCamera(new CameraUpdateRequest(_camera, world));
        _engine.CameraView.SetBackdrop(new(_camera, default, Global(eye), Backdrop(viewEye), (float)LinkScale));
        if (opening) _engine.CameraView.SetActiveCamera(_camera);
        _open = true;
        Compose(expedition, partyX, partyZ);
    }

    /// <summary>Closes the map: the player's camera is active again and the land hides with its tiles kept.</summary>
    internal void Close()
    {
        if (!_open) return;
        _open = false;
        _facts.Clear();
        _labels.Clear();
        _playerCamera();
        _terrain.Hide();
    }

    /// <summary>
    /// The map pixel under a point of the map's view, <paramref name="across"/> and <paramref name="down"/> from its top
    /// left (0 to 1), or none where the view shows sky or land not yet drawn.
    /// </summary>
    internal DaggerfallTravelMapPixel? Pick(double across, double down)
    {
        if (!_open || !double.IsFinite(across) || !double.IsFinite(down)) return null;
        CameraRay ray = CameraQueries.Ray(_backdropView, _aspect, new Vector2((float)across, (float)(1 - down)));
        return _terrain.Pick(ray.Origin, ray.Direction) is { } hit ? DaggerfallOverlandView.Pixel(hit.X, hit.Z) : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _facts.Clear();
        _camera?.Dispose();
        _models?.Dispose();
        _sea?.Dispose();
    }

    private CameraDescriptor Descriptor(CameraPose pose, double near, double far) => new(pose, CameraBasisMode.Derived, default,
        new CameraProjection(CameraProjectionKind.Perspective, _tuning.FieldOfView, 0d, near, far), FullViewport);

    /// <summary>The map's view's shape on the page, or the window's while the page has not placed it.</summary>
    private double Aspect()
    {
        CameraSurfaceReadout surface = _engine.CameraView.ReadSurface();
        if (!surface.Reported || surface.CssHeight <= 0) return _aspect;
        CameraViewportAnchorReadout view = _engine.CameraView.ReadViewportAnchor(new(ViewAnchor));
        return view.Reported && view.Height > 0 ? view.Width * surface.CssWidth / (view.Height * surface.CssHeight) : surface.CssWidth / surface.CssHeight;
    }

    /// <summary>The land's drawn height at a map point: its finest cells' surface, read once a vertex.</summary>
    private double Height(double x, double z)
    {
        double cell = DaggerfallOverlandTerrain.FineCellMetres;
        double fx = x / cell, fz = z / cell;
        int ix = (int)Math.Floor(fx), iz = (int)Math.Floor(fz);
        double u = fx - ix, v = fz - iz;
        double Vertex(int i, int j)
        {
            if (!_heights.TryGetValue((i, j), out double height))
            {
                if (_heights.Count > 200_000) _heights.Clear();
                _heights[(i, j)] = height = _terrain.Height(i * cell, j * cell);
            }
            return height;
        }
        double top = Vertex(ix, iz) + ((Vertex(ix + 1, iz) - Vertex(ix, iz)) * u);
        double bottom = Vertex(ix, iz + 1) + ((Vertex(ix + 1, iz + 1) - Vertex(ix, iz + 1)) * u);
        return top + ((bottom - top) * v);
    }

    private static Vector3 Backdrop(Vector3 metres) => metres * DaggerfallOverlandTerrain.Scale;

    private static WorldOriginGlobalPosition Global(Vector3 point)
    {
        double cellX = Math.Floor(point.X), cellY = Math.Floor(point.Y), cellZ = Math.Floor(point.Z);
        return new((long)cellX, (long)cellY, (long)cellZ, point.X - cellX, point.Y - cellY, point.Z - cellZ);
    }

    /// <summary>Everything the map stands for this update, and the names over it.</summary>
    private void Compose(DaggerfallExpedition expedition, double partyX, double partyZ)
    {
        DaggerfallOverlandModels models = _models ??= new(_engine);
        _facts.Clear();
        _labels.Clear();
        ulong next = 0;
        // Standards and dots keep their size on the screen better than true size would: they grow with the distance's root,
        // and close in still stand over the houses.
        float grow = Math.Max(.75f, (float)Math.Sqrt(View.Distance / _tuning.Distance.Opening));
        float token = (float)(_tuning.Token * grow), dot = token * .14f;

        void Stand(Appearance look, double x, double z, float scale, float turn = 0, float sink = 0)
        {
            Vector3 at = new((float)x, (float)Height(x, z) - sink, (float)z);
            _facts.Add(new AppearanceFact(DaggerfallPresentationObjectIds.TravelMap(next++), false, 0,
                new Transform(Backdrop(at), Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn), new Vector3(scale * DaggerfallOverlandTerrain.Scale)),
                look, true, RenderLayer.Backdrop));
        }
        void Standard(DaggerfallOverlandBanner banner, double x, double z)
        {
            Stand(models.Pole, x, z, token);
            Stand(models.Flags[banner], x, z, token);
        }
        void Label(string name, string kind, double x, double z, float above)
        {
            if (_labels.Count >= MostLabels) return;
            CameraProjectionResult seen = CameraQueries.Project(_backdropView, _aspect, Backdrop(new((float)x, (float)Height(x, z) + above, (float)z)));
            if (seen.InClip) _labels.Add(new(name, kind, seen.NormalizedPoint.X, 1 - seen.NormalizedPoint.Y));
        }

        // The places about the view, the known ones named; the party sees those it does not know within its sight.
        double reach = View.Distance * PlaceReach, sight = expedition.PartySight * Cell;
        // Settlements, temples and dungeons are named from any distance; homes, farms, inns and graveyards close in.
        bool close = View.Distance <= _tuning.Distance.Opening / 2;
        foreach (DaggerfallTravelDestination place in PlacesAbout(View.TargetX, View.TargetZ, reach))
        {
            bool known = expedition.Known(place.Id);
            (double x, double z) = DaggerfallOverlandView.Metres(place.MapPixel.X + .5, place.MapPixel.Y + .5);
            if (!known && Math.Sqrt(((x - partyX) * (x - partyX)) + ((z - partyZ) * (z - partyZ))) > sight) continue;
            // The nearest places come first, so a wide view leaves out only the farthest.
            if (_facts.Count >= MostPlaceParts) break;
            StandPlace(models, place, x, z, Stand);
            if (known && (close || Named.Contains(place.Kind))) Label(place.Name, DaggerfallSiteKinds.Label(place.Kind), x, z, (float)(_tuning.House * 2.6));
        }

        // The party's sight, its standard, the way it goes and where it heads.
        for (int index = 0; index < SightRingDots; index++)
        {
            double angle = index * Math.Tau / SightRingDots;
            Stand(models.Dots[DaggerfallOverlandDot.Sight], partyX + (Math.Cos(angle) * sight), partyZ + (Math.Sin(angle) * sight), dot);
        }
        if (expedition.Destination is { } destination)
        {
            (double toX, double toZ) = DaggerfallOverlandView.Metres(destination.Pixel.X + .5, destination.Pixel.Y + .5);
            double length = Math.Sqrt(((toX - partyX) * (toX - partyX)) + ((toZ - partyZ) * (toZ - partyZ)));
            int dots = (int)Math.Clamp(length / (dot * 3.5), 0, 240);
            for (int index = 1; index < dots; index++)
            {
                double share = index / (double)dots;
                Stand(models.Dots[DaggerfallOverlandDot.Route], partyX + ((toX - partyX) * share), partyZ + ((toZ - partyZ) * share), dot * .8f);
            }
            Standard(DaggerfallOverlandBanner.Destination, toX, toZ);
        }
        if (expedition.Pin is { } pin)
        {
            (double pinX, double pinZ) = DaggerfallOverlandView.Metres(pin.Pixel.X + .5, pin.Pixel.Y + .5);
            Stand(models.Pin, pinX, pinZ, token * .7f);
        }
        Standard(DaggerfallOverlandBanner.Party, partyX, partyZ);
        Label("Your party", "party", partyX, partyZ, 0);

        // The bands the party sees, a spotted one ringed in red.
        foreach (DaggerfallExpeditionBandView band in expedition.SeenBands)
        {
            (double x, double z) = DaggerfallOverlandView.Metres(band.X, band.Y);
            Standard(Banner(band.Kind), x, z);
            if (band.Spotted) Stand(models.Dots[DaggerfallOverlandDot.Spotted], x, z, token * .6f, sink: -2);
            Label(band.Name, band.Camp ? "camp" : band.Spotted ? "chase" : "band", x, z, token * 1.1f);
        }
    }

    /// <summary>A place's model: a cluster of houses by its size, a keep, a temple, a ruin or a graveyard's stones.</summary>
    private void StandPlace(DaggerfallOverlandModels models, DaggerfallTravelDestination place, double x, double z,
        Action<Appearance, double, double, float, float, float> stand)
    {
        float house = (float)_tuning.House * Math.Max(1, (float)Math.Sqrt(View.Distance / _tuning.Distance.Opening));
        (Appearance Look, int Count, float Scale) shape = place.Kind switch
        {
            DaggerfallSiteKind.TownCity => (models.House, 9, 1f),
            DaggerfallSiteKind.TownHamlet => (models.House, 6, 1f),
            DaggerfallSiteKind.TownVillage => (models.House, 4, .9f),
            DaggerfallSiteKind.HomeFarms or DaggerfallSiteKind.HomeWealthy or DaggerfallSiteKind.HomePoor => (models.House, 2, .8f),
            DaggerfallSiteKind.Tavern => (models.House, 2, .9f),
            DaggerfallSiteKind.ReligionTemple => (models.Temple, 1, 1.3f),
            DaggerfallSiteKind.ReligionCult or DaggerfallSiteKind.Coven => (models.Shrine, 1, 1.1f),
            DaggerfallSiteKind.DungeonLabyrinth => (models.Keep, 1, 1.6f),
            DaggerfallSiteKind.DungeonKeep => (models.Keep, 1, 1.2f),
            DaggerfallSiteKind.DungeonRuin => (models.Ruin, 1, 1.2f),
            DaggerfallSiteKind.Graveyard => (models.Stone, 3, .45f),
            _ => (models.House, 1, .8f),
        };
        uint seed = (uint)HashCode.Combine(place.Id.Region, place.Id.Index);
        for (int index = 0; index < shape.Count; index++)
        {
            // A cluster on a loose spiral about the pixel's middle, each turned its own way.
            double angle = (index * 2.4) + (seed % 628 / 100.0), away = index == 0 ? 0 : house * (.75 + (.42 * Math.Sqrt(index)));
            float turn = (float)(((seed >> (index % 16)) % 360) * Math.PI / 180);
            float scale = house * shape.Scale * (index == 0 && shape.Count > 3 ? 1.25f : 1);
            stand(shape.Look, x + (Math.Cos(angle) * away), z + (Math.Sin(angle) * away), scale, turn, scale * .08f);
        }
    }

    /// <summary>The kinds of place named however far out the view stands.</summary>
    private static readonly HashSet<DaggerfallSiteKind> Named =
    [
        DaggerfallSiteKind.TownCity, DaggerfallSiteKind.TownHamlet, DaggerfallSiteKind.TownVillage, DaggerfallSiteKind.ReligionTemple,
        DaggerfallSiteKind.DungeonLabyrinth, DaggerfallSiteKind.DungeonKeep, DaggerfallSiteKind.DungeonRuin,
    ];

    private static DaggerfallOverlandBanner Banner(DaggerfallBandKind kind) => kind switch
    {
        DaggerfallBandKind.Bandits => DaggerfallOverlandBanner.Bandits,
        DaggerfallBandKind.Travellers => DaggerfallOverlandBanner.Travellers,
        DaggerfallBandKind.Beasts => DaggerfallOverlandBanner.Beasts,
        DaggerfallBandKind.Undead => DaggerfallOverlandBanner.Undead,
        _ => DaggerfallOverlandBanner.Monsters,
    };

    /// <summary>The places within <paramref name="reach"/> metres of a map point, nearest blocks first.</summary>
    private IEnumerable<DaggerfallTravelDestination> PlacesAbout(double x, double z, double reach)
    {
        const int Block = 16;
        if (_blocks.Count == 0)
            foreach (DaggerfallTravelDestination place in _destinations)
            {
                (int X, int Y) block = (place.MapPixel.X / Block, place.MapPixel.Y / Block);
                if (!_blocks.TryGetValue(block, out List<DaggerfallTravelDestination>? held)) _blocks[block] = held = [];
                held.Add(place);
            }
        DaggerfallTravelMapPixel middle = DaggerfallOverlandView.Pixel(x, z);
        int span = (int)Math.Ceiling(reach / Cell / Block) + 1;
        (int X, int Y) centre = (middle.X / Block, middle.Y / Block);
        foreach ((int X, int Y) block in Enumerable.Range(-span, (2 * span) + 1)
            .SelectMany(dy => Enumerable.Range(-span, (2 * span) + 1).Select(dx => (centre.X + dx, centre.Y + dy)))
            .OrderBy(block => Math.Max(Math.Abs(block.Item1 - centre.X), Math.Abs(block.Item2 - centre.Y))))
        {
            if (!_blocks.TryGetValue(block, out List<DaggerfallTravelDestination>? places)) continue;
            foreach (DaggerfallTravelDestination place in places)
            {
                (double px, double pz) = DaggerfallOverlandView.Metres(place.MapPixel.X + .5, place.MapPixel.Y + .5);
                if (Math.Abs(px - x) <= reach && Math.Abs(pz - z) <= reach) yield return place;
            }
        }
    }
}
