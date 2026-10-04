using Rusty.Engine;
using System.Text.Json.Serialization;

namespace WorldRpg.Rulesets.Daggerfall.World;

internal enum DaggerfallWeatherKind { Sunny, Cloudy, Overcast, Fog, Rain, Thunder, Snow }

/// <summary>Resolved weather for the six source climate groups and the next shared-calendar boundary.</summary>
internal sealed record DaggerfallWeatherSave(DaggerfallWeatherKind[] Climates, long NextDay)
{
    [JsonRequired] public int[] SkyVariants { get; init; } = new int[DaggerfallWeatherState.ClimateCount];

    internal DaggerfallWeatherSave Validate()
    {
        ArgumentNullException.ThrowIfNull(Climates);
        if (Climates.Length != DaggerfallWeatherState.ClimateCount || Climates.Any(value => !Enum.IsDefined(value)))
            throw new ArgumentException("Weather must name one admitted pattern for each of the six source climates.");
        if (SkyVariants is null || SkyVariants.Length != DaggerfallWeatherState.ClimateCount || SkyVariants.Any(value => value is < 0 or > 1))
            throw new ArgumentException("Weather must retain one valid sky variant for every source climate.");
        return this;
    }
}

/// <summary>
/// Daggerfall's daily climate weather. All advancement comes from the session calendar; entering
/// a building changes presentation, never the retained outdoor weather or its next boundary.
/// </summary>
internal sealed class DaggerfallWeatherState
{
    internal const int ClimateCount = 6;
    private readonly IRandomService _random;
    private readonly DaggerfallWeatherTuning _tuning;
    private DaggerfallWeatherKind[] _climates;
    private int[] _skyVariants = new int[ClimateCount];
    private long _nextDay;

    internal DaggerfallWeatherState(IRandomService random, DaggerfallWeatherTuning tuning,
        DaggerfallCalendar calendar, DaggerfallWeatherSave? restored = null)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        if (restored is { } saved)
        {
            saved.Validate();
            if (saved.NextDay != checked(calendar.DayNumber + 1))
                throw new ArgumentException("Saved weather boundary does not follow the saved calendar day.");
            _climates = [.. saved.Climates];
            _skyVariants = [.. saved.SkyVariants];
            _nextDay = saved.NextDay;
        }
        else
        {
            _climates = Select(calendar);
            _nextDay = checked(calendar.DayNumber + 1);
        }
    }

    internal DaggerfallWeatherKind ForClimate(int sourceClimate) => _climates[ClimateGroup(sourceClimate)];
    internal int SkyVariant(int sourceClimate) => _skyVariants[ClimateGroup(sourceClimate)];
    internal DaggerfallWeatherSave Capture() => new([.. _climates], _nextDay) { SkyVariants = [.. _skyVariants] };

    internal void Advance(DaggerfallCalendar calendar)
    {
        if (calendar.DayNumber < _nextDay) return;
        // PlayerEntity changes all climate forecasts once when an admitted interval changes the
        // date, using the season at its end. A long rest/travel does not manufacture intermediate
        // weather draws which no player observed.
        _climates = Select(calendar);
        _nextDay = checked(calendar.DayNumber + 1);
    }

    private DaggerfallWeatherKind[] Select(DaggerfallCalendar calendar)
    {
        var climates = new DaggerfallWeatherKind[ClimateCount];
        for (int group = 0; group < ClimateCount; group++)
        {
            climates[group] = _tuning.Select(group, calendar.Season, Draw(group, calendar.DayNumber));
            _skyVariants[group] = climates[group] is DaggerfallWeatherKind.Rain or DaggerfallWeatherKind.Thunder or DaggerfallWeatherKind.Fog or DaggerfallWeatherKind.Snow
                ? Draw(group, calendar.DayNumber, ":sky") / 50 : 0;
        }
        return climates;
    }

    private int Draw(int group, long day, string suffix = "")
    {
        long value = _random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.weather", $"day:{day}:climate:{group}{suffix}", 0, 99)).Value;
        if (value is < 0 or > 99) throw new InvalidOperationException($"Engine Random returned weather roll {value} outside 0..99.");
        return (int)value;
    }

    internal static int ClimateGroup(int sourceClimate) => sourceClimate switch
    {
        224 or 225 => 0, // Desert / Desert2
        226 or 227 => 1, // Mountain / MountainWoods
        229 => 2,       // Rainforest
        223 or 228 => 3,// Ocean / Swamp
        230 => 4,       // Subtropical
        231 or 232 => 5,// Woodlands / HauntedWoodlands
        _ => throw new ArgumentOutOfRangeException(nameof(sourceClimate), sourceClimate, "Weather requires a named normalized climate."),
    };
}
