using System.Text.Json.Nodes;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Source.Tool.Donor;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class InternalStringsReaderTests
{
    [Fact]
    public void Preserves_multiline_values_escaped_quotes_and_source_spans()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("\ufeffKey,Value\r\nStoresA,The %ef\r\nTavernsA,\"The \"\"Golden\"\" Cup\"\r\nTavernsB,\"at %cn\r\n%rt's Rest\"\r\n");

        InternalStringsCatalog catalog = InternalStringsReader.Read(bytes, "donor/internal.csv");

        Assert.Equal(14, catalog.HeaderLength);
        Assert.Equal(["StoresA", "TavernsA", "TavernsB"], catalog.Records.Select(record => record.Key));
        Assert.Equal("The %ef", catalog.Records[0].Value);
        Assert.Equal("The \"Golden\" Cup", catalog.Records[1].Value);
        Assert.Equal("at %cn\r\n%rt's Rest", catalog.Records[2].Value);
        Assert.Equal(0, catalog.Records[0].Index);
        Assert.Equal(catalog.HeaderLength, catalog.Records[0].Offset);
        Assert.Equal(bytes.Length, catalog.Records.Sum(record => record.ByteLength) + catalog.HeaderLength);
    }

    [Theory]
    [InlineData("Key,Value\nName,One\nName,Two\n", "twice")]
    [InlineData("Key,Value\nName,One,Two\n", "more than two")]
    [InlineData("Key,Value\nName,\"One\n", "inside a quoted")]
    [InlineData("Name,Value\nName,One\n", "exact CSV header")]
    public void Refuses_a_table_that_cannot_supply_one_unambiguous_value(string csv, string expected)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            InternalStringsReader.Read(Encoding.UTF8.GetBytes(csv), "fixture/Internal_Strings.csv"));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Builds_internal_strings_as_a_text_family_of_their_own()
    {
        DaggerfallText merged = DonorInternalText.Build(
            Encoding.UTF8.GetBytes("Key,Value\nStoresA,The %ef\n"),
            "daggerfall-unity/Internal_Strings.csv",
            "en");

        DaggerfallTextRecord internalRecord = Assert.Single(merged.Records, record => record.Key.Kind == DaggerfallTextKind.Internal);
        Assert.Equal("StoresA", internalRecord.Key.Id);
        Assert.Equal("The %ef", Assert.Single(internalRecord.Tokens).Text);
        Assert.Contains(merged.Macros, macro => macro.Symbol == "%ef" && macro.Records == 1);
        Assert.Contains(merged.Sources, source => source.Kind == DaggerfallTextKind.Internal);
    }

    [Fact]
    public void Extracts_the_exact_breton_and_redguard_region_bank_table()
    {
        int[] source = [.. Enumerable.Repeat(0, 62)];
        source[0] = 1;
        source[7] = 1;
        DaggerfallBuildingNameInputs inputs = DonorBuildingNames.Build(
            Encoding.UTF8.GetBytes($"private static readonly byte[] regionRaces = {{ {string.Join(", ", source)} }};"),
            "donor/MapsFile.cs");

        Assert.Equal(62, inputs.RegionNameBanks.Count);
        Assert.Equal(1, inputs.RegionNameBanks[0]);
        Assert.Equal(1, inputs.RegionNameBanks[7]);
    }

    [Fact]
    public void The_base_pack_retains_the_donor_region_names_beside_their_bank_mapping()
    {
        JsonNode section = JsonNode.Parse(File.ReadAllBytes(TestData.PackPayload("daggerfall.base")))!["buildingNames"]!;
        string[] names = [.. section["regionNames"]!.AsArray().Select(name => name!.GetValue<string>())];
        Assert.Equal(62, names.Length);
        Assert.Equal(62, section["regionNameBanks"]!.AsArray().Count);
        Assert.Equal("Alik'r Desert", names[0]);
        Assert.Equal("Daggerfall", names[17]);
        Assert.Equal("Cybiades", names[61]);
    }
}
