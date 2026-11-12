namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// Enemy engagement by contact. A melee blow lands only if its swing volume reaches the
/// target at the hit frame, so a melee enemy closes to a tuned fraction of its reach before it swings and
/// spaces its maneuvers around that distance, leaving the target's movement during the wind-up a margin.
/// A ranged attacker engages at its full range; delivery holds its line of fire.
/// </summary>
internal sealed class DaggerfallContactEngagement(DaggerfallContactTuning tuning)
{
    /// <summary>The distance at which an enemy holding an attack of <paramref name="reach"/> engages, and spaces its maneuvers around.</summary>
    public double EngageReach(double reach, bool ranged) => ranged ? reach : reach * tuning.EngageReachFraction;
}
