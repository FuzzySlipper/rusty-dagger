using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The player's classic diseases: FORM-06's entry point for accepted monster-hit and quest exposures, read
/// with the player's active resistance, background and race, and the cures that end them. The diseases are
/// effects in the lifecycle it is handed; the day an infection starts on is the calendar's.
/// </summary>
/// <param name="effects">The lifecycle the diseases are effects in, and the magic defence a throw reads.</param>
/// <param name="actors">The actors, the player among them.</param>
/// <param name="random">The keyed draw service the throw and the candidate pick draw from.</param>
/// <param name="time">The calendar an infection's day is read from.</param>
/// <param name="character">The player's career, race and background.</param>
/// <param name="combat">The harm pipeline whose negation decides a throw.</param>
/// <param name="presentation">Where the player is told the symptoms acted.</param>
/// <param name="definitions">The internal text that line is read from.</param>
internal sealed class DaggerfallDiseases(DaggerfallEffectLifecycle effects, ActorsState actors, IRandomService random,
    DaggerfallWorldTime time, DaggerfallCharacterState character, CombatResolution combat,
    PresentationState presentation, DaggerfallDefinitions definitions)
{
    /// <summary>
    /// The named FORM-06 entry point for accepted monster-hit and quest exposure callers.
    /// Both callers supply their own stable source/instance identity; this owner supplies the
    /// admitted calendar, canonical player state, RNG, and compiled active-effect lifecycle.
    /// </summary>
    internal DaggerfallDiseaseAdmission Inflict(DaggerfallDiseaseExposure exposure) =>
        DaggerfallDiseasePolicy.InflictDisease(effects, actors, random, () => time.Calendar.DayNumber,
            exposure with { ActiveResistanceChance = effects.MagicDefenseFor(exposure.TargetId).Resistances
                    .FirstOrDefault(channel => channel.Element == Policies.DaggerfallMagicResistanceElement.DiseaseOrPoison) is { } resistance
                    ? Math.Min(100, resistance.Chance) : null,
                BiographyModifier = checked(exposure.BiographyModifier + (character.Background?.Modifiers.DiseaseResistance ?? 0)),
                RaceTolerance = character.Race.Tolerance(DaggerfallCareerTolerances.Disease) }, character.Career, combat);

    internal int Cure(DaggerfallClassicDisease disease) =>
        DaggerfallDiseasePolicy.CureDisease(effects, actors.Player.DurableId, disease);

    internal int CureAll() =>
        DaggerfallDiseasePolicy.CureAllDiseases(effects, actors.Player.DurableId);

    /// <summary>A disease's or poison's symptoms acted on <paramref name="target"/>; the player is told.</summary>
    internal void SymptomsActed(long target)
    {
        if (target == DaggerfallActorIdentity.PlayerEntityId)
            presentation.SetOutcome(string.Concat(definitions.Text.Require(new(DaggerfallTextKind.Internal, "youFeelSomewhatBad")).TextRuns));
    }
}
