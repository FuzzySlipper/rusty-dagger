using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What a region's world conditions do to its prices. Goods move through the regional price factor (added to the
/// classic 1000-based adjustment): a grain shortage and a crime wave raise them, a festival lowers them. Magic services
/// (spells sold and made, enchanting, identifying) cost more under a witch hunt. Each scales with the condition's
/// severity. Board crime waves and witch hunts show as <see cref="DaggerfallWorldConditionKind.CrimeCrackdown"/> tokens,
/// and festivals as <see cref="DaggerfallWorldConditionKind.Decree"/> tokens, told apart by their subject tag.
/// </summary>
internal sealed class DaggerfallConditionPrices(IDaggerfallWorldConditionSource conditions, Func<int> currentDay)
{
    internal const int GrainShortagePerSeverity = 150;
    internal const int CrimeWavePerSeverity = 75;
    internal const int FestivalPerSeverity = -100;
    internal const int WitchHuntPercentPerSeverity = 25;

    internal const string CrimeWaveTag = "crime-wave";
    internal const string WitchHuntTag = "witch-hunt";
    internal const string FestivalTag = "festival";

    /// <summary>The change to <paramref name="region"/>'s regional price factor its conditions make.</summary>
    internal int GoodsAdjustment(int region)
    {
        IReadOnlyList<DaggerfallWorldConditionToken> active = conditions.ActiveForRegion(region, currentDay());
        return Severity(active, DaggerfallWorldConditionKind.GrainShortage, tag: null) * GrainShortagePerSeverity
            + Severity(active, DaggerfallWorldConditionKind.CrimeCrackdown, CrimeWaveTag) * CrimeWavePerSeverity
            + Severity(active, DaggerfallWorldConditionKind.Decree, FestivalTag) * FestivalPerSeverity;
    }

    /// <summary><paramref name="price"/> for a magic service in <paramref name="region"/>, raised by a witch hunt there.</summary>
    internal int MagicService(int region, int price)
    {
        int percent = Severity(conditions.ActiveForRegion(region, currentDay()), DaggerfallWorldConditionKind.CrimeCrackdown, WitchHuntTag)
            * WitchHuntPercentPerSeverity;
        return percent == 0 ? price : checked((int)(((long)price * (100 + percent) + 99) / 100));
    }

    private static int Severity(IReadOnlyList<DaggerfallWorldConditionToken> active, DaggerfallWorldConditionKind kind, string? tag) =>
        active.Where(token => token.Kind == kind && (tag is null || token.SubjectTag == tag)).Select(token => token.Severity).DefaultIfEmpty(0).Max();
}
