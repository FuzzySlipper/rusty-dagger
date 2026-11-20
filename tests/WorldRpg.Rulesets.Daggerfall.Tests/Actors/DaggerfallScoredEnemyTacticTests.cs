using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The scored tactic without the session: what an intelligent enemy and a beast choose from what they carry, how they
/// weigh it, and the flee that holds and then leaves the target alone. Keyed noise draws its minimum here, so each
/// choice is the plain score.
/// </summary>
public sealed class DaggerfallScoredEnemyTacticTests : IDisposable
{
    private const long Actor = 42, Player = 1;
    private static readonly DaggerfallEnemyTacticTuning Tuning = DaggerfallEnemyTacticTuning.Default;
    private static readonly DaggerfallContactEngagement Engagement = new(DaggerfallContactTuning.Default);
    private static readonly DaggerfallEnemyAttackOption Bow = new("archer-shot", 51.2d, 6d, true, 5);
    private static readonly DaggerfallEnemyAttackOption Blade = new("enemy-class-equipped-melee", 2.25d, 0d, false, null);
    private static readonly DaggerfallEnemyAttackOption Bite = new("bear-bite", 2.25d, 0d, false, null);
    private static readonly PursuitPolicy Movement = new(ActorNavigationMode.Ground, CanRetreat: true, CanStrafe: true, 2.5f, 1.5f, EmitTargetLost: true);

    private readonly ActorsState _actors = new();
    private readonly ActorState _actor;
    private ulong _step;

    public DaggerfallScoredEnemyTacticTests()
    {
        StatsComponent stats = new();
        stats.AddTrack(TrackId.Parse("health"), new Track(new Stat(100), 100));
        stats.AddTrack(TrackId.Parse("magicka"), new Track(new Stat(50), 50));
        _actor = _actors.CreateActor(Actor, new EntityTypeId("enemy"), stats, new ActorPose(new WorldPoint(0, 0, 0), 0), "health");
        _actor.Actor.Add(new DaggerfallEnemyPerceptionMemory());
        _actor.Actor.Add(new PursuitMemoryComponent());
    }

    public void Dispose() => _actors.Dispose();

    private static DaggerfallScoredEnemyTactic Tactic(DaggerfallScoredPreset preset, IReadOnlyList<DaggerfallEnemyAttackOption> attacks,
        IReadOnlyList<DaggerfallEnemySpellOption>? spells = null, DaggerfallTacticTendencies? tendencies = null) =>
        new(preset, _ => attacks, (_, _) => spells ?? [],
            (actor, key, target) => new DaggerfallEnemySpellAttempt(actor.DurableId, key, DaggerfallSpellTarget.SingleTargetAtRange, actor.Position,
                target.Position, Vector3.UnitZ, target.DurableId),
            _ => tendencies, Engagement, DaggerfallPursuitManeuverTuning.Default, Tuning, RandomMinimum.Create());

    /// <summary>One step's decision at a target straight ahead; the gates the tactic weighed come back beside it.</summary>
    private (PursuitPolicy Policy, DaggerfallEnemyActionChoice Choice, List<DaggerfallDecisionGate> Gates) Step(IDaggerfallEnemyTactic tactic,
        double distance, bool lane = true, double health = 1d, double targetHealth = 1d, int allies = 0, long minute = 100,
        DaggerfallEnemyMotorContext motor = default, bool actionOpen = true)
    {
        _step++;
        _actor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(health * 100, clamp: true);
        PursuitTarget target = new(Player, WorldPoint.From(_actor.Position.ToVector() + new Vector3(0, 0, (float)distance)));
        List<DaggerfallDecisionGate> gates = [];
        PursuitPolicy policy = tactic.Move(new(_actor, target, new PursuitObservation(true, distance, 2.25d), null, null, motor with { Level = 5, TargetLevel = 5 },
            Movement, true, new Lazy<bool>(() => lane), 1, _step, 1f / 60f)
        {
            Health = health,
            TargetHealth = targetHealth,
            Allies = [.. Enumerable.Repeat(_actor.Position, allies)],
            GameMinute = minute,
        }, gates);
        DaggerfallEnemyActionChoice choice = tactic.Act(new(_actor, target, null, null, 0d, actionOpen, new Lazy<bool>(() => lane), 1, _step, 1f / 60f), gates);
        return (policy.Validate(), choice, gates);
    }

    private static DaggerfallDecisionGate Option(List<DaggerfallDecisionGate> gates, string name) =>
        Assert.Single(gates, gate => gate.Policy == DaggerfallScoredEnemyTactic.Policy && gate.Gate == name);

    private static string Chosen(List<DaggerfallDecisionGate> gates) =>
        Assert.Single(gates, gate => gate.Policy == DaggerfallScoredEnemyTactic.Policy && gate.Gate != "intent" && gate.Outcome == DaggerfallGateOutcome.Chose).Gate;

    [Fact]
    public void A_fighter_with_a_bow_and_a_blade_shoots_at_range_and_strikes_with_the_blade_close_in_or_out_of_arrows()
    {
        var shot = Step(Tactic(DaggerfallScoredPreset.Intelligent, [Bow, Blade]), 15d);
        Assert.Equal("strike:archer-shot", Chosen(shot.Gates));
        Assert.Equal(new DaggerfallEnemyActionChoice(null, true, "archer-shot"), shot.Choice);
        Assert.Equal(51.2d, shot.Policy.AttackReach);
        // The blade is still on the menu at range: closing in with it is worth less than a shot, not forgotten.
        Assert.Equal("outscored", Option(shot.Gates, "strike:enemy-class-equipped-melee").Reason);

        var close = Step(Tactic(DaggerfallScoredPreset.Intelligent, [Bow, Blade]), 1.5d);
        Assert.Equal("too-close", Option(close.Gates, "strike:archer-shot").Reason);
        Assert.Equal(new DaggerfallEnemyActionChoice(null, true, "enemy-class-equipped-melee"), close.Choice);

        var empty = Step(Tactic(DaggerfallScoredPreset.Intelligent, [Bow with { Ammunition = 0 }, Blade]), 15d);
        Assert.Equal("no-arrows", Option(empty.Gates, "strike:archer-shot").Reason);
        Assert.Equal("strike:enemy-class-equipped-melee", Chosen(empty.Gates));
        Assert.Equal(PursuitState.Chase, empty.Policy.Maneuver);
        Assert.Equal(Engagement.EngageReach(Blade.Reach, false), empty.Policy.AttackReach);
    }

    [Theory]
    [InlineData(.8d, "reposition", PursuitState.Strafe)]
    [InlineData(.3d, "strike:enemy-class-equipped-melee", PursuitState.Chase)]
    public void A_blocked_shot_is_held_for_a_new_lane_by_a_disciplined_fighter_and_given_up_for_the_blade_by_an_undisciplined_one(
        double discipline, string chosen, PursuitState maneuver)
    {
        var blocked = Step(Tactic(DaggerfallScoredPreset.Intelligent, [Bow, Blade], tendencies: new(.5d, discipline)), 15d, lane: false);

        Assert.Equal("lane-blocked", Option(blocked.Gates, "strike:archer-shot").Reason);
        Assert.Equal(chosen, Chosen(blocked.Gates));
        Assert.Equal(maneuver, blocked.Policy.Maneuver);
        Assert.Contains(new DaggerfallGateValue("discipline", discipline), Option(blocked.Gates, "reposition").Values);
        // Moving for a lane takes no action this step.
        if (chosen == "reposition") Assert.Equal(new DaggerfallEnemyActionChoice(null, false), blocked.Choice);
    }

    [Fact]
    public void Without_a_profile_an_intelligent_enemy_weighs_the_tuned_tendencies()
    {
        var blocked = Step(Tactic(DaggerfallScoredPreset.Intelligent, [Bow, Blade]), 15d, lane: false);

        Assert.Contains(new DaggerfallGateValue("discipline", Tuning.Intelligent.Discipline), Option(blocked.Gates, "reposition").Values);
    }

    [Fact]
    public void A_caster_casts_an_affordable_spell_then_waits_its_interval_and_cannot_cast_what_it_cannot_pay_for()
    {
        DaggerfallEnemySpellOption bolt = new("spell.bolt", DaggerfallSpellTarget.SingleTargetAtRange, 20, false);
        DaggerfallScoredEnemyTactic tactic = Tactic(DaggerfallScoredPreset.Intelligent, [Bow, Blade], [bolt]);

        var cast = Step(tactic, 15d);
        Assert.Equal("spell:spell.bolt", Chosen(cast.Gates));
        Assert.Equal("spell.bolt", cast.Choice.Spell?.SpellKey);
        Assert.False(cast.Choice.Strike);
        Assert.False(cast.Policy.AllowAttack);
        tactic.Admitted(_actor, cast.Choice, DaggerfallEnemyAdmission.Cast);

        // Between spells the bow is its best use of the time.
        var waiting = Step(tactic, 15d);
        Assert.Equal("cast-interval", Option(waiting.Gates, "spell:spell.bolt").Reason);
        Assert.Equal("strike:archer-shot", Chosen(waiting.Gates));

        _actor.Stats.GetTrack(TrackId.Parse("magicka")).SetCurrent(10, clamp: true);
        _actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Tactic.CastCooldown = 0f;
        Assert.Equal("magicka", Option(Step(tactic, 15d).Gates, "spell:spell.bolt").Reason);
    }

    [Fact]
    public void A_spell_on_the_target_already_or_out_of_its_band_is_not_cast()
    {
        DaggerfallScoredEnemyTactic tactic = Tactic(DaggerfallScoredPreset.Intelligent, [Blade],
            [new("spell.held", DaggerfallSpellTarget.SingleTargetAtRange, 5, true), new("spell.touch", DaggerfallSpellTarget.ByTouch, 5, false)]);

        var far = Step(tactic, 15d);
        Assert.Equal("already-on-target", Option(far.Gates, "spell:spell.held").Reason);
        Assert.Equal("out-of-touch", Option(far.Gates, "spell:spell.touch").Reason);
        var touching = Step(tactic, 1.5d);
        Assert.Equal("spell:spell.touch", Chosen(touching.Gates));
    }

    [Fact]
    public void A_hurt_enemy_flees_by_navigation_then_leaves_its_target_alone_until_the_time_passes_or_it_is_struck()
    {
        DaggerfallScoredEnemyTactic tactic = Tactic(DaggerfallScoredPreset.Intelligent, [Blade], tendencies: new(.5d, .5d));
        DaggerfallEnemyPerceptionMemory memory = _actor.Actor.Get<DaggerfallEnemyPerceptionMemory>();

        var flight = Step(tactic, 2d, health: .2d);
        Assert.Equal("flee", Chosen(flight.Gates));
        Assert.Equal(PursuitState.Flee, flight.Policy.Maneuver);
        Assert.False(flight.Policy.AllowAttack);
        Assert.Equal(new DaggerfallEnemyActionChoice(null, false), flight.Choice);
        WorldPoint destination = flight.Policy.Destination!.Value;
        Assert.Equal(PursuitFlee.Destination(_actor.Position, new(0, 0, 2), Tuning.Flee.Distance, 0), destination);

        // Committed: health alone does not end it, and the scoring is not asked again while it runs.
        var running = Step(tactic, 4d, health: 1d);
        Assert.Equal(PursuitState.Flee, running.Policy.Maneuver);
        Assert.DoesNotContain(running.Gates, gate => gate.Gate == "flee");

        _actor.ApplyPose(new(destination, 0));
        var arrived = Step(tactic, 17d, minute: 100);
        Assert.Contains(arrived.Gates, gate => gate is { Gate: "intent", Outcome: DaggerfallGateOutcome.Passed, Reason: "flee:arrived" });
        Assert.Equal(PursuitState.Idle, arrived.Policy.Maneuver);
        Assert.False(arrived.Policy.TargetObserved);

        var alone = Step(tactic, 3d, minute: 100 + Tuning.Flee.DisengageMinutes - 1);
        Assert.Contains(alone.Gates, gate => gate is { Gate: "intent", Reason: "leave-alone" });
        Assert.Equal((PursuitState.Idle, false), (alone.Policy.Maneuver, alone.Policy.AllowAttack));
        Assert.Equal("strike:enemy-class-equipped-melee", Chosen(Step(tactic, 3d, minute: 100 + Tuning.Flee.DisengageMinutes).Gates));

        memory.DisengagedFrom = Player;
        memory.DisengagedUntilMinute = 1000;
        memory.Reengage();
        Assert.Equal("strike:enemy-class-equipped-melee", Chosen(Step(tactic, 3d, minute: 101).Gates));
    }

    [Fact]
    public void A_flee_that_runs_out_of_time_also_leaves_the_target_alone_and_each_flight_makes_the_next_less_likely()
    {
        DaggerfallScoredEnemyTactic tactic = Tactic(DaggerfallScoredPreset.Intelligent, [Blade], tendencies: new(.5d, .5d));
        double first = Option(Step(tactic, 2d, health: .2d).Gates, "flee").Values[0].Value;
        for (int frame = 0; frame < (int)(Tuning.Flee.Seconds * 60f) + 1; frame++) Step(tactic, 2d);
        Assert.Equal(1, _actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Tactic.Fled);
        Assert.True(_actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().LeavesAlone(Player, 100));

        double second = Option(Step(tactic, 2d, health: .2d, minute: 100 + Tuning.Flee.DisengageMinutes).Gates, "flee").Values[0].Value;
        Assert.Equal(first / 2d, second, 6);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, false)]
    public void Allies_fighting_beside_it_hold_a_hurt_enemy_in_the_fight(int allies, bool flees)
    {
        var step = Step(Tactic(DaggerfallScoredPreset.Intelligent, [Blade], tendencies: new(.5d, .5d)), 2d, health: .25d, allies: allies);

        Assert.Equal(flees ? "flee" : "strike:enemy-class-equipped-melee", Chosen(step.Gates));
        Assert.Contains(new DaggerfallGateValue("allies", allies), Option(step.Gates, "flee").Values);
    }

    [Fact]
    public void A_fearless_enemy_never_flees()
    {
        var step = Step(Tactic(DaggerfallScoredPreset.Intelligent, [Blade], tendencies: new(1d, .5d)), 2d, health: .01d);

        Assert.Equal("steady", Option(step.Gates, "flee").Reason);
    }

    [Fact]
    public void A_beast_bites_in_reach_lunges_at_a_gap_circles_a_body_in_its_way_and_flees_hurt_by_its_fixed_courage()
    {
        DaggerfallScoredEnemyTactic beast = Tactic(DaggerfallScoredPreset.Beast, [Bite]);

        var bite = Step(beast, 1.5d);
        Assert.Equal(new DaggerfallEnemyActionChoice(null, true, "bear-bite"), bite.Choice);
        Assert.DoesNotContain(bite.Gates, gate => gate.Gate is "reposition" || gate.Gate.StartsWith("spell:", StringComparison.Ordinal));

        var noGap = Step(beast, 4d);
        Assert.Equal("no-gap", Option(noGap.Gates, "lunge").Reason);
        var lunge = Step(beast, 4d, motor: new(5, 5, false, TargetBackTurned: true, false));
        Assert.Equal("lunge", Chosen(lunge.Gates));
        Assert.Equal((PursuitState.Chase, Tuning.Beast.LungeSpeedMultiplier, true), (lunge.Policy.Maneuver, lunge.Policy.SpeedMultiplier, lunge.Policy.AllowAttack));
        Assert.Equal("bear-bite", lunge.Choice.Attack);

        var circle = Step(beast, 6d, lane: false);
        Assert.Equal("circle", Chosen(circle.Gates));
        Assert.Equal(PursuitState.Strafe, circle.Policy.Maneuver);

        // Its courage is the preset's, whatever a record might say: below its threshold it breaks off.
        double threshold = Tuning.Flee.Health * (1d - Tuning.Beast.Tendencies.Courage);
        Assert.Equal("flee", Chosen(Step(beast, 1.5d, health: threshold - .05d).Gates));
        Assert.False(beast.WeighsTendencies);
    }

    [Fact]
    public void A_choice_made_in_an_earlier_step_or_with_the_door_closed_takes_no_action()
    {
        DaggerfallScoredEnemyTactic tactic = Tactic(DaggerfallScoredPreset.Intelligent, [Blade]);

        var closed = Step(tactic, 1.5d, actionOpen: false);
        Assert.Equal(new DaggerfallEnemyActionChoice(null, false), closed.Choice);
        Assert.Contains(closed.Gates, gate => gate is { Gate: "action", Reason: "action-taken-this-update" });

        PursuitTarget target = new(Player, new WorldPoint(0, 0, 1.5f));
        Assert.Equal(new DaggerfallEnemyActionChoice(null, false),
            tactic.Act(new(_actor, target, null, null, 0d, true, new Lazy<bool>(() => true), 1, _step + 1, 1f / 60f), null));
    }
}
