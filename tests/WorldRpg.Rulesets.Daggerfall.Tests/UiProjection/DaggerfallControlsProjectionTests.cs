using WorldRpg.Kit.Presentation;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The settings page's fractional rows get their values as the decimals they were written as (#11003): a float step
/// widened to double (0.10000000149) does not divide a row's span, so the page's range stopped Surface relief at 1.9 and
/// refused a typed value. Each row's value, ends and step are exact, and its span is a whole number of steps.
/// </summary>
public sealed class DaggerfallControlsProjectionTests
{
    private static Dictionary<string, object?> Published(DaggerfallControlSettings settings)
    {
        UiValueBuilder builder = new();
        Dictionary<string, IReadOnlyList<int>> buttons = DaggerfallControlSettings.Catalog.ToDictionary(action => action.Id, _ => (IReadOnlyList<int>)[]);
        UiValue value = builder.Build(DaggerfallHudProjection.Controls(builder, settings, null, true, buttons));
        return Assert.IsType<Dictionary<string, object?>>(EngineContextFake.UiServiceFake.Decode(value));
    }

    private static Dictionary<string, object?> Row(Dictionary<string, object?> controls, string key) =>
        Assert.IsType<Dictionary<string, object?>>(controls[key]);

    private static double Number(Dictionary<string, object?> row, string key) => Convert.ToDouble(row[key]);

    private static void WholeSteps(double minimum, double maximum, double step) =>
        Assert.Equal(Math.Round((maximum - minimum) / step), (maximum - minimum) / step, 9);

    [Fact]
    public void Fractional_rows_publish_exact_values_ends_and_steps()
    {
        DaggerfallControlSettings settings = new();
        settings.SetSurfaceRelief(2);
        settings.SetClutterDistance(1.7F);
        settings.SetReliefDistance(19.75F);
        settings.SetVolume("music", .35F);
        settings.SetUiScale(1.25F);
        Dictionary<string, object?> controls = Published(settings);

        Dictionary<string, object?> relief = Row(controls, "surfaceRelief");
        Assert.Equal((2d, 2d, .1d), (Number(relief, "strength"), Number(relief, "maximum"), Number(relief, "step")));
        WholeSteps(0, Number(relief, "maximum"), Number(relief, "step"));
        Assert.Equal((19.75d, .5d, 20d, .25d), (Number(relief, "distance"), Number(relief, "distanceMinimum"),
            Number(relief, "distanceMaximum"), Number(relief, "distanceStep")));
        WholeSteps(Number(relief, "distanceMinimum"), Number(relief, "distanceMaximum"), Number(relief, "distanceStep"));

        Dictionary<string, object?> ground = Row(controls, "groundCover");
        Assert.Equal((1.7d, .5d, 2d, .1d), (Number(ground, "distance"), Number(ground, "minimum"), Number(ground, "maximum"), Number(ground, "step")));
        WholeSteps(Number(ground, "minimum"), Number(ground, "maximum"), Number(ground, "step"));

        Assert.Equal(.05d, Number(controls, "volumeStep"));
        Assert.Equal(.35d, Number(Assert.IsType<object?[]>(controls["volumes"]).Cast<Dictionary<string, object?>>().Single(bus => (string)bus["id"]! == "music"), "level"));
        Assert.Equal(1.25d, Number(controls, "uiScale"));
        Dictionary<string, object?> scale = Row(controls, "uiScaleRange");
        WholeSteps(Number(scale, "minimum"), Number(scale, "maximum"), Number(scale, "step"));
    }

    [Fact]
    public void Each_surface_relief_step_publishes_as_its_tenth()
    {
        DaggerfallControlSettings settings = new();
        for (int tenths = 0; tenths <= 20; tenths++)
        {
            settings.SetSurfaceRelief(tenths * DaggerfallControlSettings.SurfaceReliefStep);
            Assert.Equal(tenths / 10d, Number(Row(Published(settings), "surfaceRelief"), "strength"));
        }
    }
}
