using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallLockInteractionPolicyTests
{
    [Fact]
    public void Lockpicking_formulas_keep_the_donor_interior_and_exterior_overloads()
    {
        Assert.Equal(35, DaggerfallFormulaPolicy.CalculateInteriorLockpickingChance(5, 4, 30));
        Assert.Equal(5, DaggerfallFormulaPolicy.CalculateInteriorLockpickingChance(1, 20, 0));
        Assert.Equal(95, DaggerfallFormulaPolicy.CalculateInteriorLockpickingChance(30, 0, 100));

        Assert.Equal(10, DaggerfallFormulaPolicy.CalculateExteriorLockpickingChance(4, 30));
        Assert.Equal(5, DaggerfallFormulaPolicy.CalculateExteriorLockpickingChance(20, 0));
        Assert.Equal(95, DaggerfallFormulaPolicy.CalculateExteriorLockpickingChance(0, 100));
    }

    [Fact]
    public void Interior_lockpick_success_and_failure_preserve_attempt_attribution_and_duplicate_rejection()
    {
        DaggerfallDoorView door = Door(lockValue: 4);
        DaggerfallLockInteractionDecision success = DaggerfallLockInteractionPolicy.EvaluateLockpick(
            door, DaggerfallLockInteractionSurface.Interior, playerLevel: 5, lockpickingSkill: 30,
            previousFailedSkill: null, roll: 35);

        Assert.Equal(DaggerfallLockInteractionStatus.Applied, success.Status);
        Assert.Equal(DaggerfallDoorOperationKind.Unlock, success.Mutation);
        Assert.True(success.OpensAfterUnlock);
        Assert.False(success.ClearsLockBeforeOpen);
        Assert.True(success.RecordsSkillUse);
        Assert.True(success.EmitsNoise);
        Assert.Null(success.FailedSkillLevel);

        DaggerfallLockInteractionDecision failure = DaggerfallLockInteractionPolicy.EvaluateLockpick(
            door, DaggerfallLockInteractionSurface.Interior, playerLevel: 5, lockpickingSkill: 30,
            previousFailedSkill: null, roll: 36);
        Assert.Equal(DaggerfallLockInteractionStatus.Failed, failure.Status);
        Assert.Equal(30, failure.FailedSkillLevel);
        Assert.True(failure.RecordsSkillUse);

        DaggerfallLockInteractionDecision duplicate = DaggerfallLockInteractionPolicy.EvaluateLockpick(
            door, DaggerfallLockInteractionSurface.Interior, playerLevel: 5, lockpickingSkill: 30,
            previousFailedSkill: 30, roll: null);
        Assert.Equal(DaggerfallLockInteractionStatus.DuplicateAttempt, duplicate.Status);
        Assert.False(duplicate.RecordsSkillUse);
        Assert.Null(duplicate.Roll);
    }

    [Fact]
    public void Exterior_lockpick_uses_strict_boundary_and_rejects_repeated_or_invalid_door_state()
    {
        DaggerfallDoorView door = Door(lockValue: 4);
        // Exterior PlayerActivate succeeds only when chance > roll, unlike the
        // interior Dice100.FailedRoll path which accepts equality.
        Assert.Equal(DaggerfallLockInteractionStatus.Failed,
            DaggerfallLockInteractionPolicy.EvaluateLockpick(
                door, DaggerfallLockInteractionSurface.Exterior, 1, 30, null, 10).Status);
        Assert.Equal(DaggerfallLockInteractionStatus.Applied,
            DaggerfallLockInteractionPolicy.EvaluateLockpick(
                door, DaggerfallLockInteractionSurface.Exterior, 1, 30, null, 9).Status);
        Assert.Equal(DaggerfallLockInteractionStatus.DuplicateAttempt,
            DaggerfallLockInteractionPolicy.EvaluateLockpick(
                door, DaggerfallLockInteractionSurface.Exterior, 1, 30, 30, null).Status);

        Assert.Equal(DaggerfallLockInteractionStatus.AlreadyUnlocked,
            DaggerfallLockInteractionPolicy.EvaluateLockpick(
                Door(), DaggerfallLockInteractionSurface.Interior, 1, 30, null, null).Status);
        Assert.Equal(DaggerfallLockInteractionStatus.SpecialDoor,
            DaggerfallLockInteractionPolicy.EvaluateLockpick(
                Door(lockValue: 0, kind: DaggerfallDoorKind.Special), DaggerfallLockInteractionSurface.Interior,
                1, 30, null, null).Status);
        Assert.Equal(DaggerfallLockInteractionStatus.MagicallyHeld,
            DaggerfallLockInteractionPolicy.EvaluateLockpick(
                Door(lockValue: 20), DaggerfallLockInteractionSurface.Interior, 1, 30, null, null).Status);
        Assert.Equal(DaggerfallLockInteractionStatus.DoorMoving,
            DaggerfallLockInteractionPolicy.EvaluateLockpick(
                Door(lockValue: 4, motion: DaggerfallDoorMotion.Opening), DaggerfallLockInteractionSurface.Interior,
                1, 30, null, null).Status);
    }

    [Fact]
    public void Bash_uses_strength_tool_force_and_canonical_motion_with_noise_and_damage_consequences()
    {
        DaggerfallDoorView door = Door(lockValue: 4);
        DaggerfallLockInteractionDecision success = DaggerfallLockInteractionPolicy.EvaluateBash(
            door, DaggerfallLockInteractionSurface.Interior, strength: 50, toolForce: 0, roll: 16);

        Assert.Equal(16, success.Chance);
        Assert.Equal(DaggerfallLockInteractionStatus.Applied, success.Status);
        Assert.Equal(DaggerfallDoorOperationKind.Open, success.Mutation);
        Assert.True(success.ClearsLockBeforeOpen);
        Assert.Equal(5, success.DoorDamage);
        Assert.True(success.EmitsNoise);
        Assert.True(success.ConsumedAttempt);

        // Strength 60 contributes +2 through the canonical damage modifier;
        // the equipped tool contributes its admitted force directly.
        Assert.Equal(21, DaggerfallLockInteractionPolicy.CalculateBashChance(
            DaggerfallLockInteractionSurface.Interior, lockValue: 4, strength: 60, toolForce: 3));
        Assert.Equal(DaggerfallLockInteractionStatus.Failed,
            DaggerfallLockInteractionPolicy.EvaluateBash(
                door, DaggerfallLockInteractionSurface.Interior, 60, 3, 22).Status);

        // Exterior PlayerActivate bashing also uses Dice100.FailedRoll, so
        // the boundary roll succeeds even though exterior lockpicking is strict.
        Assert.Equal(DaggerfallLockInteractionStatus.Applied,
            DaggerfallLockInteractionPolicy.EvaluateBash(
                door, DaggerfallLockInteractionSurface.Exterior, 50, 0, 21).Status);
    }

    [Fact]
    public void Bash_closes_open_doors_and_rejects_special_magic_and_motion_without_consuming_a_roll()
    {
        DaggerfallLockInteractionDecision close = DaggerfallLockInteractionPolicy.EvaluateBash(
            Door(lockValue: 20, motion: DaggerfallDoorMotion.Open),
            DaggerfallLockInteractionSurface.Interior, strength: 0, toolForce: 0, roll: null);
        Assert.Equal(DaggerfallLockInteractionStatus.Applied, close.Status);
        Assert.Equal(DaggerfallDoorOperationKind.Close, close.Mutation);
        Assert.Null(close.Roll);
        Assert.Equal(0, close.DoorDamage);

        Assert.Equal(DaggerfallLockInteractionStatus.SpecialDoor,
            DaggerfallLockInteractionPolicy.EvaluateBash(
                Door(kind: DaggerfallDoorKind.Special), DaggerfallLockInteractionSurface.Interior, 50, 0, null).Status);
        Assert.Equal(DaggerfallLockInteractionStatus.MagicallyHeld,
            DaggerfallLockInteractionPolicy.EvaluateBash(
                Door(lockValue: 20), DaggerfallLockInteractionSurface.Interior, 50, 0, null).Status);
        Assert.Equal(DaggerfallLockInteractionStatus.DoorMoving,
            DaggerfallLockInteractionPolicy.EvaluateBash(
                Door(lockValue: 4, motion: DaggerfallDoorMotion.Closing), DaggerfallLockInteractionSurface.Interior, 50, 0, null).Status);
    }

    [Fact]
    public void Deferred_decisions_draw_only_after_the_door_state_admits_an_attempt()
    {
        int draws = 0;
        DaggerfallLockInteractionDecision rejected = DaggerfallLockInteractionPolicy.EvaluateLockpickDeferred(
            Door(lockValue: 4, motion: DaggerfallDoorMotion.Opening),
            DaggerfallLockInteractionSurface.Interior,
            playerLevel: 1,
            lockpickingSkill: 30,
            previousFailedSkill: null,
            drawRoll: () =>
            {
                draws++;
                return 1;
            });
        Assert.Equal(DaggerfallLockInteractionStatus.DoorMoving, rejected.Status);
        Assert.Equal(0, draws);

        DaggerfallLockInteractionDecision admitted = DaggerfallLockInteractionPolicy.EvaluateLockpickDeferred(
            Door(lockValue: 4),
            DaggerfallLockInteractionSurface.Interior,
            playerLevel: 5,
            lockpickingSkill: 30,
            previousFailedSkill: null,
            drawRoll: () =>
            {
                draws++;
                return 35;
            });
        Assert.Equal(DaggerfallLockInteractionStatus.Applied, admitted.Status);
        Assert.Equal(1, draws);
    }

    private static DaggerfallDoorView Door(
        int lockValue = 0,
        DaggerfallDoorKind kind = DaggerfallDoorKind.Normal,
        DaggerfallDoorMotion motion = DaggerfallDoorMotion.Closed) =>
        new(
            new DaggerfallRdbDoorId("S0000007.RDB", 1, 1, 3),
            new EntityId(1),
            new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One),
            motion,
            motion switch
            {
                DaggerfallDoorMotion.Closed => 0F,
                DaggerfallDoorMotion.Open => 1F,
                _ => .5F,
            },
            lockValue,
            motion == DaggerfallDoorMotion.Closed,
            kind);
}
