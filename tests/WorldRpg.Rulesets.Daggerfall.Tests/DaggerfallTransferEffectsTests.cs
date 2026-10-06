using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallTransferEffectsTests
{
    [Theory]
    [InlineData(8, "health", 10)] [InlineData(9, "stamina", 640)]
    public void Transfer_uses_full_admitted_amount_after_bounded_target_loss_and_caster_overflow(int subtype, string track, int amount)
    {
        using Harness h = new(subtype);
        h.Track(1, track).SetCurrent(100); h.Track(2, track).SetCurrent(3);
        var bundle = h.Release(); h.Casting.Deliver(bundle, [2]);
        var result = Assert.Single(h.Results);
        Assert.Equal(3, result.ActualLoss); Assert.Equal(amount, result.Restored);
        Assert.Equal(100 + amount, h.Track(1, track).Current); Assert.Equal(0, h.Track(2, track).Current);
        Assert.Equal(subtype == 8, result.TargetDefeated); Assert.Empty(h.Effects.Active);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, h.Casting.Deliver(bundle, [2]).Outcome); Assert.Single(h.Results);
        h.Track(2, "health").SetCurrent(1000); h.Track(2, track).SetCurrent(1000); h.Track(1, track).SetCurrent(999);
        h.Casting.Deliver(h.Release(), [2]); Assert.Equal(1, h.Results.Last().Restored); Assert.Equal(1000, h.Track(1, track).Current);
    }

    [Fact]
    public void Shield_reduces_health_loss_without_reducing_classic_caster_recovery()
    {
        using Harness h = new(8); h.Track(1, "health").SetCurrent(100);
        h.Effects.Start(new("shield", "shield", "spell.shield", 2, 2, "shield", null, null, 1, 10,
            DaggerfallAlterationEffects.ShieldState(new(20, 20))));
        h.Casting.Deliver(h.Release(), [2]); var result = Assert.Single(h.Results);
        Assert.Equal(0, result.ActualLoss); Assert.Equal(10, result.Restored);
        Assert.Equal(1000, h.Track(2, "health").Current); Assert.Equal(110, h.Track(1, "health").Current);
        Assert.Equal(10, DaggerfallAlterationEffects.ReadShield(Assert.Single(h.Effects.Active).State).Remaining);
    }

    [Fact]
    public void Peaceful_fatigue_target_has_no_loss_but_transfer_still_restores_and_requests_aggression()
    {
        using Harness h = new(9); h.Hostile = false; h.Track(1, "stamina").SetCurrent(100);
        h.Casting.Deliver(h.Release(), [2]);
        Assert.Equal(0, Assert.Single(h.Results).ActualLoss); Assert.Equal(640, h.Results[0].Restored);
        Assert.Equal(1000, h.Track(2, "stamina").Current); Assert.Single(h.Attacks);
        h.Hostile = true; h.Casting.Deliver(h.Release(), [2]); Assert.Equal(640, h.Results.Last().ActualLoss);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void A_dead_source_or_target_is_reported_before_any_transfer(bool source)
    {
        using Harness h = new(8); var bundle = h.Release(); h.Track(source ? 1 : 2, "health").SetCurrent(0);
        h.Casting.Deliver(bundle, [2]); Assert.Empty(h.Results);
        Assert.Equal(source ? DaggerfallCastOutcome.SourceUnavailable : DaggerfallCastOutcome.TargetUnavailable, Assert.Single(bundle.Results).Outcome);
        Assert.Empty(h.Effects.Active);
    }

    [Theory]
    [InlineData(null)] [InlineData(99L)]
    public void A_direct_request_without_an_actual_caster_never_uses_the_target_as_caster(long? caster)
    {
        using Harness h = new(8); h.Track(2, "health").SetCurrent(100);
        List<DaggerfallEffectOutcome> outcomes = []; h.Effects.Completed += outcomes.Add;
        var state = JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(h.Setting, 1, 10, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        var outcome = h.Effects.Start(new("missing", "transfer-health", "spell.transfer", caster, 2, "setting", "Magic", null, 1, 1, state));
        Assert.Equal(DaggerfallEffectAdmissionOutcome.SourceUnavailable, outcome);
        Assert.Contains(outcomes, value => value.Kind == DaggerfallEffectOutcomeKind.SourceUnavailable);
        Assert.Equal(100, h.Track(2, "health").Current); Assert.Empty(h.Results); Assert.Empty(h.Effects.Active);
    }

    [Fact]
    public void A_direct_request_with_a_defeated_target_has_no_loss_or_caster_recovery()
    {
        using Harness h = new(8); h.Track(1, "health").SetCurrent(100); h.Track(2, "health").SetCurrent(0);
        var state = JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(h.Setting, 1, 10, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        Assert.Equal(DaggerfallEffectAdmissionOutcome.TargetUnavailable,
            h.Effects.Start(new("dead", "transfer-health", "spell.transfer", 1, 2, "setting", "Magic", null, 1, 1, state)));
        Assert.Equal(100, h.Track(1, "health").Current); Assert.Empty(h.Results); Assert.Empty(h.Effects.Active);
    }

    [Fact]
    public void Lethal_self_reflection_has_one_terminal_result_and_cannot_resurrect_the_caster()
    {
        using Harness h = new(8, count: 2); h.Defense = new(0, 100, []); h.Track(1, "health").SetCurrent(5);
        h.Effects.Start(new("ward", "ward", "spell.ward", 2, 2, "ward", null, null, 1, 10, JsonSerializer.SerializeToElement(new { })));
        var bundle = h.Release(); h.Casting.Deliver(bundle, [2]);
        var result = Assert.Single(h.Results); Assert.Same(h.Player, result.Caster); Assert.Same(h.Player, result.Target);
        Assert.True(result.TargetDefeated); Assert.Equal(0, result.Restored); Assert.Equal(0, h.Track(1, "health").Current);
        Assert.Contains(bundle.Results, value => value.Outcome == DaggerfallCastOutcome.SourceUnavailable);
        Assert.Equal(1000, h.Track(2, "health").Current);
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(4, 0)]
    public void Unsupported_element_or_self_only_target_refuses_before_payment(int element, int range)
    {
        using Harness h = new(8, element: element, range: range);
        Assert.Equal(DaggerfallCastOutcome.UnsupportedEffect, h.Casting.Ready(1, "spell").Outcome);
        Assert.Equal(1000, h.Track(1, "magicka").Current); Assert.Empty(h.Results);
    }

    [Theory]
    [InlineData("spell.049", "health", true)] [InlineData("spell.063", "stamina", false)]
    public void Published_transfer_delivers_through_real_session_and_restores_current_save_without_replay(string key, string track, bool lethal)
    {
        using Fixture f = new(); var s = f.Session; const long target = 2000;
        var sourceTrack = Track(s, 1, track); var targetTrack = Track(s, target, track);
        sourceTrack.SetCurrent(1); targetTrack.Maximum.BaseValue = 1000; targetTrack.SetCurrent(1); Fund(s, 1);
        s.State.Character.LearnSpell(key); Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell(key).Outcome);
        var bundle = s.Casting.Release(1, true).Bundle!; s.Casting.Deliver(bundle, [target]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        Assert.True(sourceTrack.Current > 1); Assert.Equal(0, targetTrack.Current); Assert.Empty(s.State.Effects.Active);
        double sourceAfter = sourceTrack.Current, targetAfter = targetTrack.Current;
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, s.Casting.Deliver(bundle, [target]).Outcome);
        s.Update(new ProductUpdate(OuterUpdate(1), [])); Assert.Equal(lethal, s.Corpses.ContainsKey(target));
        using var restored = f.Restore(s.CaptureSave()); Assert.Empty(restored.State.Effects.Active);
        // Ordinary update may recover fatigue; compare the captured canonical values, not the pre-update value.
        Assert.Equal(Track(s, 1, track).Current, Track(restored, 1, track).Current);
        Assert.Equal(Track(s, target, track).Current, Track(restored, target, track).Current);
        restored.State.Effects.AdvanceElapsedRounds(100);
        Assert.Equal(Track(s, 1, track).Current, Track(restored, 1, track).Current);
        Assert.Equal(Track(s, target, track).Current, Track(restored, target, track).Current);
        Assert.Equal(lethal, restored.Corpses.ContainsKey(target));
        Assert.True(sourceAfter > 1); Assert.Equal(0, targetAfter);
    }

    [Fact]
    public void Actual_spawned_caster_retirement_between_release_and_delivery_reports_unavailable()
    {
        using Fixture f = new(); var s = f.Session;
        long caster = s.SpawnActor("rat", new(new(11, 0, 11), 0)); Fund(s, caster);
        Assert.Equal(DaggerfallCastOutcome.Ready, s.Casting.Ready(caster, "spell.049").Outcome);
        var bundle = s.Casting.Release(caster, true).Bundle!; double health = Track(s, 1, "health").Current;
        s.RetireActor(caster); s.Casting.Deliver(bundle, [1]);
        Assert.Equal(DaggerfallCastOutcome.SourceUnavailable, Assert.Single(bundle.Results).Outcome);
        Assert.Equal(health, Track(s, 1, "health").Current); Assert.Empty(s.State.Effects.Active);
    }

    private static Track Track(DaggerfallSession s, long id, string name) => (id == 1 ? s.State.Actors.Player.Stats : s.State.Actors.Get(id).Stats).GetTrack(TrackId.Parse(name));
    private static void Fund(DaggerfallSession s, long id) { var track = Track(s, id, "magicka"); track.Maximum.BaseValue = 10000; track.SetCurrent(10000); }
    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition _composition = new(TestPayload.Definitions, ReadInputs(TestData.RepositoryRoot), DaggerfallTuning.Defaults);
        internal DaggerfallSession Session { get; }
        internal Fixture() => Session = DaggerfallSession.StartNew(Engine().Context, _composition);
        private EngineContextFake Engine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, _composition.StartSite);
            return EngineContextFake.Create(content, SpatialFake.Create(_composition.StartSite.SpatialArtifact.Sha256, releases).Service,
                new AppearanceFake(releases), random: RandomMaximum.Create());
        }
        internal DaggerfallSession Restore(RulesetSavePayload payload) => DaggerfallSession.Restore(Engine().Context, _composition, payload);
        public void Dispose() => Session.Dispose();
    }

    private sealed class Harness : IDisposable
    {
        internal ActorsState Actors { get; } = new();
        internal Actor Player { get; }
        internal DaggerfallCasting Casting { get; }
        internal DaggerfallEffectLifecycle Effects { get; }
        internal DaggerfallSpellEffectDefinition Setting { get; }
        internal List<DaggerfallSpellTransferResult> Results { get; } = [];
        internal List<(long, long)> Attacks { get; } = [];
        internal bool Hostile = true;
        internal DaggerfallMagicDefense Defense = DaggerfallMagicDefense.None;
        internal Harness(int subtype, int count = 1, int element = 4, int range = 1)
        {
            Player = Actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health", DaggerActorFactory.PlayerCapabilities).Actor;
            var target = Actors.CreateActor(2, new EntityTypeId("target"), Stats(), new(new(0, 0, 0), 0f), "health", DaggerActorFactory.NonPlayerCapabilities).Actor;
            foreach (var actor in new[] { Player, target }) { actor.Add(new DaggerfallSpellReadiness()); actor.Add(new CombatContributions()); }
            Setting = new("setting", 11, subtype, 0, 0, 1, 0, 0, 1, 10, 10, 0, 0, 1);
            var spell = new DaggerfallSpellDefinition("spell", 1, false, "Transfer", element, range, 0, 0, Enumerable.Repeat(Setting, count).ToArray());
            var row = new DaggerfallMagicEffectCostDefinition(11, subtype, 1, "destruction", 1, 1, 0, 0, DaggerfallMagicCostMetadata.For(11, subtype));
            var catalog = new DaggerfallMagicCatalogSet(new Dictionary<string, DaggerfallSpellDefinition> { ["spell"] = spell }, new Dictionary<string, DaggerfallMagicItemDefinition>(), [], [],
                new Dictionary<(int, int), DaggerfallMagicEffectCostDefinition> { [(11, subtype)] = row }, new Dictionary<string, DaggerfallEnchantmentSetting>());
            DaggerfallEffectLifecycle effects = null!;
            effects = new(Actors, new([.. DaggerfallTransferEffects.Definitions(new(new CombatResolution()), Results.Add, _ => Hostile, (a, b, _) => Attacks.Add((a, b))),
                .. DaggerfallAlterationEffects.Definitions(effect => effects.Cancel(effect.Context.Instance)),
                new("ward", "ward", DaggerfallEffectStacking.Stack, 10, 1, MagicDefense: _ => Defense)])); Effects = effects;
            var profile = new DaggerfallMagicTargetProfile(50, new(DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.CriticalWeakness,
                DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal), null, 0, 0, 0, new(0, 0, 0, 0, 0), []);
            Casting = new(catalog, Effects, id => id == 1 ? Player : id == 2 ? target : null, _ => profile, _ => true, _ => { }, _ => { }, RandomMinimum.Create(), 1, playerKnowsSpell: _ => true);
        }
        internal DaggerfallLiveSpell Release() { Assert.Equal(DaggerfallCastOutcome.Ready, Casting.Ready(1, "spell").Outcome); return Casting.Release(1, true).Bundle!; }
        internal Track Track(long id, string name) => (id == 1 ? Player : Actors.Get(id).Actor).Get<StatsComponent>().GetTrack(TrackId.Parse(name));
        private static StatsComponent Stats()
        {
            StatsComponent stats = new();
            foreach (string name in new[] { "health", "stamina", "magicka" }) { var maximum = new Stat(1000); stats.AddStat(StatId.Parse(name + "-maximum"), maximum); stats.AddTrack(TrackId.Parse(name), new(maximum, 1000)); }
            stats.AddStat(StatId.Parse("destruction"), new(100)); return stats;
        }
        public void Dispose() { Effects.Dispose(); Actors.Dispose(); }
    }
}
