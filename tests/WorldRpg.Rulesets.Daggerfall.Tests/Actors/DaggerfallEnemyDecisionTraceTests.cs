using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The decision trace's delivery records: what the traced actor's strikes and casts reached beside what they meant.</summary>
public sealed class DaggerfallEnemyDecisionTraceTests
{
    private const long Traced = 40, Player = 1, Bystander = 41;

    private static DaggerfallEnemyDecisionTrace Following()
    {
        DaggerfallEnemyDecisionTrace trace = new();
        trace.Follow(Traced);
        trace.Close([new("target", "select", DaggerfallGateOutcome.Chose, "player", new DaggerfallGateValue("target", Player))],
            EnemyBehaviorState.Attack, 1, 9);
        return trace;
    }

    private static DaggerfallDecisionGate Delivered(DaggerfallEnemyDecisionTrace trace) => Assert.Single(trace.Latest!.Gates);

    private static double Value(DaggerfallDecisionGate gate, string name) => gate.Values.Single(value => value.Name == name).Value;

    [Fact]
    public void A_blow_meant_for_the_player_that_lands_on_a_bystander_says_so()
    {
        DaggerfallEnemyDecisionTrace trace = Following();
        trace.Observe(new EnemyAttackStartedFact(Traced, Player, WillHit: true, 1, 10));
        trace.Observe(new AttackHitFact(Traced, Bystander, 5, 4, 0, EnemyAttack: true, 1, 10));
        DaggerfallDecisionGate gate = Delivered(trace);
        Assert.Equal(("delivery", "strike", DaggerfallGateOutcome.Refused, "hit:bystander"), (gate.Policy, gate.Gate, gate.Outcome, gate.Reason));
        Assert.Equal(Player, Value(gate, "intended"));
        Assert.Equal(Bystander, Value(gate, "reached"));
        Assert.Equal(4, Value(gate, "healthLost"));
        Assert.Equal(10UL, trace.Latest!.FirstStep);
    }

    [Fact]
    public void Hits_on_the_target_whiffs_negations_cover_and_casts_each_record_one_delivery()
    {
        DaggerfallEnemyDecisionTrace trace = Following();
        trace.Observe(new EnemyAttackStartedFact(Traced, Player, true, 1, 10));
        trace.Observe(new AttackHitFact(Traced, Player, 5, 5, 0, true, 1, 10));
        Assert.Equal((DaggerfallGateOutcome.Passed, "hit"), (Delivered(trace).Outcome, Delivered(trace).Reason));
        trace.Observe(new EnemyAttackStartedFact(Traced, Player, false, 1, 20));
        trace.Observe(new AttackWhiffedFact(Traced, Player, true, 1, 20));
        Assert.Equal((DaggerfallGateOutcome.Refused, "whiffed"), (Delivered(trace).Outcome, Delivered(trace).Reason));
        trace.Observe(new AttackNegatedFact(Traced, Player, HarmNegation.Deflected, true, 1, 30));
        Assert.StartsWith("negated-", Delivered(trace).Reason);
        trace.Observe(new RangedShotBlockedFact(Traced, Player, 1, 40));
        Assert.Equal("blocked-by-cover", Delivered(trace).Reason);
    }

    /// <summary>A traced decision whose spell gate released a cast meant for <paramref name="intended"/>.</summary>
    private static void Casts(DaggerfallEnemyDecisionTrace trace, long sequence, long intended, ulong step) =>
        trace.Close([new("target", "select", DaggerfallGateOutcome.Chose, "player", new DaggerfallGateValue("target", Player)),
            new("spell", "cast", DaggerfallGateOutcome.Passed, "Released", new DaggerfallGateValue("intended", intended), new DaggerfallGateValue("sequence", sequence))],
            EnemyBehaviorState.Attack, 1, step);

    private static SpellCastFact Cast(long sequence, DaggerfallCastOutcome outcome, params long[] targets) =>
        new(outcome, sequence, Traced, "spell", 10, [.. targets.Select((target, index) => new DaggerfallCastEffectResult(index, target, outcome))]);

    [Fact]
    public void A_self_cast_reaches_the_target_it_meant()
    {
        DaggerfallEnemyDecisionTrace trace = Following();
        Casts(trace, sequence: 7, intended: Traced, step: 10);
        trace.Observe(Cast(7, DaggerfallCastOutcome.DeliveryCompleted, Traced));
        DaggerfallDecisionGate cast = Delivered(trace);
        Assert.Equal(("cast", DaggerfallGateOutcome.Passed, "DeliveryCompleted"), (cast.Gate, cast.Outcome, cast.Reason));
        Assert.Equal(Traced, Value(cast, "intended"));
        Assert.Equal(Traced, Value(cast, "reached"));
    }

    [Fact]
    public void A_cast_keeps_its_own_target_through_a_later_decision_and_says_when_it_hit_a_bystander()
    {
        DaggerfallEnemyDecisionTrace trace = Following();
        Casts(trace, sequence: 8, intended: Player, step: 10);
        // A later decision casts at itself; the earlier missile still lands as the earlier cast.
        Casts(trace, sequence: 9, intended: Traced, step: 11);
        trace.Observe(Cast(8, DaggerfallCastOutcome.DeliveryCompleted, Bystander));
        DaggerfallDecisionGate cast = Delivered(trace);
        Assert.Equal(DaggerfallGateOutcome.Refused, cast.Outcome);
        Assert.EndsWith(":bystander", cast.Reason);
        Assert.Equal((Player, Bystander, 1d), (Value(cast, "intended"), Value(cast, "reached"), Value(cast, "targetsReached")));
        // A cast the trace never saw decided has no intended target to compare.
        trace.Observe(Cast(99, DaggerfallCastOutcome.DeliveryCompleted, Bystander));
        Assert.Equal(0, Value(Delivered(trace), "intended"));
    }

    [Fact]
    public void Another_actors_blows_and_an_untraced_trace_record_nothing()
    {
        DaggerfallEnemyDecisionTrace trace = Following();
        int before = trace.Recent.Count;
        trace.Observe(new AttackHitFact(Bystander, Player, 5, 5, 0, true, 1, 10));
        Assert.Equal(before, trace.Recent.Count);
        trace.Follow(null);
        trace.Observe(new AttackHitFact(Traced, Player, 5, 5, 0, true, 1, 10));
        Assert.Empty(trace.Recent);
    }
}
