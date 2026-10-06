using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Facts;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallConcealmentEffectsTests
{
    [Theory]
    [InlineData(13, 0)] [InlineData(13, 1)] [InlineData(23, 0)] [InlineData(23, 1)] [InlineData(24, 0)] [InlineData(24, 1)]
    public void Every_variant_extends_concrete_incumbent_projects_power_and_restores_then_expires(int type, int subtype)
    {
        using Fixture f = new(); var s = f.Session;
        var spell = Spell(type, subtype); var casting = Casting(s, spell); Fund(s);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome); var bundle = casting.Release(1, true).Bundle!;
        casting.Deliver(bundle, [1]); Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        var effect = Assert.Single(s.State.Effects.Active); var projected = s.State.Effects.PerceptionFor(1);
        Assert.Equal(type == 13, projected.Invisible); Assert.Equal(type == 23, projected.Blending); Assert.Equal(type == 24, projected.Shade);
        Assert.NotEqual(DaggerfallConcealment.None, projected.Concealment);
        Assert.Equal(DaggerfallEffectAdmissionOutcome.Refreshed, Start(s, "extend", type, subtype, 20)); Assert.Equal(29u, effect.Lifecycle.RemainingRounds);
        s.PublishInitial(); var slot = Assert.Single(s.Slots.Read(), row => row.Owner == "magic.concealment"); Assert.Equal(subtype == 1, slot.Label.StartsWith("True "));
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(projected, restored.State.Effects.PerceptionFor(1));
        restored.State.Effects.AdvanceElapsedRounds(29); Assert.Equal(default, restored.State.Effects.PerceptionFor(1));
        restored.PublishInitial(); Assert.DoesNotContain(restored.Slots.Read(), row => row.Owner == "magic.concealment");
        Assert.Equal("illusion", TestPayload.Definitions.Magic.RequireEffectCost(spell.Effects[0]).School);
        var quoteEffect = spell.Effects[0] with { DurationBase = 2, DurationMod = 7, DurationPerLevel = 3 };
        int expectedGold = type == 13 ? (subtype == 0 ? 320 : 400) : (subtype == 0 ? 200 : 320);
        Assert.Equal(new DaggerfallMagicCost(expectedGold, expectedGold * 60 / 400),
            DaggerfallMagicCostPolicy.CalculateEffectCosts(TestPayload.Definitions.Magic, quoteEffect, new Dictionary<string, int> { ["illusion"] = 50 }));
    }

    [Fact]
    public void Overlap_keeps_true_power_after_breaking_all_normal_variants_and_preserves_other_slots()
    {
        using Fixture f = new(); var s = f.Session;
        foreach (int type in new[] { 13, 23, 24 }) { Start(s, $"n{type}", type, 0, 10); Start(s, $"t{type}", type, 1, 10); }
        s.Slots.Publish(new("quest", "quest", "Quest", "Retained", 1)); s.PublishInitial(); Assert.Equal(7, s.Slots.Read().Count);
        Assert.Equal(3, DaggerfallConcealmentEffects.BreakNormal(s.State.Effects, 1));
        var state = s.State.Effects.PerceptionFor(1); Assert.True(state.Invisible && state.Blending && state.Shade);
        Assert.Equal(DaggerfallConcealment.InvisibleTrue | DaggerfallConcealment.BlendingTrue | DaggerfallConcealment.ShadeTrue, state.Concealment);
        s.PublishInitial(); Assert.Equal(4, s.Slots.Read().Count); Assert.Contains(s.Slots.Read(), row => row.Owner == "quest");
        Assert.Equal(3, DaggerfallConcealmentEffects.End(s.State.Effects, 1)); s.PublishInitial(); Assert.Single(s.Slots.Read());
    }

    [Theory]
    [InlineData(4, 0)] [InlineData(4, 1)] [InlineData(4, 2)] [InlineData(5, -1)]
    public void Actual_destructive_delivery_breaks_normal_source_power_even_when_health_loss_is_shielded(int type, int subtype)
    {
        using Fixture f = new(); var s = f.Session;
        Start(s, "normal", 13, 0, 10); Start(s, "true", 13, 1, 10);
        var target = s.State.Actors.Get(2000).Stats;
        foreach (string track in new[] { "health", "stamina", "magicka" }) { target.GetTrack(TrackId.Parse(track)).Maximum.BaseValue = 1000; target.GetTrack(TrackId.Parse(track)).SetCurrent(1000); }
        if (type == 4 && subtype == 0) s.State.Effects.Start(new("shield", "shield", "spell.shield", 2000, 2000, "shield", "Magic", null, 1, 10, DaggerfallAlterationEffects.ShieldState(new(1000, 1000))));
        var setting = new DaggerfallSpellEffectDefinition("attack", type, subtype, 0, 0, 1, 100, 0, 1, 7, 7, 0, 0, 1);
        var spell = new DaggerfallSpellDefinition("attack.test", 1, false, "Attack", 4, 1, 0, 0, [setting]);
        var casting = Casting(s, spell); Fund(s); Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
        var bundle = casting.Release(1, true).Bundle!; casting.Deliver(bundle, [2000]); Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        Assert.DoesNotContain(s.State.Effects.Active, effect => effect.Context.Instance.Value == "normal");
        Assert.Contains(s.State.Effects.Active, effect => effect.Context.Instance.Value == "true");
        if (type == 4 && subtype == 0) Assert.Equal(1000, target.GetTrack(TrackId.Parse("health")).Current);
        Assert.Equal(DaggerfallConcealment.InvisibleTrue, s.State.Effects.PerceptionFor(1).Concealment);
    }

    [Fact]
    public void Real_stock_invisibility_perception_and_appearance_clear_with_item_source_and_actor_cleanup()
    {
        using Fixture f = new(); var s = f.Session; Fund(s); s.State.Character.LearnSpell("spell.006");
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell("spell.006").Outcome); var bundle = s.Casting.Release(1, true).Bundle!; s.Casting.Deliver(bundle, [1]);
        Assert.True(s.State.Effects.PerceptionFor(1).Invisible); s.PublishInitial(); Assert.Contains(s.Slots.Read(), row => row.Owner == "magic.concealment");
        s.State.Effects.CancelSource("spell.spell.006"); s.PublishInitial(); Assert.False(s.State.Effects.PerceptionFor(1).Invisible); Assert.DoesNotContain(s.Slots.Read(), row => row.Owner == "magic.concealment");
        long target = s.SpawnActor("rat", new(new(11, 0, 11), 0)); Start(s, "actor", 13, 1, 10, target: target); s.PublishInitial();
        Assert.Contains(f.Graphics.Snapshots.Last(), fact => fact.ObjectId == (ulong)target && !fact.Visible);
        DaggerfallConcealmentEffects.End(s.State.Effects, target); s.PublishInitial(); Assert.Contains(f.Graphics.Snapshots.Last(), fact => fact.ObjectId == (ulong)target && fact.Visible);
        Start(s, "retired", 24, 0, 10, target: target); s.RetireActor(target); Assert.Equal(default, s.State.Effects.PerceptionFor(target));
        ulong item = s.State.Inventory.Read().UniqueItems.Select(value => s.State.Inventory.GetDurableItemId(value.Entity).Value).First();
        Start(s, "item", 23, 0, 10, item: item); Assert.True(s.State.Effects.PerceptionFor(1).Blending);
        // A used item's effect outlives the item breaking; destroying the item ends it.
        s.State.ItemInstances.ReplaceUnique(item, s.State.ItemInstances.RequireUnique(item) with { CurrentCondition = 0 });
        Assert.True(s.State.Effects.PerceptionFor(1).Blending);
        s.State.ItemInstances.RemoveUnique(item);
        Assert.False(s.State.Effects.PerceptionFor(1).Blending); s.PublishInitial();
        Assert.DoesNotContain(s.Slots.Read(), row => row.Owner == "magic.concealment");
    }

    [Fact]
    public void Nonmagic_element_is_refused_before_payment_and_wrong_magnitude_state_cannot_restore()
    {
        using Fixture f = new(); var s = f.Session; var casting = Casting(s, Spell(23, 1) with { Element = 0, IsCustom = true }); Fund(s);
        Assert.Equal(DaggerfallCastOutcome.UnsupportedEffect, casting.Ready(1, "conceal.test").Outcome);
        Start(s, "bad", 23, 1, 10); var save = DaggerfallSavePayload.Read(s.CaptureSave()); var effect = Assert.Single(save.ActiveEffects);
        var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)!;
        var invalid = effect with { State = JsonSerializer.SerializeToElement(state with { Amount = 1 }, DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState) };
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(save with { ActiveEffects = [invalid] })));
    }

    [Fact]
    public void Restoring_a_prior_paralysis_attack_does_not_break_concealment_cast_after_that_attack()
    {
        using Fixture f = new(); var s = f.Session;
        var setting = new DaggerfallSpellEffectDefinition("paralyze", 0, -1, 10, 0, 1, 100, 0, 1, 0, 0, 0, 0, 1);
        s.State.Effects.Start(new("paralysis", "paralyze", "spell", 1, 2000, "paralyze", "Magic", null, 1, 10,
            JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(setting, 1, 0, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)));
        Start(s, "normal", 13, 0, 10); Start(s, "true", 24, 1, 10);
        var before = s.State.Effects.PerceptionFor(1);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(before, restored.State.Effects.PerceptionFor(1));
        Assert.True(restored.State.Effects.ControlsFor(2000).Movement);
    }

    [Theory]
    [InlineData(0)] [InlineData(10)]
    public void Canonical_physical_hit_reveals_only_its_source_on_positive_calculated_contact(int damage)
    {
        using Fixture f = new(); var s = f.Session;
        Start(s, "source-normal", 13, 0, 10); Start(s, "source-true", 13, 1, 10);
        Start(s, "target-normal", 23, 0, 10, target: 2000);
        DaggerfallConcealmentEffects.AfterPhysicalHit(s.State.Effects, new AttackHitFact(1, 2000, damage, 0, 0, false, 1, 1));
        Assert.Equal(damage == 0, s.State.Effects.Active.Any(effect => effect.Context.Instance.Value == "source-normal"));
        Assert.Contains(s.State.Effects.Active, effect => effect.Context.Instance.Value == "source-true");
        Assert.True(s.State.Effects.PerceptionFor(2000).Blending);
    }

    private static DaggerfallSpellEffectDefinition Setting(int type, int subtype) => new("conceal", type, subtype, 10, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1);
    private static DaggerfallSpellDefinition Spell(int type, int subtype) => new("conceal.test", 1, false, "Conceal", 4, 0, 0, 0, [Setting(type, subtype)]);
    private static DaggerfallEffectAdmissionOutcome Start(DaggerfallSession s, string instance, int type, int subtype, uint rounds, long target = 1, ulong? item = null) => s.State.Effects.Start(new(
        instance, DaggerfallConcealmentEffects.Key(type, subtype), "concealment", 1, target, "conceal", "Magic", item, 1, rounds,
        JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(Setting(type, subtype), 1, 0, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)));
    private static DaggerfallCasting Casting(DaggerfallSession s, DaggerfallSpellDefinition spell) => new(
        TestPayload.Definitions.Magic with { Spells = new Dictionary<string, DaggerfallSpellDefinition> { [spell.Key] = spell } }, s.State.Effects,
        id => id == 1 ? s.State.Actors.Player.Actor : s.State.Actors.TryGet(id, out var actor) ? actor.Actor : null,
        id => s.MagicProfile(id) with { LiveWillpower = 50, PlayerRace = null, BiographyMagicResistance = 0 }, _ => true, _ => { }, _ => { }, DaggerfallCastingTests.SaveDice.Create(100),
        1, playerKnowsSpell: _ => true, casterLevel: _ => 1);
    private static void Fund(DaggerfallSession s) { var track = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); track.Maximum.BaseValue = 10000; track.SetCurrent(10000); }
    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition _composition = new(TestPayload.Definitions, ReadInputs(TestData.RepositoryRoot), DaggerfallTuning.Defaults);
        internal DaggerfallSession Session { get; }
        internal AppearanceFake Graphics { get; } = new([]);
        internal Fixture() => Session = DaggerfallSession.StartNew(Engine(Graphics).Context, _composition);
        private EngineContextFake Engine(AppearanceFake graphics)
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, _composition.StartSite);
            return EngineContextFake.Create(content, SpatialFake.Create(_composition.StartSite.SpatialArtifact.Sha256, releases).Service,
                graphics, PerceptionFake.Create().Service, random: RandomMaximum.Create());
        }
        internal DaggerfallSession Restore(RulesetSavePayload payload) => DaggerfallSession.Restore(Engine(new([])).Context, _composition, payload);
        public void Dispose() => Session.Dispose();
    }
}
