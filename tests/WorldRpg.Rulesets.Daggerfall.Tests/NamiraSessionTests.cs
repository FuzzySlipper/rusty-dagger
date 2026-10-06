using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;
using EquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class NamiraSessionTests
{
    [Theory]
    [InlineData(1500, true)]
    [InlineData(5, false)]
    public void Namira_restore_reflects_through_normal_corpse_creation_and_retains_condition(int initial, bool remainsEquipped)
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        var definitions = TestPayload.Definitions;
        long enemy = inputs.Project.Actors.Values.First(placement => definitions.RequireActor(placement.ActorId).Team == "orcs").EntityId;
        EngineContextFake Engine(out AppearanceFake appearance, out PerceptionFake perception)
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, inputs);
            SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
            spatial.KeepPosition = true;
            appearance = new(releases);
            perception = PerceptionFake.Create();
            return EngineContextFake.Create(content, spatial.Service, appearance, perception.Service, random: RandomMinimum.Create());
        }
        var identity = GameCompositionResolver.Resolve(FullContent(TestData.RepositoryRoot), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        DaggerfallSessionComposition composition = new(definitions, inputs, DaggerfallTuning.Defaults, identity);
        var engine = Engine(out _, out _);
        RulesetSavePayload save;
        ulong durable;
        using (var session = DaggerfallSession.StartNew(engine.Context, composition))
        {
            var created = new DaggerfallItemFactory(definitions, engine.Context.Random)
                .Create(new("Magic", "namira-session", DaggerfallItemOwner.Player, MagicItemKey: "magic-item.0007"));
            var itemIdentity = session.UniqueItemAllocator.AllocateReference();
            durable = itemIdentity.Value;
            var item = session.State.Equipment.Materialize(itemIdentity, created.Item);
            session.State.ItemInstances.RegisterUnique(durable, created.Metadata with { CurrentCondition = initial });
            Assert.Equal(EquipmentMoveOutcome.Applied, session.EquipmentMoves.MoveToSlot(item, new EquipmentSlotId("ring1")).Outcome);
            session.State.Actors.Get(enemy).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1);
            save = session.CaptureSave();
        }
        using var restored = DaggerfallSession.Restore(Engine(out var appearance, out var perception).Context, composition, save);
        restored.State.Kit.Rules.RegisterAction(definitions.RequireActor(inputs.Project.Actors[enemy].ActorId).ActionId!, new CertainStrike());
        perception.Receipt = Receipt(new PerceptionPair(checked((ulong)enemy), 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        // The minimum draw selects the orc's first authored alternate, whose strike beat differs
        // from its primary sequence. Deliver that real marker rather than a primary-only fixture.
        var alternate = inputs.ActorSprites[enemy].AttackSequences[1];
        Assert.True(alternate.Chance > 0);
        ulong marker = checked((ulong)alternate.SourceFrames.ToList().IndexOf(-1) + 1);
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: marker);
        restored.Update(new ProductUpdate(OuterUpdate(1), []));
        restored.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.True(restored.State.Actors.Get(enemy).IsDefeated, $"Enemy {enemy}, behavior {restored.LastEnemyBehavior[enemy].State}, condition {restored.State.ItemInstances.RequireUnique(durable).CurrentCondition}, outcome {restored.Presentation.LastOutcome}");
        Assert.True(restored.Corpses.ContainsKey(enemy));
        Assert.Equal(Math.Max(0, initial - 13), restored.State.ItemInstances.RequireUnique(durable).CurrentCondition);
        Assert.Equal(remainsEquipped, restored.State.Equipment.Read().Assignments.Any(assignment => restored.State.Equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == durable));
        using var after = DaggerfallSession.Restore(Engine(out _, out _).Context, composition, restored.CaptureSave());
        Assert.True(after.State.Actors.Get(enemy).IsDefeated);
        Assert.True(after.Corpses.ContainsKey(enemy));
        Assert.Equal(Math.Max(0, initial - 13), after.State.ItemInstances.RequireUnique(durable).CurrentCondition);
        Assert.Equal(remainsEquipped, after.State.Equipment.Read().Assignments.Any(assignment => after.State.Equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == durable));
    }

    private sealed class CertainStrike : ICombatContribution
    {
        public void Hit(TryHitEvent hit) => hit.Hit = true;
        public void Damage(DamageEvent damage) => damage.Damage = 13;
    }
}
