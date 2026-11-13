using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Who a poison reaches and what its throw reads: FORM-06's entry point for every accepted delivery (a weapon's
/// coating, a drug, a quest or service), the player's own race, career and background tolerances, and the
/// admitted draw the throws and the poisons' arms share. The poisons themselves are
/// <see cref="Runtime"/>, effects in the lifecycle it is handed.
/// </summary>
/// <remarks>
/// The draw's ordinal is saved, so a replay inside a session draws the same values; the session captures and
/// restores it through <see cref="Draws"/>.
/// </remarks>
internal sealed class DaggerfallPoisoning
{
    private readonly IRandomService _random;
    private readonly ActorsState _actors;
    private readonly DaggerfallCharacterState _character;
    private readonly DaggerfallItemInstances _items;
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _actorDefinitions;
    private readonly DaggerfallDefinitions _definitions;
    private readonly CombatResolution _combat;

    /// <param name="effects">The lifecycle the poisons are effects in.</param>
    /// <param name="random">The keyed draw service the admission and the arms draw from.</param>
    /// <param name="actors">The actors a delivery can reach, the player among them.</param>
    /// <param name="character">The player's race, career and background.</param>
    /// <param name="items">The item instances a weapon's coating is read from and spent on.</param>
    /// <param name="actorDefinitions">Each site actor's definition: its level and career.</param>
    /// <param name="definitions">The careers a site actor's tolerance is read from.</param>
    /// <param name="combat">The harm pipeline whose negation decides a throw.</param>
    /// <param name="draws">The saved draw ordinal.</param>
    internal DaggerfallPoisoning(DaggerfallEffectLifecycle effects, IRandomService random, ActorsState actors,
        DaggerfallCharacterState character, DaggerfallItemInstances items,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> actorDefinitions, DaggerfallDefinitions definitions,
        CombatResolution combat, long draws)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _actorDefinitions = actorDefinitions ?? throw new ArgumentNullException(nameof(actorDefinitions));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        Draws = draws;
        Runtime = new DaggerfallPoisonRuntime(effects, Roll, () => character.Career);
    }

    /// <summary>The poisons the actors carry.</summary>
    internal DaggerfallPoisonRuntime Runtime { get; }

    /// <summary>The ordinal the admitted draws advance on.</summary>
    internal long Draws { get; private set; }

    /// <summary>
    /// The named FORM-06 entry point for accepted poison-delivery callers. The caller owns the exposure —
    /// a weapon's strike does not bypass resistance, a drug taken as medicine does — and the variant it
    /// delivers; this owner supplies the admitted draw, the canonical poison owner, and the background's
    /// own poison resistance, which is deliberately not the disease modifier beside it.
    /// </summary>
    internal DaggerfallPoisonAdmission Inflict(DaggerfallPoisonExposure exposure, int variant, ulong? itemId = null)
    {
        ArgumentNullException.ThrowIfNull(exposure);
        bool player = exposure.TargetId == _actors.Player.DurableId;
        return DaggerfallPoisonPolicy.InflictPoison(
            Runtime,
            _actors,
            exposure with
            {
                RaceImmune = player ? PlayerRaceTolerance == DaggerfallDiseaseCareerTolerance.Immune : exposure.RaceImmune,
                RaceTolerance = player ? PlayerRaceTolerance : exposure.RaceTolerance,
                BiographyModifier = checked(exposure.BiographyModifier + (player ? _character.Background?.Modifiers.PoisonResistance ?? 0 : 0)),
            },
            variant,
            Roll,
            itemId,
            _combat);
    }

    /// <summary>
    /// What a delivery aimed at the player reads. The career's own poison tolerance comes from the raw
    /// career bytes; the level is the live one, because the donor refuses a first-level target; Willpower
    /// is the player's own. Race tolerance comes from the normalized selected race catalogue.
    /// </summary>
    private DaggerfallDiseaseCareerTolerance PlayerRaceTolerance =>
        _character.Race.Tolerance(DaggerfallCareerTolerances.Poison);

    internal DaggerfallPoisonExposure PlayerExposure(bool bypassResistance)
    {
        Actor player = _actors.Player.Actor;
        return new DaggerfallPoisonExposure(
            _actors.Player.DurableId,
            TargetLevel: player.Get<ProgressionState>().Level,
            CareerImmune: false,
            RaceImmune: PlayerRaceTolerance == DaggerfallDiseaseCareerTolerance.Immune,
            Willpower: player.Get<StatsComponent>().GetStat(StatId.Parse(DaggerfallMechanicsIds.Willpower.Value)).ValueInt,
            BypassResistance: bypassResistance,
            Tolerance: DaggerfallPoisonPolicy.CareerTolerance(_character.Career), RaceTolerance: PlayerRaceTolerance);
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
        return Inflict(PlayerExposure(bypassResistance: true), variant);
    }

    /// <summary>
    /// A landed strike delivers the coating its weapon carries and spends it. Resistance is in the way
    /// exactly as the donor has it, and the swing spends the dose whatever the throw decides — the coating
    /// leaves the weapon because it was used, not because it worked.
    /// </summary>
    internal void DeliverWeaponPoison(long targetActorId, ulong weaponItemId)
    {
        if (_items.ContainsUnique(weaponItemId) && _items.RequireUnique(weaponItemId).PoisonVariant is int variant)
            DeliverWeaponPoison(targetActorId, new DaggerfallWeaponPoisonSource(weaponItemId, variant));
    }

    internal void DeliverWeaponPoison(long targetActorId, DaggerfallWeaponPoisonSource admitted)
    {
        ulong weaponItemId = admitted.ItemId;
        if (!_items.ContainsUnique(weaponItemId)) return;
        DaggerfallItemInstanceMetadata weapon = _items.RequireUnique(weaponItemId);
        if (weapon.PoisonVariant is not int variant || variant != admitted.Variant) return;
        bool player = _actors.Player.DurableId == targetActorId;
        // The player answers for itself: the durable-id lookup that finds a site actor needs an actor body,
        // which the player does not carry, so asking only that would report the player as missing.
        Actor? struck = player
            ? _actors.Player.Actor
            : _actors.TryGet(targetActorId, out ActorState target) && target is not null
                ? target.Actor
                : null;
        if (struck is null) return;

        _ = Inflict(
            new DaggerfallPoisonExposure(
                targetActorId,
                TargetLevel: player ? _actors.Player.Progression.Level
                    : _actorDefinitions.TryGetValue(targetActorId, out var targetDefinition)
                        ? targetDefinition.Level ?? 1 : throw new InvalidOperationException($"Poison target {targetActorId} has no admitted level."),
                CareerImmune: false,
                RaceImmune: player && PlayerRaceTolerance == DaggerfallDiseaseCareerTolerance.Immune,
                Willpower: struck.Get<StatsComponent>().GetStat(StatId.Parse(DaggerfallMechanicsIds.Willpower.Value)).ValueInt,
                Tolerance: player ? DaggerfallPoisonPolicy.CareerTolerance(_character.Career)
                    : _actorDefinitions.TryGetValue(targetActorId, out var definition) && definition.Career is string career
                        ? DaggerfallPoisonPolicy.CareerTolerance(_definitions.Catalogs.RequireCareer(career)) : DaggerfallDiseaseCareerTolerance.Normal,
                RaceTolerance: player ? PlayerRaceTolerance : DaggerfallDiseaseCareerTolerance.Normal),
            // The deposited dose belongs to its target. Keeping a live item reference would
            // make breaking or retiring the source weapon cure an already admitted poison.
            variant);
        _items.ReplaceUnique(weaponItemId, weapon with { PoisonVariant = null });
    }

    /// <summary>Cures every poison the player carries, taking back what they still hold.</summary>
    internal bool CurePlayer() => Runtime.Cure(_actors.Player.Actor);

    /// <summary>
    /// Draws one poison bound through the Engine's keyed service, so a replay inside a session draws the
    /// same values rather than whatever the process clock says.
    /// </summary>
    private int Roll(int minimum, int maximum) => checked((int)_random.DrawKeyed(new KeyedRngRequest(
        DaggerfallPoisonRandomKey.Seed,
        DaggerfallPoisonRandomKey.Scope,
        DaggerfallPoisonRandomKey.For(DaggerfallActorIdentity.PlayerEntityId, "admission", ++Draws, DaggerfallPoisonRandomKey.MinuteScope),
        minimum,
        maximum)).Value);
}
