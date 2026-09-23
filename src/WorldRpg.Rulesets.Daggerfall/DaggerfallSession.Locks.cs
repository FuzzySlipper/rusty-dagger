using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// One transient consequence notification from a player lock interaction.
/// Consumers such as crime and perception may subscribe without creating a
/// second persisted lock record or owning door state.
/// </summary>
internal readonly record struct DaggerfallLockIncident(
    DaggerfallRdbDoorId Door,
    DaggerfallLockInteractionKind Kind,
    DaggerfallLockInteractionSurface Surface,
    DaggerfallLockInteractionStatus Status,
    DaggerfallDoorOperationResult Operation,
    int Chance,
    int? Roll,
    int? FailedSkillLevel,
    int DoorDamage,
    bool EmitsNoise,
    bool ReportsBreakingAndEntering);

internal sealed partial class DaggerfallSession
{
    private const ulong LockpickRandomSeed = 0x4C4F434BUL;
    private const string LockpickRandomScope = "daggerfall.lockpick.v1";

    /// <summary>
    /// Transient legal/noise seam for the owning crime and perception tasks.
    /// The session remains the only publisher; no incident log is persisted.
    /// </summary>
    internal event Action<DaggerfallLockIncident>? LockIncident;

    private DaggerfallActivationOutcome ActivateDoorForce(DaggerfallRdbDoorId id, DaggerfallActivationMode mode) => mode switch
    {
        DaggerfallActivationMode.Steal => TryLockpickDoor(id),
        DaggerfallActivationMode.Bash => TryBashDoor(id),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private DaggerfallActivationOutcome TryLockpickDoor(DaggerfallRdbDoorId id)
    {
        DaggerfallDoorView door = _doors.Read(id);
        // Donor lockpicking reads the current modified skill for both chance and its retry gate.
        int skill = State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallSkills.Lockpicking)).ValueInt;
        int? previousFailedSkill = _doors.FailedLockpickingSkill(id);
        DaggerfallLockInteractionSurface surface = DaggerfallLockInteractionSurface.Interior;
        DaggerfallLockInteractionDecision decision = DaggerfallLockInteractionPolicy.EvaluateLockpickDeferred(
            door,
            surface,
            Math.Max(1, State.Progression.Level),
            skill,
            previousFailedSkill,
            () => DrawLockpickRoll(door, skill, previousFailedSkill));

        if (decision.RecordsSkillUse)
            _ = State.SkillUses.Record(new DaggerfallSkillUse(
                DaggerfallSkills.Lockpicking,
                DaggerfallSkillUseReason.LockpickingAttempt,
                DaggerfallSkillUseOutcome.Attempted));

        DaggerfallDoorOperationResult operation = _doors.ApplyLockInteraction(id, decision);
        PublishLockIncident(id, decision, operation);
        bool applied = decision.Applied && operation == DaggerfallDoorOperationResult.Started;
        return new(applied, LockpickMessage(decision.Status, applied));
    }

    private DaggerfallActivationOutcome TryBashDoor(DaggerfallRdbDoorId id)
    {
        DaggerfallDoorView before = _doors.Read(id);
        int strength = State.Actors.Player.Stats
            .GetStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value)).ValueInt;
        int toolForce = EquippedBashToolForce();
        DaggerfallDoorOperationResult operation = _doors.Bash(id, strength, toolForce, out DaggerfallLockInteractionDecision decision);
        PublishLockIncident(id, decision, operation);
        bool applied = operation == DaggerfallDoorOperationResult.Started;
        return new(applied, BashMessage(decision, operation, before.Motion));
    }

    private int EquippedBashToolForce()
    {
        EquipmentRead equipment = State.Equipment.Read();
        foreach (string slot in new[] { "right-hand", "left-hand" })
        {
            if (!equipment.TryGet(new KitEquipmentSlotId(slot), out KitUniqueInventoryItem item)
                || !_definitions.Items.TryGetValue(new DaggerfallItemId(item.Definition.Value), out DaggerfallItemDefinition? definition))
                continue;
            if (definition.Weapon is not { } weapon) continue;

            // The admitted weapon definition is the only product-owned tool
            // meaning available to this policy. Minimum damage is a stable
            // force contribution; the full weapon damage remains combat-owned.
            return Math.Max(0, weapon.MinimumDamage);
        }
        return 0;
    }

    private int DrawLockpickRoll(DaggerfallDoorView door, int skill, int? previousFailedSkill)
    {
        string previous = previousFailedSkill?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
        string key = $"{_activeProfileKey.LogicalId}:{door.Id}:{door.LockValue}:{skill}:{previous}";
        return checked((int)_random.DrawKeyed(new KeyedRngRequest(
            LockpickRandomSeed,
            LockpickRandomScope,
            key,
            1,
            100)).Value);
    }

    private void PublishLockIncident(
        DaggerfallRdbDoorId id,
        DaggerfallLockInteractionDecision decision,
        DaggerfallDoorOperationResult operation)
    {
        LockIncident?.Invoke(new(
            id,
            decision.Kind,
            decision.Surface,
            decision.Status,
            operation,
            decision.Chance,
            decision.Roll,
            decision.FailedSkillLevel,
            decision.DoorDamage,
            decision.EmitsNoise,
            decision.ReportsBreakingAndEntering));
    }

    private static string LockpickMessage(DaggerfallLockInteractionStatus status, bool applied) => status switch
    {
        DaggerfallLockInteractionStatus.Applied when applied => "The lock clicks and the door begins to open.",
        DaggerfallLockInteractionStatus.Failed => "The lock resists your pick.",
        DaggerfallLockInteractionStatus.AlreadyUnlocked => "The door is already unlocked.",
        DaggerfallLockInteractionStatus.DuplicateAttempt => "You have already tried that skill against this lock.",
        DaggerfallLockInteractionStatus.SpecialDoor => "This door only responds to its mechanism.",
        DaggerfallLockInteractionStatus.MagicallyHeld => "Magic holds the lock fast.",
        DaggerfallLockInteractionStatus.DoorMoving => "The door is moving.",
        _ => "The lock cannot be picked.",
    };

    private static string BashMessage(
        DaggerfallLockInteractionDecision decision,
        DaggerfallDoorOperationResult operation,
        DaggerfallDoorMotion before) => operation switch
        {
            DaggerfallDoorOperationResult.Started when decision.Mutation == DaggerfallDoorOperationKind.Close
                => "The door begins to close.",
            DaggerfallDoorOperationResult.Started => "The door begins to open.",
            DaggerfallDoorOperationResult.BashFailed => "The door resists your blow.",
            DaggerfallDoorOperationResult.MagicallyHeld => "Magic holds the door fast.",
            DaggerfallDoorOperationResult.SpecialDoor => "This door only responds to its mechanism.",
            DaggerfallDoorOperationResult.AlreadyOpen => "The door is already open.",
            DaggerfallDoorOperationResult.AlreadyClosed => "The door is already closed.",
            _ => before is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening
                ? "The door cannot be closed."
                : "The door cannot be moved.",
        };
}
