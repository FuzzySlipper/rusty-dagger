namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The donor's own career effect flags, and how the raw career bytes resolve to one tolerance. The bit
/// values are the donor's <c>DFCareer.EffectFlags</c> and the order is its <c>GetTolerance</c>: resistance
/// first, then immunity, then low tolerance, then critical weakness, so a career that carries more than one
/// reads the first of those it holds rather than the strongest.
/// </summary>
internal static class DaggerfallCareerTolerances
{
    internal const int Poison = 4;
    internal const int Disease = 64;

    internal static DaggerfallDiseaseCareerTolerance Tolerance(DaggerfallCareerDefinition career, int effectFlag)
    {
        ArgumentNullException.ThrowIfNull(career);
        return Tolerance(career.ResistanceFlags, career.ImmunityFlags, career.LowToleranceFlags, career.CriticalWeaknessFlags, effectFlag);
    }

    internal static DaggerfallDiseaseCareerTolerance Tolerance(int resistance, int immunity, int lowTolerance, int criticalWeakness, int effectFlag)
    {
        if ((resistance & effectFlag) != 0) return DaggerfallDiseaseCareerTolerance.Resistant;
        if ((immunity & effectFlag) != 0) return DaggerfallDiseaseCareerTolerance.Immune;
        if ((lowTolerance & effectFlag) != 0) return DaggerfallDiseaseCareerTolerance.LowTolerance;
        if ((criticalWeakness & effectFlag) != 0) return DaggerfallDiseaseCareerTolerance.CriticalWeakness;
        return DaggerfallDiseaseCareerTolerance.Normal;
    }
}
