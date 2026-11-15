using System.Text.Json;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published mesh inventory as the product ships it: every numeric record the archive declares, what
/// its own bytes say, and whether the numbers the block section names are all answered.
/// </summary>
/// <remarks>
/// Nothing at runtime reads the inventory, so the import tool keeps it in its import records outside the runtime
/// content root; what is checked here is the artifact itself, against the block document it joins to.
/// </remarks>
public sealed class DaggerfallGeometryInventoryTests
{
    [Fact]
    public void The_published_inventory_accounts_for_every_record_the_archive_declares()
    {
        using JsonDocument pack = Pack();
        JsonElement geometry = pack.RootElement.GetProperty("geometry");
        JsonElement[] records = [.. geometry.GetProperty("records").EnumerateArray()];

        Assert.Equal(10251, records.Length);
        Assert.Equal(10251, geometry.GetProperty("sources")[0].GetProperty("declaredLength").GetInt32());
        Assert.Equal(10251, geometry.GetProperty("sources")[0].GetProperty("records").GetInt32());
        Assert.Equal(27143532L, geometry.GetProperty("sources")[0].GetProperty("byteLength").GetInt64());
        Assert.Empty(geometry.GetProperty("unresolvedUseSites").EnumerateArray());

        // A record's identity is its directory ordinal, and the archive is not sorted by number: the
        // inventory is ordered the way the file states it and carries every ordinal exactly once.
        Assert.Equal(Enumerable.Range(0, 10251), records.Select(record => record.GetProperty("ordinal").GetInt32()));
        Assert.Equal(10251, records.Select(record => record.GetProperty("offset").GetInt64()).Distinct().Count());
        Assert.DoesNotContain(records, record => record.GetProperty("state").GetString() != "read");

        // Numbers repeat — the archive's directory reuses ten of them across fourteen records — so the
        // inventory carries as many records as the file states rather than one per number.
        Assert.Equal(10237, records.Select(record => record.GetProperty("recordId").GetInt64()).Distinct().Count());
    }

    [Fact]
    public void Every_number_a_block_names_is_answered_by_the_inventory()
    {
        // The cross-check the task asks for: the numbers the block document names, dungeon and town blocks
        // alike, are exactly the records the inventory marks referenced. A dungeon block spells a number as
        // five characters of text and a town block spells it plain, while this archive stores the number
        // itself, so the join is numeric: mesh 9004 is named "09004" by a dungeon block.
        using JsonDocument pack = Pack();
        JsonElement[] records = [.. pack.RootElement.GetProperty("geometry").GetProperty("records").EnumerateArray()];
        IReadOnlyList<DaggerfallGeometryUseSite> useSites = DaggerfallGeometryBuilder.UseSites(JsonSerializer.Deserialize<DaggerfallBlocks>(
            TestContentFiles.ReadCopy(Path.Combine(TestData.ImportRecords, "daggerfall.blocks.json")), PublishedJson.SectionRead)!);

        long[] named = [.. useSites.Select(site => long.Parse(site.MeshId, System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order()];
        long[] referenced = [.. records
            .Where(record => record.GetProperty("disposition").GetString() == "referenced")
            .Select(record => record.GetProperty("recordId").GetInt64())
            .Order()];

        Assert.Equal(2474, named.Length);
        Assert.Equal(named, referenced);
        Assert.Contains(useSites, site => site.MeshId == "09004");
    }

    [Fact]
    public void Publishes_the_corpus_facts_the_archive_holds()
    {
        using JsonDocument pack = Pack();
        JsonElement[] records = [.. pack.RootElement.GetProperty("geometry").GetProperty("records").EnumerateArray()];

        Assert.Equal(204479, records.Sum(record => record.GetProperty("facts").GetProperty("planes").GetInt32()));
        Assert.Equal(367262, records.Sum(record => record.GetProperty("facts").GetProperty("declaredPoints").GetInt32()));
        Assert.Equal(10109, records.Count(record => record.GetProperty("facts").GetProperty("version").GetString() == "v2.7"));
        Assert.Equal(134, records.Count(record => record.GetProperty("facts").GetProperty("version").GetString() == "v2.6"));
        Assert.Equal(8, records.Count(record => record.GetProperty("facts").GetProperty("version").GetString() == "v2.5"));

        // Two independent duplication questions: which numbers a lookup still reaches, and which records
        // are the same geometry stored twice.
        Assert.Equal(14, records.Count(record => record.GetProperty("duplicateOf").ValueKind != JsonValueKind.Null));
        Assert.Equal(1909, records.Count(record => record.GetProperty("payloadDuplicateOf").ValueKind != JsonValueKind.Null));
        Assert.Equal(2474, records.Count(record => record.GetProperty("disposition").GetString() == "referenced"));
        Assert.Equal(7763, records.Count(record => record.GetProperty("disposition").GetString() == "unused"));
        Assert.Equal(14, records.Count(record => record.GetProperty("disposition").GetString() == "duplicate"));
    }

    [Fact]
    public void A_later_record_reusing_a_number_carries_no_use_sites()
    {
        // The donor's lookup answers with the first record carrying a number, so a later record with the
        // same number is unreachable by it and the blocks naming that number belong to the first.
        using JsonDocument pack = Pack();
        JsonElement[] records = [.. pack.RootElement.GetProperty("geometry").GetProperty("records").EnumerateArray()];

        foreach (JsonElement record in records.Where(record => record.GetProperty("duplicateOf").ValueKind != JsonValueKind.Null))
        {
            Assert.Empty(record.GetProperty("useSites").EnumerateArray());
            Assert.Equal("duplicate", record.GetProperty("disposition").GetString());
            int first = record.GetProperty("duplicateOf").GetInt32();
            Assert.True(first < record.GetProperty("ordinal").GetInt32());
            Assert.Equal(record.GetProperty("recordId").GetInt64(), records[first].GetProperty("recordId").GetInt64());
        }
    }

    /// <summary>The mesh inventory from the import records.</summary>
    private static JsonDocument Pack()
    {
        using JsonDocument records = JsonDocument.Parse(TestContentFiles.ReadCopy(Path.Combine(TestData.ImportRecords, "daggerfall.import-records.json")));
        return JsonDocument.Parse($"{{\"geometry\":{records.RootElement.GetProperty("geometry").GetRawText()}}}");
    }
}
