using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DaggerfallRaceFlagsTests
{
    [Theory]
    [InlineData("Breton", 2, 0)]
    [InlineData("Redguard", 0, 0)]
    [InlineData("Nord", 16, 0)]
    [InlineData("DarkElf", 0, 0)]
    [InlineData("HighElf", 0, 1)]
    [InlineData("WoodElf", 0, 0)]
    [InlineData("Khajiit", 0, 0)]
    [InlineData("Argonian", 0, 0)]
    public void Actual_donor_race_constructor_flags_are_published(string race, int resistance, int immunity)
    {
        var flags = DaggerfallRaceFlagsReader.Read(Source())[race];
        Assert.Equal(resistance, flags.ResistanceFlags); Assert.Equal(immunity, flags.ImmunityFlags);
        Assert.Equal(0, flags.LowToleranceFlags); Assert.Equal(0, flags.CriticalWeaknessFlags);
    }

    [Theory]
    [InlineData("DFCareer.EffectFlags.Paralysis | DFCareer.EffectFlags.Poison", 5)]
    [InlineData("DFCareer.EffectFlags.Disease", 64)]
    public void Source_combinations_keep_the_donor_bit_order(string expression, int expected)
    {
        var source = Source().Replace("ImmunityFlags = DFCareer.EffectFlags.Paralysis;", $"ImmunityFlags = {expression};", StringComparison.Ordinal);
        Assert.Equal(expected, DaggerfallRaceFlagsReader.Read(source)["HighElf"].ImmunityFlags);
    }

    [Theory]
    [InlineData("ImmunityFlags = DFCareer.EffectFlags.Unknown;")]
    [InlineData("ImmunityFlags |= DFCareer.EffectFlags.Paralysis;")]
    [InlineData("ImmunityFlags = ComputeFlags();")]
    [InlineData("ImmunityFlags = DFCareer.EffectFlags.Paralysis; ImmunityFlags = DFCareer.EffectFlags.Poison;")]
    public void Unsupported_or_ambiguous_source_is_refused_instead_of_guessing(string replacement)
    {
        var source = Source().Replace("ImmunityFlags = DFCareer.EffectFlags.Paralysis;", replacement, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => DaggerfallRaceFlagsReader.Read(source));
    }

    private static string Source() => File.ReadAllText(TestData.Donor("Assets/Scripts/Game/Entities/RaceTemplate.cs"));
}
