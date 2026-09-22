using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class ActiveEffectLifecycleTests
{
    [Fact]
    public void Applies_replaces_cancels_and_cleans_reversible_contributions()
    {
        EffectsComponent component = new();
        ActiveEffectLifecycle effects = new(component);
        bool firstRemoved = false;
        bool secondRemoved = false;

        ActiveEffectContext first = Context("first", "spell-a");
        _ = effects.Admit(Definition("ward", EffectStackingPolicy.IndependentByProvenance, 2), ActiveEffectAdmissionKind.Apply,
            first, Provenance(first), 1, 3, [new DelegateActiveEffectContribution(() => firstRemoved = true)]);
        ActiveEffectContext replacement = Context("second", "spell-b");
        ActiveEffectLifecycleReceipt receipt = effects.Admit(Definition("ward", EffectStackingPolicy.Replace, 1), ActiveEffectAdmissionKind.Replace,
            replacement, Provenance(replacement), 1, 2, [new DelegateActiveEffectContribution(() => secondRemoved = true)]);

        Assert.True(firstRemoved);
        Assert.False(secondRemoved);
        Assert.Equal("second", Assert.Single(effects.Active).Context.Instance.Value);
        Assert.Equal("second", Assert.Single(component.Effects).Instance.Value);
        Assert.Equal("first", Assert.Single(receipt.Removed).Context.Instance.Value);

        Assert.Single(effects.CancelSource(new ActiveEffectSource("spell-b")));
        Assert.True(secondRemoved);
        Assert.Empty(effects.Active);
        Assert.Empty(component.Effects);
    }

    [Fact]
    public void Counts_initial_and_elapsed_rounds_once_then_expires()
    {
        ActiveEffectLifecycle effects = new(new EffectsComponent());
        ActiveEffectContext context = Context("finite", "potion");
        _ = effects.Admit(Definition("finite", EffectStackingPolicy.IndependentByProvenance, 1), ActiveEffectAdmissionKind.Apply,
            context, Provenance(context), 1, 2);
        int rounds = 0;

        Assert.Null(effects.AdvanceInitialMagicRound(context.Instance, _ => rounds++));
        Assert.Equal(1, rounds);
        Assert.Equal((uint)1, Assert.Single(effects.Active).RemainingRounds);

        ActiveEffectLifecycleReceipt expired = Assert.Single(effects.AdvanceMagicRounds(1, _ => rounds++));
        Assert.Equal(2, rounds);
        Assert.Equal(ActiveEffectEndReason.Expired, expired.EndReason);
        Assert.Empty(effects.Active);
    }

    [Fact]
    public void Compiled_policy_can_expire_after_its_current_round_payload()
    {
        ActiveEffectLifecycle effects = new(new EffectsComponent());
        ActiveEffectContext context = Context("complete", "disease");
        _ = effects.Admit(Definition("disease", EffectStackingPolicy.IndependentByProvenance, 1), ActiveEffectAdmissionKind.Apply,
            context, Provenance(context), 1, null);

        ActiveEffectLifecycleReceipt expired = Assert.Single(effects.AdvanceMagicRound(state =>
        {
            Assert.Equal(context.Instance, state.Context.Instance);
            effects.ExpireAfterCurrentRound(state.Context.Instance);
        }));

        Assert.Equal(ActiveEffectEndReason.Expired, expired.EndReason);
        Assert.Empty(effects.Active);
    }

    [Fact]
    public void Refresh_duration_keeps_original_contribution_and_provenance()
    {
        EffectsComponent component = new();
        ActiveEffectLifecycle effects = new(component);
        int removals = 0;
        ActiveEffectContext original = Context("refresh", "scroll-a");
        _ = effects.Admit(Definition("refresh", EffectStackingPolicy.Refresh, 0), ActiveEffectAdmissionKind.Apply,
            original, Provenance(original), 1, 3,
            [new DelegateActiveEffectContribution(() => removals++)]);

        ActiveEffectState refreshed = effects.RefreshDuration(EffectInstanceId.Parse("refresh"), 9);

        Assert.Same(original, refreshed.Context);
        Assert.Equal((ushort)1, refreshed.Stacks);
        Assert.Equal((uint)9, refreshed.RemainingRounds);
        Assert.Equal(0, removals);
        Assert.True(effects.Cancel(EffectInstanceId.Parse("refresh")).Removed.Single() == refreshed);
        Assert.Equal(1, removals);
    }

    [Fact]
    public void Dispose_attempts_every_cleanup_when_one_contribution_fails()
    {
        ActiveEffectLifecycle effects = new(new EffectsComponent());
        bool secondRemoved = false;
        ActiveEffectContext first = Context("a-failing", "effect-a");
        ActiveEffectContext second = Context("b-remaining", "effect-b");
        _ = effects.Admit(Definition("dispose", EffectStackingPolicy.IndependentByProvenance, 2), ActiveEffectAdmissionKind.Apply,
            first, Provenance(first), 1, null, [new DelegateActiveEffectContribution(() => throw new InvalidOperationException("first cleanup"))]);
        _ = effects.Admit(Definition("dispose", EffectStackingPolicy.IndependentByProvenance, 2), ActiveEffectAdmissionKind.Apply,
            second, Provenance(second), 1, null, [new DelegateActiveEffectContribution(() => secondRemoved = true)]);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => effects.Dispose());

        Assert.Equal("first cleanup", failure.Message);
        Assert.True(secondRemoved);
        Assert.Empty(effects.Active);
    }

    [Fact]
    public void Dispose_cancels_every_entry_before_its_component_owner_is_released()
    {
        ActiveEffectLifecycle effects = new(new EffectsComponent());
        bool removed = false;
        ActiveEffectContext context = Context("shutdown", "effect");
        _ = effects.Admit(Definition("shutdown", EffectStackingPolicy.IndependentByProvenance, 1), ActiveEffectAdmissionKind.Apply,
            context, Provenance(context), 1, null, [new DelegateActiveEffectContribution(() => removed = true)]);

        effects.Dispose();

        Assert.True(removed);
        Assert.Empty(effects.Active);
    }

    private static EffectDefinition Definition(string group, EffectStackingPolicy stacking, ushort maximumInstances) => new(
        EffectDefinitionId.Parse($"test.{group}.{stacking}"),
        StackingGroupId.Parse($"test.{group}"),
        stacking,
        maximumInstances,
        1,
        [SourceDefinitionId.Parse("test.source")]);

    private static ActiveEffectContext Context(string instance, string source) => new(
        EffectInstanceId.Parse(instance),
        new ActiveEffectSource(source),
        null,
        new DurableIdentityReference(DurableIdentityKind.Actor, 17),
        "settings",
        "magic",
        null);

    private static MechanicsSourceIdentity Provenance(ActiveEffectContext context) => new EffectSourceIdentity(
        null,
        context.Instance,
        1,
        SourceDefinitionId.Parse($"test.{context.Source.Key}"));
}
