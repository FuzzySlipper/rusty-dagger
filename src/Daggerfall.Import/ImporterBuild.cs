namespace Daggerfall.Import;

/// <summary>The identity of the importer source this assembly was built from.</summary>
/// <remarks>
/// The revision is stamped at build time (see <c>ImporterRevision.targets</c>) as one SHA-256 over the
/// importer's tracked inputs: this library, <c>Daggerfall.Import.Tool</c>, <c>WorldRpg.SpriteAuthoring</c> and
/// <c>data/</c>, by path and content. A change anywhere else leaves it unchanged, so regenerating content at the
/// same importer source and data reproduces the same bytes, and any edit there, committed or not, names
/// another revision. The data container records it, so a product built from the same tree can say whether
/// the imported data is current.
/// </remarks>
public static partial class ImporterBuild
{
    public static string Revision { get; } = StampedRevision;
}
