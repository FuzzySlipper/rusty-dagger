using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Daggerfall.Source.Tool.Donor;
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
    public void Our_race_table_carries_the_donor_constructor_flags(string race, int resistance, int immunity)
    {
        DaggerfallRaceTable table = TakenTable.Read<DaggerfallRaceTable>(File.ReadAllBytes(TestData.Input("races.json")), "races.json");
        DaggerfallRaceFlags flags = table.Races[race];
        Assert.Equal(resistance, flags.ResistanceFlags); Assert.Equal(immunity, flags.ImmunityFlags);
        Assert.Equal(0, flags.LowToleranceFlags); Assert.Equal(0, flags.CriticalWeaknessFlags);
        Assert.Equal("daggerfall-unity/Assets/Scripts/Game/Entities/RaceTemplate.cs", table.Source.Path);
    }

    [Theory]
    [InlineData("DFCareer.EffectFlags.Paralysis | DFCareer.EffectFlags.Poison", 5)]
    [InlineData("DFCareer.EffectFlags.Disease", 64)]
    public void Source_combinations_keep_the_donor_bit_order(string expression, int expected)
    {
        var source = Source().Replace("ImmunityFlags = DFCareer.EffectFlags.Paralysis;", $"ImmunityFlags = {expression};", StringComparison.Ordinal);
        Assert.Equal(expected, DonorRaceTable.Read(source)["HighElf"].ImmunityFlags);
    }

    [Theory]
    [InlineData("ImmunityFlags = DFCareer.EffectFlags.Unknown;")]
    [InlineData("ImmunityFlags |= DFCareer.EffectFlags.Paralysis;")]
    [InlineData("ImmunityFlags = ComputeFlags();")]
    [InlineData("ImmunityFlags = DFCareer.EffectFlags.Paralysis; ImmunityFlags = DFCareer.EffectFlags.Poison;")]
    public void Unsupported_or_ambiguous_source_is_refused_instead_of_guessing(string replacement)
    {
        var source = Source().Replace("ImmunityFlags = DFCareer.EffectFlags.Paralysis;", replacement, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => DonorRaceTable.Read(source));
    }

    /// <summary>A donor-shaped excerpt of <c>RaceTemplate.cs</c>: the default fields and two race constructors.</summary>
    private static string Source() => """
        public abstract class RaceTemplate
        {
            public int ID;
            public DFCareer.EffectFlags ResistanceFlags;
            public DFCareer.EffectFlags ImmunityFlags;
            public DFCareer.EffectFlags LowToleranceFlags;
            public DFCareer.EffectFlags CriticalWeaknessFlags;
        }

        public class Breton : RaceTemplate
        {
            public Breton()
            {
                ID = (int)Races.Breton;
                ResistanceFlags = DFCareer.EffectFlags.Magic;
            }
        }

        public class HighElf : RaceTemplate
        {
            public HighElf()
            {
                ID = (int)Races.HighElf;
                ImmunityFlags = DFCareer.EffectFlags.Paralysis;
            }
        }
        """;
}
