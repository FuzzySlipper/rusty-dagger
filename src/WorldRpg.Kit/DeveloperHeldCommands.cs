using Rusty.Engine;
using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// The developer first-person workbench over the session's held art: a motion viewer that shows a look with one of its
/// actions, holds any of the action's keys (its keyed poses, or the marks of the clip it plays), and plays the action at
/// any speed or paused at a moment; and the Engine tunables page over the values the art is drawn by, saved to the
/// content the ruleset names. The ruleset owns what a look, an action and their timing are; the viewer draws through the
/// ruleset's own first-person presentation.
/// These exist only as live-debug commands and the developer shell's workbench; no game surface offers them.
/// </summary>
public interface IDeveloperHeldSession
{
    /// <summary>Shows a look at rest with one of its actions, and lists the action's keys.</summary>
    DebugCommandResult ShowHeld(string look, string action);

    /// <summary>Holds the shown look exactly at key <paramref name="key"/> (0 is the first; -1 is rest).</summary>
    DebugCommandResult HoldHeldKey(int key);

    /// <summary>Holds the next key (<paramref name="step"/> 1) or the previous one (-1), wrapping through rest.</summary>
    DebugCommandResult StepHeldKey(int step);

    /// <summary>Plays the action from rest <paramref name="speed"/> times as fast as play (0.25 a quarter).</summary>
    DebugCommandResult PlayHeld(float speed);

    /// <summary>Shows the action paused <paramref name="seconds"/> after its start, as the Engine plays it.</summary>
    DebugCommandResult ShowHeldAt(float seconds);

    /// <summary>What the drawn held art plays now (in play or in the viewer): for clip-led arms, the clip, its time and phase.</summary>
    DebugCommandResult ReadHeld();

    /// <summary>Ends the viewer: the hands show what they wield again.</summary>
    DebugCommandResult EndHeldView();

    /// <summary>The workbench's tunables page: what it shows now, with each value's source.</summary>
    TunablesPage HeldWorkbench();

    /// <summary>Sets one of the page's choosers; null when it is shown, else why not.</summary>
    string? ChooseHeld(string chooser, string value);
}

/// <summary>The live-debug commands over <see cref="IDeveloperHeldSession"/>; the delegate follows session replacement.</summary>
public sealed class DeveloperHeldDebugModule(Func<IDeveloperHeldSession> current) : IDebugCommandModule
{
    /// <summary>The Engine tunables page over the current session's workbench, saving through the content source.</summary>
    public static TunablesDebugModule Workbench(IContentSourceService source, Func<IDeveloperHeldSession> current) =>
        new(source, () => current().HeldWorkbench(), (chooser, value) => current().ChooseHeld(chooser, value));

    [DebugCommand("held.motion", Description = "Developer motion viewer: show a held LOOK (e.g. weapon.dagger.steel) at rest with one of its ACTIONs (e.g. strikeDown), and list that action's keys.")]
    public DebugCommandResult Show(string look, string action) => current().ShowHeld(look, action);

    [DebugCommand("held.motion.key", Description = "Developer motion viewer: hold the shown look exactly at key N (0 is the first key; -1 is rest).")]
    public DebugCommandResult Key(int n) => current().HoldHeldKey(n);

    [DebugCommand("held.motion.next", Description = "Developer motion viewer: hold the next key (after the last, rest).")]
    public DebugCommandResult Next() => current().StepHeldKey(1);

    [DebugCommand("held.motion.prev", Description = "Developer motion viewer: hold the previous key (before the first, rest).")]
    public DebugCommandResult Previous() => current().StepHeldKey(-1);

    [DebugCommand("held.motion.play", Description = "Developer motion viewer: play the action from rest at SPEED (1 is as in play, 0.25 a quarter).")]
    public DebugCommandResult Play(float speed) => current().PlayHeld(speed);

    [DebugCommand("held.motion.at", Description = "Developer motion viewer: show the action paused SECONDS after its start, as the Engine plays it.")]
    public DebugCommandResult At(float seconds) => current().ShowHeldAt(seconds);

    [DebugCommand("held.clip", Description = "Developer readout: what the drawn held art plays now, in play or in the viewer (for clip-led arms: the clip, its time and phase, and what fades out under it).")]
    public DebugCommandResult Read() => current().ReadHeld();

    [DebugCommand("held.motion.off", Description = "Developer motion viewer: return the hands to what they wield.")]
    public DebugCommandResult Off() => current().EndHeldView();
}
