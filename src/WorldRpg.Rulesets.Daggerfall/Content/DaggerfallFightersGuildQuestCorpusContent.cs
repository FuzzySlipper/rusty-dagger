using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Admits the published Fighters Guild receipt and assesses it with the actual task compiler.</summary>
internal static class DaggerfallFightersGuildQuestCorpusContent
{
    internal static IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> Read(ProductContent content, ReadOnlyMemory<byte> payload, DaggerfallDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(definitions);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement quests = document.RootElement.GetProperty("quests");
        DaggerfallQuestCatalogRow[] catalog = [.. definitions.QuestSources.Catalog.Rows
            .Where(row => row.Active && string.Equals(row.Group, "FightersGuild", StringComparison.Ordinal))];
        if (catalog.Length != 20) throw new DaggerfallContentException(["The admitted quest catalog does not retain exactly twenty active Fighters Guild entries."]);
        List<DaggerfallFightersGuildQuestRuntimeReceipt> receipts = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        HashSet<string> sourceFiles = new(StringComparer.Ordinal);
        foreach ((JsonElement receipt, int index) in quests.EnumerateArray().Select((receipt, index) => (receipt, index)))
        {
            string name = receipt.GetProperty("name").GetString()!;
            string sourceFile = receipt.GetProperty("sourceFile").GetString()!;
            if (!names.Add(name) || !sourceFiles.Add(sourceFile)) throw new DaggerfallContentException(["Fighters Guild receipt repeats a quest name or source file."]);
            if (index >= catalog.Length) throw new DaggerfallContentException(["Fighters Guild receipt has more records than its admitted catalog selection."]);
            DaggerfallQuestCatalogRow row = catalog[index];
            if (!string.Equals(name, row.Name, StringComparison.Ordinal)
                || !string.Equals(sourceFile, row.Name + ".txt", StringComparison.Ordinal)
                || !string.Equals(receipt.GetProperty("membership").GetString(), row.Membership, StringComparison.Ordinal)
                || receipt.GetProperty("minimumRequirement").GetInt32() != row.MinimumRequirement
                || !string.Equals(receipt.GetProperty("requirementKind").GetString(), row.RequirementKind, StringComparison.Ordinal)
                || receipt.GetProperty("adult").GetBoolean() != row.Adult
                || receipt.GetProperty("oneTime").GetBoolean() != row.OneTime
                || receipt.GetProperty("catalogSourceLine").GetInt32() != row.SourceLine
                || !string.Equals(receipt.GetProperty("catalogSourceDisposition").GetString(), row.SourceDisposition, StringComparison.Ordinal))
                throw new DaggerfallContentException([$"Fighters Guild receipt '{sourceFile}' does not agree with active catalog row {row.SourceLine}."]);
            DaggerfallQuestSourceDefinition source = definitions.QuestSources.Resolve(sourceFile);
            if (!string.Equals(name, source.Name, StringComparison.Ordinal)) throw new DaggerfallContentException([$"Fighters Guild receipt '{sourceFile}' names quest '{name}', not its selected source '{source.Name}'."]);
            if (!string.Equals(receipt.GetProperty("sourceDisposition").GetString(), source.Disposition.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new DaggerfallContentException([$"Fighters Guild receipt '{sourceFile}' does not retain its normalized source disposition."]);
            string[] published = [.. receipt.GetProperty("actionLines").EnumerateArray().Select(line => $"{line.GetProperty("line").GetInt32()}\0{line.GetProperty("text").GetString()}")];
            string[] actual = [.. ActionLines(source).Select(line => $"{line.Line}\0{line.Text}")];
            if (!published.SequenceEqual(actual)) throw new DaggerfallContentException([$"Fighters Guild receipt '{sourceFile}' does not retain its exact required action lines."]);
            string[] publishedSourceDiagnostics = [.. receipt.GetProperty("sourceDiagnostics").EnumerateArray().Select(diagnostic => $"{diagnostic.GetProperty("line").GetInt32()}\0{diagnostic.GetProperty("text").GetString()}\0{diagnostic.GetProperty("reason").GetString()}")];
            string[] actualSourceDiagnostics = [.. source.Diagnostics.Select(diagnostic => $"{diagnostic.Line}\0{diagnostic.Text}\0{diagnostic.Reason}")];
            if (!publishedSourceDiagnostics.SequenceEqual(actualSourceDiagnostics)) throw new DaggerfallContentException([$"Fighters Guild receipt '{sourceFile}' does not retain its exact source diagnostics."]);
            List<DaggerfallQuestDiagnosticDefinition> diagnostics = [.. source.Diagnostics, .. DaggerfallQuestTaskCompiler.Assess(source)];
            try
            {
                foreach (DaggerfallQuestClockDefinition clock in DaggerfallQuestClockCompiler.Compile(source))
                    if (DaggerfallQuestClockCompiler.UnsupportedTravelCondition(clock) is { } reason)
                        diagnostics.Add(new DaggerfallQuestDiagnosticDefinition(1, clock.Symbol, reason));
            }
            catch (ArgumentException exception) { diagnostics.Add(new DaggerfallQuestDiagnosticDefinition(1, sourceFile, exception.Message)); }
            receipts.Add(new(name, sourceFile, diagnostics.Count == 0, diagnostics));
        }
        if (receipts.Count != catalog.Length) throw new DaggerfallContentException([$"Fighters Guild receipt contains {receipts.Count} records rather than the retained twenty."]);
        return receipts;
    }

    private static IEnumerable<(int Line, string Text)> ActionLines(DaggerfallQuestSourceDefinition source) =>
        source.Blocks.Where(block => block.Kind is "headless" or "task" or "variable" or "global")
            .SelectMany(block => block.Lines.Skip(block.Kind == "headless" ? 0 : 1)
                .Select((text, index) => (block.FirstLine + index + (block.Kind == "headless" ? 0 : 1), text)));
}

internal sealed record DaggerfallFightersGuildQuestRuntimeReceipt(string Name, string SourceFile, bool Runnable, IReadOnlyList<DaggerfallQuestDiagnosticDefinition> Diagnostics);

/// <summary>Bundle-admitted source readiness consumed by the quest instance owner at start and restore.</summary>
internal sealed class DaggerfallQuestRuntimeAdmission
{
    private readonly IReadOnlyDictionary<string, DaggerfallFightersGuildQuestRuntimeReceipt> _receipts;

    internal DaggerfallQuestRuntimeAdmission(IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        _receipts = receipts.ToDictionary(receipt => receipt.SourceFile, StringComparer.Ordinal);
        if (_receipts.Count != receipts.Count) throw new ArgumentException("Quest runtime admission repeats a source file.", nameof(receipts));
    }

    internal void RequireRunnable(string sourceFile)
    {
        if (!_receipts.TryGetValue(sourceFile, out DaggerfallFightersGuildQuestRuntimeReceipt? receipt) || receipt.Runnable) return;
        string diagnostics = string.Join("; ", receipt.Diagnostics.Select(diagnostic => $"line {diagnostic.Line}: {diagnostic.Text} ({diagnostic.Reason})"));
        throw new ArgumentException($"Quest source '{sourceFile}' has no admitted runnable program: {diagnostics}");
    }
}
