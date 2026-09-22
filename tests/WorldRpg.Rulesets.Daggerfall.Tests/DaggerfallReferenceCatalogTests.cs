using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published reference catalogs as a consumer sees them: representative keys resolve,
/// and a pack whose keys, cross-references or citations do not hold together is refused
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
        // An enemy reference resolves to the actor another part of the pack defines.
        Assert.All(definitions.Catalogs.Enemies, enemy => Assert.True(definitions.Actors.ContainsKey(new DaggerfallActorId(enemy.Id))));
        Assert.All(definitions.Catalogs.ItemTemplates, item => Assert.True(definitions.Items.ContainsKey(new DaggerfallItemId(item.Id))));
        // The two namespaces this contract declares name the tasks that fill them.
        Assert.Equal([7938, 7967], definitions.Catalogs.Pending.Select(pending => pending.OwnerTask).Order());
        // A name two careers share cannot be a key, and resolving by it says so.
        Assert.Equal(["Knight"], definitions.Catalogs.CareerNameCollisions);
        Assert.Throws<InvalidOperationException>(() => definitions.Catalogs.RequireCareerByName("Knight"));
        Assert.Equal("Healer", definitions.Catalogs.RequireCareerByName("Healer").Name);
    }

    [Fact]
    public void Every_published_citation_names_a_catalog_record_and_its_path()
    {
        DaggerfallDefinitions definitions = ReadPack();

        Assert.All(definitions.Catalogs.Careers, career =>
        {
            Assert.StartsWith("CNT-010.file.CLASS", career.Source.SourceRecordId, StringComparison.Ordinal);
            Assert.EndsWith(".CFG", career.Source.Path, StringComparison.Ordinal);
        });
        Assert.All(definitions.Catalogs.Races, race => Assert.Equal("CNT-009", race.Source.SourceRecordId));
        Assert.All(definitions.Catalogs.Skills, key => Assert.Equal("CNT-010", key.Source.SourceRecordId));
    }

    [Fact]
    public void The_composed_pack_path_reads_and_validates_the_catalogs()
    {
        // The clause is that a consumer receives normalized values through existing pack
        // resolution, so this drives the real composition: the payload the bundle names
        // is read and validated where the ruleset builds its definitions, and a payload
        // whose catalogs do not hold together fails there rather than at first use.
        ResolvedGameComposition composition = GameCompositionResolver
            .Resolve(Content(null), new GameBundleId("daggerfall.privateers-hold"))
            .RequireComposition();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(composition.RequireContentPack(new ContentPackId("daggerfall.base")).Payload);
        Assert.Equal(19, definitions.Catalogs.Careers.Count);

        ResolvedGameComposition broken = GameCompositionResolver
            .Resolve(Content(root => root["catalogs"]!["careers"]!.AsArray()[0]!["primarySkills"]!.AsArray()[0] = "not-a-skill"), new GameBundleId("daggerfall.privateers-hold"))
            .RequireComposition();
        ReadOnlyMemory<byte> brokenPayload = broken.RequireContentPack(new ContentPackId("daggerfall.base")).Payload;
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => { DaggerfallBaseContent.Read(brokenPayload); });
        Assert.Contains("not-a-skill", error.Message, StringComparison.Ordinal);
    }

    private static ProductContent Content(Action<JsonObject>? change)
    {
        string contentRoot = Path.Combine(RepositoryRoot(), "content");
        ProductContentFile[] files = [.. Directory.GetFiles(Path.Combine(contentRoot, "worldrpg"), "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (path.EndsWith("daggerfall.base.json", StringComparison.Ordinal) && change is not null)
                {
                    JsonObject pack = JsonNode.Parse(bytes)!.AsObject();
                    change(pack);
                    bytes = System.Text.Encoding.UTF8.GetBytes(pack.ToJsonString());
                }

                return new ProductContentFile(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), bytes);
            })];
        return new ProductContent(files);
    }

    [Theory]
    [InlineData("duplicate career id")]
    [InlineData("duplicate race id")]
    [InlineData("duplicate race donor value")]
    [InlineData("non-positive race donor value")]
    [InlineData("non-positive pending owner")]
    [InlineData("career with no primary skill")]
    [InlineData("career naming nine attributes")]
    [InlineData("career naming one skill twice")]
    [InlineData("career with unknown equipment restriction")]
    [InlineData("citation outside the published sources")]
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
                case "non-positive pending owner":
                    catalogs["pending"]!.AsArray()[0]!["ownerTask"] = 0;
                    break;
                case "career with no primary skill":
                    careers[0]!["primarySkills"] = new JsonArray();
                    break;
                case "career naming nine attributes":
                    JsonArray attributes = careers[0]!["attributes"]!.AsArray();
                    attributes.Add(attributes[0]!.GetValue<string>());
                    break;
                case "career naming one skill twice":
                    careers[0]!["majorSkills"]!.AsArray()[0] = careers[0]!["primarySkills"]!.AsArray()[0]!.GetValue<string>();
                    break;
                case "career with unknown equipment restriction":
                    careers[0]!["forbiddenEquipment"]!.AsArray()[0] = "forbidden-material:moonstone";
                    break;
                case "citation outside the published sources":
                    careers[0]!["source"]!["recordId"] = "CNT-999";
                    break;
            }
        });

        Assert.NotNull(error.Message);
    }

    [Fact]
    public void The_published_pack_states_the_source_records_it_drew_from()
    {
        DaggerfallDefinitions definitions = ReadPack();

        // A consumer can check a citation without owning the inventory, and the runtime
        // refuses a citation outside this set.
        Assert.Equal(23, definitions.Catalogs.SourceRecords.Count);
        Assert.Contains("CNT-009", definitions.Catalogs.SourceRecords);
        Assert.Contains("CNT-010.file.CLASS00.CFG", definitions.Catalogs.SourceRecords);
        Assert.All(definitions.Catalogs.Careers, career => Assert.Contains(career.Source.SourceRecordId, definitions.Catalogs.SourceRecords));
    }

    [Fact]
    public void Rejects_a_career_naming_a_skill_the_catalog_does_not_carry()
    {
        DaggerfallContentException error = Mutate(pack =>
            pack["catalogs"]!["careers"]!.AsArray()[0]!["primarySkills"]!.AsArray()[0] = "not-a-skill");

        Assert.Contains("not-a-skill", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_reference_to_an_actor_the_pack_does_not_define()
    {
        DaggerfallContentException error = Mutate(pack =>
            pack["catalogs"]!["enemies"]!.AsArray()[0]!["id"] = "enemy-the-pack-does-not-define");

        Assert.Contains("enemy-the-pack-does-not-define", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_reference_to_an_item_the_pack_does_not_define()
    {
        DaggerfallContentException error = Mutate(pack =>
            pack["catalogs"]!["itemTemplates"]!.AsArray()[0]!["id"] = "item-the-pack-does-not-define");

        Assert.Contains("item-the-pack-does-not-define", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_catalog_keys_that_are_not_indexed_uniquely()
    {
        DaggerfallContentException error = Mutate(pack =>
            pack["catalogs"]!["attributes"]!.AsArray()[1]!["index"] = 0);

        Assert.Contains("attributes", error.Message, StringComparison.Ordinal);
        Assert.Contains("indices", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_citation_that_is_not_a_documented_record()
    {
        DaggerfallContentException error = Mutate(pack =>
            pack["catalogs"]!["races"]!.AsArray()[0]!["source"]!["recordId"] = "invented-source");

        Assert.Contains("invented-source", error.Message, StringComparison.Ordinal);
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
    public void Rejects_career_name_collisions_that_do_not_match_the_careers()
    {
        DaggerfallContentException error = Mutate(pack =>
            pack["catalogs"]!["careerNameCollisions"] = new JsonArray("Mage"));

        Assert.Contains("collisions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_pack_without_published_catalogs()
    {
        DaggerfallContentException error = Mutate(pack => pack.Remove("catalogs"));

        Assert.Contains("catalogs", error.Message, StringComparison.Ordinal);
    }

    private static DaggerfallContentException Mutate(Action<JsonObject> change)
    {
        JsonObject pack = JsonNode.Parse(File.ReadAllText(PackPath()))!.AsObject();
        change(pack);
        return Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(pack.ToJsonString())));
    }

    private static DaggerfallDefinitions ReadPack() => DaggerfallBaseContent.Read(File.ReadAllBytes(PackPath()));

    private static string PackPath() => Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json");

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
