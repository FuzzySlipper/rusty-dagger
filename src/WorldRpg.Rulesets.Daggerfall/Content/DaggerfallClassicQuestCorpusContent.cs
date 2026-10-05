using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// One runtime reader for retained classic corpus packs. The corpus states its own categories; the reader
/// verifies each against the admitted catalog, verifies each receipt against its catalog row, and uses the
/// current compilers for readiness. No corpus is known by name, so a bundle may select any of them.
/// </summary>
internal static class DaggerfallClassicQuestCorpusContent
{
    /// <summary>A category offered through the ordinary guild and populace offer paths.</summary>
    internal const string Ordinary = "ordinary";
    /// <summary>A catalog entry the donor never offers.</summary>
    internal const string NotOffered = "notOffered";
    /// <summary>A catalog entry reachable only through an explicit Daedric summoning identity.</summary>
    internal const string SummonOnly = "summonOnly";

    internal static IReadOnlyList<DaggerfallClassicQuestCorpusReceipt> Read(ProductContent content, ReadOnlyMemory<byte> payload,
        DaggerfallDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(content); ArgumentNullException.ThrowIfNull(definitions);
        using JsonDocument document = JsonDocument.Parse(payload);
        string corpusId = document.RootElement.GetProperty("id").GetString() is { Length: > 0 } id && !string.IsNullOrWhiteSpace(id)
            ? id : throw new DaggerfallContentException(["Classic quest corpus does not identify itself."]);
        Dictionary<string, (DaggerfallQuestCatalogRow Row, int Index)> catalog = new(StringComparer.Ordinal);
        foreach ((DaggerfallQuestCatalogRow row, int index) in definitions.QuestSources.Catalog.Rows.Select((row, index) => (row, index)))
            catalog.TryAdd(row.Name, (row, index));
        List<DaggerfallClassicQuestCorpusReceipt> result = [];
        Dictionary<string, JsonElement> categories = new(StringComparer.Ordinal);
        foreach (JsonElement category in document.RootElement.GetProperty("categories").EnumerateArray())
        {
            string categoryId = category.GetProperty("id").GetString()!;
            string group = category.GetProperty("catalogGroup").GetString()!;
            bool active = category.GetProperty("active").GetBoolean();
            // The availability is what the offer paths act on, so one the ruleset cannot interpret would be
            // offered or withheld by accident rather than by the corpus's statement.
            if (category.GetProperty("availability").GetString() is not (Ordinary or NotOffered or SummonOnly))
                throw new DaggerfallContentException([$"Classic quest corpus '{corpusId}' category '{categoryId}' names an availability the ruleset does not interpret."]);
            if (!categories.TryAdd(categoryId, category))
                throw new DaggerfallContentException([$"Classic quest corpus '{corpusId}' repeats category '{categoryId}'."]);
            // Every name is a present catalog row of the category's own group and activity, in catalog order.
            int previous = -1;
            foreach (JsonElement value in category.GetProperty("names").EnumerateArray())
            {
                if (value.GetString() is not { } name || !catalog.TryGetValue(name, out (DaggerfallQuestCatalogRow Row, int Index) entry)
                    || entry.Row.Group != group || entry.Row.Active != active || entry.Row.SourceDisposition != "present" || entry.Index <= previous)
                    throw new DaggerfallContentException([$"Classic quest corpus '{corpusId}' does not retain category '{categoryId}' exactly."]);
                previous = entry.Index;
            }
        }
        // Categories that claim one catalog selection partition it: together they name each of its present
        // entries exactly once, so no entry of a claimed group is silently left out of the corpus.
        foreach (IGrouping<(string Group, bool Active), JsonElement> claimed in categories.Values
            .GroupBy(category => (category.GetProperty("catalogGroup").GetString()!, category.GetProperty("active").GetBoolean())))
        {
            string[] catalogNames = [.. definitions.QuestSources.Catalog.Rows
                .Where(row => row.Group == claimed.Key.Group && row.Active == claimed.Key.Active && row.SourceDisposition == "present")
                .Select(row => row.Name).Order(StringComparer.Ordinal)];
            string[] partitionedNames = [.. claimed.SelectMany(category => category.GetProperty("names").EnumerateArray().Select(value => value.GetString()!)).Order(StringComparer.Ordinal)];
            if (!catalogNames.SequenceEqual(partitionedNames, StringComparer.Ordinal))
                throw new DaggerfallContentException([$"Classic quest corpus '{corpusId}' does not agree with the admitted {claimed.Key.Group} catalog selection."]);
        }
        HashSet<string> names = new(StringComparer.Ordinal), files = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> categoryNames = categories.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (JsonElement receipt in document.RootElement.GetProperty("quests").EnumerateArray())
        {
            string name = receipt.GetProperty("name").GetString()!;
            string sourceFile = receipt.GetProperty("sourceFile").GetString()!;
            string categoryId = receipt.GetProperty("category").GetString()!;
            if (!categories.TryGetValue(categoryId, out JsonElement category)) throw new DaggerfallContentException([$"Classic quest receipt '{sourceFile}' names an unpublished category '{categoryId}'."]);
            if (!names.Add(name) || !files.Add(sourceFile)) throw new DaggerfallContentException(["Classic quest corpus repeats a source identity."]);
            categoryNames[categoryId].Add(name);
            DaggerfallQuestCatalogRow row = catalog.TryGetValue(name, out (DaggerfallQuestCatalogRow Row, int Index) catalogEntry)
                ? catalogEntry.Row : throw new DaggerfallContentException([$"Classic quest receipt '{sourceFile}' names no admitted catalog row."]);
            if (!string.Equals(sourceFile, name + ".txt", StringComparison.Ordinal)
                || !string.Equals(receipt.GetProperty("catalogGroup").GetString(), row.Group, StringComparison.Ordinal)
                || !string.Equals(receipt.GetProperty("membership").GetString(), row.Membership, StringComparison.Ordinal)
                || receipt.GetProperty("minimumRequirement").GetInt32() != row.MinimumRequirement
                || !string.Equals(receipt.GetProperty("requirementKind").GetString(), row.RequirementKind, StringComparison.Ordinal)
                || receipt.GetProperty("adult").GetBoolean() != row.Adult || receipt.GetProperty("oneTime").GetBoolean() != row.OneTime
                || receipt.GetProperty("active").GetBoolean() != row.Active || receipt.GetProperty("catalogSourceLine").GetInt32() != row.SourceLine
                || !string.Equals(receipt.GetProperty("catalogSourceDisposition").GetString(), row.SourceDisposition, StringComparison.Ordinal))
                throw new DaggerfallContentException([$"Classic quest receipt '{sourceFile}' does not agree with its admitted catalog row."]);
            string availability = receipt.GetProperty("availability").GetString()!;
            if (!string.Equals(category.GetProperty("catalogGroup").GetString(), row.Group, StringComparison.Ordinal)
                || category.GetProperty("active").GetBoolean() != row.Active
                || !string.Equals(category.GetProperty("availability").GetString(), availability, StringComparison.Ordinal)
                || !category.GetProperty("names").EnumerateArray().Select(value => value.GetString()).Contains(name, StringComparer.Ordinal))
                throw new DaggerfallContentException([$"Classic quest receipt '{sourceFile}' does not agree with category '{categoryId}'."]);
            DaggerfallQuestSourceDefinition source = definitions.QuestSources.Resolve(sourceFile);
            if (!string.Equals(source.Name, name, StringComparison.Ordinal) || !string.Equals(receipt.GetProperty("sourceDisposition").GetString(), source.Disposition.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new DaggerfallContentException([$"Classic quest receipt '{sourceFile}' does not agree with its normalized source."]);
            string[] published = [.. receipt.GetProperty("actionLines").EnumerateArray().Select(line => $"{line.GetProperty("line").GetInt32()}\0{line.GetProperty("text").GetString()}")];
            string[] actual = [.. ActionLines(source).Select(line => $"{line.Line}\0{line.Text}")];
            if (!published.SequenceEqual(actual)) throw new DaggerfallContentException([$"Classic quest receipt '{sourceFile}' does not retain exact action provenance."]);
            string[] publishedSourceDiagnostics = [.. receipt.GetProperty("sourceDiagnostics").EnumerateArray()
                .Select(diagnostic => $"{diagnostic.GetProperty("line").GetInt32()}\0{diagnostic.GetProperty("text").GetString()}\0{diagnostic.GetProperty("reason").GetString()}")];
            string[] actualSourceDiagnostics = [.. source.Diagnostics
                .Select(diagnostic => $"{diagnostic.Line}\0{diagnostic.Text}\0{diagnostic.Reason}")];
            if (!publishedSourceDiagnostics.SequenceEqual(actualSourceDiagnostics)) throw new DaggerfallContentException([$"Classic quest receipt '{sourceFile}' does not retain exact source diagnostics."]);
            List<DaggerfallQuestDiagnosticDefinition> diagnostics = [.. source.Diagnostics, .. DaggerfallQuestTaskCompiler.Assess(source)];
            try { _ = DaggerfallQuestClockCompiler.Compile(source); }
            catch (ArgumentException exception) { diagnostics.Add(new(1, sourceFile, exception.Message)); }
            if (availability == NotOffered) diagnostics.Add(new(1, sourceFile, "Disabled classic quest entries are not ordinary offers."));
            result.Add(new(availability, new(name, sourceFile, diagnostics.Count == 0, diagnostics)));
        }
        foreach ((string categoryId, JsonElement category) in categories)
        {
            string[] expected = [.. category.GetProperty("names").EnumerateArray().Select(value => value.GetString()!)];
            if (!categoryNames[categoryId].SequenceEqual(expected, StringComparer.Ordinal))
                throw new DaggerfallContentException([$"Classic quest corpus category '{categoryId}' does not retain its exact ordered selection."]);
        }
        return result;
    }

    private static IEnumerable<(int Line, string Text)> ActionLines(DaggerfallQuestSourceDefinition source) => source.Blocks.Where(block => block.Kind is "headless" or "task" or "variable" or "global").SelectMany(block => block.Lines.Skip(block.Kind == "headless" ? 0 : 1).Select((text, index) => (block.FirstLine + index + (block.Kind == "headless" ? 0 : 1), text)));
}

/// <summary>One admitted classic corpus receipt with the availability its category states.</summary>
internal sealed record DaggerfallClassicQuestCorpusReceipt(string Availability, DaggerfallFightersGuildQuestRuntimeReceipt Runtime)
{
    internal bool IsOrdinaryOffer => Availability == DaggerfallClassicQuestCorpusContent.Ordinary;
}
