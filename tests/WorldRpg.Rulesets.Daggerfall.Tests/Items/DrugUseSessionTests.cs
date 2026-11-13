using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Taking a drug from the inventory through the player's own use action. As the donor's inventory window does, the
/// dose is taken whatever the poison owner decides: a first-level player is refused, a later one is drugged.
/// </summary>
public sealed class DrugUseSessionTests
{
    [Theory]
    [InlineData(78, (int)DaggerfallPoisonVariant.Indulcet)]
    [InlineData(79, (int)DaggerfallPoisonVariant.Sursum)]
    [InlineData(80, (int)DaggerfallPoisonVariant.QuaestoVil)]
    [InlineData(81, (int)DaggerfallPoisonVariant.Aegrotat)]
    public void A_drug_used_from_the_inventory_is_taken_and_drugs_a_player_past_the_first_level(int template, int poison)
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var player = f.Session.State.Actors.Player;

        ulong first = Add(f, template);
        Use(f, first);
        f.Update();
        Assert.Equal("The drug has no effect on you.", f.Message);
        Assert.False(f.Session.State.ItemInstances.ContainsUnique(first));
        Assert.False(f.Session.State.Poisons.IsAfflicted(player.Actor));

        player.Progression.AdvanceTo(500, 5);
        ulong second = Add(f, template);
        Use(f, second);
        f.Update();
        Assert.Equal("The drug takes hold.", f.Message);
        Assert.False(f.Session.State.ItemInstances.ContainsUnique(second));
        Assert.True(f.Session.State.Poisons.IsAfflicted(player.Actor));
        Assert.Contains(f.Session.State.Effects.Active, effect => effect.Definition.Key == DaggerfallPoisonPolicy.EffectKey((DaggerfallPoisonVariant)poison));
    }

    private static void Use(SanguineRoseSessionTests.Fixture f, ulong item)
    {
        var entity = f.Session.State.Inventory.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Item, item));
        f.Update();
        f.Submit(new { action = "inventory-use", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"unique:{entity.Value}" });
    }

    /// <summary>One drug in the pack; drugs are unique items, one dose each.</summary>
    private static ulong Add(SanguineRoseSessionTests.Fixture f, int template)
    {
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random);
        var identity = f.Session.UniqueItemAllocator.AllocateReference();
        factory.Materialize(factory.Create(new(TestPayload.Definitions.ItemTemplateCatalog.Templates[template].Groups[0], $"drug-test.{identity.Value}",
            DaggerfallItemOwner.Player, TemplateIndex: template)), f.Session.State.Inventory, f.Session.State.ItemInstances, unique: identity);
        return identity.Value;
    }
}
