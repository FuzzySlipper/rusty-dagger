using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonMotionRuntimeTests
{
    [Theory]
    [InlineData(1, 2, 40, -1f, 0f, 0f)]
    [InlineData(1, 1, 40, 1f, 0f, 0f)]
    [InlineData(1, 4, 40, 0f, 1f, 0f)]
    [InlineData(1, 3, 40, 0f, -1f, 0f)]
    [InlineData(1, 6, 40, 0f, 0f, -1f)]
    [InlineData(1, 5, 40, 0f, 0f, 1f)]
    public void Translation_uses_source_axis_directions_units_and_ticks(
        byte actionFlag,
        byte axis,
        ushort magnitude,
        float x,
        float y,
        float z)
    {
        DaggerfallDungeonActionDefinition action = Action("translation", actionFlag, axis, duration: 37, magnitude: magnitude);

        Assert.True(DaggerfallDungeonMotionPolicy.TryInterpret(action, null, out DaggerfallDungeonMotionSpecification motion));
        Assert.Equal(DaggerfallDungeonMotionKind.Translation, motion.Kind);
        Assert.Equal(new Vector3(x, y, z), motion.Translation);
        Assert.Equal(37d / 20d, motion.DurationSeconds);
    }

    [Theory]
    [InlineData(2, -1f, 0f, 0f)]
    [InlineData(3, 1f, 0f, 0f)]
    [InlineData(4, 0f, 1f, 0f)]
    [InlineData(5, 0f, -1f, 0f)]
    [InlineData(6, 0f, 0f, -1f)]
    [InlineData(7, 0f, 0f, 1f)]
    public void Direct_axis_variants_use_axis_raw_times_eight_and_fifty_source_ticks(
        byte actionFlag,
        float x,
        float y,
        float z)
    {
        DaggerfallDungeonActionDefinition action = Action("direct-axis", actionFlag, axis: 5, duration: 1, magnitude: 999);

        Assert.True(DaggerfallDungeonMotionPolicy.TryInterpret(action, null, out DaggerfallDungeonMotionSpecification motion));
        Assert.Equal(new Vector3(x, y, z), motion.Translation);
        Assert.Equal(2.5d, motion.DurationSeconds);
    }

    [Fact]
    public void Rotation_uses_signed_source_axis_divisor_and_optional_TRP_fix()
    {
        DaggerfallDungeonActionDefinition ordinary = Action("rotation", 0x08, axis: 1, duration: 30, magnitude: 32);
        Assert.True(DaggerfallDungeonMotionPolicy.TryInterpret(ordinary, null, out DaggerfallDungeonMotionSpecification motion));
        Assert.Equal(DaggerfallDungeonMotionKind.Rotation, motion.Kind);
        Assert.Equal(-Vector3.UnitX, motion.RotationAxis);
        Assert.Equal(32f / 5.68888888888889f * (MathF.PI / 180f), motion.RotationRadians, 0.000001f);
        Assert.Equal(1.5d, motion.DurationSeconds);

        DaggerfallDungeonActionDefinition trp = Action("trp-rotation", 0x08, axis: 13, duration: 30, magnitude: 392);
        Assert.True(DaggerfallDungeonMotionPolicy.TryInterpret(trp, "TRP", out DaggerfallDungeonMotionSpecification corrected));
        Assert.Equal(-Vector3.UnitX, corrected.RotationAxis);
        Assert.Equal(400f / 5.68888888888889f * (MathF.PI / 180f), corrected.RotationRadians, 0.000001f);

        Assert.True(DaggerfallDungeonMotionPolicy.TryInterpret(trp, null, out DaggerfallDungeonMotionSpecification unrecognized));
        Assert.Equal(Vector3.Zero, unrecognized.RotationAxis);
        Assert.Equal(0f, unrecognized.RotationRadians);
    }

    [Fact]
    public void Translation_toggles_at_endpoints_ignores_retriggers_while_playing_and_restores_mid_motion()
    {
        Transform start = new(new Vector3(5f, 2f, -3f), Quaternion.Identity, Vector3.One);
        DaggerfallDungeonActionDefinition action = Action("platform", 0x01, axis: 2, duration: 40, magnitude: 40);
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
        DaggerfallDungeonActionDefinition action = Action("turning-platform", 0x08, axis: 2, duration: 20, magnitude: 32);
        using EntityStore store = CreateStore(start, out EntityId target);
        DaggerfallDungeonMotionRuntime runtime = new(store, "dungeon/turn", [new(action, target)]);

        Assert.Equal(DaggerfallDungeonMotionActivation.StartedForward, runtime.Activate(action.Id));
        runtime.Advance(.5d);
        Transform halfway = store.Get(target, EngineComponentTypes.Transform);
        float halfAngle = (32f / 5.68888888888889f) * (MathF.PI / 180f) * .5f;
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
        DaggerfallDungeonActionDefinition action = Action("instant", 0x01, axis: 2, duration: 0, magnitude: 40);
        using EntityStore store = CreateStore(start, out EntityId target);
        DaggerfallDungeonMotionRuntime runtime = new(store, "dungeon/instant", [new(action, target)]);

        Assert.Equal(DaggerfallDungeonMotionActivation.CompletedImmediately, runtime.Activate(action.Id));
        Assert.Equal(DaggerfallDungeonMotionPhase.End, Assert.Single(runtime.Capture().Actions).Phase);
        Assert.Equal(new Vector3(0f, 0f, 0f), store.Get(target, EngineComponentTypes.Transform).Translation);
    }

    [Fact]
    public void Non_motion_flags_are_left_for_their_named_action_family()
    {
        Assert.False(DaggerfallDungeonMotionPolicy.TryInterpret(Action("text", 0x0B, axis: 1), null, out _));
    }

    private static DaggerfallDungeonActionDefinition Action(
        string id,
        byte actionFlag,
        byte axis,
        ushort duration = 20,
        ushort magnitude = 40) => new(
            id,
            SourceOffset: 1,
            TriggerFlag: 2,
            ActionFlag: actionFlag,
            Axis: axis,
            Duration: duration,
            Magnitude: magnitude,
            NextObjectOffset: 0,
            NextActionId: null);

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
