using Rusty.Engine.Debugging;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>How the developer commands and the playtest readouts answer: a refusal, or JSON written in one pass.</summary>
internal static class DaggerfallDeveloperReplies
{
    internal static DebugCommandResult Refused(string reason) => DebugCommandResult.Failure(DebugCommandStatus.Failed, reason);
}
