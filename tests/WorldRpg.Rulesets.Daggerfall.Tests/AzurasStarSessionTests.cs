using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class AzurasStarSessionTests
{
    [Fact]
    public void Real_player_kill_fills_equipped_star_and_used_releases_without_wear_or_new_actor()
    {
        using var f = Star(); var s = f.Session;
        Use(s, f.Source, f.Engine); Assert.Contains("no soul", f.Message);
        Equip(f); int condition = f.Condition;
        Kill(f, f.Enemy);
        Assert.Equal(7, s.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.Contains("Soul captured", s.Presentation.LastOutcome);
        int actors = s.State.Actors.All.Count();
        Use(s, f.Source, f.Engine);
        Assert.Null(s.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.Equal(condition, f.Condition); Assert.Equal(actors, s.State.Actors.All.Count());
        Assert.Contains("Released orc", f.Message);
        Assert.Equal(f.Source, s.State.Inventory.GetDurableItemId(new(f.Item.EntityId)).Value);
        Use(s, f.Source, f.Engine); Assert.Contains("no soul", f.Message);
    }

    [Fact]
    public void Occupied_star_preserves_soul_until_real_use_then_reuses_the_same_item_for_mobile_zero()
    {
        using var f = Star(); var s = f.Session; Equip(f); Kill(f, f.Enemy);
        long rat = s.SpawnActor("rat", new(new(1, 0, 1), 0)); Kill(f, rat, step: 20000);
        Assert.Equal(7, s.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.Contains("already full", s.Presentation.LastOutcome);
        Use(s, f.Source, f.Engine);
        rat = s.SpawnActor("rat", new(new(2, 0, 2), 0)); Kill(f, rat, step: 30000);
        Assert.Equal(0, s.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        using var restored = f.Restore(out var restoredEngine);
        Assert.Equal(0, restored.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.True(restored.State.HeldEnchantments.AzurasStarEquipped);
        Use(restored, f.Source, restoredEngine);
        Assert.Null(restored.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.Contains("Released rat", restoredEngine.PublishedNested("inventory", "message")!);
    }

    [Fact]
    public void Ineligible_humanoid_has_explicit_outcome_without_filling_star()
    {
        using var f = Star(); var s = f.Session; Equip(f);
        long human = s.SpawnActor(TestPayload.Definitions.Actors.Values.First(actor => actor.Kind == DaggerfallActorKinds.EnemyClass).Id.Value, new(new(1, 0, 1), 0));
        Kill(f, human);
        Assert.Null(s.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.Contains("cannot capture", s.Presentation.LastOutcome);
    }

    [Fact]
    public void Real_allied_actor_kill_does_not_trigger_the_players_held_star()
    {
        using var f = Star(); var s = f.Session;
        var skull = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random)
            .Create(new("Magic", "star-ally", DaggerfallItemOwner.Player, MagicItemKey: "magic-item.0008", Race: "breton", Gender: "male"));
        var skullId = s.UniqueItemAllocator.AllocateReference();
        new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random).Materialize(skull, s.State.Inventory, s.State.ItemInstances, unique: skullId);
        Use(s, skullId.Value, f.Engine); long ally = Assert.Single(f.Allies());
        Equip(f);
        s.State.Actors.Get(ally).ApplyPose(new(new(0, 0, -5.9f), 0));
        s.State.Actors.Get(f.Enemy).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1);
        f.Perception.Responder = request => f.Respond(request, combat: true);
        f.Appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: 2);
        f.Update(); f.Update();
        Assert.True(s.State.Actors.Get(f.Enemy).IsDefeated);
        Assert.Null(s.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
    }

    [Fact]
    public void Common_soul_trap_fills_star_once_and_held_capture_does_not_fill_a_second_gem()
    {
        using var f = Star(); var s = f.Session; Equip(f); Trap(s, f.Enemy);
        var gem = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random)
            .Create(new("MiscItems", "star-gem", DaggerfallItemOwner.Player, TemplateIndex: 274));
        var gemId = s.UniqueItemAllocator.AllocateReference();
        new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random).Materialize(gem, s.State.Inventory, s.State.ItemInstances, unique: gemId);
        Kill(f, f.Enemy);
        Assert.Equal(7, s.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.Null(s.State.ItemInstances.RequireUnique(gemId.Value).CapturedSoulMobileId);
        Assert.Single(s.State.ItemInstances.UniqueItems, item => item.Value.CapturedSoulMobileId is not null);
        Assert.DoesNotContain(s.State.Effects.Active, effect => effect.Definition.Key == "soul-trap");
    }

    [Fact]
    public void Full_star_does_not_bypass_common_soul_traps_no_empty_gem_death_refusal()
    {
        using var f = Star(); var s = f.Session; Equip(f); Assert.True(s.SoulGems.Capture(0)); Trap(s, f.Enemy);
        var health = s.State.Actors.Get(f.Enemy).Stats.GetTrack(TrackId.Parse("health"));
        health.Maximum.BaseValue = 30;
        health.SetCurrent(30);
        s.ResolveExplicitMelee(new(1, f.Enemy, 1, 10000, .125)); f.Update();
        Assert.False(s.State.Actors.Get(f.Enemy).IsDefeated);
        Assert.Equal(1d, s.State.Actors.Get(f.Enemy).Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.Equal(0, s.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.DoesNotContain(DaggerfallSavePayload.Read(s.CaptureSave()).Corpses, corpse => corpse.ActorId == f.Enemy);
    }

    [Fact]
    public void Held_eligibility_reads_live_equipment_and_break_without_any_saved_flag()
    {
        using var f = Star(); var s = f.Session;
        Assert.False(s.State.HeldEnchantments.AzurasStarEquipped);
        Equip(f); Assert.True(s.State.HeldEnchantments.AzurasStarEquipped);
        s.State.Equipment.Unequip(f.Item); Assert.False(s.State.HeldEnchantments.AzurasStarEquipped);
        Equip(f); s.ItemCondition.Damage(f.Item, f.Condition);
        Assert.False(s.State.HeldEnchantments.AzurasStarEquipped);
        Kill(f, f.Enemy);
        Assert.Null(s.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Use(s, f.Source, f.Engine); Assert.Contains("broken", f.Message);
    }

    [Fact]
    public void Unequipped_star_does_not_capture_automatically_but_remains_available_to_normal_soul_trap()
    {
        using var f = Star(); var s = f.Session;
        Kill(f, f.Enemy); Assert.Null(s.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.True(s.SoulGems.Capture(0));
        using var restored = f.Restore(out var restoredEngine);
        Assert.False(restored.State.HeldEnchantments.AzurasStarEquipped);
        Assert.Equal(0, restored.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
    }

    [Fact]
    public void Filled_item_transfer_save_and_return_preserve_identity_and_release_through_inventory_use()
    {
        using var f = Star(); var s = f.Session; Equip(f); Assert.True(s.SoulGems.Capture(0));
        s.State.Equipment.Unequip(f.Item);
        Transfer(s, f.Source, 1, f.Enemy);
        Assert.False(s.State.HeldEnchantments.AzurasStarEquipped);
        using var restored = f.Restore(out var restoredEngine);
        Assert.Equal(0, restored.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.False(restored.State.HeldEnchantments.AzurasStarEquipped);
        Use(restored, f.Source, restoredEngine); Assert.Equal(0, restored.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Transfer(restored, f.Source, f.Enemy, 1);
        Use(restored, f.Source, restoredEngine);
        Assert.Null(restored.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.Contains("Released rat", restoredEngine.PublishedNested("inventory", "message")!);
    }

    [Fact]
    public void Actual_item_loss_removes_held_eligibility_and_cannot_be_used_or_restored_as_a_star()
    {
        using var f = Star(); var s = f.Session; Equip(f); Assert.True(s.SoulGems.Capture(0));
        s.State.Equipment.Unequip(f.Item); s.State.Inventory.Destroy(f.Item);
        s.State.Actors.Entities.Destroy(new(DurableIdentityKind.Item, f.Source));
        s.State.ItemInstances.RemoveUnique(f.Source); s.RemoveUniqueItemIdentity(f.Source);
        Assert.False(s.State.HeldEnchantments.AzurasStarEquipped);
        f.Update(); f.Use(); Assert.Contains("no longer", f.Message);
        using var restored = f.Restore(out var restoredEngine);
        Assert.False(restored.State.ItemInstances.ContainsUnique(f.Source));
        Assert.False(restored.State.HeldEnchantments.AzurasStarEquipped);
    }

    private static SanguineRoseSessionTests.Fixture Star()
    {
        var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0009");
        foreach (string action in new[] { f.Session.DefinitionsByActor[1].ActionId!, f.Session.DefinitionsByActor[f.Enemy].ActionId! }.Distinct())
            f.Session.State.Kit.Rules.RegisterAction(action, new LethalStrike());
        return f;
    }
    private static void Trap(DaggerfallSession s, long target)
    {
        var setting = new DaggerfallSpellEffectDefinition("effect", 12, -1, 10, 0, 1, 100, 0, 1, 0, 0, 0, 0, 1);
        s.State.Effects.Start(new("star-trap", "soul-trap", "spell.trap", 1, target, "soul-trap", "Magic", null, 1, 10,
            JsonSerializer.SerializeToElement(new DaggerfallSoulTrapState(new(setting, 1, 0, 100)), DaggerfallSaveJsonContext.Default.DaggerfallSoulTrapState)));
    }
    private static void Equip(SanguineRoseSessionTests.Fixture f) => f.Session.State.Equipment.Equip(f.Item, [new("amulet0")]);
    private static void Use(DaggerfallSession s, ulong source, EngineContextFake engine)
    {
        var entity = s.State.Actors.Entities.Resolve(new(DurableIdentityKind.Item, source));
        s.Update(new ProductUpdate(OuterUpdate(99999), []));
        s.Update(new ProductUpdate(OuterUpdate(100000), [Ui(JsonSerializer.Serialize(new
        {
            action = "inventory-use", revision = engine.PublishedNested("inventory", "revision"), item = $"unique:{entity.Value}",
        }))]));
    }
    private static void Transfer(DaggerfallSession s, ulong item, long from, long to)
    {
        var entity = s.State.Actors.Entities.Resolve(new(DurableIdentityKind.Item, item));
        var metadata = s.State.ItemInstances.RequireUnique(item);
        s.State.Containers.Transfer(s.State.Actors.Get(from).Actor.Entity, s.State.Actors.Get(to).Actor.Entity,
            new(new(metadata.ItemId), 1, UniqueEntityId: entity.Value));
        s.State.ItemInstances.MoveUnique(item, to == 1 ? DaggerfallItemOwner.Player : DaggerfallItemOwner.Actor(to));
    }
    private static void Kill(SanguineRoseSessionTests.Fixture f, long target, long attacker = 1, ulong step = 10000)
    {
        var s = f.Session;
        s.State.Actors.Get(target).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1);
        s.ResolveExplicitMelee(new(attacker, target, 1, step, .125)); f.Update();
        Assert.True(s.State.Actors.Get(target).IsDefeated);
    }
    private sealed class LethalStrike : ICombatContribution
    {
        public void Hit(TryHitEvent hit) { hit.Hit = true; hit.Chance = 100; hit.Roll = 1; }
        public void Damage(DamageEvent damage) { damage.Damage = 1000; }
    }
}
