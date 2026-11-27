using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer runtime inspection commands for the political board the session runs: the table at a glance, one seat in
/// depth, and the recent turns as they were played.
/// </summary>
public interface IDeveloperBoardCommands : IDebugCommandModule
{
    [DebugCommand("board.state", Description = "Developer: inspect the political board as JSON: turn and next Sunday, every seat's holder, prestige, resources, plan and reserves, wars, truces, feuds, trade agreements, regions with allegiance, tracks, supply, offices and conditions, the arbiter's balance, pending moves, queued nudges and open arcs.")]
    DebugCommandResult State();

    [DebugCommand("board.seat", Description = "Developer: inspect one political board seat (its id, such as daggerfall or mages-guild) as JSON: holder and the persona playing it, agenda, plan, grudges and help given and received, relations, hand and decks, court conditions, boons, pieces and footholds, and the world's holder of its office.")]
    DebugCommandResult Seat(string seat);

    [DebugCommand("board.turns", Description = "Developer: inspect the board's last few turns this session (up to 12) as JSON, newest first: the world's nudges applied, upkeep, the World card, arrivals, each seat's move with its alternatives, changes and courier, war checks and chronicle arcs.")]
    DebugCommandResult Turns(int count);
}
