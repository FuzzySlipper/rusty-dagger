using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Publication;

/// <summary>What happened to one classified residual path when the publication ran.</summary>
public enum ResidualPublicationOutcome
{
    /// <summary>A consumer claims it, so its artifacts are published.</summary>
    Published,

    /// <summary>It reads, and nothing in the product names a consumer for it.</summary>
    UnpublishedNoConsumer,

    /// <summary>Nothing in this repository reads it, so there is nothing to publish.</summary>
    UnpublishedNoReader,
}

/// <summary>
/// One residual path's publication decision: what it classifies as, what happened to it, and why.
/// </summary>
/// <param name="Path">The supplied file name, as the manifest cites it.</param>
/// <param name="Family">The bounded family the classification placed it in.</param>
/// <param name="Reader">The reader this repository reuses, or empty when it has none.</param>
/// <param name="Classification">The disposition the classification recorded.</param>
/// <param name="Outcome">Whether the publication emitted it, and if not, which reason applies.</param>
/// <param name="Reason">Why the outcome holds, naming the consumer when one claims it.</param>
public sealed record ResidualPublicationDecision(
    string Path,
    string Family,
    string Reader,
    SourceRecordDisposition Classification,
    ResidualPublicationOutcome Outcome,
    string Reason);

/// <summary>
/// The closure report for the residual corpus: every classified path, with the publication decision
/// its classification implies.
/// </summary>
/// <remarks>
/// This publishes no artifact of its own. Its job is the part of the publication contract that is
/// easiest to skip — saying, per path, whether anything in the product actually asks for it — so a
/// family cannot be published because it was convenient and cannot be dropped because it was
/// awkward. A path the documented inventory already imports is published, because that import is the
/// consumer; a path that reads with no such import is unpublished with the reason stated; and a path
/// this repository cannot read is unpublished as unreadable rather than silently absent.
/// </remarks>
public sealed class ResidualPublicationClosure
{
    private ResidualPublicationClosure(string source, IReadOnlyList<ResidualPublicationDecision> decisions)
    {
        Source = source;
        Decisions = decisions;
    }

    /// <summary>Logical source identity the classification was read from.</summary>
    public string Source { get; }

    /// <summary>Every classified path's decision, ordered by path.</summary>
    public IReadOnlyList<ResidualPublicationDecision> Decisions { get; }

    /// <summary>The paths whose artifacts a consumer claims.</summary>
    public IEnumerable<ResidualPublicationDecision> Published => Decisions.Where(decision => decision.Outcome == ResidualPublicationOutcome.Published);

    /// <summary>The paths that read, with nothing naming a consumer for them.</summary>
    public IEnumerable<ResidualPublicationDecision> WithoutConsumer => Decisions.Where(decision => decision.Outcome == ResidualPublicationOutcome.UnpublishedNoConsumer);

    /// <summary>The paths nothing in this repository reads.</summary>
    public IEnumerable<ResidualPublicationDecision> WithoutReader => Decisions.Where(decision => decision.Outcome == ResidualPublicationOutcome.UnpublishedNoReader);

    /// <summary>
    /// Decides every classified path, from the classification alone.
    /// </summary>
    /// <param name="inventory">The classification whose paths and dispositions are being closed out.</param>
    public static ResidualPublicationClosure From(ResidualSourceInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        List<ResidualPublicationDecision> decisions = [];
        foreach (ResidualSourceRecord file in inventory.Files)
        {
            (ResidualPublicationOutcome outcome, string reason) = file.Disposition switch
            {
                SourceRecordDisposition.Imported => (ResidualPublicationOutcome.Published,
                    "The documented inventory imports this path, so a consumer claims it."),
                SourceRecordDisposition.Unused => (ResidualPublicationOutcome.UnpublishedNoConsumer,
                    $"Read by {file.Reader}, and no consumer names it, so publishing it would be an artifact nothing asks for."),
                SourceRecordDisposition.Unresolved => (ResidualPublicationOutcome.UnpublishedNoReader,
                    "Nothing in this repository reads it, so there is nothing to publish."),
                SourceRecordDisposition.Malformed => (ResidualPublicationOutcome.UnpublishedNoReader,
                    $"The reader its family names refused it: {file.Note}"),
                SourceRecordDisposition.RequiredPending => (ResidualPublicationOutcome.Published,
                    "The documented inventory requires this path, so a consumer claims it."),
                SourceRecordDisposition.Duplicate or SourceRecordDisposition.Excluded or SourceRecordDisposition.SourceGap or SourceRecordDisposition.None =>
                    (ResidualPublicationOutcome.UnpublishedNoConsumer,
                        $"The documented inventory records this path as {file.Disposition}, which names no artifact to emit."),
                _ => throw new InvalidOperationException($"Classification '{file.Disposition}' has no publication decision, so its path would be published or dropped by accident."),
            };
            decisions.Add(new ResidualPublicationDecision(file.Path, file.Family, file.Reader, file.Disposition, outcome, reason));
        }

        return new ResidualPublicationClosure(inventory.Source, decisions);
    }
}
