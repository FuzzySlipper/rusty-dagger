using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Activation contribution for the same persistent RDB owner used by semantic actions.</summary>
internal sealed class DaggerfallDoorActivationOwner : IDaggerfallDoorActivationOwner
{
    private readonly DaggerfallDoorRuntime _doors;
    private readonly IReadOnlyDictionary<DaggerfallRdbDoorId, DurableIdentityReference> _identities;
    private readonly Func<DaggerfallRdbDoorId, DaggerfallBuildingDoor?> _buildingDoor;
    private readonly Func<DaggerfallRdbDoorId, string?> _buildingName;
    private readonly Func<DaggerfallDoorView, string> _lockDifficulty;
    private readonly Action<DaggerfallRdbDoorId> _discoverBuilding;

    internal DaggerfallDoorActivationOwner(
        DaggerfallDoorRuntime doors,
        Action<DaggerfallRdbDoorId> triggerDungeonActions,
        Func<DaggerfallRdbDoorId, DaggerfallActivationMode, DaggerfallActivationOutcome> activateForce,
        Func<DaggerfallRdbDoorId, DaggerfallActivationOutcome?> activateMagic,
        Func<DaggerfallRdbDoorId, DaggerfallActivationMode?, DaggerfallActivationOutcome?> enterExteriorBuilding,
        Func<DaggerfallRdbDoorId, DaggerfallBuildingDoor?> buildingDoor,
        Func<DaggerfallRdbDoorId, string?> buildingName,
        Func<DaggerfallDoorView, string> lockDifficulty,
        Action<DaggerfallRdbDoorId> discoverBuilding)
    {
        _lockDifficulty = lockDifficulty;
        _discoverBuilding = discoverBuilding;
        _buildingDoor = buildingDoor ?? throw new ArgumentNullException(nameof(buildingDoor));
        _buildingName = buildingName ?? throw new ArgumentNullException(nameof(buildingName));
        _doors = doors ?? throw new ArgumentNullException(nameof(doors));
        _triggerDungeonActions = triggerDungeonActions ?? throw new ArgumentNullException(nameof(triggerDungeonActions));
        _activateForce = activateForce ?? throw new ArgumentNullException(nameof(activateForce));
        _activateMagic = activateMagic ?? throw new ArgumentNullException(nameof(activateMagic));
        _enterExteriorBuilding = enterExteriorBuilding ?? throw new ArgumentNullException(nameof(enterExteriorBuilding));
        Dictionary<DaggerfallRdbDoorId, DurableIdentityReference> identities = [];
        HashSet<DurableIdentityReference> assigned = [];
        foreach (DaggerfallDoorView door in _doors.All)
        {
            DurableIdentityReference identity = _doors.IdentityOf(door.Id);
            if (!assigned.Add(identity))
                throw new InvalidOperationException($"RDB door identity hash collision for '{door.Id}'.");
            identities.Add(door.Id, identity);
        }
        _identities = identities;
    }

    private readonly Action<DaggerfallRdbDoorId> _triggerDungeonActions;
    private readonly Func<DaggerfallRdbDoorId, DaggerfallActivationMode, DaggerfallActivationOutcome> _activateForce;
    private readonly Func<DaggerfallRdbDoorId, DaggerfallActivationOutcome?> _activateMagic;
    private readonly Func<DaggerfallRdbDoorId, DaggerfallActivationMode?, DaggerfallActivationOutcome?> _enterExteriorBuilding;

    public IEnumerable<DaggerfallActivationTarget> DoorTargets(DaggerfallActivationReach? reach = null)
    {
        foreach (DaggerfallDoorView door in reach is { } within ? _doors.Near(within.From, within.Distance) : _doors.All)
        {
            yield return new(
                DaggerfallActivationTargetKind.Door,
                _identities[door.Id],
                door.Entity,
                door.Entity.Value,
                new WorldPoint(door.Pose.Translation.X, door.Pose.Translation.Y, door.Pose.Translation.Z),
                Precedence: 3,
                Label: Label(door.Id));
        }
    }

    /// <summary>
    /// What ActivateDoor would do now, read without doing it: examine in Information mode, nothing in
    /// Talk, pick a locked lock in Steal (an unlocked building is entered, an unlocked dungeon door is
    /// left as it is), bash in Bash, and otherwise enter a building's entrance or open a shut dungeon
    /// door and close an open one.
    /// </summary>
    public string? PromptAction(DaggerfallActivationSelection selection)
    {
        DaggerfallRdbDoorId id = _identities.Single(pair => pair.Value == selection.Target.Identity).Key;
        DaggerfallDoorView door = _doors.Read(id);
        bool building = _doors.ExteriorBuildingOf(id) is not null;
        bool open = door.Motion is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening;
        // A building's entrance takes its lock from the building's entry rule when used (a shop
        // locked after hours), so the prompt reads that rule rather than the lock the door last had.
        bool locked = _buildingDoor(id)?.Entry.Unlocked is bool unlocked ? !unlocked : door.IsLocked;
        return selection.Mode switch
        {
            DaggerfallActivationMode.Info => "Examine",
            DaggerfallActivationMode.Talk => null,
            DaggerfallActivationMode.Steal when locked => "Pick the lock of",
            // An unlocked building's entrance lets a thief straight in; an unlocked dungeon door has
            // no lock to pick and Steal neither opens nor closes it, so nothing is promised.
            DaggerfallActivationMode.Steal => building ? "Enter" : null,
            DaggerfallActivationMode.Bash => "Bash",
            // Ordinary use reports a lock rather than passing it, so a locked door promises nothing.
            _ when locked => null,
            _ when building => "Enter",
            _ => open ? "Close" : "Open",
        };
    }

    public DaggerfallActivationOutcome ActivateDoor(DaggerfallActivationSelection selection)
    {
        DaggerfallRdbDoorId id = _identities.Single(pair => pair.Value == selection.Target.Identity).Key;
        // A building's entrance is as its building's entry rule says at this moment (PlayerActivate
        // judges BuildingIsUnlocked on every click), not as an earlier visit left it.
        DaggerfallBuildingDoor? building = _buildingDoor(id);
        if (building is not null)
        {
            _discoverBuilding(id);
            _doors.ApplyBuildingEntry(id, building.Entry.LockValue, !building.Entry.Unlocked);
        }
        DaggerfallDoorView door = _doors.Read(id);
        if (selection.Mode == DaggerfallActivationMode.Info)
        {
            string description = building is { Name.Length: > 0 } named ? DescribeBuilding(named) : Describe(door);
            if (door.IsLocked) description += " " + _lockDifficulty(door);
            if (building?.Entry.Unlocked == true) _ = _enterExteriorBuilding(id, null);
            return new(true, description);
        }
        if (selection.Mode == DaggerfallActivationMode.Talk)
            return new(false, "The door does not answer.");
        if (_activateMagic(id) is { } magic)
        {
            if (magic.Applied) _triggerDungeonActions(id);
            if (CanEnter(id) && _enterExteriorBuilding(id, door.IsLocked ? DaggerfallActivationMode.Steal : null) is { } entered) return entered;
            return magic;
        }
        if (selection.Mode is DaggerfallActivationMode.Steal or DaggerfallActivationMode.Bash)
        {
            DaggerfallActivationOutcome forced = _activateForce(id, selection.Mode);
            if (forced.Applied) _triggerDungeonActions(id);
            if ((forced.Applied || selection.Mode == DaggerfallActivationMode.Steal && !door.IsLocked)
                && _doors.ExteriorBuildingOf(id) is not null)
            {
                if (!CanEnter(id)) _ = _doors.Open(id, DaggerfallDoorOperationSource.Player);
                if (CanEnter(id) && _enterExteriorBuilding(id, forced.Applied ? selection.Mode : null) is { } entered) return entered;
            }
            return forced;
        }

        if (_doors.ExteriorBuildingOf(id) is not null)
        {
            DaggerfallDoorOperationResult opened = _doors.Open(id, DaggerfallDoorOperationSource.Player);
            if (opened == DaggerfallDoorOperationResult.Started) _triggerDungeonActions(id);
            if (CanEnter(id) && _enterExteriorBuilding(id, null) is { } entered) return entered;
            return new(false, opened == DaggerfallDoorOperationResult.Locked && building is not null
                ? building.LockedMessage + " " + _lockDifficulty(door) : Message(opened, door.Motion));
        }

        DaggerfallDoorOperationResult result = door.Motion is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening
            ? _doors.Close(id, DaggerfallDoorOperationSource.Player)
            : _doors.Open(id, DaggerfallDoorOperationSource.Player);
        if (result == DaggerfallDoorOperationResult.Started)
            _triggerDungeonActions(id);
        return new(result == DaggerfallDoorOperationResult.Started, Message(result, door.Motion));
    }

    private bool CanEnter(DaggerfallRdbDoorId id)
    {
        DaggerfallDoorView door = _doors.Read(id);
        return !door.IsLocked && door.Motion is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening;
    }

    private string? Label(DaggerfallRdbDoorId id)
    {
        if (_doors.ExteriorBuildingOf(id) is null) return null;
        return _buildingName(id);
    }

    // ActivateBuilding's Information mode: the building's name, and its hours when a store or guild is shut.
    private static string DescribeBuilding(DaggerfallBuildingDoor building) =>
        building.ClosedNotice is { } closed ? $"{building.Name}. {closed}" : building.Name;

    private static string Describe(DaggerfallDoorView door) => door.Kind == DaggerfallDoorKind.Special
        ? "You see a sealed mechanism."
        : door.IsMagicallyHeld ? "You see a door held by magic."
        : door.IsLocked ? "You see a locked door."
        : door.Motion is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening ? "You see an open door."
        : "You see a closed door.";

    private static string Message(DaggerfallDoorOperationResult result, DaggerfallDoorMotion before) => result switch
    {
        DaggerfallDoorOperationResult.Started when before is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening => "The door begins to close.",
        DaggerfallDoorOperationResult.Started => "The door begins to open.",
        DaggerfallDoorOperationResult.AlreadyOpen => "The door is already open.",
        DaggerfallDoorOperationResult.AlreadyClosed => "The door is already closed.",
        DaggerfallDoorOperationResult.Locked => "The door is locked.",
        DaggerfallDoorOperationResult.MagicallyHeld => "Magic holds the door fast.",
        DaggerfallDoorOperationResult.SpecialDoor => "This door only responds to its mechanism.",
        DaggerfallDoorOperationResult.BashFailed => "The door resists your blow.",
        DaggerfallDoorOperationResult.LockpickFailed => "The lock resists your pick.",
        DaggerfallDoorOperationResult.AlreadyLocked => "The door is already locked.",
        DaggerfallDoorOperationResult.AlreadyUnlocked => "The door is already unlocked.",
        _ => "The door cannot be moved.",
    };

}
