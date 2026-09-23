namespace WorldRpg.Rulesets.Daggerfall.Modules;

/// <summary>One complete first page for the ruleset's bounded single-query decisions.</summary>
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

    internal const ulong AnyProjectionIdentity = 0;
    internal const uint FirstPairCursor = 0;
    internal const uint CompleteQueryPageSize = 64;

    // A 180 degree field includes the exact side plane.  Keeping the admitted
    // cosine at zero also avoids excluding a side-facing pair through the
    // tiny positive rounding residue of cos(pi / 2).
    internal const double MinimumFacingCosine = 0d;
}
