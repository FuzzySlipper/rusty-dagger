using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Facts;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class AttackExecutionTests
{
    [Fact]
    public void Delayed_attack_interruption_and_stale_or_duplicate_impacts_never_apply_twice()
    {
        using ActorsState actors = Actors();
        RecordingRules rules = new();
        AttackExecution<TestFact> execution = new(actors, rules);
        FactBuffer<TestFact> facts = new();
        AttackRequest first = new(2, 3, 7, 10, 1d, Delayed: true);

        Assert.True(execution.Start(first, facts));
        execution.Interrupt(2, 7);
        execution.ApplyImpacts([new AttackImpactNotice(2, 3, 7, 10, Expired: false)], 7, facts);
        Assert.Empty(rules.Applied);

        AttackRequest second = first with { Generation = 8, SimulationStep = 11 };
        Assert.True(execution.Start(second, facts));
        execution.ApplyImpacts([new AttackImpactNotice(2, 3, 7, 10, Expired: false)], 8, facts);
        execution.ApplyImpacts([new AttackImpactNotice(2, 3, 8, 11, Expired: false)], 8, facts);
        execution.ApplyImpacts([new AttackImpactNotice(2, 3, 8, 11, Expired: false)], 8, facts);

        Assert.Equal([second], rules.Applied);
    }

    [Fact]
    public void A_ruleset_can_defer_a_released_impact_until_its_own_delivery_arrives()
    {
        using ActorsState actors = Actors();
        RecordingRules rules = new();
        List<DeferredAttackImpact> deferred = [];
        AttackExecution<TestFact> execution = new(actors, rules, (impact, _) =>
        {
            deferred.Add(impact);
            return true;
        });
        FactBuffer<TestFact> facts = new();
        AttackRequest request = new(2, 3, 7, 10, 1d, Delayed: true);

        Assert.True(execution.Start(request, facts));
        execution.ApplyImpacts([new AttackImpactNotice(2, 3, 7, 10, Expired: false)], 7, facts);
        DeferredAttackImpact released = Assert.Single(deferred);
        Assert.Equal(request, released.Request);
        Assert.Empty(rules.Applied);

        execution.ApplyDeferredImpact(released, facts);
        Assert.Equal([request], rules.Applied);
    }

    [Fact]
    public void Cooldown_capture_restore_reconstructs_readiness_on_the_next_timeline()
    {
        using ActorsState sourceActors = Actors();
        RecordingRules sourceRules = new(cooldown: 3d);
        AttackExecution<TestFact> source = new(sourceActors, sourceRules);
        FactBuffer<TestFact> facts = new();
        AttackRequest request = new(2, 3, 4, 10, 1d, Delayed: false);
        Assert.True(source.Start(request, facts));
        AttackCooldown saved = Assert.Single(source.CaptureCooldowns(4, 11));
        Assert.Equal(2UL, saved.RemainingSteps);

        using ActorsState restoredActors = Actors();
        AttackExecution<TestFact> restored = new(restoredActors, new RecordingRules(cooldown: 3d));
        restored.RestoreCooldowns([saved]);
        restored.ObserveTimeline(9, 20);

        Assert.False(restored.IsReady(2, 9, 21));
        Assert.True(restored.IsReady(2, 9, 22));
    }

    [Fact]
    public void Immediate_attack_applies_after_start_and_refuses_defeated_targets()
    {
        using ActorsState actors = Actors();
        RecordingRules rules = new();
        AttackExecution<TestFact> execution = new(actors, rules);
        FactBuffer<TestFact> facts = new();
        AttackRequest request = new(2, 3, 1, 1, 1d, Delayed: false);

        Assert.True(execution.Start(request, facts));
        Assert.Equal([request], rules.StartedRequests);
        Assert.Equal([request], rules.Applied);
        actors.Get(3).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);

        Assert.False(execution.Start(request with { SimulationStep = 10 }, facts));
        Assert.Equal([AttackRefusal.TargetDefeated], rules.Refusals);
    }

    private static ActorsState Actors()
    {
        ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health");
        actors.CreateActor(2, new EntityTypeId("attacker"), Stats(), new ActorPose(new WorldRpg.Kit.Controls.WorldPoint(0, 0, 0), 0), "health");
        actors.CreateActor(3, new EntityTypeId("target"), Stats(), new ActorPose(new WorldRpg.Kit.Controls.WorldPoint(1, 0, 0), 0), "health");
        return actors;
    }

    private static StatsComponent Stats()
    {
        Stat maximum = new(100);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse("health-maximum"), maximum);
        stats.AddTrack(TrackId.Parse("health"), new Track(maximum, 100));
        return stats;
    }

    private sealed record TestFact : IWorldRpgFact;

    private sealed class RecordingRules(double cooldown = 2d) : IAttackRules<TestFact>
    {
        public List<AttackRequest> StartedRequests { get; } = [];
        public List<AttackRequest> Applied { get; } = [];
        public List<AttackRefusal> Refusals { get; } = [];
        public bool TryPrepare(AttackRequest request, FactBuffer<TestFact> facts, out PreparedAttack attack)
        {
            attack = new(cooldown, new AttackOutcome(true, true, 0, 10, 1, 100));
            return true;
        }
        public void Refused(AttackRefusal reason, FactBuffer<TestFact> facts) => Refusals.Add(reason);
        public void Started(AttackRequest request, PreparedAttack attack, FactBuffer<TestFact> facts) => StartedRequests.Add(request);
        public void Apply(AttackRequest request, PreparedAttack attack, FactBuffer<TestFact> facts) => Applied.Add(request);
    }
}
