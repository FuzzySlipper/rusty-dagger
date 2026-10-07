using System.Numerics;
using Daggerfall.Import.Arena2;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallRestorationEffectsTests
{
    [Fact]
    public void Free_action_releases_existing_controls_prevents_new_paralysis_and_restores_incumbent_lifetime()
    {
        using Fixture f = new(); var s = f.Session;
        DaggerfallParalysisEffectsTests.Start(s, "old-paralysis", 2000, 1, 100);
        Assert.True(s.State.Effects.ControlsFor(1).Movement);
        Start(s, "free", "free-action", Setting(26), 10);
        Assert.False(s.State.Effects.ControlsFor(1).Movement);
        Assert.True(s.State.Effects.MagicDefenseFor(1).PreventsParalysis);
        Start(s, "top-up", "free-action", Setting(26), 20);
        Assert.Equal(29u, s.State.Effects.Active.Single(e => e.Definition.Key == "free-action").Lifecycle.RemainingRounds);
        using var restored = f.Restore(s.CaptureSave());
        Assert.False(restored.State.Effects.ControlsFor(1).PhysicalAttacks);
        Assert.Equal(29u, restored.State.Effects.Active.Single(e => e.Definition.Key == "free-action").Lifecycle.RemainingRounds);
        var spell = TestPayload.Definitions.Magic.Spells["spell.047"];
        Magicka(restored, 2000).Maximum.BaseValue = 1; Magicka(restored, 2000).SetCurrent(1);
        var casting = CastingFor(restored, spell, 1);
        casting.Ready(2000, spell.Key); var bundle = casting.Release(2000, true).Bundle!;
        casting.Deliver(bundle, [1]);
        Assert.Equal(DaggerfallCastOutcome.Immune, Assert.Single(bundle.Results).Outcome);
        Assert.Single(restored.State.Effects.Active, e => e.Definition.Key == "paralyze");
        restored.State.Effects.Cancel(EffectInstanceId.Parse("free"));
        Assert.True(restored.State.Effects.ControlsFor(1).Movement);
        DaggerfallParalysisEffects.Cure(restored.State.Effects, 1);
        Assert.Equal(default, restored.State.Effects.ControlsFor(1));
    }

    [DonorFact(Arena2MagicEffectCostTable.DonorSourcePath)]
    public void Normalized_free_action_cast_has_regular_cost_binding_and_a_classic_record_is_read_without_its_element_gate()
    {
        using Fixture f = new(); var s = f.Session; Fund(s);
        var spell = new DaggerfallSpellDefinition("free-spell", 1, false, "Free Action", 4, 0, 0, 0, [Setting(26)]);
        var casting = CastingFor(s, spell, 1);
        Assert.NotNull(DaggerfallMagicCostMetadata.For(26, -1));
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
        var release = casting.Release(1, true); Assert.True(release.Bundle!.Cost > 0);
        casting.Deliver(release.Bundle, [1]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(release.Bundle.Results).Outcome);
        Assert.True(s.State.Effects.MagicDefenseFor(1).PreventsParalysis);
        // The donor reads a classic record without the spellmaker's element gate; only construction
        // holds a crafted spell to its effects' allowed elements.
        var fireElement = CastingFor(s, spell with { Element = 0 }, 1);
        Assert.Equal(DaggerfallCastOutcome.Ready, fireElement.Ready(1, spell.Key).Outcome);
    }

    [Fact]
    public void Regeneration_same_settings_extend_one_incumbent_different_settings_coexist_and_restore_does_not_heal()
    {
        using Fixture f = new(); var s = f.Session; var health = Health(s); health.Maximum.BaseValue = 100; health.SetCurrent(10);
        var setting = Setting(18) with { MagnitudeBaseLow = 2, MagnitudeBaseHigh = 5 };
        Start(s, "regen", "regenerate", setting, 10);
        Assert.Equal(15, health.Current);
        s.State.Effects.AdvanceOrdinaryRound(); Assert.Equal(20, health.Current);
        s.State.Effects.AdvanceElapsedRounds(2); Assert.Equal(30, health.Current);
        Start(s, "same-settings-other-key", "regenerate", setting with { Key = "same" }, 10);
        Assert.Equal(30, health.Current);
        Assert.Single(s.State.Effects.Active);
        Assert.Equal(16u, Assert.Single(s.State.Effects.Active).Lifecycle.RemainingRounds);
        Start(s, "different", "regenerate", setting with { MagnitudeBaseHigh = 2 }, 10);
        Assert.Equal(32, health.Current); Assert.Equal(2, s.State.Effects.Active.Count);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(32, Health(restored).Current);
        var active = restored.State.Effects.Active.Single(e => e.Context.Instance.Value == "regen");
        Assert.Equal(4, DaggerfallRestorationEffects.ReadRegeneration(active.State).NextRound);
        restored.State.Effects.AdvanceOrdinaryRound(); Assert.Equal(39, Health(restored).Current);
        Health(restored).SetCurrent(Health(restored).Maximum.Value - 1);
        restored.State.Effects.AdvanceElapsedRounds(100); Assert.Equal(Health(restored).Maximum.Value, Health(restored).Current);
        Assert.Empty(restored.State.Effects.Active);
    }

    [Fact]
    public void Published_regeneration_uses_real_calendar_and_current_save_without_repeating_initial_heal()
    {
        using Fixture f = new(); var s = f.Session; Fund(s); Health(s).SetCurrent(10);
        s.State.Character.LearnSpell("spell.023"); s.ReadyPlayerSpell("spell.023");
        var result = s.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(result.Bundle!.Results).Outcome);
        Assert.Equal(12, Health(s).Current);
        s.ReadyPlayerSpell("spell.023"); var refresh = s.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.Refreshed, Assert.Single(refresh.Bundle!.Results).Outcome);
        Assert.Equal(12, Health(s).Current); Assert.Single(s.State.Effects.Active);
        s.AdvanceElapsedTime(120); Assert.Equal(16, Health(s).Current);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(16, Health(restored).Current);
        restored.AdvanceElapsedTime(60); Assert.Equal(18, Health(restored).Current);
        Assert.Equal(4, DaggerfallRestorationEffects.ReadRegeneration(Assert.Single(restored.State.Effects.Active).State).NextRound);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, true)]
    public void Absorption_real_binding_returns_one_delivery_result_and_refunds_only_accepted_capacity(int chance, bool absorbed)
    {
        using Fixture f = new(); var s = f.Session; Fund(s);
        const long target = 2000;
        var magicka = Magicka(s, target); magicka.Maximum.BaseValue = 10000; magicka.SetCurrent(0);
        Start(s, "absorb", "spell-absorption", Setting(20) with { ChanceBase = chance }, 10, target:target);
        var damage = Setting(4) with { SubType = 0, MagnitudeBaseLow = 10, MagnitudeBaseHigh = 10 };
        var spell = new DaggerfallSpellDefinition("incoming", 2, false, "Incoming", 4, 1, 0, 0, [damage]);
        List<DaggerfallCastResult> completed = [];
        var casting = CastingFor(s, spell, 50, completed);
        casting.Ready(1, spell.Key); var bundle = casting.Release(1, true).Bundle!;
        double health = s.State.Actors.Get(target).Stats.GetTrack(TrackId.Parse("health")).Current;
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, casting.Deliver(bundle, [target]).Outcome);
        Assert.Equal(absorbed ? DaggerfallCastOutcome.Absorbed : DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        Assert.Single(completed, c => c.Outcome == DaggerfallCastOutcome.DeliveryCompleted);
        if (absorbed) { Assert.True(magicka.Current > 0); Assert.Equal(health, s.State.Actors.Get(target).Stats.GetTrack(TrackId.Parse("health")).Current); }
        else { Assert.Equal(0, magicka.Current); Assert.True(s.State.Actors.Get(target).Stats.GetTrack(TrackId.Parse("health")).Current < health); }
        double refunded = magicka.Current;
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, casting.Deliver(bundle, [target]).Outcome);
        Assert.Equal(refunded, magicka.Current);
    }

    [Fact]
    public void Multiple_absorbed_effects_reserve_total_capacity_before_one_refund()
    {
        using Fixture f = new(); var s = f.Session; Fund(s); const long target = 2000;
        var magicka = Magicka(s, target); magicka.Maximum.BaseValue = 10000; magicka.SetCurrent(0);
        Start(s, "absorb", "spell-absorption", Setting(20), 10, target:target);
        var damage = Setting(4) with { SubType = 2, MagnitudeBaseLow = 1, MagnitudeBaseHigh = 1 };
        var spell = new DaggerfallSpellDefinition("incoming", 2, false, "Incoming", 4, 1, 0, 0, [damage]);
        var single = CastingFor(s, spell, 1); single.Ready(1, spell.Key); var first = single.Release(1, true).Bundle!;
        single.Deliver(first, [target]); int refund = checked((int)magicka.Current); Assert.True(refund > 0);
        magicka.Maximum.BaseValue = refund; magicka.SetCurrent(0);
        var multiple = CastingFor(s, spell with { Effects = [damage, damage with { Key = "second" }] }, 1);
        multiple.Ready(1, spell.Key); var bundle = multiple.Release(1, true).Bundle!; multiple.Deliver(bundle, [target]);
        Assert.Equal(DaggerfallCastOutcome.Absorbed, bundle.Results[0].Outcome);
        Assert.Equal(DaggerfallCastOutcome.Applied, bundle.Results[1].Outcome);
        Assert.Equal(refund, magicka.Current); // no per-effect mutation or over-capacity successful absorption
    }

    [Fact]
    public void Published_absorption_retains_incumbent_settings_and_uses_receivers_live_level_after_restore()
    {
        using Fixture f = new(); var s = f.Session; Fund(s); s.State.Character.LearnSpell("spell.045");
        s.ReadyPlayerSpell("spell.045"); var cast = s.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(cast.Bundle!.Results).Outcome);
        Assert.Equal(15, s.State.Effects.MagicDefenseFor(1).AbsorptionChance);
        s.ReadyPlayerSpell("spell.045"); var refresh = s.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.Refreshed, Assert.Single(refresh.Bundle!.Results).Outcome);
        Assert.Single(s.State.Effects.Active);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(15, restored.State.Effects.MagicDefenseFor(1).AbsorptionChance);
        restored.State.Progression.AdvanceTo(0, 10); Assert.Equal(60, restored.State.Effects.MagicDefenseFor(1).AbsorptionChance);
        restored.State.Effects.Cure(Assert.Single(restored.State.Effects.Active).Context.Instance);
        Assert.Equal(0, restored.State.Effects.MagicDefenseFor(1).AbsorptionChance);
    }

    [Theory]
    [InlineData("free-action", 26)]
    [InlineData("spell-absorption", 20)]
    [InlineData("regenerate", 18)]
    public void Used_item_source_outlives_its_item_breaking_and_destruction_removes_recovery_defense_and_restriction_mask(string key, int type)
    {
        using Fixture f = new(); var s = f.Session; DaggerfallParalysisEffectsTests.Start(s, "paralyzed", 2000, 1, 100);
        var item = s.State.Inventory.Read().UniqueItems.First(); ulong id = s.State.Inventory.GetDurableItemId(item.Entity).Value;
        Start(s, "item", key, Setting(type), 100, item:id);
        // Only a held bundle is tied to its item; a used item's cast outlives the item breaking, as in the
        // donor, and ends when no item remains for it to name.
        s.State.ItemInstances.ReplaceUnique(id, s.State.ItemInstances.RequireUnique(id) with { CurrentCondition = 0 });
        Assert.Contains(s.State.Effects.Active, e => e.Context.Instance.Value == "item");
        s.DestroyUniqueItem(id);
        Assert.DoesNotContain(s.State.Effects.Active, e => e.Context.Instance.Value == "item");
        Assert.True(s.State.Effects.ControlsFor(1).Movement);
        Assert.False(s.State.Effects.MagicDefenseFor(1).PreventsParalysis);
    }

    [Theory]
    [InlineData("free-action", 26, false)] [InlineData("free-action", 26, true)]
    [InlineData("spell-absorption", 20, false)] [InlineData("spell-absorption", 20, true)]
    [InlineData("regenerate", 18, false)] [InlineData("regenerate", 18, true)]
    public void Item_and_spell_sources_keep_independent_cleanup_in_both_admission_orders(string key, int type, bool itemFirst)
    {
        using Fixture f = new(); var s = f.Session;
        var items = s.State.Inventory.Read().UniqueItems.Take(2).ToArray(); Assert.Equal(2, items.Length);
        ulong first = s.State.Inventory.GetDurableItemId(items[0].Entity).Value;
        ulong second = s.State.Inventory.GetDurableItemId(items[1].Entity).Value;
        if (itemFirst) Start(s, "first-item", key, Setting(type), 10, item:first);
        Start(s, "spell-source", key, Setting(type), 20);
        if (!itemFirst) Start(s, "first-item", key, Setting(type), 10, item:first);
        Start(s, "second-item", key, Setting(type), 30, item:second);
        Assert.Equal(3, s.State.Effects.Active.Count);
        using var restored = f.Restore(s.CaptureSave());
        restored.State.ItemInstances.ReplaceUnique(first, restored.State.ItemInstances.RequireUnique(first) with { CurrentCondition = 0 });
        Assert.Equal(3, restored.State.Effects.Active.Count);
        restored.DestroyUniqueItem(first);
        Assert.DoesNotContain(restored.State.Effects.Active, e => e.Context.Instance.Value == "first-item");
        Assert.Contains(restored.State.Effects.Active, e => e.Context.Instance.Value == "spell-source" && e.Lifecycle.RemainingRounds == 19);
        Assert.Contains(restored.State.Effects.Active, e => e.Context.Instance.Value == "second-item" && e.Lifecycle.RemainingRounds == 29);
        restored.DestroyUniqueItem(second);
        Assert.Equal("spell-source", Assert.Single(restored.State.Effects.Active).Context.Instance.Value);
        Assert.True(restored.State.Effects.Cure(EffectInstanceId.Parse("spell-source"))); Assert.Empty(restored.State.Effects.Active);
    }

    [Fact]
    public void Regeneration_on_the_live_player_survives_a_casters_site_unload_and_current_save()
    {
        var source = ReadInputs(TestData.RepositoryRoot);
        var destination = DaggerfallSiteContent.Read(FullContent(TestData.RepositoryRoot),
            File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot,"content/worldrpg/payloads/daggerfall.castle-necromoghan.json")),TestPayload.Definitions);
        var composition = new DaggerfallSessionComposition(TestPayload.Definitions,source,DaggerfallTuning.Defaults)
            { Profiles = new([source,destination]) };
        List<string> releases = []; ContentFake content = new(releases); PopulateContent(content,source); PopulateContent(content,destination);
        var spatial = SpatialFake.Create(source.SpatialArtifact.Sha256,releases);
        var engine = EngineContextFake.Create(content,spatial.Service,new AppearanceFake(releases),PerceptionFake.Create().Service,random:RandomMaximum.Create());
        using var session = DaggerfallSession.StartNew(engine.Context,composition);
        Health(session).SetCurrent(10);
        Start(session,"unloaded-caster","regenerate",Setting(18),100,caster:2000);
        Assert.Equal(12, Health(session).Current);
        Assert.True(session.TryTransitionTo(destination.ProfileKey));
        Assert.DoesNotContain(2000L,session.DefinitionsByActor.Keys);
        session.AdvanceElapsedTime(60); Assert.Equal(14,Health(session).Current);
        using var restored = DaggerfallSession.Restore(engine.Context,composition,session.CaptureSave());
        Assert.Equal(14,Health(restored).Current);
        restored.AdvanceElapsedTime(60); Assert.Equal(16,Health(restored).Current);
    }

    [Fact]
    public void Malformed_regeneration_round_state_is_refused_and_dead_targets_do_not_regenerate()
    {
        using Fixture f = new(); var s = f.Session; Start(s, "regen", "regenerate", Setting(18), 100);
        var saved = DaggerfallSavePayload.Read(s.CaptureSave()); var effect = Assert.Single(saved.ActiveEffects);
        var state = DaggerfallRestorationEffects.ReadRegeneration(effect.State);
        var broken = saved with { ActiveEffects = [effect with { State = DaggerfallRestorationEffects.RegenerationState(state with { NextRound = -1 }) }] };
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(broken)));
        Health(s).SetCurrent(0); s.State.Effects.AdvanceOrdinaryRound(); Assert.Equal(0, Health(s).Current); Assert.Empty(s.State.Effects.Active);
    }

    private static DaggerfallSpellEffectDefinition Setting(int type) => new("settings",type,-1,10,0,1,100,0,1,2,2,0,0,1);
    private static void Start(DaggerfallSession s, string instance, string key, DaggerfallSpellEffectDefinition settings,
        uint rounds, long target = 1, ulong? item = null, long caster = 1)
    {
        var cast = new DaggerfallCastEffectState(settings,1,0,100);
        JsonElement state = key == "regenerate" ? DaggerfallRestorationEffects.RegenerationState(new(cast,0))
            : JsonSerializer.SerializeToElement(cast,DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        s.State.Effects.Start(new(instance,key,"spell.recovery",caster,target,settings.Key,"Magic",item,1,rounds,state));
    }
    private static DaggerfallCasting CastingFor(DaggerfallSession s, DaggerfallSpellDefinition spell, int dice,
        List<DaggerfallCastResult>? completed = null)
    {
        var costs = TestPayload.Definitions.Magic.EffectCosts.ToDictionary();
        if (spell.Effects.Any(effect => effect.Type == 26))
        {
            var row = Arena2MagicEffectCostTable.Read(File.ReadAllText(TestData.Donor(Arena2MagicEffectCostTable.DonorSourcePath))).Resolve(26,-1);
            costs[(26,-1)] = new(row.Type,row.SubType,row.SettingsType,row.School,row.Coefficients[0],row.Coefficients[1],row.Coefficients[2],row.Coefficients[3]);
        }
        var catalog = TestPayload.Definitions.Magic with { Spells = new Dictionary<string,DaggerfallSpellDefinition> { [spell.Key] = spell }, EffectCosts = costs };
        var profile = new DaggerfallMagicTargetProfile(50,new(DaggerfallMagicTolerance.Normal,DaggerfallMagicTolerance.CriticalWeakness,
            DaggerfallMagicTolerance.Normal,DaggerfallMagicTolerance.Normal,DaggerfallMagicTolerance.Normal,DaggerfallMagicTolerance.Normal,
            DaggerfallMagicTolerance.Normal),null,0,0,0,new(0,0,0,0,0),[]);
        return new(catalog,s.State.Effects,id => id == 1 ? s.State.Actors.Player.Actor : s.State.Actors.TryGet(id,out var actor) ? actor.Actor : null,
            _ => profile,_ => true,_ => {},r => completed?.Add(r),DaggerfallCastingTests.SaveDice.Create(dice),1,playerKnowsSpell:_ => true,casterLevel:_ => 1);
    }
    private static Track Health(DaggerfallSession s) => s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));
    private static Track Magicka(DaggerfallSession s,long id) => (id == 1 ? s.State.Actors.Player.Stats : s.State.Actors.Get(id).Stats).GetTrack(TrackId.Parse("magicka"));
    private static void Fund(DaggerfallSession s) { var track = Magicka(s,1); track.Maximum.BaseValue = 10000; track.SetCurrent(10000); }
    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition _composition = new(TestPayload.Definitions,ReadInputs(TestData.RepositoryRoot),DaggerfallTuning.Defaults);
        internal DaggerfallSession Session { get; }
        internal Fixture() => Session = DaggerfallSession.StartNew(Engine().Context,_composition);
        private EngineContextFake Engine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content,_composition.StartSite);
            var spatial = SpatialFake.Create(_composition.StartSite.SpatialArtifact.Sha256,releases);
            return EngineContextFake.Create(content,spatial.Service,new AppearanceFake(releases),PerceptionFake.Create().Service,random:RandomMaximum.Create());
        }
        internal DaggerfallSession Restore(RulesetSavePayload payload) => DaggerfallSession.Restore(Engine().Context,_composition,payload);
        public void Dispose() => Session.Dispose();
    }
}
