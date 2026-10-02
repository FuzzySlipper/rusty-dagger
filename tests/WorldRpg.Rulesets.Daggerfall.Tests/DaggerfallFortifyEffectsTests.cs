using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallFortifyEffectsTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void All_eight_attributes_keep_equivalent_magnitude_extend_duration_and_resume_without_duplication(int subtype)
    {
        using Fixture f = new(); var s = f.Session; var stat = Stat(s, 1, subtype); stat.BaseValue = 50;
        Assert.Equal(DaggerfallEffectAdmissionOutcome.Started, Start(s, "first", subtype, 7, 10));
        Assert.Equal(57, stat.Value); Assert.Equal(50, stat.BaseValue); Assert.Single(stat.Sources);
        Assert.Equal(DaggerfallEffectAdmissionOutcome.Refreshed, Start(s, "same", subtype, 99, 10, settingsAmount: 7, caster: 2001));
        var effect = Assert.Single(s.State.Effects.Active); Assert.Equal("first", effect.Context.Instance.Value); Assert.Equal(19u, effect.Lifecycle.RemainingRounds);
        Assert.Equal(57, stat.Value); Assert.Single(stat.Sources);
        Assert.Equal(DaggerfallEffectAdmissionOutcome.Started, Start(s, "different", subtype, 3, 10));
        Assert.Equal(60, stat.Value); Assert.Equal(2, stat.Sources.Count());
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(60, Stat(restored, 1, subtype).Value); Assert.Equal(2, Stat(restored, 1, subtype).Sources.Count());
        restored.State.Effects.AdvanceElapsedRounds(9); Assert.Equal(57, Stat(restored, 1, subtype).Value);
        restored.State.Effects.AdvanceElapsedRounds(10); Assert.Equal(50, Stat(restored, 1, subtype).Value);
        Assert.Empty(restored.State.Effects.Active); Assert.Empty(Stat(restored, 1, subtype).Sources);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void Ordinary_paid_casts_all_variants_expose_bounded_stats_and_cancel_only_their_source(int subtype)
    {
        using Fixture f = new(); var s = f.Session; var stat = Stat(s, 1, subtype); stat.BaseValue = 95;
        var spell = new DaggerfallSpellDefinition("fortify.test", 1, false, "Fortify", 4, 0, 0, 0, [Setting(subtype, 20)]);
        var casting = new DaggerfallCasting(TestPayload.Definitions.Magic with { Spells = new Dictionary<string, DaggerfallSpellDefinition> { [spell.Key] = spell } },
            s.State.Effects, id => id == 1 ? s.State.Actors.Player.Actor : s.State.Actors.TryGet(id, out var actor) ? actor.Actor : null,
            s.MagicProfile, _ => true, _ => { }, _ => { }, RandomMinimum.Create(), 1, playerKnowsSpell: _ => true, casterLevel: _ => 1);
        Fund(s); Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
        var bundle = casting.Release(1, true).Bundle!; casting.Deliver(bundle, [1]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome); Assert.Equal(100, stat.Value); Assert.Equal(95, stat.BaseValue);
        Assert.Equal(100, stat.Maximum); Assert.Single(stat.Sources);
        Start(s, "other", (subtype + 1) % 8, 3, 10);
        Assert.True(s.State.Effects.Cancel(EffectInstanceId.Parse(bundle.Results[0].Instance!))); Assert.Equal(95, stat.Value); Assert.Empty(stat.Sources);
        Assert.Equal("other", Assert.Single(s.State.Effects.Active).Context.Instance.Value);
        var row = TestPayload.Definitions.Magic.RequireEffectCost(spell.Effects[0]); Assert.Equal("restoration", row.School);
        Assert.Equal(new DaggerfallMagicEffectComponentCost(0, 28, 100), row.RegularComponents!.Duration);
        Assert.Equal(new DaggerfallMagicEffectComponentCost(0, 40, 120), row.RegularComponents.Magnitude);
    }

    [Fact]
    public void Published_orc_strength_updates_canonical_vital_maxima_and_restores_live_sources()
    {
        using Fixture f = new(); var s = f.Session; var stat = Stat(s, 1, 0); stat.BaseValue = 50;
        DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(s.State.Actors.Player.Stats, s.State.Character.Career);
        var track = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")); double maximum = track.Maximum.Value;
        Fund(s); s.State.Character.LearnSpell("spell.075"); Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell("spell.075").Outcome);
        var bundle = s.Casting.Release(1, true).Bundle!; s.Casting.Deliver(bundle, [1]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome); Assert.True(stat.Value > 50); Assert.True(track.Maximum.Value > maximum);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(stat.Value, Stat(restored, 1, 0).Value);
        Assert.Equal(track.Maximum.Value, restored.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Maximum.Value);
        s.State.Effects.Cancel(EffectInstanceId.Parse(bundle.Results[0].Instance!)); Assert.Equal(50, stat.Value); Assert.Equal(maximum, track.Maximum.Value);
    }

    [Fact]
    public void Cancellation_and_retirement_remove_only_owned_contribution_and_keep_drain()
    {
        using Fixture f = new(); var s = f.Session; Stat(s, 1, 0).BaseValue = 50;
        s.State.Effects.Start(new("drain", "drain-strength", "drain", null, 1, "drain", "Magic", null, 1, null,
            DaggerfallAttributeDrainEffects.Encode(new(new(new("drain", 7, 0, 0, 0, 1, 0, 0, 1, 8, 8, 0, 0, 1), 1, 8, 100, new(2000, null, DaggerfallCastSource.Spell)), 8))));
        Start(s, "buff", 0, 7, 10); Assert.Equal(49, Stat(s, 1, 0).Value);
        s.State.Effects.CancelSource("fortify"); Assert.Equal(42, Stat(s, 1, 0).Value); Assert.Equal("drain", Assert.Single(s.State.Effects.Active).Context.Instance.Value);
        long target = s.SpawnActor("rat", new(new(11, 0, 11), 0)); Start(s, "retired", 0, 3, 10, target: target); s.RetireActor(target);
        Assert.DoesNotContain(s.State.Effects.Active, effect => effect.Context.Target.Value == (ulong)target);
        long caster = s.SpawnActor("rat", new(new(11, 0, 11), 0)); Start(s, "source-retired", 0, 3, 10, caster: caster);
        s.RetireActor(caster); Assert.Equal(42, Stat(s, 1, 0).Value); Assert.Single(Stat(s, 1, 0).Sources);
    }

    [Fact]
    public void Inconsistent_saved_source_magnitude_is_refused_instead_of_reapplied()
    {
        using Fixture f = new(); var s = f.Session; Start(s, "bad", 0, 7, 10);
        var saved = DaggerfallSavePayload.Read(s.CaptureSave()); var effect = Assert.Single(saved.ActiveEffects);
        var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)!;
        var invalid = effect with { State = JsonSerializer.SerializeToElement(state with { Amount = 6 }, DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState) };
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(saved with { ActiveEffects = [invalid] })));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void Altered_current_attribute_bounds_cannot_restore_an_unbounded_consumer(int subtype)
    {
        using Fixture f = new();
        var saved = DaggerfallStatsSaveBoundary.Capture(f.Session.State.Actors.Player.Stats, f.Session.State.Actors.Player.Actor.Entity);
        string attribute = DaggerfallMechanicsIds.Attributes[subtype].Value;
        var invalid = saved with { Snapshot = saved.Snapshot with
            { Stats = saved.Snapshot.Stats.Select(stat => stat.Id == attribute ? stat with { Maximum = 10000 } : stat).ToArray() } };
        var error = Assert.Throws<ArgumentException>(() => DaggerfallStatsSaveBoundary.Restore(invalid, f.Session.State.Actors.Player.Actor.Entity));
        Assert.Contains(attribute, error.Message); Assert.Contains("canonical live bounds", error.Message);
    }

    private static DaggerfallSpellEffectDefinition Setting(int subtype, int amount) => new("fortify", 9, subtype, 10, 0, 1, 0, 0, 1, amount, amount, 0, 0, 1);
    private static DaggerfallEffectAdmissionOutcome Start(DaggerfallSession s, string instance, int subtype, int amount, uint rounds, int? settingsAmount = null, long target = 1, long caster = 2000) => s.State.Effects.Start(new(
        instance, DaggerfallFortifyEffects.Key(subtype), "fortify", caster, target, "fortify", "Magic", null, 1, rounds,
        JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(Setting(subtype, settingsAmount ?? amount), 1, amount, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)));
    private static Stat Stat(DaggerfallSession s, long id, int subtype) => (id == 1 ? s.State.Actors.Player.Stats : s.State.Actors.Get(id).Stats)
        .GetStat(StatId.Parse(DaggerfallMechanicsIds.Attributes[subtype].Value));
    private static void Fund(DaggerfallSession s) { var track = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); track.Maximum.BaseValue = 10000; track.SetCurrent(10000); }
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
