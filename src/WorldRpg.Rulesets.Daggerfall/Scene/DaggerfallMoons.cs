using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Masser and Secunda in the cleared sky, drawn from the classic's art for each day of the lunar cycle. The cycle is the
/// calendar's own, so the sky shows the phase that lycanthropes and enchantments answer to. A moon's place follows from
/// its phase: new beside the sun, and falling behind it a day at a time, so a full moon rises at dusk. The stars stand with
/// them, wheeling about their pole with the day.
/// </summary>
internal sealed class DaggerfallMoons : IDisposable
{
    internal const int CycleDays = 32;
    /// <summary>The calendar counts its cycle from the full moon, and the art from a waxing crescent eight frames before it.</summary>
    private const int FullFrame = 8;
    private const int NewDay = CycleDays / 2;
    /// <summary>A moon gives all its light once it stands this many times above the horizon's sine, so it doesn't snap on as it rises.</summary>
    private const float RisingHeight = 4;

    private readonly IEngineContext _engine;
    private readonly DaggerfallSkyMedia _media;
    private readonly DaggerfallHorizonTuning _tuning;
    private readonly Dictionary<bool, RenderResource> _textures = [];
    private (int Day, int Minute, int Light)? _shown;
    private RenderResource? _stars;
    private bool _starsShown;

    internal DaggerfallMoons(IEngineContext engine, DaggerfallSkyMedia media, DaggerfallHorizonTuning tuning)
    {
        _engine = engine;
        _media = media;
        _tuning = tuning;
    }

    /// <summary>The frame of the art for a day of the cycle.</summary>
    internal static int Frame(int cycleDay) => (cycleDay + FullFrame) % CycleDays;

    /// <summary>The way to a moon at this time on this day of its cycle, its path leaning south by <paramref name="tiltDegrees"/>.</summary>
    internal static Vector3 Direction(DaggerfallCalendar calendar, int cycleDay, float tiltDegrees)
    {
        float sun = MathF.PI * ((calendar.SecondOfDay / 3_600F) - DaggerfallCalendar.DawnHour) / (DaggerfallCalendar.DuskHour - DaggerfallCalendar.DawnHour);
        float along = sun - (2 * MathF.PI * (cycleDay + NewDay) / CycleDays);
        float tilt = tiltDegrees * MathF.PI / 180;
        return new(-MathF.Cos(along), MathF.Sin(along) * MathF.Cos(tilt), MathF.Sin(along) * MathF.Sin(tilt));
    }

    /// <summary>How much of a moon's light reaches the ground on a day of its cycle: all of it full, none of it new. The calendar counts from the full moon.</summary>
    internal static float Phase(int cycleDay) => (1 + MathF.Cos(2 * MathF.PI * cycleDay / CycleDays)) / 2;

    /// <summary>How much of a full night's moonlight falls now, from 0 to 1: each moon gives its share by its phase, while it stands above the horizon.</summary>
    internal float Moonlight(DaggerfallCalendar calendar)
    {
        float light = 0;
        foreach (DaggerfallSkyMoon moon in _media.Moons)
        {
            int day = calendar.LunarCycleDay(moon.Masser);
            float height = Direction(calendar, day, moon.Masser ? _tuning.MasserTiltDegrees : _tuning.SecundaTiltDegrees).Y;
            light += Phase(day) * Math.Clamp(height * RisingHeight, 0, 1) * (moon.Masser ? _tuning.MasserLight : 1 - _tuning.MasserLight);
        }

        return light;
    }

    /// <summary>Stands the moons where the calendar puts them, paler by day, and the stars behind them.</summary>
    internal void Show(DaggerfallCalendar calendar, float daylight)
    {
        ShowStars(calendar, daylight);
        (int, int, int) now = (calendar.DayOfYear, calendar.SecondOfDay / 60, (int)(daylight * 20));
        if (_shown == now) return;
        _shown = now;
        float radiance = 1 - ((1 - _tuning.MoonDayBrightness) * Math.Clamp(daylight, 0, 1));
        foreach (DaggerfallSkyMoon moon in _media.Moons)
        {
            int day = calendar.LunarCycleDay(moon.Masser);
            (Vector2 min, Vector2 max) = moon.Frame(Frame(day));
            // The art draws its own phase, so the disc is lit whole.
            _engine.CameraView.SetMoon(new MoonRequest(Direction(calendar, day, moon.Masser ? _tuning.MasserTiltDegrees : _tuning.SecundaTiltDegrees),
                moon.Masser ? _tuning.MasserRadiusDegrees : _tuning.SecundaRadiusDegrees, Vector3.One, radiance, 1, 0, 0, 0,
                Slot(moon), new RenderResourceReference(Texture(moon)), min, max) with { LightIntensity = Phase(day) * _tuning.MoonCloudLight });
        }
    }

    /// <summary>The stars come out as the day goes, fewer under a bright moon, and turn once a day about their pole.</summary>
    private void ShowStars(DaggerfallCalendar calendar, float daylight)
    {
        float dark = 1 - Math.Clamp(daylight / _tuning.StarsFromDaylight, 0, 1);
        if (dark <= 0)
        {
            HideStars();
            return;
        }

        float pole = _tuning.StarPoleDegrees * MathF.PI / 180;
        Quaternion turn = Quaternion.CreateFromAxisAngle(new(0, MathF.Sin(pole), -MathF.Cos(pole)), 2 * MathF.PI * calendar.SecondOfDay / 86_400F);
        _stars ??= _media.Open(_engine.Graphics, _media.Stars);
        _engine.CameraView.SetStars(new(_stars, default, turn, _tuning.StarLimit - (Moonlight(calendar) * _tuning.StarMoonWash), _tuning.StarBrightness * dark, 0, _tuning.StarTwinkle));
        _starsShown = true;
    }

    private void HideStars()
    {
        if (!_starsShown) return;
        _starsShown = false;
        _engine.CameraView.SetStars(default);
    }

    /// <summary>Takes the moons and stars away, for a roof, the classic's sky or thick weather.</summary>
    internal void Hide()
    {
        HideStars();
        if (_shown is null) return;
        _shown = null;
        foreach (DaggerfallSkyMoon moon in _media.Moons) _engine.CameraView.SetMoon(default(MoonRequest) with { Slot = Slot(moon) });
    }

    /// <summary>Secunda is the nearer, so it crosses in front.</summary>
    private static uint Slot(DaggerfallSkyMoon moon) => moon.Masser ? 0U : 1U;

    private RenderResource Texture(DaggerfallSkyMoon moon)
    {
        if (!_textures.TryGetValue(moon.Masser, out RenderResource? texture))
            _textures.Add(moon.Masser, texture = _media.Open(_engine.Graphics, moon.Id, TextureFilter.Nearest));
        return texture;
    }

    public void Dispose()
    {
        Hide();
        foreach (RenderResource texture in _textures.Values) texture.Dispose();
        _textures.Clear();
        _stars?.Dispose();
    }
}
