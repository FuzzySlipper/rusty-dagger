namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The ruleset's keyed draws: the one seed every keyed draw takes, the scope of the player's own rolls (blows, level-ups,
/// rewards; its name is older than the rolls it now holds) and the key of a roll between an attacker and a target in an
/// admitted step. Changing any of them changes every roll drawn with it.
/// </summary>
internal static class DaggerfallRandomKey
{
    internal const ulong Seed = 0;
    internal const string PlayerScope = "dagger.combat.v1";

    internal static string For(ulong generation, ulong step, long attacker, long target, int salt) => $"generation:{generation}:step:{step}:attacker:{attacker}:target:{target}:salt:{salt}";
}
