using WorldRpg.Rulesets.Daggerfall.World;
using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private readonly DaggerfallWeatherState _weather;
    private readonly DaggerfallWeatherPresentation _weatherPresentation;
    private readonly DaggerfallHorizon _horizon;

    private void SyncWeatherContext(double seconds = 0, bool playing = false)
    {
        if (State.PlayerControl.Position is not { } position) return;
        Vector3 player = position.ToVector();
        bool outside = _sites.Projection.Inputs.ProfileKind == Content.DaggerfallWorldProfileKind.Exterior;
        if (outside)
            _sites.SetExteriorSeason(_time.Calendar.Season == DaggerfallSeason.Winter
                ? DaggerfallExteriorSeason.Winter : DaggerfallExteriorSeason.Summer,
                climate => _weather.ForClimate(climate) is DaggerfallWeatherKind.Rain or DaggerfallWeatherKind.Thunder);
        bool sheltered = outside && _spatial.CastRay(player + Vector3.UnitY * _tuning.Camera.EyeHeight,
            Vector3.UnitY, _tuning.Ambient.ShelterProbeHeight,
            _sites.CharacterEnvironment(State.PlayerControl.Motion)).Present;
        if (outside && !_controlSettings.ClassicArt && _sites.ActiveExteriorOrigin is { } origin)
            _horizon.Show(_spatial.Session, origin, player, _time.Calendar.Season == DaggerfallSeason.Winter,
                _tuning.Weather.Daylight(_time.Calendar, CurrentWeather),
                CurrentWeather is DaggerfallWeatherKind.Sunny or DaggerfallWeatherKind.Cloudy or DaggerfallWeatherKind.Overcast);
        else _horizon.Hide();
        _weatherPresentation.Update(_sites.Projection, _time.Calendar, CurrentClimate, CurrentConditions,
            _weather.SkyVariant(CurrentClimate), player, sheltered, seconds, playing, _horizon.FogDensity);
    }

    /// <summary>The actual outdoor weather at this geography, also available to indoor services.</summary>
    internal DaggerfallWeatherKind CurrentWeather => _weather.ForClimate(CurrentClimate);
    /// <summary>The weather's facts here: what any rule that cares about the weather should read.</summary>
    internal DaggerfallWeatherConditions CurrentConditions => _weather.Conditions(CurrentClimate);
    internal bool IsRaining => CurrentWeather is DaggerfallWeatherKind.Rain or DaggerfallWeatherKind.Thunder;
    internal bool IsStorming => CurrentWeather == DaggerfallWeatherKind.Thunder;

    private int CurrentClimate
    {
        get
        {
            DaggerfallExteriorCellId cell = _sites is { ExteriorResidencyInitialized: true }
                ? _sites.CurrentExteriorCell()
                : _site.ActiveSite is { } site ? new(site.MapPixelX, site.MapPixelY)
                : throw new InvalidOperationException("Weather requires the session's admitted world geography.");
            return _definitions.Grids.ClimateAtWorldPixel(cell.X, cell.Y).Value;
        }
    }
}
