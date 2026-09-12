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
        Assert.Equal(["Weapons", "Deeds"], baseline.Targets[0].DonorGroups);
        Assert.Equal(["Weapons"], baseline.Targets[2].DonorGroups);
        // A group may name one index through several members: the four book variants all
        // name index 5, and the group is recorded once.
        Assert.Equal(["Books"], baseline.Targets[5].DonorGroups);
        // A member written without a value takes the previous value plus one, as the donor's
        // runtime enumerations do: Deed0 and Deed1 are indices 0 and 1.
        Assert.Contains("Deeds", baseline.Targets[1].DonorGroups);
        Assert.Empty(baseline.OutOfRangeIndices);
        Assert.All(baseline.Rules, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Evidence)));
    }

    [Fact]
    public void Retains_a_target_no_donor_group_names()
    {
        ItemTemplateBaseline baseline = ReadFixture();

        // Index 3 is named by no enumeration in the fixture, which is a fact about
        // the donor's own coverage rather than a reason to drop the targets. The remaining
        // The remaining 284 indices are unreferenced for the same reason: the fixture names
        // only four of the 288.
        Assert.Contains(3, baseline.Unreferenced.Select(target => target.Index));
        Assert.False(baseline.Targets[3].IsReferenced);
        Assert.Equal(284, baseline.Unreferenced.Count());
        Assert.True(baseline.Targets[0].IsReferenced);
    }

    [Fact]
    public void Separates_a_reference_id_space_from_the_template_index_space()
    {
        ItemTemplateBaseline baseline = ReadFixture();

        // The donor marks ArtifactsSubTypes as mapped to MAGIC.DEF definitions rather than to
        // template indices, so its values name reference ids that share the numeric range.
        // They are published apart from the template groups instead of being counted as
        // template index references.
        Assert.Equal(["Artifacts"], baseline.Targets[0].DonorReferenceGroups);
        Assert.DoesNotContain("Artifacts", baseline.Targets[0].DonorGroups);
        Assert.Equal(["Artifacts"], baseline.Targets[1].DonorReferenceGroups);
    }

    [Fact]
    public void Refuses_a_group_mapping_whose_signature_has_no_body()
    {
        // A truncated helper source must be a typed refusal rather than an index error.
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 0 }",
            "public static class ItemHelper\n{\n public Array GetEnumArray(ItemGroups group);\n}\n",
            ItemsFileSource,
            "fixture"));

        Assert.Contains("declares no body", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_case_label_that_never_returns_an_enumeration()
    {
        // Two labels before one return would silently attribute the index to the wrong
        // group and drop the other, so it is refused.
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 7 }",
            """
            public static class ItemHelper
            {
                public Array GetEnumArray(ItemGroups group)
                {
                    switch (group)
                    {
                        case ItemGroups.Weapons:
                        case ItemGroups.Armor:
                            return Enum.GetValues(typeof(Weapons));
                        default:
                            return null;
                    }
                }
            }
            """,
            ItemsFileSource,
            "fixture"));

        Assert.Contains("labelled twice", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_a_case_label_written_on_the_same_line_as_its_return()
    {
        ItemTemplateBaseline baseline = ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 7 /* the dagger */, Longsword = 8 }",
            """
            public static class ItemHelper
            {
                public Array GetEnumArray(ItemGroups group)
                {
                    switch (group)
                    {
                        case ItemGroups.Weapons: return Enum.GetValues(typeof(Weapons)); // weapons
                        default:
                            return null;
                    }
                }
            }
            """,
            ItemsFileSource,
            "fixture");

        // The group name is the label, not the rest of the line, and the hex-free members
        // are read from a body that also carries a block comment.
        Assert.Equal(["Weapons"], baseline.Targets[7].DonorGroups);
        Assert.Equal(["Weapons"], baseline.Targets[8].DonorGroups);
    }

    [Fact]
    public void Reads_members_written_on_one_line_and_hexadecimal_values()
    {
        ItemTemplateBaseline baseline = ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 7, Longsword = 0x08, Tanto = 9 }",
            Mapping("Weapons"),
            ItemsFileSource,
            "fixture");

        Assert.Equal(["Weapons"], baseline.Targets[7].DonorGroups);
        Assert.Equal(["Weapons"], baseline.Targets[8].DonorGroups);
        Assert.Equal(["Weapons"], baseline.Targets[9].DonorGroups);
    }

    [Fact]
    public void Refuses_a_member_value_that_is_not_a_literal()
    {
        // An expression is not a literal: reading it as one would invent a value, and
        // skipping it would silently understate the published coverage.
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 1 + 2 }",
            Mapping("Weapons"),
            ItemsFileSource,
            "fixture"));

        Assert.Contains("not an integer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_an_implicit_member_that_would_wrap()
    {
        // A value past the last representable one would wrap negative and then be dropped
        // as a sentinel: a silent loss the refusal names instead.
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 2147483647, Longsword }",
            Mapping("Weapons"),
            ItemsFileSource,
            "fixture"));

        Assert.Contains("wrap", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_duplicate_enumeration_declaration()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 1 }\npublic enum Weapons { Dagger = 2 }",
            Mapping("Weapons"),
            ItemsFileSource,
            "fixture"));

        Assert.Contains("declared more than once", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_mapped_enumeration_with_no_member()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { }",
            Mapping("Weapons"),
            ItemsFileSource,
            "fixture"));

        Assert.Contains("declares no member", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_the_logical_source_when_a_declaration_body_is_not_closed()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 1",
            Mapping("Weapons"),
            ItemsFileSource,
            "fixture"));

        Assert.Equal("fixture", error.SourceName);
    }

    [Fact]
    public void Refuses_a_donor_declaring_a_different_template_count()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 0 }",
            Mapping("Weapons"),
            "public class ItemsFile { const int totalItems = 200; }",
            "fixture"));

        Assert.Contains("200", error.Message, StringComparison.Ordinal);
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
            ItemsFileSource,
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
            ItemsFileSource,
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
            ItemsFileSource,
            "fixture"));

        Assert.Contains("GetEnumArray", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_an_enumeration_body_that_is_not_closed()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => ItemTemplateBaseline.FromDonorSources(
            "public enum Weapons { Dagger = 0, ",
            Mapping("Weapons"),
            ItemsFileSource,
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

            public enum ArtifactsSubTypes                   // Mapped to artifact definitions in MAGIC.DEF
            {
                None = -1,
                Masque = 0,
                Razor = 1,
            }

            public enum Deeds
            {
                Deed0,
                Deed1,
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
                    case ItemGroups.Artifacts:
                        return Enum.GetValues(typeof(ArtifactsSubTypes));
                    case ItemGroups.Deeds:
                        return Enum.GetValues(typeof(Deeds));
                    default:
                        return null;
                }
            }
        }
        """,
        """
        public class ItemsFile
        {
            const int totalItems = 288;
        }
        """,
        "fixture");

    private const string ItemsFileSource = "public class ItemsFile { const int totalItems = 288; }";

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
