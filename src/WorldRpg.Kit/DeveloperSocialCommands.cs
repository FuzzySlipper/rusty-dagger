using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer runtime inspection commands for the live social and political layers:
/// active settlement households/offices, regional Great Houses/rulers/seats, and player standings/law/vouches.
/// </summary>
public interface IDeveloperSocialCommands : IDebugCommandModule
{
    [DebugCommand("social.site", Description = "Developer: inspect the active settlement's loaded households, local household grudge/goodwill standings, and local institutional offices as JSON.")]
    DebugCommandResult Site();

    [DebugCommand("social.region", Description = "Developer: inspect the active region's provincial Great Houses, Tier-2 palace court offices, regional ruler, board-game seat personas, and sparse office overrides as JSON.")]
    DebugCommandResult Region();

    [DebugCommand("social.player", Description = "Developer: inspect the player's regional reputations, non-zero faction standings, guild affiliations, legal status, and active vouches as JSON.")]
    DebugCommandResult Player();
}
