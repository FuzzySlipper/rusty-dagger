using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The normalized quest sources through the pack: compiled quests resolve with messages and
/// blocks, diagnosed quests resolve with their diagnostics and must not run.
/// </summary>
public sealed class DaggerfallQuestSourcesContentTests
{
    [Fact]
    public void Resolves_compiled_and_diagnosed_quests()
    {
        DaggerfallDefinitions definitions = Definitions();

        Assert.Equal(265, definitions.QuestSources.Quests.Count);
        Assert.Equal(263, definitions.QuestSources.Quests.Values.Count(quest => quest.Disposition == DaggerfallQuestDisposition.Compiled));

        // A compiled quest resolves with its messages, blocks and source lines.
        DaggerfallQuestSourceDefinition quest = definitions.QuestSources.Resolve("00B00Y00.txt");
        Assert.Equal(DaggerfallQuestDisposition.Compiled, quest.Disposition);
        Assert.NotEmpty(quest.Messages);
        Assert.NotEmpty(quest.Blocks);
        Assert.Empty(quest.Diagnostics);
        Assert.All(quest.Messages, message => Assert.True(message.FirstLine > 0));
        Assert.All(quest.Blocks, block => Assert.True(block.FirstLine > 0 && block.Lines.Count > 0));

        // The published compiler result preserves action order as content. It is not an
        // executable action program: the later quest lifecycle owner interprets these lines.
        DaggerfallQuestBlockDefinition headless = quest.Blocks.Single(block => block.Kind == "headless");
        Assert.Equal(["start timer _1stparton_ ", "reveal _mondung_ ", "log 1030 step 0 ", "place foe _monster_ at _mondung_ "], headless.Lines);
        DaggerfallQuestBlockDefinition task = quest.Blocks.First(block => block.Kind == "task");
        Assert.Equal(["_pcgetsgold_ task:", "when _qgclicked_ and _mondead_ ", "give pc _reward_ ", "end quest "], task.Lines);

        // The diagnosed pair shares one semantic id; both resolve with diagnostics attached.
        DaggerfallQuestSourceDefinition demo2 = definitions.QuestSources.Resolve("__DEMO02.txt");
        DaggerfallQuestSourceDefinition demo3 = definitions.QuestSources.Resolve("__DEMO03.txt");
        Assert.Equal(DaggerfallQuestDisposition.Diagnosed, demo2.Disposition);
        Assert.Equal(DaggerfallQuestDisposition.Diagnosed, demo3.Disposition);
        Assert.Equal(demo2.Name, demo3.Name);
        Assert.All(new[] { demo2, demo3 }, demo => Assert.Contains(demo.Diagnostics, diagnostic => diagnostic.Line > 0 && diagnostic.Reason.Contains("2 sources", StringComparison.Ordinal)));
    }

    [Fact]
    public void Published_tables_preserve_all_slots_aliases_and_provenance()
    {
        DaggerfallQuestTables tables = Definitions().QuestSources.Tables;
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
        Assert.Equal("daggerfall-unity/Assets/StreamingAssets/Tables/Quests-GlobalVars.txt", tables.Globals.SourcePath);
        Assert.Equal("daggerfall-unity/Assets/StreamingAssets/Tables/Quests-StaticMessages.txt", tables.StaticMessages.SourcePath);
        Assert.All(tables.Globals.Rows.Concat(tables.StaticMessages.Rows), row => Assert.True(row.SourceLine > 0));
    }

    [Fact]
    public void Replacing_quest_sections_admits_and_refuses_what_the_whole_payload_read_does()
    {
        // A quest-only edit read against the admitted base resolves the same sources as the spliced
        // payload read whole, and every other section stays the admitted one.
        System.Text.Json.Nodes.JsonObject added = TestPayload.Sections("questSources");
        added["questSources"]!["quests"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""
            {"name":"replaced","displayName":"Replaced","sourceFile":"replaced.txt","disposition":"compiled","messages":[{"id":10,"firstLine":1,"lines":["Done."]}],"blocks":[{"kind":"headless","firstLine":2,"lines":["end quest"],"global":null}],"diagnostics":[]}
            """));
        DaggerfallDefinitions whole = DaggerfallBaseContent.Read(TestPayload.Splice(added));
        DaggerfallDefinitions replaced = TestPayload.WithQuestSections(added);
        Assert.Equal(whole.QuestSources.Quests.Keys.Order(StringComparer.Ordinal), replaced.QuestSources.Quests.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(whole.QuestSources.Resources.Count, replaced.QuestSources.Resources.Count);
        Assert.Equal(whole.QuestSources.Tables.Globals.Rows.Count, replaced.QuestSources.Tables.Globals.Rows.Count);
        Assert.Equal(whole.QuestSources.Catalog.Rows.Count, replaced.QuestSources.Catalog.Rows.Count);
        Assert.Equal(["Done."], replaced.QuestSources.Resolve("replaced.txt").Messages.Single().Lines);
        Assert.Same(TestPayload.Definitions.Locations, replaced.Locations);
        Assert.Same(TestPayload.Definitions.Text, replaced.Text);
        Assert.Equal(265, TestPayload.Definitions.QuestSources.Quests.Count);

        // A malformed quest edit is refused with the diagnostics the whole read reports.
        System.Text.Json.Nodes.JsonObject malformed = TestPayload.Sections("questSources");
        malformed["questSources"]!["quests"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""
            {"name":"flawed","displayName":"","sourceFile":"flawed.txt","disposition":"compiled","messages":[],"blocks":[],"diagnostics":[{"line":1,"text":"x","reason":"y"}]}
            """));
        DaggerfallContentException wholeFailure = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(malformed)));
        DaggerfallContentException replacedFailure = Assert.Throws<DaggerfallContentException>(() => TestPayload.WithQuestSections(malformed));
        Assert.Equal(wholeFailure.Diagnostics, replacedFailure.Diagnostics);
        Assert.Contains("Quest 'flawed' is compiled with diagnostics attached.", replacedFailure.Diagnostics);

        // Only quest sections can be replaced this way; another section would be silently ignored.
        DaggerfallContentException foreign = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.ReadQuestSections(
            TestPayload.Definitions, System.Text.Encoding.UTF8.GetBytes("""{"rumors":{}}""")));
        Assert.Contains(foreign.Diagnostics, diagnostic => diagnostic.Contains("'rumors' is not a quest section", StringComparison.Ordinal));
    }

    private static DaggerfallDefinitions Definitions()
    {
        return TestPayload.Definitions;
    }
}
