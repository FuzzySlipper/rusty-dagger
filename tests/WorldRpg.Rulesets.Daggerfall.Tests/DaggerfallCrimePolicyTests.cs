using WorldRpg.Rulesets.Daggerfall.Crime;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCrimePolicyTests
{
    [Fact]
    public void Pickpocket_chance_applies_enemy_level_adjustment_and_classic_bounds()
    {
        Assert.Equal(42, DaggerfallCrimePolicy.CalculatePickpocketingChance(42, playerLevel: 10));
        Assert.Equal(22, DaggerfallCrimePolicy.CalculatePickpocketingChance(42, playerLevel: 10, targetLevel: 14));
        Assert.Equal(62, DaggerfallCrimePolicy.CalculatePickpocketingChance(42, playerLevel: 10, targetLevel: 6));
        Assert.Equal(5, DaggerfallCrimePolicy.CalculatePickpocketingChance(0, playerLevel: 1, targetLevel: 100));
        Assert.Equal(95, DaggerfallCrimePolicy.CalculatePickpocketingChance(100, playerLevel: 100, targetLevel: 0));
    }

    [Fact]
    public void Shoplifting_chance_uses_donor_integer_basket_weight_and_clamps()
    {
        Assert.Equal(76, DaggerfallCrimePolicy.CalculateShopliftingChance(50, shopQuality: 20, weightAndNumItems: 6));
        // DFUnity computes (int)GetWeight() + Count; explicit double-to-int conversion truncates toward zero.
        Assert.Equal(75, DaggerfallCrimePolicy.CalculateShopliftingChance(50, shopQuality: 20, basketWeight: 2.9, basketItemCount: 3));
        Assert.Equal(5, DaggerfallCrimePolicy.CalculateShopliftingChance(100, shopQuality: 0, weightAndNumItems: 0));
        Assert.Equal(95, DaggerfallCrimePolicy.CalculateShopliftingChance(0, shopQuality: 100, weightAndNumItems: 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallCrimePolicy.CalculateShopliftingChance(50, 0, double.NaN, 1));
    }

    [Fact]
    public void Regional_legal_reputation_losses_match_the_donor_crime_table()
    {
        Assert.Equal(10, DaggerfallCrimePolicy.RegionalReputationLoss(DaggerfallCrimeKind.AttemptedBreakingAndEntering));
        Assert.Equal(5, DaggerfallCrimePolicy.RegionalReputationLoss(DaggerfallCrimeKind.Trespassing));
        Assert.Equal(10, DaggerfallCrimePolicy.RegionalReputationLoss(DaggerfallCrimeKind.BreakingAndEntering));
        Assert.Equal(8, DaggerfallCrimePolicy.RegionalReputationLoss(DaggerfallCrimeKind.Assault));
        Assert.Equal(20, DaggerfallCrimePolicy.RegionalReputationLoss(DaggerfallCrimeKind.Murder));
        Assert.Equal(0, DaggerfallCrimePolicy.PeopleFactionReputationLoss(DaggerfallCrimeKind.Arson));
        Assert.Equal(75, DaggerfallCrimePolicy.RegionalReputationLoss(DaggerfallCrimeKind.HighTreason));
        Assert.Equal(2, DaggerfallCrimePolicy.RegionalReputationLoss(DaggerfallCrimeKind.Pickpocketing));
        Assert.Equal(8, DaggerfallCrimePolicy.RegionalReputationLoss(DaggerfallCrimeKind.Theft));
        Assert.Equal(36, DaggerfallCrimePolicy.RegionalReputationLoss(DaggerfallCrimeKind.Treason));
    }
}
