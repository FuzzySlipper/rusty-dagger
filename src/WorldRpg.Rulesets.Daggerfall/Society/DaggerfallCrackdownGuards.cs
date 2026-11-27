using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How many extra guards a crime crackdown calls out in a region's towns (Patch, #11120: "crimecrackdown should increase
/// the number of guard spawns in affected cities"): <see cref="DaggerfallLawTuning.CrackdownGuardsPerSeverity"/> for
/// each point of the strongest crackdown's severity, from each town's reserve watch. A crime wave or a witch hunt is a
/// crackdown token too, but it calls out no one.
/// </summary>
internal sealed class DaggerfallCrackdownGuards(IDaggerfallWorldConditionSource conditions, Func<int> currentDay, DaggerfallLawTuning law)
{
    internal const string CrackdownTag = "crackdown";

    /// <summary>The extra guards walking in <paramref name="region"/>'s towns now.</summary>
    internal int For(int region) => For(region, site: null);

    /// <summary>The extra guards walking in <paramref name="site"/> (or <paramref name="region"/> when null), capped by the settlement's watch tier.</summary>
    internal int For(int region, DaggerfallSiteRecord? site)
    {
        int severity = conditions.ActiveForRegion(region, currentDay())
            .Where(token => token.Kind == DaggerfallWorldConditionKind.CrimeCrackdown && token.SubjectTag is null or CrackdownTag)
            .Select(token => token.Severity).DefaultIfEmpty(0).Max();
        int cap = site is not null
            ? law.GuardTiers.ResolveProfile(site).MaxCrackdownReserves
            : DaggerfallExteriorPopulation.CrackdownGuardSlots;
        return Math.Min(cap, severity * law.CrackdownGuardsPerSeverity);
    }

    /// <summary>Whether <paramref name="placement"/>, in <paramref name="region"/> (and optional <paramref name="site"/>), is a reserve guard no crackdown calls out now.</summary>
    internal bool HeldBack(int region, DaggerfallPopulationPlacement placement, DaggerfallSiteRecord? site = null) =>
        placement.CrackdownGuard > 0 && placement.CrackdownGuard > For(region, site);
}
