using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>One exact classic quest selection; category labels preserve donor offer meaning without becoming executable policy.</summary>
public static class ClassicQuestCorpusPublication
{
    public static readonly IReadOnlyList<DaggerfallClassicQuestCorpusSpecification> Specifications =
    [
        new("mages", [new("mages", "MagesGuild", true, "ordinary", ["N0C00Y10", "N0C00Y11", "N0C00Y12", "N0C00Y13", "N0B00Y04", "N0B00Y06", "N0B00Y08", "N0B00Y09", "N0B00Y16", "N0B00Y17", "N0B10Y01", "N0B10Y03", "N0B11Y18", "N0B20Y02", "N0B20Y05", "N0B21Y14", "N0B30Y15", "N0B40Y07"])]),
        new("temples", [new("general", "HolyOrder", true, "ordinary", ["C0C00Y10", "C0C00Y11", "C0C00Y12", "C0C00Y13", "C0B00Y00", "C0B00Y01", "C0B00Y02", "C0B00Y03", "C0B00Y04", "C0B00Y14", "C0B10Y05", "C0B10Y06", "C0B10Y07", "C0B10Y15", "C0B20Y08", "C0B3XY09"]), new("specific", "HolyOrder", true, "ordinary", ["00B00Y00", "D0B00Y00", "E0B00Y00", "F0B00Y00", "G0B00Y00", "H0B00Y00", "I0B00Y00", "J0B00Y00"])]),
        new("social", [new("populace", "GeneralPopulace", true, "ordinary", ["O0A0AL00", "O0B00Y00", "O0B00Y01", "O0B00Y11", "O0B00Y12", "O0B10Y00", "O0B10Y03", "O0B10Y05", "O0B10Y06", "O0B10Y07", "O0B20Y02", "O0B2XY04", "O0B2XY08", "O0B2XY09", "O0B2XY10"]), new("brotherhood", "DarkBrotherHood", true, "ordinary", ["L0A01L00", "L0B00Y00", "L0B00Y01", "L0B00Y02", "L0B00Y03", "L0B10Y01", "L0B10Y03", "L0B20Y02", "L0B30Y03", "L0B30Y09", "L0B40Y04", "L0B50Y11", "L0B60Y10"]), new("knightly", "KnightlyOrder", true, "ordinary", ["B0C00Y05", "B0C00Y06", "B0C00Y10", "B0C00Y13", "B0B00Y00", "B0B00Y01", "B0B10Y04", "B0B20Y07", "B0B40Y08", "B0B40Y09", "B0B50Y11", "B0B60Y12", "B0B70Y14", "B0B70Y16", "B0B71Y03", "B0B80Y17", "B0B81Y02"])]),
        new("witches-commoners", [new("witches", "Witches", true, "ordinary", ["Q0C00Y01", "Q0C00Y03", "Q0C00Y04", "Q0C00Y06", "Q0C00Y07", "Q0C00Y08", "Q0C0XY02", "Q0C10Y00", "Q0C20Y02", "Q0C4XY04"]), new("commoners", "Commoners", true, "ordinary", ["A0C00Y00", "A0C00Y06", "A0C00Y07", "A0C00Y08", "A0C00Y10", "A0C00Y11", "A0C00Y12", "A0C00Y14", "A0C00Y15", "A0C00Y16", "A0C00Y17", "A0C01Y01", "A0C01Y03", "A0C01Y06", "A0C01Y09", "A0C01Y13", "A0C0XY04", "A0C10Y02", "A0C10Y05", "A0C41Y18"])]),
        new("merchants-vampires", [new("merchants", "Merchants", true, "ordinary", ["K0C00Y00", "K0C00Y02", "K0C00Y03", "K0C00Y04", "K0C00Y05", "K0C00Y07", "K0C00Y08", "K0C00Y09", "K0C01Y00", "K0C01Y10", "K0C0XY01", "K0C30Y03"]), new("vampires", "Vampires", true, "ordinary", ["P0A01L00", "P0B00L01", "P0B00L03", "P0B00L04", "P0B00L06", "P0B01L02", "P0B10L07", "P0B10L08", "P0B10L10", "P0B20L09"])]),
        new("disabled", [new("disabled-merchant", "Merchants", false, "notOffered", ["K0C00Y06"]), new("disabled-nobility", "Nobility", false, "notOffered", ["R0C11Y27"]), new("daedric-summon", "Oblivion", false, "summonOnly", ["10C00Y00", "20C00Y00", "30C00Y00", "40C00Y00", "50C00Y00", "60C00Y00", "70C00Y00", "80C0XY00", "90C00Y00", "T0C00Y00", "U0C00Y00", "V0C00Y00", "W0C00Y00", "X0C00Y00", "Y0C00Y00", "Z0C00Y00"])]),
        new("nobility", [new("nobility", "Nobility", true, "ordinary", ["R0C10Y00", "R0C10Y01", "R0C10Y02", "R0C10Y04", "R0C10Y05", "R0C10Y06", "R0C10Y08", "R0C10Y09", "R0C10Y10", "R0C10Y11", "R0C10Y12", "R0C10Y13", "R0C10Y14", "R0C10Y15", "R0C10Y17", "R0C10Y18", "R0C10Y20", "R0C10Y21", "R0C11Y03", "R0C11Y16", "R0C11Y19", "R0C11Y26", "R0C11Y28", "R0C20Y07", "R0C20Y22", "R0C30Y25", "R0C4XY23", "R0C60Y24"])]),
    ];

    public static DaggerfallClassicQuestCorpus Create(string id, DaggerfallQuestCatalog catalog, DaggerfallQuestPack sources, DaggerfallQuestOriginalSourceSet originals)
    {
        DaggerfallClassicQuestCorpusSpecification specification = Specifications.Single(value => value.Id == id);
        HashSet<string> required = [.. specification.Categories.SelectMany(category => category.Names)];
        Dictionary<string, DaggerfallQuestCatalogRow> rows = catalog.Rows.Where(row => required.Contains(row.Name)).ToDictionary(row => row.Name, StringComparer.Ordinal);
        Dictionary<string, DaggerfallQuestRecord> sourceByName = sources.Quests.Where(source => required.Contains(source.Name)).ToDictionary(source => source.Name, StringComparer.Ordinal);
        Dictionary<string, DaggerfallQuestOriginalSource> originalsByStem = originals.Quests.Where(source => required.Contains(source.Stem)).ToDictionary(source => source.Stem, StringComparer.Ordinal);
        List<DaggerfallClassicQuestReceipt> receipts = [];
        foreach (IGrouping<(string Group, bool Active), DaggerfallClassicQuestCategory> categories in specification.Categories.GroupBy(category => (category.CatalogGroup, category.Active)))
        {
            string[] expected = [.. catalog.Rows.Where(row => row.Group == categories.Key.Group && row.Active == categories.Key.Active && row.SourceDisposition == "present").Select(row => row.Name)];
            string[] selected = [.. categories.SelectMany(category => category.Names)];
            if (!expected.SequenceEqual(selected, StringComparer.Ordinal)) throw new InvalidOperationException($"Classic quest corpus '{id}' does not retain every source-backed {categories.Key.Group} catalog row.");
        }
        foreach (DaggerfallClassicQuestCategory category in specification.Categories)
        {
            DaggerfallQuestCatalogRow[] actual = [.. catalog.Rows.Where(row => row.Group == category.CatalogGroup && row.Active == category.Active && row.SourceDisposition == "present" && category.Names.Contains(row.Name, StringComparer.Ordinal))];
            if (!actual.Select(row => row.Name).SequenceEqual(category.Names, StringComparer.Ordinal)) throw new InvalidOperationException($"Classic quest corpus '{id}' category '{category.Id}' does not retain its exact catalog selection.");
            foreach (string name in category.Names)
            {
                DaggerfallQuestCatalogRow row = rows[name];
                DaggerfallQuestRecord source = sourceByName[name];
                DaggerfallQuestOriginalSource original = originalsByStem[name];
                source.Validate();
                receipts.Add(new(category.Id, category.Availability, row.Name, source.SourceFile, row.Group, row.Membership, row.MinimumRequirement, row.RequirementKind, row.Adult, row.OneTime, row.Active, row.SourceLine, row.SourceDisposition, source.Disposition.ToString().ToLowerInvariant(), original.Selection.ToString().ToLowerInvariant(),
                    ContentDigest.Compute(JsonSerializer.SerializeToUtf8Bytes(source, PublishedJson.SectionCompact)), ActionLines(source), source.Diagnostics));
            }
        }
        DaggerfallClassicQuestCorpus unsigned = new(id, catalog.Source, specification.Categories, receipts, default);
        DaggerfallClassicQuestCorpus corpus = unsigned with { Fingerprint = ContentDigest.Compute(JsonSerializer.SerializeToUtf8Bytes(unsigned, PublishedJson.SectionCompact)) };
        corpus.Validate();
        return corpus;
    }

    public static byte[] Serialize(DaggerfallClassicQuestCorpus corpus) { corpus.Validate(); return JsonSerializer.SerializeToUtf8Bytes(corpus, PublishedJson.Section); }
    private static IReadOnlyList<DaggerfallQuestActionLine> ActionLines(DaggerfallQuestRecord source) => [.. source.Blocks.Where(block => block.Kind is QuestBlockKind.Headless or QuestBlockKind.Task or QuestBlockKind.Variable or QuestBlockKind.Global).SelectMany(block => block.Lines.Skip(block.Kind == QuestBlockKind.Headless ? 0 : 1).Select((text, offset) => new DaggerfallQuestActionLine(block.FirstLine + offset + (block.Kind == QuestBlockKind.Headless ? 0 : 1), text)))];
}

public sealed record DaggerfallClassicQuestCorpusSpecification(string Id, IReadOnlyList<DaggerfallClassicQuestCategory> Categories);
public sealed record DaggerfallClassicQuestCategory(string Id, string CatalogGroup, bool Active, string Availability, IReadOnlyList<string> Names);
public sealed record DaggerfallClassicQuestCorpus(string Id, ImportPublicationSource CatalogSource, IReadOnlyList<DaggerfallClassicQuestCategory> Categories, IReadOnlyList<DaggerfallClassicQuestReceipt> Quests, ContentDigest Fingerprint)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id)); CatalogSource.Validate(); Fingerprint.Validate();
        if (Categories.Count == 0 || Quests.Count == 0) throw new ArgumentException("Classic quest corpus needs categories and receipts.");
        if (Quests.Select(quest => quest.Name).Distinct(StringComparer.Ordinal).Count() != Quests.Count) throw new ArgumentException("Classic quest corpus repeats a quest.");
        foreach (DaggerfallClassicQuestReceipt quest in Quests) quest.Validate();
    }
}
public sealed record DaggerfallClassicQuestReceipt(string Category, string Availability, string Name, string SourceFile, string CatalogGroup, string? Membership, int MinimumRequirement, string RequirementKind, bool Adult, bool OneTime, bool Active, int CatalogSourceLine, string CatalogSourceDisposition, string SourceDisposition, string OriginalSelection, ContentDigest SourceFingerprint, IReadOnlyList<DaggerfallQuestActionLine> ActionLines, IReadOnlyList<DaggerfallQuestDiagnostic> SourceDiagnostics)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Category) || string.IsNullOrWhiteSpace(Availability) || string.IsNullOrWhiteSpace(CatalogGroup) || MinimumRequirement < 0 || CatalogSourceLine <= 0) throw new ArgumentException("Classic quest receipt has incomplete category or catalog metadata.");
        NormalizedImportDocument.RequireLogicalId(Name, nameof(Name)); NormalizedImportDocument.RequireLogicalPath(SourceFile, nameof(SourceFile)); SourceFingerprint.Validate();
        foreach (DaggerfallQuestActionLine line in ActionLines) line.Validate(); foreach (DaggerfallQuestDiagnostic diagnostic in SourceDiagnostics) diagnostic.Validate();
    }
}
