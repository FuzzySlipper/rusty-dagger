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

    /// <summary>The people of the current place who offer a service, and where they stand.</summary>
    DebugCommandResult Find(string service);
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

    [DebugCommand("world.where", Description = "Developer: the current place, profile, building and pose, and the nearest named targets.")]
    public DebugCommandResult Where() => current().Where();

    [DebugCommand("world.find", Description = "Developer: the people of the current place offering SERVICE (for example training, food-drink, buy-spells), with their building and position.")]
    public DebugCommandResult Find(string service) => current().Find(service);
}
