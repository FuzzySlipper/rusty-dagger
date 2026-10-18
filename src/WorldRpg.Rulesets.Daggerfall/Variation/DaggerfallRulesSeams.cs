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

    /// <summary>The player's guard: whether and how the player can block, <see cref="IDaggerfallPlayerGuard"/>. Classic has none.</summary>
    public static readonly RulesSeamId PlayerGuard = new("daggerfall.player-guard");

    /// <summary>Whom a touch spell reaches at its cast frame: <see cref="IDaggerfallTouchDelivery"/>.</summary>
    public static readonly RulesSeamId TouchDelivery = new("daggerfall.touch-delivery");

    /// <summary>How close an enemy closes before it attacks: <see cref="IDaggerfallEnemyEngagement"/>.</summary>
    public static readonly RulesSeamId EnemyEngagement = new("daggerfall.enemy-engagement");

    internal static IReadOnlyList<RulesSeamId> All { get; } = [PhysicalMitigation, PhysicalDelivery, PlayerGuard, TouchDelivery, EnemyEngagement];
}
