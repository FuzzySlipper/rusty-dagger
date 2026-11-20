using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer look at the bands staged on the travel map: which are near, how far each sees and whether it has spotted
/// the party, and one staged on demand to meet or avoid. This exists only as a live-debug command; no game surface
/// offers it. Each <see cref="DebugCommandAttribute"/> here is the command: the Engine's catalogue is generated from it
/// and calls the ruleset's owner that implements this.
/// </summary>
public interface IDeveloperTravelMapCommands : IDebugCommandModule
{
    [DebugCommand("map.bands", Description = "Developer: the bands staged around the party on the open travel map, as JSON, nearest first: name, kind, size, camped, where, how far, how far it sees, whether the party sees it and whether it has spotted the party; with the party's own sight.")]
    DebugCommandResult Bands();

    [DebugCommand("map.stage-band", Description = "Developer: stage a band on the open travel map DISTANCE map pixels (0.3 to 10) off at BEARING degrees clockwise from north, camped (camp) or wandering (wander), its foe from the tables for that pixel and hour.")]
    DebugCommandResult StageBand(double distance, double bearing, string staging);
}

/// <summary>
/// Developer placement of the open travel map's view, so captures before and after a change stand at the same spot. This
/// exists only as a live-debug command; no game surface offers it.
/// </summary>
public interface IDeveloperTravelMapViewCommands : IDebugCommandModule
{
    [DebugCommand("map.view", Description = "Developer: where the open travel map's view looks, as JSON: world-map point x y (pixels), heading degrees clockwise from north, tilt degrees down, distance in metres, and whether it follows the party.")]
    DebugCommandResult View();

    [DebugCommand("map.view-set", Description = "Developer: look at world-map point X Y (pixels, as map.bands gives them) on the open travel map from HEADING degrees clockwise from north, TILT degrees down and DISTANCE metres off, which stops it following the party; each is held to the map's tuned ranges.")]
    DebugCommandResult SetView(double x, double y, double heading, double tilt, double distance);

    [DebugCommand("map.look", Description = "Developer: tune the open travel map's look by eye for this run: KEY contrast, saturation or temperature (-1 to 1), moon (the moonlight's intensity at the dark of night) or haze (a multiple of the tuned haze), to VALUE. The tuning keeps the shipped look.")]
    DebugCommandResult Look(string key, double value);
}
