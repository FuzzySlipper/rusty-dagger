using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>One runtime reader for retained classic corpus packs. It verifies each receipt against the admitted catalog and uses the current compilers for readiness.</summary>
internal static class DaggerfallClassicQuestCorpusContent
{
    internal static IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> Read(ProductContent content, ReadOnlyMemory<byte> payload,
        DaggerfallDefinitions definitions, DaggerfallClassicQuestCorpusExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(content); ArgumentNullException.ThrowIfNull(definitions); ArgumentNullException.ThrowIfNull(expectation);
        using JsonDocument document = JsonDocument.Parse(payload);
        if (!string.Equals(document.RootElement.GetProperty("id").GetString(), expectation.Id, StringComparison.Ordinal))
            throw new DaggerfallContentException([$"Classic quest corpus does not identify itself as '{expectation.Id}'."]);
        List<DaggerfallFightersGuildQuestRuntimeReceipt> result = [];
        Dictionary<string, JsonElement> categories = document.RootElement.GetProperty("categories").EnumerateArray()
            .ToDictionary(category => category.GetProperty("id").GetString()!, StringComparer.Ordinal);
        if (categories.Count != expectation.Categories.Count) throw new DaggerfallContentException([$"Classic quest corpus '{expectation.Id}' does not retain its exact category count."]);
        foreach (DaggerfallClassicQuestCategoryExpectation expected in expectation.Categories)
        {
            string[] catalogNames = CategoryNames(definitions, expected);
            if (!categories.TryGetValue(expected.Id, out JsonElement category)
                || !string.Equals(category.GetProperty("catalogGroup").GetString(), expected.CatalogGroup, StringComparison.Ordinal)
                || category.GetProperty("active").GetBoolean() != expected.Active
                || !string.Equals(category.GetProperty("availability").GetString(), expected.Availability, StringComparison.Ordinal)
                || !category.GetProperty("names").EnumerateArray().Select(value => value.GetString()).SequenceEqual(catalogNames, StringComparer.Ordinal))
                throw new DaggerfallContentException([$"Classic quest corpus '{expectation.Id}' does not retain category '{expected.Id}' exactly."]);
        }
        foreach (IGrouping<(string Group, bool Active), DaggerfallClassicQuestCategoryExpectation> expected in expectation.Categories.GroupBy(category => (category.CatalogGroup, category.Active)))
        {
            string[] catalogNames = [.. definitions.QuestSources.Catalog.Rows
                .Where(row => row.Group == expected.Key.Group && row.Active == expected.Key.Active && row.SourceDisposition == "present")
                .Select(row => row.Name)];
            string[] partitionedNames = [.. expected.SelectMany(category => CategoryNames(definitions, category))];
            if (!catalogNames.SequenceEqual(partitionedNames, StringComparer.Ordinal))
                throw new DaggerfallContentException([$"Classic quest corpus '{expectation.Id}' does not agree with the admitted {expected.Key.Group} catalog selection."]);
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
            DaggerfallQuestCatalogRow row = definitions.QuestSources.Catalog.Rows.Single(row => row.Name == name);
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
            if (availability == "notOffered") diagnostics.Add(new(1, sourceFile, "Disabled classic quest entries are not ordinary offers."));
            if (name == "R0C11Y28")
            {
                (int Line, string Text) macro = MessageLines(source).FirstOrDefault(value => value.Text.Contains("%vcn", StringComparison.OrdinalIgnoreCase));
                if (macro.Text is null) throw new DaggerfallContentException([$"Classic quest receipt '{sourceFile}' lost its retained %vcn macro context."]);
                diagnostics.Add(new(macro.Line, macro.Text, "The NPC vampire-clan macro requires quest-NPC context; #8074 owns that resolution."));
            }
            result.Add(new(name, sourceFile, diagnostics.Count == 0, diagnostics));
        }
        foreach ((string categoryId, JsonElement category) in categories)
        {
            string[] expected = [.. category.GetProperty("names").EnumerateArray().Select(value => value.GetString()!)];
            if (!categoryNames[categoryId].SequenceEqual(expected, StringComparer.Ordinal))
                throw new DaggerfallContentException([$"Classic quest corpus category '{categoryId}' does not retain its exact ordered selection."]);
        }
        return result;
    }

    private static string[] CategoryNames(DaggerfallDefinitions definitions, DaggerfallClassicQuestCategoryExpectation category) =>
        [.. definitions.QuestSources.Catalog.Rows
            .Where(row => row.Group == category.CatalogGroup && row.Active == category.Active && row.SourceDisposition == "present")
            .Where(row => category.NamePrefix is null || row.Name.StartsWith(category.NamePrefix, StringComparison.Ordinal) != category.ExcludePrefix)
            .Select(row => row.Name)];

    private static IEnumerable<(int Line, string Text)> ActionLines(DaggerfallQuestSourceDefinition source) => source.Blocks.Where(block => block.Kind is "headless" or "task" or "variable" or "global").SelectMany(block => block.Lines.Skip(block.Kind == "headless" ? 0 : 1).Select((text, index) => (block.FirstLine + index + (block.Kind == "headless" ? 0 : 1), text)));
    private static IEnumerable<(int Line, string Text)> MessageLines(DaggerfallQuestSourceDefinition source) => source.Messages.SelectMany(message => message.Lines.Select((text, index) => (message.FirstLine + index + 1, text)));
}
