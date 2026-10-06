using System.Reflection;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Effects;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDestructionEffectsTests
{
    [Theory]
    [InlineData(0, "health", 10)]
    [InlineData(1, "stamina", 640)]
    [InlineData(2, "magicka", 10)]
    public void Admitted_variants_apply_once_with_classic_fatigue_units_and_guarded_bounds(int subtype, string track, int amount)
    {
        using Harness h = new(subtype: subtype);
        var bundle = h.Release(); h.Casting.Deliver(bundle, [2]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        Assert.Equal(1000 - amount, h.Track(2, track).Current);
        Assert.Empty(h.Effects.Active); Assert.Single(h.Attacks);
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, h.Casting.Deliver(bundle, [2]).Outcome);
        Assert.Equal(1000 - amount, h.Track(2, track).Current);
        h.Track(2, track).SetCurrent(1);
        h.Casting.Deliver(h.Release(), [2]);
        Assert.Equal(0, h.Track(2, track).Current);
        if (subtype == 0) Assert.True(h.HealthResults.Last().Result.Defeated);
        else Assert.Equal(1, h.TrackResults.Last().ActualLoss);
        Assert.NotNull(h.Catalog.RequireEffectCost(bundle.Spell.Effects[0]).RegularComponents);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Every_retained_element_admits_and_applies_the_bound_variant(int element)
    {
        using Harness h = new(element: element, save: 100);
        h.Casting.Deliver(h.Release(), [2]);
        Assert.Equal(990, h.Track(2, "health").Current);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Ordinary_save_scales_admitted_magnitude_without_rerolling_payload(int subtype)
    {
        using Harness h = new(subtype: subtype, save: 50);
        h.Profile = h.Profile with { CareerTolerances = h.Profile.CareerTolerances with { Magic = DaggerfallMagicTolerance.Normal } };
        var bundle = h.Release(); h.Casting.Deliver(bundle, [2]);
        Assert.Equal(75, Assert.Single(bundle.Results).SavePercent);
        Assert.Equal(1000 - (subtype == 1 ? 448 : 7), h.Track(2, subtype == 0 ? "health" : subtype == 1 ? "stamina" : "magicka").Current);
        Assert.Empty(h.Effects.Active);
    }

    [Fact]
    public void Peaceful_nonplayer_is_protected_from_fatigue_while_player_and_hostile_targets_are_drained()
    {
        using Harness h = new(subtype: 1); h.Hostile = false;
        h.Casting.Deliver(h.Release(), [2]);
        Assert.Equal(1000, h.Track(2, "stamina").Current); Assert.Empty(h.TrackResults); Assert.Empty(h.Attacks);
        h.Casting.Deliver(h.Release(), [1]);
        Assert.Equal(360, h.Track(1, "stamina").Current); Assert.Single(h.TrackResults);
    }

    [Fact]
    public void Zero_maximum_tracks_accept_bounded_zero_loss_without_underflow()
    {
        foreach (int subtype in new[] { 1, 2 })
        {
            using Harness h = new(subtype: subtype);
            var track = h.Track(2, subtype == 1 ? "stamina" : "magicka");
            track.Maximum.BaseValue = 0; track.SetCurrent(0);
            h.Casting.Deliver(h.Release(), [2]);
            Assert.Equal(0, track.Current); Assert.Equal(0, Assert.Single(h.TrackResults).ActualLoss);
        }
    }

    [Fact]
    public void Zero_magnitude_preserves_bounds_and_still_delivers_accepted_attack_outcome()
    {
        using Harness h = new(amount: 0);
        h.Casting.Deliver(h.Release(), [2]);
        Assert.Equal(1000, h.Track(2, "health").Current);
        Assert.Equal(0, Assert.Single(h.HealthResults).Result.ActualHealthLost);
        Assert.False(h.HealthResults[0].Result.Defeated); Assert.Single(h.Attacks);
    }

    [Fact]
    public void Disintegrate_obeys_chance_and_save_but_bypasses_shield_without_spending_its_pool()
    {
        using Harness h = new(terminal: true);
        h.Shield(2, 5000);
        var bundle = h.Release(); h.Casting.Deliver(bundle, [2]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        Assert.Equal(0, h.Track(2, "health").Current);
        Assert.True(Assert.Single(h.HealthResults).Result.Defeated);
        Assert.Equal(1000, h.HealthResults[0].Result.ActualHealthLost);
        Assert.Equal(5000, DaggerfallAlterationEffects.ReadShield(Assert.Single(h.Effects.Active).State).Remaining);
        h.Casting.Deliver(h.Release(), [2]);
        Assert.Single(h.HealthResults);
        using Harness chance = new(terminal: true, chance: 0);
        var failed = chance.Release(); chance.Casting.Deliver(failed, [2]);
        Assert.Equal(DaggerfallCastOutcome.ChanceFailed, Assert.Single(failed.Results).Outcome); Assert.Empty(chance.HealthResults);
        using Harness saved = new(terminal: true, save: 1);
        saved.Profile = saved.Profile with { CareerTolerances = saved.Profile.CareerTolerances with { Magic = DaggerfallMagicTolerance.Normal } };
        var resisted = saved.Release(); saved.Casting.Deliver(resisted, [2]);
        Assert.Equal(DaggerfallCastOutcome.Resisted, Assert.Single(resisted.Results).Outcome); Assert.Empty(saved.HealthResults);
    }

    [Fact]
    public void Health_damage_spends_shield_and_emits_only_actual_remaining_loss()
    {
        using Harness h = new(); h.Shield(2, 6);
        h.Casting.Deliver(h.Release(), [2]);
        Assert.Equal(996, h.Track(2, "health").Current);
        Assert.Equal(10, Assert.Single(h.HealthResults).Result.CalculatedDamage);
        Assert.Equal(4, h.HealthResults[0].Result.ActualHealthLost); Assert.Empty(h.Effects.Active);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Lethal_or_removed_target_stops_later_payloads_in_the_same_delivery(bool removed)
    {
        using Harness h = new(count: 2);
        if (removed) h.AfterHealth = () => h.TargetPresent = false;
        else h.Track(2, "health").SetCurrent(1);
        var bundle = h.Release(); h.Casting.Deliver(bundle, [2]);
        Assert.Equal(DaggerfallCastOutcome.Applied, bundle.Results[0].Outcome);
        Assert.Equal(DaggerfallCastOutcome.TargetUnavailable, bundle.Results[1].Outcome);
        Assert.Single(h.HealthResults); Assert.Empty(h.Effects.Active);
    }

    [Fact]
    public void Lethal_reflection_stops_remaining_source_delivery_and_retains_actual_caster_target_order()
    {
        using Harness h = new(terminal: true, count: 2);
        h.Defense = new(0, 100, []); h.Ward(2);
        var bundle = h.Release(); h.Casting.Deliver(bundle, [2]);
        var death = Assert.Single(h.HealthResults).Result;
        Assert.Equal(h.Player, death.Source); Assert.Equal(h.Player, death.Target); Assert.True(death.Defeated);
        Assert.Equal(1000, h.Track(2, "health").Current);
        Assert.Contains(bundle.Results, result => result.Outcome == DaggerfallCastOutcome.SourceUnavailable);
    }

    [Fact]
    public void Active_resistance_refuses_payload_before_any_track_or_attack_consequence()
    {
        using Harness h = new(); h.Defense = new(0, 0, [new(DaggerfallMagicResistanceElement.Magic, 100)]); h.Ward(2);
        var bundle = h.Release(); h.Casting.Deliver(bundle, [2]);
        Assert.Equal(DaggerfallCastOutcome.Resisted, Assert.Single(bundle.Results).Outcome);
        Assert.Empty(h.HealthResults); Assert.Empty(h.Attacks); Assert.Equal(1000, h.Track(2, "health").Current);
    }

    [Theory]
    [InlineData("spell.016", false)] [InlineData("spell.026", false)] [InlineData("spell.052", true)]
    public void Published_spells_use_default_session_owners_and_restore_current_vitals_without_replay(string key, bool lethal)
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        var composition = new DaggerfallSessionComposition(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults);
        EngineContextFake Engine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, inputs);
            return EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service,
                new AppearanceFake(releases), random: RandomMaximum.Create());
        }
        using var session = DaggerfallSession.StartNew(Engine().Context, composition);
        var target = session.State.Actors.All.First();
        if (!lethal) target.Actor.Get<WorldRpg.Rulesets.Daggerfall.Modules.Behavior.DaggerfallEnemyPerceptionMemory>().Pacified = true;
        var health = target.Stats.GetTrack(TrackId.Parse("health")); health.Maximum.BaseValue = 1000; health.SetCurrent(lethal ? 1 : 1000);
        var magicka = target.Stats.GetTrack(TrackId.Parse("magicka")); magicka.Maximum.BaseValue = 1000; magicka.SetCurrent(1000);
        session.State.Progression.AdvanceTo(0, lethal ? 100 : 1);
        session.State.Character.LearnSpell(key);
        var source = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); source.Maximum.BaseValue = 10000; source.SetCurrent(10000);
        Assert.Equal(DaggerfallCastOutcome.Ready, session.ReadyPlayerSpell(key).Outcome);
        var released = session.Casting.Release(1, true).Bundle!;
        session.Casting.Deliver(released, [target.DurableId]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(released.Results).Outcome);
        Assert.Empty(session.State.Effects.Active);
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(lethal, session.Corpses.ContainsKey(target.DurableId));
        var saved = session.CaptureSave();
        double savedHealth = health.Current, savedMagicka = magicka.Current;
        using var restored = DaggerfallSession.Restore(Engine().Context, composition, saved);
        Assert.Equal(savedHealth, restored.State.Actors.Get(target.DurableId).Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.Equal(savedMagicka, restored.State.Actors.Get(target.DurableId).Stats.GetTrack(TrackId.Parse("magicka")).Current);
        Assert.Equal(lethal, restored.Corpses.ContainsKey(target.DurableId)); Assert.Empty(restored.State.Effects.Active);
        if (!lethal)
        {
            var memory = restored.State.Actors.Get(target.DurableId).Actor.Get<WorldRpg.Rulesets.Daggerfall.Modules.Behavior.DaggerfallEnemyPerceptionMemory>();
            Assert.True(memory.ForcedHostile); Assert.True(memory.HasEncounteredPlayer); Assert.False(memory.Pacified);
            Assert.All(restored.State.Actors.All.Where(actor => !actor.IsDefeated), actor =>
                Assert.True(actor.Actor.Get<WorldRpg.Rulesets.Daggerfall.Modules.Behavior.DaggerfallEnemyPerceptionMemory>().ForcedHostile));
        }
    }

    private sealed class Harness : IDisposable
    {
        internal ActorsState Actors { get; } = new();
        internal Actor Player { get; }
        internal DaggerfallCasting Casting { get; }
        internal DaggerfallEffectLifecycle Effects { get; }
        internal DaggerfallMagicCatalogSet Catalog { get; }
        internal List<DaggerfallEffectDamage> HealthResults { get; } = [];
        internal List<DaggerfallSpellTrackResult> TrackResults { get; } = [];
        internal List<(long,long)> Attacks { get; } = [];
        internal bool Hostile = true, TargetPresent = true;
        internal Action? AfterHealth;
        internal DaggerfallMagicDefense Defense = DaggerfallMagicDefense.None;
        internal DaggerfallMagicTargetProfile Profile = new(50, new(DaggerfallMagicTolerance.Normal,
            DaggerfallMagicTolerance.CriticalWeakness, DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal,
            DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal, DaggerfallMagicTolerance.Normal), null, 0, 0, 0, new(0,0,0,0,0), []);
        internal Harness(int subtype = 0, int element = 4, int count = 1, int amount = 10, bool terminal = false, int chance = 100, int? save = null)
        {
            Player = Actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health", DaggerActorFactory.PlayerCapabilities).Actor;
            var target = Actors.CreateActor(2, new EntityTypeId("target"), Stats(), new(new(0,0,0), 0f), "health", DaggerActorFactory.NonPlayerCapabilities).Actor;
            foreach (var actor in new[] { Player, target }) { actor.Add(new DaggerfallSpellReadiness()); actor.Add(new CombatContributions()); }
            target.Add(new ProgressionState());
            var setting = new DaggerfallSpellEffectDefinition("setting", terminal ? 5 : 4, terminal ? -1 : subtype,
                0, 0, 1, chance, 0, 1, amount, amount, 0, 0, 1);
            var spell = new DaggerfallSpellDefinition("spell", 1, false, "Destruction", element, 2, 0, 0, Enumerable.Repeat(setting,count).ToArray());
            var cost = new DaggerfallMagicEffectCostDefinition(setting.Type, setting.SubType, 1, "destruction", 10, 1, 0, 0,
                DaggerfallMagicCostMetadata.For(setting.Type, setting.SubType));
            Catalog = new(new Dictionary<string,DaggerfallSpellDefinition> { ["spell"] = spell }, new Dictionary<string,DaggerfallMagicItemDefinition>(), [], [],
                new Dictionary<(int,int),DaggerfallMagicEffectCostDefinition> { [(setting.Type,setting.SubType)] = cost }, new Dictionary<string,DaggerfallEnchantmentSetting>());
            DaggerfallEffectLifecycle effects = null!;
            var definitions = DaggerfallDestructionEffects.Definitions(new(new CombatResolution()), result => { HealthResults.Add(result); AfterHealth?.Invoke(); }, TrackResults.Add,
                _ => Hostile, (source,targetId, _) => Attacks.Add((source,targetId)));
            effects = new(Actors, new([..definitions, ..DaggerfallAlterationEffects.Definitions(effect => effects.Cancel(effect.Context.Instance)),
                new("ward", "ward", DaggerfallEffectStacking.Stack, 10, 1, MagicDefense: _ => Defense)])); Effects = effects;
            Casting = new(Catalog, Effects, id => id == 1 ? Player : TargetPresent ? target : null, _ => Profile, _ => true, _ => {}, _ => {},
                save is int value ? DaggerfallCastingTests.SaveDice.Create(value) : RandomMinimum.Create(), 1, playerKnowsSpell: _ => true);
        }
        internal DaggerfallLiveSpell Release() { Assert.Equal(DaggerfallCastOutcome.Ready, Casting.Ready(1,"spell").Outcome); return Casting.Release(1,true).Bundle!; }
        internal Track Track(long id, string key) => (id == 1 ? Player : Actors.Get(id).Actor).Get<StatsComponent>().GetTrack(TrackId.Parse(key));
        internal void Shield(long id, int amount)
        {
            var payload = DaggerfallAlterationEffects.ShieldState(new(amount, amount));
            Effects.Start(new("shield", "shield", "spell.shield", 1, id, "shield", null, null, 1, 100, payload));
        }
        internal void Ward(long id)
        {
            using var doc = JsonDocument.Parse("{}"); Effects.Start(new("ward", "ward", "spell.ward", id, id, "ward", null, null, 1, 100, doc.RootElement));
        }
        private static StatsComponent Stats()
        {
            StatsComponent stats = new();
            foreach (var key in new[] { "health", "magicka", "stamina" }) { var maximum = new Stat(1000); stats.AddStat(StatId.Parse(key+"-maximum"),maximum); stats.AddTrack(TrackId.Parse(key),new(maximum,1000)); }
            stats.AddStat(StatId.Parse("destruction"),new(100)); return stats;
        }
        public void Dispose() { Effects.Dispose(); Actors.Dispose(); }
    }
}
