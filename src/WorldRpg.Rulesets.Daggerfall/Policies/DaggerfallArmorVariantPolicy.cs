namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>
/// ItemBuilder's armor texture-variant rules. Each armor template's player records hold the leather,
/// plate and chain looks side by side, so the variant an item carries must stay inside its material's
/// family or the paper doll draws iron as leather.
/// </summary>
internal static class DaggerfallArmorVariantPolicy
{
    private const int Cuirass = 102, Gauntlets = 103, Greaves = 104, LeftPauldron = 105, RightPauldron = 106, Helm = 107, Boots = 108;

    /// <summary>
    /// RandomizeArmorVariant: draws only where a material family has more than one look, then clamps.
    /// <paramref name="draw"/> returns an inclusive value in the requested range.
    /// </summary>
    internal static int Random(int template, string material, int variants, Func<int, int, int> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        bool plate = IsPlate(material);
        int variant = template switch
        {
            Cuirass when plate => draw(1, 3),
            Greaves when material == "leather" => draw(0, 1),
            Greaves when plate => draw(2, 5),
            LeftPauldron or RightPauldron when plate => draw(1, 3),
            Boots when plate => draw(1, 2),
            Helm when variants > 0 => draw(0, variants - 1),
            _ => 0,
        };
        return Clamp(template, material, variants, variant);
    }

    /// <summary>
    /// SetVariant: keeps a requested variant inside its material family. A variant outside the template's
    /// range leaves the item on variant 0, which the donor never moves either.
    /// </summary>
    internal static int Clamp(int template, string material, int variants, int variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(material);
        if (variant < 0 || variant >= variants) return 0;
        bool leather = material == "leather", chain = material == "chain";
        return template switch
        {
            Cuirass or LeftPauldron or RightPauldron => leather ? 0 : chain ? 4 : Math.Clamp(variant, 1, 3),
            Greaves => leather ? Math.Clamp(variant, 0, 1) : chain ? 6 : Math.Clamp(variant, 2, 5),
            Gauntlets => leather ? 0 : 1,
            Boots => leather ? 0 : Math.Clamp(variant, 1, 2),
            _ => variant,
        };
    }

    private static bool IsPlate(string material) => DaggerfallItemMaterialPolicy.IsWeaponMaterial(material);
}
