using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Source weather odds, sunlight scales and the authored daylight curve.</summary>
internal sealed record DaggerfallWeatherTuning(int[][] Odds, float WinterScale, float OvercastScale,
    float RainScale, float StormScale, float SnowScale, float[][] DaylightCurve)
{
    // WeatherTable.json cites Daggerfall Chronicles p47. Rows are climate group then season;
    // columns are Sunny, Cloudy, Overcast, Fog, Rain, Thunder, Snow.
    internal static DaggerfallWeatherTuning Classic { get; } = new(
        [
            [75, 15, 0, 3, 5, 2, 0],
            [75, 15, 0, 0, 5, 5, 0],
            [85, 15, 0, 0, 0, 0, 0],
            [80, 15, 0, 0, 3, 2, 0],
            [18, 20, 25, 2, 0, 0, 35],
            [30, 23, 15, 2, 20, 10, 0],
            [45, 25, 15, 0, 10, 5, 0],
            [30, 18, 20, 2, 20, 10, 0],
            [15, 20, 25, 3, 25, 12, 0],
            [20, 15, 10, 3, 37, 15, 0],
            [35, 20, 10, 0, 25, 10, 0],
            [20, 20, 20, 0, 25, 15, 0],
            [15, 20, 25, 25, 0, 0, 15],
            [10, 10, 20, 20, 25, 15, 0],
            [25, 15, 15, 15, 20, 10, 0],
            [15, 15, 15, 20, 20, 15, 0],
            [20, 20, 20, 5, 25, 10, 0],
            [30, 15, 10, 3, 27, 15, 0],
            [40, 15, 10, 0, 20, 15, 0],
            [25, 20, 15, 0, 25, 15, 0],
            [25, 15, 20, 5, 10, 0, 25],
            [35, 15, 10, 5, 25, 10, 0],
            [60, 20, 5, 0, 10, 5, 0],
            [25, 15, 20, 10, 20, 10, 0],
        ], .65f, .65f, .45f, .25f, .45f,
        [[0, 0, 0], [.15f, .65f, 2.6095238f], [.5f, .96f, 0], [.85f, .65f, -2.609524f], [1, 0, 0]]);

    internal DaggerfallWeatherTuning Validate()
    {
        ArgumentNullException.ThrowIfNull(Odds);
        if (Odds.Length != DaggerfallWeatherState.ClimateCount * 4
            || Odds.Any(row => row is null || row.Length != 7 || row.Any(value => value is < 0 or > 100) || row.Sum() != 100))
            throw new ArgumentException("Weather odds require seven percentages totaling 100 for each climate and season.");
        foreach (float scale in new[] { WinterScale, OvercastScale, RainScale, StormScale, SnowScale })
            if (!float.IsFinite(scale) || scale is < 0 or > 1) throw new ArgumentException("Weather sunlight scales must be normalized.");
        ArgumentNullException.ThrowIfNull(DaylightCurve);
        if (DaylightCurve.Length < 2 || DaylightCurve.Any(knot => knot is null || knot.Length != 3 || knot.Any(value => !float.IsFinite(value))
                || knot[0] is < 0 or > 1 || knot[1] is < 0 or > 1)
            || DaylightCurve[0][0] != 0 || DaylightCurve[^1][0] != 1
            || DaylightCurve.Zip(DaylightCurve.Skip(1)).Any(pair => pair.First[0] >= pair.Second[0]))
            throw new ArgumentException("Daylight requires ordered finite time/value/tangent knots covering dawn through dusk.");
        return this;
    }

    internal DaggerfallWeatherKind Select(int group, DaggerfallSeason season, int roll)
    {
        if (group is < 0 or >= DaggerfallWeatherState.ClimateCount || !Enum.IsDefined(season) || roll is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(roll));
        int[] odds = Odds[group * 4 + (int)season];
        for (int pattern = 0; pattern < odds.Length; pattern++)
        {
            if (roll < odds[pattern]) return (DaggerfallWeatherKind)pattern;
            roll -= odds[pattern];
        }
        throw new InvalidOperationException("Validated weather odds did not select a pattern.");
    }

    internal float Daylight(DaggerfallCalendar calendar, DaggerfallWeatherKind weather)
    {
        float time = (calendar.SecondOfDay / 60f - DaggerfallCalendar.DawnHour * 60f)
            / ((DaggerfallCalendar.DuskHour - DaggerfallCalendar.DawnHour) * 60f);
        if (time <= 0 || time >= 1) return 0;
        float scale = weather switch
        {
            DaggerfallWeatherKind.Rain => RainScale,
            DaggerfallWeatherKind.Thunder => StormScale,
            DaggerfallWeatherKind.Snow => SnowScale,
            DaggerfallWeatherKind.Overcast or DaggerfallWeatherKind.Fog => OvercastScale,
            _ => calendar.Season == DaggerfallSeason.Winter ? WinterScale : 1f,
        };
        for (int index = 1; index < DaylightCurve.Length; index++)
        {
            float[] end = DaylightCurve[index];
            if (time > end[0]) continue;
            float[] start = DaylightCurve[index - 1];
            float span = end[0] - start[0];
            float t = (time - start[0]) / span;
            float t2 = t * t, t3 = t2 * t;
            return Math.Clamp((2*t3 - 3*t2 + 1)*start[1] + (t3 - 2*t2 + t)*span*start[2]
                + (-2*t3 + 3*t2)*end[1] + (t3 - t2)*span*end[2], 0, 1) * scale;
        }
        return 0;
    }

    internal static DaggerfallWeatherTuning Read(JsonElement weather) => new(
        weather.GetProperty("odds").EnumerateArray().Select(row => row.EnumerateArray().Select(value => value.GetInt32()).ToArray()).ToArray(),
        weather.GetProperty("winterScale").GetSingle(), weather.GetProperty("overcastScale").GetSingle(),
        weather.GetProperty("rainScale").GetSingle(), weather.GetProperty("stormScale").GetSingle(), weather.GetProperty("snowScale").GetSingle(),
        weather.GetProperty("daylightCurve").EnumerateArray().Select(row => row.EnumerateArray().Select(value => value.GetSingle()).ToArray()).ToArray());
}
