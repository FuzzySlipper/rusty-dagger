namespace WorldRpg.Rulesets.Daggerfall.Modules;

/// <summary>The donor's senses EnemySenses reads: sight and hearing reach, the field of view, stealth's distance and the illusions' breakthrough chances.</summary>
internal static class DaggerfallPerceptionQueryDefaults
{
    // These are the source units used by EnemySenses. MeshReader.GlobalScale is a
    // donor conversion constant, not a second product clock or spatial mechanism.
    internal const double ClassicGlobalScale = .025d;
    internal const double SightRadius = 4096d * ClassicGlobalScale;
    internal const double HearingRadius = 25d;
    internal const double FieldOfViewDegrees = 180d;
    internal const double StealthMaximumDistance = 1024d * ClassicGlobalScale;
    internal const int BlendingBreakthroughChance = 8;
    internal const int ShadeBreakthroughChance = 4;

    // A 180 degree field includes the exact side plane.  Keeping the admitted
    // cosine at zero also avoids excluding a side-facing pair through the
    // tiny positive rounding residue of cos(pi / 2).
    internal const double MinimumFacingCosine = 0d;
}
