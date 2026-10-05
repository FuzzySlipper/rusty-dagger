using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallWeatherTests
{
    [Fact]
    public void Source_odds_cover_all_rolls_and_snow_free_climates_remain_snow_free()
    {
        var tuning = DaggerfallWeatherTuning.Classic.Validate();
        foreach (DaggerfallSeason season in Enum.GetValues<DaggerfallSeason>())
        {
            for (int group = 0; group < 6; group++)
            {
                var patterns = Enumerable.Range(0, 100).Select(roll => tuning.Select(group, season, roll)).ToArray();
                foreach (DaggerfallWeatherKind weather in Enum.GetValues<DaggerfallWeatherKind>())
                    Assert.Equal(tuning.Odds[group * 4 + (int)season][(int)weather], patterns.Count(value => value == weather));
                if (group is 0 or 2 or 4) Assert.DoesNotContain(DaggerfallWeatherKind.Snow, patterns);
            }
        }
        Assert.Equal(3, DaggerfallWeatherState.ClimateGroup(223));
        Assert.Equal(0, DaggerfallWeatherState.ClimateGroup(225));
        Assert.Equal(1, DaggerfallWeatherState.ClimateGroup(230));
        Assert.Equal(2, DaggerfallWeatherState.ClimateGroup(227));
        Assert.Equal(4, DaggerfallWeatherState.ClimateGroup(229));
        Assert.Equal(5, DaggerfallWeatherState.ClimateGroup(232));
    }

    [Fact]
    public void Date_changing_interval_selects_once_at_final_season_and_restore_draws_nothing()
    {
        var random = KeyedRandomFake.Create(99);
        var calendar = new DaggerfallCalendar(405, 10, 29, 23, 59, 0);
        var weather = new DaggerfallWeatherState(random.Service, DaggerfallWeatherTuning.Classic, calendar);
        Assert.Equal(6, random.Requests.Count(request => !request.Key.EndsWith(":sky", StringComparison.Ordinal)));
        weather.Advance(calendar);
        Assert.Equal(6, random.Requests.Count(request => !request.Key.EndsWith(":sky", StringComparison.Ordinal)));
        calendar = calendar.Advance(DaggerfallCalendar.SecondsPerDay * 10L, out _);
        weather.Advance(calendar);
        Assert.Equal(12, random.Requests.Count(request => !request.Key.EndsWith(":sky", StringComparison.Ordinal)));
        Assert.Equal(DaggerfallWeatherKind.Snow, weather.ForClimate(226));
        Assert.Equal(calendar.DayNumber + 1, weather.Capture().NextDay);
        var saved = weather.Capture();
        int drawsBeforeRestore = random.Requests.Count;
        var restored = new DaggerfallWeatherState(random.Service, DaggerfallWeatherTuning.Classic, calendar, saved);
        Assert.Equal(12, random.Requests.Count(request => !request.Key.EndsWith(":sky", StringComparison.Ordinal)));
        Assert.Equal(drawsBeforeRestore, random.Requests.Count);
        Assert.Equal(saved.Climates, restored.Capture().Climates);
        Assert.Equal(saved.SkyVariants, restored.Capture().SkyVariants);
        Assert.Equal(saved.NextDay, restored.Capture().NextDay);
        saved.Climates[1] = DaggerfallWeatherKind.Sunny;
        Assert.Equal(DaggerfallWeatherKind.Snow, restored.ForClimate(226));
    }

    [Fact]
    public void Malformed_current_weather_or_wrong_calendar_boundary_refuses_restore()
    {
        var calendar = DaggerfallCalendar.Start;
        Assert.Throws<ArgumentException>(() => new DaggerfallWeatherState(RandomMinimum.Create(),
            DaggerfallWeatherTuning.Classic, calendar, new([], calendar.DayNumber + 1)));
        Assert.Throws<ArgumentException>(() => new DaggerfallWeatherState(RandomMinimum.Create(),
            DaggerfallWeatherTuning.Classic, calendar, new(new DaggerfallWeatherKind[6], calendar.DayNumber)));
        var malformed = DaggerfallWeatherTuning.Classic with { Odds = [] };
        Assert.Throws<ArgumentException>(() => malformed.Validate());
    }

    [Fact]
    public void Admitted_daylight_curve_and_weather_scale_cover_dawn_noon_dusk_and_winter()
    {
        var tuning = DaggerfallWeatherTuning.Classic;
        var noon = new DaggerfallCalendar(405, 5, 0, 12, 0, 0);
        Assert.Equal(0f, tuning.Daylight(noon with { Hour = 6 }, DaggerfallWeatherKind.Sunny));
        Assert.Equal(.36f, tuning.Daylight(noon with { Hour = 6, Minute = 57, Second = 36 }, DaggerfallWeatherKind.Sunny), 5);
        Assert.Equal(.9f, tuning.Daylight(noon, DaggerfallWeatherKind.Sunny), 5);
        Assert.Equal(.36f, tuning.Daylight(noon with { Hour = 17, Minute = 2, Second = 24 }, DaggerfallWeatherKind.Sunny), 5);
        Assert.Equal(0f, tuning.Daylight(noon with { Hour = 18 }, DaggerfallWeatherKind.Sunny));
        Assert.Equal(.9f * .25f, tuning.Daylight(noon, DaggerfallWeatherKind.Thunder), 5);
        Assert.Equal(.9f * .65f, tuning.Daylight(noon with { Month = 0 }, DaggerfallWeatherKind.Sunny), 5);
        Assert.Equal(.9f * .45f, tuning.Daylight(noon with { Month = 0 }, DaggerfallWeatherKind.Rain), 5);
    }
}
