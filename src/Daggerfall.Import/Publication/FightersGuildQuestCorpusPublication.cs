using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>Publishes the exact active Fighters Guild selection without treating textual parsing as runtime support.</summary>
public static class FightersGuildQuestCorpusPublication
{
    public const string Group = "FightersGuild";
    public const string PayloadFileName = "daggerfall.quests.fighters.json";

    public static readonly IReadOnlyList<string> RequiredQuestNames =
    [
        "M0C00Y11", "M0C00Y12", "M0C00Y13", "M0C00Y14", "M0B00Y00", "M0B00Y06", "M0B00Y07", "M0B00Y15", "M0B00Y16", "M0B00Y17",
        "M0B1XY01", "M0B11Y18", "M0B20Y02", "M0B21Y19", "M0B30Y03", "M0B30Y04", "M0B30Y08", "M0B40Y05", "M0B50Y09", "M0B60Y10",
    ];

    public static DaggerfallFightersGuildQuestCorpus Create(
        DaggerfallQuestCatalog catalog,
        DaggerfallQuestPack sources,
        DaggerfallQuestOriginalSourceSet originals)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(originals);

        DaggerfallQuestCatalogRow[] rows = [.. catalog.Rows.Where(row => row.Active && row.Group == Group)];
        if (!rows.Select(row => row.Name).SequenceEqual(RequiredQuestNames, StringComparer.Ordinal))
            throw new InvalidOperationException("The active FightersGuild catalog membership does not match the retained twenty-record corpus.");

        Dictionary<string, DaggerfallQuestRecord> byName = sources.Quests
            .Where(quest => RequiredQuestNames.Contains(quest.Name, StringComparer.Ordinal))
            .ToDictionary(quest => quest.Name, StringComparer.Ordinal);
        Dictionary<string, DaggerfallQuestOriginalSource> originalsByStem = originals.Quests
            .Where(source => RequiredQuestNames.Contains(source.Stem, StringComparer.Ordinal))
            .ToDictionary(source => source.Stem, StringComparer.Ordinal);
        List<DaggerfallFightersGuildQuestReceipt> receipts = [];
        foreach (DaggerfallQuestCatalogRow row in rows)
        {
            if (row.Membership is null || row.SourceDisposition != "present")
                throw new InvalidOperationException($"Fighters Guild quest '{row.Name}' has incomplete catalog membership or no source file.");
            if (!byName.TryGetValue(row.Name, out DaggerfallQuestRecord? source)
                || !string.Equals(source.SourceFile, row.Name + ".txt", StringComparison.Ordinal))
                throw new InvalidOperationException($"Fighters Guild quest '{row.Name}' does not resolve to its exact text source file.");
            if (!originalsByStem.TryGetValue(row.Name, out DaggerfallQuestOriginalSource? original))
                throw new InvalidOperationException($"Fighters Guild quest '{row.Name}' has no #8008 original-source comparison.");

            source.Validate();

            DaggerfallFightersGuildQuestReceipt receipt = new(row.Name, source.SourceFile, row.Membership, row.MinimumRequirement,
                row.RequirementKind, row.Adult, row.OneTime, row.SourceLine, row.SourceDisposition, source.Disposition.ToString().ToLowerInvariant(),
                original.Selection.ToString().ToLowerInvariant(), SourceFingerprint(source), ActionLines(source), source.Diagnostics);
            receipt.Validate();
            receipts.Add(receipt);
        }

        DaggerfallFightersGuildQuestCorpus unsigned = new(catalog.Source, receipts, default);
        DaggerfallFightersGuildQuestCorpus result = unsigned with { Fingerprint = Fingerprint(unsigned) };
        result.Validate();
        return result;
    }

    public static byte[] Serialize(DaggerfallFightersGuildQuestCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        corpus.Validate();
        return JsonSerializer.SerializeToUtf8Bytes(corpus, PublishedJson.Section);
    }

    private static ContentDigest SourceFingerprint(DaggerfallQuestRecord source) =>
        ContentDigest.Compute(JsonSerializer.SerializeToUtf8Bytes(source, PublishedJson.SectionCompact));

    private static ContentDigest Fingerprint(DaggerfallFightersGuildQuestCorpus corpus) =>
        ContentDigest.Compute(JsonSerializer.SerializeToUtf8Bytes(corpus with { Fingerprint = default }, PublishedJson.SectionCompact));

    private static IReadOnlyList<DaggerfallQuestActionLine> ActionLines(DaggerfallQuestRecord source) =>
        [.. source.Blocks.Where(block => block.Kind is QuestBlockKind.Headless or QuestBlockKind.Task or QuestBlockKind.Variable or QuestBlockKind.Global)
            .SelectMany(block => block.Lines.Skip(block.Kind == QuestBlockKind.Headless ? 0 : 1)
                .Select((text, offset) => new DaggerfallQuestActionLine(block.FirstLine + offset + (block.Kind == QuestBlockKind.Headless ? 0 : 1), text)))];
}

/// <summary>One scoped corpus receipt. Runtime computes readiness through its actual compiler.</summary>
public sealed record DaggerfallFightersGuildQuestCorpus(
    ImportPublicationSource CatalogSource,
    IReadOnlyList<DaggerfallFightersGuildQuestReceipt> Quests,
    ContentDigest Fingerprint)
{
    public void Validate()
    {
        CatalogSource.Validate();
        if (!Quests.Select(quest => quest.Name).SequenceEqual(FightersGuildQuestCorpusPublication.RequiredQuestNames, StringComparer.Ordinal))
            throw new InvalidOperationException("The Fighters Guild receipt does not retain the exact catalog order and membership.");
        foreach (DaggerfallFightersGuildQuestReceipt quest in Quests) quest.Validate();
        Fingerprint.Validate();
    }
}

/// <summary>Catalog membership, source comparison selection, and action-line provenance for one selected script.</summary>
public sealed record DaggerfallFightersGuildQuestReceipt(
    string Name,
    string SourceFile,
    string Membership,
    int MinimumRequirement,
    string RequirementKind,
    bool Adult,
    bool OneTime,
    int CatalogSourceLine,
    string CatalogSourceDisposition,
    string SourceDisposition,
    string OriginalSelection,
    ContentDigest SourceFingerprint,
    IReadOnlyList<DaggerfallQuestActionLine> ActionLines,
    IReadOnlyList<DaggerfallQuestDiagnostic> SourceDiagnostics)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Name, nameof(Name));
        NormalizedImportDocument.RequireLogicalPath(SourceFile, nameof(SourceFile));
        if (string.IsNullOrWhiteSpace(Membership) || MinimumRequirement < 0 || CatalogSourceLine <= 0
            || string.IsNullOrWhiteSpace(RequirementKind) || string.IsNullOrWhiteSpace(CatalogSourceDisposition) || string.IsNullOrWhiteSpace(SourceDisposition) || string.IsNullOrWhiteSpace(OriginalSelection))
            throw new ArgumentException($"Fighters Guild receipt '{Name}' has incomplete membership or source disposition.");
        SourceFingerprint.Validate();
        ArgumentNullException.ThrowIfNull(ActionLines);
        foreach (DaggerfallQuestActionLine line in ActionLines) line.Validate();
        ArgumentNullException.ThrowIfNull(SourceDiagnostics);
        foreach (DaggerfallQuestDiagnostic diagnostic in SourceDiagnostics) diagnostic.Validate();
    }
}

/// <summary>An ordered executable task-body line. It intentionally has no importer-side support verdict.</summary>
public sealed record DaggerfallQuestActionLine(int Line, string Text)
{
    public void Validate()
    {
        if (Line <= 0 || string.IsNullOrWhiteSpace(Text)) throw new ArgumentException("Quest action provenance has no usable source line.");
    }
}
