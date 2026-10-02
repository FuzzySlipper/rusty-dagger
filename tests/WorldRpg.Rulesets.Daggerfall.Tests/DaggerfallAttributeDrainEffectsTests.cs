using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallAttributeDrainEffectsTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void All_attributes_accumulate_one_source_floor_without_debt_and_restore_matching_partial_healing(int subtype)
    {
        using Fixture f = new(); var s = f.Session;
        var row = TestPayload.Definitions.Magic.RequireEffectCost(Setting(subtype, 7));
        Assert.Equal("destruction", row.School); Assert.Equal((4, 25, 2, 25), (row.Coefficient0, row.Coefficient1, row.Coefficient2, row.Coefficient3));
        Assert.Equal(new DaggerfallMagicEffectComponentCost(116, 8, 100), row.RegularComponents!.Magnitude);
        var stat = Stat(s, 1, subtype); stat.BaseValue = 50;
        Start(s, "first", subtype, 7); Start(s, "second", subtype, 8, caster: 2001);
        Assert.Equal(50, stat.BaseValue); Assert.Equal(35, stat.Value);
        Assert.Single(stat.Sources); var incumbent = Assert.Single(s.State.Effects.Active);
        Assert.Equal("first", incumbent.Context.Instance.Value);
        Assert.Null(incumbent.Lifecycle.RemainingRounds); Assert.Null(incumbent.Context.Caster);
        s.State.Effects.AdvanceElapsedRounds(100); Assert.Equal(35, stat.Value);
        Assert.Equal(0, Heal(s, (subtype + 1) % 8, 5)); Assert.Equal(35, stat.Value);
        Assert.Equal(4, Heal(s, subtype, 4)); Assert.Equal(39, stat.Value);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(39, Stat(restored, 1, subtype).Value); Assert.Equal(50, Stat(restored, 1, subtype).BaseValue);
        Start(restored, "floor", subtype, 10000);
        Assert.Equal(1, Stat(restored, 1, subtype).Value);
        Assert.Equal(49, DaggerfallAttributeDrainEffects.Read(Assert.Single(restored.State.Effects.Active).State, subtype).Magnitude);
        Assert.Equal(49, Heal(restored, subtype, 10000)); Assert.Equal(50, Stat(restored, 1, subtype).Value);
        Assert.Empty(restored.State.Effects.Active); Assert.Empty(Stat(restored, 1, subtype).Sources);
        using var healed = f.Restore(restored.CaptureSave()); Assert.Empty(healed.State.Effects.Active);
        Assert.Equal(50, Stat(healed, 1, subtype).Value);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void Real_compiled_casts_apply_partial_saves_to_initial_and_incumbent_payloads_once(int subtype)
    {
        using Fixture f = new(); var s = f.Session; const long target = 2000;
        Stat(s, target, subtype).BaseValue = 50;
        var setting = Setting(subtype, 10);
        var spell = TestPayload.Definitions.Magic.Spells["spell.009"] with { Key = "drain.test", Effects = [setting] };
        var casting = CastingFor(s, spell, 45);
        for (int i = 0; i < 2; i++)
        {
            Fund(s, 1); Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
            var bundle = casting.Release(1, true).Bundle!; casting.Deliver(bundle, [target]);
            var result = Assert.Single(bundle.Results);
            Assert.Equal(i == 0 ? DaggerfallCastOutcome.Applied : DaggerfallCastOutcome.Refreshed, result.Outcome);
            Assert.Equal(50, result.SavePercent); Assert.Equal(50 - 5 * (i + 1), Stat(s, target, subtype).Value);
            Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, casting.Deliver(bundle, [target]).Outcome);
        }
        Assert.Single(s.State.Effects.Active); Assert.Single(Stat(s, target, subtype).Sources);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(40, Stat(restored, target, subtype).Value);
    }

    [Fact]
    public void Published_strength_leech_is_delivered_paid_and_visible_through_existing_stats()
    {
        using Fixture f = new(); var s = f.Session; const long target = 2000;
        Fund(s, 1); s.State.Character.LearnSpell("spell.009");
        double before = Stat(s, target, 0).Value, magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).Current;
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell("spell.009").Outcome);
        var bundle = s.Casting.Release(1, true).Bundle!; s.Casting.Deliver(bundle, [target]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        Assert.True(Stat(s, target, 0).Value < before);
        Assert.True(s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).Current < magicka);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(Stat(s, target, 0).Value, Stat(restored, target, 0).Value);
    }

    [Fact]
    public void Retiring_actual_caster_and_breaking_cast_item_preserves_result_and_historical_origin_in_current_save()
    {
        using Fixture f = new(); var s = f.Session;
        long caster = s.SpawnActor("rat", new(new(11, 0, 11), 0));
        var item = s.State.Inventory.Read().UniqueItems.First(); ulong id = s.State.Inventory.GetDurableItemId(item.Entity).Value;
        var setting = Setting(0, 7);
        var spell = TestPayload.Definitions.Magic.Spells["spell.009"] with { Key = "drain.item", Effects = [setting] };
        var casting = CastingFor(s, spell, 100); Fund(s, caster);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(caster, spell.Key, id, DaggerfallCastSource.ItemStrike).Outcome);
        var bundle = casting.Release(caster, true).Bundle!; casting.Deliver(bundle, [1]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        double value = Stat(s, 1, 0).Value;
        s.RetireActor(caster); s.State.Equipment.Unequip(new(item.Entity.Value, new(item.Definition.Value))); s.State.ItemInstances.ReplaceUnique(id, s.State.ItemInstances.RequireUnique(id) with { CurrentCondition = 0 });
        var effect = Assert.Single(s.State.Effects.Active);
        Assert.Null(effect.Context.Caster); Assert.Null(effect.Context.Item);
        Assert.Equal(new DaggerfallCastOrigin(caster, id, DaggerfallCastSource.ItemStrike), DaggerfallAttributeDrainEffects.Read(effect.State, 0).Cast.Origin);
        Assert.Equal(value, Stat(s, 1, 0).Value);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(value, Stat(restored, 1, 0).Value);
        Assert.Single(restored.State.Effects.Active); Assert.Equal(7, Heal(restored, 0, 7));
    }

    [Fact]
    public void Target_retirement_cleans_sources_and_other_conditions_do_not_share_the_drain_heal_key()
    {
        using Fixture f = new(); var s = f.Session; long target = s.SpawnActor("rat", new(new(11, 0, 11), 0));
        Start(s, "retired", 0, 3, target: target); s.RetireActor(target); Assert.Empty(s.State.Effects.Active);
        s.State.Progression.AdvanceTo(0, 100);
        var health = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")); health.Maximum.BaseValue = 10000; health.SetCurrent(10000);
        Start(s, "strength", 0, 7); Start(s, "willpower", 2, 7);
        Assert.Equal(DaggerfallDiseaseAdmission.Started, s.InflictDisease(new("brain", "monster", 2000, 1, [DaggerfallClassicDisease.BrainFever])));
        Assert.True(s.State.Poisons.Afflict(s.State.Actors.Player.Actor, (int)DaggerfallPoisonVariant.Arsenic));
        s.AdvanceElapsedTime(86400);
        double diseaseLoss = Stat(s, 1, 2).BaseValue - Stat(s, 1, 2).Value - 7;
        Assert.True(diseaseLoss > 0); Assert.Equal(7, Heal(s, 2, 99));
        Assert.Equal(diseaseLoss, Stat(s, 1, 2).BaseValue - Stat(s, 1, 2).Value);
        DaggerfallDiseasePolicy.CureAllDiseases(s.State.Effects, 1); s.State.Poisons.Cure(s.State.Actors.Player.Actor);
        Assert.Equal("strength", Assert.Single(s.State.Effects.Active).Context.Instance.Value);
        Assert.Equal(7, Stat(s, 1, 0).BaseValue - Stat(s, 1, 0).Value);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(4)]
    public void Player_derived_vital_maxima_follow_live_attributes_and_partial_healing(int subtype)
    {
        using Fixture f = new(); var s = f.Session;
        string trackName = subtype == 1 ? "magicka" : "stamina";
        var track = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse(trackName)); double before = track.Maximum.Value;
        Start(s, "vitals", subtype, 10); double reduced = track.Maximum.Value; Assert.True(reduced < before);
        Heal(s, subtype, 5); Assert.True(track.Maximum.Value > reduced); Assert.True(track.Maximum.Value < before);
        Heal(s, subtype, 99); Assert.Equal(before, track.Maximum.Value);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Malformed_current_state_or_operational_lifetime_is_refused(int malformed)
    {
        using Fixture f = new(); var s = f.Session; Start(s, "bad", 0, 7);
        var saved = DaggerfallSavePayload.Read(s.CaptureSave()); var effect = Assert.Single(saved.ActiveEffects);
        var state = DaggerfallAttributeDrainEffects.Read(effect.State, 0);
        var bad = malformed switch
        {
            0 => effect with { State = DaggerfallAttributeDrainEffects.Encode(state with { Magnitude = 6 }) },
            1 => effect with { RemainingRounds = 1 },
            _ => effect with { CasterId = 2000 },
        };
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(saved with { ActiveEffects = [bad] })));
    }

    private static DaggerfallSpellEffectDefinition Setting(int subtype, int magnitude) => new("drain", 7, subtype, 0, 0, 1, 0, 0, 1, magnitude, magnitude, 0, 0, 1);
    private static void Start(DaggerfallSession s, string instance, int subtype, int amount, long target = 1, long caster = 2000) =>
        s.State.Effects.Start(new(instance, DaggerfallAttributeDrainEffects.Key(subtype), "spell.drain", null, target, "drain", "Magic", null, 1, null,
            DaggerfallAttributeDrainEffects.Encode(new(new(Setting(subtype, amount), 1, amount, 100, new(caster, null, DaggerfallCastSource.Spell)), amount))));
    private static int Heal(DaggerfallSession s, int subtype, int amount) => DaggerfallAttributeDrainEffects.Heal(s.State.Effects, 1,
        DaggerfallAttributeDrainEffects.Attributes[subtype], amount, () => s.State.Character.Career);
    private static Stat Stat(DaggerfallSession s, long id, int subtype) => (id == 1 ? s.State.Actors.Player.Stats : s.State.Actors.Get(id).Stats)
        .GetStat(StatId.Parse(DaggerfallAttributeDrainEffects.Attributes[subtype].Value));
    private static void Fund(DaggerfallSession s, long id)
    { var track = (id == 1 ? s.State.Actors.Player.Stats : s.State.Actors.Get(id).Stats).GetTrack(TrackId.Parse("magicka")); track.Maximum.BaseValue = 10000; track.SetCurrent(10000); }
    private static DaggerfallCasting CastingFor(DaggerfallSession s, DaggerfallSpellDefinition spell, int saveRoll)
    {
        var catalog = TestPayload.Definitions.Magic with { Spells = new Dictionary<string, DaggerfallSpellDefinition> { [spell.Key] = spell } };
        return new(catalog, s.State.Effects, id => id == 1 ? s.State.Actors.Player.Actor : s.State.Actors.TryGet(id, out var actor) ? actor.Actor : null,
            id => s.MagicProfile(id) with { LiveWillpower = 50, PlayerRace = null, BiographyMagicResistance = 0 },
            _ => true, _ => { }, _ => { }, DaggerfallCastingTests.SaveDice.Create(saveRoll), 1, playerKnowsSpell: _ => true, casterLevel: _ => 1);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition _composition = new(TestPayload.Definitions, ReadInputs(TestData.RepositoryRoot), DaggerfallTuning.Defaults);
        internal DaggerfallSession Session { get; }
        internal Fixture() => Session = DaggerfallSession.StartNew(Engine().Context, _composition);
        private EngineContextFake Engine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, _composition.StartSite);
            var spatial = SpatialFake.Create(_composition.StartSite.SpatialArtifact.Sha256, releases);
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service, random: RandomMaximum.Create());
        }
        internal DaggerfallSession Restore(RulesetSavePayload payload) => DaggerfallSession.Restore(Engine().Context, _composition, payload);
        public void Dispose() => Session.Dispose();
    }
}
