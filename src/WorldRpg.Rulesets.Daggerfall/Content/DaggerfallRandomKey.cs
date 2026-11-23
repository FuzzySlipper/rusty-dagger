namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The ruleset's keyed draws: each draw's own seed, the scope of the player's own rolls (blows, level-ups, rewards;
/// its name is older than the rolls it now holds) and the key of a roll between an attacker and a target in an
/// admitted step. Changing any of them changes every roll drawn with it.
/// </summary>
/// <remarks>
/// Which draws differ between games: every keyed draw a session makes goes through its <see cref="DaggerfallGameRandom"/>,
/// which mixes the game's seed into the draw's own, so encounters, bands, foes, blows, loot, locks, afflictions, greetings,
/// weather, quests, prices and creation rolls each differ from one game to the next and replay alike within a game. The
/// world itself is the same in every game: the terrain's scoped streams (keyed by cell) and the classic LCG behind building
/// names pass through unmixed. A new keyed draw needs nothing more; a draw that must stay alike in every game is a scoped
/// stream or the LCG, never a keyed draw.
/// </remarks>
internal static class DaggerfallRandomKey
{
    internal const ulong Seed = 0;
    internal const string PlayerScope = "dagger.combat.v1";

    internal static string For(ulong generation, ulong step, long attacker, long target, int salt) => $"generation:{generation}:step:{step}:attacker:{attacker}:target:{target}:salt:{salt}";
}
