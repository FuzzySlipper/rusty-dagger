using System.Text;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The quests pack's corpora and the admission read from them at load: every record's program is assessed by the
/// quest language, the corpora say how each quest is offered, and a corpus that misstates the catalog is refused.
/// </summary>
public sealed class DaggerfallQuestContentTests
{
    [Fact]
    public void Every_record_is_admitted_with_the_diagnostics_that_keep_it_from_running()
    {
        DaggerfallQuestAdmission admission = TestPayload.QuestAdmission;
        DaggerfallQuestSourceSet sources = TestPayload.Definitions.QuestSources;

        Assert.Equal(sources.Quests.Keys.Order(StringComparer.Ordinal), admission.Receipts.Select(receipt => receipt.SourceFile));
        Assert.All(admission.Receipts, receipt => Assert.Equal(receipt.Diagnostics.Count == 0, receipt.Runnable));
        Assert.Equal(
            new Dictionary<string, int> { ["fighters"] = 20, ["mages"] = 18, ["temples"] = 24, ["social"] = 45, ["witches-commoners"] = 30,
                ["merchants-vampires"] = 22, ["disabled"] = 18, ["nobility"] = 30, ["story"] = 36, ["cures"] = 2 },
            sources.Corpora.ToDictionary(corpus => corpus.Id, corpus => corpus.Categories.Sum(category => category.Names.Count)));
        Assert.Equal(
        [
            "M0C00Y11", "M0C00Y12", "M0C00Y13", "M0C00Y14", "M0B00Y00", "M0B00Y06", "M0B00Y07", "M0B00Y15", "M0B00Y16", "M0B00Y17",
            "M0B1XY01", "M0B11Y18", "M0B20Y02", "M0B21Y19", "M0B30Y03", "M0B30Y04", "M0B30Y08", "M0B40Y05", "M0B50Y09", "M0B60Y10",
        ], sources.Corpora.Single(corpus => corpus.Id == "fighters").Categories.Single().Names);

        Dictionary<string, DaggerfallQuestRuntimeReceipt> byName = admission.Receipts.ToDictionary(receipt => receipt.Name + "|" + receipt.SourceFile);
        DaggerfallQuestRuntimeReceipt Receipt(string name) => byName[name + "|" + name + ".txt"];
        Assert.All(sources.Corpora.Single(corpus => corpus.Id == "fighters").Categories.Single().Names, name => Assert.True(Receipt(name).Runnable));
        Assert.All(new[] { "P0A01L00", "P0B00L01", "P0B00L03", "P0B00L04", "P0B00L06", "P0B01L02", "P0B10L07", "P0B10L08", "P0B10L10", "P0B20L09" },
            name => Assert.True(Receipt(name).Runnable, string.Join("; ", Receipt(name).Diagnostics)));
        // The nobility's vampire-clan macros resolve through the runtime binding, not a quest-NPC context.
        Assert.DoesNotContain(Receipt("R0C11Y28").Diagnostics, diagnostic => diagnostic.Reason.Contains("quest-NPC context", StringComparison.Ordinal));
        // A catalog entry the corpora never offer may not run at all.
        Assert.Contains(Receipt("K0C00Y06").Diagnostics, diagnostic => diagnostic.Reason == "Disabled classic quest entries are not ordinary offers.");
        // A name two records share runs from neither.
        Assert.False(byName["__DEMO02|__DEMO03.txt"].Runnable);
    }

    [Fact]
    public void Runtime_admission_refuses_a_quest_whose_program_does_not_run()
    {
        DaggerfallQuestRuntimeAdmission admission = new([
            new DaggerfallQuestRuntimeReceipt("blocked", "blocked.txt", false,
                [new DaggerfallQuestDiagnosticDefinition(12, "talk to _giver_", "No current quest task runner operation supports this action.")]),
        ]);

        ArgumentException failure = Assert.Throws<ArgumentException>(() => admission.RequireRunnable("blocked.txt"));

        Assert.Contains("line 12: talk to _giver_", failure.Message, StringComparison.Ordinal);
        Assert.Contains("runner operation supports", failure.Message, StringComparison.Ordinal);

        // The quest instance owner refuses it at start and at restore alike.
        DaggerfallQuestSourceDefinition source = TestPayload.Definitions.QuestSources.Resolve("M0C00Y11.txt");
        DaggerfallQuestInstances instances = new(TestPayload.Definitions, RandomMinimum.Create(), new([
            new DaggerfallQuestRuntimeReceipt(source.Name, source.SourceFile, false, [new(12, "unsupported test action", "Unsupported operation in the selected source.")])]));
        DaggerfallQuestInstanceSave fresh = new("fighters:blocked", source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], []);
        Assert.Contains("no admitted runnable program", Assert.Throws<ArgumentException>(() => instances.Start(fresh)).Message, StringComparison.Ordinal);
        Assert.Contains("no admitted runnable program", Assert.Throws<ArgumentException>(() => instances.Restore(new([fresh]))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_category_that_misstates_its_catalog_group_or_leaves_a_row_out_is_refused()
    {
        JsonObject moved = Payload();
        Category(moved, "temples", "general")["catalogGroup"] = "MagesGuild";
        DaggerfallContentException group = Assert.Throws<DaggerfallContentException>(() => Read(moved));
        Assert.Contains(group.Diagnostics, diagnostic => diagnostic.StartsWith("Quest corpus 'temples' category 'general' names 'C0C00Y10', which is not the next active MagesGuild row", StringComparison.Ordinal));

        JsonObject dropped = Payload();
        Corpus(dropped, "temples")["categories"]!.AsArray().RemoveAt(1);
        DaggerfallContentException partition = Assert.Throws<DaggerfallContentException>(() => Read(dropped));
        Assert.Contains(partition.Diagnostics, diagnostic => diagnostic.StartsWith("The quest corpora's active HolyOrder categories leave out 00B00Y00", StringComparison.Ordinal));

        JsonObject twice = Payload();
        Category(twice, "story", "story-late")["names"]!.AsArray().Add("$CUREVAM");
        Assert.Contains(Assert.Throws<DaggerfallContentException>(() => Read(twice)).Diagnostics,
            diagnostic => diagnostic == "Quest corpus 'cures' category 'cures' names '$CUREVAM', which another category names too.");

        JsonObject unknown = Payload();
        Category(unknown, "cures", "cures")["availability"] = "sometimes";
        Assert.Contains(Assert.Throws<DaggerfallContentException>(() => Read(unknown)).Diagnostics,
            diagnostic => diagnostic.Contains("states availability 'sometimes'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_scenario_pack_adds_quests_and_corpora_beside_the_classic_one_and_may_not_restate_its_catalog()
    {
        DaggerfallQuestPackFiles scenario = new("scenario.quests", Encoding.UTF8.GetBytes("""
            {"questCorpora":[{"id":"scenario","categories":[{"id":"scenario","availability":"named","names":["scenario"]}]}]}
            """), [new("worldrpg/packs/scenario.quests/quests/scenario.json", Encoding.UTF8.GetBytes("""
            {"name":"scenario","displayName":"A scenario","sourceFile":"scenario.txt","messages":[{"id":10,"lines":["Begun."]}],
             "blocks":[{"kind":"headless","firstLine":1,"lines":["log 10 step 0","end quest"]}]}
            """))]);

        DaggerfallQuestSourceSet sources = DaggerfallQuestContent.Read([TestPayload.QuestPack(), scenario]);
        Assert.Equal(269, sources.Quests.Count);
        Assert.Equal("A scenario", sources.Resolve("scenario.txt").DisplayName);
        Assert.True(DaggerfallQuestContent.Admit(sources, new DaggerfallQuestLanguage()).Receipts.Single(receipt => receipt.SourceFile == "scenario.txt").Runnable);

        DaggerfallQuestPackFiles restated = scenario with { Payload = Encoding.UTF8.GetBytes("""{"questCatalog":[]}""") };
        Assert.Contains("Quests packs 'daggerfall.quests' and 'scenario.quests' both carry questCatalog; it has one home.",
            Assert.Throws<DaggerfallContentException>(() => DaggerfallQuestContent.Read([TestPayload.QuestPack(), restated])).Diagnostics);
    }

    [Fact]
    public void Only_the_main_quests_keep_their_finished_instances()
    {
        Assert.False(DaggerfallQuestSourceDefinition.IsProtectedMainQuest("_TUTOR__.txt"));
        foreach (string name in new[] { "S0000999", "S0000977", "_BRISIEN" })
            Assert.True(DaggerfallQuestSourceDefinition.IsProtectedMainQuest(name + ".txt"));
    }

    [Fact]
    public void Product_exclusion_is_one_exact_command_in_one_source()
    {
        var source = TestPayload.Definitions.QuestSources.Resolve("S0000007.txt");
        Assert.Contains(source.Blocks.SelectMany(block => block.Lines), line => line.Trim() == "location _tavern_ 100 27000");
        Assert.DoesNotContain(DaggerfallQuestTaskCompiler.Compile(source).Tasks.SelectMany(task => task.Operations),
            operation => operation.Source == "location _tavern_ 100 27000");
        Assert.Contains(DaggerfallQuestTaskCompiler.Assess(source with { SourceFile = "another.txt" }),
            diagnostic => diagnostic.Text == "location _tavern_ 100 27000");
    }

    private static JsonObject Payload() => JsonNode.Parse(TestPayload.QuestPack().Payload.Span)!.AsObject();

    private static JsonObject Corpus(JsonObject payload, string id) =>
        payload["questCorpora"]!.AsArray().Single(corpus => corpus!["id"]!.GetValue<string>() == id)!.AsObject();

    private static JsonObject Category(JsonObject payload, string corpus, string id) =>
        Corpus(payload, corpus)["categories"]!.AsArray().Single(category => category!["id"]!.GetValue<string>() == id)!.AsObject();

    private static DaggerfallQuestSourceSet Read(JsonObject payload) =>
        DaggerfallQuestContent.Read([TestPayload.QuestPack() with { Payload = Encoding.UTF8.GetBytes(payload.ToJsonString()) }]);
}
