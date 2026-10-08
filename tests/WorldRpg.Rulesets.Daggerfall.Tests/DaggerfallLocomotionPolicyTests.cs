using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallLocomotionPolicyTests
{
    [Theory]
    [InlineData(false, false, 0F)]
    [InlineData(false, true, 0F)]
    [InlineData(true, false, .1F)]
    [InlineData(true, true, .2F)]
    public void Athletic_jump_bonus_reaches_engine_controls_and_reverts_when_the_talent_is_removed(bool career, bool held, float bonus)
    {
        var stats = Stats(speed: 50, running: 40, stamina: 200);
        var policy = new DaggerfallLocomotionPolicy(DaggerfallLocomotionTuning.Classic, new());
        policy.SetAthletics(career, held);
        Assert.Equal(4.5F * (1.25F + bonus), policy.BeginStep([], 1F / 60F, stats, true).Controls.JumpSpeed);
        policy.SetAthletics(career, false);
        Assert.Equal(4.5F * (1.25F + (career ? .1F : 0)), policy.BeginStep([], 1F / 60F, stats, true).Controls.JumpSpeed);
    }

    [Fact]
    public void Classic_speed_modes_use_live_speed_running_skill_and_rebound_physical_keys()
    {
        StatsComponent stats = Stats(speed: 50, running: 40, stamina: 200);
        DaggerfallControlSettings controls = new();
        DaggerfallLocomotionPolicy policy = new(DaggerfallLocomotionTuning.Classic, controls);

        DaggerfallLocomotionStep run = policy.BeginStep([Key(KeyboardControl.ShiftLeft, InputEdge.Pressed)], 1f / 60f, stats, canMove: true);
        Assert.True(run.Running);
        Assert.Equal(((50f + 150f) / 39.5f) * (1.35f + (40f / 200f)), run.Controls.ForwardSpeed);

        _ = policy.BeginStep([Key(KeyboardControl.ShiftLeft, InputEdge.Released), Key(KeyboardControl.ControlLeft, InputEdge.Pressed)], 1f / 60f, stats, canMove: true);
        DaggerfallLocomotionStep crouch = policy.BeginStep([], 1f / 60f, stats, canMove: true);
        Assert.True(crouch.Controls.CrouchRequested);
        Assert.Equal((50f + 50f) / 39.5f, crouch.Controls.ForwardSpeed);
        Assert.Equal(4.5f * (1f + ((50f * .5f) / 100f)) * .8f, crouch.Controls.JumpSpeed);

        controls.Rebind("run", ["KeyQ"]);
        policy.Rebind(controls);
        DaggerfallLocomotionStep rebound = policy.BeginStep([Key(KeyboardControl.KeyQ, InputEdge.Pressed)], 1f / 60f, stats, canMove: true);
        Assert.True(rebound.Running);
        policy.Neutralize();
        Assert.False(policy.BeginStep([], 1f / 60f, stats, canMove: true).Running);
    }

    [Fact]
    public void Overweight_input_cannot_request_a_jump_and_an_accepted_airborne_transition_charges_once()
    {
        StatsComponent stats = Stats(speed: 50, running: 40, stamina: 100);
        DaggerfallLocomotionPolicy policy = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());

        Assert.False(policy.BeginStep([Key(KeyboardControl.Space, InputEdge.Pressed)], 1f / 60f, stats, canMove: false).Controls.JumpPressed);

        DaggerfallLocomotionStep jump = policy.BeginStep([Key(KeyboardControl.Space, InputEdge.Pressed)], 1f / 60f, stats, canMove: true);
        CharacterStepReceipt airborne = default(CharacterStepReceipt) with
        {
            Displacement = new Vector3(0f, .1f, 0f),
            Motion = default(CharacterMotion) with { ControlledVelocity = new Vector3(0f, 4f, 0f), Grounded = false },
        };
        List<DaggerfallSkillUse> uses = [];
        policy.CompleteStep(jump, default, airborne, 1d, stats, uses.Add);
        policy.CompleteStep(jump, default, airborne, 1d, stats, uses.Add);

        Assert.Equal(89d, stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
        Assert.Equal(DaggerfallSkillUseReason.Jumping, Assert.Single(uses).Reason);
    }

    [Fact]
    public void Low_or_empty_fatigue_never_refuses_a_run_or_a_jump_and_the_jump_charge_clamps_at_zero()
    {
        StatsComponent stats = Stats(speed: 50, running: 40, stamina: 0);
        DaggerfallLocomotionPolicy policy = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());

        DaggerfallLocomotionStep run = policy.BeginStep([Key(KeyboardControl.ShiftLeft, InputEdge.Pressed)], 1f / 60f, stats, canMove: true);
        Assert.True(run.Running);
        policy.Neutralize();
        stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).SetCurrent(5);
        DaggerfallLocomotionStep jump = policy.BeginStep([Key(KeyboardControl.Space, InputEdge.Pressed)], 1f / 60f, stats, canMove: true);
        Assert.True(jump.Controls.JumpPressed);
        Assert.True(jump.JumpRequested);
        List<DaggerfallSkillUse> uses = [];
        policy.CompleteStep(jump, default, default(CharacterStepReceipt) with
        {
            Displacement = new Vector3(0f, .1f, 0f),
            Motion = default(CharacterMotion) with { ControlledVelocity = new Vector3(0f, 4f, 0f), Grounded = false },
        }, 1d, stats, uses.Add);

        Assert.Equal(0d, stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
        Assert.Equal(DaggerfallSkillUseReason.Jumping, Assert.Single(uses).Reason);
    }

    [Fact]
    public void Running_fatigue_uses_the_calendar_once_per_minute_and_accepted_steps_record_running_without_a_throttle()
    {
        StatsComponent stats = Stats(speed: 50, running: 40, stamina: 200);
        DaggerfallLocomotionPolicy policy = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());
        DaggerfallLocomotionStep run = policy.BeginStep([Key(KeyboardControl.ShiftLeft, InputEdge.Pressed)], 1f / 60f, stats, canMove: true);
        CharacterStepReceipt moved = default(CharacterStepReceipt) with
        {
            Displacement = new Vector3(.1f, 0f, 0f),
            Motion = default(CharacterMotion) with { Grounded = true },
        };
        List<DaggerfallSkillUse> uses = [];
        policy.CompleteStep(run, default, moved, 1d, stats, uses.Add);
        policy.CompleteStep(run, default, moved, 1d, stats, uses.Add);
        policy.AdvanceCalendarMinutes(100, 101, stats);
        policy.AdvanceCalendarMinutes(101, 102, stats);

        Assert.Equal(101d, stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
        Assert.Equal(2, uses.Count(use => use.Reason == DaggerfallSkillUseReason.Running));
    }

    [Fact]
    public void Movement_fatigue_uses_the_donor_activity_rates_and_scales_idle_loss_with_athleticism()
    {
        static CharacterStepReceipt Moved() => default(CharacterStepReceipt) with
        {
            Displacement = new Vector3(.1f, 0f, 0f),
            Motion = default(CharacterMotion) with { Grounded = true },
        };

        StatsComponent climbingStats = Stats(speed: 50, running: 40, stamina: 200);
        DaggerfallLocomotionPolicy climbing = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());
        climbing.CompleteStep(new DaggerfallLocomotionStep(default, false, false, Climbing: true), default, Moved(), 60d, climbingStats, _ => { });
        climbing.AdvanceCalendarMinutes(0, 1, climbingStats);
        Assert.Equal(178d, climbingStats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);

        StatsComponent swimmingStats = Stats(speed: 50, running: 40, stamina: 200);
        DaggerfallLocomotionPolicy swimming = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());
        swimming.CompleteStep(default, default, Moved(), 60d, swimmingStats, _ => { }, swimming: true);
        swimming.AdvanceCalendarMinutes(0, 1, swimmingStats);
        Assert.Equal(156d, swimmingStats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);

        StatsComponent idleStats = Stats(speed: 50, running: 40, stamina: 200);
        DaggerfallLocomotionPolicy idle = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());
        idle.SetAthletics(careerAdvantage: true, improvedHeldTalent: false);
        idle.AdvanceCalendarMinutes(0, 1, idleStats);
        // (int)(11 * 0.9): the donor scales the default per-minute loss as well.
        Assert.Equal(191d, idleStats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
    }

    public enum Activity { Idle, Run, Climb }

    [Theory]
    [InlineData(Activity.Idle, false, false, 11d)]
    [InlineData(Activity.Idle, true, false, 9d)]
    [InlineData(Activity.Idle, true, true, 8d)]
    [InlineData(Activity.Idle, false, true, 11d)]
    [InlineData(Activity.Run, false, false, 88d)]
    [InlineData(Activity.Run, true, false, 79d)]
    [InlineData(Activity.Run, true, true, 70d)]
    [InlineData(Activity.Run, false, true, 88d)]
    [InlineData(Activity.Climb, false, false, 22d)]
    [InlineData(Activity.Climb, true, false, 19d)]
    [InlineData(Activity.Climb, true, true, 17d)]
    [InlineData(Activity.Climb, false, true, 22d)]
    public void Each_game_minute_pays_the_donor_rate_scaled_by_career_athleticism_and_the_improved_talent_only_with_it(
        Activity activity, bool career, bool improved, double expectedLoss)
    {
        StatsComponent stats = Stats(speed: 50, running: 40, stamina: 200);
        DaggerfallLocomotionPolicy policy = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());
        policy.SetAthletics(career, improved);
        CharacterStepReceipt moved = default(CharacterStepReceipt) with
        {
            Displacement = new Vector3(.1f, 0f, 0f),
            Motion = default(CharacterMotion) with { Grounded = true },
        };
        if (activity != Activity.Idle)
            policy.CompleteStep(new DaggerfallLocomotionStep(default, Running: activity == Activity.Run, JumpRequested: false,
                Climbing: activity == Activity.Climb), default, moved, 60d, stats, _ => { });
        policy.AdvanceCalendarMinutes(0, 1, stats);
        Assert.Equal(expectedLoss, 200d - stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
    }

    [Fact]
    public void Running_in_place_pays_only_the_idle_rate()
    {
        StatsComponent stats = Stats(speed: 50, running: 40, stamina: 200);
        DaggerfallLocomotionPolicy policy = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());
        policy.CompleteStep(new DaggerfallLocomotionStep(default, Running: true, JumpRequested: false), default,
            default(CharacterStepReceipt) with { Motion = default(CharacterMotion) with { Grounded = true } }, 60d, stats, _ => { });
        policy.AdvanceCalendarMinutes(0, 1, stats);
        Assert.Equal(189d, stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
    }

    [Theory]
    [InlineData(false, false, 44d)]
    [InlineData(true, false, 39d)]
    [InlineData(false, true, 11d)]
    [InlineData(true, true, 9d)]
    public void A_swimming_minute_pays_the_swimming_rate_only_when_its_roll_fails_and_otherwise_the_idle_rate(
        bool career, bool passed, double expected)
    {
        StatsComponent stats = Stats(speed: 50, running: 40, stamina: 200);
        DaggerfallLocomotionPolicy policy = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());
        policy.SetAthletics(career, false);
        policy.CompleteStep(default, default, default(CharacterStepReceipt) with
        {
            Displacement = new Vector3(.1f, 0f, 0f),
            Motion = default(CharacterMotion) with { Grounded = true },
        }, 60d, stats, _ => { }, swimming: true);
        List<long> asked = [];
        policy.AdvanceCalendarMinutes(40, 41, stats, swimmingFatigueApplies: minute => { asked.Add(minute); return !passed; });

        Assert.Equal(expected, 200d - stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
        Assert.Equal([40L], asked);
    }

    [Fact]
    public void Elapsed_calendar_catch_up_charges_each_covered_minute_and_rest_can_suppress_idle_loss()
    {
        StatsComponent travelStats = Stats(speed: 50, running: 40, stamina: 200);
        DaggerfallLocomotionPolicy travel = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());
        travel.AdvanceCalendarMinutes(0, 3, travelStats);
        Assert.Equal(167d, travelStats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);

        StatsComponent restStats = Stats(speed: 50, running: 40, stamina: 200);
        DaggerfallLocomotionPolicy rest = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());
        rest.AdvanceCalendarMinutes(0, 3, restStats, includeIdleFatigue: false);
        Assert.Equal(200d, restStats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
    }

    [Fact]
    public void Accepted_airborne_to_grounded_transition_reports_the_engine_peak_once()
    {
        StatsComponent stats = Stats(speed: 50, running: 40, stamina: 200);
        DaggerfallLocomotionPolicy policy = new(DaggerfallLocomotionTuning.Classic, new DaggerfallControlSettings());
        CharacterStepReceipt landing = default(CharacterStepReceipt) with
        {
            Transform = new Transform(new Vector3(0f, 2f, 0f), Quaternion.Identity, Vector3.One),
            Motion = default(CharacterMotion) with { Grounded = true },
        };
        CharacterMotion airborne = default(CharacterMotion) with { Grounded = false, PeakY = 9.4f };

        DaggerfallLanding? accepted = policy.CompleteStep(default, airborne, landing, 1d, stats, _ => { });
        DaggerfallLanding? repeatedGround = policy.CompleteStep(default, landing.Motion, landing, 1d, stats, _ => { });

        Assert.Equal(7.4f, accepted!.Value.Distance, precision: 4);
        Assert.Null(repeatedGround);
    }

    private static StatsComponent Stats(int speed, int running, int stamina)
    {
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.Speed.Value), new Stat(speed));
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.Running.Value), new Stat(running));
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.Jumping.Value), new Stat(50));
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value), new Track(Math.Max(200, stamina), stamina));
        return stats;
    }

    private static ProductInputEvent Key(KeyboardControl key, InputEdge edge) => new(
        InputEventKind.Key, edge, InputDevice.Keyboard, InputChannel.Button, InputAxis.None, key,
        PointerButton.None, ControllerButton.None, ControllerAxis.None, InputClearReason.None,
        InputValueKind.Digital, edge == InputEdge.Pressed ? InputPhase.Pressed : InputPhase.Released,
        InputProvenance.Physical, default, default, default, 0f, 0f,
        ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
        ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);
}
