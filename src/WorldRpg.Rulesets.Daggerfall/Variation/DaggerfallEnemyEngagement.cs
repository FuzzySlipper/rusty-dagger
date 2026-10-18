namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The enemy engagement seam (<see cref="DaggerfallRulesSeams.EnemyEngagement"/>): how close an enemy closes
/// before it attacks. Enemy behavior keeps its pursuit, maneuvers and perception; this owner only spaces the
/// attack against the delivery that resolves it.
/// </summary>
internal interface IDaggerfallEnemyEngagement
{
    /// <summary>The distance at which an enemy holding an attack of <paramref name="reach"/> engages, and spaces its maneuvers around.</summary>
    double EngageReach(double reach, bool ranged);
}

/// <summary>Classic engagement: an enemy attacks as soon as its target is within the attack's own reach.</summary>
internal sealed class DaggerfallClassicEnemyEngagement : IDaggerfallEnemyEngagement
{
    internal static DaggerfallClassicEnemyEngagement Instance { get; } = new();
    private DaggerfallClassicEnemyEngagement() { }
    public double EngageReach(double reach, bool ranged) => reach;
}
