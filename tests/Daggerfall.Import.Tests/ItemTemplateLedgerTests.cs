using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The item template baseline and the ledger built from it: the donor's own group
/// enumerations are the only thing that can be read while FALL.EXE is absent, so the
/// baseline's claims are limited to the index space and its group attribution.
/// </summary>
public sealed class ItemTemplateLedgerTests
{
    [Fact]
    public void Reads_the_index_space_and_group_attribution_from_donor_sources()
    {
        ItemTemplateBaseline baseline = ReadFixture();

        Assert.Equal(288, baseline.Targets.Count);
        Assert.Equal(Enumerable.Range(0, 288), baseline.Targets.Select(target => target.Index));
        Assert.Equal(["Weapons"], baseline.Targets[0].DonorGroups);
        Assert.Equal(["Weapons"], baseline.Targets[2].DonorGroups);
        // A group may name one index through several members: the four book variants all
        // name index 5, and the group is recorded once.
        Assert.Equal(["Books"], baseline.Targets[5].DonorGroups);
        Assert.Empty(baseline.OutOfRangeIndices);
    }

    [Fact]
    public void Retains_a_target_no_donor_group_names()
    {
        ItemTemplateBaseline baseline = ReadFixture();

        // Indices 1 and 3 are named by no enumeration in the fixture, which is a fact about
        // the donor's own coverage rather than a reason to drop the targets. The remaining
        // The remaining 285 indices are unreferenced for the same reason: the fixture names
        // only three of the 288.
        Assert.Contains(1, baseline.Unreferenced.Select(target => target.Index));
        Assert.Contains(3, baseline.Unreferenced.Select(target => target.Index));
        Assert.False(baseline.Targets[3].IsReferenced);
        Assert.Equal(285, baseline.Unreferenced.Count());
        Assert.True(baseline.Targets[0].IsReferenced);
    }

    [Fact]
    public void Excludes_the_enumerations_none_sentinel_from_the_index_space()
    {
        ItemTemplateBaseline baseline = ReadFixture();

        // The fixture declares 'None = -1' in a mapped enumeration. It names no template,
        // so it must not appear as a target or as drift.
        Assert.DoesNotContain(baseline.Targets, target => target.Index < 0);
        Assert.Empty(baseline.OutOfRangeIndices);
    }

    [Fact]
    public void Reports_a_donor_index_outside_the_classic_space()
    {
        ItemTemplateBaseline baseline = ItemTemplateBaseline.FromDonorSources(
            """
            public enum Weapons
            {
                Dagger = 0,
                Beyond = 300,
            }
            """,
            Mapping("Weapons"),
            "fixture");

        // Index 300 is donor drift: the target space is 288 entries, so it is reported
        // rather than folded into the space or dropped.
        Assert.Equal([300], baseline.OutOfRangeIndices);
        Assert.Equal(288, baseline.Targets.Count);
    }

    [Fact]
    public void Refuses_a_group_mapped_to_an_undeclared_enumeration()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 0 }",
            Mapping("Armor"),
            "fixture"));

        Assert.Contains("Armor", error.Message, StringComparison.Ordinal);
        Assert.Contains("do not declare", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_source_without_the_group_mapping()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 0 }",
            "public static class ItemHelper { }",
            "fixture"));

        Assert.Contains("GetEnumArray", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_an_enumeration_body_that_is_not_closed()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 0, ",
            Mapping("Weapons"),
            "fixture"));

        Assert.Contains("not closed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_published_ledger_covers_every_target_with_provenance_and_disposition()
    {
        System.Text.Json.Nodes.JsonObject ledger = ReadPublishedLedger();

        System.Text.Json.Nodes.JsonArray targets = ledger["targets"]!.AsArray();
        Assert.Equal(288, targets.Count);
        Assert.Equal(Enumerable.Range(0, 288), targets.Select(target => target!["index"]!.GetValue<int>()));
        Assert.All(targets, target =>
        {
            Assert.False(string.IsNullOrWhiteSpace(target!["provenance"]!.GetValue<string>()));
            Assert.Equal("unresolved", target["disposition"]!.GetValue<string>());
        });

        // The summary is checked against the entries rather than trusted: a ledger whose
        // summary and entries disagree would misdescribe its own coverage.
        System.Text.Json.Nodes.JsonObject summary = ledger["summary"]!.AsObject();
        Assert.Equal(288, summary["targets"]!.GetValue<int>());
        Assert.Equal(targets.Count(target => target!["donorGroups"]!.AsArray().Count != 0), summary["referencedByDonorGroups"]!.GetValue<int>());
        Assert.Equal(targets.Count(target => target!["donorGroups"]!.AsArray().Count == 0), summary["unreferencedByAnyGroup"]!.GetValue<int>());
        Assert.Equal(0, summary["nativeTemplatesDecoded"]!.GetValue<int>());
    }

    [Fact]
    public void The_published_ledger_records_the_absent_source_and_the_published_items()
    {
        System.Text.Json.Nodes.JsonObject ledger = ReadPublishedLedger();
        System.Text.Json.Nodes.JsonObject pack = System.Text.Json.Nodes.JsonNode
            .Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")))!
            .AsObject();

        Assert.Equal("CNT-011", ledger["target"]!["recordId"]!.GetValue<string>());
        Assert.Equal("absent", ledger["target"]!["status"]!.GetValue<string>());
        Assert.Equal("DEC-11", ledger["baseline"]!["rule"]!.GetValue<string>());

        // The published items' values were migrated, not decoded, and the ledger says so
        // beside a count that matches the payload it describes.
        System.Text.Json.Nodes.JsonObject published = ledger["publishedItems"]!.AsObject();
        Assert.Equal(pack["items"]!.AsArray().Count, published["count"]!.GetValue<int>());
        Assert.Equal("catalog-migration", published["valueProvenance"]!.GetValue<string>());
        Assert.False(published["nativeDecoding"]!.GetValue<bool>());
    }

    private static System.Text.Json.Nodes.JsonObject ReadPublishedLedger() =>
        System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")))!
            .AsObject()["itemTemplateLedger"]!.AsObject();

    /// <summary>
    /// A donor pair small enough to reason about: one group naming four indices, one
    /// aliased index, one index no group names, and a "none" sentinel.
    /// </summary>
    private static ItemTemplateBaseline ReadFixture() => ItemTemplateBaseline.FromDonorSources(
        """
        namespace Fixture
        {
            public enum ItemGroups
            {
                None = -1,
                Weapons = 0,
                Books = 1,
            }

            public enum Weapons
            {
                Dagger = 0,
                Longsword = 2,
            }

            public enum Books
            {
                Book0 = 5,
                Book1 = 5,
                Book2 = 5,
                Book3 = 5,
            }
        }
        """,
        """
        public static class ItemHelper
        {
            public Array GetEnumArray(ItemGroups group)
            {
                switch (group)
                {
                    case ItemGroups.Weapons:
                        return Enum.GetValues(typeof(Weapons));
                    case ItemGroups.Books:
                        return Enum.GetValues(typeof(Books));
                    default:
                        return null;
                }
            }
        }
        """,
        "fixture");

    private static string Mapping(string enumName) => $$"""
        public static class ItemHelper
        {
            public Array GetEnumArray(ItemGroups group)
            {
                switch (group)
                {
                    case ItemGroups.Weapons:
                        return Enum.GetValues(typeof({{enumName}}));
                    default:
                        return null;
                }
            }
        }
        """;

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
