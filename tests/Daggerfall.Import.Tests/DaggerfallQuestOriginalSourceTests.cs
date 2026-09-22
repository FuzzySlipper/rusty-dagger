using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using System.Text.Json;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>Original QBN/QRC identities stay distinct from rewritten quest text selections.</summary>
public sealed class DaggerfallQuestOriginalSourceTests
{
    [Fact]
    public void Decodes_fixed_binary_message_records_for_every_supplied_qbn()
    {
        string root = RepositoryRoot();
        QuestSourceInventory inventory = ReadInventory(root);
        foreach (QuestSourceFile source in inventory.Binaries)
        {
            QuestBinaryRecords decoded = QuestBinaryRecords.Decode(File.ReadAllBytes(Path.Combine(root, "local/arena2", source.Path)), source.Path);
            Assert.Equal(QuestBinaryRecordDisposition.Decoded, decoded.Disposition);
            Assert.NotNull(decoded.Header);
            // The header's optional resource filename is data, not the file identity: some
            // special binaries deliberately leave it empty. The exact on-disk QBN path
            // remains the source identity carried by the comparison record.
            Assert.NotNull(decoded.Header!.ResourceFileName);
            Assert.All(decoded.OpcodeReferences, reference => Assert.InRange(reference.MessageId, short.MinValue, short.MaxValue));
        }
    }

    [Fact]
    public void Decodes_a_location_record_that_ends_exactly_at_the_binary_boundary()
    {
        byte[] bytes = new byte[60 + 24];
        WriteUInt16(bytes, 24, 1); // Location count.
        WriteUInt16(bytes, 44, 60); // Location offset.

        QuestBinaryRecords decoded = QuestBinaryRecords.Decode(bytes, "fixture.QBN");

        Assert.Equal(QuestBinaryRecordDisposition.Decoded, decoded.Disposition);
        Assert.Single(decoded.ResourceReferences);
        Assert.Equal("place", decoded.ResourceReferences[0].RecordKind);
    }

    [Fact]
    public void Publishes_all_original_stems_with_real_selections_and_one_sided_availability()
    {
        DaggerfallQuestOriginalSourceSet sources = BuildSources();

        Assert.Equal(307, sources.Quests.Count);
        Assert.Equal(302, sources.Quests.Count(source => source.BinaryPath is not null && source.ResourcePath is not null));
        Assert.Equal(4, sources.Quests.Count(source => source.BinaryPath is not null && source.ResourcePath is null));
        Assert.Equal(1, sources.Quests.Count(source => source.BinaryPath is null && source.ResourcePath is not null));
        Assert.Equal(243, sources.Quests.Count(source => source.Selection == DaggerfallQuestOriginalSourceSelection.RewrittenText));
        Assert.Equal(64, sources.Quests.Count(source => source.Selection == DaggerfallQuestOriginalSourceSelection.NoRewrittenText));

        Assert.Equal(["80C00Y00", "A0C00Y04", "N0C00Y01", "R0C40Y23"], sources.Quests
            .Where(source => source.BinaryPath is not null && source.ResourcePath is null).Select(source => source.Stem).Order(StringComparer.Ordinal));
        DaggerfallQuestOriginalSource resourceOnly = Assert.Single(sources.Quests, source => source.BinaryPath is null);
        Assert.Equal("M0B40Y04", resourceOnly.Stem);
        Assert.Equal(DaggerfallQuestOriginalSourceSelection.NoRewrittenText, resourceOnly.Selection);
        Assert.Contains("not enabled", resourceOnly.Availability, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Retains_actual_repeated_qrc_records_and_reports_rewrite_identity_differences()
    {
        DaggerfallQuestOriginalSource peryite = Assert.Single(BuildSources().Quests, source => source.Stem == "50C00Y00");

        Assert.Equal(DaggerfallQuestOriginalSourceSelection.RewrittenText, peryite.Selection);
        Assert.Equal(2, peryite.OriginalMessages.Count(message => message.Id == 1008));
        DaggerfallQuestMessageComparison omitted = Assert.Single(peryite.MessageComparisons, comparison => comparison.Id == 1008);
        Assert.False(omitted.PresentInRewrittenText);
        Assert.Contains("no rewritten message", omitted.Difference, StringComparison.Ordinal);
        Assert.Contains(peryite.MessageComparisons, comparison => comparison.PresentInRewrittenText && !comparison.TextMatches);
        Assert.All(peryite.OriginalMessages, message => Assert.Matches("^[0-9a-f]{64}$", message.Digest));
        Assert.Contains(peryite.BinaryOpcodeReferences, reference => reference.MessageId == -1);
        Assert.Contains(-1, peryite.BinaryMessageIdsMissingFromOriginalResources);
        // The person declaration has no narrower typed AnyInfo property, so the builder
        // takes it from the normalized declaration's retained Parameters rather than
        // re-reading raw QBN lines.
        Assert.Contains(peryite.RewrittenResourceMessageReferences, reference => string.Equals(reference, "anyinfo:1011", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Retains_exact_text_matches_as_well_as_explicit_mismatches()
    {
        IReadOnlyList<DaggerfallQuestMessageComparison> comparisons = [.. BuildSources().Quests.SelectMany(source => source.MessageComparisons)];

        Assert.Contains(comparisons, comparison => comparison.TextMatches);
        Assert.Contains(comparisons, comparison => comparison.PresentInRewrittenText && !comparison.TextMatches);
    }

    [Fact]
    public void Base_payload_publishes_the_complete_original_source_selection()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
        JsonElement quests = document.RootElement.GetProperty("questOriginalSources").GetProperty("quests");

        Assert.Equal(307, quests.GetArrayLength());
        JsonElement peryite = quests.EnumerateArray().Single(quest => quest.GetProperty("stem").GetString() == "50C00Y00");
        Assert.Equal("rewrittenText", peryite.GetProperty("selection").GetString());
        Assert.Contains(peryite.GetProperty("originalMessages").EnumerateArray(), message => message.GetProperty("id").GetInt32() == 1008 && message.GetProperty("occurrence").GetInt32() == 1);
        JsonElement qbnOnly = quests.EnumerateArray().Single(quest => quest.GetProperty("stem").GetString() == "80C00Y00");
        Assert.Equal(JsonValueKind.Null, qbnOnly.GetProperty("resourcePath").ValueKind);
        Assert.Contains("not enabled", qbnOnly.GetProperty("availability").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    private static DaggerfallQuestOriginalSourceSet BuildSources()
    {
        string root = RepositoryRoot();
        string questText = "/home/research/daggerfall-unity/Assets/StreamingAssets/Quests";
        string tables = "/home/research/daggerfall-unity/Assets/StreamingAssets/Tables";
        IReadOnlyList<SourceInventoryRow> manifest = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(root, "docs/coverage/content-source-manifest.csv")));
        IReadOnlyDictionary<string, int> messages = DaggerfallQuestTableReader.Read(File.ReadAllBytes(Path.Combine(tables, "Quests-StaticMessages.txt")), "Tables/Quests-StaticMessages.txt").Lookup;
        IReadOnlyDictionary<string, int> globals = DaggerfallQuestTableReader.Read(File.ReadAllBytes(Path.Combine(tables, "Quests-GlobalVars.txt")), "Tables/Quests-GlobalVars.txt", globals: true).Lookup;
        List<QuestSourceDocument> documents = [];
        List<(string FileName, string QuestName, int Line, string Reason)> failures = [];
        long bytes = 0;
        foreach (string path in Directory.EnumerateFiles(questText, "*.txt").Order(StringComparer.Ordinal))
        {
            string text = File.ReadAllText(path);
            bytes += new FileInfo(path).Length;
            try
            {
                documents.Add(QuestSourceReader.Read(text, Path.GetFileName(path), messages, globals));
            }
            catch (Arena2FormatException exception)
            {
                failures.Add((Path.GetFileName(path), Path.GetFileNameWithoutExtension(path), exception.Offset, exception.Message));
            }
        }

        DaggerfallQuestPack rewritten = DaggerfallQuestPackBuilder.Build(documents, failures, "donor/StreamingAssets/Quests", new byte[bytes], manifest);
        return DaggerfallQuestOriginalSourceBuilder.Build(Path.Combine(root, "local/arena2"), ReadInventory(root), rewritten);
    }

    private static QuestSourceInventory ReadInventory(string root) => QuestSourceInventory.Enumerate(
        Directory.EnumerateFiles(Path.Combine(root, "local/arena2"))
            .Where(path => path.EndsWith(QuestSourceInventory.BinaryExtension, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(QuestSourceInventory.ResourcesExtension, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)!, "local/arena2");

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        }

        throw new InvalidOperationException("repository root not found");
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }
}
