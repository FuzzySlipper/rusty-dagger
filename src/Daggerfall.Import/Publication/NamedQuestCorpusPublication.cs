using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>Explicit story and cure selections, which are source files rather than random-offer catalog rows.</summary>
public static class NamedQuestCorpusPublication
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Selections = new Dictionary<string, IReadOnlyList<string>>
    {
        ["story-early"] = ["S0000001", "S0000002", "S0000003", "S0000004", "S0000005", "S0000006", "S0000007", "S0000008", "S0000009", "S0000010", "S0000011", "S0000012", "S0000013", "S0000015", "S0000016", "S0000017", "S0000018", "S0000020", "S0000021", "S0000022"],
        ["story-late"] = ["S0000100", "S0000101", "S0000102", "S0000103", "S0000104", "S0000106", "S0000107", "S0000500", "S0000501", "S0000502", "S0000503", "S0000977", "S0000988", "S0000999", "_BRISIEN", "_TUTOR__"],
        ["cures"] = ["$CUREVAM", "$CUREWER"],
    };

    public static DaggerfallNamedQuestCorpus Create(string id, DaggerfallQuestPack sources, DaggerfallQuestOriginalSourceSet originals)
    {
        List<DaggerfallNamedQuestReceipt> receipts = [];
        foreach (string name in Selections[id])
        {
            DaggerfallQuestRecord source = sources.Quests.Single(source => source.Name == name);
            source.Validate();
            DaggerfallQuestOriginalSource original = originals.Quests.Single(source => source.Stem == name);
            string role = name switch { "_TUTOR__" => "tutorial", "_BRISIEN" => "introduction", "$CUREVAM" => "vampirism-cure", "$CUREWER" => "lycanthropy-cure", _ => "story" };
            receipts.Add(new(name, source.SourceFile, source.DisplayName, role,
                name is "S0000999" or "S0000977" or "_BRISIEN", source.DisplayName.Length == 0 ? ["Source has no DisplayName."] : [],
                original.Selection.ToString().ToLowerInvariant(), source.Disposition.ToString().ToLowerInvariant(),
                ContentDigest.Compute(JsonSerializer.SerializeToUtf8Bytes(source, PublishedJson.SectionCompact)),
                [.. source.Blocks.Where(block => block.Kind is QuestBlockKind.Headless or QuestBlockKind.Task or QuestBlockKind.Variable or QuestBlockKind.Global)
                    .SelectMany(block => block.Lines.Skip(block.Kind == QuestBlockKind.Headless ? 0 : 1)
                        .Select((text, offset) => new DaggerfallQuestActionLine(block.FirstLine + offset + (block.Kind == QuestBlockKind.Headless ? 0 : 1), text)))], source.Diagnostics,
                QuestCorpusExclusions.Read(source), MissingReferences(source, sources)));
        }
        DaggerfallNamedQuestCorpus unsigned = new(id, receipts, default);
        return unsigned with { Fingerprint = ContentDigest.Compute(JsonSerializer.SerializeToUtf8Bytes(unsigned, PublishedJson.SectionCompact)) };
    }

    private static IReadOnlyList<DaggerfallQuestDiagnostic> MissingReferences(DaggerfallQuestRecord source, DaggerfallQuestPack sources)
    {
        List<DaggerfallQuestDiagnostic> result = [];
        foreach (var block in source.Blocks)
            for (int index = 0; index < block.Lines.Count; index++)
            {
                string text = block.Lines[index];
                var match = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^(?:start|run) quest (?:(?<number>\d+) \d+|(?<name>[\w.]+))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                string target = match.Groups["number"].Success
                    ? "S" + int.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture).ToString("0000000", System.Globalization.CultureInfo.InvariantCulture)
                    : match.Groups["name"].Value;
                if (!sources.Quests.Any(value => value.Name.Equals(target, StringComparison.OrdinalIgnoreCase)))
                    result.Add(new(block.FirstLine + index, text, $"Referenced quest '{target}' has no donor source. The retained action reports an unavailable source if reached; no replacement quest is invented."));
            }
        return result;
    }

    public static byte[] Serialize(DaggerfallNamedQuestCorpus corpus) => JsonSerializer.SerializeToUtf8Bytes(corpus, PublishedJson.Section);
}

public sealed record DaggerfallNamedQuestCorpus(string Id, IReadOnlyList<DaggerfallNamedQuestReceipt> Quests, ContentDigest Fingerprint);
public sealed record DaggerfallNamedQuestReceipt(string Name, string SourceFile, string DisplayName, string NarrativeRole,
    bool ProtectedLifecycle, IReadOnlyList<string> MetadataDiagnostics, string OriginalSelection, string SourceDisposition,
    ContentDigest SourceFingerprint, IReadOnlyList<DaggerfallQuestActionLine> ActionLines, IReadOnlyList<DaggerfallQuestDiagnostic> SourceDiagnostics,
    IReadOnlyList<DaggerfallQuestDiagnostic> ExcludedActions, IReadOnlyList<DaggerfallQuestDiagnostic> MissingQuestReferences);
