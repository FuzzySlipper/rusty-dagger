using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Session-owned retained Engine weather resources over the currently admitted site.</summary>
internal sealed class DaggerfallWeatherPresentation : IDisposable
{
    private readonly IEngineContext _engine;
    private readonly DaggerfallSkyMedia? _media;
    private readonly DaggerfallWeatherTuning _weather;
    private readonly DaggerfallAmbientTuning _tuning;
    private readonly DaggerfallHorizonTuning _horizon;
    private readonly DaggerfallAmbientPresentation _ambient;
    private readonly Dictionary<string, RenderResource> _sky = new(StringComparer.Ordinal);
    private Color _skyColor = new(.5f,.5f,.5f,1);
    private (float Cover, Vector2 Drift, Vector3 Color) _clouds;
    private float _falling;
    private bool _fallingSnow, _outside, _closed;
    private (WetnessRequest Wet, SnowRequest Snow, WindRequest Wind)? _ground;
    private DaggerfallMoons? _moons;
    private bool _disposed;

    internal DaggerfallWeatherPresentation(IEngineContext engine, DaggerfallSkyMedia? media,
        DaggerfallWeatherTuning weather, DaggerfallAmbientTuning tuning, DaggerfallHorizonTuning horizon,
        DaggerfallPresentationAudioTuning audioTuning, Func<DaggerfallWorldProfileKey, string, AudioClip?> openClip)
    {
        _engine = engine; _media = media; _weather = weather; _tuning = tuning.Validate(); _horizon = horizon;
        _ambient = new(engine.Audio, engine.Random, tuning, openClip, audioTuning);
    }

    internal void Update(DaggerfallSiteProjection projection, DaggerfallCalendar calendar, int climate,
        DaggerfallWeatherConditions conditions, int variant, Vector3 player, bool sheltered, double seconds, bool playing, float? horizonFog)
    {
        DaggerfallWeatherKind weather = conditions.Kind;
        var inputs = projection.Inputs;
        var zones = inputs.AmbientZones.Where(zone => zone.Contains(player - projection.WorldOffset)).ToArray();
        bool castle = zones.Any(zone => zone.Kind == DaggerfallAmbientZoneKind.Castle);
        bool special = zones.Any(zone => zone.Kind == DaggerfallAmbientZoneKind.SpecialArea);
        _ambient.Update(new(inputs.ProfileKey, weather, !calendar.IsDay, sheltered, castle, special), player, seconds, playing);
        // Moons stand with the cleared sky: the classic's painted one has none, and thick weather closes over them.
        bool moons = _media is not null && horizonFog is not null && weather is DaggerfallWeatherKind.Sunny or DaggerfallWeatherKind.Cloudy or DaggerfallWeatherKind.Overcast;
        if (moons) _moons ??= new(_engine, _media!, _horizon);
        projection.Lighting.LightNightBy(horizonFog is null ? 1 : _horizon.MoonlessNight + ((1 - _horizon.MoonlessNight) * (moons ? _moons!.Moonlight(calendar) : 0)));
        projection.Lighting.UpdateAmbient(calendar, _weather.Daylight(calendar, weather),
            castle || special ? _tuning.ZoneAmbient : null, _ambient.FlashIntensity);
        projection.Lighting.UpdateWindowGlass(projection.Appearance, calendar);
        projection.Lighting.UpdateLampGlow(projection.Appearance, calendar);
        // Over a cleared sky, thick weather closes the sky in its own air. The world fades exactly into that, whatever the
        // panorama of the day would have shown under the clouds.
        bool outside = inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior;
        Vector3 air = _horizon.AirColors[(int)weather];
        bool closed = outside && horizonFog is not null && air != Vector3.Zero;
        float daylight = _weather.Daylight(calendar, weather);
        if (outside && _media is not null && !closed)
        {
            DaggerfallSkySelection sky = _media.Select(climate, calendar, weather, variant);
            SelectSky(horizonFog is null ? sky : _media.Cleared(sky));
        }
        else
        {
            // A panorama selected above replaces whatever stood; only a sky that goes without one needs its background put back.
            bool cleared = ClearSky();
            if (!closed && (cleared || _closed)) projection.Lighting.ApplyBackground();
        }
        air = (air * MathF.Max(daylight, _horizon.NightAir)) + new Vector3(_ambient.FlashIntensity * _horizon.FlashAir);
        Color airColor = new(air.X, air.Y, air.Z, 1);
        _engine.CameraView.SetFog(Fog(inputs.ProfileKind, weather, horizonFog, closed ? airColor : null));
        if (closed) _engine.CameraView.SetBackgroundColor(new(airColor));
        _closed = closed;
        Vector2 wind = Blowing(conditions);
        SetClouds(horizonFog is null || weather == DaggerfallWeatherKind.Fog ? 0
            : weather is DaggerfallWeatherKind.Sunny or DaggerfallWeatherKind.Cloudy ? _horizon.CloudCoverage : _horizon.StormCloudCoverage,
            wind * _horizon.CloudDrift.Length() * (1 + (conditions.Wind * _tuning.WindDrift)), _horizon.CloudColors[(int)weather]);
        SetFall(conditions, outside, sheltered, wind, daylight, seconds);
        SetGround(conditions, outside, wind, calendar.Season == DaggerfallSeason.Winter);
        if (moons) _moons!.Show(calendar, daylight);
        else _moons?.Hide();
    }

    private void SetClouds(float cover, Vector2 drift, Vector3 color)
    {
        if (_clouds == (cover, drift, color)) return;
        _clouds = (cover, drift, color);
        _engine.CameraView.SetClouds(new(cover, drift, _horizon.CloudAltitudeMetres, _horizon.CloudScaleMetres,
            color, 0, CloudKind.Cumulus, _horizon.CloudTwilightDegrees) { PixelMetres = _horizon.CloudPixelMetres, ShadeSteps = _horizon.CloudShadeSteps });
    }

    /// <summary>The way the wind blows over the ground: x east and y south, from its compass degree.</summary>
    private static Vector2 Blowing(DaggerfallWeatherConditions conditions)
    {
        float angle = conditions.WindDegrees * MathF.PI / 180;
        return new(MathF.Sin(angle), -MathF.Cos(angle));
    }

    /// <summary>
    /// Rain or snow falling around the player, driven by the wind. It eases with the weather and at a shelter's edge; through
    /// a door it is there or gone at once, so none falls in the room.
    /// </summary>
    private void SetFall(DaggerfallWeatherConditions conditions, bool outside, bool sheltered, Vector2 wind, float daylight, double seconds)
    {
        float target = outside && !sheltered ? conditions.Falling : 0;
        if (target > 0) _fallingSnow = conditions.Kind == DaggerfallWeatherKind.Snow;
        _falling = outside != _outside ? target
            : _falling + ((target - _falling) * (1 - MathF.Exp(-(float)seconds / _tuning.FallEaseSeconds)));
        _outside = outside;
        float light = _tuning.NightFall + ((1 - _tuning.NightFall) * Math.Clamp(daylight, 0, 1));
        float drift = conditions.Wind * _tuning.WindDrift;
        if (_fallingSnow)
            _engine.CameraView.SetPrecipitation(new((uint)(_tuning.SnowDrops * _falling), PrecipitationShape.Flake,
                new(wind.X * drift, -_tuning.SnowSpeed, wind.Y * drift), _tuning.SnowSize, 0, new(light, light, light * 1.04f, .9f), false,
                _tuning.PrecipitationRadius, _tuning.PrecipitationHeight));
        else
            _engine.CameraView.SetPrecipitation(new((uint)(_tuning.RainDrops * _falling), PrecipitationShape.Streak,
                new(wind.X * drift * 2, -_tuning.RainSpeed, wind.Y * drift * 2), _tuning.RainSize, _tuning.RainStreakSeconds,
                new(light * .8f, light * .85f, light * .95f, .55f), false, _tuning.PrecipitationRadius, _tuning.PrecipitationHeight));
    }

    /// <summary>What the weather has left on open ground, and the wind. Under a roof none of it shows.</summary>
    private void SetGround(DaggerfallWeatherConditions conditions, bool outside, Vector2 wind, bool winter)
    {
        float wet = outside ? conditions.Wet * _tuning.WetLook : 0, snow = outside ? conditions.Snow * _tuning.SnowLook : 0;
        (WetnessRequest, SnowRequest, WindRequest) ground = (new(wet, wet * _tuning.PuddleShare, winter ? 1 : 0),
            new(snow, Vector3.Zero, _tuning.SnowTexelsPerMetre), new(wind, conditions.Wind, conditions.Wind * .6f));
        if (_ground == ground) return;
        _ground = ground;
        _engine.CameraView.SetWetness(ground.Item1);
        _engine.CameraView.SetSnow(ground.Item2);
        _engine.CameraView.SetWind(ground.Item3);
    }

    /// <summary>Leaves the camera view without this session's weather, for whatever draws next.</summary>
    private void ClearFall()
    {
        _engine.CameraView.SetPrecipitation(new(0, PrecipitationShape.Streak, default, _tuning.RainSize, 0, default, false,
            _tuning.PrecipitationRadius, _tuning.PrecipitationHeight));
        _engine.CameraView.SetWetness(new(0, 0, 0));
        _engine.CameraView.SetSnow(new(0, Vector3.Zero, 0));
        _engine.CameraView.SetWind(new(Vector2.UnitX, 0, 0));
    }

    /// <summary>Takes the panorama down, if one stands.</summary>
    private bool ClearSky()
    {
        if (_sky.Count == 0) return false;
        _engine.CameraView.ClearSkyBackground(default);
        foreach (var resource in _sky.Values) resource.Dispose();
        _sky.Clear();
        return true;
    }

    private void SelectSky(DaggerfallSkySelection selection)
    {
        string[] ids = new[] {selection.First, selection.Second}.Distinct(StringComparer.Ordinal).ToArray();
        List<(string Id, RenderResource Resource)> opened = [];
        _skyColor = _media!.ClearColor(selection);
        try
        {
            foreach (string id in ids)
                if (!_sky.ContainsKey(id)) opened.Add((id, _media!.Open(_engine.Graphics, id)));
            RenderResource Resource(string id) => _sky.TryGetValue(id, out var retained)
                ? retained : opened.Single(entry => entry.Id == id).Resource;
            if (selection.First == selection.Second) _engine.CameraView.SetSkyBackground(Resource(selection.First));
            else _engine.CameraView.SetSkyBackgroundBlend(new(Resource(selection.First), Resource(selection.Second), selection.Amount));
        }
        catch
        {
            foreach (var entry in opened) entry.Resource.Dispose();
            throw;
        }
        foreach (var entry in opened) _sky.Add(entry.Id, entry.Resource);
        foreach (string id in _sky.Keys.Where(id => !ids.Contains(id, StringComparer.Ordinal)).ToArray())
        { _sky[id].Dispose(); _sky.Remove(id); }
    }

    private FogRequest Fog(DaggerfallWorldProfileKind kind, DaggerfallWeatherKind weather, float? horizonFog, Color? air)
    {
        if (kind != DaggerfallWorldProfileKind.Exterior)
            return new(FogMode.Exponential, new Color(0,0,0,1), 0, 0,
                kind == DaggerfallWorldProfileKind.Dungeon ? _tuning.DungeonFogDensity : _tuning.InteriorFogDensity);
        float density = weather switch
        {
            DaggerfallWeatherKind.Rain or DaggerfallWeatherKind.Thunder => _tuning.RainFogDensity,
            DaggerfallWeatherKind.Snow => _tuning.SnowFogDensity,
            DaggerfallWeatherKind.Fog => _tuning.HeavyFogDensity, _ => 0,
        };
        // The fog meets the sky's own horizon: the panorama's colours are sRGB and a fog's are linear. A thick fog fills
        // the view, so it takes the sky's brightness without its blue.
        float grey = (_skyColor.R + _skyColor.G + _skyColor.B) / 3;
        Color color = air ?? (density > _tuning.RainFogDensity ? Color.FromSrgb(grey, grey, grey) : Color.FromSrgb(_skyColor.R, _skyColor.G, _skyColor.B));
        // The classic's clear fog ends with the walked ground, which would close over a horizon past it.
        if (density == 0 && horizonFog is { } haze) return new(FogMode.Exponential, color, 0, 0, haze);
        return density == 0 ? new(FogMode.Linear, color, 0, _tuning.ClearFogEnd, 0)
            : new(FogMode.Exponential, color, 0, 0, density);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception> failures = [];
        try {_ambient.Dispose();} catch (Exception error) {failures.Add(error);}
        try {if (_sky.Count != 0) _engine.CameraView.ClearSkyBackground(default);} catch (Exception error) {failures.Add(error);}
        try {_engine.CameraView.SetFog(new(FogMode.Off, new Color(0,0,0,1),0,0,0));} catch (Exception error) {failures.Add(error);}
        try {SetClouds(0, default, Vector3.One);} catch (Exception error) {failures.Add(error);}
        try {ClearFall();} catch (Exception error) {failures.Add(error);}
        try {_moons?.Dispose();} catch (Exception error) {failures.Add(error);}
        foreach (var resource in _sky.Values)
            try {resource.Dispose();} catch (Exception error) {failures.Add(error);}
        _sky.Clear();
        if (failures.Count != 0) throw new AggregateException(failures);
    }
}
