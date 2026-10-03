using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Targeting;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using UniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;
using EquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class NewGameBowSessionTests
{
    [Theory]
    [InlineData(4d)]
    [InlineData(9.5d)]
    public void A_new_archer_targets_beyond_melee_reach_and_spends_one_starting_arrow(double distance)
    {
        using Fixture f = new(distance);
        var action = f.Game.InspectPlaytestAction("attack");
        Assert.True(action.Available, action.Reason);
        double stamina = f.Game.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current;

        f.Attack();

        TargetingEvidence targeting = Assert.IsType<TargetingEvidence>(f.Game.LastMeleeTargeting);
        Assert.Equal(Fixture.Target, targeting.SelectedTargetId);
        Assert.Equal(TestPayload.Definitions.Actions["bow-shot"].Reach,
            targeting.Request.Observers.Span[0].MaximumDistance);
        Assert.Equal(23UL, f.Arrows().Quantity);
        Assert.Equal(stamina - 5, f.Game.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current);
        Assert.NotNull(f.Game.State.Actors.Player.Attack.Pending);
    }

    [Fact]
    public void A_new_archer_cannot_select_or_damage_a_target_beyond_the_authored_bow_reach()
    {
        using Fixture f = new(TestPayload.Definitions.Actions["bow-shot"].Reach!.Value + 1);
        double health = f.Game.State.Actors.Get(Fixture.Target).Stats.GetTrack(TrackId.Parse("health")).Current;

        f.Attack();

        Assert.Null(Assert.IsType<TargetingEvidence>(f.Game.LastMeleeTargeting).SelectedTargetId);
        Assert.Null(f.Game.State.Actors.Player.Attack.Pending);
        Assert.Equal(health, f.Game.State.Actors.Get(Fixture.Target).Stats.GetTrack(TrackId.Parse("health")).Current);
        using var observation = JsonDocument.Parse(f.Game.ReadPlaytestObservation().Message);
        Assert.Equal("AttackRejected:NoTargetInReach", observation.RootElement.GetProperty("lastCombatEvent").GetProperty("kind").GetString());
    }

    [Fact]
    public void A_new_archer_without_arrows_refuses_the_shot_before_spending_stamina()
    {
        using Fixture f = new(4);
        InventoryStackId stack = f.Arrows().Id;
        f.Game.State.Inventory.Consume(new InventoryConsume(stack, 24));
        f.Game.State.ItemInstances.RemoveStack(DaggerfallItemOwner.Player, stack);
        double stamina = f.Game.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current;
        var action = f.Game.InspectPlaytestAction("attack");
        Assert.False(action.Available);
        Assert.Equal("EmptyQuiver", action.Reason);

        f.Attack();

        Assert.Null(f.Game.State.Actors.Player.Attack.Pending);
        Assert.Equal(stamina, f.Game.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current);
        using var observation = JsonDocument.Parse(f.Game.ReadPlaytestObservation().Message);
        Assert.Equal("AttackRejected:EmptyQuiver", observation.RootElement.GetProperty("lastCombatEvent").GetProperty("kind").GetString());
    }

    [Fact]
    public void The_last_normalized_starting_arrow_retires_its_player_metadata()
    {
        using Fixture f = new(4);
        InventoryStackId stack = f.Arrows().Id;
        f.Game.State.Inventory.Consume(new InventoryConsume(stack, 23));

        f.Attack();

        Assert.DoesNotContain(f.Game.State.Inventory.Read().Stacks, item => item.Id == stack);
        Assert.Throws<InvalidOperationException>(() => f.Game.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player, stack));
        Assert.NotNull(f.Game.State.Actors.Player.Attack.Pending);
        // The current save must not retain an orphan instance for a consumed stack.
        _ = DaggerfallSavePayload.Read(f.Game.CaptureSave());
    }

    private sealed class Fixture : IDisposable
    {
        internal const long Target = 2008;
        internal DaggerfallSession Game { get; }
        private readonly DaggerfallSession title;

        internal Fixture(double distance)
        {
            var inputs = ReadInputs(TestData.RepositoryRoot);
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, inputs);
            var spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
            spatial.KeepPosition = true;
            var perception = PerceptionFake.Create();
            perception.Responder = request =>
            {
                var observer = request.Observers.Span[0];
                if (observer.Entity != DaggerfallActorIdentity.PlayerEntityId) return Receipt();
                var targets = request.Targets.ToArray().Where(item => item.Entity == Target).ToArray();
                if (targets.Length == 0) return Receipt();
                var target = targets[0];
                double separation = Vector3.Distance(observer.Origin, target.Center);
                return separation <= observer.MaximumDistance
                    ? Receipt(new PerceptionPair(observer.Entity, target.Entity, separation, 1, PerceptionPairKind.Visible, separation))
                    : Receipt();
            };
            var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service,
                random: RandomMinimum.Create());
            title = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));
            NewGameSessionTests.Commit(title, "class13");
            Game = Assert.IsType<DaggerfallSession>(title.CreateNewGame());
            Game.ApplyProductMode(ProductMode.Playing);
            var bow = Game.State.Inventory.Read().UniqueItems.Single(item => item.Definition.Value == "template-130-iron");
            var move = Game.EquipmentMoves.MoveToSlot(new UniqueInventoryItem(bow.Entity.Value, new InventoryItemId(bow.Definition.Value)),
                new EquipmentSlotId("right-hand"));
            Assert.Equal(EquipmentMoveOutcome.Applied, move.Outcome);
            var origin = Game.State.PlayerControl.Position!.Value.ToVector();
            float bodyHeight = inputs.ActorSprites[Target].Size.Y * .5f;
            Game.State.Actors.Get(Target).ApplyPose(new ActorPose(
                WorldPoint.From(origin - Vector3.UnitZ * (float)distance - Vector3.UnitY * bodyHeight), 0));
            Game.State.PlayerControl.YawRadians = 0;
            Game.State.PlayerControl.PitchRadians = 0;
        }

        internal InventoryStack Arrows() => Assert.Single(Game.State.Inventory.Read().Stacks,
            item => item.Definition.Value == "template-131");

        internal void Attack() => Game.Update(new ProductUpdate(OuterUpdate(1),
            [Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "attack")]));

        public void Dispose() { Game.Dispose(); title.Dispose(); }
    }
}
