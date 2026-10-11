using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class GeneratedRangedEnemySessionTests
{
    [Theory]
    [InlineData(20, true)]
    [InlineData(6, false)]
    [InlineData(51.2f, false)]
    [InlineData(2, false)]
    public void Generated_archer_uses_real_bow_and_quiver_only_inside_the_range_band(float distance, bool shoots)
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        long id = Spawn(f, distance);
        var inventory = f.Session.State.ActorInventories.InventoryFor(id)!;
        Assert.Contains(inventory.Read().UniqueItems, item => TestPayload.Definitions.RequireItem(new(item.Definition.Value)).Weapon?.IsBow == true);
        ulong before = Arrows(f, id);
        f.Update();
        Assert.Equal(before - (shoots ? 1UL : 0UL), Arrows(f, id));
        Assert.Equal(distance <= 2 || shoots ? EnemyBehaviorState.Attack : EnemyBehaviorState.Chase, f.Session.LastEnemyBehavior[id].State);
        using var restored = f.Restore();
        Assert.Equal(Arrows(f, id), restored.State.ActorInventories.InventoryFor(id)!.Read().Stacks.Where(stack => stack.Definition.Value == "arrow").Aggregate(0UL, (n, row) => n + row.Quantity));
    }

    [Fact]
    public void Empty_quiver_chases_then_uses_melee_without_regenerating_arrows()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        long id = Spawn(f, 20);
        var quiver = f.Session.State.ActorInventories.InventoryFor(id)!;
        foreach (var stack in quiver.Read().Stacks.Where(row => row.Definition.Value == "arrow").ToArray())
        {
            quiver.Consume(new(stack.Id, stack.Quantity));
            f.Session.State.ItemInstances.RemoveStack(DaggerfallItemOwner.Actor(id), stack.Id);
        }
        f.Update();
        Assert.Equal(EnemyBehaviorState.Chase, f.Session.LastEnemyBehavior[id].State);
        Place(f, id, 2);
        int before = f.Appearance.PlaybackRequests.Count;
        f.Update();
        Assert.Equal(EnemyBehaviorState.Attack, f.Session.LastEnemyBehavior[id].State);
        Assert.True(f.Appearance.PlaybackRequests.Count > before);
        Assert.Equal(0UL, Arrows(f, id));
        using var restored = f.Restore();
        Assert.DoesNotContain(restored.State.ActorInventories.InventoryFor(id)!.Read().Stacks, row => row.Definition.Value == "arrow");
    }

    [Fact]
    public void Last_arrow_remains_a_projectile_after_the_quiver_is_empty()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        long id = Spawn(f, 20);
        var quiver = f.Session.State.ActorInventories.InventoryFor(id)!;
        var stack = Assert.Single(quiver.Read().Stacks, row => row.Definition.Value == "arrow");
        quiver.Consume(new(stack.Id, stack.Quantity - 1));
        f.Update();
        Assert.Equal(0UL, Arrows(f, id));
        var sequence = f.Inputs.MobileSprites[141].RangedAttackSequence!;
        int index = sequence.SourceFrames.ToList().IndexOf(-1);
        f.Appearance.AdvanceReceiptForAll = CrossedMarker(1, checked((ulong)index + 1));
        var arrow = f.Inputs.ClassicPresentation.WorldVisuals.Single(row => row.MediaId == "visual.missile.arrow");
        f.Update();
        Assert.True(f.Appearance.Snapshots.Last().Any(fact => ReferenceEquals(fact.Appearance, f.Appearance.StaticMeshByPath[arrow.Path])),
            $"Outcome: {f.Session.Presentation.LastOutcome}; marker: {index + 1}; actor: {f.Session.LastEnemyBehavior[id]}; probes: {string.Join(';', f.Spatial.FloorProbes.TakeLast(4))}");
    }

    private static long Spawn(SanguineRoseSessionTests.Fixture f, float distance)
    {
        long id = f.Session.SpawnActor("archer", new(new(0, 0, -distance), 0), level: 5);
        f.Session.State.Actors.Get(id).Actor.Get<DaggerfallEnemyPerceptionMemory>().SetForcedHostile(true);
        f.Session.State.WeaponDrawn = false;
        Place(f, id, distance);
        return id;
    }
    private static void Place(SanguineRoseSessionTests.Fixture f, long id, float distance)
    {
        f.Session.State.PlayerControl.MoveTo(Vector3.Zero);
        f.Session.State.Actors.Get(id).ApplyPose(new(new(0, 0, -distance), MathF.PI));
        f.Perception.Responder = _ => Receipt(new PerceptionPair((ulong)id, 1, distance, 1, PerceptionPairKind.Visible, 1));
    }
    private static ulong Arrows(SanguineRoseSessionTests.Fixture f, long id) => f.Session.State.ActorInventories.InventoryFor(id)!.Read().Stacks
        .Where(row => row.Definition.Value == "arrow").Aggregate(0UL, (n, row) => n + row.Quantity);
}
