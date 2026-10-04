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
    private readonly DaggerfallAmbientPresentation _ambient;
    private readonly Dictionary<string, RenderResource> _sky = new(StringComparer.Ordinal);
    private readonly Dictionary<bool, RenderResource> _particles = [];
    private Color _skyColor = new(.5f,.5f,.5f,1);
    private bool _disposed;

    internal DaggerfallWeatherPresentation(IEngineContext engine, DaggerfallSkyMedia? media,
        DaggerfallWeatherTuning weather, DaggerfallAmbientTuning tuning, DaggerfallPresentationAudioTuning audioTuning,
        Func<DaggerfallWorldProfileKey, string, AudioClip?> openClip)
    {
        _engine = engine; _media = media; _weather = weather; _tuning = tuning.Validate();
        _ambient = new(engine.Presentation, engine.Audio, engine.Random, tuning, openClip, Particle, audioTuning);
    }

    internal void Update(DaggerfallSiteProjection projection, DaggerfallCalendar calendar, int climate,
        DaggerfallWeatherKind weather, int variant, Vector3 player, bool sheltered, double seconds, bool playing)
    {
        var inputs = projection.Inputs;
        var zones = inputs.AmbientZones.Where(zone => zone.Contains(player - projection.WorldOffset)).ToArray();
        bool castle = zones.Any(zone => zone.Kind == DaggerfallAmbientZoneKind.Castle);
        bool special = zones.Any(zone => zone.Kind == DaggerfallAmbientZoneKind.SpecialArea);
        _ambient.Update(new(inputs.ProfileKey, weather, !calendar.IsDay, sheltered, castle, special), player, seconds, playing);
        projection.Lighting.UpdateAmbient(calendar, _weather.Daylight(calendar, weather),
            castle || special ? _tuning.ZoneAmbient : null, _ambient.FlashIntensity);
        if (inputs.ProfileKind == DaggerfallWorldProfileKind.Exterior && _media is not null)
            SelectSky(_media.Select(climate, calendar, weather, variant));
        else if (_sky.Count != 0)
        {
            _engine.CameraView.ClearSkyBackground(default);
            foreach (var resource in _sky.Values) resource.Dispose(); _sky.Clear();
            projection.Lighting.ApplyBackground();
        }
        _engine.CameraView.SetFog(Fog(inputs.ProfileKind, weather));
    }

    private RenderResourceReference Particle(bool snow)
    {
        if (!_particles.TryGetValue(snow, out var resource))
        {
            if (_media is null) throw new InvalidOperationException("A precipitating composition must admit the published weather media.");
            resource = _media.Open(_engine.Graphics, _media.Particle(snow));
            _particles.Add(snow, resource);
        }
        return new(resource);
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

    private FogRequest Fog(DaggerfallWorldProfileKind kind, DaggerfallWeatherKind weather)
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
        Color color = density > _tuning.RainFogDensity ? new(.5f,.5f,.5f,1) : _skyColor;
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
        foreach (var resource in _sky.Values)
            try {resource.Dispose();} catch (Exception error) {failures.Add(error);}
        _sky.Clear();
        foreach (var resource in _particles.Values)
            try {resource.Dispose();} catch (Exception error) {failures.Add(error);}
        _particles.Clear();
        if (failures.Count != 0) throw new AggregateException(failures);
    }
}
