using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The normalized quest sources through the pack: runnable quests resolve with messages and
/// blocks, diagnosed quests resolve with their diagnostics and must not run.
/// </summary>
public sealed class DaggerfallQuestSourcesContentTests
{
    [Fact]
    public void Resolves_runnable_and_diagnosed_quests()
    {
        DaggerfallDefinitions definitions = Definitions();

        Assert.Equal(265, definitions.QuestSources.Quests.Count);
        Assert.Equal(263, definitions.QuestSources.Quests.Values.Count(quest => quest.Disposition == DaggerfallQuestDisposition.Runnable));

        // A runnable quest resolves with its messages, blocks and source lines.
        DaggerfallQuestSourceDefinition quest = definitions.QuestSources.Resolve("00B00Y00.txt");
        Assert.Equal(DaggerfallQuestDisposition.Runnable, quest.Disposition);
        Assert.NotEmpty(quest.Messages);
        Assert.NotEmpty(quest.Blocks);
        Assert.Empty(quest.Diagnostics);
        Assert.All(quest.Messages, message => Assert.True(message.FirstLine > 0));
        Assert.All(quest.Blocks, block => Assert.True(block.FirstLine > 0 && block.Lines.Count > 0));

        // The diagnosed pair shares one semantic id; both resolve with diagnostics attached.
        DaggerfallQuestSourceDefinition demo2 = definitions.QuestSources.Resolve("__DEMO02.txt");
        DaggerfallQuestSourceDefinition demo3 = definitions.QuestSources.Resolve("__DEMO03.txt");
        Assert.Equal(DaggerfallQuestDisposition.Diagnosed, demo2.Disposition);
        Assert.Equal(DaggerfallQuestDisposition.Diagnosed, demo3.Disposition);
        Assert.Equal(demo2.Name, demo3.Name);
        Assert.All(new[] { demo2, demo3 }, demo => Assert.Contains(demo.Diagnostics, diagnostic => diagnostic.Line > 0 && diagnostic.Reason.Contains("2 sources", StringComparison.Ordinal)));
    }

    private static DaggerfallDefinitions Definitions()
    {
        string root = RepositoryRoot();
        return DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
    }

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
