namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// One native item template target in the classic 288-entry space: the donor groups whose
/// enumerations name it, the provenance of that attribution, and its disposition. A target
/// stays unresolved while the native source is absent, and it carries no name, weight,
/// price or damage because those live in that source.
/// </summary>
internal sealed record DaggerfallItemTemplateTarget(
    int Index,
    IReadOnlyList<string> DonorGroups,
    string Provenance,
    string Disposition);

/// <summary>
/// The item-template ledger the base pack carries: what the native source is, what state it
/// is in, and every target with its provenance and disposition. The ledger is what lets a
/// reader tell a migrated item value from a decoded native template fact.
/// </summary>
internal sealed record DaggerfallItemTemplateLedger(
    string SourceRecordId,
    string SourcePath,
    string SourceStatus,
    string SourceReason,
    string BaselineRule,
    string BaselinePath,
    int PublishedItemCount,
    string PublishedValueProvenance,
    bool PublishedNativeDecoding,
    int DeclaredTargets,
    int DeclaredReferenced,
    int DeclaredUnreferenced,
    int DeclaredNativeTemplatesDecoded,
    IReadOnlyList<DaggerfallItemTemplateTarget> Targets)
{
    /// <summary>The classic native template count every ledger must cover.</summary>
    public const int TargetCount = 288;

    /// <summary>The status a native source has while it is not supplied.</summary>
    public const string AbsentStatus = "absent";

    /// <summary>The disposition every target carries while no native fact is decoded.</summary>
    public const string UnresolvedDisposition = "unresolved";

    /// <summary>Whether the native source is supplied and decoded.</summary>
    public bool NativeSourceSupplied => !string.Equals(SourceStatus, AbsentStatus, StringComparison.Ordinal);
}
