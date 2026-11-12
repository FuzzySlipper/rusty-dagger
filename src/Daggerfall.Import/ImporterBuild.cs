namespace Daggerfall.Import;

/// <summary>The identity of the importer source this assembly was built from.</summary>
/// <remarks>
/// The revision is stamped at build time (see <c>ImporterRevision.targets</c>) as one SHA-256 over what the
/// import reads from the repository: this library, <c>Daggerfall.Import.Tool</c> and the tracked data files its
/// steps read, by path and content. A change anywhere else leaves it unchanged, so regenerating content at the
/// same importer source and data reproduces the same bytes, and any edit there, committed or not, names
/// another revision. The data container records it, so a product built from the same tree can say whether
/// the imported data is current.
/// </remarks>
public static partial class ImporterBuild
{
    public static string Revision { get; } = StampedRevision;

    /// <summary>
    /// The identity of the relief bake's own source, <c>src/Daggerfall.Import/Relief</c>, stamped the same way: a kept
    /// bake is recorded against it, so an edit anywhere else in the importer leaves the bake current and an edit to the
    /// bake makes the next import bake again.
    /// </summary>
    public static string ReliefBakeRevision { get; } = StampedReliefBakeRevision;
}
