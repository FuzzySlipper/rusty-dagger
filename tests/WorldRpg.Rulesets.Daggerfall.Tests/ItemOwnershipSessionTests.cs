using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>A resident unique item's owner is the Engine container that holds it, not a copied record.</summary>
public sealed class ItemOwnershipSessionTests
{
    [Fact]
    public void An_engine_transfer_that_skips_the_ownership_stamp_still_reports_the_holding_container()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var (item, id) = ItemEnchantmentMutationTests.Weapon(f);
        var wagon = s.State.Wagon.EnsureCreated();
        Assert.Equal(DaggerfallItemOwner.Player, s.State.ItemInstances.RequireUnique(id).Owner);

        // Only the Engine moves it; no transfer owner stamps the new owner.
        s.State.Containers.Transfer(s.State.Actors.Player.Actor.Entity, wagon.Owner, new(item.Definition, 1, UniqueEntityId: item.EntityId));

        Assert.Equal(DaggerfallItemOwner.Wagon(wagon.Id), s.State.ItemInstances.RequireUnique(id).Owner);
        Assert.Contains(s.State.ItemInstances.UniqueItems, entry => entry.Key == id && entry.Value.Owner == DaggerfallItemOwner.Wagon(wagon.Id));
        using var restored = f.Restore();
        Assert.Equal(DaggerfallItemOwner.Wagon(wagon.Id), restored.State.ItemInstances.RequireUnique(id).Owner);
    }
}
