using System.Numerics;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using UniqueItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class ItemCastTriggerSessionTests
{
    [Fact]
    public void Equip_admits_one_permanent_source_and_unequip_cancels_it_without_charging_again_on_restore()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0035");
        var s = f.Session;
        Assert.DoesNotContain(s.State.Effects.Active, value => value.Context.Item?.Value == f.Source);
        double strength = Stat(s, "strength").Value;
        Equip(s, f.Item);
        var active = Assert.Single(s.State.Effects.Capture(), value => value.ItemId == f.Source);
        Assert.Equal(DaggerfallEffectBundleKind.HeldMagicItem, active.BundleKind);
        Assert.Null(active.RemainingRounds);
        Assert.True(Stat(s, "strength").Value > strength);
        int condition = f.Condition;
        Assert.True(condition < 1500);
        f.Update(); Assert.Equal(condition, f.Condition);
        using var restored = f.Restore();
        Assert.Equal(condition, restored.State.ItemInstances.RequireUnique(f.Source).CurrentCondition);
        Assert.Equal(Stat(s, "strength").Value, Stat(restored, "strength").Value);
        Assert.Single(restored.State.Effects.Capture(), value => value.ItemId == f.Source);
        Assert.Equal(EquipmentMoveOutcome.Applied, restored.EquipmentMoves.MoveToGrid(f.Item, 49).Outcome);
        Assert.Null(restored.State.ItemInstances.RequireUnique(f.Source).HeldCast);
        Assert.DoesNotContain(restored.State.Effects.Capture(), value => value.ItemId == f.Source);
        Assert.Equal(strength, Stat(restored, "strength").Value);
    }

    [Fact]
    public void Ordinary_admitted_minutes_wear_on_global_four_round_beat()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0035");
        var s = f.Session;
        // Prime the already-running lifecycle before this item is equipped; equip must not reset its beat.
        s.AdvanceElapsedTime(60);
        Equip(s, f.Item); int before = f.Condition;
        for (int minute = 0; minute < 3; minute++) f.Update(60d / DaggerfallTuning.Defaults.Time.GameSecondsPerRealSecond);
        Assert.Equal(4, s.State.Effects.MagicRounds); Assert.Equal(before, f.Condition);
        f.Update(60d / DaggerfallTuning.Defaults.Time.GameSecondsPerRealSecond);
        Assert.Equal(5, s.State.Effects.MagicRounds); Assert.Equal(before - 1, f.Condition);
    }

    [Fact]
    public void Calendar_cadence_is_global_persisted_and_rest_wears_every_sixty_rounds()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0035");
        var s = f.Session; Equip(s, f.Item); int before = f.Condition;
        s.AdvanceElapsedTime(60, resting: true);
        Assert.Equal(before - 1, f.Condition); // Startup count zero is the donor's first beat.
        Assert.Equal(1, s.State.Effects.MagicRounds);
        using var restored = f.Restore();
        Assert.Equal(1, restored.State.Effects.MagicRounds);
        restored.AdvanceElapsedTime(59 * 60, resting: true);
        Assert.Equal(before - 1, restored.State.ItemInstances.RequireUnique(f.Source).CurrentCondition);
        restored.AdvanceElapsedTime(60, resting: true);
        Assert.Equal(before - 2, restored.State.ItemInstances.RequireUnique(f.Source).CurrentCondition);
    }

    [Fact]
    public void Synthetic_time_keeps_condition_and_replaces_held_bundle_after_six_hours_once_without_initial_cost()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0035");
        var s = f.Session; Equip(s, f.Item); int before = f.Condition;
        long sequence = Assert.Single(s.State.Effects.Capture(), value => value.ItemId == f.Source).BundleSequence;
        s.AdvanceElapsedTime(359 * 60);
        Assert.Equal(sequence, Assert.Single(s.State.Effects.Capture(), value => value.ItemId == f.Source).BundleSequence);
        Assert.Equal(before, f.Condition);
        s.AdvanceElapsedTime(60);
        Assert.True(Assert.Single(s.State.Effects.Capture(), value => value.ItemId == f.Source).BundleSequence > sequence);
        Assert.Equal(before, f.Condition);
        var held = s.State.ItemInstances.RequireUnique(f.Source).HeldCast;
        using var restored = f.Restore();
        Assert.Equal(held, restored.State.ItemInstances.RequireUnique(f.Source).HeldCast);
        long resumed = Assert.Single(restored.State.Effects.Capture(), value => value.ItemId == f.Source).BundleSequence;
        restored.AdvanceElapsedTime(60);
        Assert.Equal(resumed, Assert.Single(restored.State.Effects.Capture(), value => value.ItemId == f.Source).BundleSequence);
    }

    [Fact]
    public void Real_inventory_self_use_delivers_once_without_magicka_and_uses_canonical_condition()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0025");
        var s = f.Session; var magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.SetCurrent(0); long next = s.Casting.NextSequence;
        f.Use();
        Assert.Equal(1490, f.Condition); Assert.Equal(0, magicka.Current);
        Assert.Equal(next + 1, s.Casting.NextSequence);
        var effect = Assert.Single(s.State.Effects.Capture(), value => value.ItemId == f.Source);
        Assert.Equal("shield", effect.EffectKey); Assert.Equal(1, effect.CasterId); Assert.Equal(1, effect.TargetId);
        Assert.Contains("cast", f.Message);
        using var restored = f.Restore();
        Assert.Equal(1490, restored.State.ItemInstances.RequireUnique(f.Source).CurrentCondition);
        Assert.Single(restored.State.Effects.Capture(), value => value.ItemId == f.Source);
    }

    [Fact]
    public void Nonself_item_use_preserves_source_readiness_in_save_and_releases_through_existing_ranged_cast()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0019");
        f.Use(); var s = f.Session;
        var ready = Assert.IsType<DaggerfallReadySpell>(s.Casting.ReadyFor(1));
        Assert.Equal(f.Source, ready.ItemId); Assert.Equal(DaggerfallCastSource.ItemUse, ready.Source);
        Assert.Equal(0, ready.Cost); Assert.Equal(1490, f.Condition);
        Assert.Equal(ready, DaggerfallSavePayload.Read(s.CaptureSave()).ReadySpell);
        using var restored = f.Restore(); Assert.Equal(ready, restored.Casting.ReadyFor(1));
        var release = restored.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.Released, release.Outcome);
        Assert.Equal(f.Source, release.Bundle!.ItemId); Assert.Null(restored.Casting.ReadyFor(1));
        restored.State.ItemInstances.RemoveUnique(f.Source);
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, restored.Casting.Deliver(release.Bundle, [f.Enemy]).Outcome);
    }

    [Fact]
    public void A_breaking_use_cleans_readiness_and_cannot_cast_again()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0019");
        var s = f.Session;
        s.State.ItemInstances.ReplaceUnique(f.Source, s.State.ItemInstances.RequireUnique(f.Source) with { CurrentCondition = 10 });
        f.Update(); f.Use(); Assert.Equal(0, f.Condition); Assert.Null(s.Casting.ReadyFor(1));
        long sequence = s.Casting.NextSequence; f.Use(); Assert.Equal(sequence, s.Casting.NextSequence);
        Assert.Contains("broken", f.Message);
    }

    [Theory]
    [InlineData(false, 5)] [InlineData(true, 0)] [InlineData(true, 5)]
    public void Only_an_accepted_positive_weapon_strike_admits_item_effects(bool hit, int damage)
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0046");
        var s = f.Session; Equip(s, f.Item, weapon: true);
        var contribution = new Attack(hit, damage);
        s.State.Kit.Rules.RegisterAction(TestPayload.Definitions.RequireActor(new DaggerfallActorId("player")).ActionId!, contribution);
        long sequence = s.Casting.NextSequence; int condition = f.Condition;
        s.ResolveExplicitMelee(new(1, f.Enemy, 1, 10000, .125));
        Assert.Equal(hit && damage > 0 ? sequence + 2 : sequence, s.Casting.NextSequence);
        if (hit && damage > 0)
        {
            Assert.True(f.Condition <= condition - 20);
            Assert.All(s.State.Effects.Capture().Where(value => value.ItemId == f.Source), value =>
            { Assert.Equal(1, value.CasterId); Assert.Equal(f.Enemy, value.TargetId); });
        }
        else Assert.Equal(condition, f.Condition);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Delayed_strike_retains_accepted_source_and_one_delivery_while_unequip_stops_its_trigger(bool unequip)
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0046");
        var s = f.Session; Equip(s, f.Item, weapon: true);
        s.State.Kit.Rules.RegisterAction(TestPayload.Definitions.RequireActor(new DaggerfallActorId("player")).ActionId!, new Attack(true, 5));
        var facts = new FactBuffer<IProductFact>();
        long next = s.Casting.NextSequence;
        Assert.True(s.State.Kit.AttackExecution.Start(new(1, f.Enemy, 1, 1000, .125, true), facts));
        Assert.Equal(next, s.Casting.NextSequence);
        if (unequip) Assert.Equal(EquipmentMoveOutcome.Applied, s.EquipmentMoves.MoveToGrid(f.Item, 49).Outcome);
        var notice = new AttackImpactNotice(1, f.Enemy, 1, 1000, false);
        s.State.Kit.AttackExecution.ApplyImpacts([notice], 1, facts);
        s.State.Kit.AttackExecution.ApplyImpacts([notice], 1, facts);
        Assert.Equal(unequip ? next : next + 2, s.Casting.NextSequence);
        List<IProductFact> results = []; facts.Deliver(results.Add);
        Assert.Single(results.OfType<AttackHitFact>());
    }

    [Theory]
    [InlineData("magic-item.0035", true)]
    [InlineData("magic-item.0019", false)]
    public void Ordinary_drop_transfers_source_and_cleans_held_or_ready_state(string key, bool held)
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: key);
        var s = f.Session;
        if (held) Equip(s, f.Item); else f.Use();
        f.Update();
        f.Submit(new { action = "inventory-drop", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"unique:{f.Item.EntityId}", amount = 1 });
        if (held)
        {
            Assert.Equal("player", s.State.ItemInstances.RequireUnique(f.Source).Owner.Scope);
            Assert.Equal(EquipmentMoveOutcome.Applied, s.EquipmentMoves.MoveToGrid(f.Item, 49).Outcome);
            f.Update();
            f.Submit(new { action = "inventory-drop", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"unique:{f.Item.EntityId}", amount = 1 });
        }
        var metadata = s.State.ItemInstances.RequireUnique(f.Source);
        Assert.Equal("ground", metadata.Owner.Scope); Assert.Null(metadata.HeldCast);
        Assert.Null(s.Casting.ReadyFor(1));
        Assert.DoesNotContain(s.State.Effects.Capture(), value => value.ItemId == f.Source);
        using var restored = f.Restore();
        Assert.Equal(metadata, restored.State.ItemInstances.RequireUnique(f.Source));
    }

    [Fact]
    public void Equip_that_breaks_source_cancels_its_new_held_bundle_and_persists_no_orphan_cadence()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0035");
        var s = f.Session;
        s.State.ItemInstances.ReplaceUnique(f.Source, s.State.ItemInstances.RequireUnique(f.Source) with { CurrentCondition = 1 });
        double before = Stat(s, "strength").Value;
        Equip(s, f.Item);
        Assert.Equal(0, f.Condition); Assert.Equal(before, Stat(s, "strength").Value);
        Assert.Null(s.State.ItemInstances.RequireUnique(f.Source).HeldCast);
        Assert.DoesNotContain(s.State.Effects.Capture(), value => value.ItemId == f.Source);
        Assert.DoesNotContain(s.State.Equipment.Read().Assignments, value => value.Item == f.Item);
        using var restored = f.Restore(); Assert.Equal(0, restored.State.ItemInstances.RequireUnique(f.Source).CurrentCondition);
    }

    private static Stat Stat(DaggerfallSession s, string id) => s.State.Actors.Player.Stats.GetStat(StatId.Parse(id));
    private static void Equip(DaggerfallSession s, UniqueItem item, bool weapon = false)
    {
        var definition = TestPayload.Definitions.RequireItem(new DaggerfallItemId(item.Definition.Value));
        var slot = weapon ? TestPayload.Definitions.EquipmentSlots.Values.Single(value => value.Id.Value == "right-hand")
            : TestPayload.Definitions.EquipmentSlots.Values.First(value => value.AllowedClassifications.Intersect(definition.Equipment!.Classifications).Any());
        var result = s.EquipmentMoves.MoveToSlot(item, new SlotId(slot.Id.Value));
        Assert.True(result.Outcome == EquipmentMoveOutcome.Applied, result.Detail);
    }
    private sealed class Attack(bool hit, int damage) : ICombatContribution
    {
        public void Hit(TryHitEvent value) => value.Hit = hit;
        public void Damage(DamageEvent value) => value.Damage = damage;
    }
}
