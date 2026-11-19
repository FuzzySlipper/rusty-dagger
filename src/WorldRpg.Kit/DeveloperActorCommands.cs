using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer inspection and steering of the actors in the current site: one actor's whole state read from its live
/// components and the behaviour owners' latest evidence, an opt-in trace of one enemy's decisions, and run-only
/// overrides that freeze, pacify, hold, retarget or provoke enemies through the inputs their behaviour owner already
/// reads. Overrides are never saved. These exist only as live-debug commands; no game surface offers them. Each
/// <see cref="DebugCommandAttribute"/> here is the command: the Engine's catalogue is generated from it and calls the
/// ruleset's owner that implements this.
/// </summary>
public interface IDeveloperActorCommands : IDebugCommandModule
{
    [DebugCommand("actor.inspect", Description = "Developer: one actor's identities, definition, stats and tracks, effects, equipment, target, hostility and team, pursuit state and maneuver, attack and spell readiness, wander state and overrides, as JSON. ACTOR is an actor id, target (the live one the player faces, within 15 degrees) or nearest.")]
    DebugCommandResult Inspect(string actor);

    [DebugCommand("actor.trace", Description = "Developer: trace ACTOR's decisions (an actor id, target or nearest): perception, target choice, motor rolls and maneuver, attack and spell gates, each passed, refused or chosen with its reason and values, and what each strike and cast reached beside the target it meant. Shown on the actors overlay; run it again to read back the recent decisions; off stops it.")]
    DebugCommandResult Trace(string actor);

    [DebugCommand("ai.freeze", Description = "Developer: on holds every enemy still, starting no attack or spell, as a paralysis does; off lets them act. For this run only.")]
    DebugCommandResult Freeze(string state);

    [DebugCommand("actor.passive", Description = "Developer: on makes ACTOR pursue and attack no one, even when struck; off ends it. For this run only.")]
    DebugCommandResult Passive(string actor, string state);

    [DebugCommand("actor.force-state", Description = "Developer: hold ACTOR in STATE whatever its motor rolls: Chase (close without swinging), Attack (close and swing in reach), Retreat, Strafe, Search (go where the target was last seen) or Idle, the moving states as if it knew where its target is; off ends it. For this run only.")]
    DebugCommandResult ForceState(string actor, string state);

    [DebugCommand("actor.target", Description = "Developer: make ACTOR pursue TARGET (player, an actor id, target or nearest) in place of its own choice; off ends it. For this run only.")]
    DebugCommandResult Target(string actor, string target);

    [DebugCommand("actor.hostile", Description = "Developer: on makes ACTOR fight the player whatever its disposition (pacified, ally or quest-restrained), without changing what is saved; off ends it. For this run only.")]
    DebugCommandResult Hostile(string actor, string state);
}
