namespace WorldRpg.Rulesets.Daggerfall.Variants;

/// <summary>
/// Every compiled Daggerfall rules variant, in a fixed order. A game bundle selects them by id; the
/// Host composes this catalog into the Daggerfall ruleset at its explicit built-in seam.
/// </summary>
public static class DaggerfallVariants
{
    public static IReadOnlyList<DaggerfallRulesVariant> All { get; } = [];
}
