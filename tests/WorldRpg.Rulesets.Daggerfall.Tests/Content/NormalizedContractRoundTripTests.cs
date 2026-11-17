using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Normalized;
using Normalized = Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The normalized contract has one writer, the importer's records, and one reader, this ruleset's content
/// readers. They are separate code in separate assemblies - the runtime ruleset may not reference the
/// offline importer - so this suite is what joins them: every imported section is read back through the
/// importer's own record type and rewritten by the importer's writer, and the ruleset reads what was
/// rewritten. Renaming a normalized field on the writing side fails the strict record read of the
/// published section (or, once regenerated, the ruleset's read of it); renaming it on the reading side
/// fails the ruleset's read. The generated content supplies the values; the bytes the reader sees are
/// written here, by the importer, at test time.
/// </summary>
public sealed class NormalizedContractRoundTripTests
{
    /// <summary>Every imported payload section a record type writes, by section name.</summary>
    private static readonly (string Section, Type Record)[] RecordSections =
    [
        ("characterPresentation", typeof(DaggerfallCharacterPresentation)),
        ("locations", typeof(DaggerfallLocations)),
        ("text", typeof(DaggerfallText)),
        ("names", typeof(DaggerfallNameTables)),
        ("rumors", typeof(DaggerfallRumorCatalog)),
        ("biographies", typeof(DaggerfallBiographies)),
        ("books", typeof(DaggerfallBooks)),
        ("climate", typeof(DaggerfallClimateGrid)),
        ("politic", typeof(DaggerfallPoliticGrid)),
        ("terrain", typeof(DaggerfallTerrain)),
        ("cinematics", typeof(DaggerfallCinematicPack)),
    ];

    /// <summary>
    /// The sections an importer document builder writes without a record type. Their names are fixed in
    /// that builder, so the ruleset's read of the published section is the check.
    /// </summary>
    private static readonly string[] BuilderSections = ["paperDoll"];

    [Fact]
    public void Every_imported_section_is_written_by_an_importer_contract_this_suite_names()
    {
        JsonObject imported = ReadImported();
        Assert.Equal(
            imported.Select(section => section.Key).Order(StringComparer.Ordinal),
            RecordSections.Select(section => section.Section).Concat(BuilderSections).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Sections_rewritten_by_the_importer_s_records_read_back_through_the_ruleset()
    {
        JsonObject imported = ReadImported();
        Dictionary<string, string> rewrittenSections = new(StringComparer.Ordinal);
        foreach ((string section, Type record) in RecordSections)
        {
            JsonNode published = imported[section]!;
            object value;
            try
            {
                value = published.Deserialize(record, PublishedJson.SectionRead)
                    ?? throw new InvalidOperationException($"Section '{section}' read as null.");
            }
            catch (JsonException exception)
            {
                Assert.Fail($"The published '{section}' section no longer matches the importer's {record.Name}: {exception.Message}");
                throw;
            }

            JsonNode rewritten = JsonNode.Parse(JsonSerializer.Serialize(value, record, PublishedJson.Section))!;
            // The writer states every value the published section states: nothing the reader relies on
            // exists only in the generated file.
            Assert.True(JsonNode.DeepEquals(published, rewritten), $"The importer's {record.Name} does not rewrite the published '{section}' section unchanged.");
            rewrittenSections[section] = rewritten.ToJsonString(PublishedJson.SectionCompact);
        }

        byte[] authored = TestContentFiles.ReadCopy(TestData.PackPayload("daggerfall.base"));
        byte[] rewrittenPayload = RewriteImportedPayload(rewrittenSections);
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(authored, rewrittenPayload, TestPayload.Data);
        Assert.NotEmpty(definitions.CharacterPresentation.Races);
    }

    [Fact]
    public void The_site_reader_admits_exactly_the_media_kinds_the_importer_publishes()
    {
        Assert.Equal(
            Enum.GetValues<global::Daggerfall.Import.Normalization.NormalizedMediaKind>().Select(kind => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString())).Order(StringComparer.Ordinal),
            DaggerfallSiteContent.ClassicMediaKinds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void World_media_documents_read_back_through_the_importer_s_records()
    {
        string publication = Path.Combine(TestData.RepositoryRoot, "content", "worldrpg", "imports", "shared");
        RoundTrip<DungeonMediaManifestSidecar>(Path.Combine(publication, Arena2MediaBundlePublication.DungeonMediaManifestRelativePath));
        RoundTrip<ClassicMediaManifestSidecar>(Path.Combine(publication, Arena2MediaBundlePublication.ClassicMediaManifestRelativePath));
        RoundTrip<CanonicalImportManifest>(Path.Combine(publication, ImportPublicationManifestSerializer.ManifestRelativePath));
    }

    [Fact]
    public void The_block_building_fields_read_back_through_the_importer_s_record_and_the_ruleset()
    {
        string payload = PayloadPath("daggerfall.blocks.json");
        RoundTrip<DaggerfallBlockBuildingSet>(payload);
        DaggerfallBlocksSnapshot blocks = DaggerfallBlocksContent.Read(TestContentFiles.ReadCopy(payload));
        Assert.Equal(
            JsonNode.Parse(TestContentFiles.ReadCopy(payload))!["buildings"]!.AsArray().Count,
            blocks.RmbBuildings.Count);
    }

    /// <summary>
    /// Reads a published document strictly through the importer's record and checks the importer's writer
    /// states every value it holds: a field renamed on either side fails the read or the comparison.
    /// </summary>
    private static void RoundTrip<T>(string path)
    {
        JsonNode published = JsonNode.Parse(TestContentFiles.ReadCopy(path))!;
        T value = published.Deserialize<T>(PublishedJson.SectionRead)
            ?? throw new InvalidOperationException($"'{path}' read as null.");
        Assert.True(
            JsonNode.DeepEquals(published, JsonNode.Parse(JsonSerializer.Serialize(value, PublishedJson.Section))),
            $"The importer's {typeof(T).Name} does not rewrite '{path}' unchanged.");
    }

    private static JsonObject ReadImported() => JsonNode.Parse(TestContentFiles.ReadCopy(PayloadPath("daggerfall.imported.json")))!.AsObject();

    private static byte[] RewriteImportedPayload(IReadOnlyDictionary<string, string> replacements) =>
        Encoding.UTF8.GetBytes(TopLevelJsonSectionRewriter.ReplaceOrAppend(
            TestContentFiles.ReadText(PayloadPath("daggerfall.imported.json")), replacements));

    private static string PayloadPath(string file) => Path.Combine(TestData.RepositoryRoot, "content", "worldrpg", "payloads", file);
}
