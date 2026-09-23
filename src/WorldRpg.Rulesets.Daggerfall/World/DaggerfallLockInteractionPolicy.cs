using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>The two source surfaces that retain separate classic lockpicking formulas.</summary>
internal enum DaggerfallLockInteractionSurface
{
    Interior,
    Exterior,
}

/// <summary>One operation admitted or refused by the canonical door state.</summary>
internal enum DaggerfallLockInteractionKind
{
    Lockpick,
    Bash,
}

/// <summary>Explicit result categories for UI, skill attribution, crime and persistence callers.</summary>
internal enum DaggerfallLockInteractionStatus
{
    Applied,
    Failed,
    AlreadyUnlocked,
    AlreadyOpen,
    DuplicateAttempt,
    SpecialDoor,
    MagicallyHeld,
    DoorMoving,
}

/// <summary>
/// The single result of a lock or bash decision. The policy does not mutate a
/// door or draw randomness; the caller owns both through the admitted session
/// update and applies <see cref="Mutation"/> to <see cref="DaggerfallDoorRuntime"/>.
/// </summary>
internal readonly record struct DaggerfallLockInteractionDecision(
    DaggerfallLockInteractionKind Kind,
    DaggerfallLockInteractionSurface Surface,
    DaggerfallLockInteractionStatus Status,
    DaggerfallDoorOperationKind? Mutation,
    int Chance,
    int? Roll,
    int? FailedSkillLevel,
    bool RecordsSkillUse,
    bool EmitsNoise,
    int DoorDamage,
    bool ReportsBreakingAndEntering)
{
    internal bool Applied => Status == DaggerfallLockInteractionStatus.Applied;
    /// <summary>Successful lockpicking follows the donor by opening after the lock is cleared.</summary>
    internal bool OpensAfterUnlock => Applied && Kind == DaggerfallLockInteractionKind.Lockpick;
    /// <summary>Successful bashing clears a locked door before invoking its open transition.</summary>
    internal bool ClearsLockBeforeOpen => Applied && Kind == DaggerfallLockInteractionKind.Bash
        && Mutation == DaggerfallDoorOperationKind.Open;
    internal bool ConsumedAttempt => RecordsSkillUse
        || Kind == DaggerfallLockInteractionKind.Bash
            && Status is DaggerfallLockInteractionStatus.Applied or DaggerfallLockInteractionStatus.Failed;
}

/// <summary>Deterministic Daggerfall lockpicking and door-bash policy over the live door projection.</summary>
/// <remarks>
/// PlayerActivate keeps an interior failed attempt at the current skill level,
/// while its exterior building path rejects every skill value at or below the
/// last failed value. This class preserves that distinction and returns the
/// state transition for the session to apply. It never creates a second lock
/// record or bypasses the door runtime.
/// </remarks>
internal static class DaggerfallLockInteractionPolicy
{
    private const int MaximumSkillValue = 100;
    private const int InteriorBashBaseChance = 20;
    private const int ExteriorBashBaseChance = 25;

    /// <summary>
    /// Calculates the donor-shaped bash chance and carries the product's live
    /// strength and equipped-tool contribution into the decision. At the
    /// neutral strength/tool values, this is the exact donor interior
    /// <c>20 - lock</c> or exterior <c>25 - lock</c> chance.
    /// </summary>
    internal static int CalculateBashChance(
        DaggerfallLockInteractionSurface surface,
        int lockValue,
        int strength,
        int toolForce)
    {
        ValidateSurface(surface);
        ValidateLockValue(lockValue);
        ValidateAttribute(strength, nameof(strength));
        ArgumentOutOfRangeException.ThrowIfNegative(toolForce);

        int baseChance = surface == DaggerfallLockInteractionSurface.Exterior
            ? ExteriorBashBaseChance
            : InteriorBashBaseChance;
        return Math.Clamp(checked(baseChance - lockValue + DaggerfallFormulaPolicy.DamageModifier(strength) + toolForce), 0, 100);
    }

    /// <summary>
    /// Evaluates a lockpick attempt against a snapshot of the one canonical
    /// door. A roll is required only when the state admits an attempt; callers
    /// can therefore inspect the rejection without consuming RNG.
    /// </summary>
    internal static DaggerfallLockInteractionDecision EvaluateLockpick(
        DaggerfallDoorView door,
        DaggerfallLockInteractionSurface surface,
        int playerLevel,
        int lockpickingSkill,
        int? previousFailedSkill,
        int? roll)
        => EvaluateLockpickCore(door, surface, playerLevel, lockpickingSkill, previousFailedSkill, () => RequireRoll(roll));

    /// <summary>
    /// Evaluates a lockpick attempt while deferring the admitted roll to the
    /// caller. Rejected state therefore consumes no random result.
    /// </summary>
    internal static DaggerfallLockInteractionDecision EvaluateLockpickDeferred(
        DaggerfallDoorView door,
        DaggerfallLockInteractionSurface surface,
        int playerLevel,
        int lockpickingSkill,
        int? previousFailedSkill,
        Func<int> drawRoll)
    {
        ArgumentNullException.ThrowIfNull(drawRoll);
        return EvaluateLockpickCore(door, surface, playerLevel, lockpickingSkill, previousFailedSkill, drawRoll);
    }

    private static DaggerfallLockInteractionDecision EvaluateLockpickCore(
        DaggerfallDoorView door,
        DaggerfallLockInteractionSurface surface,
        int playerLevel,
        int lockpickingSkill,
        int? previousFailedSkill,
        Func<int> drawRoll)
    {
        ValidateSurface(surface);
        ArgumentOutOfRangeException.ThrowIfLessThan(playerLevel, 1);
        ValidateAttribute(lockpickingSkill, nameof(lockpickingSkill));
        ValidateLockValue(door.LockValue);
        if (previousFailedSkill is < 0 or > MaximumSkillValue)
            throw new ArgumentOutOfRangeException(nameof(previousFailedSkill));

        if (door.Kind == DaggerfallDoorKind.Special)
            return RejectDoorState(door, DaggerfallLockInteractionKind.Lockpick, surface, DaggerfallLockInteractionStatus.SpecialDoor, null);
        if (door.Motion is DaggerfallDoorMotion.Open or DaggerfallDoorMotion.Opening or DaggerfallDoorMotion.Closing)
            return RejectDoorState(door, DaggerfallLockInteractionKind.Lockpick, surface, DaggerfallLockInteractionStatus.DoorMoving, null);
        if (door.LockValue <= 0)
            return RejectDoorState(door, DaggerfallLockInteractionKind.Lockpick, surface, DaggerfallLockInteractionStatus.AlreadyUnlocked, null);

        bool duplicate = previousFailedSkill.HasValue && (surface == DaggerfallLockInteractionSurface.Interior
            ? previousFailedSkill.Value == lockpickingSkill
            : lockpickingSkill <= previousFailedSkill.Value);
        if (duplicate)
        {
            return new(
                DaggerfallLockInteractionKind.Lockpick,
                surface,
                DaggerfallLockInteractionStatus.DuplicateAttempt,
                null,
                Chance: 0,
                Roll: null,
                FailedSkillLevel: previousFailedSkill,
                RecordsSkillUse: false,
                EmitsNoise: false,
                DoorDamage: 0,
                ReportsBreakingAndEntering: false);
        }
        if (door.IsMagicallyHeld)
            return RejectDoorState(door, DaggerfallLockInteractionKind.Lockpick, surface, DaggerfallLockInteractionStatus.MagicallyHeld, null);

        int chance = surface == DaggerfallLockInteractionSurface.Interior
            ? DaggerfallFormulaPolicy.CalculateInteriorLockpickingChance(playerLevel, door.LockValue, lockpickingSkill)
            : DaggerfallFormulaPolicy.CalculateExteriorLockpickingChance(door.LockValue, lockpickingSkill);
        int admittedRoll = RequireRoll(drawRoll());
        // Interior action doors use Dice100.FailedRoll(chance): roll <= chance
        // succeeds. The exterior PlayerActivate branch compares chance > roll,
        // retaining its strict boundary as a separate donor overload.
        bool success = surface == DaggerfallLockInteractionSurface.Interior
            ? admittedRoll <= chance
            : admittedRoll < chance;

        return new(
            DaggerfallLockInteractionKind.Lockpick,
            surface,
            success ? DaggerfallLockInteractionStatus.Applied : DaggerfallLockInteractionStatus.Failed,
            success ? DaggerfallDoorOperationKind.Unlock : null,
            chance,
            admittedRoll,
            success ? null : lockpickingSkill,
            RecordsSkillUse: true,
            EmitsNoise: true,
            DoorDamage: 0,
            ReportsBreakingAndEntering: surface == DaggerfallLockInteractionSurface.Exterior);
    }

    /// <summary>
    /// Evaluates one player bash against current door state. Strength and
    /// equipped-tool force are explicit inputs, while the current door view is
    /// the only source of lock, motion and special-door truth.
    /// </summary>
    internal static DaggerfallLockInteractionDecision EvaluateBash(
        DaggerfallDoorView door,
        DaggerfallLockInteractionSurface surface,
        int strength,
        int toolForce,
        int? roll)
        => EvaluateBashCore(door, surface, strength, toolForce, () => RequireRoll(roll));

    /// <summary>Evaluates a bash while deferring its admitted roll to the caller.</summary>
    internal static DaggerfallLockInteractionDecision EvaluateBashDeferred(
        DaggerfallDoorView door,
        DaggerfallLockInteractionSurface surface,
        int strength,
        int toolForce,
        Func<int> drawRoll)
    {
        ArgumentNullException.ThrowIfNull(drawRoll);
        return EvaluateBashCore(door, surface, strength, toolForce, drawRoll);
    }

    private static DaggerfallLockInteractionDecision EvaluateBashCore(
        DaggerfallDoorView door,
        DaggerfallLockInteractionSurface surface,
        int strength,
        int toolForce,
        Func<int> drawRoll)
    {
        ValidateSurface(surface);
        ValidateAttribute(strength, nameof(strength));
        ValidateLockValue(door.LockValue);
        ArgumentOutOfRangeException.ThrowIfNegative(toolForce);

        if (door.Kind == DaggerfallDoorKind.Special)
            return RejectDoorState(door, DaggerfallLockInteractionKind.Bash, surface, DaggerfallLockInteractionStatus.SpecialDoor, null);

        // PlayerActivate closes an already-open door before considering the
        // lock, including a door that was opened by another source.
        if (door.Motion == DaggerfallDoorMotion.Open)
        {
            return new(
                DaggerfallLockInteractionKind.Bash,
                surface,
                DaggerfallLockInteractionStatus.Applied,
                DaggerfallDoorOperationKind.Close,
                Chance: 100,
                Roll: null,
                FailedSkillLevel: null,
                RecordsSkillUse: false,
                EmitsNoise: true,
                DoorDamage: 0,
                ReportsBreakingAndEntering: false);
        }

        if (door.Motion is DaggerfallDoorMotion.Opening or DaggerfallDoorMotion.Closing)
            return RejectDoorState(door, DaggerfallLockInteractionKind.Bash, surface, DaggerfallLockInteractionStatus.DoorMoving, null);
        if (door.IsMagicallyHeld)
            return RejectDoorState(door, DaggerfallLockInteractionKind.Bash, surface, DaggerfallLockInteractionStatus.MagicallyHeld, null);

        int chance = CalculateBashChance(surface, door.LockValue, strength, toolForce);
        int admittedRoll = RequireRoll(drawRoll());
        // Both PlayerActivate bash paths use Dice100.FailedRoll(chance), whose
        // success boundary is inclusive (roll <= chance). Exterior
        // lockpicking is the separate strict chance > roll path above.
        bool success = admittedRoll <= chance;
        int impact = checked(Math.Max(1, (strength / 10) + toolForce));
        return new(
            DaggerfallLockInteractionKind.Bash,
            surface,
            success ? DaggerfallLockInteractionStatus.Applied : DaggerfallLockInteractionStatus.Failed,
            success ? DaggerfallDoorOperationKind.Open : null,
            chance,
            admittedRoll,
            FailedSkillLevel: null,
            RecordsSkillUse: false,
            EmitsNoise: true,
            DoorDamage: success ? impact : 0,
            ReportsBreakingAndEntering: surface == DaggerfallLockInteractionSurface.Exterior);
    }

    private static DaggerfallLockInteractionDecision RejectDoorState(
        DaggerfallDoorView door,
        DaggerfallLockInteractionKind kind,
        DaggerfallLockInteractionSurface surface,
        DaggerfallLockInteractionStatus alreadyUnlockedStatus,
        int? roll)
    {
        DaggerfallLockInteractionStatus status = door.Kind == DaggerfallDoorKind.Special
            ? DaggerfallLockInteractionStatus.SpecialDoor
            : door.IsMagicallyHeld
                ? DaggerfallLockInteractionStatus.MagicallyHeld
                : door.Motion is DaggerfallDoorMotion.Opening or DaggerfallDoorMotion.Closing
                    ? DaggerfallLockInteractionStatus.DoorMoving
                    : door.LockValue <= 0
                        ? alreadyUnlockedStatus
                        : DaggerfallLockInteractionStatus.Applied;
        return new(kind, surface, status, null, 0, roll, null, false, false, 0, false);
    }

    private static int RequireRoll(int? roll)
    {
        if (roll is not (>= 1 and <= 100)) throw new ArgumentOutOfRangeException(nameof(roll));
        return roll.Value;
    }

    private static void ValidateSurface(DaggerfallLockInteractionSurface surface)
    {
        if (!Enum.IsDefined(surface)) throw new ArgumentOutOfRangeException(nameof(surface));
    }

    private static void ValidateLockValue(int lockValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lockValue);
    }

    private static void ValidateAttribute(int value, string parameterName)
    {
        if (value is < 0 or > MaximumSkillValue) throw new ArgumentOutOfRangeException(parameterName);
    }
}
