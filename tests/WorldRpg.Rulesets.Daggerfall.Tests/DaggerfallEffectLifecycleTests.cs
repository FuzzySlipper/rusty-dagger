using System.Text.Json;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallEffectLifecycleTests
{
    [Fact]
    public void Applies_daggerfall_like_kind_stack_replace_and_reject_policy()
    {
        using ActorsState actors = Actors();
        DaggerfallEffectLifecycle effects = new(actors, new DaggerfallEffectCatalog(
        [
            Definition("stack", "shared", DaggerfallEffectStacking.Stack, 2),
            Definition("replace", "shared", DaggerfallEffectStacking.Replace, 1),
            Definition("reject", "shared", DaggerfallEffectStacking.Reject, 1),
        ]));
        List<DaggerfallEffectOutcome> outcomes = [];
        effects.Completed += outcomes.Add;

        Assert.Equal(DaggerfallEffectAdmissionOutcome.Started, effects.Start(Request("one", "stack", "spell-a", 5)));
        Assert.Equal(DaggerfallEffectAdmissionOutcome.Started, effects.Start(Request("two", "stack", "spell-b", 5)));
        Assert.Equal(2, effects.Active.Count);

        Assert.Equal(DaggerfallEffectAdmissionOutcome.Replaced, effects.Start(Request("three", "replace", "spell-c", 5)));
        Assert.Equal("three", Assert.Single(effects.Active).Lifecycle.Context.Instance.Value);
        Assert.Equal(DaggerfallEffectAdmissionOutcome.Rejected, effects.Start(Request("four", "reject", "spell-d", 5)));
        Assert.Equal("three", Assert.Single(effects.Active).Lifecycle.Context.Instance.Value);
        Assert.Equal(
            [DaggerfallEffectOutcomeKind.Started, DaggerfallEffectOutcomeKind.Started,
                DaggerfallEffectOutcomeKind.Replaced, DaggerfallEffectOutcomeKind.Rejected],
            outcomes.Select(outcome => outcome.Kind));
    }

    [Fact]
    public void Cancels_a_source_and_removes_its_stat_contribution()
    {
        using ActorsState actors = Actors();
        ActorState target = actors.Get(2);
        DaggerfallEffectLifecycle effects = new(actors, new DaggerfallEffectCatalog(
        [Definition("fortify", "fortify", DaggerfallEffectStacking.Stack, 2,
            effect =>
            {
                StatModifierHandle handle = target.Stats.GetStat(StatId.Parse("health-maximum")).AddModifier(10);
                return [new DelegateActiveEffectContribution(() => target.Stats.GetStat(StatId.Parse("health-maximum")).RemoveModifier(handle))];
            })]));

        Assert.Equal(100d, target.Stats.GetStat(StatId.Parse("health-maximum")).Value);
        _ = effects.Start(Request("fortify-a", "fortify", "amulet", 4));
        _ = effects.Start(Request("fortify-b", "fortify", "amulet", 4));
        Assert.Equal(120d, target.Stats.GetStat(StatId.Parse("health-maximum")).Value);

        Assert.Equal(2, effects.CancelSource("amulet"));
        Assert.Empty(effects.Active);
        Assert.Equal(100d, target.Stats.GetStat(StatId.Parse("health-maximum")).Value);
    }

    [Fact]
    public void Cure_reports_an_ordered_outcome_and_removes_its_contribution_and_round_work()
    {
        using ActorsState actors = Actors();
        ActorState target = actors.Get(2);
        int rounds = 0;
        DaggerfallEffectLifecycle effects = new(actors, new DaggerfallEffectCatalog(
        [Definition("cure", "cure", DaggerfallEffectStacking.Stack, 1,
            effect =>
            {
                Stat stat = effect.Target.Get<StatsComponent>().GetStat(StatId.Parse("health-maximum"));
                StatModifierHandle handle = stat.AddModifier(10);
                return [new DelegateActiveEffectContribution(() => stat.RemoveModifier(handle))];
            },
            _ => rounds++)]));
        List<DaggerfallEffectOutcome> outcomes = [];
        effects.Completed += outcomes.Add;

        _ = effects.Start(Request("cure-instance", "cure", "temple", 3));
        Assert.Equal(110d, target.Stats.GetStat(StatId.Parse("health-maximum")).Value);
        Assert.Equal(1, rounds);

        Assert.True(effects.Cure(EffectInstanceId.Parse("cure-instance")));
        effects.AdvanceOrdinaryRound();

        Assert.Empty(effects.Active);
        Assert.Empty(target.Effects.Effects);
        Assert.Equal(100d, target.Stats.GetStat(StatId.Parse("health-maximum")).Value);
        Assert.Equal(1, rounds);
        Assert.Equal(
            [DaggerfallEffectOutcomeKind.Started, DaggerfallEffectOutcomeKind.Cured],
            outcomes.Select(outcome => outcome.Kind));
        Assert.False(effects.Cure(EffectInstanceId.Parse("cure-instance")));
    }

    [Fact]
    public void Refresh_duration_retains_incumbent_payload_stacks_and_reversible_contribution()
    {
        using ActorsState actors = Actors();
        ActorState target = actors.Get(2);
        DaggerfallEffectDefinition definition = Definition("refresh", "refresh", DaggerfallEffectStacking.RefreshDuration, 0,
            effect =>
            {
                double amount = effect.Context.Settings == "strong" ? 10 : 1;
                Stat stat = effect.Target.Get<StatsComponent>().GetStat(StatId.Parse("health-maximum"));
                StatModifierHandle handle = stat.AddModifier(amount);
                return [new DelegateActiveEffectContribution(() => stat.RemoveModifier(handle))];
            }, maximumStacks: 2);
        DaggerfallEffectLifecycle effects = new(actors, new DaggerfallEffectCatalog([definition]));

        _ = effects.Start(new DaggerfallEffectRequest("first", "refresh", "scroll-a", null, 2, "weak", "magic", null, 1, 5, State()));
        Assert.Equal(101d, target.Stats.GetStat(StatId.Parse("health-maximum")).Value);
        Assert.Equal(DaggerfallEffectAdmissionOutcome.Refreshed,
            effects.Start(new DaggerfallEffectRequest("incoming", "refresh", "scroll-b", null, 2, "strong", "fire", null, 2, 9, State())));

        DaggerfallActiveEffect active = Assert.Single(effects.Active);
        Assert.Equal("first", active.Lifecycle.Context.Instance.Value);
        Assert.Equal("scroll-a", active.Context.Source.Key);
        Assert.Equal("weak", active.Context.Settings);
        Assert.Equal("magic", active.Context.Element);
        Assert.Equal((uint)9, active.Lifecycle.RemainingRounds);
        Assert.Equal((ushort)1, active.Lifecycle.Stacks);
        Assert.Equal(101d, target.Stats.GetStat(StatId.Parse("health-maximum")).Value);
        DaggerfallActiveEffectSave saved = Assert.Single(effects.Capture());
        Assert.Equal(("scroll-a", "weak", "magic", (uint)9, (ushort)1),
            (saved.Source, saved.Settings, saved.Element, saved.RemainingRounds, saved.Stacks));

        Assert.Equal(1, effects.CancelSource("scroll-a"));
        Assert.Equal(100d, target.Stats.GetStat(StatId.Parse("health-maximum")).Value);
    }

    [Fact]
    public void Refuses_an_instance_already_live_on_another_target_before_any_mutation()
    {
        using ActorsState actors = Actors(includeSecondTarget: true);
        int applied = 0;
        DaggerfallEffectLifecycle effects = new(actors, new DaggerfallEffectCatalog(
        [Definition("duplicate", "duplicate", DaggerfallEffectStacking.Stack, 2,
            _ =>
            {
                applied++;
                return [];
            })]));

        _ = effects.Start(Request("shared", "duplicate", "spell", 3));
        Assert.Throws<ArgumentException>(() => effects.Start(new DaggerfallEffectRequest(
            "shared", "duplicate", "spell", null, 3, "classic", "magic", null, 1, 3, State())));

        Assert.Equal(1, applied);
        Assert.Single(effects.Active);
        Assert.Empty(actors.Get(3).Effects.Effects);
    }

    [Fact]
    public void Cancels_target_contributions_before_target_removal()
    {
        using ActorsState actors = Actors();
        ActorState target = actors.Get(2);
        DaggerfallEffectLifecycle effects = new(actors, new DaggerfallEffectCatalog(
        [Definition("target", "target", DaggerfallEffectStacking.Stack, 1,
            effect =>
            {
                Stat stat = effect.Target.Get<StatsComponent>().GetStat(StatId.Parse("health-maximum"));
                StatModifierHandle handle = stat.AddModifier(5);
                return [new DelegateActiveEffectContribution(() => stat.RemoveModifier(handle))];
            })]));

        _ = effects.Start(Request("target", "target", "spell", 3));
        Assert.Equal(105d, target.Stats.GetStat(StatId.Parse("health-maximum")).Value);
        Assert.Equal(1, effects.CancelTarget(2));
        Assert.Equal(100d, target.Stats.GetStat(StatId.Parse("health-maximum")).Value);
        actors.Entities.Destroy(ActorsState.Identity(2));
    }

    [Fact]
    public void Cancels_caster_and_item_references_on_another_target_before_their_identity_is_retired()
    {
        using ActorsState actors = Actors(includeSecondTarget: true);
        int removed = 0;
        DaggerfallEffectLifecycle effects = new(actors, new DaggerfallEffectCatalog(
        [Definition("bound", "bound", DaggerfallEffectStacking.Stack, 3,
            _ => [new DelegateActiveEffectContribution(() => removed++)]) ]));

        _ = effects.Start(new DaggerfallEffectRequest("caster-bound", "bound", "spell", 3, 2, "classic", "magic", null, 1, 5, State()));
        Assert.Equal(1, effects.CancelActorReferences(3));
        Assert.Empty(effects.Active);
        Assert.Equal(1, removed);

        _ = effects.Start(new DaggerfallEffectRequest("item-bound", "bound", "item", null, 2, "classic", "magic", 99, 1, 5, State()));
        Assert.Equal(1, effects.CancelItemReferences(99));
        Assert.Empty(effects.Active);
        Assert.Equal(2, removed);
    }

    [Fact]
    public void Expires_after_initial_and_elapsed_rounds_with_donor_catchup_bound()
    {
        using ActorsState actors = Actors();
        ActorState target = actors.Get(2);
        int rounds = 0;
        DaggerfallEffectLifecycle effects = new(actors, new DaggerfallEffectCatalog(
        [Definition("timer", "timer", DaggerfallEffectStacking.Stack, 1,
            effect =>
            {
                Stat stat = effect.Target.Get<StatsComponent>().GetStat(StatId.Parse("health-maximum"));
                StatModifierHandle handle = stat.AddModifier(5);
                return [new DelegateActiveEffectContribution(() => stat.RemoveModifier(handle))];
            },
            _ => rounds++)]));

        _ = effects.Start(Request("timer", "timer", "spell", DaggerfallEffectLifecycle.MaximumElapsedCatchupRounds + 2));
        Assert.Equal(1, rounds); // Start performs the first magic round.
        Assert.Equal(105d, target.Stats.GetStat(StatId.Parse("health-maximum")).Value);
        Assert.Equal(DaggerfallEffectLifecycle.MaximumElapsedCatchupRounds,
            effects.AdvanceElapsedRounds(10_000));
        Assert.Equal(1 + (int)DaggerfallEffectLifecycle.MaximumElapsedCatchupRounds, rounds);
        Assert.Single(effects.Active);

        effects.AdvanceOrdinaryRound();
        Assert.Empty(effects.Active);
        Assert.Empty(target.Effects.Effects);
        Assert.Equal(100d, target.Stats.GetStat(StatId.Parse("health-maximum")).Value);
        Assert.Equal(2 + (int)DaggerfallEffectLifecycle.MaximumElapsedCatchupRounds, rounds);
    }

    [Fact]
    public void Restores_after_fresh_actor_reconstruction_without_replaying_start_round()
    {
        DaggerfallActiveEffectSave[] saved;
        int originalRounds = 0;
        using (ActorsState originalActors = Actors())
        {
            DaggerfallEffectLifecycle original = new(originalActors, new DaggerfallEffectCatalog([RestoreDefinition(originalActors, () => originalRounds++)]));
            _ = original.Start(Request("restore-instance", "restore", "spell", 4));
            saved = original.Capture();
            Assert.Equal(110d, originalActors.Get(2).Stats.GetStat(StatId.Parse("health-maximum")).Value);
        }

        using ActorsState restoredActors = Actors();
        int restoredRounds = 0;
        DaggerfallEffectLifecycle restored = new(restoredActors, new DaggerfallEffectCatalog([RestoreDefinition(restoredActors, () => restoredRounds++)]));
        restored.Restore(saved);

        Assert.Equal(1, originalRounds);
        Assert.Equal(0, restoredRounds);
        Assert.Equal(110d, restoredActors.Get(2).Stats.GetStat(StatId.Parse("health-maximum")).Value);
        Assert.Equal((uint)3, Assert.Single(restored.Active).Lifecycle.RemainingRounds);
        Assert.Equal("payload", Assert.Single(restored.Active).State.GetProperty("state").GetString());
        Assert.True(restored.Cancel(EffectInstanceId.Parse("restore-instance")));
        Assert.Equal(100d, restoredActors.Get(2).Stats.GetStat(StatId.Parse("health-maximum")).Value);

        static DaggerfallEffectDefinition RestoreDefinition(ActorsState actors, Action magicRound)
        {
            IEnumerable<IActiveEffectContribution> Contribute(DaggerfallActiveEffect effect)
            {
                ActorState target = actors.Get(checked((long)effect.Context.Target.Value));
                StatModifierHandle handle = target.Stats.GetStat(StatId.Parse("health-maximum")).AddModifier(10);
                return [new DelegateActiveEffectContribution(() => target.Stats.GetStat(StatId.Parse("health-maximum")).RemoveModifier(handle))];
            }

            return Definition("restore", "restore", DaggerfallEffectStacking.Stack, 1,
                Contribute, _ => magicRound(), Contribute);
        }

    }

    private static DaggerfallEffectDefinition Definition(string key, string likeKind, DaggerfallEffectStacking stacking,
        ushort maximumInstances, Func<DaggerfallActiveEffect, IEnumerable<IActiveEffectContribution>>? apply = null,
        Action<DaggerfallActiveEffect>? magicRound = null,
        Func<DaggerfallActiveEffect, IEnumerable<IActiveEffectContribution>>? resume = null,
        ushort maximumStacks = 1) => new(key, likeKind, stacking, maximumInstances, maximumStacks, apply, magicRound, resume);

    private static DaggerfallEffectRequest Request(string instance, string effectKey, string source, uint rounds) => new(
        instance, effectKey, source, null, 2, "classic", "magic", null, 1, rounds, State());

    private static JsonElement State()
    {
        using JsonDocument document = JsonDocument.Parse("{\"state\":\"payload\"}");
        return document.RootElement.Clone();
    }

    private static ActorsState Actors(bool includeSecondTarget = false)
    {
        ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health");
        actors.CreateActor(2, new EntityTypeId("target"), Stats(), new ActorPose(new WorldPoint(0, 0, 0), 0f), "health");
        if (includeSecondTarget)
            actors.CreateActor(3, new EntityTypeId("target-two"), Stats(), new ActorPose(new WorldPoint(1, 0, 0), 0f), "health");
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
}
