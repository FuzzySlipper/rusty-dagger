namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The importer revision this ruleset was built with: the same SHA-256 over the importer's sources and
/// <c>data/</c> that the import tool records in the data container it packs (<c>ImporterRevision.targets</c>).
/// A container recording another revision was imported by another importer, so its content may not be what
/// this build reads.
/// </summary>
internal static partial class DaggerfallImporterRevision
{
    internal static string Value => StampedRevision;
}
