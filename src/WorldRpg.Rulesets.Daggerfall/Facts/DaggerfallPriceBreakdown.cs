using System.Globalization;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One signed gold step in a price calculation, produced by the same owner that sets the final total (#11158).</summary>
internal sealed record DaggerfallPriceLine(string Reason, int DeltaGold, string? Detail = null);

/// <summary>
/// Authoritative line-by-line price breakdown emitted alongside every quoted merchant or magic service price (#11158).
/// Invariant: <c>BaseGold + Steps.Sum(s =&gt; s.DeltaGold) == FinalGold</c>.
/// </summary>
internal sealed record DaggerfallPriceBreakdown(
    string BaseLabel,
    int BaseGold,
    IReadOnlyList<DaggerfallPriceLine> Steps,
    int FinalGold)
{
    private const char Minus = '\u2212';

    internal string[] FormatLines()
    {
        List<string> lines = [FormatUnsigned(BaseLabel, BaseGold)];
        foreach (DaggerfallPriceLine step in Steps)
        {
            if (step.DeltaGold == 0 && string.IsNullOrWhiteSpace(step.Detail))
                continue;
            string label = !string.IsNullOrWhiteSpace(step.Detail)
                ? $"{step.Detail} {step.Reason}"
                : step.Reason;
            lines.Add(FormatSigned(label, step.DeltaGold));
        }
        lines.Add(FormatUnsigned("Total", FinalGold));
        return [.. lines];
    }

    internal string FormatTooltip() => string.Join("\n", FormatLines());

    internal static string FormatSignedPercentFromThousandths(int deltaThousandths)
    {
        if (deltaThousandths == 0)
            return "0%";
        string sign = deltaThousandths > 0 ? "+" : Minus.ToString();
        int abs = Math.Abs(deltaThousandths);
        return abs % 10 == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{sign}{abs / 10}%")
            : string.Create(CultureInfo.InvariantCulture, $"{sign}{abs / 10}.{abs % 10}%");
    }

    internal static string FormatSignedPercent(int percent)
    {
        if (percent == 0)
            return "0%";
        string sign = percent > 0 ? "+" : Minus.ToString();
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{Math.Abs(percent)}%");
    }

    private static string FormatUnsigned(string label, int gold) =>
        string.Create(CultureInfo.InvariantCulture, $"{label}: {gold:N0} gold");

    private static string FormatSigned(string label, int deltaGold)
    {
        if (deltaGold == 0)
            return string.Create(CultureInfo.InvariantCulture, $"{label}: 0 gold");
        string sign = deltaGold > 0 ? "+" : Minus.ToString();
        return string.Create(CultureInfo.InvariantCulture, $"{label}: {sign}{Math.Abs(deltaGold):N0} gold");
    }
}

/// <summary>One active regional world condition's goods-price factor contribution (1000-based) with player-facing reason (#11158).</summary>
internal sealed record DaggerfallGoodsPriceFactor(string Reason, int FactorDelta, int Severity);

/// <summary>One active regional world condition's magic-service surcharge percent with player-facing reason (#11158).</summary>
internal sealed record DaggerfallMagicPriceAdjustment(string Reason, int Percent, int Severity);

/// <summary>One office-allegiance reaction shift contribution with player-facing label (#11158).</summary>
internal sealed record DaggerfallReactionShiftCause(string Label, int Delta);

/// <summary>Structured trade reaction breakdown splitting base standing/disposition from board office allegiance shifts (#11158).</summary>
internal sealed record DaggerfallTradeReactionBreakdown(
    int Total,
    int BaseSocialReaction,
    IReadOnlyList<DaggerfallReactionShiftCause> OfficeShifts)
{
    internal static DaggerfallTradeReactionBreakdown Plain(int reaction) => new(reaction, reaction, []);
}
