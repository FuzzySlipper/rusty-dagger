using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall;

namespace WorldRpg.Host;

internal static class BuiltInRulesets
{
    internal static IGameRuleset Resolve(RulesetId id)
    {
        if (id == DaggerfallRuleset.Identity) return new DaggerfallRuleset();
        throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown built-in ruleset.");
    }
}
