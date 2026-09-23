using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallClimbingPolicyTests
{
    [Fact]
    public void Donor_chance_applies_race_then_enhancement_then_skill_clamp_and_luck()
    {
        Assert.Equal(90, DaggerfallFormulaPolicy.CalculateClimbingChance(50, 50, false, false, 70));
        Assert.Equal(90, DaggerfallFormulaPolicy.CalculateClimbingChance(20, 50, true, false, 70));
        Assert.Equal(108, DaggerfallFormulaPolicy.CalculateClimbingChance(95, 100, false, true, 70));
        Assert.Equal(71, DaggerfallFormulaPolicy.CalculateClimbingChance(0, -20, false, false, 70));
    }

    [Fact]
    public void Accepted_checks_attach_then_slip_recover_and_detach_when_wall_is_lost()
    {
        DaggerfallClimbingPolicy policy = new(DaggerfallClimbingTuning.Classic);
        StatsComponent stats = Stats(stamina: 200);
        DaggerfallLocomotionStep forward = Forward();
        List<DaggerfallSkillUse> uses = [];
        DaggerfallClimbStep start = policy.BeginStep(forward, default(CharacterMotion) with { Grounded = true },
            wallAhead: true, wallAtFeet: true, wallBehind: false, canMove: true, stats, khajiit: false, enhancedClimbing: false,
            .8f, () => 1);
        Assert.True(start.Climbing);
        Assert.Equal(1f, start.VerticalVelocity);
        policy.CompleteStep(start, Receipt(.1f), uses.Add);
        policy.CompleteStep(start, Receipt(.1f), uses.Add);
        Assert.Single(uses);

        DaggerfallClimbStep slip = policy.BeginStep(forward, default, true, true, false, true, stats, false, false, .9f, () => 100);
        Assert.True(slip.Climbing);
        Assert.Null(slip.VerticalVelocity);
        policy.CompleteStep(slip, Receipt(-.1f), uses.Add);
        DaggerfallClimbStep regained = policy.BeginStep(forward, default, true, true, false, true, stats, false, false, .3f, () => 1);
        Assert.True(regained.Climbing);
        policy.CompleteStep(regained, Receipt(.1f), uses.Add);
        Assert.Equal(3, uses.Count);

        DaggerfallClimbStep upperEdge = policy.BeginStep(forward, default, false, true, false, true, stats, false, false, .1f, () => 1);
        Assert.True(upperEdge.Climbing);
        Assert.Equal(1f, upperEdge.VerticalVelocity);

        Assert.False(policy.BeginStep(forward, default, false, false, false, true, stats, false, false, .1f, () => 1).Climbing);
        Assert.False(policy.IsAttached);
    }

    [Fact]
    public void Falling_backstep_rappel_and_restored_wall_attachment_have_distinct_admitted_outcomes()
    {
        DaggerfallClimbingPolicy policy = new(DaggerfallClimbingTuning.Classic);
        StatsComponent stats = Stats(stamina: 200);
        DaggerfallLocomotionStep back = Forward() with { ForwardIntent = -1f };
        List<DaggerfallSkillUse> uses = [];
        Assert.False(policy.BeginStep(back, default(CharacterMotion) with { Grounded = false, ControlledVelocity = Vector3.UnitY },
            false, false, true, true, stats, false, false, .1f, () => 100).Climbing);
        DaggerfallClimbStep rappel = policy.BeginStep(back, default(CharacterMotion) with { Grounded = false, ControlledVelocity = new Vector3(0f, -1f, 0f) },
            wallAhead: false, wallAtFeet: false, wallBehind: true, canMove: true, stats, false, false, .1f, () => 100);
        Assert.True(rappel.Rappelling);
        Assert.Equal(-1f, rappel.VerticalVelocity);
        policy.CompleteStep(rappel, Receipt(-.1f), uses.Add);
        Assert.Equal(DaggerfallSkillUseReason.Rappelling, Assert.Single(uses).Reason);
        DaggerfallClimbStep continuing = policy.BeginStep(back, default, false, false, true, true, stats, false, false, .1f, () => 100);
        Assert.True(continuing.Rappelling);
        Assert.True(continuing.Climbing);

        DaggerfallClimbingSave saved = policy.Capture();
        DaggerfallClimbingPolicy restored = new(DaggerfallClimbingTuning.Classic);
        restored.Restore(saved);
        DaggerfallClimbStep first = restored.BeginStep(back, default,
            wallAhead: false, wallAtFeet: false, wallBehind: true, canMove: true, stats, false, false, .1f, () => 100);
        Assert.Equal(0f, first.VerticalVelocity);
        Assert.True(restored.IsAttached);
        Assert.False(restored.BeginStep(Forward() with { ForwardIntent = 0f }, default,
            wallAhead: true, wallAtFeet: true, wallBehind: false, canMove: true, stats, false, false, .1f, () => 100).Climbing);
        Assert.False(restored.IsAttached);
    }

    [Fact]
    public void Exhaustion_detaches_and_accepted_climbing_time_charges_the_calendar_owner_once()
    {
        StatsComponent stats = Stats(stamina: 200);
        DaggerfallClimbingPolicy climbing = new(DaggerfallClimbingTuning.Classic);
        DaggerfallClimbStep attached = climbing.BeginStep(Forward(), default(CharacterMotion) with { Grounded = true },
            true, true, false, true, stats, false, false, .8f, () => 1);
        climbing.CompleteStep(attached, Receipt(.1f), _ => { });
        stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).SetCurrent(0);
        Assert.False(climbing.BeginStep(Forward(), default, true, true, false, true, stats, false, false, .1f, () => 1).Climbing);

        stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).SetCurrent(200);
        DaggerfallLocomotionPolicy locomotion = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());
        locomotion.CompleteStep(Forward() with { Climbing = true }, default, Receipt(.1f), 60d, stats, _ => { });
        locomotion.AdvanceCalendarMinutes(0, 1, stats);
        locomotion.AdvanceCalendarMinutes(1, 2, stats);
        Assert.Equal(167d, stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
    }

    private static DaggerfallLocomotionStep Forward() => new(new CharacterStepControls(ForwardSpeed: 3f),
        Running: false, JumpRequested: false, ForwardIntent: 1f);

    private static CharacterStepReceipt Receipt(float verticalDisplacement) => default(CharacterStepReceipt) with
    {
        Displacement = new Vector3(0f, verticalDisplacement, 0f),
        Motion = default(CharacterMotion) with { Grounded = false },
    };

    private static StatsComponent Stats(int stamina)
    {
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.Climbing.Value), new Stat(10));
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.Luck.Value), new Stat(50));
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value), new Track(200, stamina));
        return stats;
    }
}
