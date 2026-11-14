using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Targeting;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The activation in force: the active site's <see cref="DaggerfallActivationModule"/> and its door owner, which
/// composition hands over with each site (so a new site starts in Grab, as before), the mode, a use of what the player
/// aims at with the dungeon's door warning first and the door's or the ray's Direct actions after, and a swing that
/// bashes a shut door. It returns what happened; reporting it is the session's.
/// </summary>
/// <param name="dungeonActions">The dungeon's actions: the door ray, the warning shown first, the Direct actions.</param>
/// <param name="doors">The active site's doors.</param>
/// <param name="useReach">How far use reaches.</param>
/// <param name="swingReach">How far a melee swing reaches.</param>
internal sealed class DaggerfallActivation(
    DaggerfallDungeonActions dungeonActions,
    ActorsState actors,
    PlayerControlState player,
    Func<DaggerfallDoorRuntime> doors,
    double useReach,
    double swingReach)
{
    private DaggerfallDoorActivationOwner? _door;

    /// <summary>The active site's module, null until composition hands one over.</summary>
    internal DaggerfallActivationModule? Module { get; private set; }

    internal DaggerfallActivationMode Mode => Module?.Mode ?? DaggerfallActivationMode.Grab;
    internal InteractionTargetingEvidence? LastEvidence => Module?.LastEvidence;

    /// <summary>Takes the module and door owner composed for the site now active.</summary>
    internal void Use(DaggerfallActivationModule module, DaggerfallDoorActivationOwner door)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        _door = door ?? throw new ArgumentNullException(nameof(door));
    }

    /// <summary>Changes the mode use acts in; false when it was already that mode.</summary>
    internal bool ChangeMode(DaggerfallActivationMode mode) =>
        (Module ?? throw new InvalidOperationException("No activation is composed.")).ChangeMode(mode);

    /// <summary>
    /// Uses what the player aims at: null with no module composed. A dungeon door's first-contact warning, shown, stops
    /// the use; otherwise the module acts and the door's Direct actions, or the action ray's, follow. The refusal is the
    /// module's reason when it acted.
    /// </summary>
    internal DaggerfallActivationOutcome? Activate(LookReceipt look, out string? refusal)
    {
        refusal = null;
        if (Module is not { } module) return null;
        dungeonActions.ClearPreflight();
        // Keep the door identity observed before activation: opening disables its collider, but
        // PlayerActivate invokes the ordinary door operation before its Direct action event.
        DaggerfallRdbDoorId? actionDoor = dungeonActions.FindDoor(look.Forward, useReach);
        bool directDoorActionsDispatched = false;
        if (actionDoor is { } gatedDoor
            && dungeonActions.PreflightDoorText(gatedDoor, out directDoorActionsDispatched))
        {
            dungeonActions.ClearPreflight();
            return new(true, "The door's warning stops you.");
        }
        DaggerfallActivationOutcome outcome;
        try
        {
            outcome = module.Activate(actors.Player.Actor.Entity, player, look);
            refusal = module.LastEvidence?.Use.Reason.ToString();
        }
        finally { dungeonActions.ClearPreflight(); }
        if (actionDoor is { } door && !directDoorActionsDispatched && dungeonActions.TriggerDoorDirect(door) is { } applied)
        {
            if (applied)
            {
                DaggerfallDoorView current = doors().Read(door);
                string message = current.Motion switch
                {
                    DaggerfallDoorMotion.Opening => "The door begins to open.",
                    DaggerfallDoorMotion.Closing => "The door begins to close.",
                    DaggerfallDoorMotion.Open => "The door is open.",
                    _ when current.IsLocked => "The door locks.",
                    _ => "The door unlocks.",
                };
                outcome = new(true, message);
            }
        }
        else if (!directDoorActionsDispatched)
            _ = dungeonActions.TriggerRay(look.Forward, useReach, DaggerfallDungeonActionEvent.Direct);
        return outcome;
    }

    /// <summary>
    /// As the donor's WeaponManager.WeaponEnvDamage, a swing that reaches no creature but strikes a closed door
    /// bashes it (DaggerfallActionDoor.AttemptBash, PlayerActivate.AttemptExteriorDoorBash), through the same door
    /// owner, crime and wording the Bash activation uses; no mode has to be chosen first. Null when it bashes nothing.
    /// </summary>
    /// <param name="creatureSelected">Whether the swing's targeting selected a creature.</param>
    internal DaggerfallActivationOutcome? BashWithSwing(LookReceipt look, bool creatureSelected)
    {
        if (_door is null || creatureSelected) return null;
        if (dungeonActions.FindDoor(look.Forward, swingReach) is not { } id) return null;
        DaggerfallDoorView door = doors().Read(id);
        if (door.Motion != DaggerfallDoorMotion.Closed) return null;
        foreach (DaggerfallActivationTarget target in _door.DoorTargets())
        {
            if (target.Entity != door.Entity) continue;
            return _door.ActivateDoor(new(DaggerfallActivationMode.Bash, target));
        }
        return null;
    }
}
