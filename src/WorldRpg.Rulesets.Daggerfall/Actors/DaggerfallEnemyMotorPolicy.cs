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
    bool TargetBackTurned, bool TargetBowOrSheathed);

/// <summary>
/// EnemyMotor's admitted-time advance, retreat and strafe choices; Kit executes movement. A traced enemy's rolls and
/// its chosen maneuver go to <c>gates</c> under the policy name <see cref="Policy"/>.
/// </summary>
internal static class DaggerfallEnemyMotorPolicy
{
    internal const string Policy = "motor";

    internal static PursuitPolicy Select(DaggerfallEnemyMotorMemory memory, PursuitPolicy movement,
        DaggerfallPursuitManeuverTuning tuning, DaggerfallEnemyMotorContext context, PursuitObservation observation,
        bool rangedAttack, bool inSight, bool positionKnown, bool maySearch, float deltaSeconds, Func<string, int, int, int> draw,
        List<DaggerfallDecisionGate>? gates = null)
    {
        memory.AdvanceTimer -= deltaSeconds;
        memory.StrafeTimer -= deltaSeconds;
        movement = movement with { AllowAttack = inSight, TargetObserved = positionKnown, SearchLastKnownPosition = maySearch,
            SearchDistance = tuning.SearchDistance, SearchStep = tuning.SearchStep };
        // EnemyMotor.DoRangedAttack: while the attack owner has a ranged shot for this distance and the
        // target is seen, the enemy stands and shoots, strafing at quarter speed when enhanced AI decides to.
        if (rangedAttack && inSight && positionKnown)
        {
            if (tuning.EnhancedCombatAi && memory.StrafeTimer <= 0) StrafeDecision();
            bool strafing = memory.Strafe && memory.StrafeTimer > 0;
            gates?.Add(new(Policy, "maneuver", DaggerfallGateOutcome.Chose, strafing ? "ranged-strafe" : "ranged-hold",
                new DaggerfallGateValue("distance", observation.Distance)));
            return strafing
                ? movement with { Maneuver = PursuitState.Strafe, SpeedMultiplier = .25f, StrafeDirection = memory.StrafeDirection }
                : movement with { Maneuver = PursuitState.Idle };
        }
        if (!tuning.EnhancedCombatAi || !inSight || context.TargetParalyzed || context.TargetBackTurned || context.TargetBowOrSheathed)
        {
            memory.Advance = true;
            memory.Strafe = false;
            memory.AdvanceTimer = 0;
            return Choose(PursuitState.Chase, !tuning.EnhancedCombatAi ? "classic" : !inSight ? "unseen" : context.TargetParalyzed ? "target-paralyzed"
                : context.TargetBackTurned ? "target-back-turned" : "target-guard-open");
        }
        if (observation.Reach is not double reach)
        {
            gates?.Add(new(Policy, "maneuver", DaggerfallGateOutcome.Refused, "no-reach"));
            return movement;
        }
        if (memory.AdvanceTimer <= 0)
        {
            memory.Decision++;
            memory.AdvanceTimer = draw("advance-duration", 1, 2);
            int level = Math.Clamp((context.Level - context.TargetLevel) / 2, -4, 4);
            int roll = draw("advance", level, 9 + level);
            memory.Advance = roll > 4;
            memory.RetreatMultiplier = tuning.RetreatMultiplier * (1 + .25f * (2 - roll));
            gates?.Add(new(Policy, "advance-roll", memory.Advance ? DaggerfallGateOutcome.Passed : DaggerfallGateOutcome.Refused,
                memory.Advance ? "advance" : "hold-back", new DaggerfallGateValue("roll", roll), new DaggerfallGateValue("minimum", level),
                new DaggerfallGateValue("maximum", 9 + level), new DaggerfallGateValue("passAbove", 4),
                new DaggerfallGateValue("seconds", memory.AdvanceTimer), new DaggerfallGateValue("retreatMultiplier", memory.RetreatMultiplier)));
        }
        double distance = observation.Distance;
        if (!memory.Retreating && distance >= reach * tuning.AdvanceDistanceMultiplier || memory.Advance && distance > reach)
            return Choose(PursuitState.Chase, memory.Advance && distance > reach ? "advancing" : "far");
        if (memory.StrafeTimer <= 0) StrafeDecision();
        if (memory.Strafe && distance >= reach * tuning.TooCloseMultiplier)
            return Choose(PursuitState.Strafe, "strafe-roll") with { SpeedMultiplier = .25f, StrafeDirection = memory.StrafeDirection };
        if (distance < reach * tuning.TooCloseMultiplier || !memory.Advance && distance < reach * memory.RetreatMultiplier)
            return Choose(PursuitState.Retreat, distance < reach * tuning.TooCloseMultiplier ? "too-close" : "hold-back") with { SpeedMultiplier = .5f };
        return Choose(PursuitState.Idle, "in-band");

        void StrafeDecision()
        {
            memory.Decision++;
            int roll = draw("strafe", 0, 3);
            memory.Strafe = roll == 0;
            memory.StrafeTimer = draw("strafe-duration-ms", 1000, 2000) / 1000f;
            memory.StrafeDirection = draw("strafe-direction", 0, 1) == 0 ? -1 : 1;
            gates?.Add(new(Policy, "strafe-roll", memory.Strafe ? DaggerfallGateOutcome.Passed : DaggerfallGateOutcome.Refused,
                memory.Strafe ? "strafe" : "no-strafe", new DaggerfallGateValue("roll", roll), new DaggerfallGateValue("passOn", 0),
                new DaggerfallGateValue("maximum", 3), new DaggerfallGateValue("seconds", memory.StrafeTimer),
                new DaggerfallGateValue("direction", memory.StrafeDirection)));
        }

        PursuitPolicy Choose(PursuitState intent, string reason)
        {
            memory.Retreating = intent == PursuitState.Retreat;
            gates?.Add(new(Policy, "maneuver", DaggerfallGateOutcome.Chose, $"{intent.ToString().ToLowerInvariant()}:{reason}",
                new DaggerfallGateValue("distance", observation.Distance), new DaggerfallGateValue("reach", observation.Reach ?? 0)));
            return movement with { Maneuver = intent };
        }
    }
}
