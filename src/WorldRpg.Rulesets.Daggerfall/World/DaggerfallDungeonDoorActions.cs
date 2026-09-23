namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>Interprets linked dungeon door flags through the one persistent door owner.</summary>
internal static class DaggerfallDungeonDoorActions
{
    internal static DaggerfallDungeonActionExecution? Execute(
        DaggerfallDungeonActionDefinition action, DaggerfallDoorRuntime doors)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(doors);
        DaggerfallDungeonActionFlag flag = (DaggerfallDungeonActionFlag)action.ActionFlag;
        if (flag is not (DaggerfallDungeonActionFlag.LockDoor or DaggerfallDungeonActionFlag.UnlockDoor
            or DaggerfallDungeonActionFlag.OpenDoor or DaggerfallDungeonActionFlag.CloseDoor))
            return null;

        DaggerfallDoorView[] matches = action.DoorId is null ? [] : doors.All
            .Where(door => StringComparer.Ordinal.Equals(DaggerfallDungeonActionGraph.DoorSourceId(door.Id), action.DoorId))
            .Take(2).ToArray();
        if (matches.Length != 1)
            return new(action.Id, DaggerfallDungeonActionOutcome.MissingTarget,
                Diagnostic: $"Dungeon action '{action.Id}' cannot resolve one loaded door for '{action.DoorId ?? "<none>"}'.");

        DaggerfallRdbDoorId id = matches[0].Id;
        DaggerfallDoorOperationResult result = flag switch
        {
            DaggerfallDungeonActionFlag.LockDoor => doors.Lock(id, DaggerfallDoorOperationSource.DungeonAction),
            DaggerfallDungeonActionFlag.UnlockDoor => doors.Unlock(id, DaggerfallDoorOperationSource.DungeonAction),
            DaggerfallDungeonActionFlag.OpenDoor => doors.Open(id, DaggerfallDoorOperationSource.DungeonAction),
            DaggerfallDungeonActionFlag.CloseDoor => doors.Close(id, DaggerfallDoorOperationSource.DungeonAction),
            _ => throw new InvalidOperationException("The action is not a door operation."),
        };
        return result switch
        {
            DaggerfallDoorOperationResult.Started => new(action.Id, DaggerfallDungeonActionOutcome.Applied),
            DaggerfallDoorOperationResult.AlreadyOpen or DaggerfallDoorOperationResult.AlreadyClosed
                or DaggerfallDoorOperationResult.AlreadyLocked or DaggerfallDoorOperationResult.AlreadyUnlocked
                => new(action.Id, DaggerfallDungeonActionOutcome.AppliedWithoutChange),
            _ => new(action.Id, DaggerfallDungeonActionOutcome.RejectedOperation,
                Diagnostic: $"Dungeon action '{action.Id}' could not {flag} door '{id}': {result}."),
        };
    }
}
