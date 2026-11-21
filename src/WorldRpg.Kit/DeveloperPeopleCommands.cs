using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer look at the people who walk the world: draws every walking person as one figure, the ruleset's parts-rig
/// test figure seen from a set of directions or one of its gsplat characters, to judge the figure in play and what
/// many of them cost. This exists only as a
/// live-debug command; no game surface offers it. The <see cref="DebugCommandAttribute"/> here is the command: the
/// Engine's catalogue is generated from it and calls the ruleset's owner that implements this.
/// </summary>
public interface IDeveloperPeopleCommands : IDebugCommandModule
{
    [DebugCommand("people.rig", Description = "Developer: draw every walking person as the parts-rig test figure seen from SET directions (4 or 8), walking while they walk; off draws their sprites.")]
    DebugCommandResult Rig(string set);

    [DebugCommand("people.splat", Description = "Developer: draw every walking person as gsplat character ID, lit, walking while they walk and idling when they stop; own draws each as the character of their classic townsperson art (the default); off draws their sprites.")]
    DebugCommandResult Splat(string character);

    [DebugCommand("people.spawn", Description = "Developer: stand the COUNT nearest walking people in front of the player as gsplat character ID, whatever the rest are drawn as, until people.unstage; off draws them as the rest again.")]
    DebugCommandResult Spawn(string character, int count);

    [DebugCommand("people.splat-look", Description = "Developer: draw the gsplat people lit on a pixel grid of GRID screen pixels (0 smooth), a block drawn where they cover COVERAGE of it, snapped to their own palette (own) or not (none), posed POSERATE times a second (0 every frame), for this run; the tuning's splatPeople holds the shipped look.")]
    DebugCommandResult SplatLook(int grid, double coverage, string palette, double poseRate);

    [DebugCommand("people.splat-pace", Description = "Developer: walkers drawn in a long skirt play their gentle walk faster to keep the walkers' pace (clip), or the ones people.spawn stands walk slower, at the gentle walk's own pace (slow), for this run.")]
    DebugCommandResult SplatPace(string pace);

    [DebugCommand("people.splat-shadow", Description = "Developer: cast the gsplat people into the sun's shadow as none, ellipsoids (one a joint) or splats (their own silhouette), for this run; the tuning's splatPeople holds the shipped caster.")]
    DebugCommandResult SplatShadow(string caster);
}

/// <summary>
/// Developer staging of the people who walk the world, to look at a handful of them close up: the nearest loaded
/// townsfolk stand in a line in front of the player or walk short paths across the view, until unstaged. This exists
/// only as a live-debug command; no game surface offers it.
/// </summary>
public interface IDeveloperPeopleStageCommands : IDebugCommandModule
{
    [DebugCommand("people.stage", Description = "Developer: stand the COUNT nearest walking people in a line METRES in front of the player, SPREAD metres wide, facing the player, until people.unstage.")]
    DebugCommandResult Stage(int count, double metres, double spread);

    [DebugCommand("people.cross", Description = "Developer: walk the COUNT nearest walking people back and forth across the view, SPREAD metres wide, in rows from METRES in front of the player, until people.unstage.")]
    DebugCommandResult Cross(int count, double metres, double spread);

    [DebugCommand("people.unstage", Description = "Developer: let every staged walking person wander on their own again.")]
    DebugCommandResult Unstage();
}
