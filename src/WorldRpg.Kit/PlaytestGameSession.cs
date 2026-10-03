using Rusty.Engine.Debugging;
using Rusty.Engine.Interaction;

namespace WorldRpg.Kit;

/// <summary>Optional live gameplay inspection over the current ruleset session.</summary>
public interface IPlaytestGameSession
{
    IReadOnlyList<string> PlaytestActions { get; }
    DebugCommandResult ReadPlaytestObservation();
    PlaytestAction InspectPlaytestAction(string id);
    DebugCommandResult InspectPlaytestLook(double yawDegrees, double pitchDegrees);
    DebugCommandResult ReadPlaytestTargets();
}

/// <summary>Optional inspection of the current session's existing interaction and spatial owners.</summary>
public interface IPlaytestWorldInspectionSession
{
    WorldInteraction CreateInteractionInspection();
    DebugCommandResult ReadSpatialGrid(int radius, int verticalRadius, double cellSize);
    DebugCommandResult ReadSpatialProbe(double distance);
    DebugCommandResult ReadJumpPlan(double x, double y, double z);
}

/// <summary>Read-only scene adapter that follows the current session for Engine's standard debug module.</summary>
public sealed class CurrentInteractionInspectionScene(Func<WorldInteraction> current) : IWorldInteractionScene
{
    public InteractionSceneSnapshot ReadInteraction() => current().Inspect().Scene;
    public InteractionActionResult UseInteraction(InteractionTarget target) =>
        throw new InvalidOperationException("An inspection scene cannot activate a target.");
}

/// <summary>Loaded target meaning stays in the ruleset; the delegate follows session replacement.</summary>
public sealed class PlaytestTargetsDebugModule(Func<DebugCommandResult> targets) : IDebugCommandModule
{
    [DebugCommand("navigation.targets", Description = "Read named currently loaded gameplay targets; no movement or route guarantee.")]
    public DebugCommandResult Targets() => targets();
}
