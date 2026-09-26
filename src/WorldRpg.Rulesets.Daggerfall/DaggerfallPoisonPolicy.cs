using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The twelve classic poison types, in the donor's own order and with its classic values. The donor
/// starts them at 128 and builds each variant's effect key as <c>Poison-{name}</c>; the names here are
/// the pack's spelling of the same identities.
/// </summary>
internal enum DaggerfallPoisonVariant
{
    NuxVomica = 128,
    Arsenic = 129,
    Moonseed = 130,
    Drothweed = 131,
    Somnalius = 132,
    PyrrhicAcid = 133,
    Magebane = 134,
    Thyrwort = 135,
    Indulcet = 136,
    Sursum = 137,
    QuaestoVil = 138,
    Aegrotat = 139,
}

/// <summary>
/// The twelve compiled poison effects, one per archetype, over this session's draw and vitality owners.
/// The archetype's own key is the effect key, so a save names the poison it carries through the catalog
/// that has to interpret it, and a key no archetype answers is refused rather than silently inert.
/// </summary>
internal static class DaggerfallPoisonEffects
{
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(
        IRandomService random,
        DaggerfallVitalityConsequences vitality,
        Func<DaggerfallCareerDefinition> playerCareer)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(vitality);
        ArgumentNullException.ThrowIfNull(playerCareer);
        return DaggerfallPoisonArchetypes.All.Select(archetype => new DaggerfallEffectDefinition(
            archetype.Key,
            archetype.Key,
            DaggerfallEffectStacking.Stack,
            ushort.MaxValue,
            1,
            // The arms are stat sources the effect owns, so ending the poison is one cleanup action whether
            // it ended by completing, by a superseding dose, or by a cure.
            Apply: effect => [new DelegateActiveEffectContribution(() => DaggerfallPoisonArms.RemoveArms(effect, playerCareer))],
            MagicRound: effect => DaggerfallPoisonArms.AdvanceMinute(effect, archetype, random, vitality, playerCareer),
            Resume: effect =>
            {
                DaggerfallPoisonArms.Resume(effect, DaggerfallPoisonArms.State(effect));
                return [new DelegateActiveEffectContribution(() => DaggerfallPoisonArms.RemoveArms(effect, playerCareer))];
            }));
    }
}

/// <summary>What an attempted poisoning came to.</summary>
internal enum DaggerfallPoisonAdmission
{
    /// <summary>The poison takes hold and its effect is started.</summary>
    Admitted,

    /// <summary>The target's saving throw against the shared DiseaseOrPoison element held.</summary>
    Resisted,

    /// <summary>Nothing to resist: the target cannot be poisoned at all.</summary>
    Immune,
}

/// <summary>
/// One attempt to poison a target, with everything the donor's admission reads. The caller owns where
/// each value came from: the career's own poison tolerance, the live race's immunity flag and the
/// background's poison-specific resistance modifier.
/// </summary>
internal sealed record DaggerfallPoisonExposure(
    long TargetId,
    int TargetLevel,
    bool CareerImmune,
    bool RaceImmune,
    int Willpower,
    DaggerfallDiseaseCareerTolerance Tolerance = DaggerfallDiseaseCareerTolerance.Normal,
    int BiographyModifier = 0,
    bool BypassResistance = false);

/// <summary>
/// FORM-06's poison admission, as the donor's <c>FormulaHelper.InflictPoison</c> orders it: a target
/// whose career tolerates no poison, a player race immune to it, or a first-level creature is never
/// poisoned; otherwise the target's saving throw on the shared DiseaseOrPoison element decides, and a
/// caller may bypass that throw. The poison's own onset, duration and periodic effects belong to the
/// effect it starts, not to this decision.
/// </summary>
internal static class DaggerfallPoisonPolicy
{
    /// <summary>The twelve variants in the donor's order, which is also its classic numbering.</summary>
    internal static IReadOnlyList<DaggerfallPoisonVariant> Variants { get; } =
    [
        DaggerfallPoisonVariant.NuxVomica,
        DaggerfallPoisonVariant.Arsenic,
        DaggerfallPoisonVariant.Moonseed,
        DaggerfallPoisonVariant.Drothweed,
        DaggerfallPoisonVariant.Somnalius,
        DaggerfallPoisonVariant.PyrrhicAcid,
        DaggerfallPoisonVariant.Magebane,
        DaggerfallPoisonVariant.Thyrwort,
        DaggerfallPoisonVariant.Indulcet,
        DaggerfallPoisonVariant.Sursum,
        DaggerfallPoisonVariant.QuaestoVil,
        DaggerfallPoisonVariant.Aegrotat,
    ];

    /// <summary>
    /// Whether a variant is one of the four the donor treats as a drug rather than a poison. The
    /// distinction matters to the effect's own positive-stat cleanup, not to admission.
    /// </summary>
    internal static bool IsDrug(DaggerfallPoisonVariant variant) => variant is
        DaggerfallPoisonVariant.Indulcet or DaggerfallPoisonVariant.Sursum
        or DaggerfallPoisonVariant.QuaestoVil or DaggerfallPoisonVariant.Aegrotat;

    /// <summary>
    /// The effect key the donor builds for a variant: <c>Poison-</c> plus its own enum identifier, which
    /// carries an underscore wherever the classic name is two words.
    /// </summary>
    internal static string EffectKey(DaggerfallPoisonVariant variant)
    {
        // The archetype table names every variant, so it answers the key; a variant outside the classic
        // twelve keeps the donor's own fallback shape rather than inventing a name.
        return DaggerfallPoisonArchetypes.TryResolve((int)variant, out DaggerfallPoisonArchetype archetype)
            ? archetype.Key
            : $"Poison-{variant}";
    }

    /// <summary>
    /// The poison a drug item delivers. The classic drug templates are 78-81 and the poison values they
    /// carry are 136-139 — the same four drugs in the same order, an offset of 58. The donor's own drug use
    /// adds 66 instead, which names 144-147: those are not poison values at all, so its effect key becomes
    /// <c>Poison-144</c> and no effect answers it. The offset here is by name against the archetype table,
    /// which is what the classic files carry.
    /// </summary>
    internal static int? VariantForDrugTemplate(int template) => template is >= 78 and <= 81 ? template + 58 : null;

    /// <summary>The donor's own poison identity for a classic value, or null when nothing carries it.</summary>
    internal static DaggerfallPoisonVariant? VariantFor(int classicValue) =>
        Enum.IsDefined(typeof(DaggerfallPoisonVariant), classicValue) ? (DaggerfallPoisonVariant)classicValue : null;

    /// <summary>
    /// The decision before any throw: immunity is settled first and a bypass skips the saving throw, so
    /// this answers <see cref="DaggerfallPoisonAdmission.Admitted"/> for an attempt that still has to be
    /// thrown. Use the overload that takes a roll for the complete decision.
    /// </summary>
    internal static DaggerfallPoisonAdmission AdmitBeforeThrow(DaggerfallPoisonExposure exposure)
    {
        ArgumentNullException.ThrowIfNull(exposure);
        if (exposure.TargetLevel < 1) throw new ArgumentOutOfRangeException(nameof(exposure), "A poison target needs a level.");
        if (exposure.CareerImmune || exposure.RaceImmune) return DaggerfallPoisonAdmission.Immune;
        if (exposure.TargetLevel == 1) return DaggerfallPoisonAdmission.Immune;
        if (exposure.BypassResistance) return DaggerfallPoisonAdmission.Admitted;

        int chance = SavingThrowChance(exposure.Willpower, exposure.Tolerance, exposure.BiographyModifier);
        return chance == 100 ? DaggerfallPoisonAdmission.Immune : DaggerfallPoisonAdmission.Admitted;
    }

    /// <summary>
    /// The same decision, but with the caller's own 1-100 throw: the throw cancels the poisoning when it
    /// is above the chance, and a near miss leaves the donor's reduced payload rather than immunity.
    /// </summary>
    internal static DaggerfallPoisonAdmission Admit(DaggerfallPoisonExposure exposure, int roll)
    {
        ArgumentNullException.ThrowIfNull(exposure);
        if (roll is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(roll));
        DaggerfallPoisonAdmission decided = AdmitBeforeThrow(exposure);
        if (decided != DaggerfallPoisonAdmission.Admitted || exposure.BypassResistance)
            return decided;
        int chance = SavingThrowChance(exposure.Willpower, exposure.Tolerance, exposure.BiographyModifier);
        return DaggerfallDiseasePolicy.DiseaseSavingThrowAmount(chance, roll) == 0
            ? DaggerfallPoisonAdmission.Resisted
            : DaggerfallPoisonAdmission.Admitted;
    }

    /// <summary>
    /// The career's own poison tolerance, read from the raw bytes the donor resolves: the poison flag is its
    /// own bit, so a career resistant to disease and weak to poison reads exactly that.
    /// </summary>
    internal static DaggerfallDiseaseCareerTolerance CareerTolerance(DaggerfallCareerDefinition career) =>
        DaggerfallCareerTolerances.Tolerance(career, DaggerfallCareerTolerances.Poison);

    /// <summary>
    /// FORM-06's infliction, as the donor orders it: immunity is settled first, a bypass skips the throw,
    /// and only an attempt that still needs one draws it — the donor's own throw draws nothing for a target
    /// it refused before reaching it. An admitted attempt then starts the archetype's own effect, so
    /// admission and the effect it starts are one step.
    /// </summary>
    /// <param name="poisons">The owner that starts the admitted poison.</param>
    /// <param name="actors">The actors an admitted poison can be started on.</param>
    /// <param name="exposure">What the throw reads, with the caller's own modifiers already applied.</param>
    /// <param name="variant">The classic variant the delivery carries.</param>
    /// <param name="roll">Draws the caller's inclusive 1-100 throw, and is called only when one is needed.</param>
    internal static DaggerfallPoisonAdmission InflictPoison(
        DaggerfallPoisonRuntime poisons,
        ActorsState actors,
        DaggerfallPoisonExposure exposure,
        int variant,
        Func<int, int, int> roll,
        ulong? itemId = null)
    {
        ArgumentNullException.ThrowIfNull(poisons);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(exposure);
        ArgumentNullException.ThrowIfNull(roll);
        if (exposure.TargetLevel < 1) throw new ArgumentOutOfRangeException(nameof(exposure), "A poison target needs a level.");

        // The donor's own order, kept exactly because it decides what the admitted draw is spent on: a
        // career or race immunity is refused before it throws at all, a tolerance that cannot be beaten is
        // the same, a bypassed delivery skips the throw, and only then does the level matter — which is why
        // a first-level target that would have been refused still drew the throw the donor made.
        if (exposure.CareerImmune || exposure.RaceImmune) return DaggerfallPoisonAdmission.Immune;
        int chance = SavingThrowChance(exposure.Willpower, exposure.Tolerance, exposure.BiographyModifier);
        DaggerfallPoisonAdmission decided;
        if (chance == 100) decided = DaggerfallPoisonAdmission.Immune;
        else if (exposure.BypassResistance) decided = DaggerfallPoisonAdmission.Admitted;
        else
        {
            decided = DaggerfallDiseasePolicy.DiseaseSavingThrowAmount(chance, roll(1, 100)) == 0
                ? DaggerfallPoisonAdmission.Resisted
                : DaggerfallPoisonAdmission.Admitted;
        }

        if (decided == DaggerfallPoisonAdmission.Admitted && exposure.TargetLevel == 1)
            decided = DaggerfallPoisonAdmission.Immune;
        if (decided != DaggerfallPoisonAdmission.Admitted) return decided;
        // The player is not one of the actors carrying a body, so the durable-id lookup that answers for a
        // site actor would report the player missing; the player's own state answers for it instead.
        Actor target = actors.Player.DurableId == exposure.TargetId
            ? actors.Player.Actor
            : actors.TryGet(exposure.TargetId, out ActorState? actor) && actor is not null
                ? actor.Actor
                : throw new ArgumentException($"An admitted poison names missing actor {exposure.TargetId}.", nameof(exposure));

        if (!poisons.Afflict(target, variant, itemId))
        {
            // Admission and the effect it starts are one step: a variant no archetype answers cannot come
            // back as a quietly successful poisoning.
            throw new ArgumentException($"Admitted poison variant {variant} is not one of the twelve archetypes.", nameof(variant));
        }

        return DaggerfallPoisonAdmission.Admitted;
    }

    /// <summary>
    /// The donor's saving-throw baseline for poison. It is FORM-06's shared DiseaseOrPoison throw — the
    /// same arithmetic the disease path uses — so the two differ only in which tolerance and background
    /// value the caller supplies, never in the formula.
    /// </summary>
    internal static int SavingThrowChance(int willpower, DaggerfallDiseaseCareerTolerance tolerance = DaggerfallDiseaseCareerTolerance.Normal, int biographyModifier = 0) =>
        DaggerfallDiseasePolicy.DiseaseSavingThrowChance(willpower, tolerance, biographyModifier);
}
