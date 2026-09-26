namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>
    /// The named FORM-06 entry point for accepted poison-delivery callers. The caller owns the exposure —
    /// a weapon's strike does not bypass resistance, a drug taken as medicine does — and the variant it
    /// delivers; this session supplies the admitted draw, the canonical poison owner, and the background's
    /// own poison resistance, which is deliberately not the disease modifier beside it.
    /// </summary>
    internal DaggerfallPoisonAdmission InflictPoison(DaggerfallPoisonExposure exposure, int variant)
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
            PoisonRoll(1, 100));
    }

    /// <summary>Cures every poison the player carries, taking back what they still hold.</summary>
    internal bool CurePoison() => State.Poisons.Cure(State.Actors.Player.Actor);
}
