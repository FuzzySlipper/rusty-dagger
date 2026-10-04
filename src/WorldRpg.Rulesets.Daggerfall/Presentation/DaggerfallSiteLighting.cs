using System.Numerics;
using Rusty.Engine;
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
    private readonly List<(Light Light, LightRequest Request)> _points = [];
    private readonly List<LightRequest> _suspendedPoints = [];
    private readonly IGraphicsService _graphics;
    private readonly ICameraViewService _camera;
    private readonly DaggerfallWorldProfileKind _profileKind;
    private readonly DaggerfallSiteLightingTuning _tuning;
    private Light? _ambient;
    private readonly ulong _ambientId;
    private Light? _sun;
    private LightRequest? _sunRequest;
    private readonly ulong _sunId;
    private float _ambientLevel;
    private float _daylight = 1f;
    private float? _dungeonLevel;
    private float _flash;
    private bool _disposed;
    private bool _suspended;

    internal DaggerfallSiteLighting(IGraphicsService graphics, ICameraViewService camera, DaggerfallSiteProfile inputs,
        DaggerfallSiteLightingTuning tuning, DaggerfallCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(inputs);

        _graphics = graphics;
        _camera = camera;
        _profileKind = inputs.ProfileKind;
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        long instance = Interlocked.Increment(ref s_lastInstance);
        _ambientId = LogicalLightId(inputs.ProfileKey.LogicalId, instance, "ambient");
        _sunId = LogicalLightId(inputs.ProfileKey.LogicalId, instance, "sun");
        _ambientLevel = AmbientLevel(calendar);

        List<Light> created = [];
        HashSet<ulong> lightIds = [_ambientId];
        try
        {
            foreach (DaggerfallSiteLight light in inputs.Lights)
            {
                ulong lightId = LogicalLightId(inputs.ProfileKey.LogicalId, instance, light.Id);
                if (!lightIds.Add(lightId))
                    throw new InvalidOperationException($"Site light '{light.Id}' collides with another admitted light identity.");
                LightRequest request = new(
                    lightId,
                    false,
                    0,
                    new LightDescriptor(
                        LightKind.Point,
                        light.Color,
                        light.Intensity,
                        true,
                        light.Position.ToVector(),
                        Vector3.Zero,
                        true,
                        light.Range,
                        2F,
                        0F,
                        0F,
                        LightShadowIntent.Disabled));
                Light point = graphics.CreateLight(request);
                created.Add(point);
                _points.Add((point, request));
            }
            if (_profileKind == DaggerfallWorldProfileKind.Exterior)
            {
                LightRequest sunRequest = SunRequest(calendar, calendar.IsDay ? 1f : 0f);
                _sunRequest = sunRequest;
                _sun = graphics.CreateLight(sunRequest);
                created.Add(_sun);
            }
            _ambient = graphics.CreateLight(AmbientRequest(_ambientLevel));
            created.Add(_ambient);
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
        _daylight = exteriorDaylight;
        _dungeonLevel = dungeonLevel;
        _flash = lightningFlash;
        if (_profileKind == DaggerfallWorldProfileKind.Exterior)
            _sunRequest = SunRequest(calendar, exteriorDaylight);
        if (!_suspended && _sun is { } sun && _sunRequest is { } sunRequest)
            _graphics.UpdateLight(new(sun, sunRequest));
        float level = AmbientLevel(calendar);
        if (level == _ambientLevel) return;
        if (!_suspended && _ambient is { } ambient)
            _graphics.UpdateLight(new LightUpdateRequest(ambient, AmbientRequest(level)));
        _ambientLevel = level;
    }

    /// <summary>Releases point and ambient light resources while retaining their authored requests.</summary>
    internal void Suspend()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DaggerfallSiteLighting));
        if (_suspended) return;
        _suspendedPoints.Clear();
        _suspendedPoints.AddRange(_points.Select(value => value.Request));
        foreach (Light light in _lights.AsEnumerable().Reverse()) light.Dispose();
        _points.Clear();
        _lights.Clear();
        _ambient = null;
        _sun = null;
        _suspended = true;
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
            if (_sunRequest is { } sunRequest)
            {
                _sun = _graphics.CreateLight(sunRequest);
                created.Add(_sun);
            }
            Light ambient = _graphics.CreateLight(AmbientRequest(_ambientLevel));
            _ambient = ambient;
            created.Add(ambient);
            _lights.AddRange(created);
            _suspendedPoints.Clear();
            _suspended = false;
            ApplyBackground();
        }
        catch
        {
            foreach (Light light in created.AsEnumerable().Reverse()) light.Dispose();
            _points.Clear();
            _lights.Clear();
            _ambient = null;
        _sun = null;
            throw;
        }
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
        new LightDescriptor(LightKind.Ambient, Vector3.One, level, true,
            Vector3.Zero, Vector3.Zero, false, 0F, 0F, 0F, 0F, LightShadowIntent.Disabled));

    private LightRequest SunRequest(DaggerfallCalendar calendar, float daylight)
    {
        float phase = (calendar.SecondOfDay / 60f - DaggerfallCalendar.DawnHour * 60f)
            / ((DaggerfallCalendar.DuskHour - DaggerfallCalendar.DawnHour) * 60f);
        Vector3 direction = Vector3.Normalize(new(MathF.Cos(phase * MathF.PI), -MathF.Sin(phase * MathF.PI), 0f));
        return new(_sunId, false, 0, new LightDescriptor(LightKind.Directional, Vector3.One,
            daylight, true, Vector3.Zero, direction, false, 0f, 0f, 0f, 0f, LightShadowIntent.Disabled));
    }

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
        _points.Clear();
        _lights.Clear();
        _suspendedPoints.Clear();
        _ambient = null;
        _sun = null;
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private static ulong LogicalLightId(string profile, long instance, string id)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        const ulong jsonSafeMaximum = (1UL << 53) - 1UL;
        ulong hash = offset;
        foreach (char value in $"site-light:{profile}:{instance}:{id}") { hash ^= value; hash *= prime; }
        // Engine publishes light IDs through JSON; keep identities inside that exact range.
        return (hash % jsonSafeMaximum) + 1UL;
    }
}
