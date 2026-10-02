using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallHealingEffectsTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void Every_compiled_attribute_heal_selects_only_its_drain_and_current_save_never_replays(int subtype)
    {
        using Fixture f = new(); var s = f.Session;
        var stat = Stat(s, subtype); stat.BaseValue = 50;
        Drain(s, "matching", subtype, 10); Drain(s, "other", (subtype + 1) % 8, 8);
        Assert.Equal(40, stat.Value);
        var spell = Spell(subtype, 4); var casting = Casting(s, spell); Fund(s);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
        var bundle = casting.Release(1, true).Bundle!; casting.Deliver(bundle, [1]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        Assert.Equal(44, stat.Value); Assert.Equal(50, stat.BaseValue); Assert.Equal(2, s.State.Effects.Active.Count());
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(44, Stat(restored, subtype).Value);
        restored.State.Effects.AdvanceElapsedRounds(100); Assert.Equal(44, Stat(restored, subtype).Value);
        Assert.Equal(DaggerfallCastOutcome.Applied, Heal(restored, subtype, 99)); Assert.Equal(50, Stat(restored, subtype).Value);
        Assert.Equal("other", Assert.Single(restored.State.Effects.Active).Context.Instance.Value);
        Assert.Equal(DaggerfallCastOutcome.NoMatch, Heal(restored, subtype, 1));
        var row = TestPayload.Definitions.Magic.RequireEffectCost(spell.Effects[0]);
        Assert.Equal("restoration", row.School); Assert.Equal(new DaggerfallMagicEffectComponentCost(0, 40, 28), row.RegularComponents!.Magnitude);
    }

    [Theory]
    [InlineData(8, "health", 10)] [InlineData(9, "stamina", 640)]
    public void Vital_healing_uses_bounded_tracks_and_actual_initial_delivery(int subtype, string trackName, int amount)
    {
        using Fixture f = new(); var s = f.Session; var track = Track(s, trackName); track.Maximum.BaseValue = 1000; track.SetCurrent(1);
        Assert.Equal(DaggerfallCastOutcome.Applied, Heal(s, subtype, 10)); Assert.Equal(1 + amount, track.Current);
        track.SetCurrent(999); Assert.Equal(DaggerfallCastOutcome.Applied, Heal(s, subtype, 10)); Assert.Equal(1000, track.Current);
        using var restored = f.Restore(s.CaptureSave()); Assert.Empty(restored.State.Effects.Active);
        Assert.Equal(1000, Track(restored, trackName).Current); restored.State.Effects.AdvanceElapsedRounds(100); Assert.Equal(1000, Track(restored, trackName).Current);
        Track(s, "health").SetCurrent(0);
        Assert.Equal(DaggerfallEffectAdmissionOutcome.TargetUnavailable, Direct(s, subtype, 10));
        Assert.Equal(0, Track(s, "health").Current); Assert.Empty(s.State.Effects.Active);
    }

    [Fact]
    public void Published_health_and_fatigue_spells_use_the_ordinary_paid_casting_path()
    {
        using Fixture f = new(); var s = f.Session;
        foreach (int subtype in new[] { 8, 9 })
        {
            var spell = TestPayload.Definitions.Magic.Spells.Values.First(spell => spell.Element == 4 && spell.RangeType == 0
                && spell.Effects.Count == 1 && spell.Effects[0].Type == 10 && spell.Effects[0].SubType == subtype);
            var track = Track(s, subtype == 8 ? "health" : "stamina"); track.SetCurrent(1); Fund(s); s.State.Character.LearnSpell(spell.Key);
            Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell(spell.Key).Outcome);
            var bundle = s.Casting.Release(1, true).Bundle!; s.Casting.Deliver(bundle, [1]);
            Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome); Assert.True(track.Current > 1); Assert.Empty(s.State.Effects.Active);
        }
    }

    [Fact]
    public void Matching_healing_does_not_cure_disease_or_poison_or_their_stat_sources()
    {
        using Fixture f = new(); var s = f.Session;
        s.State.Progression.AdvanceTo(0, 100); Track(s, "health").Maximum.BaseValue = 10000; Track(s, "health").SetCurrent(10000);
        Drain(s, "willpower", 2, 7);
        Assert.Equal(DaggerfallDiseaseAdmission.Started, s.InflictDisease(new("brain", "monster", 2000, 1, [DaggerfallClassicDisease.BrainFever])));
        s.AdvanceElapsedTime(86400); Assert.True(s.State.Poisons.Afflict(s.State.Actors.Player.Actor, (int)DaggerfallPoisonVariant.Arsenic));
        double before = Stat(s, 2).Value; Assert.Equal(DaggerfallCastOutcome.Applied, Heal(s, 2, 99)); Assert.Equal(before + 7, Stat(s, 2).Value);
        Assert.True(s.State.Poisons.IsAfflicted(s.State.Actors.Player.Actor) || s.State.Poisons.HasPersistingDamage(s.State.Actors.Player.Actor)); Assert.NotEmpty(s.State.Effects.Active);
        Assert.Equal(DaggerfallCastOutcome.NoMatch, Heal(s, 2, 99));
    }

    [Fact]
    public void Potion_only_spell_points_have_no_classic_binding_bound_self_payload_and_no_saved_replay()
    {
        using Fixture f = new(); var s = f.Session;
        Track(s, "magicka").SetCurrent(0);
        Assert.Equal(DaggerfallEffectAdmissionOutcome.Started, Potion(s, 7)); Assert.Equal(7, Track(s, "magicka").Current);
        Assert.Equal(DaggerfallEffectAdmissionOutcome.Started, Potion(s, 10000)); Assert.Equal(Track(s, "magicka").Maximum.Value, Track(s, "magicka").Current);
        using var restored = f.Restore(s.CaptureSave()); Assert.Empty(restored.State.Effects.Active);
        Assert.Equal(Track(s, "magicka").Current, Track(restored, "magicka").Current);
        restored.State.Effects.AdvanceElapsedRounds(100); Assert.Equal(Track(s, "magicka").Current, Track(restored, "magicka").Current);
        Assert.Throws<ArgumentException>(() => s.State.Effects.Start(new("invalid", "heal-spell-points", "potion", 2000, 1, "potion", "Magic", null, 1, 1, DaggerfallHealingEffects.SpellPointState(7))));
        Track(s, "health").SetCurrent(0); double before = Track(s, "magicka").Current;
        Assert.Equal(DaggerfallEffectAdmissionOutcome.TargetUnavailable, Potion(s, 10)); Assert.Equal(before, Track(s, "magicka").Current);
    }

    [Fact]
    public void Retired_target_and_invalid_current_instant_save_are_explicitly_refused()
    {
        using Fixture f = new(); var s = f.Session;
        long target = s.SpawnActor("rat", new(new(11, 0, 11), 0)); var spell = Spell(8, 10) with { RangeType = 1 }; var casting = Casting(s, spell); Fund(s);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome); var bundle = casting.Release(1, true).Bundle!;
        s.RetireActor(target); casting.Deliver(bundle, [target]); Assert.Equal(DaggerfallCastOutcome.TargetUnavailable, Assert.Single(bundle.Results).Outcome);
        var save = DaggerfallSavePayload.Read(s.CaptureSave());
        // An instant family cannot be forged into a resumable save by retaining an ordinary lifecycle record.
        Drain(s, "saved", 0, 1); var active = Assert.Single(DaggerfallSavePayload.Read(s.CaptureSave()).ActiveEffects);
        var instant = active with { EffectKey = "heal-health", RemainingRounds = 1,
            State = JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(Setting(8, 10), 1, 10, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState) };
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(save with { ActiveEffects = [instant] })));
    }

    private static DaggerfallSpellEffectDefinition Setting(int subtype, int amount) => new("heal", 10, subtype, 0, 0, 1, 0, 0, 1, amount, amount, 0, 0, 1);
    private static DaggerfallSpellDefinition Spell(int subtype, int amount) => new("heal.test", 1, false, "Heal", 4, 0, 0, 0, [Setting(subtype, amount)]);
    private static DaggerfallCasting Casting(DaggerfallSession s, DaggerfallSpellDefinition spell) => new(
        TestPayload.Definitions.Magic with { Spells = new Dictionary<string, DaggerfallSpellDefinition> { [spell.Key] = spell } }, s.State.Effects,
        id => id == 1 ? s.State.Actors.Player.Actor : s.State.Actors.TryGet(id, out var actor) ? actor.Actor : null,
        s.MagicProfile, _ => true, _ => { }, _ => { }, RandomMinimum.Create(), 1, playerKnowsSpell: _ => true, casterLevel: _ => 1);
    private static DaggerfallCastOutcome Heal(DaggerfallSession s, int subtype, int amount)
    {
        var spell = Spell(subtype, amount); var casting = Casting(s, spell); Fund(s);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome); var bundle = casting.Release(1, true).Bundle!;
        casting.Deliver(bundle, [1]); return Assert.Single(bundle.Results).Outcome;
    }
    private static DaggerfallEffectAdmissionOutcome Direct(DaggerfallSession s, int subtype, int amount) => s.State.Effects.Start(new(
        "direct", subtype == 8 ? "heal-health" : "heal-fatigue", "spell", 1, 1, "heal", "Magic", null, 1, 1,
        JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(Setting(subtype, amount), 1, amount, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)));
    private static DaggerfallEffectAdmissionOutcome Potion(DaggerfallSession s, int amount) => s.State.Effects.Start(new(
        "potion", "heal-spell-points", "potion", 1, 1, "restorePower", "Magic", null, 1, 1, DaggerfallHealingEffects.SpellPointState(amount)));
    private static void Drain(DaggerfallSession s, string instance, int subtype, int amount) => s.State.Effects.Start(new(
        instance, DaggerfallAttributeDrainEffects.Key(subtype), "drain", null, 1, "drain", "Magic", null, 1, null,
        DaggerfallAttributeDrainEffects.Encode(new(new(new("drain", 7, subtype, 0, 0, 1, 0, 0, 1, amount, amount, 0, 0, 1), 1, amount, 100, new(2000, null, DaggerfallCastSource.Spell)), amount))));
    private static Track Track(DaggerfallSession s, string name) => s.State.Actors.Player.Stats.GetTrack(TrackId.Parse(name));
    private static Stat Stat(DaggerfallSession s, int subtype) => s.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallAttributeDrainEffects.Attributes[subtype].Value));
    private static void Fund(DaggerfallSession s) { Track(s, "magicka").Maximum.BaseValue = 10000; Track(s, "magicka").SetCurrent(10000); }
    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition _composition = new(TestPayload.Definitions, ReadInputs(TestData.RepositoryRoot), DaggerfallTuning.Defaults);
        internal DaggerfallSession Session { get; }
        internal Fixture() => Session = DaggerfallSession.StartNew(Engine().Context, _composition);
        private EngineContextFake Engine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, _composition.StartSite);
            return EngineContextFake.Create(content, SpatialFake.Create(_composition.StartSite.SpatialArtifact.Sha256, releases).Service,
                new AppearanceFake(releases), PerceptionFake.Create().Service, random: RandomMaximum.Create());
        }
        internal DaggerfallSession Restore(RulesetSavePayload payload) => DaggerfallSession.Restore(Engine().Context, _composition, payload);
        public void Dispose() => Session.Dispose();
    }
}
