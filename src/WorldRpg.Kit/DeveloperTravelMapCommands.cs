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
