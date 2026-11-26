using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Publication;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published reference catalogs as a consumer sees them: representative keys resolve,
/// and a pack whose keys or cross-references do not hold together is refused
/// rather than handed to a consumer that would resolve nothing.
/// </summary>
public sealed class DaggerfallReferenceCatalogTests
{
    [Fact]
    public void Resolves_race_career_and_enemy_keys_through_the_published_pack()
    {
        DaggerfallDefinitions definitions = ReadPack();

        Assert.Equal(8, definitions.Catalogs.Races.Count);
        Assert.Equal(19, definitions.Catalogs.Careers.Count);
        Assert.Equal("breton", definitions.Catalogs.Races[0].Id);
        Assert.Equal(1, definitions.Catalogs.RequireRace("breton").DonorRaceId);
        Assert.Equal(8, definitions.Catalogs.RequireRace("argonian").DonorRaceId);
        Assert.False(definitions.Catalogs.TryGetRace("vampire", out _));
        Assert.Equal("Mage", definitions.Catalogs.RequireCareer("class00").Name);
        Assert.Equal(["mysticism", "alteration", "thaumaturgy"], definitions.Catalogs.RequireCareer("class00").PrimarySkills);
        Assert.Contains("forbidden-weapon:long-blade", definitions.Catalogs.RequireCareer("class00").ForbiddenEquipment);
        Assert.False(definitions.Catalogs.TryGetCareer("class19", out _));
        // The indices are the vocabulary's own order, so a consumer can go from a record
        // byte to the key a catalog publishes without a second table.
        Assert.Equal(definitions.Vocabulary.Skills.Select(id => id.Value), definitions.Catalogs.Skills.Select(key => key.Id));
        Assert.Equal(Enumerable.Range(0, 35), definitions.Catalogs.Skills.Select(key => key.Index));
        Assert.Equal(Enumerable.Range(0, 5), definitions.Catalogs.Resistances.Select(key => key.Index));
        // A name two careers share cannot be a key, and resolving by it says so.
        Assert.Throws<InvalidOperationException>(() => definitions.Catalogs.RequireCareerByName("Knight"));
        Assert.Equal("Healer", definitions.Catalogs.RequireCareerByName("Healer").Name);
    }

    [Fact]
    public void The_composed_pack_path_reads_and_validates_the_catalogs()
    {
        // The clause is that a consumer receives normalized values through existing pack
        // resolution, so this drives the real composition: the payload the bundle names
        // is read and validated where the ruleset builds its definitions, and a payload
        // whose catalogs do not hold together fails there rather than at first use.
        ResolvedGameComposition composition = TestSessions
            .Resolve(Content(null), new GameBundleId("daggerfall.classic"))
            .RequireComposition();
        ContentPack basePack = composition.RequireContentPack(new ContentPackId("daggerfall.base"));
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(
            DaggerfallBaseContent.Files(composition.Content, basePack.Id.Value, basePack.PayloadPath, basePack.Payload),
            composition.RequireContentPack(new ContentPackId("daggerfall.imported")).Payload, DaggerfallData.For(composition));
        Assert.Equal(19, definitions.Catalogs.Careers.Count);

        ResolvedGameComposition broken = TestSessions
            .Resolve(Content(root => root["catalogs"]!["careers"]!.AsArray()[0]!["primarySkills"]!.AsArray()[0] = 35), new GameBundleId("daggerfall.classic"))
            .RequireComposition();
        ContentPack brokenPack = broken.RequireContentPack(new ContentPackId("daggerfall.base"));
        DaggerfallBasePackFiles brokenBase = DaggerfallBaseContent.Files(broken.Content, brokenPack.Id.Value, brokenPack.PayloadPath, brokenPack.Payload);
        ReadOnlyMemory<byte> importedPayload = broken.RequireContentPack(new ContentPackId("daggerfall.imported")).Payload;
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => { DaggerfallBaseContent.Read(brokenBase, importedPayload, DaggerfallData.For(broken)); });
        Assert.Contains("skill 35", error.Message, StringComparison.Ordinal);
    }

    private static ProductContent Content(Action<JsonObject>? change) => change is null
        ? TestSessions.FullContent(TestData.RepositoryRoot)
        : TestSessions.FullContentWithEntry(TestData.RepositoryRoot, "worldrpg/packs/daggerfall.base/world/catalogs.json", bytes =>
        {
            JsonObject pack = new() { ["catalogs"] = JsonNode.Parse(bytes) };
            change(pack);
            return Encoding.UTF8.GetBytes(pack["catalogs"]!.ToJsonString(PublishedJson.SectionCompact));
        });

    [Theory]
    [InlineData("duplicate career id")]
    [InlineData("duplicate race id")]
    [InlineData("duplicate race donor value")]
    [InlineData("non-positive race donor value")]
    [InlineData("career with no primary skill")]
    [InlineData("career naming nine attributes")]
    [InlineData("career naming one skill twice")]
    [InlineData("career with unknown equipment restriction")]
    public void Rejects_a_pack_whose_catalogs_do_not_hold_together(string mutation)
    {
        // Each of these is a pack a hand edit can produce and the builder never would.
        // Every one must be a content diagnostic naming the cause, never an unhandled
        // argument failure escaping the read.
        DaggerfallContentException error = Mutate(pack =>
        {
            JsonObject catalogs = pack["catalogs"]!.AsObject();
            JsonArray careers = catalogs["careers"]!.AsArray();
            switch (mutation)
            {
                case "duplicate career id":
                    careers[1]!["id"] = careers[0]!["id"]!.GetValue<string>();
                    break;
                case "duplicate race id":
                    catalogs["races"]!.AsArray()[1]!["id"] = catalogs["races"]!.AsArray()[0]!["id"]!.GetValue<string>();
                    break;
                case "duplicate race donor value":
                    catalogs["races"]!.AsArray()[1]!["donorRaceId"] = catalogs["races"]!.AsArray()[0]!["donorRaceId"]!.GetValue<int>();
                    break;
                case "non-positive race donor value":
                    catalogs["races"]!.AsArray()[0]!["donorRaceId"] = 0;
                    break;
                case "career with no primary skill":
                    careers[0]!["primarySkills"] = new JsonArray();
                    break;
                case "career naming nine attributes":
                    JsonArray attributes = careers[0]!["attributeValues"]!.AsArray();
                    attributes.Add(attributes[0]!.GetValue<int>());
                    break;
                case "career naming one skill twice":
                    careers[0]!["majorSkills"]!.AsArray()[0] = careers[0]!["primarySkills"]!.AsArray()[0]!.GetValue<int>();
                    break;
                case "career with unknown equipment restriction":
                    careers[0]!["forbiddenEquipment"]!.AsArray()[0] = "forbidden-material:moonstone";
                    break;
            }
        });

        Assert.NotNull(error.Message);
    }

    [Fact]
    public void Rejects_a_career_naming_a_skill_the_catalog_does_not_carry()
    {
        DaggerfallContentException error = Mutate(pack =>
            pack["catalogs"]!["careers"]!.AsArray()[0]!["primarySkills"]!.AsArray()[0] = 35);

        Assert.Contains("skill 35", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_vocabulary_edit_names_every_careers_skills()
    {
        // A career names its skills by classic index and the vocabulary names the indices when the ruleset loads, so an
        // edit to the vocabulary reaches every career with the careers left as they are.
        JsonObject payload = TestPayload.Sections("vocabulary");
        JsonArray skills = payload["vocabulary"]!["skills"]!.AsArray();
        List<string> names = [.. skills.Select(skill => skill!.GetValue<string>())];
        int mysticism = names.IndexOf("mysticism"), alteration = names.IndexOf("alteration");
        skills[mysticism] = "alteration";
        skills[alteration] = "mysticism";

        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(TestPayload.Splice(payload));

        Assert.Equal(["alteration", "mysticism", "thaumaturgy"], definitions.Catalogs.RequireCareer("class00").PrimarySkills);
        Assert.Equal(mysticism, definitions.Catalogs.Skills.Single(key => key.Id == "alteration").Index);
    }

    [Fact]
    public void Rejects_catalog_keys_that_are_not_indexed_uniquely()
    {
        DaggerfallContentException error = Mutate(pack =>
            pack["catalogs"]!["resistances"]!.AsArray()[1]!["index"] = 0);

        Assert.Contains("resistances", error.Message, StringComparison.Ordinal);
        Assert.Contains("indices", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_element_lists_that_disagree_with_the_carriers_own_flags()
    {
        // The published elements are an interpretation of a published byte, so a pack
        // whose list contradicts its own flags is refused rather than handed to a
        // consumer that would grant immunity the carrier never did.
        DaggerfallContentException error = Mutate(pack =>
        {
            System.Text.Json.Nodes.JsonObject monk = pack["catalogs"]!["careers"]!.AsArray()
                .Select(value => value!.AsObject())
                .Single(career => career["id"]!.GetValue<string>() == "class12");
            monk["resistanceElements"] = new JsonArray("frost");
        });

        Assert.Contains("class12", error.Message, StringComparison.Ordinal);
        Assert.Contains("shock", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_pack_without_published_catalogs()
    {
        DaggerfallContentException error = Mutate(pack => pack.Remove("catalogs"));

        Assert.Contains("catalogs", error.Message, StringComparison.Ordinal);
    }

    private static DaggerfallContentException Mutate(Action<JsonObject> change)
    {
        JsonObject pack = TestPayload.Sections("catalogs");
        change(pack);
        byte[] payload = pack.ContainsKey("catalogs")
            ? TestPayload.Splice(pack)
            : TestPayload.Without("catalogs");
        return Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(payload));
    }

    private static DaggerfallDefinitions ReadPack() => TestPayload.Definitions;

}
