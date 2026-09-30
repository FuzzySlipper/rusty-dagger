namespace Daggerfall.Import;

/// <summary>The identity of the importer source this assembly was built from.</summary>
/// <remarks>
/// The revision is stamped at build time (see <c>StampImporterRevision</c> in the project file) as the last
/// commit that changed the importer's own source: this library, <c>Daggerfall.Import.Tool</c> and
/// <c>WorldRpg.SpriteAuthoring</c>. A commit that touches only content or other projects therefore leaves it
/// unchanged, so regenerating content at the same importer source reproduces the same bytes. A build from
/// uncommitted importer changes carries a <c>-dirty</c> suffix, and a build outside a Git checkout says
/// <c>unknown</c>; both are recorded as observed rather than refused.
/// </remarks>
public static partial class ImporterBuild
{
    public static string Revision { get; } = StampedRevision;
}
