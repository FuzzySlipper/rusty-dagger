using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Named story and cure packs use the same compiled actions and lifecycle as catalog quests.</summary>
internal static class DaggerfallNamedQuestCorpusContent
{
    internal static IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> Read(ReadOnlyMemory<byte> payload, DaggerfallDefinitions definitions)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        List<DaggerfallFightersGuildQuestRuntimeReceipt> result = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (JsonElement receipt in document.RootElement.GetProperty("quests").EnumerateArray())
        {
            string name = receipt.GetProperty("name").GetString()!;
            string file = receipt.GetProperty("sourceFile").GetString()!;
            if (!names.Add(name) || !definitions.QuestSources.Quests.TryGetValue(file, out DaggerfallQuestSourceDefinition? source)
                || source.Name != name || source.DisplayName != receipt.GetProperty("displayName").GetString()
                || receipt.GetProperty("protectedLifecycle").GetBoolean() != DaggerfallQuestInstances.IsProtectedMainQuest(file))
                throw new DaggerfallContentException([$"Named quest receipt '{file}' disagrees with its source or lifecycle policy."]);
            string[] published = [.. receipt.GetProperty("actionLines").EnumerateArray().Select(line => $"{line.GetProperty("line").GetInt32()}\0{line.GetProperty("text").GetString()}")];
            string[] actual = [.. source.Blocks.Where(block => block.Kind is "headless" or "task" or "variable" or "global")
                .SelectMany(block => block.Lines.Skip(block.Kind == "headless" ? 0 : 1)
                    .Select((text, offset) => $"{block.FirstLine + offset + (block.Kind == "headless" ? 0 : 1)}\0{text}"))];
            if (!published.SequenceEqual(actual)) throw new DaggerfallContentException([$"Named quest receipt '{file}' lost action provenance."]);
            List<DaggerfallQuestDiagnosticDefinition> diagnostics = [.. source.Diagnostics, .. DaggerfallQuestTaskCompiler.Assess(source)];
            try { _ = DaggerfallQuestClockCompiler.Compile(source); }
            catch (ArgumentException exception) { diagnostics.Add(new(1, file, exception.Message)); }
            result.Add(new(name, file, diagnostics.Count == 0, diagnostics));
        }
        return result;
    }
}
