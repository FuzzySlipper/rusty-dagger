using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
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

/// <summary>One pick or bash on a door's lock: what the lock policy decided, what the door did, and its words.</summary>
internal sealed record DaggerfallLockAttempt(
    DaggerfallRdbDoorId Door,
    DaggerfallLockInteractionDecision Decision,
    DaggerfallDoorOperationResult Operation,
    DaggerfallActivationOutcome Outcome);

/// <summary>
/// The player picking or bashing a door's lock: the chance and its words, the keyed pick roll, the force of the wielded
/// weapon, the skill use, a building's remembered failed pick, and the outcome's message. What an attempt means to the
/// law is Society's; the session's <c>ForceLock</c> list hands it the attempt before <see cref="Publish"/> tells
/// <see cref="Incident"/>'s subscribers.
/// </summary>
/// <param name="actors">The player's stats and level.</param>
/// <param name="equipment">The player's equipment, whose weapon lends a bash its force.</param>
/// <param name="site">The site context, which remembers a building's failed pick across visits.</param>
internal sealed class DaggerfallLocks(
    ActorsState actors,
    DaggerfallSkillUseReactions skillUses,
    MechanicsEquipmentCoordinator equipment,
    DaggerfallSiteContext site,
    DaggerfallDefinitions definitions,
    IRandomService random)
{
    private const ulong LockpickRandomSeed = 0x4C4F434BUL;
    private const string LockpickRandomScope = "daggerfall.lockpick.v1";

    /// <summary>
    /// Transient legal/noise seam for the owning crime and perception tasks.
    /// This owner is the only publisher; no incident log is persisted.
    /// </summary>
    internal event Action<DaggerfallLockIncident>? Incident;

    /// <summary>Picks (<see cref="DaggerfallActivationMode.Steal"/>) or bashes one door of the active site's projection.</summary>
    internal DaggerfallLockAttempt Force(DaggerfallDoorRuntime doors, DaggerfallRdbDoorId id, DaggerfallActivationMode mode,
        DaggerfallWorldProfileKey profile) => mode switch
    {
        DaggerfallActivationMode.Steal => Pick(doors, id, profile),
        DaggerfallActivationMode.Bash => Bash(doors, id),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Tells <see cref="Incident"/>'s subscribers what an attempt did.</summary>
    internal void Publish(DaggerfallLockAttempt attempt)
    {
        DaggerfallLockInteractionDecision decision = attempt.Decision;
        Incident?.Invoke(new(
            attempt.Door,
            decision.Kind,
            decision.Surface,
            decision.Status,
            attempt.Operation,
            decision.Chance,
            decision.Roll,
            decision.FailedSkillLevel,
            decision.DoorDamage,
            decision.EmitsNoise,
            decision.ReportsBreakingAndEntering));
    }

    /// <summary>How hard a locked door's lock looks to the player's Lockpicking, in the donor's words.</summary>
    internal string DescribeDifficulty(DaggerfallDoorView door)
    {
        if (door.IsMagicallyHeld) return InternalText("magicLock");
        int skill = actors.Player.Stats.GetStat(StatId.Parse(DaggerfallSkills.Lockpicking)).ValueInt;
        int chance = Policies.DaggerfallFormulaPolicy.CalculateInteriorLockpickingChance(Math.Max(1, actors.Player.Progression.Level), door.LockValue, skill);
        if (chance < 30) return InternalText("lockpickChance1");
        if (chance < 35) return InternalText("lockpickChance2");
        if (chance < 45) return InternalText("lockpickChance3");
        return definitions.Text.RequireInternalEntry("lockpickChance", chance >= 95 ? 9 : (chance - 45) / 5);
    }

    private DaggerfallLockAttempt Pick(DaggerfallDoorRuntime doors, DaggerfallRdbDoorId id, DaggerfallWorldProfileKey profile)
    {
        DaggerfallDoorView door = doors.Read(id);
        // Donor lockpicking reads the current modified skill for both chance and its retry gate.
        int skill = actors.Player.Stats.GetStat(StatId.Parse(DaggerfallSkills.Lockpicking)).ValueInt;
        var building = doors.ExteriorBuildingOf(id);
        int? previousFailedSkill = building is { } placed ? site.FailedBuildingPick(profile.Site, placed) : doors.FailedLockpickingSkill(id);
        DaggerfallLockInteractionSurface surface = doors.InteractionSurface(id);
        DaggerfallLockInteractionDecision decision = DaggerfallLockInteractionPolicy.EvaluateLockpickDeferred(
            door,
            surface,
            Math.Max(1, actors.Player.Progression.Level),
            skill,
            previousFailedSkill,
            () => DrawLockpickRoll(profile, door, skill, previousFailedSkill));

        if (decision.RecordsSkillUse)
            _ = skillUses.Record(new DaggerfallSkillUse(
                DaggerfallSkills.Lockpicking,
                DaggerfallSkillUseReason.LockpickingAttempt,
                DaggerfallSkillUseOutcome.Attempted));

        if (building is { } failedBuilding && decision.Status == DaggerfallLockInteractionStatus.Failed)
            site.RememberBuildingPick(profile.Site, failedBuilding, skill);
        DaggerfallDoorOperationResult operation = doors.ApplyLockInteraction(id, decision);
        bool applied = decision.Applied && operation == DaggerfallDoorOperationResult.Started;
        return new(id, decision, operation, new(applied, LockpickMessage(decision.Status, applied)));
    }

    private DaggerfallLockAttempt Bash(DaggerfallDoorRuntime doors, DaggerfallRdbDoorId id)
    {
        DaggerfallDoorView before = doors.Read(id);
        int strength = actors.Player.Stats
            .GetStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value)).ValueInt;
        int toolForce = EquippedBashToolForce();
        DaggerfallDoorOperationResult operation = doors.Bash(id, strength, toolForce, out DaggerfallLockInteractionDecision decision);
        bool applied = operation == DaggerfallDoorOperationResult.Started;
        return new(id, decision, operation, new(applied, BashMessage(decision, operation, before.Motion)));
    }

    private int EquippedBashToolForce()
    {
        EquipmentRead worn = equipment.Read();
        foreach (string slot in new[] { "right-hand", "left-hand" })
        {
            if (!worn.TryGet(new KitEquipmentSlotId(slot), out KitUniqueInventoryItem item)
                || !definitions.Items.TryGetValue(new DaggerfallItemId(item.Definition.Value), out DaggerfallItemDefinition? definition))
                continue;
            if (definition.Weapon is not { } weapon) continue;

            // The admitted weapon definition is the only product-owned tool
            // meaning available to this policy. Minimum damage is a stable
            // force contribution; the full weapon damage remains combat-owned.
            return Math.Max(0, weapon.MinimumDamage);
        }
        return 0;
    }

    private int DrawLockpickRoll(DaggerfallWorldProfileKey profile, DaggerfallDoorView door, int skill, int? previousFailedSkill)
    {
        string previous = previousFailedSkill?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
        string key = $"{profile.LogicalId}:{door.Id}:{door.LockValue}:{skill}:{previous}";
        return checked((int)random.DrawKeyed(new KeyedRngRequest(
            LockpickRandomSeed,
            LockpickRandomScope,
            key,
            1,
            100)).Value);
    }

    private string InternalText(string key) =>
        string.Concat(definitions.Text.Require(new(DaggerfallTextKind.Internal, key)).TextRuns);

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
