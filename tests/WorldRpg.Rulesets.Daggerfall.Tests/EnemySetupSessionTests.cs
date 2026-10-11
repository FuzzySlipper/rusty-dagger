using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class EnemySetupSessionTests
{
    [Theory]
    [InlineData(3, 45)]
    [InlineData(20, 100)]
    public void Monster_skills_use_spawn_level_and_survive_restore(int level, int expected)
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        long id = f.Session.SpawnActor("spriggan", new(new(0, 0, -4), 0), level);
        Assert.Equal(expected, f.Session.State.Actors.Get(id).Stats.GetStat(StatId.Parse("dodging")).Value);
        Assert.NotNull(f.Session.State.Kit.Attacks.ReachOf(id));
        var actor = f.Session.State.Actors.Get(id);
        actor.Actor.Get<WorldRpg.Rulesets.Daggerfall.Modules.Behavior.DaggerfallEnemyPerceptionMemory>().SetForcedHostile(true);
        f.Perception.Responder = _ => TestSessions.Receipt(new PerceptionPair((ulong)id, 1, 4, 1, PerceptionPairKind.Visible, 1));
        f.Update();
        Assert.NotEqual(WorldRpg.Rulesets.Daggerfall.Modules.Behavior.EnemyBehaviorState.Idle, f.Session.LastEnemyBehavior[id].State);
        var restored = f.Restore();
        using (restored) Assert.Equal(expected, restored.State.Actors.Get(id).Stats.GetStat(StatId.Parse("critical-strike")).Value);
        // Authored placements go through the same factory rule, too.
        Assert.Equal(55, f.Session.State.Actors.Get(f.Enemy).Stats.GetStat(StatId.Parse("dodging")).Value);
    }

    [Theory]
    [InlineData("orc-shaman")]
    [InlineData("orc-warlord")]
    public void Equipped_monsters_keep_generated_gear_in_the_corpse(string name)
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var s = f.Session;
        long id = s.SpawnActor(name, new(new(0, 0, -4), 0));
        var equipment = s.State.ActorInventories.EquipmentFor(id)!;
        Assert.NotEmpty(equipment.Read().Assignments);
        var items = s.State.ActorInventories.InventoryFor(id)!.Read().UniqueItems.Select(item => item.Definition.Value).ToArray();
        Assert.NotEmpty(items);
        s.State.Kit.Rules.RegisterAction(s.DefinitionsByActor[1].ActionId!, new LethalStrike());
        s.ResolveExplicitMelee(new(1, id, 1, 10000, .125));
        f.Update();
        var corpse = Assert.Single(DaggerfallSavePayload.Read(s.CaptureSave()).Corpses, row => row.ActorId == id);
        Assert.NotNull(corpse);
        Assert.All(items, item => Assert.Contains(corpse.UniqueItems, value => value.ItemId == item));
    }

    [Fact]
    public void Soul_gem_uses_the_admitted_mobile_value()
    {
        var section = TestPayload.Sections("mobiles");
        var shaman = section["mobiles"]!["mobiles"]!.AsArray().Single(row => row!["donorId"]!.GetValue<int>() == 21)!;
        shaman["soulPoints"] = 4321;
        var definitions = DaggerfallBaseContent.Read(TestPayload.Splice(section));
        var gem = definitions.RequireItem(new("template-274"));
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        var identity = f.Session.UniqueItemAllocator.AllocateReference();
        f.Session.State.Equipment.Materialize(identity, new WorldRpg.Kit.Inventory.InventoryItemId(gem.Id.Value));
        f.Session.State.ItemInstances.RegisterUnique(identity.Value, DaggerfallItemInstanceMetadata.Default(gem, DaggerfallItemOwner.Player));
        Assert.True(f.Session.SoulGems.Capture(21));
        Assert.Equal(9321, Assert.Single(f.Session.ReadInventoryPresentation().Items, item => item.Definition == gem.Id.Value).Value);
        using var restored = f.Restore();
        Assert.Equal(9321, Assert.Single(restored.ReadInventoryPresentation().Items, item => item.Definition == gem.Id.Value).Value);
    }

    [Fact]
    public void Frost_daedra_glow_follows_motion_and_is_released_on_defeat()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        long id = f.Session.SpawnActor("frost-daedra", new(new(3, 0, -4), 0));
        Assert.Equal(SpriteShadowPolicy.None, f.Appearance.SpriteRequests[^2].Material.Shadow);
        f.Update();
        var glow = f.Appearance.LightRequests.Last(light => light.Descriptor.Color == TestPayload.Definitions.Mobiles.Mobiles[25].GlowColor);
        Assert.Equal(4f, glow.Descriptor.Range);
        f.Session.State.Actors.Get(id).ApplyPose(new(new(8, 0, -4), 0));
        f.Update();
        Assert.Contains(f.Appearance.LightUpdates, update => update.Replacement.LogicalId == glow.LogicalId && update.Replacement.Descriptor.Position.X == 8);
        int before = f.Appearance.DisposedLights;
        f.Session.State.Actors.Get(id).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0);
        f.Update();
        Assert.True(f.Appearance.DisposedLights > before);
    }
    private sealed class LethalStrike : ICombatContribution
    {
        public void Hit(TryHitEvent hit) { hit.Hit = true; hit.Chance = 100; hit.Roll = 1; }
        public void Damage(DamageEvent damage) { damage.Damage = 1000; }
    }

}
