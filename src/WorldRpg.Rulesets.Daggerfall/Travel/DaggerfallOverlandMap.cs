using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>A name the travel map shows over a place, the party or its destination, where the view puts it: across and
/// down the map's view, from its top left, 0 to 1.</summary>
internal sealed record DaggerfallOverlandLabel(string Name, string Kind, double Across, double Down);

/// <summary>The travel map's look as this run draws it: the tuning's, or what a developer set by eye with map.look.</summary>
internal readonly record struct DaggerfallOverlandLook(DaggerfallOverlandGrading Grading, float Moon, double Haze);

/// <summary>
/// The expedition's travel map as a world the Engine draws: the land (<see cref="DaggerfallOverlandTerrain"/>) in the
/// backdrop, the places on it as the art set's small model for each kind of place, the party's and the bands' figures, the
/// ring of the party's sight, the way it goes, where it heads and the pin, and rain or snow where the world's weather falls
/// (<see cref="DaggerfallOverlandFall"/>), under a camera of its own that orbits, tilts, pans and zooms
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
    private readonly DaggerfallTravelMapArt? _art;
    /// <summary>Each kind of place's model, made the first time one is drawn.</summary>
    private readonly Dictionary<DaggerfallSiteKind, Appearance> _places = [];
    private readonly Dictionary<Color, Material> _paints = [];
    private DaggerfallOverlandFall? _fall;
    /// <summary>The drops' textures and atlases, released after the drops, atlases first.</summary>
    private readonly List<IDisposable> _owned = [];
    /// <summary>The height in backdrop units the view looks at, where drops fall to, and the view's eye.</summary>
    private double _lookedAt;
    private Vector3 _viewEye;
    private readonly Dictionary<(int X, int Y), List<DaggerfallTravelDestination>> _blocks = [];
    private readonly Dictionary<(int X, int Z), double> _heights = [];
    private readonly List<AppearanceFact> _facts = [];
    private readonly List<DaggerfallOverlandLabel> _labels = [];
    private DaggerfallOverlandModels? _models;
    private readonly Func<int, NormalizedActorSprite?> _sprite;
    private readonly Func<int> _partyMobile;
    private readonly BillboardMode _facing;
    /// <summary>Each mobile's billboard, made the first time it is drawn; none where the site publishes no sprite.</summary>
    private readonly Dictionary<int, (SpriteAtlas Atlas, Appearance Look, RenderResource Texture)?> _billboards = [];
    private Light? _moon;
    private LightRequest? _moonRequest;
    /// <summary>The parts whose identities stay put from update to update: the party's billboard, which the moonlight hangs from.</summary>
    private const ulong PartyPart = 0, MoonlightPart = 1, RainPart = 2, SnowPart = 3, FirstCountedPart = 4;
    private Camera? _camera;
    private CameraDescriptor _backdropView;
    private double _aspect = 16d / 9;
    private bool _open;
    private bool _disposed;

    /// <param name="destinations">Every place on the world map.</param>
    /// <param name="playerCamera">Makes the player's camera the active one again.</param>
    /// <param name="sprite">A mobile's billboard as the world draws it, or none where the site publishes none.</param>
    /// <param name="partyMobile">The mobile the party is drawn as: the class figure of the player's career.</param>
    /// <param name="facing">How the billboards turn to the camera, as the world's actors do.</param>
    /// <param name="art">The art set's place models and drops; without it no places stand and nothing falls.</param>
    internal DaggerfallOverlandMap(IEngineContext engine, DaggerfallOverlandTerrain terrain, DaggerfallOverlandTuning tuning,
        IReadOnlyList<DaggerfallTravelDestination> destinations, Action playerCamera,
        Func<int, NormalizedActorSprite?> sprite, Func<int> partyMobile, BillboardMode facing, DaggerfallTravelMapArt? art)
    {
        _art = art;
        _sprite = sprite ?? throw new ArgumentNullException(nameof(sprite));
        _partyMobile = partyMobile ?? throw new ArgumentNullException(nameof(partyMobile));
        _facing = facing;
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _destinations = destinations ?? throw new ArgumentNullException(nameof(destinations));
        _playerCamera = playerCamera ?? throw new ArgumentNullException(nameof(playerCamera));
        View = new(tuning);
        Look = new(tuning.Grading, tuning.Moonlight.Intensity, 1);
    }

    /// <summary>The map's look this run.</summary>
    internal DaggerfallOverlandLook Look { get; private set; }

    /// <summary>The haze the land is seen through in a weather where the party is, per metre.</summary>
    internal double Haze(DaggerfallWeatherKind weather) => _tuning.Haze[weather] * Look.Haze;

    /// <summary>Sets one part of the look by eye for this run, or says why not.</summary>
    internal string? Tune(string key, double value)
    {
        if (!double.IsFinite(value)) return "map.look takes a number.";
        DaggerfallOverlandGrading grading = Look.Grading;
        DaggerfallOverlandLook next = key switch
        {
            "contrast" => Look with { Grading = grading with { Contrast = (float)value } },
            "saturation" => Look with { Grading = grading with { Saturation = (float)value } },
            "temperature" => Look with { Grading = grading with { Temperature = (float)value } },
            "moon" when value >= 0 => Look with { Moon = (float)value },
            "haze" when value >= 0 => Look with { Haze = value },
            _ => Look,
        };
        if (next == Look && key is not ("contrast" or "saturation" or "temperature" or "moon" or "haze")) return "map.look takes contrast, saturation, temperature, moon or haze.";
        if (next.Grading.Contrast is < -1 or > 1 || next.Grading.Saturation is < -1 or > 1 || next.Grading.Temperature is < -1 or > 1) return "A grading lies from -1 to 1.";
        Look = next;
        if (_open) Grade();
        return null;
    }

    private void Grade()
    {
        DaggerfallOverlandGrading wet = _wet ? _tuning.Wet : default, grading = Look.Grading;
        float cool = _tuning.Moonlight.Cool * _night;
        _engine.CameraView.SetColorGrading(new(Math.Clamp(grading.Temperature + wet.Temperature + cool, -1, 1), 0,
            Math.Clamp(grading.Contrast + wet.Contrast, -1, 1), Math.Clamp(grading.Saturation + wet.Saturation, -1, 1)));
    }

    /// <summary>Whether the weather where the party is greys the map's colours.</summary>
    private bool _wet;
    /// <summary>How far into the night the grading last cooled for, in steps too small to see.</summary>
    private float _night;

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
    internal void Prepare(double x, double z, bool winter)
    {
        if (_open) return;
        // The drops the map took away as it closed are out of the snapshots now.
        _fall?.Release();
        _terrain.Follow(x, z, winter, shown: false);
    }

    /// <summary>
    /// Draws the map for this update: the view follows the party unless panned; the land, the places, the party, the
    /// bands, the sight ring, the way and the pin stand where the expedition has them, under the map's camera at the
    /// player's <paramref name="eye"/>. While it is open its colours are graded stronger, and a moonlight in the backdrop
    /// alone lights it by <paramref name="night"/> (0 by day, 1 at the dark of night), so the land reads at every hour;
    /// grey or wet <paramref name="weather"/> where the party is grades them cooler and greyer.
    /// </summary>
    internal void Show(DaggerfallExpedition expedition, Vector3 eye, bool winter, float night, DaggerfallWeatherKind weather)
    {
        bool wet = weather is not (DaggerfallWeatherKind.Sunny or DaggerfallWeatherKind.Cloudy);
        if (_disposed) return;
        (double partyX, double partyZ) = DaggerfallOverlandView.Metres(expedition.Position.X, expedition.Position.Y);
        bool opening = !_open;
        if (opening)
        {
            View.Open(partyX, partyZ);
            _fall?.Release();
        }
        View.Track(partyX, partyZ);
        _terrain.UpdateAtmosphere(weather, 1 - Math.Clamp(night, 0, 1), winter);
        _terrain.Follow(View.TargetX, View.TargetZ, winter, shown: true);
        Vector3 target = View.Target(Height(View.TargetX, View.TargetZ));
        _lookedAt = Backdrop(target).Y;
        Vector3 viewEye = View.Eye(target.Y);
        _viewEye = Backdrop(viewEye);
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
        float dark = MathF.Round(Math.Clamp(night, 0, 1) * 20) / 20;
        if (opening || wet != _wet || dark != _night)
        {
            (_wet, _night) = (wet, dark);
            Grade();
        }
        // The moonlight hangs from the party's billboard, which stands in the backdrop once a snapshot has carried it.
        else Moonlight(night);
        _open = true;
        Compose(expedition, partyX, partyZ);
    }

    /// <summary>A light that lights the map's land and nothing of the world, brighter the darker the night.</summary>
    private void Moonlight(float night)
    {
        DaggerfallOverlandMoonlight moon = _tuning.Moonlight;
        LightRequest request = new(DaggerfallPresentationObjectIds.TravelMap(MoonlightPart), true, DaggerfallPresentationObjectIds.TravelMap(PartyPart),
            new LightDescriptor(LightKind.Directional, moon.Color, Look.Moon * Math.Clamp(night, 0, 1), true, Vector3.Zero,
                Vector3.Normalize(new Vector3(-.35f, -.8f, -.45f)), false, 0, 0, 0, 0, LightShadowIntent.Disabled));
        if (_moon is null) _moon = _engine.Graphics.CreateLight(request);
        else if (request != _moonRequest) _engine.Graphics.UpdateLight(new(_moon, request));
        _moonRequest = request;
    }

    /// <summary>
    /// Rain or snow over the map for this update's real <paramref name="seconds"/>, where the world's
    /// <paramref name="weather"/> at a map pixel falls, lit by <paramref name="daylight"/> (0 to 1). It falls over what the
    /// map's view showed last, and the map's next list draws it.
    /// </summary>
    internal void Fall(Func<DaggerfallTravelMapPixel, DaggerfallWeatherConditions> weather, float daylight, double seconds)
    {
        if (!_open || _art is null || _disposed) return;
        _fall ??= new(_engine.Graphics, _tuning.Fall, Atlas(_art.Rain, 64), Atlas(_art.Snow, 32));
        _fall.Update(_backdropView, _aspect, _viewEye, _tuning.FieldOfView, View.Distance, _lookedAt,
            (x, z) => weather(DaggerfallOverlandView.Pixel(x, z)), daylight, seconds);
    }

    /// <summary>A drop's atlas: <see cref="DaggerfallOverlandFall.FadeFrames"/> square frames of <paramref name="side"/> texels, side by side.</summary>
    private SpriteAtlas Atlas(DaggerfallArtFile file, int side)
    {
        RenderResource texture = _engine.Graphics.OpenTexture(_engine.Content, file.Path, file.Sha256, TextureFilter.Linear).Handle;
        _owned.Add(texture);
        SpriteAtlas atlas = _engine.Graphics.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture, SpriteAtlasAdapter.ToAtlasFrames(
            side * DaggerfallOverlandFall.FadeFrames, side,
            [.. Enumerable.Range(0, DaggerfallOverlandFall.FadeFrames).Select(frame => new NormalizedSpriteFrame((uint)frame, frame * side, 0, side, side))])));
        _owned.Add(atlas);
        return atlas;
    }

    /// <summary>Closes the map: the player's camera is active again, the land hides with its tiles kept, and nothing falls.</summary>
    internal void Close()
    {
        if (!_open) return;
        _open = false;
        _fall?.Clear();
        _facts.Clear();
        _labels.Clear();
        // Its parent leaves the scene with the map's parts.
        _moon?.Dispose();
        _moon = null;
        _moonRequest = null;
        _engine.CameraView.SetColorGrading(default);
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
        _moon?.Dispose();
        _camera?.Dispose();
        _models?.Dispose();
        _fall?.Dispose();
        for (int index = _owned.Count - 1; index >= 0; index--) _owned[index].Dispose();
        foreach (Appearance place in _places.Values) place.Dispose();
        foreach (Material paint in _paints.Values) paint.Dispose();
        foreach ((SpriteAtlas atlas, Appearance look, RenderResource texture) in _billboards.Values.OfType<(SpriteAtlas, Appearance, RenderResource)>())
        {
            look.Dispose();
            atlas.Dispose();
            texture.Dispose();
        }
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
        ulong next = FirstCountedPart;
        // Standards and dots keep their size on the screen better than true size would: they grow with the distance's root,
        // and close in still stand over the houses.
        float grow = Math.Max(.75f, (float)Math.Sqrt(View.Distance / _tuning.Distance.Opening));
        float token = (float)(_tuning.Token * grow), dot = token * .14f;

        void Stand(Appearance look, double x, double z, float scale, float turn = 0, float sink = 0, ulong? part = null)
        {
            Vector3 at = new((float)x, (float)Height(x, z) - sink, (float)z);
            _facts.Add(new AppearanceFact(DaggerfallPresentationObjectIds.TravelMap(part ?? next++), false, 0,
                new Transform(Backdrop(at), Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn), new Vector3(scale * DaggerfallOverlandTerrain.Scale)),
                look, true, RenderLayer.Backdrop));
        }
        void Standard(DaggerfallOverlandBanner banner, double x, double z, ulong? part = null)
        {
            Stand(models.Pole, x, z, token, part: part);
            Stand(models.Flags[banner], x, z, token);
        }
        // A figure stands as its mobile's billboard, sized in screen pixels so the party and the bands read the same at
        // every zoom, or as a standard where the site has no sprite for it. Its transform takes no scale.
        void Figure(int? mobile, DaggerfallOverlandBanner banner, double x, double z, ulong? part = null)
        {
            if (mobile is int id && Billboard(id) is { } look) Stand(look, x, z, 1 / DaggerfallOverlandTerrain.Scale, part: part);
            else Standard(banner, x, z, part);
        }
        void Label(string name, string kind, double x, double z, float above)
        {
            if (_labels.Count >= MostLabels) return;
            CameraProjectionResult seen = CameraQueries.Project(_backdropView, _aspect, Backdrop(new((float)x, (float)Height(x, z) + above, (float)z)));
            if (seen.InClip) _labels.Add(new(name, kind, seen.NormalizedPoint.X, 1 - seen.NormalizedPoint.Y));
        }

        // The places about the view, the known ones named; the party sees those it does not know within its sight. A model
        // is in house widths, and places grow farther out as the standards do.
        double reach = View.Distance * PlaceReach, sight = expedition.PartySight * Cell;
        float house = (float)_tuning.House * Math.Max(1, (float)Math.Sqrt(View.Distance / _tuning.Distance.Opening));
        // Settlements, temples and dungeons are named from any distance; homes, farms, inns and graveyards close in.
        bool close = View.Distance <= _tuning.Distance.Opening / 2;
        foreach (DaggerfallTravelDestination place in PlacesAbout(View.TargetX, View.TargetZ, reach))
        {
            bool known = expedition.Known(place.Id);
            (double x, double z) = DaggerfallOverlandView.Metres(place.MapPixel.X + .5, place.MapPixel.Y + .5);
            if (!known && Math.Sqrt(((x - partyX) * (x - partyX)) + ((z - partyZ) * (z - partyZ))) > sight) continue;
            // The nearest places come first, so a wide view leaves out only the farthest.
            if (_facts.Count >= MostPlaceParts) break;
            if (Place(place.Kind) is { } model)
            {
                // Each turned its own way, sunk a little so it sits into a slope.
                uint seed = (uint)HashCode.Combine(place.Id.Region, place.Id.Index);
                Stand(model, x, z, house, (float)(seed % 360 * Math.PI / 180), house * .03f);
            }
            // The party's own place is named under its figure instead, which would stand behind this name.
            if (known && (close || Named.Contains(place.Kind)) && place.MapPixel != expedition.Party)
                Label(place.Name, DaggerfallSiteKinds.Label(place.Kind), x, z, (float)(_tuning.House * 2.6));
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
        Figure(_partyMobile(), DaggerfallOverlandBanner.Party, partyX, partyZ, PartyPart);
        // The rain and snow, each one batch whose drops stand in the backdrop already.
        foreach ((Appearance? drops, ulong part) in new[] { (_fall?.Rain, RainPart), (_fall?.Snow, SnowPart) })
            if (drops is not null)
                _facts.Add(new AppearanceFact(DaggerfallPresentationObjectIds.TravelMap(part), false, 0, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One),
                    drops, true, RenderLayer.Backdrop));
        // Figures are a fixed height on the screen, so their names stand at their feet and the page sets them clear of the figure.
        string? here = expedition.LocationHere is { } site && expedition.Known(site.Id) ? site.Name : null;
        Label(here is null ? "Your party" : $"Your party · {here}", "party", partyX, partyZ, 0);

        // The bands the party sees, ringed in red when giving chase or amber when in an active clash.
        foreach (DaggerfallExpeditionBandView band in expedition.SeenBands)
        {
            (double x, double z) = DaggerfallOverlandView.Metres(band.X, band.Y);
            Figure(band.Mobile, Banner(band.Kind), x, z);
            if (band.Spotted) Stand(models.Dots[DaggerfallOverlandDot.Spotted], x, z, token * .6f, sink: -2);
            else if (band.IsClash) Stand(models.Dots[DaggerfallOverlandDot.Clash], x, z, token * .65f, sink: -2);
            string labelKind = band.IsClash
                ? "clash"
                : band.Spotted
                    ? "chase"
                    : band.Camp
                        ? "camp"
                        : band.Kind switch
                        {
                            DaggerfallBandKind.Caravan => "caravan",
                            DaggerfallBandKind.Courier => "courier",
                            DaggerfallBandKind.Patrol => "patrol",
                            DaggerfallBandKind.Hostile => "hostile",
                            _ => "band",
                        };
            Label(band.Name, labelKind, x, z, 0);
        }
    }

    /// <summary>
    /// A mobile's billboard as the world's actors draw it, facing the camera in its resting frame, a tuned height in screen
    /// pixels at every zoom; made the first time it is drawn and kept, or none where the site publishes no sprite for it.
    /// Like a token on a map it draws over the places and the land, so a party standing in a town is never lost among its roofs.
    /// </summary>
    private Appearance? Billboard(int mobile)
    {
        if (!_billboards.TryGetValue(mobile, out var made))
        {
            made = null;
            if (_sprite(mobile) is { } sprite && sprite.Size.Y > 0)
            {
                RenderResource texture = _engine.Graphics.OpenTexture(_engine.Content, sprite.TexturePath, sprite.TextureSha256).Handle;
                SpriteAtlas atlas = _engine.Graphics.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture, SpriteAtlasAdapter.ToAtlasFrames(sprite.AtlasWidth,
                    sprite.AtlasHeight, [.. sprite.Frames.Select(frame => new NormalizedSpriteFrame(frame.Id, frame.X, frame.Y, frame.Width, frame.Height, frame.DisplaySize))])));
                float height = (float)_tuning.Figure;
                Appearance look = _engine.Graphics.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(atlas, sprite.InitialFrameId,
                    sprite.Pivot, new Vector2(height * sprite.Size.X / sprite.Size.Y, height), _facing, SpriteSizeMode.Pixel, 0, SpriteDepthPolicy.DepthTestOff,
                    new Color(1, 1, 1, 1)));
                made = (atlas, look, texture);
            }
            _billboards[mobile] = made;
        }
        return made is var (_, appearance, _) ? appearance : null;
    }

    /// <summary>
    /// A kind of place's model from the art set, a static mesh read and uploaded off the update and painted flat in its
    /// slots' colours, made the first time one is drawn and shared by every place of its kind; none without the set.
    /// </summary>
    private Appearance? Place(DaggerfallSiteKind kind)
    {
        if (_places.TryGetValue(kind, out Appearance? model)) return model;
        if (_art?.Places.GetValueOrDefault(kind) is not { } place) return null;
        model = _engine.Graphics.CreateStaticMesh(_engine.Content, place.File.Path, place.File.Sha256, new Color(1, 1, 1, 1), deferred: true);
        _places.Add(kind, model);
        _engine.Graphics.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(model,
            place.Slots.Select((colour, slot) => new MeshMaterialBinding((uint)slot, Paint(colour))).ToArray()));
        return model;
    }

    /// <summary>A flat-shaded material of one colour, made once for every place model that uses it.</summary>
    private Material Paint(Color colour)
    {
        if (!_paints.TryGetValue(colour, out Material? material))
            _paints.Add(colour, material = _engine.Graphics.CreateMaterial(
                new MaterialRequest(colour, default(RenderResourceReference), .9f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with { FlatShading = true }));
        return material;
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
        DaggerfallBandKind.Caravan => DaggerfallOverlandBanner.Caravan,
        DaggerfallBandKind.Courier => DaggerfallOverlandBanner.Courier,
        DaggerfallBandKind.Patrol => DaggerfallOverlandBanner.Patrol,
        DaggerfallBandKind.Hostile => DaggerfallOverlandBanner.Hostile,
        DaggerfallBandKind.Clash => DaggerfallOverlandBanner.Clash,
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
