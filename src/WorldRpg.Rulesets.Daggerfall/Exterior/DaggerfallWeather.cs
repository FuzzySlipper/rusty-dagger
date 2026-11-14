namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The weather where the player is: the source climate of the map pixel they stand on, and that climate's weather and
/// conditions from the retained <see cref="DaggerfallWeatherState"/>. A rule that cares about the weather reads
/// <see cref="Conditions"/> here, never which sky draws.
/// </summary>
/// <param name="state">The outdoor weather of every climate, advanced and saved by the session.</param>
/// <param name="climateAt">The source climate of a world map pixel.</param>
/// <param name="streamedCell">The exterior cell under the player while the exterior streams cells; null otherwise.</param>
/// <param name="sitePixel">The active site's map pixel, when there is an active site.</param>
internal sealed class DaggerfallWeather(
    DaggerfallWeatherState state,
    Func<int, int, int> climateAt,
    Func<DaggerfallExteriorCellId?> streamedCell,
    Func<DaggerfallExteriorCellId?> sitePixel)
{
    private readonly DaggerfallWeatherState _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly Func<int, int, int> _climateAt = climateAt ?? throw new ArgumentNullException(nameof(climateAt));
    private readonly Func<DaggerfallExteriorCellId?> _streamedCell = streamedCell ?? throw new ArgumentNullException(nameof(streamedCell));
    private readonly Func<DaggerfallExteriorCellId?> _sitePixel = sitePixel ?? throw new ArgumentNullException(nameof(sitePixel));

    /// <summary>Every climate's weather: what the session advances with the calendar, saves and a developer forces.</summary>
    internal DaggerfallWeatherState State => _state;

    /// <summary>The source climate where the player stands.</summary>
    internal int Climate
    {
        get
        {
            DaggerfallExteriorCellId cell = _streamedCell() ?? _sitePixel()
                ?? throw new InvalidOperationException("Weather requires the session's admitted world geography.");
            return _climateAt(cell.X, cell.Y);
        }
    }

    /// <summary>The actual outdoor weather at this geography, also available to indoor services.</summary>
    internal DaggerfallWeatherKind Current => _state.ForClimate(Climate);

    /// <summary>The weather's facts here: what any rule that cares about the weather should read.</summary>
    internal DaggerfallWeatherConditions Conditions => _state.Conditions(Climate);

    /// <summary>The sky drawn for this climate's weather.</summary>
    internal int SkyVariant => _state.SkyVariant(Climate);

    internal bool IsRaining => Current is DaggerfallWeatherKind.Rain or DaggerfallWeatherKind.Thunder;
    internal bool IsStorming => Current == DaggerfallWeatherKind.Thunder;
}
