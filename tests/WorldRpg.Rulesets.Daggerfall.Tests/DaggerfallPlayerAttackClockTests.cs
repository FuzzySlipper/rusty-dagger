using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The player's attack clock over a fake session. Every length here is a multiple of the eighth-second step, so a
/// phase ends on a known step: a fast attack hits two steps in, enters recovery at four and ends at six.
/// </summary>
public sealed class DaggerfallPlayerAttackClockTests
{
    private const double Step = .125d;
    private static readonly DaggerfallAttackTiming Fast = new(.25d, .25d, .25d);
    private static readonly DaggerfallAttackTiming Release = new(.25d, .25d, .5d);
    private static readonly DaggerfallAttackTiming Kick = new(.25d, .125d, .25d);
    private static readonly DaggerfallAttackTuning Tuning = new(.25d, 2d, 2d, Kick, 6, 1, 1.7f,
        new Dictionary<string, DaggerfallWeaponClassAttacks> { ["short-blade"] = new(Fast, .5d, Release, 2f) },
        new Dictionary<string, DaggerfallAttackPath>(), []);

    [Fact]
    public void A_tap_starts_fastA_in_its_own_step()
    {
        Stage stage = new();

        stage.Tap();

        Assert.Equal(["start fastA", "motion fastA"], stage.Log);
        Assert.Equal(Fast, stage.Timings["fastA"]);
        Assert.True(stage.Clock.Busy);
        Assert.False(stage.Clock.Free);
    }

    [Fact]
    public void The_impact_fires_exactly_at_the_end_of_the_windup_and_once()
    {
        Stage stage = new();
        stage.Tap();

        stage.Steps(1);
        Assert.Equal(0, stage.Count("impact"));
        stage.Steps(1);
        Assert.Equal(1, stage.Count("impact"));
        stage.Steps(4);
        Assert.Equal(1, stage.Count("impact"));
    }

    [Fact]
    public void A_press_in_recovery_chains_the_next_attack_at_once_and_the_chain_wraps_after_fastD()
    {
        Stage stage = new();
        stage.Tap();
        foreach (string next in new[] { "fastB", "fastC", "fastD", "fastA", "fastB" })
        {
            stage.Steps(4); // Into the recovery.
            Assert.True(stage.Clock.Free);
            stage.Log.Clear();
            stage.Tap();
            Assert.Equal([$"start {next}", $"motion {next}"], stage.Log);
        }
        Assert.Equal(0, stage.Count("rest"));
    }

    [Fact]
    public void A_press_during_the_windup_waits_for_the_recovery()
    {
        Stage stage = new();
        stage.Tap();
        stage.Steps(1);

        stage.Clock.Press();
        stage.Steps(2);
        Assert.Equal(0, stage.Count("start fastB"));
        Assert.Equal(1, stage.Count("impact")); // The first swing still lands first.

        stage.Steps(1);
        Assert.Equal(1, stage.Count("start fastB"));
    }

    [Fact]
    public void A_press_during_the_commit_waits_for_the_recovery()
    {
        Stage stage = new();
        stage.Tap();
        stage.Steps(3);

        stage.Clock.Press();
        Assert.False(stage.Clock.Free);
        stage.Steps(1);

        Assert.Equal(1, stage.Count("start fastB"));
    }

    [Fact]
    public void Several_presses_before_the_recovery_are_one_buffered_attack()
    {
        Stage stage = new();
        stage.Tap();
        stage.Clock.Press();
        stage.Steps(1);
        stage.Clock.Press();
        stage.Steps(3);

        Assert.Equal(1, stage.Count("start fastB"));
        Assert.Equal(0, stage.Count("start fastC"));
    }

    [Fact]
    public void A_swing_run_to_its_end_rests_and_resets_the_chain_to_fastA()
    {
        Stage stage = new();
        stage.Tap();
        stage.Steps(4);
        stage.Tap(); // fastB
        stage.Steps(5);
        Assert.Equal(0, stage.Count("rest"));

        stage.Steps(1);
        Assert.Equal(1, stage.Count("rest"));
        Assert.False(stage.Clock.Busy);
        Assert.True(stage.Clock.Free);

        stage.Log.Clear();
        stage.Tap();
        Assert.Equal(["start fastA", "motion fastA"], stage.Log);
    }

    [Fact]
    public void A_hold_past_the_threshold_charges_holds_and_releases_with_the_impact_only_on_the_release()
    {
        Stage stage = new();
        stage.Down();
        stage.Clock.Press();

        stage.Steps(1);
        Assert.Empty(stage.Log); // Under the threshold nothing is decided yet.
        stage.Steps(1);
        Assert.Equal(["motion powerIn"], stage.Log);
        Assert.Equal(new DaggerfallAttackTiming(.5d, 0d, 0d), stage.Timings["powerIn"]);

        stage.Steps(4);
        Assert.Equal(["motion powerIn", "motion powerHold"], stage.Log);
        Assert.Null(stage.Timings["powerHold"]);

        stage.Steps(40); // A charged attack holds for as long as the button does.
        Assert.Equal(["motion powerIn", "motion powerHold"], stage.Log);

        stage.Up();
        stage.Steps(1);
        Assert.Equal(["motion powerIn", "motion powerHold", "start powerOut", "motion powerOut"], stage.Log);
        Assert.Equal(Release, stage.Timings["powerOut"]);

        stage.Steps(2);
        Assert.Equal(1, stage.Count("impact"));
        stage.Steps(6);
        Assert.Equal(1, stage.Count("impact"));
        Assert.Equal("rest", stage.Log[^1]);
    }

    [Fact]
    public void A_release_during_the_charge_looses_when_the_charge_ends()
    {
        Stage stage = new();
        stage.Down();
        stage.Clock.Press();
        stage.Steps(2);
        stage.Steps(1);

        stage.Up();
        stage.Steps(2);
        Assert.Equal(0, stage.Count("start powerOut"));

        stage.Steps(1);
        Assert.Equal(["motion powerIn", "start powerOut", "motion powerOut"], stage.Log);
    }

    [Fact]
    public void A_click_released_under_the_threshold_is_a_fast_attack_started_on_its_release()
    {
        Stage stage = new();
        stage.Down();
        stage.Clock.Press();
        stage.Steps(1);
        Assert.Empty(stage.Log);

        stage.Up();
        stage.Steps(1);

        Assert.Equal(["start fastA", "motion fastA"], stage.Log);
    }

    [Fact]
    public void A_held_buttons_repeat_presses_do_not_start_a_second_attack()
    {
        Stage stage = new();
        stage.Down();
        stage.Clock.Press();
        stage.Steps(2);
        for (int step = 0; step < 12; step++)
        {
            stage.Clock.Press();
            stage.Steps(1);
        }

        stage.Up();
        stage.Steps(10);

        Assert.Equal(1, stage.Count("motion powerIn"));
        Assert.Equal(1, stage.Count("start powerOut"));
        Assert.Equal(1, stage.Log.Count(entry => entry.StartsWith("start", StringComparison.Ordinal)));
        Assert.False(stage.Clock.Busy);
    }

    [Fact]
    public void A_refused_start_plays_no_motion_and_leaves_the_clock_free_on_the_same_attack()
    {
        Stage stage = new() { Refusing = true };

        stage.Tap();

        Assert.Equal(["start fastA"], stage.Log);
        Assert.True(stage.Clock.Free);
        Assert.False(stage.Clock.Busy);

        stage.Refusing = false;
        stage.Log.Clear();
        stage.Tap();
        Assert.Equal(["start fastA", "motion fastA"], stage.Log);
    }

    [Fact]
    public void A_sweep_that_meets_nothing_runs_the_hit_window_out_and_then_resolves_once()
    {
        Stage stage = new() { MeetsAt = 2d };
        stage.Tap();

        stage.Steps(30);

        Assert.Single(stage.Log, entry => entry == "impact");
        Assert.Equal(1d, stage.Cycles[^1]);
        Assert.Equal(stage.Cycles.Order(), stage.Cycles);
    }

    [Fact]
    public void A_sweep_stops_at_what_it_meets_inside_the_hit_window()
    {
        Stage stage = new() { MeetsAt = .4d };
        stage.Tap();

        stage.Steps(30);

        Assert.Single(stage.Log, entry => entry == "impact");
        Assert.InRange(stage.Cycles[^1], .4d, .9d);
    }

    [Fact]
    public void A_refused_chain_leaves_the_swing_it_would_have_cut_to_finish()
    {
        Stage stage = new();
        stage.Tap();
        stage.Steps(4);
        stage.Refusing = true;
        stage.Log.Clear();

        stage.Tap();

        Assert.Equal(["start fastB"], stage.Log);
        Assert.True(stage.Clock.Busy);
        stage.Steps(10);
        Assert.Equal("rest", stage.Log[^1]);
        Assert.False(stage.Clock.Busy);
    }

    [Fact]
    public void Input_lost_during_a_charge_abandons_it_without_a_blow()
    {
        Stage stage = new();
        stage.Down();
        stage.Clock.Press();
        stage.Steps(4);
        Assert.Contains("motion powerIn", stage.Log);
        stage.Log.Clear();

        stage.Clock.Observe([Input(InputEventKind.Clear, InputEdge.None)], []);
        stage.Steps(20);

        Assert.Equal(["rest"], stage.Log);
        Assert.False(stage.Clock.Busy);
    }

    [Fact]
    public void A_buffered_shot_refused_as_the_last_one_ends_still_returns_the_bow_to_rest()
    {
        Stage stage = new() { Shot = Fast };
        stage.Tap();
        stage.Clock.Press(); // A shot takes no chain, so this waits for its end.
        stage.Refusing = true;

        stage.Steps(6);

        Assert.Equal("rest", stage.Log[^1]);
    }

    [Fact]
    public void Cancel_retires_the_attack_without_its_impact_and_resets_the_chain()
    {
        Stage stage = new();
        stage.Tap();
        stage.Steps(4);
        stage.Tap(); // fastB
        stage.Steps(1);
        int impacts = stage.Count("impact");

        stage.Clock.Cancel();
        Assert.Equal("rest", stage.Log[^1]);
        Assert.False(stage.Clock.Busy);
        stage.Steps(8);
        Assert.Equal(impacts, stage.Count("impact"));

        stage.Log.Clear();
        stage.Tap();
        Assert.Equal(["start fastA", "motion fastA"], stage.Log);
    }

    [Fact]
    public void Cancel_drops_a_waiting_press_and_kick_and_rests_only_a_motion()
    {
        Stage stage = new();
        stage.Down();
        stage.Clock.Press();
        stage.Clock.PressKick();

        stage.Clock.Cancel();
        stage.Up();
        stage.Steps(4);

        Assert.Empty(stage.Log);
    }

    [Fact]
    public void A_bow_plays_its_shot_at_once_whether_or_not_the_button_is_held_and_never_charges()
    {
        Stage stage = new() { Shot = Fast };
        stage.Down();
        stage.Clock.Press();

        stage.Steps(1);
        Assert.Equal(["start shot", "motion shot"], stage.Log);

        stage.Steps(12);
        Assert.Equal(0, stage.Count("motion powerIn"));
        Assert.Equal(1, stage.Count("impact"));
    }

    [Fact]
    public void A_press_in_a_shots_recovery_does_not_chain_and_waits_for_its_end()
    {
        Stage stage = new() { Shot = Fast };
        stage.Tap();
        stage.Steps(4);
        Assert.False(stage.Clock.Free);

        stage.Clock.Press();
        stage.Steps(1);
        Assert.Equal(1, stage.Count("start shot"));

        stage.Steps(1);
        Assert.Equal(2, stage.Count("start shot"));
        Assert.Equal(0, stage.Count("rest")); // One shot runs straight into the next.
        Assert.DoesNotContain(stage.Log, entry => entry.Contains("fast", StringComparison.Ordinal));
    }

    [Fact]
    public void A_kick_works_from_rest_with_its_own_timing_and_impact()
    {
        Stage stage = new();

        stage.Clock.PressKick();
        stage.Steps(1);
        Assert.Equal(["start kick", "motion kick"], stage.Log);
        Assert.Equal(Kick, stage.Timings["kick"]);

        stage.Steps(2);
        Assert.Equal(1, stage.Count("impact"));
        stage.Steps(3);
        Assert.Equal("rest", stage.Log[^1]);
        Assert.False(stage.Clock.Busy);
    }

    [Fact]
    public void A_kick_cuts_a_recovery_at_once_and_waits_out_a_windup()
    {
        Stage stage = new();
        stage.Tap();
        stage.Clock.PressKick();
        stage.Steps(3);
        Assert.Equal(0, stage.Count("start kick"));

        stage.Steps(1);
        Assert.Equal(1, stage.Count("start kick"));
        Assert.Equal(0, stage.Count("rest"));
    }

    [Fact]
    public void A_kick_pressed_while_a_power_attack_is_held_waits_for_its_recovery()
    {
        Stage stage = new();
        stage.Down();
        stage.Clock.Press();
        stage.Steps(6);
        stage.Clock.PressKick();
        stage.Steps(4);
        Assert.Equal(0, stage.Count("start kick"));

        stage.Up();
        stage.Steps(5);
        Assert.Equal(1, stage.Count("start kick"));
        Assert.True(stage.Log.IndexOf("start powerOut") < stage.Log.IndexOf("start kick"));
    }

    [Fact]
    public void Each_motion_is_timed_by_its_weapon_class_and_an_unknown_class_is_a_content_error()
    {
        DaggerfallAttackTuning tuning = DaggerfallAttackTuning.Default;
        DaggerfallWeaponClassAttacks dagger = tuning.For("short-blade");

        Assert.Equal(dagger.Fast, tuning.Timing("fastC", "short-blade"));
        Assert.Equal(new DaggerfallAttackTiming(dagger.PowerCharge, 0d, 0d), tuning.Timing(DaggerfallAttackIds.PowerIn, "short-blade"));
        Assert.Equal(dagger.PowerRelease, tuning.Timing(DaggerfallAttackIds.PowerOut, "short-blade"));
        Assert.Equal(tuning.Kick, tuning.Timing(DaggerfallAttackIds.Kick, "short-blade"));
        Assert.Null(tuning.Timing(DaggerfallAttackIds.PowerHold, "short-blade"));
        Assert.Throws<InvalidOperationException>(() => tuning.For("archery"));
        Assert.Equal(tuning, DaggerfallTuning.Defaults.Attacks with { Classes = tuning.Classes });
        Assert.Equal(tuning.Classes.Keys.Order(), DaggerfallTuning.Defaults.Attacks.Classes.Keys.Order());
        foreach (var (skill, attacks) in tuning.Classes) Assert.Equal(attacks, DaggerfallTuning.Defaults.Attacks.For(skill));
    }

    [Fact]
    public void A_press_buffered_through_the_hit_window_starts_only_after_the_blow_has_resolved()
    {
        Stage stage = new() { MeetsAt = 2d }; // A whiff: the impact comes as the window closes, the step recovery opens.
        stage.Tap();
        stage.Clock.Press();
        for (int step = 0; step < 8 && stage.Count("start fastB") == 0; step++)
        {
            int impacts = stage.Count("impact");
            stage.Steps(1);
            if (stage.Count("impact") > impacts) Assert.Equal(0, stage.Count("start fastB"));
        }
        Assert.Equal(1, stage.Count("impact"));
        Assert.Equal(1, stage.Count("start fastB"));
    }

    [Fact]
    public void The_chain_wraps_at_the_length_the_weapon_skill_reaches()
    {
        Stage stage = new() { Chain = 2 };
        stage.Tap();
        foreach (string next in new[] { "fastB", "fastA", "fastB" })
        {
            stage.Steps(4); // Into the recovery.
            stage.Log.Clear();
            stage.Tap();
            Assert.Equal([$"start {next}", $"motion {next}"], stage.Log);
        }
    }

    [Fact]
    public void What_the_character_scales_an_attacks_time_by_reaches_the_motion()
    {
        Stage stage = new() { Scale = .5d };
        stage.Tap();
        DaggerfallAttackTiming fast = Tuning.For("short-blade").Fast;
        Assert.Equal(new DaggerfallAttackTiming(fast.Windup * .5d, fast.Commit * .5d, fast.Recovery * .5d), stage.Timings["fastA"]);
    }

    [Fact]
    public void A_winded_hold_never_charges_and_falls_to_a_fast_attack_on_release()
    {
        Stage stage = new() { Tired = true };
        stage.Down();
        stage.Clock.Press();
        stage.Steps((int)Math.Ceiling(Tuning.PowerThresholdSeconds * 3d / Step));
        Assert.Empty(stage.Log);
        stage.Up();
        stage.Steps(1);
        Assert.Equal(1, stage.Count("start fastA"));
    }

    /// <summary>The clock over a session that records what it was asked, in order.</summary>
    private sealed class Stage : IDaggerfallPlayerAttacks
    {
        internal Stage() => Clock = new DaggerfallPlayerAttackClock(Tuning, this);

        internal DaggerfallPlayerAttackClock Clock { get; }
        internal List<string> Log { get; } = [];
        internal Dictionary<string, DaggerfallAttackTiming?> Timings { get; } = [];
        internal bool Refusing { get; set; }
        internal DaggerfallAttackTiming? Shot { get; init; }

        internal int Count(string entry) => Log.Count(value => value == entry);

        /// <summary>A press with the button already up: the attack starts in this step.</summary>
        internal void Tap()
        {
            Clock.Press();
            Clock.Step(Step);
        }

        internal void Steps(int count)
        {
            for (int step = 0; step < count; step++) Clock.Step(Step);
        }

        /// <summary>Where in the hit window the sweep meets something; past 1 it meets nothing.</summary>
        internal double MeetsAt { get; set; }
        internal List<double> Cycles { get; } = [];

        internal void Down() => Clock.Observe([Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1F, intent: "attack")], []);

        internal void Up() => Clock.Observe([Input(InputEventKind.MappedDigital, InputEdge.Released, intent: "attack")], []);

        internal int Chain { get; set; } = 4;
        internal bool Tired { get; set; }
        internal double Scale { get; set; } = 1d;

        string IDaggerfallPlayerAttacks.WeaponSkill => "short-blade";
        int IDaggerfallPlayerAttacks.ChainLength => Chain;
        bool IDaggerfallPlayerAttacks.Winded => Tired;
        double IDaggerfallPlayerAttacks.TimeScale(string attack) => Scale;
        DaggerfallAttackTiming? IDaggerfallPlayerAttacks.ShotTiming => Shot;

        bool IDaggerfallPlayerAttacks.Start(string attack)
        {
            Log.Add($"start {attack}");
            return !Refusing;
        }

        bool IDaggerfallPlayerAttacks.Sweep(double cycle)
        {
            Cycles.Add(cycle);
            return cycle >= MeetsAt;
        }

        void IDaggerfallPlayerAttacks.Impact() => Log.Add("impact");

        void IDaggerfallPlayerAttacks.Motion(string attack, DaggerfallAttackTiming? timing)
        {
            Log.Add($"motion {attack}");
            Timings[attack] = timing;
        }

        void IDaggerfallPlayerAttacks.Rest() => Log.Add("rest");
    }
}
