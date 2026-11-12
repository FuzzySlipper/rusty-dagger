using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>What a blow's force does against a body's weight.</summary>
public sealed class DaggerfallStrikeTuningTests
{
    private static readonly DaggerfallStrikeTuning Tuning = DaggerfallStrikeTuning.Default;

    [Theory]
    [InlineData("fastA", 50, null, "Flinch")] // A person shrugs off a fast attack.
    [InlineData("fastA", 71, null, "Flinch")] // However strong the Warrior.
    [InlineData("powerOut", 50, null, "Stagger")]
    [InlineData("kick", 50, null, "Knockdown")]
    [InlineData("kick", 45, null, "Knockdown")] // A weak Mage still floors a person.
    [InlineData("kick", 71, 600, "Stagger")] // An orc takes a stagger.
    [InlineData("kick", 71, 1000, "Flinch")] // A bear hardly notices.
    [InlineData("fastA", 71, 2, "Stagger")] // A fast attack never knocks down, even a rat.
    [InlineData("powerOut", 71, 2, "Knockdown")]
    public void Force_against_weight_sets_the_reaction(string attack, int strength, int? weight, string level) =>
        Assert.Equal(level, Tuning.React(attack, strength, weight).Level.ToString());

    [Fact]
    public void A_flinch_holds_nothing_and_only_a_kick_shoves_through_one()
    {
        Assert.Equal(default, Tuning.React("fastA", 50, null));
        DaggerfallStrikeReaction shove = Tuning.React("kick", 71, 1000);
        Assert.Equal(0d, shove.HoldSeconds);
        Assert.True(shove.Push > 1d);
    }

    [Fact]
    public void A_weightless_body_is_untouched_and_nothing_flies_past_the_maximum()
    {
        Assert.Equal(default, Tuning.React("kick", 100, 0));
        Assert.Equal(Tuning.MaximumPush, Tuning.React("kick", 100, 2).Push);
    }

    [Theory]
    [InlineData("kick", true)]
    [InlineData("powerOut", true)]
    [InlineData("fastA", false)]
    [InlineData("shot", false)]
    public void Only_kicks_and_power_attacks_are_heavy(string attack, bool heavy) => Assert.Equal(heavy, DaggerfallStrikeTuning.Heavy(attack));
}
