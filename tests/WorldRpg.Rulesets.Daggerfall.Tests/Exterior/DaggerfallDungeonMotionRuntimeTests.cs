using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonMotionRuntimeTests
{
    [Fact]
    public void Rebasing_a_moving_model_keeps_its_anchor_for_the_remaining_motion()
    {
        Transform start = new(new Vector3(5, 2, -3), Quaternion.Identity, Vector3.One);
        DaggerfallDungeonActionDefinition action = Action("platform", Translate(-Vector3.UnitX, 2d));
        using EntityStore store = CreateStore(start, out EntityId target);
        DaggerfallDungeonMotionRuntime runtime = new(store, "dungeon/a", [new(action, target)]);
        runtime.Activate(action.Id);
        runtime.Advance(.5);
        Vector3 delta = new(-1000, 3, 40);
        runtime.Rebase(delta);
        runtime.Advance(1.5);
        Assert.Equal(new Vector3(4, 2, -3) + delta, store.Get(target, EngineComponentTypes.Transform).Translation);
        Assert.Equal(DaggerfallDungeonMotionPhase.End, Assert.Single(runtime.Capture().Actions).Phase);
    }

    [Fact]
    public void Translation_toggles_at_endpoints_ignores_retriggers_while_playing_and_restores_mid_motion()
    {
        Transform start = new(new Vector3(5f, 2f, -3f), Quaternion.Identity, Vector3.One);
        DaggerfallDungeonActionDefinition action = Action("platform", Translate(-Vector3.UnitX, 2d));
        using EntityStore store = CreateStore(start, out EntityId target);
        DaggerfallDungeonMotionBinding binding = new(action, target);
        DaggerfallDungeonMotionRuntime runtime = new(store, "dungeon/a", [binding]);

        Assert.Equal(DaggerfallDungeonMotionActivation.StartedForward, runtime.Activate(action.Id));
        Assert.Equal(DaggerfallDungeonMotionActivation.IgnoredWhileMoving, runtime.Activate(action.Id));
        runtime.Advance(.5d);
        Transform halfway = store.Get(target, EngineComponentTypes.Transform);
        Assert.Equal(new Vector3(4.75f, 2f, -3f), halfway.Translation);

        DaggerfallDungeonMotionSnapshot snapshot = runtime.Capture();
        DaggerfallDungeonMotionSave saved = Assert.Single(snapshot.Actions);
        Assert.Equal(DaggerfallDungeonMotionPhase.PlayingForward, saved.Phase);
        Assert.Equal(DaggerfallDungeonMotionEndpoint.End, saved.EndpointIntent);
        Assert.Equal(.5d, saved.ElapsedSeconds);

        using EntityStore restoredStore = CreateStore(start, out EntityId restoredTarget);
        DaggerfallDungeonMotionRuntime restored = new(restoredStore, "dungeon/a", [new(action, restoredTarget)], snapshot);
        Assert.Equal(halfway, restoredStore.Get(restoredTarget, EngineComponentTypes.Transform));
        restored.Advance(1.5d);
        Assert.Equal(DaggerfallDungeonMotionPhase.End, Assert.Single(restored.Capture().Actions).Phase);
        Assert.Equal(new Vector3(4f, 2f, -3f), restoredStore.Get(restoredTarget, EngineComponentTypes.Transform).Translation);

        Assert.Equal(DaggerfallDungeonMotionActivation.StartedReverse, restored.Activate(action.Id));
        restored.Advance(1d);
        Assert.Equal(DaggerfallDungeonMotionActivation.IgnoredWhileMoving, restored.Activate(action.Id));
        restored.Advance(1d);
        DaggerfallDungeonMotionSave returned = Assert.Single(restored.Capture().Actions);
        Assert.Equal(DaggerfallDungeonMotionPhase.Start, returned.Phase);
        Assert.Equal(DaggerfallDungeonMotionEndpoint.Start, returned.EndpointIntent);
        Assert.Equal(start, restoredStore.Get(restoredTarget, EngineComponentTypes.Transform));
    }

    [Fact]
    public void Rotation_keeps_object_origin_as_pivot()
    {
        Quaternion startRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .35f);
        Transform start = new(new Vector3(2f, 7f, 9f), startRotation, Vector3.One);
        DaggerfallDungeonActionDefinition action = Action("turning-platform", new(DaggerfallDungeonMotionKind.Rotation, Vector3.Zero, Vector3.UnitX, SourceTurn, 1d));
        using EntityStore store = CreateStore(start, out EntityId target);
        DaggerfallDungeonMotionRuntime runtime = new(store, "dungeon/turn", [new(action, target)]);

        Assert.Equal(DaggerfallDungeonMotionActivation.StartedForward, runtime.Activate(action.Id));
        runtime.Advance(.5d);
        Transform halfway = store.Get(target, EngineComponentTypes.Transform);
        float halfAngle = SourceTurn * .5f;
        Quaternion expected = Quaternion.Normalize(startRotation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, halfAngle));
        Assert.Equal(start.Translation, halfway.Translation);
        Assert.Equal(expected.X, halfway.Rotation.X, 0.000001f);
        Assert.Equal(expected.Y, halfway.Rotation.Y, 0.000001f);
        Assert.Equal(expected.Z, halfway.Rotation.Z, 0.000001f);
        Assert.Equal(expected.W, halfway.Rotation.W, 0.000001f);

    }

    [Fact]
    public void Zero_duration_motion_completes_on_activation()
    {
        Transform start = new(new Vector3(1f, 0f, 0f), Quaternion.Identity, Vector3.One);
        DaggerfallDungeonActionDefinition action = Action("instant", Translate(-Vector3.UnitX, 0d));
        using EntityStore store = CreateStore(start, out EntityId target);
        DaggerfallDungeonMotionRuntime runtime = new(store, "dungeon/instant", [new(action, target)]);

        Assert.Equal(DaggerfallDungeonMotionActivation.CompletedImmediately, runtime.Activate(action.Id));
        Assert.Equal(DaggerfallDungeonMotionPhase.End, Assert.Single(runtime.Capture().Actions).Phase);
        Assert.Equal(new Vector3(0f, 0f, 0f), store.Get(target, EngineComponentTypes.Transform).Translation);
    }

    private static readonly float SourceTurn = 32f / 5.68888888888889f * (MathF.PI / 180f);

    private static DaggerfallDungeonActionDefinition Action(string id, DaggerfallDungeonMotionSpecification motion) => new(
        id, SourceOffset: 1, TriggerFlag: 2, ActionFlag: (byte)motion.Kind, Axis: 0, Duration: 0, Magnitude: 0, NextObjectOffset: 0, NextActionId: null)
    { Motion = motion };

    private static DaggerfallDungeonMotionSpecification Translate(Vector3 by, double seconds) =>
        new(DaggerfallDungeonMotionKind.Translation, by, Vector3.Zero, 0f, seconds);

    private static EntityStore CreateStore(Transform transform, out EntityId entity)
    {
        EntityStore store = new([EngineComponentTypes.Transform, EngineComponentTypes.SpatialCollider]);
        entity = store.Create(new EntityTypeId("daggerfall.dungeon-action-motion"));
        store.Set(entity, EngineComponentTypes.Transform, transform);
        store.Set(entity, EngineComponentTypes.SpatialCollider, new SpatialCollider(
            new Vector3(-1f, -.25f, -1f),
            new Vector3(1f, .25f, 1f),
            uint.MaxValue,
            uint.MaxValue,
            Enabled: true,
            StaticCollider: false,
            Trigger: false));
        return store;
    }
}
