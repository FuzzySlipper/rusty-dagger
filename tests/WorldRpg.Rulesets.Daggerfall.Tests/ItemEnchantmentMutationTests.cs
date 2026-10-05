using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using UniqueItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class ItemEnchantmentMutationTests
{
    [Theory]
    [InlineData(23)]
    [InlineData(11)]
    public void Enchant_weight_updates_real_capacity_once_and_survives_transfer_and_restore(int type)
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var (item, id) = Weapon(f);
        ulong original = DaggerfallEncumbrancePolicy.ClassicWeightCost(TestPayload.Definitions.RequireItem(new(item.Definition.Value)));
        long before = s.State.Encumbrance.Read().CurrentClassicUnits;
        ulong expected = type == 23 ? original * 4 : 100;
        s.ItemCondition.Enchant(item, $"enchantment.{type}.-1");
        Assert.Equal(before - (long)original + (long)expected, s.State.Encumbrance.Read().CurrentClassicUnits);
        Assert.Equal(expected, s.State.ItemInstances.RequireUnique(id).WeightClassicUnits);
        Assert.Equal(1, s.State.ItemInstances.RequireUnique(id).CurrentCondition);
        Assert.Throws<InvalidOperationException>(() => s.ItemCondition.Enchant(item, $"enchantment.{type}.-1"));
        var target = s.State.Actors.Get(f.Enemy).Actor.Entity;
        s.State.Containers.Transfer(s.State.Actors.Player.Actor.Entity, target, new(item.Definition, 1, UniqueEntityId: item.EntityId));
        s.State.ItemInstances.MoveUnique(id, DaggerfallItemOwner.Actor(f.Enemy));
        Assert.Equal(before - (long)original, s.State.Encumbrance.Read().CurrentClassicUnits);
        using var restored = f.Restore();
        var actor = restored.State.Actors.Get(f.Enemy).Actor.Entity;
        var row = restored.State.Containers.Read(actor).UniqueItems.Single(value => restored.State.Actors.Entities.IdentityOf(value.Entity).Value == id);
        restored.State.Containers.Transfer(actor, restored.State.Actors.Player.Actor.Entity, new(item.Definition, 1, UniqueEntityId: row.Entity.Value));
        restored.State.ItemInstances.MoveUnique(id, DaggerfallItemOwner.Player);
        Assert.Equal(before - (long)original + (long)expected, restored.State.Encumbrance.Read().CurrentClassicUnits);
        Assert.Equal(expected, restored.State.ItemInstances.RequireUnique(id).WeightClassicUnits);
    }

    [Fact]
    public void Missing_soul_or_conflicting_forced_payload_does_not_mutate_item_or_consume_source()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var (item, id) = Weapon(f); var before = s.State.ItemInstances.RequireUnique(id);
        Assert.Throws<InvalidOperationException>(() => s.ItemCondition.Enchant(item, "enchantment.15.18"));
        Assert.Equal(before, s.State.ItemInstances.RequireUnique(id));
        ulong gem = Gem(f, 18);
        Assert.Throws<ArgumentException>(() => DaggerfallEnchantmentConstruction.Quote(TestPayload.Definitions, before, "Conflict", ["enchantment.15.18", "enchantment.23.-1"]));
        Assert.True(s.State.ItemInstances.ContainsUnique(gem));
        Assert.Equal(before, s.State.ItemInstances.RequireUnique(id));
    }

    [Fact]
    public void Ghost_binding_consumes_one_real_gem_and_keeps_forced_effects_and_weight_on_reload()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var (item, id) = Weapon(f); ulong gem = Gem(f, 18);
        s.ItemCondition.Enchant(item, "enchantment.15.18");
        Assert.False(s.State.ItemInstances.ContainsUnique(gem));
        var made = s.State.ItemInstances.RequireUnique(id).MadeEnchantment!;
        Assert.Equal(4, made.Settings.Length);
        Assert.All(made.Settings.Skip(1), setting => Assert.Equal(0, setting.Parent));
        Assert.Equal(100UL, s.State.ItemInstances.RequireUnique(id).WeightClassicUnits);
        using var restored = f.Restore();
        Assert.Equal(made.Settings, restored.State.ItemInstances.RequireUnique(id).MadeEnchantment!.Settings);
        Assert.Equal(100UL, restored.State.ItemInstances.RequireUnique(id).WeightClassicUnits);
    }

    [Fact]
    public void Soul_releases_one_hostile_actor_only_on_break_and_never_again_after_restore_or_repair()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var (item, id) = Weapon(f); Gem(f, 7);
        s.ItemCondition.Enchant(item, "enchantment.15.7");
        int before = s.State.Actors.All.Count();
        s.ItemCondition.Damage(item, 1);
        Assert.Equal(before + 1, s.State.Actors.All.Count());
        Assert.True(s.State.ItemInstances.RequireUnique(id).BoundSoulReleased);
        Assert.False(s.State.ItemInstances.RequireUnique(id).BoundSoulReleasePending);
        Assert.Equal(DaggerfallItemConditionOutcome.AlreadyBroken, s.ItemCondition.Damage(item, 1).Outcome);
        s.ItemCondition.Repair(item); s.ItemCondition.Damage(item, 1);
        Assert.Equal(before + 1, s.State.Actors.All.Count());
        using var restored = f.Restore();
        Assert.Equal(before + 1, restored.State.Actors.All.Count());
        Assert.True(restored.State.ItemInstances.RequireUnique(id).BoundSoulReleased);
    }

    [Fact]
    public void Failed_spatial_placement_keeps_pending_soul_through_save_and_retries_in_admitted_update()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var (item, id) = Weapon(f); Gem(f, 7); s.ItemCondition.Enchant(item, "enchantment.15.7");
        f.Spatial.FloorHit = _ => default;
        int before = s.State.Actors.All.Count(); s.ItemCondition.Damage(item, 1);
        Assert.Equal(before, s.State.Actors.All.Count());
        Assert.True(s.State.ItemInstances.RequireUnique(id).BoundSoulReleasePending);
        using var restored = f.Restore();
        restored.Update(new ProductUpdate(TestSessions.OuterUpdate(100), []));
        Assert.Equal(before + 1, restored.State.Actors.All.Count());
        Assert.True(restored.State.ItemInstances.RequireUnique(id).BoundSoulReleased);
    }

    [Fact]
    public void Made_held_contributions_use_the_saved_item_and_clean_up_on_break()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var (item, id) = Weapon(f);
        s.State.Character.BeginChoices();
        s.State.Character.ReplacePending(s.State.Character.ReadCreation().Current with { CareerId = "class16", CustomCareer = null, Background = null });
        s.State.Character.CommitChoices();
        int skill = s.State.Actors.Player.Stats.GetStat(StatId.Parse("long-blade")).ValueInt;
        var quote = DaggerfallEnchantmentConstruction.Quote(TestPayload.Definitions, s.State.ItemInstances.RequireUnique(id),
            "Weighted listener", ["enchantment.10.29", "enchantment.13.0", "enchantment.23.-1"]);
        s.ItemCondition.EnchantMade(item, quote.Enchantment);
        var equip = s.EquipmentMoves.MoveToSlot(item, new("right-hand"));
        Assert.True(equip.Outcome == EquipmentMoveOutcome.Applied, equip.Detail);
        Assert.Equal(skill + 15, s.State.Actors.Player.Stats.GetStat(StatId.Parse("long-blade")).ValueInt);
        Assert.True(s.State.HeldEnchantments.Talents.AcuteHearing);
        using var restored = f.Restore();
        Assert.Equal(skill + 15, restored.State.Actors.Player.Stats.GetStat(StatId.Parse("long-blade")).ValueInt);
        Assert.True(restored.State.HeldEnchantments.Talents.AcuteHearing);
        var worn = restored.State.Equipment.Read().Assignments.First(value => restored.State.Equipment.GetDurableItemId(new(value.Item.EntityId)).Value == id).Item;
        restored.ItemCondition.Damage(worn, 1);
        Assert.Equal(skill, restored.State.Actors.Player.Stats.GetStat(StatId.Parse("long-blade")).ValueInt);
        Assert.False(restored.State.HeldEnchantments.Talents.AcuteHearing);
    }

    [Fact]
    public void Binding_consumes_regular_trap_before_reusable_star_and_plain_removal_never_releases()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random);
        var starDefinition = TestPayload.Definitions.Magic.MagicItems.Values.Single(value => value.Enchantments.Any(payload => payload.Type == 26 && payload.Param == 9));
        var created = factory.Create(new("Magic", "star", DaggerfallItemOwner.Player, MagicItemKey: starDefinition.Key));
        var star = s.UniqueItemAllocator.AllocateReference(); factory.Materialize(created, s.State.Inventory, s.State.ItemInstances, unique: star);
        Assert.True(s.SoulGems.Capture(7)); ulong gem = Gem(f, 7);
        var (item, id) = Weapon(f); s.ItemCondition.Enchant(item, "enchantment.15.7");
        Assert.False(s.State.ItemInstances.ContainsUnique(gem)); Assert.Equal(7, s.State.ItemInstances.RequireUnique(star.Value).CapturedSoulMobileId);
        var (second, secondId) = Weapon(f); s.ItemCondition.Enchant(second, "enchantment.15.7");
        Assert.Null(s.State.ItemInstances.RequireUnique(star.Value).CapturedSoulMobileId);
        int before = s.State.Actors.All.Count(); s.State.Inventory.Destroy(item); s.State.ItemInstances.RemoveUnique(id);
        Assert.Equal(before, s.State.Actors.All.Count()); Assert.False(s.State.ItemInstances.RequireUnique(secondId).BoundSoulReleased);
    }

    [Fact]
    public void Forced_soul_effects_charge_gold_and_slots_but_not_power()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var (_, id) = Weapon(f);
        var quote = DaggerfallEnchantmentConstruction.Quote(TestPayload.Definitions, f.Session.State.ItemInstances.RequireUnique(id),
            "Ghost", ["enchantment.15.18"]);
        Assert.Equal(-300, quote.Power);
        Assert.Equal(1000, quote.Gold);
        Assert.Equal(4, quote.Enchantment.Settings.Length);
    }

    [Fact]
    public void Soul_forced_weapon_effects_remain_allowed_on_nonweapon_items()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var definition = TestPayload.Definitions.RequireItem(new("template-135"));
        var metadata = DaggerfallItemInstanceMetadata.Default(definition, DaggerfallItemOwner.Player);
        var quote = DaggerfallEnchantmentConstruction.Quote(TestPayload.Definitions, metadata, "Ghost jewel", ["enchantment.15.18"]);
        Assert.Contains(quote.Enchantment.Settings, value => value.Key == "enchantment.20.0" && value.Parent == 0);
        Assert.Throws<ArgumentException>(() => DaggerfallEnchantmentConstruction.Quote(TestPayload.Definitions, metadata, "Low damage", ["enchantment.20.0"]));
    }

    internal static (UniqueItem Item, ulong Id) Weapon(SanguineRoseSessionTests.Fixture f)
    {
        var s = f.Session; var definition = TestPayload.Definitions.RequireItem(new("template-121-daedric"));
        var id = s.UniqueItemAllocator.AllocateReference(); var item = s.State.Equipment.Materialize(id, new(definition.Id.Value));
        s.State.ItemInstances.RegisterDefaultUnique(id.Value, definition, DaggerfallItemOwner.Player);
        return (item, id.Value);
    }

    internal static ulong Gem(SanguineRoseSessionTests.Fixture f, int mobile)
    {
        var s = f.Session; var factory = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random);
        var created = factory.Create(new("MiscItems", $"gem-{s.State.ItemInstances.Revision}", DaggerfallItemOwner.Player, TemplateIndex: 274));
        var id = s.UniqueItemAllocator.AllocateReference(); factory.Materialize(created, s.State.Inventory, s.State.ItemInstances, unique: id);
        Assert.True(s.SoulGems.Capture(mobile)); return id.Value;
    }
}
