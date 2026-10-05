using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class PotionUseSessionTests
{
    [Fact]
    public void Every_published_potion_uses_normal_inventory_action_and_consumes_one_actual_dose()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        foreach (var recipe in TestPayload.Definitions.Magic.PotionRecipes.Values.OrderBy(value => value.ClassicIndex))
        {
            var stack = Add(f, recipe.Key, 2);
            f.Update();
            f.Submit(new { action = "inventory-use", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"stack:{stack.Value}" });
            Assert.Equal(1UL, f.Session.State.Inventory.Read().Stacks.Single(value => value.Id == stack).Quantity);
            Assert.Equal(recipe.Key, f.Session.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player, stack).PotionRecipeKey);
        }
        using var restored = f.Restore();
        Assert.Equal(20, restored.State.Inventory.Read().Stacks.Count(value => value.Definition.Value == "template-83"));
    }

    [Fact]
    public void Healing_and_restore_power_use_exact_magnitude_without_magicka_cost_or_readiness_loss()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var health = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        var magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        health.Maximum.BaseValue = 100; health.SetCurrent(10); magicka.Maximum.BaseValue = 1000; magicka.SetCurrent(1000);
        s.State.Character.LearnSpell("spell.023"); Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell("spell.023").Outcome);
        var ready = s.Casting.ReadyFor(s.State.Actors.Player.DurableId);
        magicka.SetCurrent(50);
        var heal = Add(f, 4975678, 1); var power = Add(f, 5188896, 1); f.Update();
        f.Submit(new { action = "inventory-use", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"stack:{heal.Value}" });
        Assert.Equal(24, health.Current); Assert.Equal(50, magicka.Current); Assert.Equal(ready, s.Casting.ReadyFor(s.State.Actors.Player.DurableId));
        f.Submit(new { action = "inventory-use", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"stack:{power.Value}" });
        Assert.Equal(59, magicka.Current); Assert.Equal(ready, s.Casting.ReadyFor(s.State.Actors.Player.DurableId));
        Assert.False(s.State.ItemInstances.ContainsStack(DaggerfallItemOwner.Player, heal));
        Assert.False(s.State.ItemInstances.ContainsStack(DaggerfallItemOwner.Player, power));
    }

    [Fact]
    public void Failed_use_and_recipe_sheet_leave_quantity_unchanged()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var potion = Add(f, 221871, 2);
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random);
        var sheet = f.Session.UniqueItemAllocator.AllocateReference();
        factory.Materialize(factory.Create(new("MiscItems", "sheet", DaggerfallItemOwner.Player, TemplateIndex: 278, PotionRecipeKey: 221871)),
            f.Session.State.Inventory, f.Session.State.ItemInstances, unique: sheet);
        var entity = f.Session.State.Inventory.Entities.Resolve(sheet);
        f.Update();
        f.Submit(new { action = "inventory-use", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"unique:{entity.Value}" });
        Assert.True(f.Session.State.ItemInstances.ContainsUnique(sheet.Value));
        f.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0);
        Assert.Equal(DaggerfallCastOutcome.SourceUnavailable, f.Session.Casting.DrinkPotion(f.Session.State.Actors.Player.DurableId, 221871).Outcome);
        Assert.Equal(2UL, f.Session.State.Inventory.Read().Stacks.Single(value => value.Id == potion).Quantity);
    }

    private static InventoryStackId Add(SanguineRoseSessionTests.Fixture f, int recipe, ulong quantity, int template = 83)
    {
        var key = $"potion-test.{template}.{recipe}"; var stack = InventoryStackId.Parse(key);
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random);
        var created = factory.Create(new(template == 83 ? "UselessItems1" : "MiscItems", key, DaggerfallItemOwner.Player,
            Quantity: quantity, TemplateIndex: template, PotionRecipeKey: recipe));
        factory.Materialize(created, f.Session.State.Inventory, f.Session.State.ItemInstances, stack);
        return stack;
    }
}
