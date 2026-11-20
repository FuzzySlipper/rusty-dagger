using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// The developer's input readout: a line at the bottom of the main view, carried by the HUD projection, with the
/// player's live animation and combat state and the latest input refused or dropped, and why. Off by default and never
/// saved; the developer panel's toggle sends these. Each <see cref="DebugCommandAttribute"/> here is the command: the
/// Engine's catalogue is generated from it and calls the ruleset's owner that implements this.
/// </summary>
public interface IDeveloperReadoutCommands : IDebugCommandModule
{
    /// <summary>Whether the readout is shown.</summary>
    bool On { get; set; }

    [DebugCommand("hud.readout", Description = "Developer: whether the input readout is shown, as JSON; hud.readout-on and hud.readout-off turn it on and off.")]
    DebugCommandResult Read() => DebugCommandResult.Success($"{{\"on\":{(On ? "true" : "false")}}}");

    [DebugCommand("hud.readout-on", Description = "Developer: show a line at the bottom of the view with the player's weapon, attack, guard, cast and first-person clip state, and the latest input refused or dropped and why, held for 5 s.")]
    DebugCommandResult ShowReadout() => Turn(true);

    [DebugCommand("hud.readout-off", Description = "Developer: hide the input readout.")]
    DebugCommandResult HideReadout() => Turn(false);

    private DebugCommandResult Turn(bool on)
    {
        On = on;
        return DebugCommandResult.Success($"Input readout {(on ? "on" : "off")}.");
    }
}
