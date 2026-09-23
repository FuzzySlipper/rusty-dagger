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
    private readonly IReadOnlyList<Light> _lights;
    private readonly IGraphicsService _graphics;
    private readonly ICameraViewService _camera;
    private readonly DaggerfallWorldProfileKind _profileKind;
    private readonly DaggerfallSiteLightingTuning _tuning;
    private readonly Light _ambient;
    private readonly ulong _ambientId;
    private float _ambientLevel;
    private bool _disposed;

    internal DaggerfallSiteLighting(IGraphicsService graphics, ICameraViewService camera, PrivateersHoldInputs inputs,
        DaggerfallSiteLightingTuning tuning, DaggerfallCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(inputs);

        _graphics = graphics;
        _camera = camera;
        _profileKind = inputs.ProfileKind;
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _ambientId = StableLogicalId(inputs.ProfileKey.LogicalId, "ambient");
        _ambientLevel = AmbientLevel(calendar);

        List<Light> created = [];
        try
        {
            foreach (DaggerfallSiteLight light in inputs.Lights)
            {
                LightRequest request = new(
                    StableLogicalId(inputs.ProfileKey.LogicalId, light.Id),
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
                created.Add(graphics.CreateLight(request));
            }
            _ambient = graphics.CreateLight(AmbientRequest(_ambientLevel));
            created.Add(_ambient);
            _lights = created;
            ApplyBackground();
        }
        catch
        {
            foreach (Light light in created.AsEnumerable().Reverse()) light.Dispose();
            throw;
        }
    }

    internal int Count => _lights.Count;

    internal void UpdateAmbient(DaggerfallCalendar calendar)
    {
        float level = AmbientLevel(calendar);
        if (level == _ambientLevel) return;
        _graphics.UpdateLight(new LightUpdateRequest(_ambient, AmbientRequest(level)));
        _ambientLevel = level;
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

    private float AmbientLevel(DaggerfallCalendar calendar)
    {
        bool night = calendar.Hour is < 6 or >= 18;
        return _profileKind switch
        {
            DaggerfallWorldProfileKind.Interior => night ? _tuning.InteriorNight : _tuning.InteriorDay,
            DaggerfallWorldProfileKind.Dungeon => _tuning.Dungeon,
            DaggerfallWorldProfileKind.Exterior => night ? _tuning.ExteriorNight : _tuning.ExteriorNoon,
            _ => throw new ArgumentOutOfRangeException(nameof(_profileKind)),
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        foreach (Light light in _lights.Reverse())
        {
            try { light.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private static ulong StableLogicalId(string profile, string id)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (char value in $"site-light:{profile}:{id}") { hash ^= value; hash *= prime; }
        return hash;
    }
}
