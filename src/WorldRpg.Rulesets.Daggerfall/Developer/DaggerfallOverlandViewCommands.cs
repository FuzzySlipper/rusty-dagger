using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Travel;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The developer's placement of the travel map's view, for captures at a repeatable spot.</summary>
internal sealed class DaggerfallOverlandViewCommands(DaggerfallOverlandMap map) : IDeveloperTravelMapViewCommands
{
    private const double Cell = World.DaggerfallWorldGrid.HorizontalSize;

    public DebugCommandResult SetView(double x, double y, double heading, double tilt, double distance)
    {
        if (!map.Open) return DaggerfallDeveloperReplies.Refused("The travel map is not open.");
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(heading) || !double.IsFinite(tilt) || !double.IsFinite(distance) || distance <= 0)
            return DaggerfallDeveloperReplies.Refused("map.view-set takes X Y HEADING TILT DISTANCE.");
        DaggerfallOverlandView view = map.View;
        (double metresX, double metresZ) = DaggerfallOverlandView.Metres(x, y);
        view.Look(metresX, metresZ);
        view.Move(heading - view.Heading, tilt - view.Tilt, 0, 0, Math.Log(view.Distance / distance, DaggerfallOverlandView.ZoomStep));
        return View();
    }

    public DebugCommandResult Look(string key, double value)
    {
        if (!map.Open) return DaggerfallDeveloperReplies.Refused("The travel map is not open.");
        return map.Tune(key, value) is { } refusal ? DaggerfallDeveloperReplies.Refused(refusal)
            : DaggerfallDeveloperReplies.Json(w =>
            {
                DaggerfallOverlandLook look = map.Look;
                w.WriteStartObject();
                w.WriteNumber("contrast", look.Grading.Contrast);
                w.WriteNumber("saturation", look.Grading.Saturation);
                w.WriteNumber("temperature", look.Grading.Temperature);
                w.WriteNumber("moon", look.Moon);
                w.WriteNumber("haze", look.Haze);
                w.WriteEndObject();
            });
    }

    public DebugCommandResult View()
    {
        if (!map.Open) return DaggerfallDeveloperReplies.Refused("The travel map is not open.");
        DaggerfallOverlandView view = map.View;
        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("x", Math.Round(view.TargetX / Cell, 3));
            w.WriteNumber("y", Math.Round((view.TargetZ / Cell) + 1, 3));
            w.WriteNumber("heading", Math.Round(view.Heading, 1));
            w.WriteNumber("tilt", Math.Round(view.Tilt, 1));
            w.WriteNumber("distance", Math.Round(view.Distance));
            w.WriteBoolean("following", view.Following);
            w.WriteEndObject();
        });
    }
}
