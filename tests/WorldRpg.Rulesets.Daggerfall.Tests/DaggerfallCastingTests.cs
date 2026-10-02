using System.Text.Json;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Effects;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCastingTests
{
    [Fact]
    public void Pending_cast_rebases_its_origin_without_changing_direction_or_redelivering()
    {
        using Harness h = new(range: 3);
        h.Casting.Ready(1, "spell");
        var bundle = Assert.IsType<DaggerfallLiveSpell>(h.Casting.Release(1, true,
            new System.Numerics.Vector3(1001, 2, 1003), System.Numerics.Vector3.UnitZ).Bundle);
        h.Casting.Rebase(new System.Numerics.Vector3(-1000, 0, -1000));
        Assert.Equal(new System.Numerics.Vector3(1, 2, 3), bundle.ReleaseOrigin);
        Assert.Equal(System.Numerics.Vector3.UnitZ, bundle.ReleaseDirection);
        Assert.False(bundle.Delivered);
        h.Casting.Deliver(bundle, [2]);
        Assert.True(bundle.Delivered);
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, h.Casting.Deliver(bundle, [2]).Outcome);
    }

    [Fact]
    public void Actual_release_charges_latched_skills_once_and_records_each_effect_without_cancel_or_replay_use()
    {
        using Harness h = new(range: 0, count: 2);
        Assert.Equal(DaggerfallCastOutcome.Ready, h.Casting.Ready(1, "spell").Outcome);
        Assert.Empty(h.Uses);
        h.Casting.Cancel(1);
        Assert.Equal(DaggerfallCastOutcome.Unready, h.Casting.Release(1, true).Outcome);
        Assert.Empty(h.Uses);
        h.Casting.Ready(1, "spell");
        h.Player.Get<StatsComponent>().GetStat(StatId.Parse("destruction")).BaseValue = 80;
        var release = h.Casting.Release(1, true);
        var bundle = Assert.IsType<DaggerfallLiveSpell>(release.Bundle);
        int expected = DaggerfallMagicCostPolicy.QuoteCasting(h.Catalog, bundle.Spell, new Dictionary<string,int> { ["destruction"] = 50 }).SpellPoints;
        Assert.Equal(expected, bundle.Cost);
        Assert.Equal(1000 - expected, h.Magicka(1).Current);
        Assert.Equal(2, h.Uses.Count);
        Assert.All(h.Uses, use => Assert.Equal(DaggerfallSkillUseReason.ReleasedSpellEffect, use.Reason));
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, h.Casting.Deliver(bundle, [1]).Outcome);
        Assert.Equal(2, h.Effects.Active.Count);
        Assert.Equal(1020, h.HealthMaximum(1).Value);
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, h.Casting.Deliver(bundle, [1]).Outcome);
        Assert.Equal(DaggerfallCastOutcome.Unready, h.Casting.Release(1, true).Outcome);
        Assert.Equal(2, h.Uses.Count);
        Assert.DoesNotContain(h.Completed.SkipWhile(result => result.Outcome != DaggerfallCastOutcome.Released), result => result.Outcome == DaggerfallCastOutcome.Cancelled);
    }

    [Fact]
    public void Unsupported_unknown_unlearned_and_invalid_touch_do_not_pay_or_tally()
    {
        using Harness h = new();
        Assert.Equal(DaggerfallCastOutcome.UnknownSpell, h.Casting.Ready(1, "missing").Outcome);
        h.Known = false;
        Assert.Equal(DaggerfallCastOutcome.UnknownSpell, h.Casting.Ready(1, "spell").Outcome);
        h.Known = true;
        h.Casting.Ready(1, "spell");
        Assert.Equal(DaggerfallCastOutcome.InvalidTarget, h.Casting.Release(1, false).Outcome);
        Assert.NotNull(h.Casting.ReadyFor(1));
        Assert.Equal(1000, h.Magicka(1).Current);
        Assert.Empty(h.Uses);
        using var unsupported = new Harness(bind: false);
        Assert.Equal(DaggerfallCastOutcome.UnsupportedEffect, unsupported.Casting.Ready(1, "spell").Outcome);
        Assert.Null(unsupported.Casting.ReadyFor(1));
        Assert.Empty(unsupported.Uses);
    }

    [Fact]
    public void Player_requires_affordability_but_enemy_requires_positive_current_and_common_deduction()
    {
        using Harness h = new();
        h.Magicka(1).SetCurrent(1);
        Assert.Equal(DaggerfallCastOutcome.InsufficientMagicka, h.Casting.Ready(1, "spell").Outcome);
        h.Magicka(2).SetCurrent(1);
        Assert.Equal(DaggerfallCastOutcome.Ready, h.Casting.Ready(2, "spell").Outcome);
        Assert.Equal(DaggerfallCastOutcome.Released, h.Casting.Release(2, true).Outcome);
        Assert.Equal(0, h.Magicka(2).Current);
        Assert.Empty(h.Uses);
    }

    [Fact]
    public void Readiness_performs_affordability_once_and_release_keeps_its_latched_cost_if_magicka_changes()
    {
        using Harness h = new(); h.Casting.Ready(1,"spell");
        int cost = h.Casting.ReadyFor(1)!.Cost;
        h.Magicka(1).SetCurrent(0);
        var released = h.Casting.Release(1,true);
        Assert.Equal(DaggerfallCastOutcome.Released,released.Outcome);
        Assert.Equal(cost,released.Bundle!.Cost); Assert.Equal(0,h.Magicka(1).Current);
        Assert.Single(h.Uses); Assert.Null(h.Casting.ReadyFor(1));
    }

    [Fact]
    public void Silence_after_readiness_blocks_release_without_clearing_or_charging_and_exempts_item_sources()
    {
        using Harness h = new();
        Assert.Equal(DaggerfallCastOutcome.Ready, h.Casting.Ready(1, "spell").Outcome);
        h.Defense = new(0, 0, [], BlocksCasting: true); h.ApplyDefense(1);
        Assert.Equal(DaggerfallCastOutcome.Silenced, h.Casting.Release(1, true).Outcome);
        Assert.NotNull(h.Casting.ReadyFor(1));
        Assert.Equal(1000, h.Magicka(1).Current); Assert.Empty(h.Uses);
        Assert.Equal(1, h.Casting.NextSequence);
        h.Defense = DaggerfallMagicDefense.None;
        Assert.Equal(DaggerfallCastOutcome.Released, h.Casting.Release(1, true).Outcome);
        Assert.Single(h.Uses); Assert.Null(h.Casting.ReadyFor(1));
        Assert.Equal(DaggerfallCastOutcome.Unready, h.Casting.Release(1, true).Outcome);

        using Harness item = new();
        Assert.Equal(DaggerfallCastOutcome.Ready, item.Casting.Ready(1, "spell", 42).Outcome);
        item.Defense = new(0, 0, [], BlocksCasting: true); item.ApplyDefense(1);
        Assert.Equal(DaggerfallCastOutcome.Released, item.Casting.Release(1, true).Outcome);
        Assert.Equal(1000, item.Magicka(1).Current);
    }

    [Fact]
    public void Hard_immunity_precedes_defenses_and_save_uses_live_profile()
    {
        using Harness h = new(paralysis: true);
        h.Target.Get<StatsComponent>().GetStat(StatId.Parse(DaggerfallMechanicsIds.ImmunityParalysis.Value)).BaseValue = 1;
        var bundle = h.Release();
        h.Casting.Deliver(bundle, [2]);
        Assert.Equal(DaggerfallCastOutcome.Immune, Assert.Single(bundle.Results).Outcome);
        Assert.Empty(h.Effects.Active);
        h.Target.Get<StatsComponent>().GetStat(StatId.Parse(DaggerfallMechanicsIds.ImmunityParalysis.Value)).BaseValue = 0;
        h.Profile = h.Profile with { CareerTolerances = h.Profile.CareerTolerances with { Magic = DaggerfallMagicTolerance.Immune } };
        bundle = h.Release(); h.Casting.Deliver(bundle, [2]);
        Assert.Equal(DaggerfallCastOutcome.Resisted, Assert.Single(bundle.Results).Outcome);
        Assert.Empty(h.Effects.Active);
        h.Profile = h.Profile with { CareerTolerances = h.Profile.CareerTolerances with { Magic = DaggerfallMagicTolerance.CriticalWeakness } };
        bundle = h.Release(); h.Casting.Deliver(bundle, [2]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        Assert.Equal(1010, h.HealthMaximum(2).Value);
    }

    [Fact]
    public void Self_absorption_uses_this_cast_cost_and_capacity_before_refund()
    {
        using Harness h = new(range: 0, count: 2);
        h.Player.Get<StatsComponent>().GetStat(StatId.Parse("destruction")).BaseValue = 100;
        h.Defense = new(100, 0, []); h.ApplyDefense(1);
        var bundle = h.Release();
        h.Casting.Deliver(bundle, [1]);
        Assert.Equal(DaggerfallCastOutcome.Absorbed, bundle.Results[0].Outcome);
        Assert.Equal(DaggerfallCastOutcome.Applied, bundle.Results[1].Outcome); // aggregate refunds cannot exceed available capacity
        Assert.Equal(999, h.Magicka(1).Current);
        Assert.Equal(2, h.Effects.Active.Count); // defense and the effect that could not be absorbed
        Assert.Equal(2, h.Uses.Count);
    }

    [Fact]
    public void Reflection_redirects_whole_bundle_once_and_resistance_precedes_save()
    {
        using Harness h = new(count: 2);
        h.Defense = new(0, 100, []); h.ApplyDefense(2);
        var bundle = h.Release(); h.Casting.Deliver(bundle, [2]);
        Assert.Single(bundle.Results, result => result.Outcome == DaggerfallCastOutcome.Reflected);
        Assert.Equal(2, bundle.Results.Count(result => result.TargetId == 1 && result.Outcome == DaggerfallCastOutcome.Applied));
        using Harness resisted = new();
        resisted.Defense = new(0, 0, [new(DaggerfallMagicResistanceElement.Magic, 100)]);
        resisted.ApplyDefense(2);
        bundle = resisted.Release(); resisted.Casting.Deliver(bundle, [2]);
        Assert.Equal(DaggerfallCastOutcome.Resisted, Assert.Single(bundle.Results).Outcome);
        Assert.Equal(1000, resisted.HealthMaximum(2).Value);
    }

    [Fact]
    public void Missing_source_item_target_and_save_boundary_refuse_callback_without_attaching()
    {
        using Harness h = new();
        var bundle = h.Release();
        h.SourcePresent = false;
        Assert.Equal(DaggerfallCastOutcome.SourceUnavailable, h.Casting.Deliver(bundle, [2]).Outcome);
        Assert.Equal(DaggerfallCastOutcome.SourceUnavailable, Assert.Single(bundle.Results).Outcome);
        Assert.Empty(h.Effects.Active);
        h.SourcePresent = true;
        bundle = h.Release(); h.TargetPresent = false; h.Casting.Deliver(bundle, [2]);
        Assert.Equal(DaggerfallCastOutcome.TargetUnavailable, Assert.Single(bundle.Results).Outcome);
        h.TargetPresent = true;
        Assert.Equal(DaggerfallCastOutcome.Ready, h.Casting.Ready(1, "spell", 42).Outcome);
        bundle = h.Casting.Release(1, true).Bundle!; h.ItemPresent = false;
        Assert.Equal(DaggerfallCastOutcome.SourceUnavailable, h.Casting.Deliver(bundle, [2]).Outcome);
        h.ItemPresent = true;
        bundle = h.Release(); h.Casting.ClearTransient();
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, h.Casting.Deliver(bundle, [2]).Outcome);
        Assert.Null(h.Casting.ReadyFor(1));
        Assert.Empty(h.Effects.Active);
    }

    [Fact]
    public void Live_effect_payload_restore_removes_sources_without_replaying_initial_round()
    {
        using Harness h = new(range: 0);
        var bundle = h.Release(); h.Casting.Deliver(bundle, [1]);
        var saved = Assert.Single(h.Effects.Capture());
        Assert.Equal(1, saved.CasterId); Assert.Equal("Magic", saved.Element);
        var state = saved.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)!;
        Assert.Equal(10, state.Amount); Assert.Equal(1, state.CasterLevel); Assert.Equal(100, state.SavePercent);
        Assert.Equal(1, h.Rounds);
        using Harness restored = new(range: 0);
        restored.Effects.Restore([saved]);
        Assert.Equal(1010, restored.HealthMaximum(1).Value);
        Assert.Equal(0, restored.Rounds);
        restored.Effects.CancelActorReferences(1);
        Assert.Equal(1000, restored.HealthMaximum(1).Value);
    }

    [Fact]
    public void Chance_failure_silence_and_incumbent_refresh_preserve_release_and_source_semantics()
    {
        using Harness chance = new(chance:true, random:SaveDice.Create(100));
        var bundle = chance.Release(); chance.Casting.Deliver(bundle,[2]);
        Assert.Equal(DaggerfallCastOutcome.ChanceFailed, Assert.Single(bundle.Results).Outcome);
        Assert.Single(chance.Uses); Assert.Empty(chance.Effects.Active);
        using Harness silent = new(); silent.Defense = new(0,0,[],BlocksCasting:true); silent.ApplyDefense(1);
        Assert.Equal(DaggerfallCastOutcome.Silenced, silent.Casting.Ready(1,"spell").Outcome);
        Assert.Empty(silent.Uses);
        Assert.Equal(DaggerfallCastOutcome.Ready, silent.Casting.Ready(1,"spell",42).Outcome);
        using Harness refresh = new(range:0,stacking:DaggerfallEffectStacking.RefreshDuration);
        bundle = refresh.Release(); refresh.Casting.Deliver(bundle,[1]);
        string instance = Assert.Single(refresh.Effects.Active).Context.Instance.Value;
        bundle = refresh.Release(); refresh.Casting.Deliver(bundle,[1]);
        var result = Assert.Single(bundle.Results);
        Assert.Equal(DaggerfallCastOutcome.Refreshed,result.Outcome); Assert.Equal(instance,result.Instance);
        Assert.Single(refresh.Effects.Active); Assert.Equal(1010,refresh.HealthMaximum(1).Value);
    }

    [Fact]
    public void Area_reflection_is_bundle_wide_and_returned_spells_run_the_casters_active_resistance()
    {
        using Harness h = new(range:3,count:2);
        h.Defense = new(0,100,[new(DaggerfallMagicResistanceElement.Magic,100)]);
        h.ApplyDefense(1); h.ApplyDefense(2); h.ApplyDefense(3);
        var bundle = h.Release(); h.Casting.Deliver(bundle,[2,3]);
        Assert.Single(bundle.Results,result => result.Outcome == DaggerfallCastOutcome.Reflected);
        Assert.Equal(2,bundle.Results.Count(result => result.TargetId == 1 && result.Outcome == DaggerfallCastOutcome.Resisted));
        Assert.Equal(1000,h.HealthMaximum(1).Value);
    }

    [Theory]
    [InlineData(true, 7, 2u)]
    [InlineData(false, 0, 2u)]
    public void Returned_save_percentage_scales_magnitude_while_nonzero_nonmagnitude_saves_keep_full_duration(bool magnitude, int expectedAmount, uint remaining)
    {
        using Harness h = new(magnitude:magnitude,random:SaveDice.Create(50));
        h.Profile = h.Profile with { CareerTolerances = h.Profile.CareerTolerances with { Magic = DaggerfallMagicTolerance.Normal } };
        var bundle = h.Release(); h.Casting.Deliver(bundle,[2]);
        var active = Assert.Single(h.Effects.Active);
        var state = active.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)!;
        Assert.Equal(75,state.SavePercent); Assert.Equal(expectedAmount,state.Amount);
        Assert.Equal(remaining,active.Lifecycle.RemainingRounds);
    }

    public class SaveDice : DispatchProxy
    {
        private int _value;
        internal static IRandomService Create(int value)
        {
            var service = DispatchProxy.Create<IRandomService,SaveDice>();
            ((SaveDice)(object)service)._value = value; return service;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var request = (KeyedRngRequest)args![0]!;
            return new KeyedRngReceipt(request.Maximum == 100 ? _value : request.Minimum);
        }
    }

    private sealed class Harness : IDisposable
    {
        internal ActorsState Actors { get; } = new();
        internal Actor Player { get; }
        internal Actor Target { get; }
        internal DaggerfallMagicCatalogSet Catalog { get; }
        internal DaggerfallEffectLifecycle Effects { get; }
        internal DaggerfallCasting Casting { get; }
        internal List<DaggerfallSkillUse> Uses { get; } = [];
        internal List<DaggerfallCastResult> Completed { get; } = [];
        internal DaggerfallMagicDefense Defense { get; set; } = DaggerfallMagicDefense.None;
        internal bool Known = true, SourcePresent = true, TargetPresent = true, ItemPresent = true;
        internal int Rounds;
        internal DaggerfallMagicTargetProfile Profile = new(50,
            new(DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.CriticalWeakness, DaggerfallMagicTolerance.Normal,
                DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal),
            null, 0, 0, 0, new(0,0,0,0,0), []);
        internal Harness(int range = 1, int count = 1, bool bind = true, bool paralysis = false, bool chance = false, bool magnitude = true, DaggerfallEffectStacking stacking = DaggerfallEffectStacking.Stack, IRandomService? random = null)
        {
            Player = Actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health").Actor;
            Target = Actors.CreateActor(2, new EntityTypeId("target"), Stats(), new(new(0,0,0),0f), "health").Actor;
            Actors.CreateActor(3, new EntityTypeId("target-other"), Stats(), new(new(0,0,1),0f), "health");
            foreach (var actor in new[] { Player, Target })
            { actor.Add(new DaggerfallSpellReadiness()); if (actor == Target) actor.Add(new ProgressionState()); }
            DaggerfallSpellEffectDefinition setting = new("settings", 99, -1, 3, 0, 1, chance ? 0 : 100, 0, 1, 10, 10, 0, 0, 1);
            DaggerfallSpellDefinition spell = new("spell", 1, false, "Compiled spell", 4, range, 0, 0, Enumerable.Repeat(setting, count).ToArray());
            var row = new DaggerfallMagicEffectCostDefinition(99,-1,1,"destruction",10,1,0,0,
                new(new(0,10,1),null,null));
            Catalog = new(new Dictionary<string,DaggerfallSpellDefinition> { ["spell"] = spell }, new Dictionary<string,DaggerfallMagicItemDefinition>(), [], [],
                new Dictionary<(int,int),DaggerfallMagicEffectCostDefinition> { [(99,-1)] = row }, new Dictionary<string,DaggerfallEnchantmentSetting>());
            IEnumerable<IActiveEffectContribution> Apply(DaggerfallActiveEffect effect)
            {
                var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)!;
                Stat maximum = effect.Target.Get<StatsComponent>().GetStat(StatId.Parse("health-maximum"));
                var handle = maximum.AddModifier(state.Amount);
                return [new DelegateActiveEffectContribution(() => maximum.RemoveModifier(handle))];
            }
            var definition = new DaggerfallEffectDefinition("compiled", "compiled", stacking, 20, 1,
                Apply, _ => Rounds++, Apply, Spell: new(99,-1,SupportsDuration:true,RollChanceOnCast:chance,SupportsMagnitude:magnitude,IsParalysis:paralysis));
            var defense = new DaggerfallEffectDefinition("defense","defense",DaggerfallEffectStacking.Stack,20,1,MagicDefense:_ => Defense);
            Effects = new(Actors, new(bind ? [definition,defense] : new[] { defense }));
            Casting = new(Catalog, Effects, id => id == 1 ? SourcePresent ? Player : null : TargetPresent && Actors.TryGet(id,out var actor) ? actor.Actor : null,
                _ => Profile, _ => ItemPresent, Uses.Add, Completed.Add, random ?? RandomMinimum.Create(), 1, playerKnowsSpell:_ => Known);
        }
        internal DaggerfallLiveSpell Release() { Casting.Ready(1,"spell"); return Casting.Release(1,true).Bundle!; }
        internal Track Magicka(long id) => (id == 1 ? Player : Target).Get<StatsComponent>().GetTrack(TrackId.Parse("magicka"));
        internal Stat HealthMaximum(long id) => (id == 1 ? Player : Target).Get<StatsComponent>().GetStat(StatId.Parse("health-maximum"));
        internal void ApplyDefense(long id)
        {
            using var doc = JsonDocument.Parse("{}");
            Effects.Start(new($"defense.{id}","defense","defense",id,id,"defense",null,null,1,20,doc.RootElement));
        }
        private static StatsComponent Stats()
        {
            StatsComponent stats = new();
            foreach (var key in new[] { "health", "magicka" })
            { var maximum = new Stat(1000); stats.AddStat(StatId.Parse(key+"-maximum"),maximum); stats.AddTrack(TrackId.Parse(key),new(maximum,1000)); }
            stats.AddStat(StatId.Parse("destruction"),new(50));
            stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.ImmunityParalysis.Value),new(0));
            return stats;
        }
        public void Dispose() { Effects.Dispose(); Actors.Dispose(); }
    }
}
