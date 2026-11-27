using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>What a region's world conditions, the board's among them, do to its prices (#11055).</summary>
public sealed class ConditionPricesTests
{
    private const int Region = 17;
    private const int Elsewhere = 18;

    private static DaggerfallConditionPrices Prices(params DaggerfallWorldConditionToken[] tokens)
    {
        DaggerfallWorldConditions conditions = new();
        conditions.SeedAuthored(tokens);
        return new DaggerfallConditionPrices(conditions, () => 10, DaggerfallConditionPriceTuning.Default);
    }

    [Fact]
    public void Goods_rise_with_a_grain_shortage_and_a_crime_wave_and_fall_in_a_festival()
    {
        DaggerfallConditionPrices prices = Prices(
            new("grain", DaggerfallWorldConditionKind.GrainShortage, Region, Severity: 2),
            new("crime", DaggerfallWorldConditionKind.CrimeCrackdown, Region, SubjectTag: DaggerfallConditionPrices.CrimeWaveTag, Severity: 1),
            new("festival", DaggerfallWorldConditionKind.Decree, Region, SubjectTag: DaggerfallConditionPrices.FestivalTag, Severity: 2),
            // A plain crackdown and a recruitment decree leave prices alone.
            new("crackdown", DaggerfallWorldConditionKind.CrimeCrackdown, Region, SubjectTag: "crackdown", Severity: 3),
            new("recruiting", DaggerfallWorldConditionKind.Decree, Region, SubjectTag: "recruitment", Severity: 3));

        Assert.Equal(2 * DaggerfallConditionPriceTuning.Default.GrainShortagePerSeverity + DaggerfallConditionPriceTuning.Default.CrimeWavePerSeverity
            + 2 * DaggerfallConditionPriceTuning.Default.FestivalPerSeverity, prices.GoodsAdjustment(Region));
        Assert.Equal(0, prices.GoodsAdjustment(Elsewhere));
    }

    [Fact]
    public void A_witch_hunt_raises_magic_services_by_its_severity_and_nothing_else_does()
    {
        DaggerfallConditionPrices prices = Prices(
            new("hunt", DaggerfallWorldConditionKind.CrimeCrackdown, Region, SubjectTag: DaggerfallConditionPrices.WitchHuntTag, Severity: 2),
            new("crime", DaggerfallWorldConditionKind.CrimeCrackdown, Elsewhere, SubjectTag: DaggerfallConditionPrices.CrimeWaveTag, Severity: 3));

        Assert.Equal(150, prices.MagicService(Region, 100));
        Assert.Equal(2, prices.MagicService(Region, 1));
        Assert.Equal(100, prices.MagicService(Elsewhere, 100));
        Assert.Equal(0, prices.GoodsAdjustment(Region));
    }
}
