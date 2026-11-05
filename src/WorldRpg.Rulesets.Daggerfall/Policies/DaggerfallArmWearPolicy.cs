namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>What the player's arms are dressed in, which the 3D first-person art draws them by, lightest first.</summary>
internal enum DaggerfallArmWear
{
    Cloth,
    Leather,
    Mail,
}

/// <summary>
/// Reads what the arms wear from the armour on them: the gauntlets and pauldrons. The heaviest piece shows, and chain
/// and plate are both mail, as neither has arms of its own.
/// </summary>
internal static class DaggerfallArmWearPolicy
{
    /// <summary>The equipment slots whose armour shows on the arms.</summary>
    internal static readonly string[] Slots = ["gloves", "left-arm", "right-arm"];

    /// <summary>What arms with armour of these <paramref name="materials"/> wear; with none, cloth.</summary>
    internal static DaggerfallArmWear Heaviest(IEnumerable<string> materials) =>
        materials.Select(material => material == "leather" ? DaggerfallArmWear.Leather : DaggerfallArmWear.Mail).DefaultIfEmpty(DaggerfallArmWear.Cloth).Max();
}
