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
/// How much each moves them is <paramref name="tuning"/>.
/// </summary>
internal sealed class DaggerfallConditionPrices(
    IDaggerfallWorldConditionSource conditions,
    Func<int> currentDay,
    DaggerfallConditionPriceTuning tuning,
    Func<int, string>? regionName = null)
{

    internal const string CrimeWaveTag = "crime-wave";
    internal const string WitchHuntTag = "witch-hunt";
    internal const string FestivalTag = "festival";
    internal const string PriceSurgeTag = "price-surge";
    internal const string CorruptAdvisorTag = "corrupt-advisor";
    internal const string ProsperityTag = "prosperity";
    internal const string RecruitmentTag = "recruitment";

    /// <summary>Each active world condition's contribution to <paramref name="region"/>'s regional price factor, with its player-facing reason (#11158).</summary>
    internal IReadOnlyList<DaggerfallGoodsPriceFactor> DescribeGoodsAdjustments(int region)
    {
        IReadOnlyList<DaggerfallWorldConditionToken> active = conditions.ActiveForRegion(region, currentDay());
        string place = FormatPlace(region);
        List<DaggerfallGoodsPriceFactor> factors = [];
        Add(factors, active, DaggerfallWorldConditionKind.GrainShortage, null, tuning.GrainShortagePerSeverity, "grain shortage", place);
        Add(factors, active, DaggerfallWorldConditionKind.CrimeCrackdown, CrimeWaveTag, tuning.CrimeWavePerSeverity, "crime wave", place);
        Add(factors, active, DaggerfallWorldConditionKind.Decree, FestivalTag, tuning.FestivalPerSeverity, "festival", place);
        Add(factors, active, DaggerfallWorldConditionKind.Decree, PriceSurgeTag, tuning.PriceSurgePerSeverity, "price surge", place);
        Add(factors, active, DaggerfallWorldConditionKind.Decree, CorruptAdvisorTag, tuning.CorruptAdvisorPerSeverity, "corrupt advisor", place);
        Add(factors, active, DaggerfallWorldConditionKind.Decree, ProsperityTag, tuning.ProsperityPerSeverity, "prosperity", place);
        return factors;
    }

    /// <summary>The change to <paramref name="region"/>'s regional price factor its conditions make.</summary>
    internal int GoodsAdjustment(int region)
    {
        int total = 0;
        foreach (DaggerfallGoodsPriceFactor factor in DescribeGoodsAdjustments(region))
            total += factor.FactorDelta;
        return total;
    }

    /// <summary><paramref name="fee"/> to hire a companion or driver in <paramref name="region"/>, lowered where a crown is recruiting.</summary>
    internal ulong HireFee(int region, ulong fee)
    {
        int discount = Severity(conditions.ActiveForRegion(region, currentDay()), DaggerfallWorldConditionKind.Decree, RecruitmentTag)
            * tuning.RecruitmentHirePercentPerSeverity;
        return discount == 0 ? fee : checked(fee * (ulong)(100 - discount) / 100);
    }

    /// <summary>The active witch-hunt magic-service surcharge in <paramref name="region"/>, or null when none is in force (#11158).</summary>
    internal DaggerfallMagicPriceAdjustment? DescribeMagicService(int region)
    {
        int severity = Severity(conditions.ActiveForRegion(region, currentDay()), DaggerfallWorldConditionKind.CrimeCrackdown, WitchHuntTag);
        int percent = severity * tuning.WitchHuntPercentPerSeverity;
        if (severity <= 0 || percent == 0)
            return null;
        string place = FormatPlace(region);
        string reason = string.IsNullOrEmpty(place)
            ? $"witch hunt (severity {severity})"
            : $"witch hunt in {place} (severity {severity})";
        return new(reason, percent, severity);
    }

    /// <summary>
    /// Computes <paramref name="price"/> for a magic service in <paramref name="region"/> along with the exact
    /// line-by-line breakdown produced by that same calculation (#11158).
    /// </summary>
    internal (int Price, DaggerfallPriceBreakdown Breakdown) QuoteMagicService(int region, int price, string baseLabel = "Base service cost")
    {
        DaggerfallMagicPriceAdjustment? adjustment = DescribeMagicService(region);
        if (adjustment is null)
            return (price, new DaggerfallPriceBreakdown(baseLabel, price, [], price));
        int finalPrice = checked((int)(((long)price * (100 + adjustment.Percent) + 99) / 100));
        int delta = finalPrice - price;
        DaggerfallPriceLine[] steps = delta != 0
            ? [new DaggerfallPriceLine(adjustment.Reason, delta, DaggerfallPriceBreakdown.FormatSignedPercent(adjustment.Percent))]
            : [];
        return (finalPrice, new DaggerfallPriceBreakdown(baseLabel, price, steps, finalPrice));
    }

    /// <summary><paramref name="price"/> for a magic service in <paramref name="region"/>, raised by a witch hunt there.</summary>
    internal int MagicService(int region, int price) => QuoteMagicService(region, price).Price;

    private string FormatPlace(int region)
    {
        string? name = regionName?.Invoke(region);
        return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
    }

    private static void Add(
        List<DaggerfallGoodsPriceFactor> factors,
        IReadOnlyList<DaggerfallWorldConditionToken> active,
        DaggerfallWorldConditionKind kind,
        string? tag,
        int perSeverity,
        string label,
        string place)
    {
        int severity = Severity(active, kind, tag);
        int delta = severity * perSeverity;
        if (severity <= 0 || delta == 0)
            return;
        string reason = string.IsNullOrEmpty(place)
            ? $"{label} (severity {severity})"
            : $"{label} in {place} (severity {severity})";
        factors.Add(new(reason, delta, severity));
    }

    private static int Severity(IReadOnlyList<DaggerfallWorldConditionToken> active, DaggerfallWorldConditionKind kind, string? tag) =>
        active.Where(token => token.Kind == kind && (tag is null || token.SubjectTag == tag)).Select(token => token.Severity).DefaultIfEmpty(0).Max();
}
