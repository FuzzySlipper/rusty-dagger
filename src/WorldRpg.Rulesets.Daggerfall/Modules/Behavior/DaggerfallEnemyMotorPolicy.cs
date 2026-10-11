using WorldRpg.Kit.Ai;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

internal sealed class DaggerfallEnemyMotorMemory
{
    internal float AdvanceTimer;
    internal float StrafeTimer;
    internal bool Advance = true;
    internal bool Strafe;
    internal bool Retreating;
    internal int StrafeDirection = 1;
    internal float RetreatMultiplier;
    internal ulong Decision;
}

internal readonly record struct DaggerfallEnemyMotorContext(int Level, int TargetLevel, bool TargetParalyzed,
    bool TargetBackTurned, bool TargetBowOrSheathed, bool PrefersRanged = false, double MinimumRangedDistance = 0);

/// <summary>EnemyMotor's admitted-time advance, retreat and strafe choices; Kit executes movement.</summary>
internal static class DaggerfallEnemyMotorPolicy
{
    internal static PursuitPolicy Select(DaggerfallEnemyMotorMemory memory, PursuitPolicy movement,
        DaggerfallPursuitManeuverTuning tuning, DaggerfallEnemyMotorContext context, PursuitObservation observation,
        bool inSight, bool positionKnown, bool maySearch, float deltaSeconds, Func<string, int, int, int> draw)
    {
        memory.AdvanceTimer -= deltaSeconds;
        memory.StrafeTimer -= deltaSeconds;
        movement = movement with { AllowAttack = inSight, TargetObserved = positionKnown, SearchLastKnownPosition = maySearch,
            SearchDistance = tuning.SearchDistance, SearchStep = tuning.SearchStep };
        // The published preference keeps an equipped ranged actor at its usable reach.
        // A melee fallback has melee reach and follows the normal advance policy instead.
        if (context.PrefersRanged && inSight && observation.Reach is > 2.25 and var rangedReach)
        {
            if (observation.Distance < context.MinimumRangedDistance)
                return Choose(PursuitState.Retreat);
            return Choose(observation.Distance <= rangedReach ? PursuitState.Idle : PursuitState.Chase);
        }
        if (!tuning.EnhancedCombatAi || !inSight || context.TargetParalyzed || context.TargetBackTurned || context.TargetBowOrSheathed)
        {
            memory.Advance = true;
            memory.Strafe = false;
            memory.AdvanceTimer = 0;
            return Choose(PursuitState.Chase);
        }
        if (observation.Reach is not double reach) return movement;
        if (memory.AdvanceTimer <= 0)
        {
            memory.Decision++;
            memory.AdvanceTimer = draw("advance-duration", 1, 2);
            int level = Math.Clamp((context.Level - context.TargetLevel) / 2, -4, 4);
            int roll = draw("advance", level, 9 + level);
            memory.Advance = roll > 4;
            memory.RetreatMultiplier = tuning.RetreatMultiplier * (1 + .25f * (2 - roll));
        }
        double distance = observation.Distance;
        if (!memory.Retreating && distance >= reach * tuning.AdvanceDistanceMultiplier || memory.Advance && distance > reach)
            return Choose(PursuitState.Chase);
        if (memory.StrafeTimer <= 0)
        {
            memory.Decision++;
            memory.Strafe = draw("strafe", 0, 3) == 0;
            memory.StrafeTimer = draw("strafe-duration-ms", 1000, 2000) / 1000f;
            memory.StrafeDirection = draw("strafe-direction", 0, 1) == 0 ? -1 : 1;
        }
        if (memory.Strafe && distance >= reach * tuning.TooCloseMultiplier)
            return Choose(PursuitState.Strafe) with { SpeedMultiplier = .25f, StrafeDirection = memory.StrafeDirection };
        if (distance < reach * tuning.TooCloseMultiplier || !memory.Advance && distance < reach * memory.RetreatMultiplier)
            return Choose(PursuitState.Retreat) with { SpeedMultiplier = .5f };
        return Choose(PursuitState.Idle);

        PursuitPolicy Choose(PursuitState intent)
        {
            memory.Retreating = intent == PursuitState.Retreat;
            return movement with { Maneuver = intent };
        }
    }
}
