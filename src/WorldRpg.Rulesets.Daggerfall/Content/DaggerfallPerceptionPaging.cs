namespace WorldRpg.Rulesets.Daggerfall.Modules;

/// <summary>
/// One complete first page of an Engine perception query, for the ruleset's bounded single-query decisions (an enemy's
/// pursuit, a corpse in sight): the first pair cursor and a page that holds every pair those queries ask for.
/// </summary>
internal static class DaggerfallPerceptionPaging
{
    internal const uint FirstPairCursor = 0;
    internal const uint CompleteQueryPageSize = 64;
}
