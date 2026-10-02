using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using Item = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class StrikeEnchantmentSessionTests
{
    [Theory]
    [InlineData(4, 0)] [InlineData(4, 1)] [InlineData(4, 2)] [InlineData(4, 3)]
    [InlineData(20, 0)] [InlineData(20, 1)] [InlineData(20, 2)] [InlineData(20, 3)]
    public void Each_enemy_group_modulates_the_one_accepted_strike_only_for_its_matching_enemy(int type, int parameter)
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var source = Add(f, type, parameter); Equip(s, source.Item);
        DaggerfallEnemyGroup group = parameter switch { 0 => DaggerfallEnemyGroup.Undead, 1 => DaggerfallEnemyGroup.Daedra, 2 => DaggerfallEnemyGroup.Humanoid, _ => DaggerfallEnemyGroup.Animals };
        long target = Spawn(f, group);
        var health = Health(s, target); health.Maximum.BaseValue = 100; health.SetCurrent(100); double before = health.Current;
        s.State.Kit.Rules.RegisterAction(s.DefinitionsByActor[1].ActionId!, new Strike(true, 10));
        s.ResolveExplicitMelee(new(1, target, 1, 10000, .125));
        Assert.Equal(type == 4 ? 15d : 5d, before - health.Current);
        long other = Spawn(f, group == DaggerfallEnemyGroup.Animals ? DaggerfallEnemyGroup.Undead : DaggerfallEnemyGroup.Animals);
        var otherHealth = Health(s, other); otherHealth.Maximum.BaseValue = 100; otherHealth.SetCurrent(100); before = otherHealth.Current;
        s.ResolveExplicitMelee(new(1, other, 1, 20000, .125));
        Assert.Equal(10d, before - otherHealth.Current);
    }

    [Fact]
    public void A_missed_contact_preserves_the_donor_payload_semantics_and_no_target_does_not_trigger()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var source = Add(f, 4, 2); Equip(s, source.Item);
        s.State.ItemInstances.ReplaceUnique(source.Id,
            s.State.ItemInstances.RequireUnique(source.Id) with { PoisonVariant = 130 });
        var health = Health(s, f.Enemy); double before = health.Current;
        s.State.Kit.Rules.RegisterAction(s.DefinitionsByActor[1].ActionId!, new Strike(false, 10));
        s.ResolveExplicitMelee(new(1, f.Enemy, 1, 10000, .125));
        Assert.Equal(5d, before - health.Current); // Only PotentVs: the missed physical strike contributes zero.
        Assert.Equal(130, s.State.ItemInstances.RequireUnique(source.Id).PoisonVariant);
        before = health.Current;
        s.State.Kit.AttackExecution.Start(new(1, null, 1, 20000, .125, false), new FactBuffer<IProductFact>());
        Assert.Equal(before, health.Current);
        Assert.Equal(EquipmentMoveOutcome.Applied, s.EquipmentMoves.MoveToGrid(source.Item, 49).Outcome);
        s.ResolveExplicitMelee(new(1, f.Enemy, 1, 30000, .125));
        Assert.Equal(before, health.Current);
    }

    [Fact]
    public void Whenever_used_leech_updates_the_item_age_on_missed_strike_and_actual_inventory_use_without_extra_wear()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var source = Add(f, 21, 0); Equip(s, source.Item);
        int condition = s.State.ItemInstances.RequireUnique(source.Id).CurrentCondition;
        var health = Health(s, 1); double before = health.Current;
        s.AdvanceElapsedTime(60);
        s.State.Kit.Rules.RegisterAction(s.DefinitionsByActor[1].ActionId!, new Strike(false, 10));
        s.State.Kit.AttackExecution.Start(new(1, null, 1, 9000, .125, false), new FactBuffer<IProductFact>()); Assert.Equal(before, health.Current);
        s.ResolveExplicitMelee(new(1, f.Enemy, 1, 10000, .125));
        Assert.Equal(before - 8, health.Current);
        Assert.Equal(Minute(s), s.State.ItemInstances.RequireUnique(source.Id).HealthLeechLastUsedMinute);
        Assert.Equal(condition, s.State.ItemInstances.RequireUnique(source.Id).CurrentCondition);
        f.Update();
        f.Submit(new { action = "inventory-use", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"unique:{source.Item.EntityId}" });
        Assert.Equal(before - 24, health.Current);
        Assert.Equal(condition, s.State.ItemInstances.RequireUnique(source.Id).CurrentCondition);
        using var restored = f.Restore();
        Assert.Equal(health.Current, Health(restored, 1).Current);
        Assert.Equal(Minute(s), restored.State.ItemInstances.RequireUnique(source.Id).HealthLeechLastUsedMinute);
    }

    [Theory]
    [InlineData(1, 1440)] [InlineData(2, 10080)]
    public void Timed_leech_uses_strict_day_week_thresholds_suppresses_synthetic_time_and_rebinds_age_after_save(int parameter, int interval)
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var source = Add(f, 21, parameter); Equip(s, source.Item);
        long age = s.State.ItemInstances.RequireUnique(source.Id).HealthLeechLastUsedMinute;
        Assert.Equal(Minute(s), age); // The real enchantment commit initialized it, not equipping it.
        var health = Health(s, 1); double before = health.Current;
        s.AdvanceElapsedTime(interval * 60, resting: true); Assert.Equal(before, health.Current);
        s.AdvanceElapsedTime(60, resting: true); Assert.Equal(before - 1, health.Current);
        s.AdvanceElapsedTime(4 * 60); Assert.Equal(before - 1, health.Current);
        using var restored = f.Restore();
        Assert.Equal(age, restored.State.ItemInstances.RequireUnique(source.Id).HealthLeechLastUsedMinute);
        restored.AdvanceElapsedTime(60, resting: true); Assert.Equal(before - 2, Health(restored, 1).Current);
        var item = restored.State.Equipment.Read().Assignments.DistinctBy(value => value.Item.EntityId).Single(value => restored.State.Actors.Entities.IdentityOf(new(value.Item.EntityId)).Value == source.Id).Item;
        Assert.Equal(EquipmentMoveOutcome.Applied, restored.EquipmentMoves.MoveToGrid(item, 49).Outcome);
        restored.AdvanceElapsedTime(4 * 60, resting: true); Assert.Equal(before - 2, Health(restored, 1).Current);
    }

    [Fact]
    public void Vampiric_strike_restores_base_damage_with_overflow_and_handles_target_death_through_shared_vitality()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var source = Add(f, 6, 1); Equip(s, source.Item);
        var player = Health(s, 1); player.SetCurrent(player.Maximum.Value - 2);
        var enemy = Health(s, f.Enemy); enemy.SetCurrent(2);
        s.State.Kit.Rules.RegisterAction(s.DefinitionsByActor[1].ActionId!, new Strike(true, 10));
        s.ResolveExplicitMelee(new(1, f.Enemy, 1, 10000, .125));
        Assert.Equal(player.Maximum.Value, player.Current); Assert.True(s.State.Actors.Get(f.Enemy).IsDefeated);
        using var restored = f.Restore(); Assert.True(restored.State.Actors.Get(f.Enemy).IsDefeated);
    }

    [Fact]
    public void Vampiric_range_drains_all_living_enemies_in_range_on_the_shared_beat_and_stops_when_the_source_breaks()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var source = Add(f, 6, 0); Equip(s, source.Item);
        var origin = s.State.PlayerControl.Position!.Value;
        long near = s.SpawnActor("rat", new(origin with { X = origin.X + 1 }, 0), level: 50);
        long edge = s.SpawnActor("orc", new(origin with { Z = origin.Z + 2.25f }, 0), level: 50);
        long outside = s.SpawnActor("rat", new(origin with { Z = origin.Z + 2.26f }, 0), level: 50);
        s.State.Actors.Get(near).ApplyPose(new(origin with { X = origin.X + 1 }, 0));
        s.State.Actors.Get(edge).ApplyPose(new(origin with { Z = origin.Z + 2.25f }, 0));
        s.State.Actors.Get(outside).ApplyPose(new(origin with { Z = origin.Z + 2.26f }, 0));
        var player = Health(s, 1); player.SetCurrent(player.Maximum.Value - 20);
        double before = player.Current, nearBefore = Health(s, near).Current, edgeBefore = Health(s, edge).Current, outsideBefore = Health(s, outside).Current;
        s.AdvanceElapsedTime(60);
        Assert.Equal(nearBefore - 1, Health(s, near).Current); Assert.Equal(edgeBefore - 1, Health(s, edge).Current);
        Assert.Equal(outsideBefore, Health(s, outside).Current); Assert.Equal(before + 2, player.Current);
        s.AdvanceElapsedTime(3 * 60); Assert.Equal(before + 2, player.Current);
        s.AdvanceElapsedTime(60); Assert.Equal(before + 4, player.Current);
        Assert.Equal(DaggerfallItemConditionOutcome.Broken, s.ItemCondition.Damage(source.Item, int.MaxValue).Outcome);
        s.AdvanceElapsedTime(4 * 60); Assert.Equal(before + 4, player.Current);
        using var restored = f.Restore(); restored.AdvanceElapsedTime(4 * 60); Assert.Equal(before + 4, Health(restored, 1).Current);
    }

    [Fact]
    public void A_lethal_leech_does_not_resurrect_the_wearer_and_tuning_and_save_reject_invalid_values()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var source = Add(f, 21, 0); Equip(s, source.Item); Health(s, 1).SetCurrent(8);
        s.State.Kit.Rules.RegisterAction(s.DefinitionsByActor[1].ActionId!, new Strike(false, 0));
        s.ResolveExplicitMelee(new(1, f.Enemy, 1, 10000, .125)); Assert.Equal(0, Health(s, 1).Current);
        s.AdvanceElapsedTime(60, resting: true); Assert.Equal(0, Health(s, 1).Current);
        using var restored = f.Restore(); Assert.Equal(0, Health(restored, 1).Current);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallStrikeEnchantmentTuning(-1, 2).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallStrikeEnchantmentTuning(5, double.NaN).Validate());
        var metadata = s.State.ItemInstances.RequireUnique(source.Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => (metadata with { HealthLeechLastUsedMinute = -1 }).Validate());
    }

    private static (Item Item, ulong Id) Add(SanguineRoseSessionTests.Fixture f, int type, int parameter)
    {
        var s = f.Session;
        var created = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random)
            .Create(new("Weapons", $"strike-{type}-{parameter}", DaggerfallItemOwner.Player, TemplateIndex: 115, Material: "daedric"));
        var id = s.UniqueItemAllocator.AllocateReference();
        var item = s.State.Equipment.Materialize(id, created.Item);
        s.State.ItemInstances.RegisterUnique(id.Value, created.Metadata);
        Assert.Equal(DaggerfallItemConditionOutcome.Enchanted, s.ItemCondition.Enchant(item, $"enchantment.{type}.{parameter}").Outcome);
        return (item, id.Value);
    }
    private static long Spawn(SanguineRoseSessionTests.Fixture f, DaggerfallEnemyGroup group)
    {
        var definition = TestPayload.Definitions.Actors.Values.First(value => value.Kind == DaggerfallActorKinds.Monster && DaggerfallFormulaPolicy.EnemyGroupFor(value) == group);
        var origin = f.Session.State.PlayerControl.Position!.Value;
        return f.Session.SpawnActor(definition.Id.Value, new(origin with { Z = origin.Z - 1 }, 0), level: 50);
    }
    private static void Equip(DaggerfallSession s, Item item) => Assert.Equal(EquipmentMoveOutcome.Applied, s.EquipmentMoves.MoveToSlot(item, new("right-hand")).Outcome);
    private static Track Health(DaggerfallSession s, long id) => (id == 1 ? s.State.Actors.Player.Stats : s.State.Actors.Get(id).Stats).GetTrack(TrackId.Parse("health"));
    private static long Minute(DaggerfallSession s) { var v = DaggerfallSavePayload.Read(s.CaptureSave()).Calendar; return new World.DaggerfallCalendar(v.Year, v.Month, v.Day, v.Hour, v.Minute, v.Second).ToAbsoluteSeconds() / 60; }
    private sealed class Strike(bool hit, int damage) : ICombatContribution
    {
        public void Hit(TryHitEvent value) => value.Hit = hit;
        public void Damage(DamageEvent value) => value.Damage = damage;
    }
}
