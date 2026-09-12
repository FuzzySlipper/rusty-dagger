using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The catalog contract: the classic career record decodes, the documented catalogs
/// build from the supplied corpus, and a citation or cross-reference the sources cannot
/// back is refused rather than published.
/// </summary>
public sealed class DaggerfallCatalogTests
{
    [Fact]
    public void Decodes_a_supplied_career_record()
    {
        ClassCfgRecord career = ClassCfgDecoder.Decode(ReadClass("CLASS00.CFG"), "CLASS00.CFG");

        // The first supplied record is the Mage template: the magic primaries, the lowest
        // hit points per level and the high intelligence and willpower the corpus carries.
        Assert.Equal("Mage", career.Name);
        Assert.Equal([27, 25, 26], new[] { career.PrimarySkill1, career.PrimarySkill2, career.PrimarySkill3 });
        Assert.Equal(6, career.HitPointsPerLevel);
        Assert.Equal(1.0390625f, career.AdvancementMultiplier, 6);
        Assert.Equal(8, career.Attributes.Length);
        Assert.Equal(42, career.Attributes[0]);
        Assert.Equal(65, career.Attributes[2]);
    }

    [Fact]
    public void Decodes_every_supplied_career_record()
    {
        string[] files = [.. Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "local/arena2"), "CLASS*.CFG").Order(StringComparer.Ordinal)];

        Assert.Equal(19, files.Length);
        foreach (string file in files)
        {
            ClassCfgRecord career = ClassCfgDecoder.Decode(File.ReadAllBytes(file), Path.GetFileName(file));
            Assert.False(string.IsNullOrWhiteSpace(career.Name));
            Assert.True(career.HitPointsPerLevel > 0);
            Assert.True(career.AdvancementMultiplier > 0f);
        }
    }

    [Fact]
    public void Rejects_a_career_record_that_is_not_the_classic_length()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ClassCfgDecoder.Decode(new byte[73], "CLASS00.CFG"));

        Assert.Equal("CLASS00.CFG", error.SourceName);
        Assert.Contains("74", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_career_record_naming_a_skill_beyond_the_index_space()
    {
        byte[] bytes = ReadClass("CLASS00.CFG");
        // The major-skill slot at byte 19 names skill 36, past the terminal no-skill value.
        bytes[19] = 36;

        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ClassCfgDecoder.Decode(bytes, "CLASS00.CFG"));

        Assert.Equal(19, error.Offset);
        Assert.Contains("skill index 36", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_career_record_without_a_printable_name()
    {
        byte[] bytes = ReadClass("CLASS00.CFG");
        bytes[28] = 0;

        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ClassCfgDecoder.Decode(bytes, "CLASS00.CFG"));

        Assert.Equal(28, error.Offset);
    }

    [Fact]
    public void Builds_the_documented_catalogs_from_the_supplied_corpus()
    {
        DaggerfallCatalogs catalogs = BuildFromRepository();

        Assert.Equal(8, catalogs.Races.Count);
        Assert.Equal(19, catalogs.Careers.Count);
        Assert.Equal(8, catalogs.Attributes.Count);
        Assert.Equal(35, catalogs.Skills.Count);
        Assert.Equal(5, catalogs.Resistances.Count);
        // Every pack actor except the player is an enemy reference the catalog resolves.
        Assert.Equal(44, catalogs.Enemies.Count);
        Assert.Equal(31, catalogs.ItemTemplates.Count);
        // Every record cites a documented inventory record, which the build validated.
        Assert.All(catalogs.Careers, career => Assert.StartsWith("CNT-010.file.CLASS", career.Source.RecordId, StringComparison.Ordinal));
        Assert.All(catalogs.Races, race => Assert.Equal("CNT-009", race.Source.RecordId));
        // Two supplied records are both named Knight, so the collision is recorded and the
        // carrier's own file identity is the key.
        Assert.Equal(["Knight"], catalogs.CareerNameCollisions);
        Assert.Equal("Knight", catalogs.Careers.Single(career => career.Id == "class17").Name);
        Assert.Equal("Knight", catalogs.Careers.Single(career => career.Id == "class18").Name);
        // The supplied Knight record holds the terminal no-skill value in a major slot, so
        // it contributes two major skills rather than a key nothing can resolve.
        Assert.Equal(["etiquette", "dodging"], catalogs.Careers.Single(career => career.Id == "class18").MajorSkills);
        Assert.Equal("Mage", catalogs.Careers.Single(career => career.Id == "class00").Name);
        Assert.Equal(["mysticism", "alteration", "thaumaturgy"], catalogs.Careers.Single(career => career.Id == "class00").PrimarySkills);
    }

    [Fact]
    public void Refuses_a_career_carrier_the_documented_inventory_does_not_carry()
    {
        IReadOnlyList<SourceInventoryRow> inventory = [.. ReadInventory().Where(row => row.Id != "CNT-010.file.CLASS18.CFG")];

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => DaggerfallCatalogBuilder.Build(
            inventory, VocabularyAttributes(), VocabularySkills(), ReadCareers(), EnemyIds(), ItemIds()));

        Assert.Contains("CLASS18.CFG", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Publishes_resistance_and_immunity_elements_from_the_classic_effect_flags()
    {
        DaggerfallCatalogs catalogs = BuildFromRepository();

        // The classic bytes are EffectFlags, not element indices: fire is 8, frost 16,
        // poison 4, disease 64, shock 32, magic 2 and paralysis 1. Every one of the four
        // non-zero flag cases in the supplied corpus is checked here.
        DaggerfallCareerRecord monk = catalogs.Careers.Single(career => career.Id == "class12");
        Assert.Equal(34, monk.ResistanceFlags);
        Assert.Equal(["shock", "magic"], monk.ResistanceElements);
        Assert.Equal(0, monk.ImmunityFlags);
        Assert.Empty(monk.ImmunityElements);
        DaggerfallCareerRecord barbarian = catalogs.Careers.Single(career => career.Id == "class15");
        Assert.Equal(4, barbarian.ImmunityFlags);
        Assert.Equal(["disease-or-poison"], barbarian.ImmunityElements);
        // Both Knights are immune to paralysis, which is not one of the five elements, so
        // the element list is empty and the byte keeps the bit for the task that owns it.
        foreach (string id in (string[])["class17", "class18"])
        {
            DaggerfallCareerRecord knight = catalogs.Careers.Single(career => career.Id == id);
            Assert.Equal(1, knight.ImmunityFlags);
            Assert.Empty(knight.ImmunityElements);
        }

        // Every career's element lists are exactly the interpretation of its own bytes.
        foreach (DaggerfallCareerRecord career in catalogs.Careers)
        {
            Assert.Equal(ExpectedElements(career.ResistanceFlags), career.ResistanceElements);
            Assert.Equal(ExpectedElements(career.ImmunityFlags), career.ImmunityElements);
        }
    }

    private static List<string> ExpectedElements(int flags)
    {
        string[] keys = ["fire", "frost", "disease-or-poison", "shock", "magic"];
        int[] masks = [8, 16, 4 | 64, 32, 2];
        return [.. keys.Where((_, index) => (flags & masks[index]) != 0)];
    }

    [Fact]
    public void The_published_career_carries_its_data_and_no_computed_views()
    {
        // The published record is the carrier the runtime reads, so it must not carry
        // computed views: a view serialized by accident published an array of empty
        // objects beside the values it was derived from.
        System.Text.Json.Nodes.JsonObject career = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(PackPath()))!
            ["catalogs"]!["careers"]!.AsArray()
            .Select(value => value!.AsObject())
            .First(value => value["id"]!.GetValue<string>() == "class00");

        Assert.DoesNotContain("flagBytes", career.Select(property => property.Key));
        Assert.DoesNotContain("skillReferences", career.Select(property => property.Key));
        Assert.Equal(34, System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(PackPath()))!
            ["catalogs"]!["careers"]!.AsArray()
            .Select(value => value!.AsObject())
            .Single(value => value["id"]!.GetValue<string>() == "class12")["resistanceFlags"]!.GetValue<int>());
    }

    [Fact]
    public void The_published_catalogs_cover_every_pack_key_they_reference()
    {
        DaggerfallCatalogs catalogs = BuildFromRepository();

        // The reference catalogs are a view of keys the pack defines, so the *published*
        // pack must cover every one of them: an actor or item added without re-running
        // the builder would otherwise leave a consumer resolving a key the catalog the
        // pack actually carries does not list. Comparing the builder's own inputs with
        // its own output would prove nothing, so this reads the pack file.
        System.Text.Json.Nodes.JsonNode published = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(PackPath()))!;
        string[] publishedEnemies = [.. published["catalogs"]!["enemies"]!.AsArray().Select(value => value!["id"]!.GetValue<string>())];
        string[] publishedItems = [.. published["catalogs"]!["itemTemplates"]!.AsArray().Select(value => value!["id"]!.GetValue<string>())];
        Assert.Equal(EnemyIds().Order(StringComparer.Ordinal), publishedEnemies.Order(StringComparer.Ordinal));
        Assert.Equal(ItemIds().Order(StringComparer.Ordinal), publishedItems.Order(StringComparer.Ordinal));
        // And the builder reproduces that published section.
        Assert.Equal(publishedEnemies, catalogs.Enemies.Select(enemy => enemy.Id));
        Assert.Equal(publishedItems, catalogs.ItemTemplates.Select(item => item.Id));
    }

    [Fact]
    public void Refuses_a_catalog_citing_a_source_the_inventory_does_not_carry()
    {
        DaggerfallCatalogs catalogs = BuildFromRepository();
        DaggerfallCatalogs invented = catalogs with
        {
            Races = [.. catalogs.Races.Select((race, index) => index == 0 ? race with { Source = new DaggerfallCatalogSource("CNT-999", "local/arena2/nowhere") } : race)],
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            invented.Validate(ReadInventory().Select(row => row.Id).ToHashSet(StringComparer.Ordinal)));

        Assert.Contains("CNT-999", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_career_naming_a_skill_the_catalog_does_not_carry()
    {
        DaggerfallCatalogs catalogs = BuildFromRepository();
        DaggerfallCareerRecord career = catalogs.Careers[0];
        DaggerfallCatalogs dangling = catalogs with
        {
            Careers = [career with { PrimarySkills = ["not-a-skill", .. career.PrimarySkills.Skip(1)] }, .. catalogs.Careers.Skip(1)],
        };
        IReadOnlySet<string> inventoryIds = ReadInventory().Select(row => row.Id).ToHashSet(StringComparer.Ordinal);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => dangling.Validate(inventoryIds));

        Assert.Contains("not-a-skill", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_catalog_keys_whose_indices_are_not_contiguous()
    {
        DaggerfallCatalogs catalogs = BuildFromRepository();
        DaggerfallCatalogs sparse = catalogs with
        {
            // Index 99 leaves the others untouched and makes the index space non-contiguous.
            Attributes = [catalogs.Attributes[0] with { Index = 99 }, .. catalogs.Attributes.Skip(1)],
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            sparse.Validate(ReadInventory().Select(row => row.Id).ToHashSet(StringComparer.Ordinal)));

        Assert.Contains("contiguous", error.Message, StringComparison.Ordinal);
    }

    private static DaggerfallCatalogs BuildFromRepository() => DaggerfallCatalogBuilder.Build(
        ReadInventory(), VocabularyAttributes(), VocabularySkills(), ReadCareers(), EnemyIds(), ItemIds());

    private static IReadOnlyList<SourceInventoryRow> ReadInventory() =>
        SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")));

    private static List<(string FileName, byte[] Bytes)> ReadCareers() =>
        [.. Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "local/arena2"), "CLASS*.CFG")
            .Order(StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), File.ReadAllBytes(path)))];

    private static byte[] ReadClass(string name) => File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2", name));

    private static List<string> VocabularyAttributes() =>
        [.. System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(PackPath()))!["vocabulary"]!["attributes"]!.AsArray().Select(value => value!.GetValue<string>())];

    private static List<string> VocabularySkills() =>
        [.. System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(PackPath()))!["vocabulary"]!["skills"]!.AsArray().Select(value => value!.GetValue<string>())];

    private static List<string> EnemyIds() =>
        [.. System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(PackPath()))!["actors"]!.AsArray()
            .Select(value => value!.AsObject()["id"]!.GetValue<string>())
            .Where(id => id != "player")];

    private static List<string> ItemIds() =>
        [.. System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(PackPath()))!["items"]!.AsArray().Select(value => value!.AsObject()["id"]!.GetValue<string>())];

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
