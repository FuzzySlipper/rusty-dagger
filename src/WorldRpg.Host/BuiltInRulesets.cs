using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Variants;

namespace WorldRpg.Host;

internal static class BuiltInRulesets
{
    internal static IGameRuleset Resolve(RulesetId id)
    {
        if (id == DaggerfallRuleset.Identity) return new DaggerfallRuleset(DaggerfallVariants.All);
        throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown built-in ruleset.");
    }

    /// <summary>Whether a bundle's ruleset is one this product compiles.</summary>
    internal static bool Composes(RulesetId id) => id == DaggerfallRuleset.Identity;

    /// <summary>The built-in ruleset whose installed containers the product opens: Daggerfall's data and music.</summary>
    internal static IInstalledContentRuleset InstalledContent { get; } = new DaggerfallRuleset(DaggerfallVariants.All);

    /// <summary>The manifests of every compiled rules variant a built-in bundle may select.</summary>
    internal static IReadOnlyList<RulesVariantManifest> VariantManifests { get; } =
        [.. DaggerfallVariants.All.Select(variant => variant.Manifest)];
}
