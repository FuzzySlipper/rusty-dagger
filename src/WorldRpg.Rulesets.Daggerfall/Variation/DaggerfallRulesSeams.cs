using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The variation points a compiled rules variant may replace as whole owners. Each seam has one owner
/// interface; classic Daggerfall supplies the default owner, and a variant claiming the seam supplies
/// its own. Nothing inside a classic owner branches on which variant is selected.
/// </summary>
public static class DaggerfallRulesSeams
{
    /// <summary>What a target does to physical harm that reached it: <see cref="IDaggerfallPhysicalMitigation"/>.</summary>
    public static readonly RulesSeamId PhysicalMitigation = new("daggerfall.physical-mitigation");

    /// <summary>When an admitted physical attack's harm is decided and whom it reaches: <see cref="Modules.Combat.IDaggerfallPhysicalDelivery"/>.</summary>
    public static readonly RulesSeamId PhysicalDelivery = new("daggerfall.physical-delivery");
}
