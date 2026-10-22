using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer inspection over the current session's world: the ruleset's named debug overlay sets, drawn by the
/// Engine's inspection overlays (<c>engine.inspect.overlay show set NAME</c>), and its named inspection targets. A set
/// is built only while it is on, so a hidden set costs nothing; turning one off removes it.
/// </summary>
public interface IDeveloperInspectionSession
{
    /// <summary>Every overlay set the ruleset can publish, in the order a panel lists them.</summary>
    IReadOnlyList<string> DebugOverlaySets { get; }

    /// <summary>Whether a set is being built and published now.</summary>
    bool IsDebugOverlayOn(string name);

    /// <summary>Starts or stops building a set; false when the ruleset has no set of that name.</summary>
    bool SetDebugOverlay(string name, bool on);

    /// <summary>The named points inspection views frame (the player, the person spoken to, the nearest door).</summary>
    IReadOnlyList<InspectionTarget> InspectionTargets();
}

/// <summary>The live-debug commands over <see cref="IDeveloperInspectionSession"/>; the delegate follows session replacement.</summary>
public sealed class DeveloperInspectionDebugModule(Func<IDeveloperInspectionSession> current) : IDebugCommandModule
{
    [DebugCommand("world.overlays", Description = "Developer: the ruleset's debug overlay sets and which are being built. Draw one with world.overlay-on NAME and engine.inspect.overlay show set NAME.")]
    public DebugCommandResult List()
    {
        IDeveloperInspectionSession session = current();
        return DebugCommandResult.Success("{\"sets\":[" + string.Join(",", session.DebugOverlaySets.Select(name =>
            $"{{\"name\":\"{name}\",\"on\":{(session.IsDebugOverlayOn(name) ? "true" : "false")}}}")) + "]}");
    }

    [DebugCommand("world.overlay-on", Description = "Developer: start building the debug overlay set NAME every frame; engine.inspect.overlay show set NAME draws it.")]
    public DebugCommandResult On(string name) => Set(name, on: true);

    [DebugCommand("world.overlay-off", Description = "Developer: stop building the debug overlay set NAME and remove it.")]
    public DebugCommandResult Off(string name) => Set(name, on: false);

    private DebugCommandResult Set(string name, bool on) => current().SetDebugOverlay(name, on)
        ? DebugCommandResult.Success($"{name} {(on ? "on" : "off")}.")
        : DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"No overlay set is named '{name}'. world.overlays lists them.");
}
