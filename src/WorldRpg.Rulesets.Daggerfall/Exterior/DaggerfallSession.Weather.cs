using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private readonly DaggerfallWeatherState _weather;
    private readonly DaggerfallWeatherPresentation _weatherPresentation;
    private readonly DaggerfallHorizon _horizon;

    /// <inheritdoc/>
    public LoadingProgress AwaitArrivalGround()
    {
        SyncWeatherContext();
        LoadingProgress collider = _exterior.AwaitGroundCollider();
        return collider.Done ? _horizon.GroundProgress() : collider;
    }

    /// <summary>Who needs exterior ground beside the player: the living, past a town's people, who have its ground plane.</summary>
    private IEnumerable<Vector3> GroundWalkers() => State.Actors.All
        .Where(actor => actor.DurableId != DaggerfallActorIdentity.PlayerEntityId && !actor.IsDefeated
            && _roster.Definitions.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition)
            && definition.Kind is not (DaggerfallActorKinds.Civilian or DaggerfallActorKinds.StaticNpc))
        .Select(actor => actor.Position.ToVector());

    private void SyncWeatherContext(double seconds = 0, bool playing = false)
    {
        if (State.PlayerControl.Position is not { } position) return;
        Vector3 player = position.ToVector();
        bool outside = _sites.Projection.Inputs.ProfileKind == Content.DaggerfallWorldProfileKind.Exterior;
        if (outside)
            _exterior.SetExteriorSeason(_time.Calendar.Season == DaggerfallSeason.Winter
                ? DaggerfallExteriorSeason.Winter : DaggerfallExteriorSeason.Summer,
                climate => _weather.ForClimate(climate) is DaggerfallWeatherKind.Rain or DaggerfallWeatherKind.Thunder);
        bool sheltered = outside && _spatial.CastRay(player + Vector3.UnitY * _tuning.Camera.EyeHeight,
            Vector3.UnitY, _tuning.Ambient.ShelterProbeHeight,
            _exterior.CharacterEnvironment(State.PlayerControl.Motion)).Present;
        _camera.DrawOutTo(outside ? _tuning.Horizon.DrawnMetres : null);
        if (outside && _exterior.ActiveExteriorOrigin is { } origin)
            _horizon.Show(_spatial.Session, origin, player, _time.Calendar.Season == DaggerfallSeason.Winter,
                _sites.Projection.Lighting.SkyLight / _tuning.SiteLighting.ViewFull,
                CurrentWeather is DaggerfallWeatherKind.Sunny or DaggerfallWeatherKind.Cloudy or DaggerfallWeatherKind.Overcast,
                !_classicArt, _exterior.GroundLayers, _exterior.FloraLook);
        else _horizon.Hide();
        _weatherPresentation.Update(_sites.Projection, _time.Calendar, CurrentClimate, CurrentConditions,
            _weather.SkyVariant(CurrentClimate), player, sheltered, seconds, playing, _horizon.FogDensity);
    }

    /// <summary>Whether river water about the player is still being built, a few map pixels an update.</summary>
    internal bool RiverWaterBuilding => _horizon.RiversBuilding;

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
            DaggerfallExteriorCellId cell = _sites?.Exterior is { ExteriorResidencyInitialized: true }
                ? _exterior.CurrentExteriorCell()
                : _site.ActiveSite is { } site ? new(site.MapPixelX, site.MapPixelY)
                : throw new InvalidOperationException("Weather requires the session's admitted world geography.");
            return _definitions.Grids.ClimateAtWorldPixel(cell.X, cell.Y).Value;
        }
    }
}
