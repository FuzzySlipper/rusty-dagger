using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// The developer first-person workbench over the session's held art: a motion viewer that shows a look with one of its
/// actions, holds any of the action's keys (its keyed poses, or the marks of the clip it plays), and plays the action at
/// any speed or paused at a moment. The ruleset owns what a look, an action and their timing are; the viewer draws through
/// the ruleset's own first-person presentation, and the ruleset offers the Engine tunables page over the values the art
/// is drawn by beside these. These exist only as live-debug commands and the developer shell's workbench; no game surface
/// offers them. Each <see cref="DebugCommandAttribute"/> here is the command: the Engine's catalogue is generated from it
/// and calls the ruleset's owner that implements this.
/// </summary>
public interface IDeveloperHeldCommands : IDebugCommandModule
{
    [DebugCommand("held.motion", Description = "Developer motion viewer: show a held LOOK (e.g. weapon.dagger.steel) at rest with one of its ACTIONs (e.g. fastA), and list that action's keys.")]
    DebugCommandResult Show(string look, string action);

    [DebugCommand("held.motion.key", Description = "Developer motion viewer: hold the shown look exactly at key N (0 is the first key; -1 is rest).")]
    DebugCommandResult Key(int n);

    /// <summary>Holds the next key (<paramref name="step"/> 1) or the previous one (-1), wrapping through rest.</summary>
    DebugCommandResult Step(int step);

    [DebugCommand("held.motion.next", Description = "Developer motion viewer: hold the next key (after the last, rest).")]
    DebugCommandResult Next() => Step(1);

    [DebugCommand("held.motion.prev", Description = "Developer motion viewer: hold the previous key (before the first, rest).")]
    DebugCommandResult Previous() => Step(-1);

    [DebugCommand("held.motion.play", Description = "Developer motion viewer: play the action from rest at SPEED (1 is as in play, 0.25 a quarter).")]
    DebugCommandResult Play(float speed);

    [DebugCommand("held.motion.at", Description = "Developer motion viewer: show the action paused SECONDS after its start, as the Engine plays it.")]
    DebugCommandResult At(float seconds);

    [DebugCommand("held.shield", Description = "Developer: place a held shield by eye: PART seat (where on the hand joint, then its turn) or carry (the hand's grip, then its hold), X Y Z then three degrees. Shown at the next draw; the answer is what to save.")]
    DebugCommandResult Shield(string part, float x, float y, float z, float turnX, float turnY, float turnZ);

    [DebugCommand("held.arms", Description = "Developer motion viewer: show the look in arm set SET, or worn for what the player wears.")]
    DebugCommandResult Arms(string set);

    [DebugCommand("held.enchant", Description = "Developer motion viewer: show the look enchanted (true) or plain (false).")]
    DebugCommandResult Enchant(bool on);

    [DebugCommand("held.clip", Description = "Developer readout: what the drawn held art plays now, in play or in the viewer (for clip-led arms: the clip, its time and phase, and what fades out under it).")]
    DebugCommandResult Read();

    [DebugCommand("held.motion.off", Description = "Developer motion viewer: return the hands to what they wield.")]
    DebugCommandResult Off();
}
