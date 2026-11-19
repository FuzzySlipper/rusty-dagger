using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer movement around the current session's world: a pose in the scene, a place on the map, a
/// building there, or a named person or door; and the world variant a place draws, as a quest's world update selects it. The ruleset resolves names and lands the player through
/// the same site and movement owners ordinary travel and doors use, so the next save holds the result.
/// These exist only as live-debug commands; no game surface offers them. Each <see cref="DebugCommandAttribute"/>
/// here is the command: the Engine's catalogue is generated from it and calls the ruleset's owner that implements this.
/// </summary>
public interface IDeveloperPlacementCommands : IDebugCommandModule
{
    /// <summary>Stands the player at scene-local feet <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>; an occupied spot is refused unless forced.</summary>
    DebugCommandResult Teleport(double x, double y, double z, bool force);

    [DebugCommand("world.teleport", Description = "Developer: stand the player at scene-local feet X Y Z (as navigation.targets reports positions). Refuses a spot the body would overlap.")]
    DebugCommandResult Teleport(double x, double y, double z) => Teleport(x, y, z, force: false);

    [DebugCommand("world.teleport-force", Description = "Developer: stand the player at scene-local feet X Y Z even when the body would overlap collision there; a body the Engine cannot step free is stood at the nearest clear spot within its recovery reach, or held there.")]
    DebugCommandResult TeleportForce(double x, double y, double z) => Teleport(x, y, z, force: true);

    [DebugCommand("world.move", Description = "Developer: move the player by a scene-local offset DX DY DZ, refusing an occupied spot.")]
    DebugCommandResult Move(double dx, double dy, double dz);

    [DebugCommand("world.forward", Description = "Developer: move the player METRES along their facing, refusing an occupied spot.")]
    DebugCommandResult Forward(double metres);

    [DebugCommand("world.travel", Description = "Developer: land at a map place by name (quoted when it has spaces) or region/index, instantly and free.")]
    DebugCommandResult Travel(string place);

    [DebugCommand("world.variant", Description = "Developer: select a map place's (name or region/index) world VARIANT as a quest's world update does, or - for its base; the place draws it from its next build, and the save keeps it.")]
    DebugCommandResult Variant(string place, string variant);

    [DebugCommand("world.enter", Description = "Developer: land inside a building of a place, from its doorstep: world.enter PLACE BUILDING, the building by name or block-x/block-y/index. Refused when no clear spot stands at its door.")]
    DebugCommandResult Enter(string place, string building);

    [DebugCommand("world.goto", Description = "Developer: stand within reach of a loaded person, door or container (its label or navigation.targets id), facing it.")]
    DebugCommandResult GoTo(string target);

    [DebugCommand("world.spawn", Description = "Developer: bring COUNT of ACTOR (a published actor definition id, such as encounter-warrior or fire-daedra) within METRES (2 to 30) of the player, each on clear floor in sight of them, ahead where there is room, facing them; an enemy comes hostile. Refused, saying why, when no clear floor is found.")]
    DebugCommandResult Spawn(string actor, int count, double metres);

    [DebugCommand("world.where", Description = "Developer: the current place, profile, building and pose, and the nearest named targets.")]
    DebugCommandResult Where();

    [DebugCommand("world.find", Description = "Developer: the people of the current place offering SERVICE (for example training, food-drink, buy-spells), with their building and position.")]
    DebugCommandResult Find(string service);

    [DebugCommand("world.stand", Description = "Developer: stand the player on the ground EAST and SOUTH metres away, inside the walked window.")]
    DebugCommandResult Stand(double east, double south);

    [DebugCommand("world.scout", Description = "Developer: stand in the middle of the nearest open ground that is mostly KIND (the ruleset names its kinds), searching out to KILOMETRES.")]
    DebugCommandResult Scout(string kind, double kilometres);
}
