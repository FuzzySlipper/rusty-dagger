using Rusty.Engine.Debugging;

namespace WorldRpg.Kit;

/// <summary>
/// Developer overrides of the player's possessions and progress, for testing what needs an item, money or a level the
/// test would otherwise have to play for. Each goes through the ruleset's own owners (item creation, equipment,
/// currency, skills, level-up, vitals), so the result behaves and saves as if earned. These exist only as live-debug
/// commands; no game surface offers them.
/// </summary>
public interface IDeveloperPlayerSession
{
    /// <summary>Gives the player <paramref name="count"/> of an item, named as the ruleset names it, as if picked up.</summary>
    DebugCommandResult Give(string item, int count);

    /// <summary>Gives the player one of an item and wields it in the main hand, drawn, through the ordinary equip rules.</summary>
    DebugCommandResult Hold(string item);

    /// <summary>Gives the player an amount of money.</summary>
    DebugCommandResult Money(long amount);

    /// <summary>Sets one of the player's skills to a value, within the ruleset's range.</summary>
    DebugCommandResult Skill(string skill, int value);

    /// <summary>Advances the player's skills until a level is earned and starts the ordinary level-up.</summary>
    DebugCommandResult LevelUp();

    /// <summary>Restores the player's vitals to their maximum.</summary>
    DebugCommandResult Heal();

    /// <summary>The items <see cref="Give"/> knows whose names contain <paramref name="filter"/>, and the skills <see cref="Skill"/> sets.</summary>
    DebugCommandResult Items(string filter);
}

/// <summary>The live-debug commands over <see cref="IDeveloperPlayerSession"/>; the delegate follows session replacement.</summary>
public sealed class DeveloperPlayerDebugModule(Func<IDeveloperPlayerSession> current) : IDebugCommandModule
{
    [DebugCommand("player.give", Description = "Developer: give the player COUNT of ITEM (quoted when it has spaces, such as \"Steel Dagger\"; player.items lists names), as if picked up.")]
    public DebugCommandResult Give(string item, int count) => current().Give(item, count);

    [DebugCommand("player.hold", Description = "Developer: give the player one ITEM and wield it in the main hand, drawn, through the ordinary equip rules (what was there stays in the pack).")]
    public DebugCommandResult Hold(string item) => current().Hold(item);

    [DebugCommand("player.gold", Description = "Developer: give the player AMOUNT gold.")]
    public DebugCommandResult Money(long amount) => current().Money(amount);

    [DebugCommand("player.skill", Description = "Developer: set the player's SKILL (player.items lists the ids) to VALUE.")]
    public DebugCommandResult Skill(string skill, int value) => current().Skill(skill, value);

    [DebugCommand("player.levelup", Description = "Developer: raise the player's level skills until the next level is earned and open the ordinary level-up.")]
    public DebugCommandResult LevelUp() => current().LevelUp();

    [DebugCommand("player.heal", Description = "Developer: restore the player's vitals to their maximum.")]
    public DebugCommandResult Heal() => current().Heal();

    [DebugCommand("player.items", Description = "Developer: the item names player.give and player.hold take that contain FILTER (\"\" for all), and the skill ids player.skill takes.")]
    public DebugCommandResult Items(string filter) => current().Items(filter);
}
