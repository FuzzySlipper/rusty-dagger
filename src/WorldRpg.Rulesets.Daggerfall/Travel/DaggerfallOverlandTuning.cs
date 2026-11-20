using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>
/// How the travel map looks and is looked at: the <c>expedition.map</c> section of the ruleset tuning. Distances are map
/// metres, as the world's; angles are degrees, a tilt measured down from level.
/// </summary>
/// <param name="Relief">How far land stands raised above the sea, so ranges read from high above.</param>
/// <param name="FieldOfView">The map camera's vertical field of view.</param>
/// <param name="Tilt">The lowest and highest tilt the view takes, and the one it opens at.</param>
/// <param name="Distance">The nearest and farthest the eye stands from the point it looks at, and where it opens.</param>
/// <param name="Token">How tall the party's and a band's standard stands, at the opening distance; it grows with the
/// distance's square root, so a far view still finds them.</param>
/// <param name="House">How wide a house of a place's model stands at the opening distance: places are drawn far larger
/// than life, as on a map, and grow farther out as the standards do.</param>
/// <param name="Haze">The haze the land is seen through in each weather where the party is, per metre as the world's fog:
/// the map's views are kilometres long, so it is far thinner than the world's own.</param>
internal sealed record DaggerfallOverlandTuning(
    double Relief,
    double FieldOfView,
    DaggerfallOverlandRange Tilt,
    DaggerfallOverlandRange Distance,
    double Token,
    double House,
    IReadOnlyDictionary<DaggerfallWeatherKind, double> Haze)
{
    internal static DaggerfallOverlandTuning Default { get; } = new(4, 40, new(30, 82, 55), new(2_500, 60_000, 12_000), 420, 140,
        new Dictionary<DaggerfallWeatherKind, double>
        {
            [DaggerfallWeatherKind.Sunny] = .000006, [DaggerfallWeatherKind.Cloudy] = .000007, [DaggerfallWeatherKind.Overcast] = .00001,
            [DaggerfallWeatherKind.Fog] = .00006, [DaggerfallWeatherKind.Rain] = .00002, [DaggerfallWeatherKind.Thunder] = .000025,
            [DaggerfallWeatherKind.Snow] = .000025,
        });

    internal DaggerfallOverlandTuning Validate()
    {
        Positive(Relief, "relief");
        if (!double.IsFinite(FieldOfView) || FieldOfView is <= 5 or >= 120)
            throw new InvalidOperationException("Expedition tuning 'map.fieldOfView' must be between 5 and 120 degrees.");
        Tilt.Validate("tilt");
        if (Tilt.Least <= 0 || Tilt.Most >= 90) throw new InvalidOperationException("Expedition tuning 'map.tilt' must lie between level and straight down.");
        Distance.Validate("distance");
        Positive(Distance.Least, "distance");
        Positive(Token, "token");
        Positive(House, "house");
        foreach (DaggerfallWeatherKind weather in Enum.GetValues<DaggerfallWeatherKind>())
            if (!Haze.TryGetValue(weather, out double haze) || !double.IsFinite(haze) || haze < 0)
                throw new InvalidOperationException($"Expedition tuning 'map.haze.{weather.ToString().ToLowerInvariant()}' must be a haze of 0 or more.");
        return this;
    }

    internal static DaggerfallOverlandTuning Read(JsonElement section) => new DaggerfallOverlandTuning(
        section.GetProperty("relief").GetDouble(),
        section.GetProperty("fieldOfView").GetDouble(),
        DaggerfallOverlandRange.Read(section.GetProperty("tilt")),
        DaggerfallOverlandRange.Read(section.GetProperty("distance")),
        section.GetProperty("token").GetDouble(),
        section.GetProperty("house").GetDouble(),
        Enum.GetValues<DaggerfallWeatherKind>().ToDictionary(weather => weather,
            weather => section.GetProperty("haze").GetProperty(weather.ToString().ToLowerInvariant()).GetDouble())).Validate();

    private static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new InvalidOperationException($"Expedition tuning 'map.{name}' must be a positive number.");
    }
}

/// <summary>A tuned range and where in it a view opens: <c>[least, most, opening]</c>.</summary>
internal readonly record struct DaggerfallOverlandRange(double Least, double Most, double Opening)
{
    internal void Validate(string name)
    {
        if (!double.IsFinite(Least) || !double.IsFinite(Most) || !double.IsFinite(Opening) || Least >= Most || Opening < Least || Opening > Most)
            throw new InvalidOperationException($"Expedition tuning 'map.{name}' must be [least, most, opening] with the opening between them.");
    }

    internal double Clamp(double value) => Math.Clamp(value, Least, Most);

    internal static DaggerfallOverlandRange Read(JsonElement range) => range.GetArrayLength() == 3
        ? new(range[0].GetDouble(), range[1].GetDouble(), range[2].GetDouble())
        : throw new InvalidOperationException("Expedition tuning 'map' ranges are [least, most, opening].");
}
