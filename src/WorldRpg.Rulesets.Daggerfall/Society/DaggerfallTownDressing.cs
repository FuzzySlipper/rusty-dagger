using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Which street dressing a region's towns put out now (#11169), from its world conditions: a festival or prosperity
/// decree, unrest, a witch hunt. Each kind shows at the strongest severity of its conditions there, and goes when they
/// expire; the art pack says what each kind puts out, and the town where.
/// </summary>
internal sealed class DaggerfallTownDressing(IDaggerfallWorldConditionSource conditions, Func<int> currentDay)
{
    /// <summary>The board's unrest shows as border tension with this tag.</summary>
    internal const string UnrestTag = "unrest";

    /// <summary>The dressing <paramref name="region"/>'s towns put out now, each kind once, in kind order.</summary>
    internal IReadOnlyList<(DaggerfallTownDressingKind Kind, int Severity)> For(int region) =>
        [.. conditions.ActiveForRegion(region, currentDay())
            .Select(token => (Kind: Of(token), token.Severity))
            .Where(entry => entry.Kind is not null)
            .GroupBy(entry => entry.Kind!.Value)
            .OrderBy(group => group.Key)
            .Select(group => (group.Key, group.Max(entry => entry.Severity)))];

    /// <summary>The dressing a condition puts out, if any.</summary>
    internal static DaggerfallTownDressingKind? Of(DaggerfallWorldConditionToken token) => (token.Kind, token.SubjectTag) switch
    {
        (DaggerfallWorldConditionKind.Decree, DaggerfallConditionPrices.FestivalTag) => DaggerfallTownDressingKind.Festival,
        (DaggerfallWorldConditionKind.Decree, DaggerfallConditionPrices.ProsperityTag) => DaggerfallTownDressingKind.Prosperity,
        (DaggerfallWorldConditionKind.BorderTension, UnrestTag) => DaggerfallTownDressingKind.Unrest,
        (DaggerfallWorldConditionKind.CrimeCrackdown, DaggerfallConditionPrices.WitchHuntTag) => DaggerfallTownDressingKind.WitchHunt,
        _ => null,
    };
}
