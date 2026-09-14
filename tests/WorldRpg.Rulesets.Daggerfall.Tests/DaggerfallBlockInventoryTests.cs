using System.Text.Json;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published block inventory as the product ships it: every record the archive declares, what its
/// name says it is, and whether the block names the location section publishes are all carried.
/// </summary>
/// <remarks>
/// The section has no runtime reader yet — the tasks that publish dungeon, exterior and geometry
/// assemblies are its consumers — so what is checked here is the artifact itself, from its own published
/// numbers, rather than through a reader invented for a consumer that does not exist.
/// </remarks>
public sealed class DaggerfallBlockInventoryTests
{
    [Fact]
    public void The_published_inventory_accounts_for_every_record_the_archive_declares()
    {
        using JsonDocument pack = Pack();
        JsonElement blocks = pack.RootElement.GetProperty("blocks");
        JsonElement[] records = [.. blocks.GetProperty("records").EnumerateArray()];

        Assert.Equal(1295, records.Length);
        Assert.Equal(1295, blocks.GetProperty("sources")[0].GetProperty("declaredLength").GetInt32());
        Assert.Equal(1295, blocks.GetProperty("sources")[0].GetProperty("records").GetInt32());
        Assert.Equal(32884997L, blocks.GetProperty("sources")[0].GetProperty("byteLength").GetInt64());

        // An identity is the archive's own ordinal, so every one is carried exactly once and in order.
        Assert.Equal(Enumerable.Range(0, 1295), records.Select(record => record.GetProperty("ordinal").GetInt32()));
        Assert.Equal(1295, records.Select(record => record.GetProperty("sourceKey").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(records, record => record.GetProperty("state").GetString() != "read");

        // The kind and the disposition are functions of the name, so a consumer can reproduce both from
        // the archive key alone rather than trusting two independent claims.
        Assert.All(records, record =>
        {
            string key = record.GetProperty("sourceKey").GetString()!;
            string kind = record.GetProperty("kind").GetString()!;
            string fromName = Path.GetExtension(key).ToLowerInvariant() switch
            {
                ".rmb" => "rmb",
                ".rdb" => "rdb",
                ".rdi" => "rdi",
                _ => "unknown",
            };

            Assert.Equal(fromName, kind);
            Assert.Equal(Expected(kind), record.GetProperty("disposition").GetString());
        });
    }

    [Fact]
    public void Every_published_city_block_header_accounts_for_its_own_bytes()
    {
        // The header's own arithmetic is republished so it can be checked without the archive: the fixed
        // header, the sub-records the block declares, the objects that follow them and whatever the record
        // carries past both have to add up to the record's own length. This is what makes the summary's
        // offsets positions rather than guesses, and the supplied corpus satisfies it exactly.
        JsonElement[] city = [.. Records("rmb")];
        Assert.Equal(920, city.Length);
        int buildings = 0;
        int paddings = 0;

        foreach (JsonElement record in city)
        {
            JsonElement header = record.GetProperty("rmb");
            int accounted = 6776 + header.GetProperty("trailingBytes").GetInt32()
                + (header.GetProperty("misc3dObjects").GetInt32() * 66)
                + (header.GetProperty("miscFlatObjects").GetInt32() * 17)
                + header.GetProperty("buildings").EnumerateArray().Sum(building => building.GetProperty("byteLength").GetInt32());
            Assert.Equal(record.GetProperty("byteLength").GetInt32(), accounted);

            // A block states a name for itself, and the header's shape is established by that name being
            // the archive key the record is stored under.
            Assert.Equal(record.GetProperty("sourceKey").GetString(), header.GetProperty("name").GetString());
            Assert.Equal(header.GetProperty("declaredBlocks").GetInt32(), header.GetProperty("buildings").GetArrayLength());

            // Each sub-record is a pair of halves, and the bytes those halves declare plus their two
            // headers and the padding they reserve have to account for exactly the size the sub-record
            // reserves. This is the check that a published building cannot describe a shape it cannot have.
            foreach (JsonElement building in header.GetProperty("buildings").EnumerateArray())
            {
                int halves = 34 + Half(building.GetProperty("exterior")) + Half(building.GetProperty("interior"));
                Assert.Equal(building.GetProperty("byteLength").GetInt32(), halves + building.GetProperty("paddingBytes").GetInt32());
                Assert.InRange(building.GetProperty("paddingBytes").GetInt32(), 0, 1);
                buildings++;
                paddings += building.GetProperty("paddingBytes").GetInt32();
            }
        }

        Assert.Equal(9005, buildings);
        Assert.Equal(5549, paddings);
    }

    [Fact]
    public void Publishes_the_slot_values_and_both_halves_the_source_states()
    {
        // A building's faction sits eighteen bytes into its twenty-six-byte slot, after four uninterpreted
        // words; reading it a word early states zero for every building in the corpus. And a sub-record's
        // counts are a pair: the outside half alone understates every building with an inside.
        using JsonDocument pack = Pack();
        JsonElement[] city = [.. Records("rmb")];
        JsonElement wall = city.Single(record => record.GetProperty("sourceKey").GetString() == "WALLAA03.RMB");
        JsonElement slot = wall.GetProperty("rmb").GetProperty("buildings")[0];

        Assert.Equal("rmb", wall.GetProperty("kind").GetString());
        Assert.Equal(615, slot.GetProperty("byteLength").GetInt32());
        Assert.Equal(0, slot.GetProperty("paddingBytes").GetInt32());
        Assert.Equal(1, slot.GetProperty("exterior").GetProperty("objects").GetInt32());
        Assert.Equal(6, slot.GetProperty("interior").GetProperty("objects").GetInt32());
        Assert.Equal(7, slot.GetProperty("interior").GetProperty("flats").GetInt32());

        JsonElement mark = city.Single(record => record.GetProperty("sourceKey").GetString() == "MARKAA00.RMB");
        Assert.Equal(510, mark.GetProperty("rmb").GetProperty("buildings")[1].GetProperty("factionId").GetInt32());

        JsonElement[] slots = [.. city.SelectMany(record => record.GetProperty("rmb").GetProperty("buildings").EnumerateArray())];
        Assert.Equal(460, slots.Count(building => building.GetProperty("factionId").GetInt32() != 0));
        Assert.Equal(6832, slots.Count(building => Half(building.GetProperty("interior")) != 0));
        Assert.Equal(65535, slots.Max(building => building.GetProperty("factionId").GetInt32()));
    }

    /// <summary>The bytes the records one published half's counts declare occupy.</summary>
    private static int Half(JsonElement counts) =>
        (counts.GetProperty("objects").GetInt32() * 66)
        + (counts.GetProperty("flats").GetInt32() * 17)
        + (counts.GetProperty("sections").GetInt32() * 16)
        + (counts.GetProperty("people").GetInt32() * 17)
        + (counts.GetProperty("doors").GetInt32() * 19);

    [Fact]
    public void Keeps_both_donor_indices_where_one_prefix_names_two_kinds()
    {
        // The donor's prefix table carries TEMP twice, so a name beginning with it could have come from
        // either temple kind. Publishing one index would be a classification the source does not support.
        JsonElement[] temples = [.. Records("rmb")
            .Where(record => record.GetProperty("rmbName").ValueKind == JsonValueKind.Object)
            .Where(record => record.GetProperty("rmbName").GetProperty("prefix").GetString() == "TEMP")];

        Assert.Equal(10, temples.Length);
        Assert.All(temples, record => Assert.Equal([13, 14], record.GetProperty("rmbName").GetProperty("tableIndices").EnumerateArray().Select(index => index.GetInt32())));
    }

    [Fact]
    public void Classifies_the_records_the_donor_reads_and_ignores()
    {
        // A dungeon block index is five hundred and twelve bytes of unknown data the donor's own descriptor
        // says to ignore, and one archive record carries no extension the donor recognises at all. Neither
        // is silently dropped: both are carried with the disposition that says what the donor does with them.
        JsonElement[] indexes = [.. Records("rdi")];
        Assert.Equal(187, indexes.Length);
        Assert.All(indexes, record => Assert.Equal("donorUnsupported", record.GetProperty("disposition").GetString()));
        Assert.All(indexes, record => Assert.Equal(512, record.GetProperty("byteLength").GetInt32()));

        JsonElement[] unknown = [.. Records("unknown")];
        JsonElement record0 = Assert.Single(unknown);
        Assert.Equal("FOO", record0.GetProperty("sourceKey").GetString());
        Assert.Equal("unknownKind", record0.GetProperty("disposition").GetString());
    }

    [Fact]
    public void Every_block_the_location_section_names_is_carried_by_the_inventory()
    {
        // This is the cross-check the task asks for: the dungeon records the location publication carries
        // name their blocks, and every one of those names has to be an identity the inventory publishes.
        // A name that resolved to nothing would leave a dungeon referencing a block no task can read.
        using JsonDocument pack = Pack();
        string[] inventory = [.. pack.RootElement.GetProperty("blocks").GetProperty("records").EnumerateArray()
            .Select(record => record.GetProperty("sourceKey").GetString()!)];

        string[] referenced = [.. pack.RootElement.GetProperty("locations").GetProperty("dungeons").EnumerateArray()
            .SelectMany(dungeon => dungeon.GetProperty("blocks").EnumerateArray())
            .Select(block => block.GetString()!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(179, referenced.Length);
        Assert.Empty(referenced.Except(inventory, StringComparer.Ordinal));
        Assert.All(referenced, name => Assert.Contains(name, inventory));
    }

    /// <summary>The disposition a record of one kind carries, which the reader derives from the kind.</summary>
    private static string Expected(string kind) => kind switch
    {
        "rmb" or "rdb" => "summarized",
        "rdi" => "donorUnsupported",
        _ => "unknownKind",
    };

    private static IEnumerable<JsonElement> Records(string kind) =>
        Pack().RootElement.GetProperty("blocks").GetProperty("records").EnumerateArray().Where(record => record.GetProperty("kind").GetString() == kind).ToArray();

    private static JsonDocument Pack() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));

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
