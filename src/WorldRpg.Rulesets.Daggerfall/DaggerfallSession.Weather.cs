using WorldRpg.Rulesets.Daggerfall.World;
using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private readonly DaggerfallWeatherState _weather;
    private readonly DaggerfallWeatherPresentation _weatherPresentation;

    private void SyncWeatherContext(double seconds = 0, bool playing = false)
    {
        if (State.PlayerControl.Position is not { } position) return;
        Vector3 player = position.ToVector();
        bool outside = _sites.Projection.Inputs.ProfileKind == Content.DaggerfallWorldProfileKind.Exterior;
        if (outside)
            _sites.SetExteriorSeason(_time.Calendar.Season == DaggerfallSeason.Winter
                ? DaggerfallExteriorSeason.Winter : DaggerfallExteriorSeason.Summer);
        bool sheltered = outside && _spatial.CastRay(player + Vector3.UnitY * _tuning.Camera.EyeHeight,
            Vector3.UnitY, _tuning.Ambient.ShelterProbeHeight,
            _sites.CharacterEnvironment(State.PlayerControl.Motion)).Present;
        _weatherPresentation.Update(_sites.Projection, _time.Calendar, CurrentClimate, CurrentWeather,
            _weather.SkyVariant(CurrentClimate), player, sheltered, seconds, playing);
    }

    /// <summary>The actual outdoor weather at this geography, also available to indoor services.</summary>
    internal DaggerfallWeatherKind CurrentWeather => _weather.ForClimate(CurrentClimate);
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
