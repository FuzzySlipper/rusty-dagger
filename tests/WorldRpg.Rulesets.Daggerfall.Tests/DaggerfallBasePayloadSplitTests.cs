using System.Text;
using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The base definitions come from the authored daggerfall.base payload and the generated daggerfall.imported
/// payload; each section has exactly one of them as its owner.
/// </summary>
public sealed class DaggerfallBasePayloadSplitTests
{
    [Fact]
    public void A_section_both_payloads_carry_is_refused_with_its_name()
    {
        byte[] authored = Encoding.UTF8.GetBytes("""{ "ruleset": "daggerfall", "vocabulary": { "attributes": [] } }""");
        byte[] imported = Encoding.UTF8.GetBytes("""{ "catalogs": {}, "vocabulary": { "attributes": [] } }""");

        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Combine(authored, imported));

        Assert.Contains("Section 'vocabulary' is in both the authored and the imported base payload", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_joined_root_keeps_every_section_of_both_payloads_as_written()
    {
        byte[] authored = Encoding.UTF8.GetBytes("""{ "ruleset": "daggerfall", "items": [ { "id": "dagger" } ] }""");
        byte[] imported = Encoding.UTF8.GetBytes("""{ "catalogs": { "races": [] } }""");

        using JsonDocument joined = JsonDocument.Parse(DaggerfallBaseContent.Combine(authored, imported));

        Assert.Equal(["ruleset", "items", "catalogs"], joined.RootElement.EnumerateObject().Select(section => section.Name));
        Assert.Equal("""[ { "id": "dagger" } ]""", joined.RootElement.GetProperty("items").GetRawText());
    }

    [Fact]
    public void The_tracked_authored_payload_and_the_imported_payload_share_no_section()
    {
        string payloads = Path.Combine(TestData.RepositoryRoot, "content", "worldrpg", "payloads");
        using JsonDocument authored = JsonDocument.Parse(TestContentFiles.ReadCopy(Path.Combine(payloads, "daggerfall.base.json")));
        using JsonDocument imported = JsonDocument.Parse(TestContentFiles.ReadCopy(Path.Combine(payloads, "daggerfall.imported.json")));

        Assert.Equal(
            ["ruleset", "enemyRanged", "vocabulary", "armorValuesByMaterial", "actors", "authoredItemTemplates", "items", "equipmentSlots", "actions", "lootTables", "hudResources", "donorErrata", "encounters", "assembledSites", "newGame", "enemySpells", "dialogueWorldRules", "help", "questJournalTitles"],
            authored.RootElement.EnumerateObject().Select(section => section.Name));
        Assert.Empty(imported.RootElement.EnumerateObject().Select(section => section.Name)
            .Intersect(authored.RootElement.EnumerateObject().Select(section => section.Name)));
        // Importer records nothing at runtime reads stay outside the runtime content root.
        Assert.DoesNotContain(imported.RootElement.EnumerateObject(), section => section.Name is "geometry" or "questOriginalSources" or "blocks");
    }

    [Fact]
    public void Help_paragraphs_name_controls_by_their_action_and_an_action_no_control_has_is_refused()
    {
        using JsonDocument root = JsonDocument.Parse("""
            { "help": { "pages": [ { "title": "Moving", "paragraphs": ["Hold {run} to run, and {jump} to jump.", "Press {fly} to fly."] } ] } }
            """);
        DaggerfallContentDiagnostics diagnostics = new();

        DaggerfallHelpPage page = Assert.Single(DaggerfallBaseContent.ReadHelp(root.RootElement, diagnostics));

        Assert.Equal("Moving", page.Title);
        Assert.Equal([new("Hold ", null), new(null, "run"), new(" to run, and ", null), new(null, "jump"), new(" to jump.", null)], page.Paragraphs[0]);
        Assert.Contains("names the control 'fly', which no action has",
            Assert.Throws<DaggerfallContentException>(diagnostics.ThrowIfAny).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_authored_help_names_only_controls_the_player_can_bind()
    {
        Assert.NotEmpty(TestPayload.Definitions.Help);
        Assert.All(TestPayload.Definitions.Help.SelectMany(page => page.Paragraphs).SelectMany(paragraph => paragraph),
            segment => Assert.True(segment.Action is null || DaggerfallControlSettings.Catalog.Any(action => action.Id == segment.Action)));
    }
}
