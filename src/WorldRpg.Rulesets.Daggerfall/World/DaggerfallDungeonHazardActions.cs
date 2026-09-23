using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The live actors selected by a dungeon trigger and the source values that are not yet present
/// on <see cref="DaggerfallDungeonActionDefinition"/>.  The graph increments its action count
/// before calling a family owner, so <see cref="ActivationCount"/> is one-based just like DFU's
/// <c>DaggerfallAction.activationCount</c>.
/// </summary>
internal sealed record DaggerfallDungeonHazardExecutionContext(
    Actor Source,
    Actor Target,
    int TargetLevel,
    ulong ActivationCount,
    int? ActionIndex = null)
{
    internal DaggerfallDungeonHazardExecutionContext Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentNullException.ThrowIfNull(Target);
        if (ActivationCount == 0)
            throw new ArgumentOutOfRangeException(nameof(ActivationCount), "A dungeon action activation count is one-based.");
        return this;
    }
}

/// <summary>
/// One accepted dungeon hazard result. Health changes carry the Kit combat result so the session
/// can append the ordinary Daggerfall damage/death facts. Magicka changes carry the actual guarded
/// track loss for the same feedback path.
/// </summary>
internal sealed record DaggerfallDungeonHazardActionResult(
    DaggerfallDungeonActionExecution Execution,
    DamageResult? HealthDamage = null,
    double MagickaLost = 0d)
{
    internal bool Accepted => Execution.Outcome is DaggerfallDungeonActionOutcome.Applied
        or DaggerfallDungeonActionOutcome.AppliedWithoutChange;
}

/// <summary>
/// Daggerfall's Hurt21-25 and DrainMagicka action-family policy over canonical actor tracks.
/// This class does not acquire a player, infer a target from an Engine hit, or publish facts;
/// the session supplies the selected actors and publishes the returned result through its normal
/// feedback/fact owner.
/// </summary>
internal static class DaggerfallDungeonHazardActions
{
    private static readonly TrackId HealthTrack = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private static readonly TrackId MagickaTrack = TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value);

    // DFU's UnityEngine.Random is replaced only at this named dynamic-random edge.  The keyed
    // Engine service keeps repeated accepted activations deterministic without introducing a
    // second product clock or a local random generator.
    private const ulong RandomSeed = 0;
    private const string RandomScope = "daggerfall.dungeon-hazard.v1";

    /// <summary>
    /// Executes one retained hazard action. A null result means the action flag belongs to
    /// another family owner. A non-null rejected result means this family recognized the flag
    /// but could not safely apply it to the supplied live actor/source data.
    /// </summary>
    internal static DaggerfallDungeonHazardActionResult? Execute(
        DaggerfallDungeonActionDefinition action,
        DaggerfallDungeonHazardExecutionContext context,
        CombatResolution combat,
        IRandomService random)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(random);
        context.Validate();

        DaggerfallDungeonActionFlag flag = (DaggerfallDungeonActionFlag)action.ActionFlag;
        return flag switch
        {
            DaggerfallDungeonActionFlag.Hurt21 => Hurt21(action, context, combat, random),
            DaggerfallDungeonActionFlag.Hurt22
                or DaggerfallDungeonActionFlag.Hurt23
                or DaggerfallDungeonActionFlag.Hurt24
                or DaggerfallDungeonActionFlag.Hurt25 => LevelScaledHurt(action, context, combat),
            DaggerfallDungeonActionFlag.DrainMagicka => DrainMagicka(action, context),
            _ => null,
        };
    }

    private static DaggerfallDungeonHazardActionResult Hurt21(
        DaggerfallDungeonActionDefinition action,
        DaggerfallDungeonHazardExecutionContext context,
        CombatResolution combat,
        IRandomService random)
    {
        // DFU's action 21 deliberately fires every twentieth accepted activation. The graph
        // increments before its family callback, matching the donor's Receive -> Play order.
        if (context.ActivationCount % 20 != 0)
        {
            return new(
                new DaggerfallDungeonActionExecution(action.Id, DaggerfallDungeonActionOutcome.AppliedWithoutChange,
                    Diagnostic: $"Dungeon Hurt21 action '{action.Id}' preserved the donor sporadic activation gate at count {context.ActivationCount}."));
        }

        // DFU's AddAction receives soundID_and_index from the RDB model/flat SoundIndex field.
        // The normalized runtime calls that retained byte SoundIndex; an importer that exposes
        // the donor name may pass ActionIndex explicitly, but Axis is never a substitute.
        int index = context.ActionIndex ?? action.SoundIndex;

        if (!TryGetHealth(context.Target, action, out Track? health, out DaggerfallDungeonHazardActionResult? missing))
            return missing!;

        if (!context.Target.TryGet<DurableEntityIdentity>(out DurableEntityIdentity? identity))
        {
            return Rejected(action, $"Hurt21 action '{action.Id}' requires a durable target identity for the Engine random key.");
        }

        int minimum = Math.Max(1, checked((int)action.Magnitude));
        int maximumExclusive = Math.Max(1, index);
        int roll;
        if (maximumExclusive == minimum)
        {
            roll = minimum;
        }
        else
        {
            // Unity swaps reversed integer arguments while keeping the original first
            // argument inclusive and the second exclusive. Thus Range(8, 3) draws [4, 8].
            int minimumInclusive = minimum < maximumExclusive ? minimum : checked(maximumExclusive + 1);
            int maximumInclusive = minimum < maximumExclusive ? checked(maximumExclusive - 1) : minimum;
            long value = random.DrawKeyed(new KeyedRngRequest(
                RandomSeed,
                RandomScope,
                $"action:{action.Id}:target:{identity.Identity.Value}:activation:{context.ActivationCount}",
                minimumInclusive,
                maximumInclusive)).Value;
            if (value < minimumInclusive || value > maximumInclusive)
                throw new InvalidOperationException($"Engine random service returned Hurt21 value {value} outside [{minimumInclusive}, {maximumInclusive}].");
            roll = checked((int)value);
        }

        int damage = checked(roll * Math.Max(context.TargetLevel, 1));
        return ApplyHealth(action, context, combat, health!, damage);
    }

    private static DaggerfallDungeonHazardActionResult LevelScaledHurt(
        DaggerfallDungeonActionDefinition action,
        DaggerfallDungeonHazardExecutionContext context,
        CombatResolution combat)
    {
        if (!TryGetHealth(context.Target, action, out Track? health, out DaggerfallDungeonHazardActionResult? missing))
            return missing!;

        // DFU uses Magnitude only for flat records and ActionAxisRawValue otherwise. Axis is the
        // normalized retained byte for that latter source field. Level is clamped to one exactly
        // as PlayerEntity.Level was in the donor action delegate.
        int parameter = action.IsFlat ? action.Magnitude : action.Axis;
        int damage = checked(parameter * Math.Max(context.TargetLevel, 1));
        return ApplyHealth(action, context, combat, health!, damage);
    }

    private static DaggerfallDungeonHazardActionResult DrainMagicka(
        DaggerfallDungeonActionDefinition action,
        DaggerfallDungeonHazardExecutionContext context)
    {
        if (!context.Target.Get<StatsComponent>().TryGetTrack(MagickaTrack, out Track? magicka))
        {
            return Rejected(action, $"Dungeon DrainMagicka action '{action.Id}' target actor has no '{MagickaTrack.Value}' track.");
        }

        int parameter = action.IsFlat ? action.Magnitude : action.Axis;
        double requested = Math.Max(1, parameter);
        double available = Math.Max(0d, magicka.Current - magicka.Minimum);
        double requestedApplied = Math.Min(requested, available);
        if (requestedApplied == 0d)
        {
            return new(new DaggerfallDungeonActionExecution(action.Id, DaggerfallDungeonActionOutcome.AppliedWithoutChange),
                MagickaLost: 0d);
        }

        double before = magicka.Current;
        if (!magicka.TrySpend(requestedApplied))
        {
            return Rejected(action, $"Dungeon DrainMagicka action '{action.Id}' could not spend its guarded magicka amount.");
        }

        double lost = before - magicka.Current;
        return new(
            new DaggerfallDungeonActionExecution(action.Id,
                lost > 0d ? DaggerfallDungeonActionOutcome.Applied : DaggerfallDungeonActionOutcome.AppliedWithoutChange),
            MagickaLost: lost);
    }

    private static DaggerfallDungeonHazardActionResult ApplyHealth(
        DaggerfallDungeonActionDefinition action,
        DaggerfallDungeonHazardExecutionContext context,
        CombatResolution combat,
        Track health,
        int damage)
    {
        ApplyHitEvent applied = combat.ApplyToHealth(
            new CombatParticipants(context.Source, context.Target, "dungeon-hazard"),
            damage,
            0,
            health);
        DaggerfallDungeonActionOutcome outcome = applied.ActualHealthLost > 0d
            ? DaggerfallDungeonActionOutcome.Applied
            : DaggerfallDungeonActionOutcome.AppliedWithoutChange;
        return new(new DaggerfallDungeonActionExecution(action.Id, outcome), applied.Result);
    }

    private static bool TryGetHealth(
        Actor target,
        DaggerfallDungeonActionDefinition action,
        out Track? health,
        out DaggerfallDungeonHazardActionResult? missing)
    {
        if (target.Get<StatsComponent>().TryGetTrack(HealthTrack, out health))
        {
            missing = null;
            return true;
        }

        health = null;
        missing = Rejected(action, $"Dungeon health action '{action.Id}' target actor has no '{HealthTrack.Value}' track.");
        return false;
    }

    private static DaggerfallDungeonHazardActionResult Rejected(DaggerfallDungeonActionDefinition action, string diagnostic) =>
        new(new DaggerfallDungeonActionExecution(action.Id, DaggerfallDungeonActionOutcome.RejectedOperation, Diagnostic: diagnostic));
}
