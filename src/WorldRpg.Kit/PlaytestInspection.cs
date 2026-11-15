using Rusty.Engine.Debugging;
using Rusty.Engine.Interaction;

namespace WorldRpg.Kit;

/// <summary>
/// Read-only scene for the Engine's standard interaction debug module over an inspection the ruleset builds afresh at
/// each read, so it follows what the player can reach now.
/// </summary>
public sealed class CurrentInteractionInspectionScene(Func<WorldInteraction> current) : IWorldInteractionScene
{
    public InteractionSceneSnapshot ReadInteraction() => current().Inspect().Scene;
    public InteractionActionResult UseInteraction(InteractionTarget target) =>
        throw new InvalidOperationException("An inspection scene cannot activate a target.");
}

/// <summary>Loaded target meaning stays in the ruleset, which reads its targets at each call.</summary>
public sealed class PlaytestTargetsDebugModule(Func<DebugCommandResult> targets) : IDebugCommandModule
{
    [DebugCommand("navigation.targets", Description = "Read named currently loaded gameplay targets; no movement or route guarantee.")]
    public DebugCommandResult Targets() => targets();
}
