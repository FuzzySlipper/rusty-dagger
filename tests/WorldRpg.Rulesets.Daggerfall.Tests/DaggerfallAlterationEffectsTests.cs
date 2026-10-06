using System.Numerics;
using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallAlterationEffectsTests
{
    [Theory]
    [InlineData((int)DaggerfallMagicResistanceElement.Fire)]
    [InlineData((int)DaggerfallMagicResistanceElement.Frost)]
    [InlineData((int)DaggerfallMagicResistanceElement.DiseaseOrPoison)]
    [InlineData((int)DaggerfallMagicResistanceElement.Shock)]
    [InlineData((int)DaggerfallMagicResistanceElement.Magic)]
    public void All_variants_keep_independent_source_channels_and_rebuild_without_mutating_numeric_resistance(int elementValue)
    {
        var element = (DaggerfallMagicResistanceElement)elementValue;
        Assert.NotNull(DaggerfallMagicCostMetadata.For(8, (int)element));
        using Fixture f = new(); var s = f.Session;
        var before = s.MagicProfile(1).ResistanceModifiers;
        Resistance(s, "weak", element, 0, 10);
        Resistance(s, "strong", element, 80, 20);
        Resistance(s, "partial", element, 30, 30);
        Assert.Equal(100, Assert.Single(s.MagicProfile(1).ActiveResistances).Chance);
        Assert.Equal(before, s.MagicProfile(1).ResistanceModifiers);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(100, Assert.Single(restored.MagicProfile(1).ActiveResistances).Chance);
        restored.State.Effects.Cancel(EffectInstanceId.Parse("strong"));
        Assert.Equal(30, Assert.Single(restored.MagicProfile(1).ActiveResistances).Chance);
        restored.State.Effects.Cancel(EffectInstanceId.Parse("partial"));
        var zero = Assert.Single(restored.MagicProfile(1).ActiveResistances);
        Assert.Equal(element, zero.Element); Assert.Equal(0, zero.Chance);
        restored.State.Effects.AdvanceElapsedRounds(10);
        Assert.Empty(restored.MagicProfile(1).ActiveResistances);
        Assert.Equal(before, restored.MagicProfile(1).ResistanceModifiers);
    }

    [Theory]
    [InlineData("spell.011", (int)DaggerfallMagicResistanceElement.Frost)]
    [InlineData("spell.012", (int)DaggerfallMagicResistanceElement.Fire)]
    [InlineData("spell.013", (int)DaggerfallMagicResistanceElement.Shock)]
    public void Published_resistance_spells_use_default_compiled_catalog_and_real_cast_counters(string key, int elementValue)
    {
        var element = (DaggerfallMagicResistanceElement)elementValue;
        using Fixture f = new(); var s = f.Session; PrepareCast(s, key);
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell(key).Outcome);
        var result = s.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(result.Bundle!.Results).Outcome);
        Assert.Equal(element, Assert.Single(s.MagicProfile(1).ActiveResistances).Element);
        Assert.Equal(1, s.State.Progression.SkillUses["alteration"]);
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, s.Casting.Deliver(result.Bundle, [1]).Outcome);
        Assert.Equal(1, s.State.Progression.SkillUses["alteration"]);
    }

    [Theory]
    [InlineData(0, (int)DaggerfallDiseaseAdmission.Started)]
    [InlineData(50, (int)DaggerfallDiseaseAdmission.Started)]
    [InlineData(100, (int)DaggerfallDiseaseAdmission.Resisted)]
    public void Disease_exposure_reads_actual_active_channel_then_continues_to_ordinary_save(int chance, int expectedValue)
    {
        var expected = (DaggerfallDiseaseAdmission)expectedValue;
        using Fixture f = new(); var s = f.Session;
        s.State.Progression.AdvanceTo(0, 2);
        s.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.ResistanceDiseaseOrPoison.Value)).BaseValue = 100;
        Resistance(s, "disease-ward", DaggerfallMagicResistanceElement.DiseaseOrPoison, chance, 10);
        Assert.Equal(expected, s.InflictDisease(new("exposure", "monster-hit", 1, 1, [DaggerfallClassicDisease.BloodRot])));
        s.State.Effects.Cancel(EffectInstanceId.Parse("disease-ward"));
        Assert.Empty(s.MagicProfile(1).ActiveResistances);
        Assert.Equal(DaggerfallDiseaseAdmission.Started, s.InflictDisease(new("after-ward", "monster-hit", 1, 1, [DaggerfallClassicDisease.BloodRot])));
    }

    [Fact]
    public void Shield_cast_partial_damage_restore_topup_depletion_and_expiry_use_one_canonical_application()
    {
        using Fixture f = new(); var s = f.Session; PrepareCast(s, "spell.017");
        s.ReadyPlayerSpell("spell.017");
        var cast = s.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(cast.Bundle!.Results).Outcome);
        var effect = Assert.Single(s.State.Effects.Active);
        var initial = DaggerfallAlterationEffects.ReadShield(effect.State);
        Assert.True(initial.Starting > 2);
        var health = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        double before = health.Current;
        var partial = s.State.Kit.Rules.ApplyToHealth(new(s.State.Actors.Player.Actor, s.State.Actors.Player.Actor, "falling"), 2, 0, health);
        Assert.Equal(2, partial.CalculatedDamage); Assert.Equal(0, partial.ActualHealthLost);
        Assert.Equal(initial.Starting - 2, DaggerfallAlterationEffects.ReadShield(effect.State).Remaining);
        using var restored = f.Restore(s.CaptureSave());
        var savedEffect = Assert.Single(restored.State.Effects.Active);
        uint? priorRounds = savedEffect.Lifecycle.RemainingRounds;
        Assert.Equal(initial.Starting - 2, DaggerfallAlterationEffects.ReadShield(savedEffect.State).Remaining);
        restored.ReadyPlayerSpell("spell.017");
        var topup = restored.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.Refreshed, Assert.Single(topup.Bundle!.Results).Outcome);
        Assert.Equal(initial.Starting, DaggerfallAlterationEffects.ReadShield(savedEffect.State).Remaining);
        Assert.True(savedEffect.Lifecycle.RemainingRounds > priorRounds);
        Track restoredHealth = restored.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        var hit = restored.State.Kit.Rules.ApplyToHealth(new(restored.State.Actors.Player.Actor, restored.State.Actors.Player.Actor, "poison"), initial.Starting + 3, 0, restoredHealth);
        Assert.Equal(3, hit.ActualHealthLost); Assert.Equal(before - 3, restoredHealth.Current);
        Assert.Empty(restored.State.Effects.Active); Assert.Empty(restored.State.Actors.Player.Actor.Get<CombatContributions>().Rules);
        restored.ReadyPlayerSpell("spell.017"); restored.ReleaseReadySpell(1, Vector3.UnitZ);
        restored.State.Effects.AdvanceElapsedRounds(100);
        Assert.Empty(restored.State.Effects.Active); Assert.Empty(restored.State.Actors.Player.Actor.Get<CombatContributions>().Rules);
        var after = restored.State.Kit.Rules.ApplyToHealth(new(restored.State.Actors.Player.Actor, restored.State.Actors.Player.Actor, "melee"), 1, 0, restoredHealth);
        Assert.Equal(1, after.ActualHealthLost);
    }

    [Fact]
    public void Shield_does_not_spend_on_dead_targets_and_exact_depletion_removes_it_without_health_loss()
    {
        using Fixture f = new(); var s = f.Session; PrepareCast(s, "spell.017");
        s.ReadyPlayerSpell("spell.017"); s.ReleaseReadySpell(1, Vector3.UnitZ);
        var effect = Assert.Single(s.State.Effects.Active); var initial = DaggerfallAlterationEffects.ReadShield(effect.State);
        var player = s.State.Actors.Player; var health = player.Stats.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(0);
        Assert.Equal(0, s.State.Kit.Rules.ApplyToHealth(new(player.Actor, player.Actor, "dead"), 100, 0, health).ActualHealthLost);
        Assert.Equal(initial, DaggerfallAlterationEffects.ReadShield(effect.State));
        health.SetCurrent(health.Maximum.Value);
        var applied = s.State.Kit.Rules.ApplyToHealth(new(player.Actor, player.Actor, "magic"), initial.Remaining, 0, health);
        Assert.Equal(0, applied.ActualHealthLost); Assert.False(applied.Defeated);
        Assert.Empty(s.State.Effects.Active); Assert.Empty(player.Actor.Get<CombatContributions>().Rules);
    }

    [Fact]
    public void Destroyed_shield_item_source_removes_its_rule_and_malformed_saved_pool_is_refused()
    {
        using Fixture f = new(); var s = f.Session;
        var item = s.State.Inventory.Read().UniqueItems.First();
        ulong source = s.State.Inventory.GetDurableItemId(item.Entity).Value;
        s.Casting.Ready(1, "spell.017", source); s.ReleaseReadySpell(1, Vector3.UnitZ);
        var saved = DaggerfallSavePayload.Read(s.CaptureSave()); var effect = Assert.Single(saved.ActiveEffects);
        var malformed = saved with { ActiveEffects = [effect with { State = DaggerfallAlterationEffects.ShieldState(new(1, 2)) }] };
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(malformed)));
        malformed = saved with { ActiveEffects = [effect with { State = DaggerfallAlterationEffects.ShieldState(new(1, 0)) }] };
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(malformed)));
        // A used item's shield outlives the item breaking, as in the donor; destroying the item ends it.
        s.State.ItemInstances.ReplaceUnique(source, s.State.ItemInstances.RequireUnique(source) with { CurrentCondition = 0 });
        Assert.Single(s.State.Effects.Active);
        s.DestroyUniqueItem(source);
        Assert.Empty(s.State.Effects.Active); Assert.Empty(s.State.Actors.Player.Actor.Get<CombatContributions>().Rules);
    }

    internal static void Resistance(DaggerfallSession session, string instance, DaggerfallMagicResistanceElement element, int chance,
        uint rounds, long target = 1, ulong? item = null)
    {
        var setting = new DaggerfallSpellEffectDefinition("ward-settings", 8, (int)element, 10, 0, 1, chance, 0, 1, 0, 0, 0, 0, 1);
        var state = JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(setting, 1, 0, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        session.State.Effects.Start(new(instance, $"resist-{element.ToString().ToLowerInvariant()}", "spell.ward", 1, target, setting.Key, "Magic", item, 1, rounds, state));
    }
    private static void PrepareCast(DaggerfallSession s, string key)
    {
        s.State.Character.LearnSpell(key);
        Track magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.Maximum.BaseValue = 1000; magicka.SetCurrent(1000);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition _composition = new(TestPayload.Definitions, ReadInputs(TestData.RepositoryRoot), DaggerfallTuning.Defaults);
        internal DaggerfallSession Session { get; }
        internal Fixture() => Session = DaggerfallSession.StartNew(Engine().Context, _composition);
        private EngineContextFake Engine()
        {
            List<string> releases = [];
            ContentFake content = new(releases); PopulateContent(content, _composition.StartSite);
            var spatial = SpatialFake.Create(_composition.StartSite.SpatialArtifact.Sha256, releases);
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service, random:RandomMaximum.Create());
        }
        internal DaggerfallSession Restore(WorldRpg.Kit.RulesetSavePayload payload) => DaggerfallSession.Restore(Engine().Context, _composition, payload);
        public void Dispose() => Session.Dispose();
    }
}
