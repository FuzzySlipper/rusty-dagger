using Daggerfall.Import.Publication;
using System.Text.Json;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class TopLevelJsonSectionRewriterTests
{
    [Fact]
    public void Replaces_only_named_root_values_and_preserves_other_source_bytes()
    {
        const string source = "{\n  \"keep\": {\n    \"spaces\": [ 1,  2 ]\n  },\n  \"replace\": { \"old\": true },\n  \"alsoKeep\": \"\\u003cunchanged\\u003e\"\n}\n";

        string result = TopLevelJsonSectionRewriter.ReplaceOrAppend(source, new Dictionary<string, string>
        {
            ["replace"] = "{\n  \"new\": true\n}",
        });

        Assert.Contains("\"keep\": {\n    \"spaces\": [ 1,  2 ]\n  }", result, StringComparison.Ordinal);
        Assert.Contains("\"alsoKeep\": \"\\u003cunchanged\\u003e\"", result, StringComparison.Ordinal);
        // The written value is compact; the members it does not name keep their bytes.
        Assert.Contains("\"replace\": {\"new\":true}", result, StringComparison.Ordinal);
        using JsonDocument _ = JsonDocument.Parse(result);
    }

    [Fact]
    public void A_compact_value_keeps_the_bytes_inside_its_strings()
    {
        Assert.Equal("{\"a b\":[1,2],\"q\":\"say \\\" \\\\ \\u003c x\",\"n\":-1.5e3}",
            TopLevelJsonSectionRewriter.Compact("{\n  \"a b\": [ 1,\t2 ],\r\n  \"q\": \"say \\\" \\\\ \\u003c x\",\n  \"n\": -1.5e3\n}"));
    }

    [Fact]
    public void Appends_an_absent_section_without_reserializing_existing_members()
    {
        const string source = "{\n  \"keep\": [ 1,  2 ]\n}\n";

        string result = TopLevelJsonSectionRewriter.ReplaceOrAppend(source, new Dictionary<string, string>
        {
            ["added"] = "{\n  \"value\": 3\n}",
        });

        Assert.Contains("\"keep\": [ 1,  2 ]", result, StringComparison.Ordinal);
        Assert.Contains("\"added\": {\"value\":3}", result, StringComparison.Ordinal);
        using JsonDocument _ = JsonDocument.Parse(result);
    }
}
