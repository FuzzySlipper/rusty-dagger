using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published text a caller resolves values through: the corpus's own keys, languages and macro
/// usage, and what the reader refuses. The mutations rewrite the real payload rather than a fixture,
/// because what is under test is the reader of the section the product ships.
/// </summary>
public sealed class DaggerfallTextSetTests
{
    /// <summary>
    /// The parsed payload, kept so each mutation pays for one clone rather than one parse of eight
    /// megabytes of normalized corpus.
    /// </summary>
    private static readonly Lazy<JsonObject> PayloadTemplate = new(() => JsonNode.Parse(File.ReadAllBytes(PackPath()))!.AsObject());

    [Fact]
    public void Loads_the_published_text_and_resolves_a_key_by_its_own_identity()
    {
        DaggerfallTextSet text = Definitions().Text;

        Assert.Equal(1408, text.Values.Count);
        Assert.Equal(DaggerfallTextResolution.Resolved, text.Resolve(new DaggerfallTextKey(DaggerfallTextKind.Resource, "0"), out DaggerfallTextValue? value));
        Assert.Equal("local/arena2/TEXT.RSC", value!.Source);
        Assert.Equal("en", value.Language);
        Assert.Equal("STRENGTH", value.TextRuns.First());
        Assert.Equal(0, value.Index);
        Assert.Equal(8456L, value.Offset);
        Assert.Equal(284, value.ByteLength);
        Assert.Equal(1, value.Subrecords);
        Assert.Equal(DaggerfallTextState.Read, value.State);

        // The corpus shares twelve regions between keys, and the donor reads each entry's own bytes, so
        // two keys carry one text. A loader that keyed on the region would collapse them.
        Assert.Equal(DaggerfallTextResolution.Resolved, text.Resolve(new DaggerfallTextKey(DaggerfallTextKind.Resource, "1201"), out DaggerfallTextValue? shared));
        Assert.Equal("Paralysis", shared!.TextRuns.First());
        Assert.Equal("Paralysis", text.Require(new DaggerfallTextKey(DaggerfallTextKind.Resource, "1202")).TextRuns.First());
        Assert.Equal(shared.Offset, text.Require(new DaggerfallTextKey(DaggerfallTextKind.Resource, "1202")).Offset);
        Assert.Equal(5, text.Require(new DaggerfallTextKey(DaggerfallTextKind.Resource, "11")).Subrecords);
    }

    [Fact]
    public void A_key_the_pack_does_not_carry_is_a_miss_rather_than_empty_text()
    {
        DaggerfallTextSet text = Definitions().Text;

        // A key no source supplies, and a book key, whose family this contract declares and another
        // task fills: both are misses, and neither is an empty string a caller would render as silence.
        Assert.Equal(DaggerfallTextResolution.Missing, text.Resolve(new DaggerfallTextKey(DaggerfallTextKind.Resource, "99999"), out DaggerfallTextValue? missing));
        Assert.Null(missing);
        Assert.Equal(DaggerfallTextResolution.Missing, text.Resolve(new DaggerfallTextKey(DaggerfallTextKind.Book, "BOK00042"), out _));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => text.Require(new DaggerfallTextKey(DaggerfallTextKind.Book, "BOK00042")));
        Assert.Contains("does not contain 'Book:BOK00042'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_value_resolves_with_its_reason_rather_than_as_empty_text()
    {
        DaggerfallDefinitions definitions = Definitions(payload => Malformed(Records(payload)[0]!.AsObject()));

        DaggerfallTextKey key = new(DaggerfallTextKind.Resource, "0");
        Assert.Equal(DaggerfallTextResolution.Malformed, definitions.Text.Resolve(key, out DaggerfallTextValue? value));
        Assert.Equal("the fixture states the record could not be read", value!.Reason);
        Assert.Empty(value.Tokens);

        // A caller that needs the text fails with the source's own reason instead of rendering nothing.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => definitions.Text.Require(key));
        Assert.Contains("is published as malformed", error.Message, StringComparison.Ordinal);
        Assert.Contains("the fixture states the record could not be read", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_macro_index_separates_what_the_donor_handles_from_what_it_leaves()
    {
        DaggerfallTextSet text = Definitions().Text;

        Assert.Equal(168, text.Macros.Count);
        Assert.Equal(141, text.Macros.Count(macro => macro.Disposition == DaggerfallTextMacroDisposition.Handled));
        Assert.Equal(18, text.Macros.Count(macro => macro.Disposition == DaggerfallTextMacroDisposition.DonorUnresolved));
        Assert.Equal(9, text.Macros.Count(macro => macro.Disposition == DaggerfallTextMacroDisposition.Unrecognised));

        // The index says which values carry a symbol. How often the corpus spells one is a fact about a
        // value's text that only the reader's own macro grammar produces, so the pack does not state it
        // and nothing here reads it: the reader checks the index against the values instead.
        Assert.Equal(168, text.Macros.Select(macro => macro.Symbol).Distinct(StringComparer.Ordinal).Count());

        // The distinction is the donor's own table, so a symbol it names without a handler is not
        // reported as absent from it, and a symbol it never names is not reported as one it decided on.
        Assert.Equal(DaggerfallTextMacroDisposition.Handled, text.Macros.Single(macro => macro.Symbol == "%str").Disposition);
        Assert.Equal(DaggerfallTextMacroDisposition.DonorUnresolved, text.Macros.Single(macro => macro.Symbol == "%hol").Disposition);
        Assert.Equal(DaggerfallTextMacroDisposition.Unrecognised, text.Macros.Single(macro => macro.Symbol == "%pc").Disposition);

        // A value's own symbols are the ones a resolver expands in it, in the order it carries them.
        DaggerfallTextValue spell = text.Require(new DaggerfallTextKey(DaggerfallTextKind.Resource, "1202"));
        Assert.Equal(["%bdr", "%adr", "%cld", "%bch", "%ach", "%clc", "%1bm", "%2bm", "%1am", "%2am", "%clm"], spell.Macros);
    }

    [Fact]
    public void The_declared_families_name_the_tasks_that_supply_them()
    {
        IReadOnlyList<DaggerfallTextPendingKind> pending = Definitions().Text.PendingKinds;

        Assert.Equal(["biography", "book", "name", "rumor"], pending.Select(kind => kind.Kind.ToString().ToLowerInvariant()).Order());
        Assert.Equal(7951, pending.Single(kind => kind.Kind == DaggerfallTextKind.Book).OwnerTask);
        Assert.All(pending.Where(kind => kind.Kind != DaggerfallTextKind.Book), kind => Assert.Equal(7941, kind.OwnerTask));
        Assert.All(pending, kind => Assert.NotEmpty(kind.Reason));
    }

    [Fact]
    public void Rewriting_the_payload_without_a_change_still_loads()
    {
        // The control for every mutation below: the harness rewrites the payload through JSON, so this
        // states that the rewrite alone is not what a mutation test is observing.
        Assert.Equal(1408, Definitions(_ => { }).Text.Values.Count);
    }

    [Fact]
    public void Rejects_a_variant_count_that_disagrees_with_the_separators_it_publishes()
    {
        // The separators are what divide the value into the variants a random answer selects between, so
        // a count that disagrees with them describes a value no consumer can divide.
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload).Single(record => record!.AsObject()["key"]!.AsObject()["id"]!.GetValue<string>() == "11").AsObject()["subrecords"] = 99)));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("variants where its separators divide it into 5", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_macro_disposition_the_contract_does_not_declare()
    {
        // A consumer decides what to do with a symbol by its disposition, so a spelling the contract does
        // not declare is refused rather than read as a disposition nobody published.
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Macros(payload)[0]!.AsObject()["disposition"] = "probably")));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("states the disposition 'probably', which the contract does not declare", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_malformed_value_whose_reason_is_only_whitespace()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload =>
            {
                JsonObject record = Records(payload)[0]!.AsObject();
                Malformed(record);
                record["reason"] = " ";
            })));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("or states no reason", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_readable_value_that_states_a_reason()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload)[0]!.AsObject()["reason"] = "because the fixture says so")));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("or states a reason it is not readable", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_payload_on_a_code_that_takes_none()
    {
        // A payload belongs to the prefix that states it, so a code that takes none and carries one would
        // be applied at a position the source never gave it.
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Tokens(payload)[1]!.AsObject()["x"] = 4)));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("or with a payload it does not take", StringComparison.Ordinal));
    }

    [Fact]
    public void Refuses_a_name_that_is_a_number_and_accepts_one_spelled_in_another_case()
    {
        // The published dialect writes a name from a closed set, so a numeric spelling - which parses to a
        // member but is not one the producer writes - is refused, while the case of a real name carries no
        // meaning and is read.
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload)[0]!.AsObject()["state"] = "1")));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("states the state '1', which the contract does not declare", StringComparison.Ordinal));
        Assert.Equal(DaggerfallTextState.Read, Definitions(payload => Records(payload)[0]!.AsObject()["state"] = "Read").Text.Require(new DaggerfallTextKey(DaggerfallTextKind.Resource, "0")).State);
    }

    [Fact]
    public void Rejects_a_macro_index_entry_that_no_value_carries_or_that_appears_twice()
    {
        // An index entry nothing carries would report a symbol the corpus lacks, and one indexed twice
        // would leave a consumer reading one of the two rows and never the other.
        DaggerfallContentException phantom = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Macros(payload).Add(JsonNode.Parse("""{"symbol":"%zzz","records":1,"disposition":"unrecognised"}""")))));

        Assert.Contains(phantom.Diagnostics, diagnostic => diagnostic.Contains("is indexed against 1 values where 0 carry it", StringComparison.Ordinal));

        DaggerfallContentException zero = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Macros(payload).Add(JsonNode.Parse("""{"symbol":"%zzz","records":0,"disposition":"unrecognised"}""")))));

        Assert.Contains(zero.Diagnostics, diagnostic => diagnostic.Contains("is indexed against 0 values", StringComparison.Ordinal));

        DaggerfallContentException twice = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Macros(payload).Add(Macros(payload)[0]!.DeepClone()))));

        Assert.Contains(twice.Diagnostics, diagnostic => diagnostic.Contains("is indexed twice", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_value_that_begins_at_a_negative_source_byte()
    {
        // The source's offsets are unsigned, so a published value at a negative byte describes a position
        // nothing could have read.
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload)[0]!.AsObject()["offset"] = -5)));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("begins at the negative source byte -5", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_payload_that_publishes_no_text_section()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => payload.Remove("text"))));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("publishes no text section", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_section_whose_schema_version_it_does_not_know()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Text(payload)["schemaVersion"] = 2)));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("must declare schemaVersion 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_section_that_publishes_one_key_twice()
    {
        // Two values under one key leave one of them unreachable through every lookup the set offers.
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload)[1]!.AsObject()["key"] = JsonNode.Parse("""{"kind":"resource","id":"0"}"""))));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("is unreachable", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_value_whose_source_the_section_does_not_carry()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload)[0]!.AsObject()["source"] = "elsewhere/TEXT.RSC")));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("which the section does not carry", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_value_whose_family_is_not_the_family_of_the_source_it_names()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload)[0]!.AsObject()["key"] = JsonNode.Parse("""{"kind":"book","id":"0"}"""))));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("by its source and to 'Book' by its key", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_malformed_value_that_still_carries_text()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload)[0]!.AsObject()["state"] = "malformed")));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("or states no reason", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_malformed_value_that_states_no_reason()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload =>
            {
                JsonObject record = Records(payload)[0]!.AsObject();
                Malformed(record);
                record["reason"] = "";
            })));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("or states no reason", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_readable_value_that_spans_no_bytes()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload)[0]!.AsObject()["byteLength"] = 0)));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("is readable but spans 0 bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_state_the_contract_does_not_declare()
    {
        // A state a consumer does not know is not a readable value: reading it as one would hide the
        // defect behind text the pack never said it carried.
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload)[0]!.AsObject()["state"] = "repaired")));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("states the state 'repaired', which the contract does not declare", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_run_that_states_a_byte()
    {
        // A named code carries its byte in its name and a run carries none at all, so a run stating one
        // would be applied as a code it does not claim to be.
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Tokens(payload)[0]!.AsObject()["value"] = -1)));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("holds no text or states a value a run does not have", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_value_that_is_not_in_its_sources_order()
    {
        // A source's ordinals are what a caller reads its values by, so two values claiming one ordinal
        // would leave one of them unaddressable inside the group.
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload)[1]!.AsObject()["index"] = 0)));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("after ordinal 0, so its values are not in source order", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_values_of_one_source_interleaved_with_another()
    {
        // Each source's values are published as a group, so a caller reads one source's ordinals without
        // interleaving another's. The second source here is the same bytes under another documented path,
        // which is what a pack with two text sources would look like.
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload =>
            {
                JsonObject second = Text(payload)["sources"]!.AsArray()[0]!.AsObject().DeepClone().AsObject();
                second["path"] = "local/arena2/OTHER.RSC";
                second["records"] = 1;
                Text(payload)["sources"]!.AsArray().Add(second);
                Records(payload)[1]!.AsObject()["source"] = "local/arena2/OTHER.RSC";
            })));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("is interleaved with another source rather than grouped", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_source_whose_declared_record_count_is_not_what_it_publishes()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Text(payload)["sources"]!.AsArray()[0]!.AsObject()["records"] = 1407)));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("declares 1407 records and publishes 1408", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_macro_a_value_carries_that_the_index_does_not_account_for()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload)[0]!.AsObject()["macros"] = new JsonArray("%str", "%nothing-accounts-for-this"))));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("which the published macro index does not account for", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_macro_the_index_claims_against_the_wrong_number_of_values()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload =>
            {
                JsonObject indexed = Macros(payload).Single(macro => macro!.AsObject()["symbol"]!.GetValue<string>() == "%str")!.AsObject();
                indexed["records"] = 3;
            })));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("is indexed against 3 values", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_family_that_is_both_pending_and_carried()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Text(payload)["pendingKinds"]!.AsArray()[0]!.AsObject()["kind"] = "resource")));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("is published as pending and carried by a source", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_family_the_contract_does_not_declare()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Records(payload)[0]!.AsObject()["key"] = JsonNode.Parse("""{"kind":"scroll","id":"0"}"""))));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("the source family 'scroll', which the contract does not declare", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_an_element_kind_the_contract_does_not_declare()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Tokens(payload)[0]!.AsObject()["code"] = "smallCaps")));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("carries the element kind 'smallCaps'", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_named_code_that_states_a_byte()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Tokens(payload)[1]!.AsObject()["value"] = 253)));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("states byte 253 for the named code", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_run_that_states_no_text()
    {
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Tokens(payload)[0]!.AsObject().Remove("text"))));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("holds no text or states a value a run does not have", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_an_unnamed_code_with_no_byte()
    {
        // A code the contract cannot name is published with the byte it came from, because that byte is
        // all a consumer has to go on: one without it describes an element of unknown origin.
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Tokens(payload)[1]!.AsObject()["code"] = "unknown")));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("carries an unnamed code with no byte", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_prefix_that_states_no_payload()
    {
        // A position prefix always consumes the byte after it, so a published one without a payload
        // describes a token the source reader cannot produce.
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(
            Payload(payload => Tokens(payload)[1]!.AsObject()["code"] = "positionPrefix")));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Contains("or with a payload it does not take", StringComparison.Ordinal));
    }

    private static DaggerfallDefinitions Definitions(Action<JsonObject>? mutate = null) =>
        DaggerfallBaseContent.Read(mutate is null ? File.ReadAllBytes(PackPath()) : Payload(mutate));

    private static byte[] Payload(Action<JsonObject> mutate)
    {
        JsonObject payload = PayloadTemplate.Value.DeepClone().AsObject();
        mutate(payload);
        return Encoding.UTF8.GetBytes(payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static JsonObject Text(JsonObject payload) => payload["text"]!.AsObject();

    private static JsonArray Records(JsonObject payload) => Text(payload)["records"]!.AsArray();

    private static JsonArray Macros(JsonObject payload) => Text(payload)["macros"]!.AsArray();

    private static JsonArray Tokens(JsonObject payload) => Records(payload)[0]!.AsObject()["tokens"]!.AsArray();

    /// <summary>Makes one published value state that its bytes could not be read.</summary>
    private static void Malformed(JsonObject record)
    {
        record["state"] = "malformed";
        record["reason"] = "the fixture states the record could not be read";
        record["byteLength"] = 0;
        record["subrecords"] = 0;
        record["tokens"] = new JsonArray();
    }

    private static string PackPath() => Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json");

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        }

        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
