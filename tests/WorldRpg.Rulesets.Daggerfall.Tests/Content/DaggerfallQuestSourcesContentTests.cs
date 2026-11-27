using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The quest records the classic quests pack carries, as the ruleset reads them: each resolves with its messages and
/// blocks, a name two records share diagnoses both, and an edit to a record is read as written, with nothing to import.
/// </summary>
public sealed class DaggerfallQuestSourcesContentTests
{
    [Fact]
    public void Resolves_compiled_and_diagnosed_quests()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        Assert.Equal(268, definitions.QuestSources.Quests.Count);
        Assert.Equal(266, definitions.QuestSources.Quests.Values.Count(quest => quest.Disposition == DaggerfallQuestDisposition.Compiled));

        // A compiled quest resolves with its messages, blocks and line labels.
        DaggerfallQuestSourceDefinition quest = definitions.QuestSources.Resolve("00B00Y00.txt");
        Assert.Equal(DaggerfallQuestDisposition.Compiled, quest.Disposition);
        Assert.NotEmpty(quest.Messages);
        Assert.NotEmpty(quest.Blocks);
        Assert.Empty(quest.Diagnostics);
        Assert.All(quest.Blocks, block => Assert.True(block.FirstLine > 0 && block.Lines.Count > 0));

        // The record keeps action order as content; the quest runner compiles it.
        DaggerfallQuestBlockDefinition headless = quest.Blocks.Single(block => block.Kind == "headless");
        Assert.Equal(["start timer _1stparton_ ", "reveal _mondung_ ", "log 1030 step 0 ", "place foe _monster_ at _mondung_ "], headless.Lines);
        DaggerfallQuestBlockDefinition task = quest.Blocks.First(block => block.Kind == "task");
        Assert.Equal(["_pcgetsgold_ task:", "when _qgclicked_ and _mondead_ ", "give pc _reward_ ", "end quest "], task.Lines);

        // __DEMO03.txt names itself __DEMO02, so neither record is the one source that name resolves to.
        DaggerfallQuestSourceDefinition demo2 = definitions.QuestSources.Resolve("__DEMO02.txt");
        DaggerfallQuestSourceDefinition demo3 = definitions.QuestSources.Resolve("__DEMO03.txt");
        Assert.Equal(DaggerfallQuestDisposition.Diagnosed, demo2.Disposition);
        Assert.Equal(DaggerfallQuestDisposition.Diagnosed, demo3.Disposition);
        Assert.Equal(demo2.Name, demo3.Name);
        Assert.All(new[] { demo2, demo3 }, demo => Assert.Contains(demo.Diagnostics, diagnostic => diagnostic.Reason.Contains("2 sources", StringComparison.Ordinal)));
    }

    [Fact]
    public void Tables_keep_every_slot_and_alias()
    {
        DaggerfallQuestTables tables = TestPayload.Definitions.QuestSources.Tables;
        Assert.Equal(66, tables.Globals.Rows.Count);
        Assert.Equal(Enumerable.Range(0, 64), tables.Globals.Rows.Select(row => row.Id).Distinct().Order());
        Assert.Equal(5, tables.Globals.Lookup["Unused1"]);
        Assert.Equal(5, tables.Globals.Lookup["TookTheCure"]);
        Assert.Equal(10, tables.Globals.Lookup["Unused2"]);
        Assert.Equal(10, tables.Globals.Lookup["OpenedShapeshifters"]);
        Assert.Equal(17, tables.StaticMessages.Rows.Count);
        Assert.Equal(new[] { 0 }.Concat(Enumerable.Range(1000, 11)).Append(1045), tables.StaticMessages.Rows.Select(row => row.Id).Distinct().Order());
        Assert.Equal(new[] { "RumorsPostfailure", "RumorsPostFailure" }, tables.StaticMessages.Rows.Where(row => row.Id == 1006).Select(row => row.Name));
        Assert.Equal(1006, tables.StaticMessages.Lookup["rumorspostfailure"]);
    }

    [Fact]
    public void An_edited_record_declares_what_its_lines_say()
    {
        JsonArray records = TestPayload.QuestRecords();
        JsonNode curevam = records.Single(record => record!["sourceFile"]!.GetValue<string>() == "$CUREVAM.txt")!;
        JsonNode foe = curevam["blocks"]!.AsArray().Single(block => block!["lines"]![0]!.GetValue<string>() == "Foe _bloodfather_ is Ancient_vampire")!;
        foe["lines"]![0] = "Foe _bloodfather_ is 2 Ancient_vampire";

        DaggerfallQuestResourceDefinition declared = TestPayload.WithQuests(records).QuestSources.Resources
            .Single(resource => resource.SourceFile == "$CUREVAM.txt" && resource.CanonicalId == "bloodfather");

        Assert.Equal(2, declared.Foe!.Count);
        Assert.Equal("ancient_vampire", declared.TargetCanonicalId);
        Assert.Equal(1, TestPayload.Definitions.QuestSources.Resources
            .Single(resource => resource.SourceFile == "$CUREVAM.txt" && resource.CanonicalId == "bloodfather").Foe!.Count);
    }

    [Fact]
    public void A_record_with_a_misspelt_member_or_a_line_label_two_blocks_share_is_refused()
    {
        JsonArray misspelt = TestPayload.QuestRecords();
        misspelt.Add(JsonNode.Parse("""{"name":"flawed","sourceFile":"flawed.txt","mesages":[],"blocks":[]}"""));
        DaggerfallContentException unknown = Assert.Throws<DaggerfallContentException>(() => TestPayload.WithQuests(misspelt));
        Assert.Contains(unknown.Diagnostics, diagnostic => diagnostic.StartsWith("Quest record 'quests/268.json' is malformed", StringComparison.Ordinal));

        // A saved headless task, disease or keyed draw is named by its line label, so a label names one line.
        JsonArray shared = TestPayload.QuestRecords();
        shared.Add(JsonNode.Parse("""
            {"name":"shared","sourceFile":"shared.txt","messages":[],
             "blocks":[{"kind":"headless","firstLine":1,"lines":["end quest","say 10"]},{"kind":"variable","firstLine":2,"lines":["variable _x_"]}]}
            """));
        DaggerfallContentException labels = Assert.Throws<DaggerfallContentException>(() => TestPayload.WithQuests(shared));
        Assert.Contains("Quest record 'quests/268.json''s variable block at line 2 labels line 2, which another block labels too.", labels.Diagnostics);

        JsonArray twice = TestPayload.QuestRecords();
        twice.Add(twice[0]!.DeepClone());
        Assert.Contains(Assert.Throws<DaggerfallContentException>(() => TestPayload.WithQuests(twice)).Diagnostics,
            diagnostic => diagnostic.Contains("a quest has one record", StringComparison.Ordinal));
    }
}
