using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer movement around the current session's world: a pose in the scene, a place on the map, a
/// building there, or a named person or door. The ruleset resolves names and lands the player through
/// the same site and movement owners ordinary travel and doors use, so the next save holds the result.
/// These exist only as live-debug commands; no game surface offers them.
/// </summary>
public interface IDeveloperWorldSession
{
    /// <summary>Stands the player at scene-local feet <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>; an occupied spot is refused unless forced.</summary>
    DebugCommandResult Teleport(double x, double y, double z, bool force);

    /// <summary>Moves the player by a scene-local offset, as <see cref="Teleport"/> does.</summary>
    DebugCommandResult Move(double dx, double dy, double dz);

    /// <summary>Moves the player along their facing on the ground plane.</summary>
    DebugCommandResult Forward(double metres);

    /// <summary>Lands the player at a place on the map, named or numbered, as a journey arrives there with no time or cost.</summary>
    DebugCommandResult Travel(string place);

    /// <summary>Lands the player inside a building of a place, as its door would, without the door's greeting or rules.</summary>
    DebugCommandResult Enter(string place, string building);

    /// <summary>Stands the player within reach of a loaded person, door or container, facing it.</summary>
    DebugCommandResult GoTo(string target);

    /// <summary>The current place, profile, building and pose, and the nearest named targets.</summary>
    DebugCommandResult Where();

    /// <summary>Brings one actor of a published definition into the scene <paramref name="metres"/> along the player's facing, facing back.</summary>
    DebugCommandResult Spawn(string actor, double metres);

    /// <summary>The people of the current place who offer a service, and where they stand.</summary>
    DebugCommandResult Find(string service);

    /// <summary>Lets world time run on to the next time the clock reads <paramref name="hour"/>.</summary>
    DebugCommandResult Hour(int hour);

    /// <summary>Changes how ground layers for this run.</summary>
    DebugCommandResult GroundBlend(double contrast, double repeatMetres, double triplanarSharpness);

    /// <summary>Lets world time run on to the first day of the next <paramref name="season"/>.</summary>
    DebugCommandResult Season(string season);

    /// <summary>Sets the weather everywhere until the day's next change.</summary>
    DebugCommandResult Weather(string kind);
    /// <summary>Leaves open ground this wet and this snowed, for a developer.</summary>
    DebugCommandResult GroundWeather(double wet, double snow);

    /// <summary>Stands the player on the ground <paramref name="east"/> and <paramref name="south"/> metres away, as far as the walked window reaches.</summary>
    DebugCommandResult Stand(double east, double south);

    /// <summary>Stands the player in the nearest ground of a <paramref name="kind"/> the ruleset tells apart, within <paramref name="kilometres"/>.</summary>
    DebugCommandResult Scout(string kind, double kilometres);

    /// <summary>Slopes at walking scale from random points across the land, and what a map pixel's surface costs to build.</summary>
    DebugCommandResult GroundSurvey();
    DebugCommandResult NearestRiver();

    /// <summary>What the distant horizon is drawing, and how it is set.</summary>
    DebugCommandResult Horizon();

    /// <summary>Sets the horizon's lift and haze for this run, for judging them by eye.</summary>
    DebugCommandResult HorizonSet(double lift, double fogDensity);

    /// <summary>Grows flora at a density for this run, for judging it by eye.</summary>
    DebugCommandResult Flora(double density);

    /// <summary>Which ground cover draws for this run, for comparing its kinds by eye.</summary>
    DebugCommandResult GroundCover(string kinds);

    /// <summary>Whether trees with meshes grow as meshes or sprites for this run, for comparing them by eye.</summary>
    DebugCommandResult Trees(string kind);

    /// <summary>Whether forest grows on the far hills for this run, for comparing by eye.</summary>
    DebugCommandResult FarForest(string state);

    /// <summary>Draws the ruleset's classic art in place of its refreshed art for this run, to compare the two: on or off.</summary>
    DebugCommandResult ClassicArt(string state);
}

/// <summary>The live-debug commands over <see cref="IDeveloperWorldSession"/>; the delegate follows session replacement.</summary>
public sealed class DeveloperWorldDebugModule(Func<IDeveloperWorldSession> current) : IDebugCommandModule
{
    [DebugCommand("world.teleport", Description = "Developer: stand the player at scene-local feet X Y Z (as navigation.targets reports positions). Refuses a spot the body would overlap.")]
    public DebugCommandResult Teleport(double x, double y, double z) => current().Teleport(x, y, z, force: false);

    [DebugCommand("world.teleport-force", Description = "Developer: stand the player at scene-local feet X Y Z even when the body would overlap collision there.")]
    public DebugCommandResult TeleportForce(double x, double y, double z) => current().Teleport(x, y, z, force: true);

    [DebugCommand("world.move", Description = "Developer: move the player by a scene-local offset DX DY DZ, refusing an occupied spot.")]
    public DebugCommandResult Move(double dx, double dy, double dz) => current().Move(dx, dy, dz);

    [DebugCommand("world.forward", Description = "Developer: move the player METRES along their facing, refusing an occupied spot.")]
    public DebugCommandResult Forward(double metres) => current().Forward(metres);

    [DebugCommand("world.travel", Description = "Developer: land at a map place by name (quoted when it has spaces) or region/index, instantly and free.")]
    public DebugCommandResult Travel(string place) => current().Travel(place);

    [DebugCommand("world.enter", Description = "Developer: land inside a building of a place: world.enter PLACE BUILDING, the building by name or block-x/block-y/index.")]
    public DebugCommandResult Enter(string place, string building) => current().Enter(place, building);

    [DebugCommand("world.goto", Description = "Developer: stand within reach of a loaded person, door or container (its label or navigation.targets id), facing it.")]
    public DebugCommandResult GoTo(string target) => current().GoTo(target);

    [DebugCommand("world.spawn", Description = "Developer: bring one ACTOR (a published actor definition id, such as fire-daedra) into the scene METRES along the player's facing, facing back.")]
    public DebugCommandResult Spawn(string actor, double metres) => current().Spawn(actor, metres);

    [DebugCommand("world.where", Description = "Developer: the current place, profile, building and pose, and the nearest named targets.")]
    public DebugCommandResult Where() => current().Where();

    [DebugCommand("world.find", Description = "Developer: the people of the current place offering SERVICE (for example training, food-drink, buy-spells), with their building and position.")]
    public DebugCommandResult Find(string service) => current().Find(service);

    [DebugCommand("world.hour", Description = "Developer: let world time run on to the next HOUR (0 to 23) of the clock, as resting would.")]
    public DebugCommandResult Hour(int hour) => current().Hour(hour);

    [DebugCommand("world.ground-blend", Description = "Developer: layer ground at CONTRAST (1 or more) with textures repeating every METRES, triplanar at SHARPNESS (0 for none).")]
    public DebugCommandResult GroundBlend(double contrast, double repeatMetres, double triplanarSharpness) => current().GroundBlend(contrast, repeatMetres, triplanarSharpness);

    [DebugCommand("world.season", Description = "Developer: let world time run on to the first day of the next SEASON (Winter, Spring, Summer or Autumn), at this hour.")]
    public DebugCommandResult Season(string season) => current().Season(season);

    [DebugCommand("world.weather", Description = "Developer: set the weather everywhere until the next day: Sunny, Cloudy, Overcast, Fog, Rain, Thunder or Snow.")]
    public DebugCommandResult Weather(string kind) => current().Weather(kind);

    [DebugCommand("world.ground-weather", Description = "Developer: leave open ground this WET and this SNOWED, both from 0 to 1.")]
    public DebugCommandResult GroundWeather(double wet, double snow) => current().GroundWeather(wet, snow);

    [DebugCommand("world.stand", Description = "Developer: stand the player on the ground EAST and SOUTH metres away, inside the walked window.")]
    public DebugCommandResult Stand(double east, double south) => current().Stand(east, south);

    [DebugCommand("world.scout", Description = "Developer: stand in the middle of the nearest open ground that is mostly KIND (the ruleset names its kinds), searching out to KILOMETRES.")]
    public DebugCommandResult Scout(string kind, double kilometres) => current().Scout(kind, kilometres);

    [DebugCommand("world.river", Description = "Developer: the nearest river's bearing, surface and width.")]
    public DebugCommandResult NearestRiver() => current().NearestRiver();

    [DebugCommand("world.ground-survey", Description = "Developer: slopes at walking scale from random points across the land, and what a surface costs to build.")]
    public DebugCommandResult GroundSurvey() => current().GroundSurvey();

    [DebugCommand("world.horizon", Description = "Developer: what the distant horizon is drawing, and how it is set.")]
    public DebugCommandResult Horizon() => current().Horizon();

    [DebugCommand("world.horizon-set", Description = "Developer: set the horizon's LIFT (1 is true height) and haze per metre FOG for this run.")]
    public DebugCommandResult HorizonSet(double lift, double fogDensity) => current().HorizonSet(lift, fogDensity);

    [DebugCommand("world.flora", Description = "Developer: grow flora at DENSITY times the ruleset's base count for this run, each climate keeping its share.")]
    public DebugCommandResult Flora(double density) => current().Flora(density);

    [DebugCommand("world.farforest", Description = "Developer: grow forest on the far hills for this run: on or off.")]
    public DebugCommandResult FarForest(string state) => current().FarForest(state);

    [DebugCommand("world.classic-art", Description = "Developer: draw the classic art in place of the refreshed art for this run, to compare them: on or off.")]
    public DebugCommandResult ClassicArt(string state) => current().ClassicArt(state);

    [DebugCommand("world.trees", Description = "Developer: grow trees that have meshes as KIND for this run: mesh or sprite.")]
    public DebugCommandResult Trees(string kind) => current().Trees(kind);

    [DebugCommand("world.groundcover", Description = "Developer: draw only these KINDS of ground cover for this run: grass, relief and tufts, separated by commas, or all or none.")]
    public DebugCommandResult GroundCover(string kinds) => current().GroundCover(kinds);
}
