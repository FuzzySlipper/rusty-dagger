using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>
/// The expedition map's rule values: the <c>expedition</c> section of the ruleset tuning. A pixel's base minutes are the
/// donor's (<see cref="DaggerfallTravelPolicy.PixelMinutes"/>); these scale them and set what the map costs and pays.
/// </summary>
/// <param name="TimeScales">Game minutes that pass per real second at each speed above the pause, slowest first.</param>
/// <param name="CampTime">How many times a speed's game minutes pass per real second in camp, so a night's rest goes by in moments.</param>
/// <param name="RecklessTime">A reckless pixel's share of a cautious one's minutes; the donor's reckless travel takes half.</param>
/// <param name="RecklessFatigue">How many times the per-minute fatigue a reckless march spends of a cautious one.</param>
/// <param name="NightTime">How many times a pixel's minutes a march takes between dusk and dawn.</param>
/// <param name="WeatherTime">How many times a pixel's minutes a march or a voyage takes in each weather.</param>
/// <param name="WeatherFatigue">How many times the per-minute fatigue a march spends in each weather.</param>
/// <param name="Lodging">Gold a night's lodging costs in each kind of place that offers it; a camp anywhere else is free.</param>
/// <param name="ShipHire">Gold a passage costs on a hired ship; an owned ship sails free.</param>
/// <param name="PortReach">How many map pixels from a port and from the sea a party can board a ship.</param>
/// <param name="LowFatigue">The share of the fatigue pool under which the map warns once and pauses.</param>
internal sealed record DaggerfallExpeditionTuning(
    IReadOnlyList<double> TimeScales,
    double CampTime,
    double RecklessTime,
    double RecklessFatigue,
    double NightTime,
    IReadOnlyDictionary<DaggerfallWeatherKind, double> WeatherTime,
    IReadOnlyDictionary<DaggerfallWeatherKind, double> WeatherFatigue,
    IReadOnlyDictionary<DaggerfallSiteKind, int> Lodging,
    int ShipHire,
    int PortReach,
    double LowFatigue)
{
    internal static DaggerfallExpeditionTuning Default { get; } = new(
        [30, 120, 360], 6, .5, 2, 1.25,
        new Dictionary<DaggerfallWeatherKind, double>
        {
            [DaggerfallWeatherKind.Sunny] = 1, [DaggerfallWeatherKind.Cloudy] = 1, [DaggerfallWeatherKind.Overcast] = 1,
            [DaggerfallWeatherKind.Fog] = 1.25, [DaggerfallWeatherKind.Rain] = 1.2, [DaggerfallWeatherKind.Thunder] = 1.35,
            [DaggerfallWeatherKind.Snow] = 1.5,
        },
        new Dictionary<DaggerfallWeatherKind, double>
        {
            [DaggerfallWeatherKind.Sunny] = 1, [DaggerfallWeatherKind.Cloudy] = 1, [DaggerfallWeatherKind.Overcast] = 1,
            [DaggerfallWeatherKind.Fog] = 1, [DaggerfallWeatherKind.Rain] = 1.25, [DaggerfallWeatherKind.Thunder] = 1.5,
            [DaggerfallWeatherKind.Snow] = 1.5,
        },
        new Dictionary<DaggerfallSiteKind, int>
        {
            [DaggerfallSiteKind.TownCity] = 30, [DaggerfallSiteKind.TownHamlet] = 20, [DaggerfallSiteKind.TownVillage] = 10,
            [DaggerfallSiteKind.Tavern] = 10,
        },
        100, 2, .2);

    /// <summary>How the travel map looks and is looked at.</summary>
    internal DaggerfallOverlandTuning Map { get; init; } = DaggerfallOverlandTuning.Default;

    /// <summary>The bands staged on the map, their sight and the party's.</summary>
    internal DaggerfallBandTuning Bands { get; init; } = DaggerfallBandTuning.Default;

    /// <summary>Where travelling bands are live on the map, and what those on the road beyond meet (#11178).</summary>
    internal DaggerfallRoadTuning Road { get; init; } = DaggerfallRoadTuning.Default;

    /// <summary>How many speeds the map runs at above the pause.</summary>
    internal int Speeds => TimeScales.Count;

    internal DaggerfallExpeditionTuning Validate()
    {
        if (TimeScales.Count == 0 || TimeScales.Any(scale => !double.IsFinite(scale) || scale <= 0)
            || TimeScales.Zip(TimeScales.Skip(1)).Any(pair => pair.Second <= pair.First))
            throw new InvalidOperationException("Expedition tuning 'timeScales' must be positive game minutes per second, slowest first.");
        Positive(CampTime, "campTime");
        Positive(RecklessTime, "recklessTime");
        Positive(RecklessFatigue, "recklessFatigue");
        Positive(NightTime, "nightTime");
        foreach (DaggerfallWeatherKind weather in Enum.GetValues<DaggerfallWeatherKind>())
        {
            Positive(WeatherTime.TryGetValue(weather, out double time) ? time : double.NaN, $"weatherTime.{Key(weather)}");
            Positive(WeatherFatigue.TryGetValue(weather, out double fatigue) ? fatigue : double.NaN, $"weatherFatigue.{Key(weather)}");
        }
        if (Lodging.Values.Any(gold => gold <= 0)) throw new InvalidOperationException("Expedition tuning 'lodging' prices must be positive.");
        if (ShipHire < 0) throw new InvalidOperationException("Expedition tuning 'shipHire' cannot be negative.");
        if (PortReach < 0) throw new InvalidOperationException("Expedition tuning 'portReach' cannot be negative.");
        if (!double.IsFinite(LowFatigue) || LowFatigue is < 0 or >= 1)
            throw new InvalidOperationException("Expedition tuning 'lowFatigue' must be a share from 0 up to 1.");
        return this;
    }

    internal static DaggerfallExpeditionTuning Read(JsonElement section) => new DaggerfallExpeditionTuning(
        [.. section.GetProperty("timeScales").EnumerateArray().Select(scale => scale.GetDouble())],
        section.GetProperty("campTime").GetDouble(),
        section.GetProperty("recklessTime").GetDouble(),
        section.GetProperty("recklessFatigue").GetDouble(),
        section.GetProperty("nightTime").GetDouble(),
        Weather(section.GetProperty("weatherTime")),
        Weather(section.GetProperty("weatherFatigue")),
        section.GetProperty("lodging").EnumerateObject().ToDictionary(
            entry => SiteKind(entry.Name), entry => entry.Value.GetInt32()),
        section.GetProperty("shipHire").GetInt32(),
        section.GetProperty("portReach").GetInt32(),
        section.GetProperty("lowFatigue").GetDouble()) { Bands = DaggerfallBandTuning.Read(section.GetProperty("bands")), Road = DaggerfallRoadTuning.Read(section.GetProperty("road")), Map = DaggerfallOverlandTuning.Read(section.GetProperty("map")) }.Validate();

    private static Dictionary<DaggerfallWeatherKind, double> Weather(JsonElement section) =>
        Enum.GetValues<DaggerfallWeatherKind>().ToDictionary(weather => weather, weather => section.GetProperty(Key(weather)).GetDouble());

    private static DaggerfallSiteKind SiteKind(string name) => name switch
    {
        "city" => DaggerfallSiteKind.TownCity,
        "hamlet" => DaggerfallSiteKind.TownHamlet,
        "village" => DaggerfallSiteKind.TownVillage,
        "tavern" => DaggerfallSiteKind.Tavern,
        _ => throw new InvalidOperationException($"Expedition tuning 'lodging' names '{name}', which is not a city, hamlet, village or tavern."),
    };

    private static string Key(DaggerfallWeatherKind weather) => weather.ToString().ToLowerInvariant();

    private static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new InvalidOperationException($"Expedition tuning '{name}' must be a positive number.");
    }
}
