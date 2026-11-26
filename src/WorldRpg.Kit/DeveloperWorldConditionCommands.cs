using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer authoring and inspection commands for world-condition tokens and town notice boards so playtests can
/// exercise notice boards, condition-staged expedition bands, gate/service scrutiny, and condition-aware talk/barks
/// before or alongside the political board game.
/// </summary>
public interface IDeveloperWorldConditionCommands : IDebugCommandModule
{
    [DebugCommand("world.conditions", Description = "Developer: list active world-condition tokens and gate scrutiny for the current region as JSON.")]
    DebugCommandResult ListConditions();

    [DebugCommand("world.condition-set", Description = "Developer: set a stubbed world condition KIND (grain-shortage, border-tension, monster-surge, crime-crackdown, guild-embargo, succession-crisis, plague-scare, decree) at SEVERITY (1-3) in the current region, with TAG (none, a subject tag like undead/orcs, or a numeric faction id) and DAYS duration (0 for no expiry).")]
    DebugCommandResult SetCondition(string kind, int severity, string tag, int days);

    [DebugCommand("world.condition-clear", Description = "Developer: clear one world-condition TOKEN_ID or 'all' authored condition tokens.")]
    DebugCommandResult ClearCondition(string target);

    [DebugCommand("world.notice-board", Description = "Developer: read the active town's public notice board.")]
    DebugCommandResult NoticeBoard();

    [DebugCommand("world.word-spread", Description = "Developer: inspect active news/standing wavefronts and their arrival status at the current site as JSON.")]
    DebugCommandResult ListWordSpread();

    [DebugCommand("world.dispatches", Description = "Developer: inspect active and resolved board-game courier dispatches as JSON.")]
    DebugCommandResult ListDispatches();

    [DebugCommand("world.dispatch-send", Description = "Developer: stage a board-game courier dispatch from the active site for SEAT_ID (e.g. crown:daggerfall), ACTION_KIND (e.g. border-posture), to DESTINATION_REGION (0-61).")]
    DebugCommandResult SendDispatch(string seatId, string actionKind, int destinationRegion);

    [DebugCommand("world.clash", Description = "Developer: stage a live two-sided creature-faction clash nearby between FACTION_A and FACTION_B (or 'auto' to pick from active regional world conditions) with COUNT combatants per side.")]
    DebugCommandResult StageClash(string factionA, string factionB, int count);
}
