using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What a region's world conditions do to its prices. Goods move through the regional price factor (added to the
/// classic 1000-based adjustment): a grain shortage (a cut-off region's too), a crime wave, a price surge, a corrupt
/// advisor skimming the court's coffers and a prosperous region's demand raise them, and a festival lowers them. Magic
/// services (spells sold and made, enchanting, identifying) cost more under a witch hunt, and hirelings cost less where
/// a crown is recruiting. Each scales with the condition's severity. Board crime waves and witch hunts show as
/// <see cref="DaggerfallWorldConditionKind.CrimeCrackdown"/> tokens, and festivals, recruitment, prosperity, price surges
/// and court conditions as <see cref="DaggerfallWorldConditionKind.Decree"/> tokens, told apart by their subject tag.
/// </summary>
internal sealed class DaggerfallConditionPrices(IDaggerfallWorldConditionSource conditions, Func<int> currentDay)
{
    internal const int GrainShortagePerSeverity = 150;
    internal const int CrimeWavePerSeverity = 75;
    internal const int FestivalPerSeverity = -100;
    internal const int PriceSurgePerSeverity = 50;
    internal const int CorruptAdvisorPerSeverity = 50;
    internal const int ProsperityPerSeverity = 50;
    internal const int WitchHuntPercentPerSeverity = 25;
    internal const int RecruitmentHirePercentPerSeverity = -25;

    internal const string CrimeWaveTag = "crime-wave";
    internal const string WitchHuntTag = "witch-hunt";
    internal const string FestivalTag = "festival";
    internal const string PriceSurgeTag = "price-surge";
    internal const string CorruptAdvisorTag = "corrupt-advisor";
    internal const string ProsperityTag = "prosperity";
    internal const string RecruitmentTag = "recruitment";

    /// <summary>The change to <paramref name="region"/>'s regional price factor its conditions make.</summary>
    internal int GoodsAdjustment(int region)
    {
        IReadOnlyList<DaggerfallWorldConditionToken> active = conditions.ActiveForRegion(region, currentDay());
        return Severity(active, DaggerfallWorldConditionKind.GrainShortage, tag: null) * GrainShortagePerSeverity
            + Severity(active, DaggerfallWorldConditionKind.CrimeCrackdown, CrimeWaveTag) * CrimeWavePerSeverity
            + Severity(active, DaggerfallWorldConditionKind.Decree, FestivalTag) * FestivalPerSeverity
            + Severity(active, DaggerfallWorldConditionKind.Decree, PriceSurgeTag) * PriceSurgePerSeverity
            + Severity(active, DaggerfallWorldConditionKind.Decree, CorruptAdvisorTag) * CorruptAdvisorPerSeverity
            + Severity(active, DaggerfallWorldConditionKind.Decree, ProsperityTag) * ProsperityPerSeverity;
    }

    /// <summary><paramref name="fee"/> to hire a companion or driver in <paramref name="region"/>, lowered where a crown is recruiting.</summary>
    internal ulong HireFee(int region, ulong fee)
    {
        int percent = Severity(conditions.ActiveForRegion(region, currentDay()), DaggerfallWorldConditionKind.Decree, RecruitmentTag)
            * RecruitmentHirePercentPerSeverity;
        return percent == 0 ? fee : checked(fee * (ulong)(100 + percent) / 100);
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
