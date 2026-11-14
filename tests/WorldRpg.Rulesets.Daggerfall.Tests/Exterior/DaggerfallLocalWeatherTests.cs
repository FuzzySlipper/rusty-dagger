using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary><see cref="DaggerfallWeather"/>, the weather where the player is, built over its inputs alone.</summary>
public sealed class DaggerfallLocalWeatherTests
{
    private static readonly DaggerfallCalendar Calendar = new(405, 5, 10, 8, 0, 0);

    [Fact]
    public void The_streamed_cell_names_the_climate_before_the_sites_map_pixel()
    {
        DaggerfallExteriorCellId? streamed = null;
        DaggerfallExteriorCellId? site = new(10, 20);
        DaggerfallWeather weather = new(new DaggerfallWeatherState(RandomMinimum.Create(), DaggerfallWeatherTuning.Classic, Calendar),
            (x, y) => x == 10 && y == 20 ? 224 : x == 30 && y == 40 ? 227 : throw new InvalidOperationException($"No climate at {x},{y}."),
            () => streamed, () => site);

        Assert.Equal(224, weather.Climate);
        streamed = new(30, 40);
        Assert.Equal(227, weather.Climate);
        streamed = null;
        site = null;
        Assert.Throws<InvalidOperationException>(() => weather.Climate);
    }

    [Fact]
    public void Its_weather_and_conditions_are_the_climate_heres()
    {
        DaggerfallWeatherState state = new(RandomMinimum.Create(), DaggerfallWeatherTuning.Classic, Calendar);
        DaggerfallWeather weather = new(state, (_, _) => 226, () => new(1, 1), () => null);

        state.Force(DaggerfallWeatherKind.Thunder);
        Assert.Equal(DaggerfallWeatherKind.Thunder, weather.Current);
        Assert.Equal(state.Conditions(226), weather.Conditions);
        Assert.Equal(state.SkyVariant(226), weather.SkyVariant);
        Assert.True(weather.IsRaining);
        Assert.True(weather.IsStorming);

        state.Force(DaggerfallWeatherKind.Rain);
        Assert.True(weather.IsRaining);
        Assert.False(weather.IsStorming);

        state.Force(DaggerfallWeatherKind.Snow);
        Assert.False(weather.IsRaining);
        Assert.Same(state, weather.State);
    }
}
