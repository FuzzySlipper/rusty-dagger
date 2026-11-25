using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer settings of the world around the player, for judging them by eye: the time and season, the weather, how
/// ground layers and what grows on it, the far horizon, and the classic art in place of the refreshed. Time passes
/// through the ruleset's own time owner; the rest lasts for the run. These exist only as live-debug commands; no game
/// surface offers them. Each <see cref="DebugCommandAttribute"/> here is the command: the Engine's catalogue is
/// generated from it and calls the ruleset's owner that implements this.
/// </summary>
public interface IDeveloperSceneCommands : IDebugCommandModule
{
    [DebugCommand("world.hour", Description = "Developer: let world time run on to the next HOUR (0 to 23) of the clock, as resting would.")]
    DebugCommandResult Hour(int hour);

    [DebugCommand("world.ground-blend", Description = "Developer: layer ground at CONTRAST (1 or more) with textures repeating every METRES, triplanar at SHARPNESS (0 for none).")]
    DebugCommandResult GroundBlend(double contrast, double repeatMetres, double triplanarSharpness);

    [DebugCommand("world.season", Description = "Developer: let world time run on to the first day of the next SEASON (Winter, Spring, Summer or Autumn), at this hour.")]
    DebugCommandResult Season(string season);

    [DebugCommand("world.weather", Description = "Developer: set the weather everywhere until the next day: Sunny, Cloudy, Overcast, Fog, Rain, Thunder or Snow.")]
    DebugCommandResult Weather(string kind);

    [DebugCommand("world.ground-weather", Description = "Developer: leave open ground this WET and this SNOWED, both from 0 to 1.")]
    DebugCommandResult GroundWeather(double wet, double snow);

    [DebugCommand("world.river", Description = "Developer: the nearest river's bearing, surface and width.")]
    DebugCommandResult NearestRiver();

    [DebugCommand("world.ground-survey", Description = "Developer: slopes at walking scale from random points across the land, and what a surface costs to build.")]
    DebugCommandResult GroundSurvey();

    [DebugCommand("world.horizon", Description = "Developer: what the distant horizon is drawing, and how it is set.")]
    DebugCommandResult Horizon();

    [DebugCommand("world.horizon-set", Description = "Developer: set the horizon's LIFT (1 is true height) and haze per metre FOG for this run.")]
    DebugCommandResult HorizonSet(double lift, double fogDensity);

    [DebugCommand("world.cloud-edges", Description = "Developer: draw the raymarched clouds' rims for this run at EDGE sharpness (0 for the Engine's, higher is harder) and EROSION by a finer noise (0 for none, about 1 strong), each a number or tuning for the shipped value. They show while the video options draw volumetric clouds.")]
    DebugCommandResult CloudEdges(string edge, string erosion);

    [DebugCommand("world.flora", Description = "Developer: grow flora at DENSITY times the ruleset's base count for this run, each climate keeping its share.")]
    DebugCommandResult Flora(double density);

    [DebugCommand("world.farforest", Description = "Developer: grow forest on the far hills for this run: on or off.")]
    DebugCommandResult FarForest(string state);

    [DebugCommand("world.hanging", Description = "Developer: the hanging roots, chains, cage and lanterns here: how many hang and swing, their bodies, and what their steps cost.")]
    DebugCommandResult Hanging();

    [DebugCommand("world.loose-items", Description = "Developer: the loose items' bodies near the player: how many, which are awake or asleep, what stepping them costs, and the nearest five with where they stand, their speed, weight and size.")]
    DebugCommandResult LooseItems();

    [DebugCommand("world.classic-art", Description = "Developer: draw the classic art in place of the refreshed art for this run, to compare them: on or off.")]
    DebugCommandResult ClassicArt(string state);

    [DebugCommand("world.trees", Description = "Developer: grow trees that have meshes as KIND for this run: mesh or sprite.")]
    DebugCommandResult Trees(string kind);

    [DebugCommand("world.tree-bodies", Description = "Developer: trees' trunks and rocks stand as bodies near the player (on, the default) or bodies pass through them (off), for this run; to compare their cost.")]
    DebugCommandResult TreeBodies(string state);

    [DebugCommand("world.groundcover", Description = "Developer: draw only these KINDS of ground cover for this run: grass, relief and tufts, separated by commas, or all or none.")]
    DebugCommandResult GroundCover(string kinds);

    [DebugCommand("world.interior-look", Description = "Developer: select an interior & dungeon look preset for this run (baseline, step1-light, step2-gi-ao, step3-fog, pulp-rolloff, pulp-neutral, pulp-aces, pulp-aces-grain, pulp-aces-grain-bold, pulp-aces-autoexp, dungeon-cobalt, dungeon-violet, dungeon-teal, dungeon-emerald, interior-cozy, interior-dramatic, or status/list).")]
    DebugCommandResult InteriorLook(string preset);
}
