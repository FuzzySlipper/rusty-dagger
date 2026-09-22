namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>
    /// The named FORM-06 entry point for accepted monster-hit and quest exposure callers.
    /// Both callers supply their own stable source/instance identity; this session supplies the
    /// admitted calendar, canonical player state, RNG, and compiled active-effect lifecycle.
    /// </summary>
    internal DaggerfallDiseaseAdmission InflictDisease(DaggerfallDiseaseExposure exposure) =>
        DaggerfallDiseasePolicy.InflictDisease(State.Effects, State.Actors, _random, () => _time.Calendar.DayNumber, exposure, State.Character.Career);

    internal int CureDisease(DaggerfallClassicDisease disease) =>
        DaggerfallDiseasePolicy.CureDisease(State.Effects, State.Actors.Player.DurableId, disease);

    internal int CureAllDiseases() =>
        DaggerfallDiseasePolicy.CureAllDiseases(State.Effects, State.Actors.Player.DurableId);
}
