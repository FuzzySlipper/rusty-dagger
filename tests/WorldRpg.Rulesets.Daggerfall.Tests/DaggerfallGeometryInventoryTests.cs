using System.Text.Json;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published mesh inventory as the product ships it: every numeric record the archive declares, what
/// its own bytes say, and whether the numbers the block section names are all answered.
/// </summary>
/// <remarks>
/// The section has no runtime reader yet — this is the inventory the tasks that publish geometry consume —
/// so what is checked here is the artifact itself, against the block section it joins to.
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
        // The cross-check the task asks for: the block section names mesh numbers the way the dungeon
        // source stores them, as five characters of text, while this archive stores the number itself.
        // Joining the two by spelling would report a number missing from the archive that carries it.
        using JsonDocument pack = Pack();
        long[] carried = [.. pack.RootElement.GetProperty("geometry").GetProperty("records").EnumerateArray()
            .Select(record => record.GetProperty("recordId").GetInt64())];

        long[] named = [.. pack.RootElement.GetProperty("blocks").GetProperty("records").EnumerateArray()
            .Where(block => block.TryGetProperty("objects", out JsonElement objects) && objects.ValueKind == JsonValueKind.Object)
            .SelectMany(block => block.GetProperty("objects").GetProperty("modelIds").EnumerateArray())
            .Select(model => long.Parse(model.GetString()!, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()];

        Assert.Equal(1043, named.Length);
        Assert.Empty(named.Except(carried));

        // At least one of them is spelled with the leading zero that makes the join a numeric question:
        // mesh 9004 is named "09004" by the blocks and carried as 9004 by the archive.
        string[] spellings = [.. pack.RootElement.GetProperty("blocks").GetProperty("records").EnumerateArray()
            .Where(block => block.TryGetProperty("objects", out JsonElement objects) && objects.ValueKind == JsonValueKind.Object)
            .SelectMany(block => block.GetProperty("objects").GetProperty("modelIds").EnumerateArray())
            .Select(model => model.GetString()!)
            .Where(model => model.StartsWith('0'))
            .Distinct()];

        Assert.Contains("09004", spellings, StringComparer.Ordinal);
        Assert.Contains(9004L, carried);
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
        Assert.Equal(1043, records.Count(record => record.GetProperty("disposition").GetString() == "referenced"));
        Assert.Equal(9194, records.Count(record => record.GetProperty("disposition").GetString() == "unused"));
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
