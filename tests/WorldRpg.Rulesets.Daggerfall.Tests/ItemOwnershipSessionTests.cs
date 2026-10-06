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

    [Fact]
    public void A_held_item_moved_by_the_engine_alone_drops_its_held_cast_and_stays_mutable_and_saveable()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0035"); var s = f.Session;
        ItemCastTriggerSessionTests.Equip(s, f.Item);
        f.Update();
        Assert.NotNull(s.State.ItemInstances.RequireUnique(f.Source).HeldCast);
        Assert.Contains(s.State.Effects.Active, effect => effect.Context.Item?.Value == f.Source);
        var wagon = s.State.Wagon.EnsureCreated();

        // Only the Engine unequips and moves it; no owner stamps the move.
        s.State.Equipment.Unequip(f.Item);
        s.State.Containers.Transfer(s.State.Actors.Player.Actor.Entity, wagon.Owner, new(f.Item.Definition, 1, UniqueEntityId: f.Item.EntityId));

        var moved = s.State.ItemInstances.RequireUnique(f.Source);
        Assert.Equal((DaggerfallItemOwner.Wagon(wagon.Id), (DaggerfallHeldCastState?)null), (moved.Owner, moved.HeldCast));
        s.State.ItemInstances.ReplaceUnique(f.Source, moved with { Identified = true });
        f.Update();
        Assert.DoesNotContain(s.State.Effects.Active, effect => effect.Context.Item?.Value == f.Source);
        using var restored = f.Restore();
        var reloaded = restored.State.ItemInstances.RequireUnique(f.Source);
        Assert.Equal((DaggerfallItemOwner.Wagon(wagon.Id), (DaggerfallHeldCastState?)null, true), (reloaded.Owner, reloaded.HeldCast, reloaded.Identified));
    }
}
