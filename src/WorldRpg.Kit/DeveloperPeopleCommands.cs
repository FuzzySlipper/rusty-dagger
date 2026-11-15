using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer look at the people who walk the world: draws every walking person as the ruleset's parts-rig test figure,
/// seen from a set of directions, to judge the figure in play and what many of them cost. This exists only as a
/// live-debug command; no game surface offers it. The <see cref="DebugCommandAttribute"/> here is the command: the
/// Engine's catalogue is generated from it and calls the ruleset's owner that implements this.
/// </summary>
public interface IDeveloperPeopleCommands : IDebugCommandModule
{
    [DebugCommand("people.rig", Description = "Developer: draw every walking person as the parts-rig test figure seen from SET directions (4 or 8), walking while they walk; off draws their own art again.")]
    DebugCommandResult Rig(string set);
}
