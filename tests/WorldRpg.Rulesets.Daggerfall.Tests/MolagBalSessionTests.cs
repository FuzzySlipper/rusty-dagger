using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;
using UniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;
using EquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class MolagBalSessionTests
{
    [Fact]
    public void Accepted_strike_transfers_available_magicka_once_and_restores_overflow_without_reapplying()
    {
        using Fixture f = new(); var s = f.Session;
        var source = f.Magicka(s, 1); var victim = f.Magicka(s, f.Enemy);
        source.SetCurrent(source.Maximum.Value); victim.Maximum.BaseValue = 100; victim.SetCurrent(8);
        double maximum = source.Maximum.Value; int condition = f.Condition(s);
        f.Strike(s);
        Assert.Equal(1, f.Attack.Applications);
        Assert.Equal(0, victim.Current); Assert.Equal(maximum + 8, source.Maximum.Value); Assert.Equal(maximum + 8, source.Current);
        Assert.True(f.Condition(s) <= condition - 13);
        Assert.Contains("transferred 8 magicka", s.Presentation.LastOutcome);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(maximum + 8, f.Magicka(restored, 1).Current);
        Assert.Equal(maximum + 8, f.Magicka(restored, 1).Maximum.Value);
        Assert.Equal(0, f.Magicka(restored, f.Enemy).Current);
        Assert.Single(restored.State.Effects.Active, effect => effect.Definition.Key == DaggerfallMolagBalEffects.Key);
        restored.AdvanceElapsedTime(12 * 60); Assert.Equal(maximum + 8, f.Magicka(restored, 1).Maximum.Value);
        restored.AdvanceElapsedTime(60); Assert.Equal(maximum, f.Magicka(restored, 1).Maximum.Value);
        Assert.Equal(maximum, f.Magicka(restored, 1).Current);
    }

    [Fact]
    public void Empty_magicka_drains_common_strength_and_raises_live_cap_then_fresh_strike_refreshes_calendar_expiry()
    {
        using Fixture f = new(); var s = f.Session;
        f.Magicka(s, f.Enemy).SetCurrent(0);
        var source = f.Strength(s, 1); var victim = f.Strength(s, f.Enemy);
        source.BaseValue = 100; victim.BaseValue = 50;
        f.Strike(s);
        Assert.Equal(106, source.Value); Assert.Equal(106, source.Maximum); Assert.Equal(44, victim.Value);
        Assert.Equal(50, victim.BaseValue); Assert.Single(victim.Sources);
        s.AdvanceElapsedTime(10 * 60); f.Strike(s, 1000);
        Assert.Equal(112, source.Value); Assert.Equal(38, victim.Value); Assert.Single(victim.Sources);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(112, f.Strength(restored, 1).Value); Assert.Equal(112, f.Strength(restored, 1).Maximum);
        Assert.Equal(38, f.Strength(restored, f.Enemy).Value);
        restored.AdvanceElapsedTime(12 * 60); Assert.Equal(112, f.Strength(restored, 1).Value);
        restored.AdvanceElapsedTime(60); Assert.Equal(100, f.Strength(restored, 1).Value);
        Assert.Equal(100, f.Strength(restored, 1).Maximum); Assert.Equal(38, f.Strength(restored, f.Enemy).Value);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Unequip_or_break_removes_temporary_bonus_but_preserves_common_permanent_drain(bool broken)
    {
        using Fixture f = new(); var s = f.Session;
        f.Magicka(s, f.Enemy).SetCurrent(0); var strength = f.Strength(s, 1); double before = strength.Value;
        f.Strike(s); Assert.Equal(before + 6, strength.Value);
        if (broken) s.ItemCondition.Damage(f.Weapon, f.Condition(s));
        else Assert.Equal(EquipmentMoveOutcome.Applied, s.EquipmentMoves.MoveToGrid(f.Weapon, 49).Outcome);
        Assert.Equal(before, strength.Value); Assert.Equal(100, strength.Maximum);
        Assert.DoesNotContain(s.State.Effects.Active, effect => effect.Definition.Key == DaggerfallMolagBalEffects.Key);
        Assert.Contains(s.State.Effects.Active, effect => effect.Definition.Key == "drain-strength");
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(before, f.Strength(restored, 1).Value);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Miss_or_magic_immunity_has_no_artifact_transfer_or_extra_durability_charge(bool immune)
    {
        using Fixture f = new(); var s = f.Session; f.Magicka(s, f.Enemy).SetCurrent(8);
        if (immune)
        {
            var other = s.State.Inventory.Read().UniqueItems.First(item => item.Entity.Value != f.Weapon.EntityId);
            ulong otherId = s.State.Inventory.GetDurableItemId(other.Entity).Value;
            DaggerfallAlterationEffectsTests.Resistance(s, "mace-ward", DaggerfallMagicResistanceElement.Magic, 100, 20, f.Enemy, otherId);
        }
        else f.Attack.Hits = false;
        int condition = f.Condition(s); f.Strike(s);
        Assert.Equal(8, f.Magicka(s, f.Enemy).Current);
        Assert.DoesNotContain(s.State.Effects.Active, effect => effect.Definition.Key == DaggerfallMolagBalEffects.Key);
        Assert.Equal(immune ? 1 : 0, f.Attack.Applications);
        Assert.True(immune ? f.Condition(s) > condition - 13 : f.Condition(s) == condition);
    }

    [Fact]
    public void Terminal_hit_still_has_one_damage_application_and_broken_source_has_no_saved_bonus()
    {
        using Fixture f = new(condition: 5); var s = f.Session; f.Magicka(s, f.Enemy).SetCurrent(8);
        s.State.Actors.Get(f.Enemy).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1);
        f.Strike(s); Assert.Equal(1, f.Attack.Applications); Assert.True(s.State.Actors.Get(f.Enemy).IsDefeated);
        Assert.Equal(0, f.Condition(s)); Assert.True(s.Corpses.ContainsKey(f.Enemy));
        Assert.DoesNotContain(s.State.Effects.Active, effect => effect.Definition.Key == DaggerfallMolagBalEffects.Key);
        using var restored = f.Restore(s.CaptureSave()); Assert.True(restored.State.Actors.Get(f.Enemy).IsDefeated);
        Assert.DoesNotContain(restored.State.Effects.Active, effect => effect.Definition.Key == DaggerfallMolagBalEffects.Key);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Changing_weapon_between_admission_and_impact_cannot_supply_or_retain_a_mace_payload(bool equipAfter)
    {
        using Fixture f = new(); var s = f.Session; f.Magicka(s, f.Enemy).SetCurrent(8);
        if (equipAfter) f.EquipPlain();
        FactBuffer<IProductFact> facts = new();
        s.State.Kit.AttackExecution.ObserveTimeline(1, 1);
        Assert.True(s.State.Kit.AttackExecution.Start(new(1, f.Enemy, 1, 1, .125, true), facts));
        if (equipAfter) Assert.Equal(EquipmentMoveOutcome.Applied,
            s.EquipmentMoves.MoveToSlot(f.Weapon, new EquipmentSlotId("right-hand")).Outcome);
        else f.EquipPlain();
        s.State.Kit.AttackExecution.ApplyImpacts([new(1, f.Enemy, 1, 1, false)], 1, facts);
        List<IProductFact> delivered = []; facts.Deliver(delivered.Add);
        Assert.Single(delivered.OfType<AttackHitFact>());
        Assert.DoesNotContain(delivered, fact => fact is ArtifactResourceTransferredFact);
        Assert.Equal(8, f.Magicka(s, f.Enemy).Current);
    }

    [Fact]
    public void Wielder_death_removes_artifact_contributions_before_current_save_capture()
    {
        using Fixture f = new(); var s = f.Session; f.Magicka(s, f.Enemy).SetCurrent(0);
        double before = f.Strength(s, 1).Value; f.Strike(s);
        s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0);
        var save = s.CaptureSave();
        Assert.Equal(before, f.Strength(s, 1).Value);
        Assert.DoesNotContain(DaggerfallSavePayload.Read(save).ActiveEffects, effect => effect.EffectKey == DaggerfallMolagBalEffects.Key);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Malformed_current_bonus_or_future_strike_time_is_refused(bool future)
    {
        using Fixture f = new(); var s = f.Session; f.Magicka(s, f.Enemy).SetCurrent(0); f.Strike(s);
        var save = DaggerfallSavePayload.Read(s.CaptureSave());
        var effect = Assert.Single(save.ActiveEffects, effect => effect.EffectKey == DaggerfallMolagBalEffects.Key);
        var state = DaggerfallMolagBalEffects.Read(effect.State);
        var bad = effect with { State = DaggerfallMolagBalEffects.Encode(future
            ? state with { LastStrikeMinute = state.LastStrikeMinute + 1 }
            : state with { StrengthIncrease = state.StrengthIncrease + 1 }) };
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(save with
            { ActiveEffects = save.ActiveEffects.Select(entry => entry.Instance == effect.Instance ? bad : entry).ToArray() })));
    }

    [Theory]
    [InlineData("LastStrikeMinute")]
    [InlineData("MagickaMaximumIncrease")]
    [InlineData("StrengthIncrease")]
    public void Incomplete_current_artifact_state_is_refused(string omitted)
    {
        using Fixture f = new(); var s = f.Session;
        f.Magicka(s, f.Enemy).SetCurrent(0); f.Strike(s);
        var save = DaggerfallSavePayload.Read(s.CaptureSave());
        var effect = Assert.Single(save.ActiveEffects, effect => effect.EffectKey == DaggerfallMolagBalEffects.Key);
        var fields = effect.State.EnumerateObject().Where(field => field.Name != omitted)
            .ToDictionary(field => field.Name, field => field.Value);
        var incomplete = effect with { State = JsonSerializer.SerializeToElement(fields) };
        Assert.Throws<JsonException>(() => f.Restore(DaggerfallSavePayload.Encode(save with
            { ActiveEffects = save.ActiveEffects.Select(entry => entry.Instance == effect.Instance ? incomplete : entry).ToArray() })));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Ordinary_wear_break_still_delivers_the_accepted_transfer_without_retaining_a_bonus(bool strength)
    {
        using Fixture f = new(condition: 1); var s = f.Session;
        f.Magicka(s, f.Enemy).SetCurrent(strength ? 0 : 8);
        double before = f.Strength(s, f.Enemy).Value;
        f.Strike(s);
        Assert.Equal(1, f.Attack.Applications); Assert.Equal(0, f.Condition(s));
        Assert.Equal(0, f.Magicka(s, f.Enemy).Current);
        Assert.Equal(before - (strength ? 6 : 0), f.Strength(s, f.Enemy).Value);
        Assert.DoesNotContain(s.State.Effects.Active, effect => effect.Definition.Key == DaggerfallMolagBalEffects.Key);
        Assert.Contains(strength ? "transferred 6 Strength" : "transferred 8 magicka", s.Presentation.LastOutcome);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(before - (strength ? 6 : 0), f.Strength(restored, f.Enemy).Value);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition _composition;
        internal DaggerfallSession Session { get; }
        internal long Enemy { get; }
        internal ulong Item { get; }
        internal UniqueInventoryItem Weapon { get; }
        internal StrikeContribution Attack { get; } = new();
        internal Fixture(int condition = 1500)
        {
            var inputs = ReadInputs(TestData.RepositoryRoot); var definitions = TestPayload.Definitions;
            _composition = new(definitions, inputs, DaggerfallTuning.Defaults);
            Enemy = inputs.Project.Actors.Values.First(actor => definitions.RequireActor(actor.ActorId).Team == "orcs").EntityId;
            Session = DaggerfallSession.StartNew(Engine().Context, _composition);
            var created = new DaggerfallItemFactory(definitions, RandomMaximum.Create()).Create(new("Magic", "mace-tests", DaggerfallItemOwner.Player, MagicItemKey: "magic-item.0002"));
            var identity = Session.UniqueItemAllocator.AllocateReference(); Item = identity.Value;
            Weapon = Session.State.Equipment.Materialize(identity, created.Item);
            Session.State.ItemInstances.RegisterUnique(Item, created.Metadata with { CurrentCondition = condition });
            Assert.Equal(EquipmentMoveOutcome.Applied, Session.EquipmentMoves.MoveToSlot(Weapon, new EquipmentSlotId("right-hand")).Outcome);
            Magicka(Session, Enemy).Maximum.BaseValue = 100;
            var health = Session.State.Actors.Get(Enemy).Stats.GetTrack(TrackId.Parse("health")); health.Maximum.BaseValue = 1000; health.SetCurrent(1000);
            Session.State.Kit.Rules.RegisterAction(definitions.RequireActor(new DaggerfallActorId("player")).ActionId!, Attack);
        }
        private EngineContextFake Engine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, _composition.StartSite);
            var spatial = SpatialFake.Create(_composition.StartSite.SpatialArtifact.Sha256, releases); spatial.KeepPosition = true;
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: RandomMaximum.Create());
        }
        internal Track Magicka(DaggerfallSession session, long actor) => Stats(session, actor).GetTrack(TrackId.Parse("magicka"));
        internal Stat Strength(DaggerfallSession session, long actor) => Stats(session, actor).GetStat(StatId.Parse("strength"));
        private static StatsComponent Stats(DaggerfallSession session, long actor) => actor == 1 ? session.State.Actors.Player.Stats : session.State.Actors.Get(actor).Stats;
        internal int Condition(DaggerfallSession session) => session.State.ItemInstances.RequireUnique(Item).CurrentCondition;
        internal void EquipPlain()
        {
            var item = Session.State.Inventory.Read().UniqueItems.First(item => item.Definition.Value == "iron-dagger");
            var result = Session.EquipmentMoves.MoveToSlot(
                new UniqueInventoryItem(item.Entity.Value, new InventoryItemId(item.Definition.Value)), new EquipmentSlotId("right-hand"));
            Assert.True(result.Outcome == EquipmentMoveOutcome.Applied, result.Detail);
        }
        internal void Strike(DaggerfallSession session, ulong step = 1) => session.ResolveExplicitMelee(new(1, Enemy, 1, step, .125));
        internal DaggerfallSession Restore(RulesetSavePayload save) => DaggerfallSession.Restore(Engine().Context, _composition, save);
        public void Dispose() => Session.Dispose();
    }
    private sealed class StrikeContribution : ICombatContribution
    {
        internal bool Hits { get; set; } = true;
        internal int Applications { get; private set; }
        public void Hit(TryHitEvent interaction) => interaction.Hit = Hits;
        public void Damage(DamageEvent interaction) => interaction.Damage = 13;
        public void Applying(ApplyHitEvent interaction) => Applications++;
    }
}
