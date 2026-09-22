using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDoorRuntimeTests
{
    [Fact]
    public void Lock_open_conflicts_and_motion_keep_collision_and_pose_on_one_projection()
    {
        using EntityStore store = new();
        using DaggerfallDoorRuntime doors = new(store, Random(1), [Door(startingLock: 4)]);
        DaggerfallRdbDoorId id = Door().Id;

        Assert.Equal(DaggerfallDoorOperationResult.Locked, doors.Open(id, DaggerfallDoorOperationSource.Player));
        Assert.Equal(DaggerfallDoorOperationResult.Started, doors.Open(id, DaggerfallDoorOperationSource.DungeonAction));
        DaggerfallDoorView started = doors.Read(id);
        Assert.Equal(0, started.LockValue);
        Assert.Equal(DaggerfallDoorMotion.Opening, started.Motion);
        Assert.False(started.CollisionEnabled);
        Assert.Single(doors.CharacterEnvironment().Obstacles.Span.ToArray(), obstacle => !obstacle.CollisionEnabled);

        doors.Advance(.75d);
        DaggerfallDoorView moving = doors.Read(id);
        Assert.Equal(.5F, moving.Progress);
        Assert.NotEqual(started.Pose.Rotation, moving.Pose.Rotation);
        Assert.False(moving.CollisionEnabled);
        Assert.Equal(DaggerfallDoorOperationResult.AlreadyOpen, doors.Open(id, DaggerfallDoorOperationSource.Player));

        doors.Advance(.75d);
        Assert.Equal(DaggerfallDoorMotion.Open, doors.Read(id).Motion);
        Assert.Equal(DaggerfallDoorOperationResult.Started, doors.Close(id, DaggerfallDoorOperationSource.DungeonAction));
        Assert.Equal(4, doors.Read(id).LockValue);
        doors.Advance(1.5d);
        DaggerfallDoorView closed = doors.Read(id);
        Assert.Equal(DaggerfallDoorMotion.Closed, closed.Motion);
        Assert.True(closed.CollisionEnabled);
        Assert.True(doors.CharacterEnvironment().Obstacles.Span[0].CollisionEnabled);
    }

    [Fact]
    public void Reload_mid_motion_and_unload_after_bash_preserve_the_same_normalized_door_state()
    {
        DaggerfallRdbDoorId id = Door().Id;
        DaggerfallDoorSave[] saved;
        using (EntityStore originalStore = new())
        using (DaggerfallDoorRuntime original = new(originalStore, Random(1), [Door()]))
        {
            Assert.Equal(DaggerfallDoorOperationResult.Started, original.Open(id, DaggerfallDoorOperationSource.Player));
            original.Advance(.75d);
            saved = original.Capture();
        }

        using (EntityStore restoredStore = new())
        using (DaggerfallDoorRuntime restored = new(restoredStore, Random(1), [Door()], saved))
        {
            DaggerfallDoorView moving = restored.Read(id);
            Assert.Equal(DaggerfallDoorMotion.Opening, moving.Motion);
            Assert.Equal(.5F, moving.Progress);
            Assert.False(moving.CollisionEnabled);
            restored.Advance(.75d);
            Assert.Equal(DaggerfallDoorMotion.Open, restored.Read(id).Motion);
        }

        using (EntityStore bashedStore = new())
        using (DaggerfallDoorRuntime bashed = new(bashedStore, Random(1), [Door()]))
        {
            Assert.Equal(DaggerfallDoorOperationResult.Started, bashed.Bash(id));
            DaggerfallDoorSave afterBash = Assert.Single(bashed.Capture());
            Assert.Equal(DaggerfallDoorMotion.Opening, afterBash.Motion);
            Assert.Equal(0, afterBash.LockValue);
            Assert.Equal(1UL, afterBash.BashAttempts);
        }
    }

    [Fact]
    public void Special_doors_refuse_player_spell_and_bash_but_accept_linked_actions()
    {
        using EntityStore store = new();
        DaggerfallRdbDoorDefinition special = Door() with { Kind = DaggerfallDoorKind.Special };
        using DaggerfallDoorRuntime doors = new(store, Random(1), [special]);

        Assert.Equal(DaggerfallDoorOperationResult.SpecialDoor, doors.Open(special.Id, DaggerfallDoorOperationSource.Player));
        Assert.Equal(DaggerfallDoorOperationResult.SpecialDoor, doors.Open(special.Id, DaggerfallDoorOperationSource.Spell));
        Assert.Equal(DaggerfallDoorOperationResult.SpecialDoor, doors.Bash(special.Id));
        Assert.Equal(DaggerfallDoorOperationResult.Started, doors.Open(special.Id, DaggerfallDoorOperationSource.DungeonAction));
        doors.Advance(1.5d);
        Assert.Equal(DaggerfallDoorMotion.Open, doors.Read(special.Id).Motion);
        Assert.Equal(DaggerfallDoorOperationResult.Started, doors.Close(special.Id, DaggerfallDoorOperationSource.DungeonAction));
    }

    private static DaggerfallRdbDoorDefinition Door(int startingLock = 0) => new(
        new DaggerfallRdbDoorId("S0000007.RDB", 1, 1, 3), new Vector3(2, 0, -4), new Vector3(0, 90, 0),
        new Vector3(-.5F, 0, -.1F), new Vector3(.5F, 2, .1F), DaggerfallDoorKind.Normal, startingLock);

    private static IRandomService Random(int value)
    {
        IRandomService service = DispatchProxy.Create<IRandomService, RandomProxy>();
        ((RandomProxy)(object)service).Value = value;
        return service;
    }

    private class RandomProxy : DispatchProxy
    {
        internal int Value { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(Value)
            : throw new NotSupportedException(method?.Name);
    }
}
