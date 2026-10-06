using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;
using EquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class RazorSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_magic_resistance_survives_restore_blocks_terminal_cost_then_expiry_or_source_removal_allows_razor(bool removeItem)
    {
        var inputs = ReadInputs(TestData.RepositoryRoot); var definitions = TestPayload.Definitions;
        long enemy = inputs.Project.Actors.Values.First(x => definitions.RequireActor(x.ActorId).Team == "orcs").EntityId;
        EngineContextFake Engine()
        {
            List<string> releases = [];
            ContentFake content = new(releases); PopulateContent(content, inputs);
            var spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases); spatial.KeepPosition = true;
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random:RandomMaximum.Create());
        }
        DaggerfallSessionComposition composition = new(definitions, inputs, DaggerfallTuning.Defaults);
        var engine = Engine(); using var session = DaggerfallSession.StartNew(engine.Context, composition);
        var wardSource = session.State.Inventory.Read().UniqueItems.First();
        ulong wardId = session.State.Inventory.GetDurableItemId(wardSource.Entity).Value;
        var created = new DaggerfallItemFactory(definitions, engine.Context.Random).Create(new("Magic", "razor-resistance", DaggerfallItemOwner.Player, MagicItemKey:"magic-item.0001"));
        var identity = session.UniqueItemAllocator.AllocateReference();
        var item = session.State.Equipment.Materialize(identity, created.Item);
        session.State.ItemInstances.RegisterUnique(identity.Value, created.Metadata with { CurrentCondition = 1500 });
        Assert.Equal(EquipmentMoveOutcome.Applied, session.EquipmentMoves.MoveToSlot(item, new EquipmentSlotId("right-hand")).Outcome);
        var health = session.State.Actors.Get(enemy).Stats.GetTrack(TrackId.Parse("health"));
        health.Maximum.BaseValue = 1000; health.SetCurrent(1000);
        DaggerfallAlterationEffectsTests.Resistance(session, "magic-ward", DaggerfallMagicResistanceElement.Magic, 100, 20, enemy, wardId);
        Assert.Equal(100, Assert.Single(session.MagicProfile(enemy).ActiveResistances).Chance);
        using var restored = DaggerfallSession.Restore(Engine().Context, composition, session.CaptureSave());
        Assert.Equal(100, Assert.Single(restored.MagicProfile(enemy).ActiveResistances).Chance);
        restored.State.Kit.Rules.RegisterAction(definitions.RequireActor(new DaggerfallActorId("player")).ActionId!, new CertainStrike());
        restored.ResolveExplicitMelee(new(1, enemy, 1, 1, .125));
        Assert.False(restored.State.Actors.Get(enemy).IsDefeated);
        Assert.True(restored.State.ItemInstances.RequireUnique(identity.Value).CurrentCondition > 1300);
        if (removeItem)
        {
            var removed = restored.State.Inventory.Read().UniqueItems.Single(value => restored.State.Inventory.GetDurableItemId(value.Entity).Value == wardId);
            restored.State.Inventory.Destroy(new WorldRpg.Kit.Inventory.UniqueInventoryItem(removed.Entity.Value, new InventoryItemId(removed.Definition.Value)));
            restored.State.ItemInstances.RemoveUnique(wardId);
            restored.State.Actors.Entities.Destroy(new(WorldRpg.Kit.World.DurableIdentityKind.Item, wardId));
        }
        else restored.State.Effects.AdvanceElapsedRounds(20);
        Assert.Empty(restored.MagicProfile(enemy).ActiveResistances);
        using var unprotected = DaggerfallSession.Restore(Engine().Context, composition, restored.CaptureSave());
        unprotected.State.Kit.Rules.RegisterAction(definitions.RequireActor(new DaggerfallActorId("player")).ActionId!, new CertainStrike());
        unprotected.State.Kit.AttackExecution.ObserveTimeline(1, 1);
        unprotected.ResolveExplicitMelee(new(1, enemy, 1, 1000, .125));
        Assert.True(unprotected.State.Actors.Get(enemy).IsDefeated);
        Assert.True(unprotected.State.ItemInstances.RequireUnique(identity.Value).CurrentCondition < 1300);
    }

    [Theory]
    [InlineData(1500, true, false)]
    [InlineData(1500, true, true)]
    [InlineData(5, false, false)]
    [InlineData(5, false, true)]
    public void Restored_razor_strike_uses_normal_corpse_rewards_and_retains_target_and_source_state(int initial, bool remainsEquipped, bool experimentalKillExperience)
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        var definitions = TestPayload.Definitions;
        long enemy = inputs.Project.Actors.Values.First(x => definitions.RequireActor(x.ActorId).Team == "orcs").EntityId;
        EngineContextFake Engine()
        {
            List<string> releases = [];
            ContentFake content = new(releases); PopulateContent(content, inputs);
            var spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases); spatial.KeepPosition = true;
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: RandomMaximum.Create());
        }
        var identity = GameCompositionResolver.Resolve(FullContent(TestData.RepositoryRoot), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        var tuning = DaggerfallTuning.Defaults with { Progression = DaggerfallTuning.Defaults.Progression with { EnableExperimentalKillExperience = experimentalKillExperience } };
        DaggerfallSessionComposition composition = new(definitions, inputs, tuning, identity);
        RulesetSavePayload save;
        ulong source;
        var engine = Engine();
        using (var session = DaggerfallSession.StartNew(engine.Context, composition))
        {
            var created = new DaggerfallItemFactory(definitions, engine.Context.Random).Create(new("Magic", "razor-session", DaggerfallItemOwner.Player, MagicItemKey: "magic-item.0001"));
            var itemIdentity = session.UniqueItemAllocator.AllocateReference(); source = itemIdentity.Value;
            var item = session.State.Equipment.Materialize(itemIdentity, created.Item);
            session.State.ItemInstances.RegisterUnique(source, created.Metadata with { CurrentCondition = initial });
            Assert.Equal(EquipmentMoveOutcome.Applied, session.EquipmentMoves.MoveToSlot(item, new EquipmentSlotId("right-hand")).Outcome);
            save = session.CaptureSave();
        }
        using var restored = DaggerfallSession.Restore(Engine().Context, composition, save);
        restored.State.Kit.Rules.RegisterAction(definitions.RequireActor(new DaggerfallActorId("player")).ActionId!, new CertainStrike());
        var before = restored.State.Progression.Experience;
        restored.ResolveExplicitMelee(new ExplicitMeleeRequest(1, enemy, 1, 1, .125));
        Assert.True(restored.State.Actors.Get(enemy).IsDefeated);
        Assert.True(restored.Corpses.ContainsKey(enemy));
        Assert.Equal(experimentalKillExperience, restored.State.Progression.Experience > before);
        int condition = restored.State.ItemInstances.RequireUnique(source).CurrentCondition;
        Assert.True(condition < initial);
        Assert.Equal(remainsEquipped, restored.State.Equipment.Read().Assignments.Any(x => restored.State.Equipment.GetDurableItemId(new EntityId(x.Item.EntityId)).Value == source));
        using var after = DaggerfallSession.Restore(Engine().Context, composition, restored.CaptureSave());
        Assert.True(after.State.Actors.Get(enemy).IsDefeated);
        Assert.True(after.Corpses.ContainsKey(enemy));
        Assert.Equal(condition, after.State.ItemInstances.RequireUnique(source).CurrentCondition);
        Assert.Equal(restored.State.Progression.Experience, after.State.Progression.Experience);
        Assert.Equal(remainsEquipped, after.State.Equipment.Read().Assignments.Any(x => after.State.Equipment.GetDurableItemId(new EntityId(x.Item.EntityId)).Value == source));
    }

    private sealed class CertainStrike : ICombatContribution
    {
        public void Hit(TryHitEvent hit) => hit.Hit = true;
        public void Damage(DamageEvent damage) => damage.Damage = 13;
    }
}
