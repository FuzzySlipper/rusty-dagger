namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// One motion's three phases, in seconds: the windup up to the hit, the commit (the hit window), and the
/// recovery, in which the next fast attack can start.
/// </summary>
internal readonly record struct DaggerfallAttackTiming(double Windup, double Commit, double Recovery)
{
    internal double Duration => Windup + Commit + Recovery;
}

/// <summary>How hard a blow rocked its target.</summary>
internal enum DaggerfallStrikeLevel { Flinch, Stagger, Knockdown }

/// <summary>What a blow does to its target: how hard it rocks it, how fast it throws it, and how long it holds it.</summary>
internal readonly record struct DaggerfallStrikeReaction(DaggerfallStrikeLevel Level, double Push, double HoldSeconds);
