using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>
    /// The named FORM-06 entry point for accepted poison-delivery callers. The caller owns the exposure —
    /// a weapon's strike does not bypass resistance, a drug taken as medicine does — and the variant it
    /// delivers; this session supplies the admitted draw, the canonical poison owner, and the background's
    /// own poison resistance, which is deliberately not the disease modifier beside it.
    /// </summary>
    internal DaggerfallPoisonAdmission InflictPoison(DaggerfallPoisonExposure exposure, int variant, ulong? itemId = null)
    {
        ArgumentNullException.ThrowIfNull(exposure);
        return DaggerfallPoisonPolicy.InflictPoison(
            State.Poisons,
            State.Actors,
            exposure with
            {
                BiographyModifier = checked(exposure.BiographyModifier + (State.Character.Background?.Modifiers.PoisonResistance ?? 0)),
            },
            variant,
            PoisonRoll,
            itemId);
    }

    /// <summary>
    /// What a delivery aimed at the player reads. The career's own poison tolerance comes from the raw
    /// career bytes; the level is the live one, because the donor refuses a first-level target; Willpower
    /// is the player's own. Race immunity is false because the imported race catalogue carries no immunity
    /// flags at all — the donor's own nine races set none for poison (only the High Elf sets any, and that
    /// is paralysis) — so this must be revisited when race flags are imported rather than standing in for
    /// a value that was never read.
    /// </summary>
    internal DaggerfallPoisonExposure PlayerPoisonExposure(bool bypassResistance)
    {
        Actor player = State.Actors.Player.Actor;
        return new DaggerfallPoisonExposure(
            State.Actors.Player.DurableId,
            TargetLevel: player.Get<ProgressionState>().Level,
            CareerImmune: false,
            RaceImmune: false,
            Willpower: player.Get<StatsComponent>().GetStat(StatId.Parse(DaggerfallMechanicsIds.Willpower.Value)).ValueInt,
            BypassResistance: bypassResistance,
            Tolerance: DaggerfallPoisonPolicy.CareerTolerance(State.Character.Career));
    }

    /// <summary>
    /// What taking a drug does: the template names the poison, and a self-delivered dose bypasses
    /// resistance the way the donor's own drug use does. A template that is not one of the four drugs names
    /// no poison at all rather than a neighbouring one.
    /// </summary>
    internal DaggerfallPoisonAdmission UseDrug(int template)
    {
        int variant = DaggerfallPoisonPolicy.VariantForDrugTemplate(template)
            ?? throw new ArgumentOutOfRangeException(nameof(template), $"Item template {template} is not one of the four classic drugs.");
        return InflictPoison(PlayerPoisonExposure(bypassResistance: true), variant);
    }

    /// <summary>
    /// A landed strike delivers the coating its weapon carries and spends it. Resistance is in the way
    /// exactly as the donor has it, and the swing spends the dose whatever the throw decides — the coating
    /// leaves the weapon because it was used, not because it worked.
    /// </summary>
    internal void DeliverWeaponPoison(long targetActorId, ulong weaponItemId)
    {
        if (!State.ItemInstances.ContainsUnique(weaponItemId)) return;
        DaggerfallItemInstanceMetadata weapon = State.ItemInstances.RequireUnique(weaponItemId);
        if (weapon.PoisonVariant is not int variant) return;
        // The player answers for itself: the durable-id lookup that finds a site actor needs an actor body,
        // which the player does not carry, so asking only that would report the player as missing.
        Actor? struck = State.Actors.Player.DurableId == targetActorId
            ? State.Actors.Player.Actor
            : State.Actors.TryGet(targetActorId, out WorldRpg.Kit.Actors.ActorState target) && target is not null
                ? target.Actor
                : null;
        if (struck is null) return;

        _ = InflictPoison(
            new DaggerfallPoisonExposure(
                targetActorId,
                TargetLevel: struck.Get<ProgressionState>().Level,
                CareerImmune: false,
                RaceImmune: false,
                Willpower: struck.Get<StatsComponent>().GetStat(StatId.Parse(DaggerfallMechanicsIds.Willpower.Value)).ValueInt,
                Tolerance: DaggerfallCareerTolerances.Tolerance(State.Character.Career, DaggerfallCareerTolerances.Poison)),
            variant,
            weaponItemId);
        State.ItemInstances.ReplaceUnique(weaponItemId, weapon with { PoisonVariant = null });
    }

    /// <summary>Cures every poison the player carries, taking back what they still hold.</summary>
    internal bool CurePoison() => State.Poisons.Cure(State.Actors.Player.Actor);
}
