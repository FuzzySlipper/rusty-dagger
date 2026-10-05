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
                RaceImmune = exposure.TargetId == State.Actors.Player.DurableId ? PlayerRacePoisonTolerance == DaggerfallDiseaseCareerTolerance.Immune : exposure.RaceImmune,
                RaceTolerance = exposure.TargetId == State.Actors.Player.DurableId ? PlayerRacePoisonTolerance : exposure.RaceTolerance,
                BiographyModifier = checked(exposure.BiographyModifier + (exposure.TargetId == State.Actors.Player.DurableId ? State.Character.Background?.Modifiers.PoisonResistance ?? 0 : 0)),
            },
            variant,
            PoisonRoll,
            itemId);
    }

    /// <summary>
    /// What a delivery aimed at the player reads. The career's own poison tolerance comes from the raw
    /// career bytes; the level is the live one, because the donor refuses a first-level target; Willpower
    /// is the player's own. Race tolerance comes from the normalized selected race catalogue.
    /// </summary>
    private DaggerfallDiseaseCareerTolerance PlayerRacePoisonTolerance =>
        State.Character.Race.Tolerance(DaggerfallCareerTolerances.Poison);

    internal DaggerfallPoisonExposure PlayerPoisonExposure(bool bypassResistance)
    {
        Actor player = State.Actors.Player.Actor;
        return new DaggerfallPoisonExposure(
            State.Actors.Player.DurableId,
            TargetLevel: player.Get<ProgressionState>().Level,
            CareerImmune: false,
            RaceImmune: PlayerRacePoisonTolerance == DaggerfallDiseaseCareerTolerance.Immune,
            Willpower: player.Get<StatsComponent>().GetStat(StatId.Parse(DaggerfallMechanicsIds.Willpower.Value)).ValueInt,
            BypassResistance: bypassResistance,
            Tolerance: DaggerfallPoisonPolicy.CareerTolerance(State.Character.Career), RaceTolerance: PlayerRacePoisonTolerance);
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
        if (State.ItemInstances.ContainsUnique(weaponItemId) && State.ItemInstances.RequireUnique(weaponItemId).PoisonVariant is int variant)
            DeliverWeaponPoison(targetActorId, new DaggerfallWeaponPoisonSource(weaponItemId, variant));
    }

    internal void DeliverWeaponPoison(long targetActorId, DaggerfallWeaponPoisonSource admitted)
    {
        ulong weaponItemId = admitted.ItemId;
        if (!State.ItemInstances.ContainsUnique(weaponItemId)) return;
        DaggerfallItemInstanceMetadata weapon = State.ItemInstances.RequireUnique(weaponItemId);
        if (weapon.PoisonVariant is not int variant || variant != admitted.Variant) return;
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
                TargetLevel: targetActorId == State.Actors.Player.DurableId ? State.Actors.Player.Progression.Level
                    : _roster.Definitions.TryGetValue(targetActorId, out var targetDefinition)
                        ? targetDefinition.Level ?? 1 : throw new InvalidOperationException($"Poison target {targetActorId} has no admitted level."),
                CareerImmune: false,
                RaceImmune: targetActorId == State.Actors.Player.DurableId && PlayerRacePoisonTolerance == DaggerfallDiseaseCareerTolerance.Immune,
                Willpower: struck.Get<StatsComponent>().GetStat(StatId.Parse(DaggerfallMechanicsIds.Willpower.Value)).ValueInt,
                Tolerance: targetActorId == State.Actors.Player.DurableId ? DaggerfallPoisonPolicy.CareerTolerance(State.Character.Career)
                    : _roster.Definitions.TryGetValue(targetActorId, out var definition) && definition.Career is string career
                        ? DaggerfallPoisonPolicy.CareerTolerance(_definitions.Catalogs.RequireCareer(career)) : DaggerfallDiseaseCareerTolerance.Normal,
                RaceTolerance: targetActorId == State.Actors.Player.DurableId ? PlayerRacePoisonTolerance : DaggerfallDiseaseCareerTolerance.Normal),
            // The deposited dose belongs to its target. Keeping a live item reference would
            // make breaking or retiring the source weapon cure an already admitted poison.
            variant);
        State.ItemInstances.ReplaceUnique(weaponItemId, weapon with { PoisonVariant = null });
    }

    /// <summary>Cures every poison the player carries, taking back what they still hold.</summary>
    internal bool CurePoison() => CurePoison(State.Actors.Player.Actor);

    private bool CurePoison(Actor actor) => _poisons.Cure(actor);
}
