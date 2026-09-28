using Rusty.Engine.Debugging;

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

/// <summary>Loaded target meaning stays in the ruleset; the delegate follows session replacement.</summary>
public sealed class PlaytestTargetsDebugModule(Func<DebugCommandResult> targets) : IDebugCommandModule
{
    [DebugCommand("navigation.targets", Description = "Read named currently loaded gameplay targets; no movement or route guarantee.")]
    public DebugCommandResult Targets() => targets();
}
