namespace WorldRpg.Rulesets.Daggerfall.Variants;

/// <summary>
/// Enemy engagement under the contact variant. A melee blow lands only if its swing volume reaches the
/// target at the hit frame, so a melee enemy closes to a tuned fraction of its reach before it swings and
/// spaces its maneuvers around that distance, leaving the target's movement during the wind-up a margin.
/// A ranged attacker engages at its full range; its line of fire is held by the delivery owner.
/// </summary>
internal sealed class DaggerfallContactEngagement(DaggerfallContactTuning tuning) : IDaggerfallEnemyEngagement
{
    public double EngageReach(double reach, bool ranged) => ranged ? reach : reach * tuning.EngageReachFraction;
}
