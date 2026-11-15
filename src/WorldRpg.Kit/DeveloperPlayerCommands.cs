using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer overrides of the player's possessions and progress, for testing what needs an item, money or a level the
/// test would otherwise have to play for. Each goes through the ruleset's own owners (item creation, equipment,
/// currency, skills, level-up, vitals), so the result behaves and saves as if earned. These exist only as live-debug
/// commands; no game surface offers them. Each <see cref="DebugCommandAttribute"/> here is the command: the Engine's
/// catalogue is generated from it and calls the ruleset's owner that implements this.
/// </summary>
public interface IDeveloperPlayerCommands : IDebugCommandModule
{
    [DebugCommand("player.give", Description = "Developer: give the player COUNT of ITEM (quoted when it has spaces, such as \"Steel Dagger\"; player.items lists names), as if picked up.")]
    DebugCommandResult Give(string item, int count);

    [DebugCommand("player.hold", Description = "Developer: give the player one ITEM and wield it in the main hand, drawn, through the ordinary equip rules (what was there stays in the pack).")]
    DebugCommandResult Hold(string item);

    [DebugCommand("player.hold.magic", Description = "Developer: give the player a magic weapon and wield it in the main hand, drawn, through the ordinary equip rules.")]
    DebugCommandResult HoldMagic();

    [DebugCommand("player.equip", Description = "Developer: give the player one ITEM and wear it through the ordinary equip rules, a shield on the off arm (what was there stays in the pack).")]
    DebugCommandResult Equip(string item);

    [DebugCommand("player.gold", Description = "Developer: give the player AMOUNT gold.")]
    DebugCommandResult Money(long amount);

    [DebugCommand("player.skill", Description = "Developer: set the player's SKILL (player.items lists the ids) to VALUE.")]
    DebugCommandResult Skill(string skill, int value);

    [DebugCommand("player.levelup", Description = "Developer: raise the player's level skills until the next level is earned and open the ordinary level-up.")]
    DebugCommandResult LevelUp();

    [DebugCommand("player.heal", Description = "Developer: restore the player's vitals to their maximum.")]
    DebugCommandResult Heal();

    [DebugCommand("player.spell", Description = "Developer: teach the player SPELL, named or part of its name (quoted when it has spaces), as if learned.")]
    DebugCommandResult Spell(string spell);

    [DebugCommand("player.ride", Description = "Developer: put the player on MOUNT (horse, cart), giving them it if they have none, or back on foot.")]
    DebugCommandResult Ride(string mount);

    [DebugCommand("player.ride.state", Description = "Developer: the mount under the player, its speed in metres a second, its heading and how far the view looks past it.")]
    DebugCommandResult RideState();

    [DebugCommand("player.morph", Description = "Developer: change the player into beast FORM, cursing them with it if they aren't, or back with \"\".")]
    DebugCommandResult Morph(string form);

    [DebugCommand("player.items", Description = "Developer: the item names player.give and player.hold take that contain FILTER (\"\" for all), and the skill ids player.skill takes.")]
    DebugCommandResult Items(string filter);
}
