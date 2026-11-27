using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How much each point of a world condition's severity moves prices (<see cref="DaggerfallConditionPrices"/>): the
/// regional price factor's change for a grain shortage, a crime wave, a festival, a price surge, a corrupt advisor and
/// prosperity; the percent a witch hunt adds to magic services; and the percent recruitment takes off a hireling's fee.
/// </summary>
internal sealed record DaggerfallConditionPriceTuning(
    int GrainShortagePerSeverity,
    int CrimeWavePerSeverity,
    int FestivalPerSeverity,
    int PriceSurgePerSeverity,
    int CorruptAdvisorPerSeverity,
    int ProsperityPerSeverity,
    int WitchHuntPercentPerSeverity,
    int RecruitmentHirePercentPerSeverity)
{
    internal static DaggerfallConditionPriceTuning Default { get; } = new(150, 75, -100, 50, 50, 50, 25, 25);

    /// <summary>A factor change a regional price adjustment can carry at full severity.</summary>
    private const int FactorLimit = 1000;

    internal DaggerfallConditionPriceTuning Validate()
    {
        int max = DaggerfallWorldConditionToken.MaxSeverity;
        bool factors = new[] { GrainShortagePerSeverity, CrimeWavePerSeverity, FestivalPerSeverity, PriceSurgePerSeverity, CorruptAdvisorPerSeverity, ProsperityPerSeverity }
            .All(perSeverity => Math.Abs(perSeverity) * max <= FactorLimit);
        return factors && WitchHuntPercentPerSeverity is >= 0 and <= 100
            && RecruitmentHirePercentPerSeverity >= 0 && RecruitmentHirePercentPerSeverity * max < 100
            ? this
            : throw new ArgumentException("Condition price tuning requires factor changes of at most 1000 at full severity, a witch hunt surcharge of 0 to 100 percent, and a recruitment discount below 100 percent at full severity.");
    }

    internal static DaggerfallConditionPriceTuning Read(JsonElement section) => new DaggerfallConditionPriceTuning(
        section.GetProperty("grainShortagePerSeverity").GetInt32(),
        section.GetProperty("crimeWavePerSeverity").GetInt32(),
        section.GetProperty("festivalPerSeverity").GetInt32(),
        section.GetProperty("priceSurgePerSeverity").GetInt32(),
        section.GetProperty("corruptAdvisorPerSeverity").GetInt32(),
        section.GetProperty("prosperityPerSeverity").GetInt32(),
        section.GetProperty("witchHuntPercentPerSeverity").GetInt32(),
        section.GetProperty("recruitmentHirePercentPerSeverity").GetInt32()).Validate();
}
