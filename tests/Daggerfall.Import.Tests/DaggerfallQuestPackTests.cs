using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The quest source contract: donor-shaped structure, refusal of unknown signatures, duplicate
/// semantic ids diagnosed, and source path and line retention.
/// </summary>
public sealed class DaggerfallQuestPackTests
{
    private static readonly IReadOnlyDictionary<string, int> MessageIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Message"] = 0,
        ["QuestorOffer"] = 1000,
    };

    private static readonly IReadOnlyDictionary<string, int> GlobalKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["LiftedCurse"] = 0,
        ["TookTheCure"] = 5,
        ["OpenedShapeshifters"] = 10,
    };

    [Fact]
    public void Refuses_malformed_sources_and_unknown_signatures()
    {
        Assert.Throws<Arena2FormatException>(() => QuestSourceReader.Read("displayname: X\nqrc:\nMessage: 1\ntext\nqbn:\nclock a\n", "q.txt", MessageIds, GlobalKeys));
        Assert.Throws<Arena2FormatException>(() => QuestSourceReader.Read("quest: Q\nqbn:\nclock a\n", "q.txt", MessageIds, GlobalKeys));
        Assert.Throws<Arena2FormatException>(() => QuestSourceReader.Read("quest: Q\nqrc:\nMessage: 1\ntext\n", "q.txt", MessageIds, GlobalKeys));
        // The first stray line is the headless entry and the second joins no block: the donor's
        // block reader swallows following lines into the open block, so the refusal lands late.
        Arena2FormatException unknown = Assert.Throws<Arena2FormatException>(() => QuestSourceReader.Read("quest: Q\nqrc:\nMessage: 1\ntext\nqbn:\nclock a\nfrobnicate wildly\n\nzorbnication opens\n\nzorbnication continues\n", "q.txt", MessageIds, GlobalKeys));
        Assert.Contains("zorbnication opens", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_structure_blocks_and_links()
    {
        QuestSourceDocument document = QuestSourceReader.Read(
            "quest: Q\nqrc:\nQuestorOffer:  [1000]\nOffer text\n\nMessage:  1011\nFirst\n\nSecond\nqbn:\nclock myclock 1 day 1\n\nLiftedCurse _S.01_\n\n_pcgetsgold_ task:\nperform action\n",
            "q.txt", MessageIds, GlobalKeys);
        Assert.Equal("Q", document.QuestName);
        Assert.Equal(2, document.Messages.Count);
        Assert.Equal(1000, document.Messages[0].Id);
        Assert.Equal(["First", " ", "Second"], document.Messages[1].Lines);
        Assert.Equal(3, document.Blocks.Count);
        Assert.Equal(QuestBlockKind.Clock, document.Blocks[0].Kind);
        Assert.Equal(QuestBlockKind.Global, document.Blocks[1].Kind);
        Assert.Equal(0, document.Blocks[1].Global);
        Assert.Equal(QuestBlockKind.Task, document.Blocks[2].Kind);
    }

    [Fact]
    public void Builds_the_pack_and_diagnoses_duplicates()
    {
        QuestSourceDocument first = QuestSourceReader.Read("quest: Q\nqrc:\nMessage: 1\ntext\nqbn:\nclock a\n", "a.txt", MessageIds, GlobalKeys);
        QuestSourceDocument second = QuestSourceReader.Read("quest: Q\nqrc:\nMessage: 1\ntext\nqbn:\nclock a\n", "b.txt", MessageIds, GlobalKeys);
        DaggerfallQuestPack pack = DaggerfallQuestPackBuilder.Build([first, second], [], "donor/StreamingAssets/Quests", [1], Inventory());
        pack.Validate();
        Assert.Equal(2, pack.Quests.Count);
        Assert.All(pack.Quests, quest => Assert.Equal(DaggerfallQuestDisposition.Diagnosed, quest.Disposition));
        Assert.All(pack.Quests, quest => Assert.Single(quest.Diagnostics));
        Assert.Contains("2 sources", pack.Quests[0].Diagnostics[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_the_donor_corpus()
    {
        const string quests = "/home/research/daggerfall-unity/Assets/StreamingAssets/Quests";
        if (!Directory.Exists(quests)) return;

        DaggerfallQuestTable globals = DaggerfallQuestTableReader.Read(File.ReadAllBytes("/home/research/daggerfall-unity/Assets/StreamingAssets/Tables/Quests-GlobalVars.txt"), "Tables/Quests-GlobalVars.txt", globals: true);
        DaggerfallQuestTable messages = DaggerfallQuestTableReader.Read(File.ReadAllBytes("/home/research/daggerfall-unity/Assets/StreamingAssets/Tables/Quests-StaticMessages.txt"), "Tables/Quests-StaticMessages.txt");
        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")));
        List<QuestSourceDocument> documents = [];
        List<(string, string, int, string)> failures = [];
        long total = 0;
        foreach (string path in Directory.EnumerateFiles(quests, "*.txt").Order(StringComparer.Ordinal))
        {
            total += new FileInfo(path).Length;
            try
            {
                documents.Add(QuestSourceReader.Read(File.ReadAllText(path), Path.GetFileName(path), messages.Lookup, globals.Lookup));
            }
            catch (Arena2FormatException exception)
            {
                failures.Add((Path.GetFileName(path), Path.GetFileNameWithoutExtension(path), exception.Offset, exception.Message));
            }
        }

        DaggerfallQuestPack pack = DaggerfallQuestPackBuilder.Build(documents, failures, "donor/StreamingAssets/Quests", new byte[total], inventory);
        pack.Validate();
        Assert.Equal(265, pack.Quests.Count);
        Assert.Equal(263, pack.Quests.Count(quest => quest.Disposition == DaggerfallQuestDisposition.Runnable));
        // The diagnosed pair shares one semantic id; every runnable quest keeps source lines.
        Assert.Equal(2, pack.Quests.Count(quest => quest.Disposition == DaggerfallQuestDisposition.Diagnosed));
        Assert.All(pack.Quests.Where(quest => quest.Disposition == DaggerfallQuestDisposition.Runnable), quest => Assert.NotEmpty(quest.Blocks));

        // Resource lines never merge: the donor reads 35 blocks in $CUREVAM.
        DaggerfallQuestRecord curevam = pack.Quests.Single(quest => quest.Name == "$CUREVAM");
        Assert.Equal(35, curevam.Blocks.Count);
        // A terminating blank ends the message without appending: no trailing break line.
        DaggerfallQuestMessage error = curevam.Messages.Single(message => message.Id == 1000);
        Assert.Equal(12, error.FirstLine);
        Assert.Equal(["Error -- 1000 called"], error.Lines);
        // Dash lines abutting content are content, not comments.
        DaggerfallQuestRecord s15 = pack.Quests.Single(quest => quest.Name == "S0000015");
        DaggerfallQuestMessage rumor = s15.Messages.Single(message => message.Id == 1022);
        Assert.Contains("--added \"and\" to fix poor grammar", rumor.Lines);
        Assert.Contains("--added \"to\" to fix poor grammar", rumor.Lines);
    }

    private static IReadOnlyList<SourceInventoryRow> Inventory() =>
    [
        new SourceInventoryRow("CNT-017-QBN", "family", "CNT-017", "quest-source", "donor", string.Empty, string.Empty, string.Empty),
    ];

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }
}
