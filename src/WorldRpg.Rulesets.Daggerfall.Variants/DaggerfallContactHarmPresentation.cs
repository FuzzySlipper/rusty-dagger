namespace WorldRpg.Rulesets.Daggerfall.Variants;

/// <summary>
/// Harm presentation under the contact variant: a spell effect's negation sounds like the physical outcome
/// it matches, so a reflected effect is heard as a deflection and an absorbed one as harm taken whole. A
/// spell that met no one has already sounded its release, and an applied effect keeps its classic presentation.
/// </summary>
internal sealed class DaggerfallContactHarmPresentation : IDaggerfallHarmPresentation
{
    public DaggerfallOutcomeCue? SpellOutcomeCue(DaggerfallCastOutcome outcome) => outcome switch
    {
        DaggerfallCastOutcome.Reflected => DaggerfallOutcomeCue.Deflected,
        DaggerfallCastOutcome.Absorbed => DaggerfallOutcomeCue.Absorbed,
        _ => null,
    };
}
