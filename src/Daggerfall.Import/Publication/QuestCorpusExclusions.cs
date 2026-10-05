using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>Explicit product dispositions for the two source commands whose meaning the donor leaves unknown.</summary>
internal static class QuestCorpusExclusions
{
    internal static IReadOnlyList<DaggerfallQuestDiagnostic> Read(DaggerfallQuestRecord source) =>
        [.. source.Blocks.SelectMany(block => block.Lines.Select((text, offset) => (Text: text, Line: block.FirstLine + offset)))
            .Where(line => (source.Name, line.Text.Trim()) is ("S0000007", "location _tavern_ 100 27000") or ("B0B71Y03", "_0x3c_ 19"))
            .Select(line => new DaggerfallQuestDiagnostic(line.Line, line.Text,
                "Explicitly excluded by product decision: original meaning is unknown; donor documents and ignores this command. Retained as provenance, not an executable action."))];
}
