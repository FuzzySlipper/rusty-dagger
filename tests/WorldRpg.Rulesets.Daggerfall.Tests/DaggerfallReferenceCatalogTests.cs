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
