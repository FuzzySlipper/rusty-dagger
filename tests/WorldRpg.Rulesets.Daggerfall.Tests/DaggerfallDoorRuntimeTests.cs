using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDoorRuntimeTests
{
    [Fact]
    public void Linked_action_flags_mutate_the_persistent_door_owner_in_graph_order()
    {
        using EntityDirectory store = new();
        using DaggerfallDoorRuntime doors = new(store, Random(1), [Door(startingLock: 4)], "test");
        DaggerfallRdbDoorId id = Door().Id;
        string sourceId = DaggerfallDungeonActionGraph.DoorSourceId(id);
        DaggerfallDungeonActionDefinition[] actions =
        [
            Action("lock", 1, DaggerfallDungeonActionFlag.LockDoor, sourceId),
            Action("unlock", 2, DaggerfallDungeonActionFlag.UnlockDoor, sourceId),
            Action("open", 3, DaggerfallDungeonActionFlag.OpenDoor, sourceId),
            Action("close", 4, DaggerfallDungeonActionFlag.CloseDoor, sourceId),
        ];
        DaggerfallDungeonActionGraph graph = new("site", actions,
            new DaggerfallVariableStore(new Dictionary<string, int>()),
            executeFamilyAction: action => DaggerfallDungeonDoorActions.Execute(action, doors));

        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, graph.Trigger("unlock", DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);
        Assert.Equal(0, doors.Read(id).LockValue);
        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, graph.Trigger("lock", DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);
        Assert.Equal(16, doors.Read(id).LockValue);
        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, graph.Trigger("open", DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);
        Assert.Equal(DaggerfallDoorMotion.Opening, doors.Read(id).Motion);
        Assert.Equal(0, doors.Read(id).LockValue);
        doors.Advance(1.5d);
        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, graph.Trigger("close", DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);
        DaggerfallDoorSave saved = Assert.Single(doors.Capture());
        Assert.Equal(DaggerfallDoorMotion.Closing, saved.Motion);
        Assert.Equal(4, saved.LockValue);

        using EntityDirectory restoredStore = new();
        using DaggerfallDoorRuntime restoredDoors = new(restoredStore, Random(1), [Door(startingLock: 4)], "test", [saved]);
        Assert.Equal(saved, Assert.Single(restoredDoors.Capture()));
    }

    [Fact]
    public void Linked_door_action_rejects_missing_or_special_lock_target()
    {
        using EntityDirectory store = new();
        DaggerfallRdbDoorDefinition special = Door() with { Kind = DaggerfallDoorKind.Special };
        using DaggerfallDoorRuntime doors = new(store, Random(1), [special], "test");
        string sourceId = DaggerfallDungeonActionGraph.DoorSourceId(special.Id);
        DaggerfallDungeonActionExecution missing = DaggerfallDungeonDoorActions.Execute(
            Action("missing", 1, DaggerfallDungeonActionFlag.OpenDoor, "door/missing-rdb/0/0/0"), doors)!;
        DaggerfallDungeonActionExecution rejected = DaggerfallDungeonDoorActions.Execute(
            Action("lock", 2, DaggerfallDungeonActionFlag.LockDoor, sourceId), doors)!;
        Assert.Equal(DaggerfallDungeonActionOutcome.MissingTarget, missing.Outcome);
        Assert.Equal(DaggerfallDungeonActionOutcome.RejectedOperation, rejected.Outcome);
        Assert.Equal(0, doors.Read(special.Id).LockValue);
    }

    private static DaggerfallDungeonActionDefinition Action(string id, int offset, DaggerfallDungeonActionFlag flag, string doorId)
        => new(id, offset, (uint)DaggerfallDungeonTriggerFlag.Direct, (byte)flag, 0, 0, 0, 0, null, doorId);

    [Fact]
    public void Lock_open_conflicts_and_motion_keep_collision_and_pose_on_one_projection()
    {
        using EntityDirectory store = new();
        using DaggerfallDoorRuntime doors = new(store, Random(1), [Door(startingLock: 4)], "test");
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
        using (EntityDirectory originalStore = new())
        using (DaggerfallDoorRuntime original = new(originalStore, Random(1), [Door()], "test"))
        {
            Assert.Equal(DaggerfallDoorOperationResult.Started, original.Open(id, DaggerfallDoorOperationSource.Player));
            original.Advance(.75d);
            saved = original.Capture();
        }

        using (EntityDirectory restoredStore = new())
        using (DaggerfallDoorRuntime restored = new(restoredStore, Random(1), [Door()], "test", saved))
        {
            DaggerfallDoorView moving = restored.Read(id);
            Assert.Equal(DaggerfallDoorMotion.Opening, moving.Motion);
            Assert.Equal(.5F, moving.Progress);
            Assert.False(moving.CollisionEnabled);
            restored.Advance(.75d);
            Assert.Equal(DaggerfallDoorMotion.Open, restored.Read(id).Motion);
        }

        using (EntityDirectory bashedStore = new())
        using (DaggerfallDoorRuntime bashed = new(bashedStore, Random(1), [Door()], "test"))
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
        using EntityDirectory store = new();
        DaggerfallRdbDoorDefinition special = Door() with { Kind = DaggerfallDoorKind.Special };
        using DaggerfallDoorRuntime doors = new(store, Random(1), [special], "test");

        Assert.Equal(DaggerfallDoorOperationResult.SpecialDoor, doors.Open(special.Id, DaggerfallDoorOperationSource.Player));
        Assert.Equal(DaggerfallDoorOperationResult.SpecialDoor, doors.Open(special.Id, DaggerfallDoorOperationSource.Spell));
        Assert.Equal(DaggerfallDoorOperationResult.SpecialDoor, doors.Bash(special.Id));
        Assert.Equal(DaggerfallDoorOperationResult.Started, doors.Open(special.Id, DaggerfallDoorOperationSource.DungeonAction));
        doors.Advance(1.5d);
        Assert.Equal(DaggerfallDoorMotion.Open, doors.Read(special.Id).Motion);
        Assert.Equal(DaggerfallDoorOperationResult.Started, doors.Close(special.Id, DaggerfallDoorOperationSource.DungeonAction));
    }

    [Fact]
    public void Lockpick_mutation_retains_failed_skill_until_a_later_success_and_save_reload()
    {
        DaggerfallRdbDoorId id = Door(startingLock: 4).Id;
        DaggerfallLockInteractionDecision failed = DaggerfallLockInteractionPolicy.EvaluateLockpick(
            RuntimeView(Door(startingLock: 4), lockValue: 4),
            DaggerfallLockInteractionSurface.Interior,
            playerLevel: 5,
            lockpickingSkill: 30,
            previousFailedSkill: null,
            roll: 36);

        using EntityDirectory store = new();
        using DaggerfallDoorRuntime doors = new(store, Random(1), [Door(startingLock: 4)], "test");
        Assert.Equal(DaggerfallDoorOperationResult.LockpickFailed, doors.ApplyLockInteraction(id, failed));
        Assert.Equal(30, doors.FailedLockpickingSkill(id));
        DaggerfallDoorSave saved = Assert.Single(doors.Capture());
        Assert.Equal(30, saved.FailedLockpickingSkill);

        DaggerfallDoorView current = doors.Read(id);
        DaggerfallLockInteractionDecision success = DaggerfallLockInteractionPolicy.EvaluateLockpick(
            current,
            DaggerfallLockInteractionSurface.Interior,
            playerLevel: 5,
            lockpickingSkill: 31,
            previousFailedSkill: doors.FailedLockpickingSkill(id),
            roll: 36);
        Assert.Equal(DaggerfallDoorOperationResult.Started, doors.ApplyLockInteraction(id, success));
        Assert.Equal(0, doors.Read(id).LockValue);
        Assert.Null(doors.FailedLockpickingSkill(id));
        Assert.Equal(DaggerfallDoorMotion.Opening, doors.Read(id).Motion);

        using EntityDirectory restoredStore = new();
        using DaggerfallDoorRuntime restored = new(restoredStore, Random(1), [Door(startingLock: 4)], "test", [saved]);
        Assert.Equal(30, restored.FailedLockpickingSkill(id));
    }

    private static DaggerfallRdbDoorDefinition Door(int startingLock = 0) => new(
        new DaggerfallRdbDoorId("S0000007.RDB", 1, 1, 3), new Vector3(2, 0, -4), new Vector3(0, 90, 0),
        new Vector3(-.5F, 0, -.1F), new Vector3(.5F, 2, .1F), DaggerfallDoorKind.Normal, startingLock);

    private static DaggerfallDoorView RuntimeView(DaggerfallRdbDoorDefinition definition, int lockValue) => new(
        definition.Id,
        new EntityId(1),
        new Transform(definition.Position, Quaternion.Identity, Vector3.One),
        DaggerfallDoorMotion.Closed,
        0F,
        lockValue,
        true,
        definition.Kind);

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
