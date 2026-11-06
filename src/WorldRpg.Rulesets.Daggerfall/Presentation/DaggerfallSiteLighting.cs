using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Owns Engine point and ambient lights for one admitted site. Older RDB records without a color
/// retain Daggerfall's neutral-white default, while Engine owns culling and rendering.
/// </summary>
internal sealed class DaggerfallSiteLighting : IDisposable
{
    // Engine refuses a logical light ID another live light owns. A replacement session builds its site
    // before the session it replaces is disposed, so each lighting instance names its lights apart.
    private static long s_lastInstance;
    private readonly List<Light> _lights = [];
    private readonly Dictionary<long, (Light Light, LightRequest Request)> _mobileLights = [];
    private readonly Dictionary<long, (Light Light, LightRequest Request)> _spellLights = [];
    private readonly long _instance;
    private readonly string _profileId;
    private readonly List<(Light Light, LightRequest Request)> _points = [];
    private readonly List<LightRequest> _suspendedPoints = [];
    private readonly Dictionary<ulong, float> _cityIntensities = [];
    private bool? _lastCityLightsOn;
    private readonly IGraphicsService _graphics;
    private readonly ICameraViewService _camera;
    private readonly DaggerfallWorldProfileKind _profileKind;
    private readonly DaggerfallSiteLightingTuning _tuning;
    private Light? _magicCandle;
    private LightRequest? _magicCandleRequest;
    private readonly ulong _magicCandleId;
    private Light? _ambient;
    private readonly ulong _ambientId;
    private Light? _sun;
    private LightRequest? _sunRequest;
    private readonly ulong _sunId;
    private float _ambientLevel;
    private float _daylight = 1f;
    private float? _dungeonLevel;
    private float _flash;
    private readonly bool _carriesSky;
    private bool _disposed;
    private bool _suspended;

    /// <param name="carriesSky">Whether this site lights the world with its sun and ambient. A resident location beside the
    /// active one brings only its own lamps: there is one sky, and a second sun would light the same ground twice.</param>
    internal DaggerfallSiteLighting(IGraphicsService graphics, ICameraViewService camera, DaggerfallSiteProfile inputs,
        DaggerfallSiteLightingTuning tuning, DaggerfallCalendar calendar, bool carriesSky = true)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(inputs);

        _graphics = graphics;
        _camera = camera;
        _profileKind = inputs.ProfileKind;
        _carriesSky = carriesSky;
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        long instance = _instance = Interlocked.Increment(ref s_lastInstance);
        _profileId = inputs.ProfileKey.LogicalId;
        _ambientId = LogicalLightId(inputs.ProfileKey.LogicalId, instance, "ambient");
        _magicCandleId = LogicalLightId(inputs.ProfileKey.LogicalId, instance, "magic-candle");
        _sunId = LogicalLightId(inputs.ProfileKey.LogicalId, instance, "sun");
        _ambientLevel = AmbientLevel(calendar);

        List<Light> created = [];
        HashSet<ulong> lightIds = [_ambientId, _sunId, _magicCandleId];
        try
        {
            foreach (DaggerfallSiteLight light in inputs.Lights)
            {
                ulong lightId = LogicalLightId(inputs.ProfileKey.LogicalId, instance, $"authored:{light.Id}");
                if (!lightIds.Add(lightId))
                    throw new InvalidOperationException($"Site light '{light.Id}' collides with another admitted light identity.");
                LightRequest request = new(
                    lightId,
                    false,
                    0,
                    new LightDescriptor(
                        LightKind.Point,
                        light.Color,
                        light.Intensity * Scale,
                        true,
                        light.Position.ToVector(),
                        Vector3.Zero,
                        true,
                        light.Range,
                        2F,
                        0F,
                        0F,
                        LightShadowIntent.Disabled));
                if (light.CityLight)
                {
                    _cityIntensities.Add(lightId, light.Intensity * Scale);
                    request = CityRequest(request, calendar);
                }
                Light point = graphics.CreateLight(request);
                created.Add(point);
                _points.Add((point, request));
            }
            if (carriesSky && _profileKind == DaggerfallWorldProfileKind.Exterior) _sunRequest = SunRequest(calendar, calendar.IsDay ? 1f : 0f);
            CreateSky(created);
            _lights.AddRange(created);
            ApplyBackground();
        }
        catch
        {
            foreach (Light light in created.AsEnumerable().Reverse()) light.Dispose();
            throw;
        }
    }

    internal int Count => _lights.Count;

    /// <summary>
    /// What this site's sky gives a surface open to it, before the Engine divides by pi: the ambient in its colour and the
    /// sun whatever its angle, as a hand held up takes a low sun as well as a high one.
    /// </summary>
    internal Vector3 SkyLight => AmbientColor(_ambientLevel) * (_ambientLevel * Scale) + Vector3.One * (_sunRequest?.Descriptor.Intensity ?? 0);

    /// <summary>What this site's lamps, glowing mobiles, flying spells and magic candle give <paramref name="at"/>, by the renderer's own falloff.</summary>
    internal Vector3 LampLight(Vector3 at)
    {
        Vector3 light = Vector3.Zero;
        foreach ((_, LightRequest request) in _points) light += Reaching(request.Descriptor, at);
        foreach ((_, LightRequest request) in _mobileLights.Values) light += Reaching(request.Descriptor, at);
        foreach ((_, LightRequest request) in _spellLights.Values) light += Reaching(request.Descriptor, at);
        if (_magicCandleRequest is { } candle) light += Reaching(candle.Descriptor, at);
        return light;
    }

    // Matches the Engine's lighting.wgsl distance_attenuation, with no surface to turn from the light.
    private static Vector3 Reaching(LightDescriptor lamp, Vector3 at)
    {
        float distance = Vector3.Distance(lamp.Position, at);
        if (distance >= lamp.Range) return Vector3.Zero;
        float ratio = distance / lamp.Range;
        float window = 1 - ratio * ratio * ratio * ratio;
        return lamp.Color * (lamp.Intensity * window * window / MathF.Max(MathF.Pow(distance, lamp.Decay), .01f));
    }

    internal void UpdateMagicCandle(Vector3? position, DaggerfallNormalLightTuning tuning)
    {
        if (_disposed || _suspended) return;
        if (position is not Vector3 value)
        {
            if (_magicCandle is { } ended) { _lights.Remove(ended); ended.Dispose(); _magicCandle = null; _magicCandleRequest = null; }
            return;
        }
        var request = new LightRequest(_magicCandleId, false, 0, new LightDescriptor(LightKind.Point,
            Vector3.One, tuning.Intensity, true, value, Vector3.Zero, true, tuning.Range, 2f, 0f, 0f, LightShadowIntent.Disabled));
        if (_magicCandle is null) { _magicCandle = _graphics.CreateLight(request); _lights.Add(_magicCandle); }
        else _graphics.UpdateLight(new(_magicCandle, request));
        _magicCandleRequest = request;
    }


    internal void UpdateMobileLights(IEnumerable<WorldRpg.Kit.Actors.ActorState> actors,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions, DaggerfallMobileCatalogSet mobiles)
    {
        if (_disposed) return;
        HashSet<long> live = [];
        foreach (var actor in actors)
        {
            if (actor.IsDefeated || !definitions.TryGetValue(actor.DurableId, out var definition)
                || definition.MobileId is not int id || !mobiles.Mobiles.TryGetValue(id, out var mobile)
                || mobile.GlowColor is not Vector3 color) continue;
            live.Add(actor.DurableId);
            // SetupDemoEnemy's LightAura: local offset (0,.3,.2), intensity .1 and radius 4.
            Vector3 forward = WorldRpg.Kit.Actors.ActorHeading.Forward(actor.HeadingYawRadians);
            Vector3 position = actor.Position.ToVector() + Vector3.UnitY * .3f + forward * .2f;
            var request = new LightRequest(LogicalLightId(_profileId, _instance, $"mobile:{actor.DurableId}"), false, 0,
                new LightDescriptor(LightKind.Point, color, _tuning.MobileGlowIntensity, true, position, Vector3.Zero, true, _tuning.MobileGlowRange, 2f, 0f, 0f, LightShadowIntent.Disabled));
            if (_mobileLights.TryGetValue(actor.DurableId, out var held)) _graphics.UpdateLight(new(held.Light, request));
            else { held.Light = _graphics.CreateLight(request); _lights.Add(held.Light); }
            _mobileLights[actor.DurableId] = (held.Light, request);
        }
        foreach (long retired in _mobileLights.Keys.Where(id => !live.Contains(id)).ToArray())
        {
            Light light = _mobileLights[retired].Light;
            _mobileLights.Remove(retired); _lights.Remove(light); light.Dispose();
        }
    }

    /// <summary>
    /// Each flying spell carries a point light in its element's colour, as the donor's missile prefabs do
    /// (fire, cold, poison, shock, magic), retired when its flight ends.
    /// </summary>
    internal void UpdateSpellLights(IReadOnlyList<DaggerfallDungeonSpellFlightView> flights)
    {
        if (_disposed || _suspended) return;
        HashSet<long> live = [];
        foreach (DaggerfallDungeonSpellFlightView flight in flights)
        {
            live.Add(flight.Sequence);
            var request = new LightRequest(LogicalLightId(_profileId, _instance, $"spell:{flight.Sequence}"), false, 0,
                new LightDescriptor(LightKind.Point, SpellLightColor(flight.Element), _tuning.SpellLightIntensity, true, flight.Position.ToVector(),
                    Vector3.Zero, true, _tuning.SpellLightRange, 2f, 0f, 0f, LightShadowIntent.Disabled));
            if (_spellLights.TryGetValue(flight.Sequence, out var held)) _graphics.UpdateLight(new(held.Light, request));
            else { held.Light = _graphics.CreateLight(request); _lights.Add(held.Light); }
            _spellLights[flight.Sequence] = (held.Light, request);
        }
        foreach (long retired in _spellLights.Keys.Where(id => !live.Contains(id)).ToArray())
        {
            Light light = _spellLights[retired].Light;
            _spellLights.Remove(retired); _lights.Remove(light); light.Dispose();
        }
    }

    private static Vector3 SpellLightColor(int element) => element switch
    {
        0 => new(.604f, .094f, .031f),
        1 => new(.620f, .792f, .792f),
        2 => new(.396f, .627f, .235f),
        3 => new(.267f, .486f, .753f),
        _ => new(.396f, .537f, .471f),
    };

    internal void Rebase(Vector3 delta)
    {
        if (_suspended)
        {
            for (int index = 0; index < _suspendedPoints.Count; index++)
            {
                LightRequest request = _suspendedPoints[index];
                _suspendedPoints[index] = request with
                {
                    Descriptor = request.Descriptor with { Position = request.Descriptor.Position + delta },
                };
            }
            return;
        }
        for (int index = 0; index < _points.Count; index++)
        {
            (Light light, LightRequest request) = _points[index];
            request = request with { Descriptor = request.Descriptor with { Position = request.Descriptor.Position + delta } };
            _graphics.UpdateLight(new LightUpdateRequest(light, request));
            _points[index] = (light, request);
        }
    }

    internal void UpdateAmbient(DaggerfallCalendar calendar, float exteriorDaylight = 1f,
        float? dungeonLevel = null, float lightningFlash = 0f)
    {
        if (!float.IsFinite(exteriorDaylight) || exteriorDaylight is < 0f or > 1f
            || dungeonLevel is float variant && (!float.IsFinite(variant) || variant is < 0f or > 1f)
            || !float.IsFinite(lightningFlash) || lightningFlash < 0f)
            throw new ArgumentOutOfRangeException(nameof(exteriorDaylight), "Ambient context requires normalized daylight/zone values and finite nonnegative flash intensity.");
        UpdateCityLights(calendar);
        _daylight = exteriorDaylight;
        _dungeonLevel = dungeonLevel;
        _flash = lightningFlash;
        if (_carriesSky && _profileKind == DaggerfallWorldProfileKind.Exterior)
            _sunRequest = SunRequest(calendar, exteriorDaylight);
        if (_sun is { } sun && _sunRequest is { } sunRequest)
            _graphics.UpdateLight(new(sun, sunRequest));
        float level = AmbientLevel(calendar);
        if (level == _ambientLevel) return;
        if (_ambient is { } ambient)
            _graphics.UpdateLight(new LightUpdateRequest(ambient, AmbientRequest(level)));
        _ambientLevel = level;
    }

    // DaggerfallDateTime keeps city lamps on slightly beyond the ordinary night interval.
    internal static bool CityLightsOn(DaggerfallCalendar calendar) => calendar.Hour >= 17 || calendar.Hour < 8;

    /// <summary>Lights a location's window glass on the city lamps' hours.</summary>
    internal void UpdateWindowGlass(DaggerfallSiteAppearance appearance, DaggerfallCalendar calendar) =>
        appearance.UpdateWindowGlass(_tuning.WindowColor, CityLightsOn(calendar) ? _tuning.WindowLevel : 0f);

    /// <summary>Whether a site's lamps burn: outdoors on the city lamps' hours, and always under a roof.</summary>
    internal static bool LampsLit(DaggerfallWorldProfileKind kind, DaggerfallCalendar calendar) =>
        kind != DaggerfallWorldProfileKind.Exterior || CityLightsOn(calendar);

    /// <summary>Lights a site's refreshed lamps while they burn.</summary>
    internal void UpdateLampGlow(DaggerfallSiteAppearance appearance, DaggerfallCalendar calendar) =>
        appearance.UpdateLampGlow(LampsLit(_profileKind, calendar) ? _tuning.LampLevel : 0f);

    private LightRequest CityRequest(LightRequest request, DaggerfallCalendar calendar) =>
        _cityIntensities.TryGetValue(request.LogicalId, out float intensity)
            ? request with { Descriptor = request.Descriptor with { Intensity = CityLightsOn(calendar) ? intensity : 0f } }
            : request;

    internal void UpdateCityLights(DaggerfallCalendar calendar)
    {
        bool on = CityLightsOn(calendar);
        if (_cityIntensities.Count == 0 || _lastCityLightsOn == on) return;
        _lastCityLightsOn = on;
        for (int index = 0; index < _points.Count; index++)
        {
            var (light, prior) = _points[index];
            var next = CityRequest(prior, calendar);
            if (next == prior) continue;
            _graphics.UpdateLight(new(light, next));
            _points[index] = (light, next);
        }
        for (int index = 0; index < _suspendedPoints.Count; index++)
            _suspendedPoints[index] = CityRequest(_suspendedPoints[index], calendar);
    }

    /// <summary>
    /// Releases the site's light resources while retaining their authored requests. <paramref name="keepSky"/> leaves the
    /// sun and ambient standing: they light the wilderness too, after the location itself has unloaded.
    /// </summary>
    internal void Suspend(bool keepSky = false)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DaggerfallSiteLighting));
        if (!_suspended)
        {
            _suspendedPoints.Clear();
            _suspendedPoints.AddRange(_points.Select(value => value.Request));
            foreach (Light light in _lights.AsEnumerable().Reverse().Where(light => !IsSky(light))) light.Dispose();
            _lights.RemoveAll(light => !IsSky(light));
            _mobileLights.Clear();
            _spellLights.Clear();
            _points.Clear();
            _magicCandle = null;
            _magicCandleRequest = null;
            _suspended = true;
        }

        if (keepSky) return;
        foreach (Light light in _lights.AsEnumerable().Reverse()) light.Dispose();
        _lights.Clear();
        _ambient = null;
        _sun = null;
    }

    /// <summary>Recreates the retained lights after a location is admitted again.</summary>
    internal void Resume()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DaggerfallSiteLighting));
        if (!_suspended) return;
        List<Light> created = [];
        try
        {
            foreach (LightRequest request in _suspendedPoints)
            {
                Light light = _graphics.CreateLight(request);
                created.Add(light);
                _points.Add((light, request));
            }
            CreateSky(created);
            _lights.AddRange(created);
            _suspendedPoints.Clear();
            _suspended = false;
            ApplyBackground();
        }
        catch
        {
            DisposeCreated(created);
            throw;
        }
    }

    /// <summary>Stands the sky again over a suspended site, after a transition away from it fails.</summary>
    internal void RestoreSky()
    {
        List<Light> created = [];
        try
        {
            CreateSky(created);
            _lights.AddRange(created);
        }
        catch
        {
            DisposeCreated(created);
            throw;
        }
    }

    private bool IsSky(Light light) => ReferenceEquals(light, _sun) || ReferenceEquals(light, _ambient);

    /// <summary>Creates whichever of the sun and ambient this site carries and isn't holding.</summary>
    private void CreateSky(List<Light> created)
    {
        if (!_carriesSky) return;
        if (_sun is null && _sunRequest is { } sunRequest) created.Add(_sun = _graphics.CreateLight(sunRequest));
        if (_ambient is null) created.Add(_ambient = _graphics.CreateLight(AmbientRequest(_ambientLevel)));
    }

    private void DisposeCreated(List<Light> created)
    {
        foreach (Light light in created.AsEnumerable().Reverse())
        {
            if (ReferenceEquals(light, _sun)) _sun = null;
            if (ReferenceEquals(light, _ambient)) _ambient = null;
            light.Dispose();
        }
        _points.Clear();
    }

    internal void ApplyBackground()
    {
        if (_profileKind is DaggerfallWorldProfileKind.Interior or DaggerfallWorldProfileKind.Dungeon)
            _camera.SetBackgroundColor(new(new Color(0F, 0F, 0F, 1F)));
        else
            _camera.ClearSkyBackground(default);
    }

    private LightRequest AmbientRequest(float level) => new(
        _ambientId, false, 0,
        new LightDescriptor(LightKind.Ambient, AmbientColor(level), level * Scale, true,
            Vector3.Zero, Vector3.Zero, false, 0F, 0F, 0F, 0F, LightShadowIntent.Disabled));

    private LightRequest SunRequest(DaggerfallCalendar calendar, float daylight)
    {
        float phase = (calendar.SecondOfDay / 60f - DaggerfallCalendar.DawnHour * 60f)
            / ((DaggerfallCalendar.DuskHour - DaggerfallCalendar.DawnHour) * 60f);
        Vector3 direction = Vector3.Normalize(new(MathF.Cos(phase * MathF.PI), -MathF.Sin(phase * MathF.PI), 0f));
        return new(_sunId, false, 0, new LightDescriptor(LightKind.Directional, Vector3.One,
            daylight * _tuning.ExteriorSun, true, Vector3.Zero, direction, false, 0f, 0f, 0f, 0f, LightShadowIntent.Disabled));
    }

    /// <summary>What this kind of place multiplies its lights by.</summary>
    private float Scale => _profileKind switch
    {
        DaggerfallWorldProfileKind.Exterior => _tuning.ExteriorScale,
        DaggerfallWorldProfileKind.Interior => _tuning.InteriorScale,
        _ => _tuning.DungeonScale,
    };

    /// <summary>The ambient's colour at a level: outdoors it cools toward the night's, and indoors it stays white.</summary>
    private Vector3 AmbientColor(float level) => _profileKind != DaggerfallWorldProfileKind.Exterior ? Vector3.One
        : Vector3.Lerp(_tuning.NightColor, Vector3.One, Math.Clamp((level - _tuning.ExteriorNight) / (_tuning.ExteriorNoon - _tuning.ExteriorNight), 0, 1));

    private float AmbientLevel(DaggerfallCalendar calendar)
    {
        bool night = calendar.Hour is < 6 or >= 18;
        return _profileKind switch
        {
            DaggerfallWorldProfileKind.Interior => night ? _tuning.InteriorNight : _tuning.InteriorDay,
            DaggerfallWorldProfileKind.Dungeon => _dungeonLevel ?? _tuning.Dungeon,
            DaggerfallWorldProfileKind.Exterior => Math.Max(_flash,
                _tuning.ExteriorNight + (_tuning.ExteriorNoon - _tuning.ExteriorNight) * (night ? 0f : _daylight)),
            _ => throw new ArgumentOutOfRangeException(nameof(_profileKind)),
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        foreach (Light light in _lights.AsEnumerable().Reverse())
        {
            try { light.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        _mobileLights.Clear();
        _spellLights.Clear();
        _points.Clear();
        _lights.Clear();
        _suspendedPoints.Clear();
        _ambient = null;
        _sun = null;
        _magicCandle = null;
        _magicCandleRequest = null;
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private static ulong LogicalLightId(string profile, long instance, string id)
    {
        const ulong jsonSafeMaximum = (1UL << 53) - 1UL;
        ulong hash = StableHash.Fnv1a64($"site-light:{profile}:{instance}:{id}");
        // Engine publishes light IDs through JSON; keep identities inside that exact range.
        return (hash % jsonSafeMaximum) + 1UL;
    }
}
