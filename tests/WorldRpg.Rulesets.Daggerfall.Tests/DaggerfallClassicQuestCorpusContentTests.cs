using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallClassicQuestCorpusContentTests
{
    [Fact]
    public void Admits_each_published_category_and_derives_readiness_from_the_current_compiler()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        IReadOnlyDictionary<string, int> expected = new Dictionary<string, int>
        {
            ["mages"] = 18, ["temples"] = 24, ["social"] = 45, ["witches-commoners"] = 30,
            ["merchants-vampires"] = 22, ["disabled"] = 18, ["nobility"] = 28,
        };

        foreach ((string id, int count) in expected)
        {
            IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> receipts = DaggerfallClassicQuestCorpusContent.Read(
                new ProductContent(Array.Empty<ProductContentFile>()),
                File.ReadAllBytes(Path.Combine(root, $"content/worldrpg/payloads/daggerfall.quests.{id}.json")), definitions, DaggerfallClassicQuestCorpusExpectations.Require(id));
            Assert.Equal(count, receipts.Count);
            Assert.All(receipts.Where(receipt => receipt.Runnable), receipt => Assert.Empty(receipt.Diagnostics));
            Assert.All(receipts.Where(receipt => !receipt.Runnable), receipt => Assert.NotEmpty(receipt.Diagnostics));
        }
    }

    [Fact]
    public void Rejects_category_and_catalog_metadata_corruption_before_runtime_admission()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        JsonObject payload = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.quests.temples.json")))!.AsObject();
        payload["categories"]![0]!["catalogGroup"] = "MagesGuild";

        DaggerfallContentException failure = Assert.Throws<DaggerfallContentException>(() => DaggerfallClassicQuestCorpusContent.Read(
            new ProductContent(Array.Empty<ProductContentFile>()), Encoding.UTF8.GetBytes(payload.ToJsonString()), definitions, DaggerfallClassicQuestCorpusExpectations.Require("temples")));

        Assert.Contains("does not retain category 'general' exactly", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_deleted_category_and_its_receipts_against_the_ruleset_contract()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        JsonObject payload = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.quests.temples.json")))!.AsObject();
        JsonArray categories = payload["categories"]!.AsArray();
        categories.RemoveAt(1);
        JsonArray quests = payload["quests"]!.AsArray();
        for (int index = quests.Count - 1; index >= 0; index--)
            if (quests[index]!["category"]!.GetValue<string>() == "specific") quests.RemoveAt(index);

        DaggerfallContentException failure = Assert.Throws<DaggerfallContentException>(() => DaggerfallClassicQuestCorpusContent.Read(
            new ProductContent(Array.Empty<ProductContentFile>()), Encoding.UTF8.GetBytes(payload.ToJsonString()), definitions,
            DaggerfallClassicQuestCorpusExpectations.Require("temples")));

        Assert.Contains("exact category count", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Retains_vampire_and_nobility_special_cases_as_diagnostics_instead_of_excluding_sources()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> vampire = DaggerfallClassicQuestCorpusContent.Read(
            new ProductContent(Array.Empty<ProductContentFile>()),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.quests.merchants-vampires.json")), definitions, DaggerfallClassicQuestCorpusExpectations.Require("merchants-vampires"));
        IReadOnlyList<DaggerfallFightersGuildQuestRuntimeReceipt> nobility = DaggerfallClassicQuestCorpusContent.Read(
            new ProductContent(Array.Empty<ProductContentFile>()),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.quests.nobility.json")), definitions, DaggerfallClassicQuestCorpusExpectations.Require("nobility"));

        Assert.Equal(10, vampire.Count(receipt => receipt.Name.StartsWith("P", StringComparison.Ordinal)));
        Assert.Contains(vampire, receipt => !receipt.Runnable && receipt.Diagnostics.Any(diagnostic => diagnostic.Reason.Contains("runner operation", StringComparison.Ordinal)));
        DaggerfallFightersGuildQuestRuntimeReceipt vampireClan = Assert.Single(nobility, receipt => receipt.Name == "R0C11Y28");
        Assert.Contains(vampireClan.Diagnostics, diagnostic => diagnostic.Line == 218 && diagnostic.Text.Contains("%vcn", StringComparison.Ordinal) && diagnostic.Reason.Contains("#8074", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("repository root not found");
    }
}
