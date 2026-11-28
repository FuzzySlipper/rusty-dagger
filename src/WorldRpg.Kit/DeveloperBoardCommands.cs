using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer runtime inspection commands for the political board the session runs: the table at a glance, one seat in
/// depth, the recent turns as they were played, and every seat's moves as one log.
/// </summary>
public interface IDeveloperBoardCommands : IDebugCommandModule
{
    [DebugCommand("board.state", Description = "Developer: inspect the political board as JSON: turn and next Sunday, every seat's holder, prestige, resources, plan and reserves, wars, truces, feuds, trade agreements, regions with allegiance, tracks, supply, offices and conditions, the arbiter's balance, pending moves, queued nudges and open arcs.")]
    DebugCommandResult State();

    [DebugCommand("board.seat", Description = "Developer: inspect one political board seat (its id, such as daggerfall or mages-guild) as JSON: holder and the persona playing it, agenda, plan, grudges and help given and received, relations, hand and decks, court conditions, boons, pieces and footholds, and the world's holder of its office.")]
    DebugCommandResult Seat(string seat);

    [DebugCommand("board.turns", Description = "Developer: inspect the board's last few turns this session (up to 12) as JSON, newest first: the world's nudges applied, upkeep, the World card, arrivals, each seat's move with its alternatives, changes and courier, war checks and chronicle arcs.")]
    DebugCommandResult Turns(int count);

    [DebugCommand("board.moves", Description = "Developer: the board's moves this session as a chronological log in JSON, newest turn first (up to 12 turns): each seat's move with its card, targets and visibility, and how it came out (landed, under way with its courier, delayed, delivered early, intercepted, arrived or failed); the world's nudges, the slow moves arriving and the chronicle arcs at the turn they open, peak or settle.")]
    DebugCommandResult Moves();

    [DebugCommand("board.allegiance", Description = "Developer: give a political board region (its id, such as glenpoint) to a seat (such as wayrest), or to no one (none), at once, as an annexation would, so the world's liege, heraldry, banners and the map's Realms view follow it. A capital keeps its crown.")]
    DebugCommandResult Allegiance(string region, string seat);

    [DebugCommand("board.send-convoy", Description = "Developer: send this week's ration convoys now, as Sunday sends them, for every grain agreement in force; with none in force, first strike coin for grain between the first two seats that can trade it. Lists each convoy's band, giver and towns as JSON.")]
    DebugCommandResult SendConvoy();

    [DebugCommand("board.ambush-convoy", Description = "Developer: spring the road's ambush now on the convoy the party rides with as its hired guard on the open travel map, as a raid rolled on its road would: its raiders, drawn from the tables where it stands, clash with it beside the party.")]
    DebugCommandResult AmbushConvoy();
}
