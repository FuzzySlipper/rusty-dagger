namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>FORM-10 material bands, adapted from the donor's runtime random calls.</summary>
internal static class DaggerfallItemMaterialPolicy
{
    private static readonly int[] Bands = [64, 128, 10, 21, 13, 8, 5, 3, 2, 5];

    internal static string RandomMaterial(int level, int random0To255)
    {
        if (random0To255 is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(random0To255));
        int modifier = level - 10;
        modifier *= modifier >= 0 ? 2 : 4;
        int remaining = Math.Clamp(modifier + random0To255, 0, 256);
        int material = 0;
        while (material < Bands.Length - 1 && Bands[material] < remaining)
            remaining -= Bands[material++];
        return DaggerfallItemMaterials.WeaponMaterials[material];
    }

    /// <summary>FORM-10 armor selection: 1–69 leather, 70–89 chain, 90–100 plate material.</summary>
    internal static string RandomArmorMaterial(int level, int roll1To100, int random0To255)
    {
        if (roll1To100 is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(roll1To100));
        return roll1To100 < 70 ? "leather" : roll1To100 < 90 ? "chain" : RandomMaterial(level, random0To255);
    }
}
