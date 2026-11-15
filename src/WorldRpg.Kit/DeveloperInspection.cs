using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer inspection over the current session's world: the ruleset's named debug overlay sets, drawn by the
/// Engine's inspection overlays (<c>engine.inspect.overlay show set NAME</c>). A set is built only while it is on, so
/// a hidden set costs nothing; turning one off removes it. Each <see cref="DebugCommandAttribute"/> here is the
/// command: the Engine's catalogue is generated from it and calls the ruleset's owner that implements this.
/// </summary>
public interface IDeveloperInspectionCommands : IDebugCommandModule
{
    /// <summary>Every overlay set the ruleset can publish, in the order a panel lists them.</summary>
    IReadOnlyList<string> Sets { get; }

    /// <summary>Whether a set is being built and published now.</summary>
    bool IsOn(string name);

    /// <summary>Starts or stops building a set; false when the ruleset has no set of that name.</summary>
    bool Set(string name, bool on);

    [DebugCommand("world.overlays", Description = "Developer: the ruleset's debug overlay sets and which are being built. Draw one with world.overlay-on NAME and engine.inspect.overlay show set NAME.")]
    DebugCommandResult List() => DebugCommandResult.Success("{\"sets\":[" + string.Join(",", Sets.Select(name =>
        $"{{\"name\":\"{name}\",\"on\":{(IsOn(name) ? "true" : "false")}}}")) + "]}");

    [DebugCommand("world.overlay-on", Description = "Developer: start building the debug overlay set NAME every frame; engine.inspect.overlay show set NAME draws it.")]
    DebugCommandResult On(string name) => Turn(name, on: true);

    [DebugCommand("world.overlay-off", Description = "Developer: stop building the debug overlay set NAME and remove it.")]
    DebugCommandResult Off(string name) => Turn(name, on: false);

    private DebugCommandResult Turn(string name, bool on) => Set(name, on)
        ? DebugCommandResult.Success($"{name} {(on ? "on" : "off")}.")
        : DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"No overlay set is named '{name}'. world.overlays lists them.");
}
