using System.Text.Json.Nodes;
using System.Text;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class NamedQuestCorpusTests
{
    [Theory]
    [InlineData("story-early", 20)]
    [InlineData("story-late", 16)]
    [InlineData("cures", 2)]
    public void Named_receipts_use_actual_action_capabilities(string id, int count)
    {
        IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> receipts = DaggerfallNamedQuestCorpusContent.Read(Payload(id), TestPayload.Definitions);
        Assert.Equal(count, receipts.Count);
        DaggerfallQuestRuntimeAdmission admission = new(receipts);
        foreach (DaggerfallFightersGuildQuestRuntimeReceipt receipt in receipts)
        {
            Assert.Equal(receipt.Diagnostics.Count == 0, receipt.Runnable);
            if (!receipt.Runnable) Assert.Throws<ArgumentException>(() => admission.RequireRunnable(receipt.SourceFile));
        }
    }

    [Fact]
    public void Only_source_identified_quests_can_claim_protected_lifecycle()
    {
        JsonObject payload = JsonNode.Parse(Payload("story-early"))!.AsObject();
        payload["quests"]![0]!["protectedLifecycle"] = true;
        Assert.Throws<DaggerfallContentException>(() => DaggerfallNamedQuestCorpusContent.Read(Encoding.UTF8.GetBytes(payload.ToJsonString()), TestPayload.Definitions));
        Assert.False(DaggerfallQuestInstances.IsProtectedMainQuest("_TUTOR__.txt"));
        foreach (string name in new[] { "S0000999", "S0000977", "_BRISIEN" })
            Assert.True(DaggerfallQuestInstances.IsProtectedMainQuest(name + ".txt"));
    }

    internal static byte[] Payload(string id) => File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, $"content/worldrpg/payloads/daggerfall.quests.{id}.json"));

    [Fact]
    public void Product_exclusion_is_one_exact_command_in_one_source_and_retains_provenance()
    {
        var source = TestPayload.Definitions.QuestSources.Resolve("S0000007.txt");
        Assert.Contains(source.Blocks.SelectMany(block => block.Lines), line => line.Trim() == "location _tavern_ 100 27000");
        Assert.DoesNotContain(DaggerfallQuestTaskCompiler.Compile(source).Tasks.SelectMany(task => task.Operations),
            operation => operation.Source == "location _tavern_ 100 27000");
        Assert.Contains(DaggerfallQuestTaskCompiler.Assess(source with { SourceFile = "another.txt" }),
            diagnostic => diagnostic.Text == "location _tavern_ 100 27000");
    }
}
