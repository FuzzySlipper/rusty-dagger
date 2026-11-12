using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallItemMaterialPolicyTests
{
    [Theory]
    [InlineData(1, 0, "iron")]
    [InlineData(10, 65, "steel")]
    [InlineData(20, 255, "daedric")]
    [InlineData(-100, 255, "iron")]
    public void Random_material_uses_the_donor_bands_after_its_level_modifier(int level, int roll, string expected) =>
        Assert.Equal(expected, DaggerfallItemMaterialPolicy.RandomMaterial(level, roll));

    [Theory]
    [InlineData(69, "leather")]
    [InlineData(70, "chain")]
    [InlineData(89, "chain")]
    [InlineData(90, "daedric")]
    public void Random_armor_material_preserves_its_leather_chain_plate_bands(int roll, string expected) =>
        Assert.Equal(expected, DaggerfallItemMaterialPolicy.RandomArmorMaterial(20, roll, 255));

    [Fact]
    public void Material_rolls_reject_out_of_range_engine_results()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallItemMaterialPolicy.RandomMaterial(1, 256));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallItemMaterialPolicy.RandomArmorMaterial(1, 0, 0));
    }
}
