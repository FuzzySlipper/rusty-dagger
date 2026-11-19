using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

/// <summary>
/// The legacy tactic: the whole decision enemies made before tactics existed, which every mobile fights with unless
/// its record names another (<c>"tactic": "legacy"</c> is the default).
/// <list type="bullet">
/// <item>It moves by the donor's EnemyMotor (<see cref="DaggerfallEnemyMotorPolicy"/>): the enhanced advance, strafe
/// and retreat rolls when the tuning's <c>enhancedCombatAi</c> is on, as it ships, else the classic straight close; it
/// holds its band for a bow shot or a ready ranged spell.</item>
/// <item>It casts by the classic spell choice (<see cref="DaggerfallEnemyMagicModule.Choose"/>): in the ranged band a
/// keyed uniform pick and a one-in-forty roll with the lane clear, at melee distance a touch or self spell.</item>
/// <item>It strikes with the attack combat gives it at the distance: a bow in its band while arrows last, else its
/// melee weapon (combat's EnemyAction).</item>
/// </list>
/// Its motor memory is the actor's <see cref="DaggerfallEnemyMotorMemory"/>; it keeps nothing of its own.
/// </summary>
internal sealed class DaggerfallLegacyEnemyTactic(DaggerfallEnemyMagicModule magic, DaggerfallContactEngagement engagement,
    DaggerfallPursuitManeuverTuning maneuvers, IRandomService random) : IDaggerfallEnemyTactic
{
    public PursuitPolicy Move(DaggerfallEnemyMoveObservation seen, List<DaggerfallDecisionGate>? gates)
    {
        bool inSight = seen.Senses?.InSight ?? seen.Pursuit.Visible;
        bool known = seen.Pursuit.Visible && (inSight || seen.Senses?.InEarshot == true);
        // EnemyMotor.DoRangedAttack holds the band for a selected bow shot or a ready ranged spell.
        bool rangedAttack = seen.Attack?.Ranged == true || magic.HasRangedSpellReady(seen.Actor, seen.Target, seen.Pursuit.Distance, seen.LaneClear);
        // Engagement spaces the attack and the maneuvers around it against the delivery that resolves it.
        PursuitObservation observation = seen.Pursuit with
        {
            Reach = (seen.Attack?.Reach ?? seen.Pursuit.Reach) is double reach ? engagement.EngageReach(reach, seen.Attack?.Ranged == true) : null,
        };
        DaggerfallEnemyMotorMemory memory = seen.Actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Motor;
        long actor = seen.Actor.DurableId, target = seen.Target.DurableId;
        return DaggerfallEnemyMotorPolicy.Select(memory, seen.Movement, maneuvers, seen.Motor, observation, rangedAttack, inSight, known,
            seen.MaySearch, seen.DeltaSeconds,
            (key, min, max) => checked((int)random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.enemy-motor",
                $"actor:{actor}:target:{target}:generation:{seen.Generation}:step:{seen.Step}:decision:{memory.Decision}:{key}", min, max)).Value),
            gates) with { AttackReach = observation.Reach };
    }

    /// <summary>A spell first, by the classic choice, and the strike when none took the action.</summary>
    public DaggerfallEnemyActionChoice Act(DaggerfallEnemyActionObservation seen, List<DaggerfallDecisionGate>? gates)
    {
        if (!seen.ActionOpen)
        {
            gates?.Add(new(DaggerfallEnemyMagicModule.Policy, "action", DaggerfallGateOutcome.Refused, "action-taken-this-update"));
            return new(null, Strike: true);
        }
        return new(magic.Choose(seen.Actor, seen.Target, seen.Visibility, seen.Senses, seen.LaneClear, seen.Generation, seen.Step, gates), Strike: true);
    }

    /// <summary>The legacy decision remembers nothing of what it did.</summary>
    public void Admitted(ActorState actor, DaggerfallEnemyActionChoice choice, DaggerfallEnemyAdmission admission) { }
}
