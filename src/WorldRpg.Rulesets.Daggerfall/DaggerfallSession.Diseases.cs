namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>
    /// The named FORM-06 entry point for accepted monster-hit and quest exposure callers.
    /// Both callers supply their own stable source/instance identity; this session supplies the
    /// admitted calendar, canonical player state, RNG, and compiled active-effect lifecycle.
    /// </summary>
    internal DaggerfallDiseaseAdmission InflictDisease(DaggerfallDiseaseExposure exposure) =>
        DaggerfallDiseasePolicy.InflictDisease(State.Effects, State.Actors, _random, () => _time.Calendar.DayNumber,
            exposure with { ActiveResistanceChance = State.Effects.MagicDefenseFor(exposure.TargetId).Resistances
                    .FirstOrDefault(channel => channel.Element == Policies.DaggerfallMagicResistanceElement.DiseaseOrPoison) is { } resistance
                    ? Math.Min(100, resistance.Chance) : null,
                BiographyModifier = checked(exposure.BiographyModifier + (State.Character.Background?.Modifiers.DiseaseResistance ?? 0)),
                RaceTolerance = _definitions.Catalogs.RequireRace(State.Character.Identity.RaceId).Tolerance(Content.DaggerfallCareerTolerances.Disease) }, State.Character.Career);

    /// <summary>Accepted combat/quest exposures supply stable provenance; #8142 owns monster-hit selection and chance.</summary>
    internal DaggerfallInfectionAdmission InflictTransformationInfection(DaggerfallInfectionExposure exposure) => Infections.Inflict(exposure);

    internal int CureDisease(DaggerfallClassicDisease disease) =>
        DaggerfallDiseasePolicy.CureDisease(State.Effects, State.Actors.Player.DurableId, disease);

    internal int CureAllDiseases() =>
        DaggerfallDiseasePolicy.CureAllDiseases(State.Effects, State.Actors.Player.DurableId);
}
