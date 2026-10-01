using System.Text.Json;

namespace Daggerfall.Import.Publication;

/// <summary>A source citation in a concrete artifact; archive context is separate from record evidence.</summary>
public sealed record SourceCoverageConsumer(string Artifact, string Section, string SourcePath, int? ArchiveOrdinal = null,
    string? LedgerDisposition = null, bool OfflineOnly = false, bool ContextOnly = false);

public sealed record SourceCoverageRecord(SourceManifestRecord Source, SourceRecordDisposition Disposition,
    IReadOnlyList<SourceCoverageConsumer> Consumers, IReadOnlyList<SourceCoverageConsumer> ArchiveContext,
    bool HasRecordEvidence, string Detail);

public sealed record SourceCoverageQuestHandoff(string Stem, string Selection, string Availability,
    string? BinaryPath, string? ResourcePath, string? RewrittenSource, IReadOnlyList<string> CorpusArtifacts);

public sealed record SourceCoverageFamilyEvidence(string FamilyId, string DocumentedDisposition,
    IReadOnlyList<string> SourcePaths, IReadOnlyList<SourceCoverageConsumer> Consumers);

public sealed record SourceCoverageUnsupported(string Artifact, string Family, IReadOnlyList<string> Files, string Reason);

/// <summary>Import coverage evidence, never certification that the game implements the source behavior.</summary>
public sealed record SourceCoverageReport(IReadOnlyList<SourceCoverageRecord> Records,
    IReadOnlyList<SourceManifestFamilyCount> Families, IReadOnlyList<SourceCoverageQuestHandoff> QuestHandoffs,
    IReadOnlyList<SourceCoverageUnsupported> Unsupported, IReadOnlyList<string> Errors,
    IReadOnlyList<SourceCoverageFamilyEvidence> FamilyEvidence)
{
    public bool IsReconciled => Errors.Count == 0;
    public bool RuntimeParityCertified => false;
}

/// <summary>Joins the existing source inventory and producer evidence without promoting file reads to record coverage.</summary>
public static class SourceCoverageReconciler
{
    public static SourceCoverageReport Build(SourceManifest manifest, IReadOnlyList<SourceCoverageConsumer> consumers,
        IReadOnlyList<SourceCoverageQuestHandoff>? quests = null, IReadOnlyList<SourceCoverageUnsupported>? unsupported = null,
        IReadOnlyList<SourceInventoryRow>? inventory = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(consumers);
        manifest.Validate();
        List<string> errors = [];
        List<SourceCoverageRecord> records = [];
        var familyRows = (inventory ?? []).Where(row => row.RowType == "family").ToArray();
        var byPath = consumers.Distinct().GroupBy(value => value.SourcePath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(value => value.Artifact, StringComparer.Ordinal)
                .ThenBy(value => value.Section, StringComparer.Ordinal).ThenBy(value => value.ArchiveOrdinal)
                .ThenBy(value => value.LedgerDisposition, StringComparer.Ordinal).ThenBy(value => value.OfflineOnly).ThenBy(value => value.ContextOnly).ToArray(), StringComparer.Ordinal);
        foreach (SourceManifestRecord source in manifest.Canonicalize().Records)
        {
            SourceCoverageConsumer[] pathConsumers = byPath.GetValueOrDefault(source.SourcePath, []);
            SourceCoverageConsumer[] direct = [.. pathConsumers.Where(value => value.ArchiveOrdinal == source.ArchiveOrdinal)];
            SourceCoverageConsumer[] context = source.ArchiveOrdinal is null ? [] : [.. pathConsumers.Where(value => value.ArchiveOrdinal is null)];
            SourceRecordDisposition disposition = source.Disposition;
            string detail = source.Note;
            if (disposition == SourceRecordDisposition.Unused && direct.Any(value => value.ContextOnly))
                disposition = SourceRecordDisposition.RequiredPending;
            if (disposition == SourceRecordDisposition.Imported && direct.Length != 0 && direct.All(value => value.OfflineOnly || value.ContextOnly))
            {
                disposition = SourceRecordDisposition.RequiredPending;
                detail = "Only offline or aggregate evidence is present; no normalized pack record is claimed. " + detail;
            }
            var decisions = direct.Where(value => value.LedgerDisposition is not null).Select(value => value.LedgerDisposition!).Distinct().ToArray();
            if (decisions.Length > 1)
                errors.Add($"{source.Id}: producer ledgers disagree: {string.Join(", ", decisions.Order(StringComparer.Ordinal))}.");
            if (decisions.Length == 1)
            {
                // These are dispositions from the existing geometry/block owners, not inferred from a filename.
                switch (decisions[0])
                {
                    case "unused": disposition = SourceRecordDisposition.Unused; break;
                    case "duplicate": disposition = SourceRecordDisposition.Duplicate; break;
                    case "malformed": disposition = SourceRecordDisposition.Malformed; break;
                    case "referenced":
                    case "summarized":
                    case "donorUnsupported":
                    case "unknownKind":
                        // An offline summary/use-site does not prove a runnable world artifact.
                        disposition = SourceRecordDisposition.RequiredPending;
                        break;
                    default: errors.Add($"{source.Id}: unsupported producer disposition '{decisions[0]}'."); break;
                }
                detail = $"Producer ledger: {decisions[0]}. {source.Note}";
            }
            if (source.FamilyId == "CNT-028" && (disposition != SourceRecordDisposition.Excluded || direct.Length != 0 || context.Length != 0))
                errors.Add($"{source.Id}: MIDI must remain excluded and have no consuming artifact.");
            if (disposition is SourceRecordDisposition.None or SourceRecordDisposition.Unresolved)
                errors.Add($"{source.Id}: no resolved source disposition.");
            if (disposition is SourceRecordDisposition.Imported or SourceRecordDisposition.RequiredPending
                && direct.Length == 0 && context.Length == 0)
                errors.Add($"{source.Id}: required/imported source has no consuming artifact reference.");
            records.Add(new(source, disposition, direct, context, direct.Any(value => !value.ContextOnly), detail));
        }
        var known = manifest.Records.Select(value => (value.SourcePath, value.ArchiveOrdinal)).ToHashSet();
        foreach (SourceCoverageConsumer consumer in consumers)
            if (!known.Contains((consumer.SourcePath, consumer.ArchiveOrdinal))
                && !familyRows.Any(row => row.PathOrPattern == consumer.SourcePath && !consumer.SourcePath.StartsWith("arena2/", StringComparison.Ordinal)))
                errors.Add($"{consumer.Artifact}/{consumer.Section}: source '{consumer.SourcePath}' record {consumer.ArchiveOrdinal} is absent from the admitted inventory.");
        var pathsByFamily = records.GroupBy(record => record.Source.FamilyId).ToDictionary(group => group.Key,
            group => group.Select(record => record.Source.SourcePath).ToHashSet(StringComparer.Ordinal));
        var familyCounts = manifest.Canonicalize().Families.Select(family => SourceManifestFamilyCount.From(family.FamilyId,
            family.DocumentedDisposition, records.Where(value => value.Source.FamilyId == family.FamilyId)
                .Select(value => value.Source with { Disposition = value.Disposition }))).ToArray();
        return new(records, familyCounts,
            [.. (quests ?? []).OrderBy(value => value.Stem, StringComparer.Ordinal)],
            [.. (unsupported ?? []).OrderBy(value => value.Artifact, StringComparer.Ordinal).ThenBy(value => value.Family, StringComparer.Ordinal)],
            [.. errors.Distinct().Order(StringComparer.Ordinal)],
            [.. familyRows.GroupBy(row => row.FamilyId).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group =>
                new SourceCoverageFamilyEvidence(group.Key, group.First().Disposition,
                    [.. group.Select(row => row.PathOrPattern).Distinct().Order(StringComparer.Ordinal)],
                    [.. consumers.Where(consumer => group.Any(row => row.PathOrPattern == consumer.SourcePath)
                        || pathsByFamily.TryGetValue(group.Key, out var paths) && paths.Contains(consumer.SourcePath))
                        .Distinct().OrderBy(consumer => consumer.Artifact, StringComparer.Ordinal).ThenBy(consumer => consumer.Section, StringComparer.Ordinal)
                        .ThenBy(consumer => consumer.SourcePath, StringComparer.Ordinal).ThenBy(consumer => consumer.ArchiveOrdinal).ThenBy(consumer => consumer.LedgerDisposition, StringComparer.Ordinal)
                        .ThenBy(consumer => consumer.OfflineOnly).ThenBy(consumer => consumer.ContextOnly)]))]);
    }

    public static byte[] Serialize(SourceCoverageReport report) =>
        [.. JsonSerializer.SerializeToUtf8Bytes(report, PublishedJson.Section), (byte)'\n'];
}
